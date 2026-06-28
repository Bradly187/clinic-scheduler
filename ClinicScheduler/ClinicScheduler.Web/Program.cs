using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ClinicScheduler.Web;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Web.Components;
using ClinicScheduler.Web.Services;
using ClinicScheduler.Shared.Services;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Configuration;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Infrastructure.Ehr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

// Bootstrap logger captures startup failures before full Serilog is wired up
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Clinic Scheduler");

    var builder = WebApplication.CreateBuilder(args);

    // Serilog: read full config from appsettings then replace the bootstrap logger
    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .Enrich.WithMachineName()
           .Enrich.WithThreadId()
           .WriteTo.File(
               new CompactJsonFormatter(),
               path: "logs/clinic-.log",
               rollingInterval: RollingInterval.Day,
               retainedFileCountLimit: 14,
               fileSizeLimitBytes: 50_000_000,
               rollOnFileSizeLimit: true);

        // WriteTo.Console(ITextFormatter) throws when formatter is null;
        // use the overload with no formatter for human-readable dev output.
        if (ctx.HostingEnvironment.IsProduction())
            cfg.WriteTo.Console(new CompactJsonFormatter());
        else
            cfg.WriteTo.Console();
    });

    // Behind the ALB, derive scheme/client IP from X-Forwarded-* headers so HTTPS
    // detection, secure cookies, and rate-limit partitioning see real values.
    // The task's security group only admits traffic from the ALB, so the proxy is trusted.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // Register the Database Context
    var defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(defaultConnectionString) && !builder.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException(
            "The connection string 'DefaultConnection' is missing or empty. Please configure a valid connection string in appsettings.json or environment configuration.");
    }

    builder.Services.AddDbContextFactory<ClinicDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
    // Also register ClinicDbContext directly (scoped) for controllers and services that need it
    builder.Services.AddDbContext<ClinicDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")),
        ServiceLifetime.Scoped,
        ServiceLifetime.Singleton);

    // Register the repositories
    builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

    // Audit attribution: resolve the acting user from the ambient HTTP context
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IAppointmentEventService, AppointmentEventService>();
    builder.Services.AddSingleton<ICurrentUserService, HttpContextCurrentUserService>();
    builder.Services.AddScoped<IAuditLogger, AuditLogger>();

    // Register business logic services
    builder.Services.AddScoped<AppointmentSchedulingService>();
    builder.Services.AddScoped<MissedAppointmentService>();
    builder.Services.AddScoped<TreatmentPlanScheduleService>();
    builder.Services.AddScoped<WaitlistService>();
    builder.Services.AddScoped<WaitlistFulfillmentNotifier>();
    builder.Services.AddScoped<AppointmentNotificationService>();
    builder.Services.AddSingleton<ClinicScheduler.Web.Services.Skills.ISkillRegistry, ClinicScheduler.Web.Services.Skills.SkillRegistry>();
    builder.Services.AddScoped<ClinicScheduler.Web.Services.Skills.ISkillExecutor, ClinicScheduler.Web.Services.Skills.SkillExecutor>();
    // AgentService is the shared LLM tool-loop primitive (typed HttpClient for Gemini).
    builder.Services.AddHttpClient<AgentService>();
    // The chat is served by the multi-agent orchestrator: a coordinator that routes each
    // request to a specialist sub-agent (Info / Scheduling / Triage), each running its own
    // tool loop via AgentService.RunLoopAsync.
    builder.Services.AddScoped<ClinicScheduler.Shared.Services.IAgentService, OrchestratorAgentService>();

    // OpenTelemetry Setup
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(
            serviceName: Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "ClinicScheduler.Web",
            serviceVersion: "1.0.0"))
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddSource("ClinicScheduler.AgentService")
                .AddSource("ClinicScheduler.OrchestratorAgentService")
                .AddSource("ClinicScheduler.BusinessLogic");
            
            var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                tracing.AddOtlpExporter(opt =>
                {
                    opt.Endpoint = new Uri(otlpEndpoint);
                });
            }
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("ClinicScheduler.BusinessLogic")
                .AddPrometheusExporter();
        });

    // Outbound email (no-op until the Email section is configured)
    builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
    builder.Services.AddSingleton<IClinicEmailSender, SmtpEmailSender>();

    // Outbound SMS via Twilio (no-op until the Sms section is configured)
    builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection(SmsOptions.SectionName));
    builder.Services.AddHttpClient("twilio");
    builder.Services.AddSingleton<ISmsSender, TwilioSmsSender>();

    // FHIR Integration
    builder.Services.Configure<FhirSettings>(builder.Configuration.GetSection("FhirSettings"));
    builder.Services.AddScoped<IFhirSyncService, FhirSyncService>();

    // Background services
    builder.Services.AddHostedService<AppointmentReminderService>();
    builder.Services.AddHostedService<WaitlistProcessingService>();

    // Health check endpoint — used by load balancers and monitoring tools.
    // Npgsql check only registered when a connection string is available; in the
    // Testing environment the fixture overrides the DbContext but appsettings.json
    // has an empty string, so we skip the DB check rather than throwing at startup.
    var healthChecks = builder.Services.AddHealthChecks();
    if (!string.IsNullOrWhiteSpace(defaultConnectionString))
    {
        healthChecks.AddNpgSql(defaultConnectionString, name: "postgres", tags: ["db", "ready"]);
    }

    // ASP.NET Core Identity
    builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
    {
        // Baseline: all environments
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireUppercase = true;

        // Elevated: production only
        if (builder.Environment.IsProduction())
        {
            options.Password.RequiredLength = 10;
        }

        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ClinicDbContext>()
    .AddDefaultTokenProviders();

    // Security:RequireHttps is enabled by the deployment when TLS terminates at the ALB
    // (set automatically by Terraform when an ACM certificate is configured)
    var requireHttps = builder.Configuration.GetValue<bool>("Security:RequireHttps");

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = requireHttps
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
    });

    builder.Services.AddDataProtection();

    // JWT Bearer — for external REST API clients; issues via POST /api/auth/token
    // Set Jwt__SigningKey (env var) in production; dev falls back to a hard-coded insecure key.
    var jwtIssuer   = builder.Configuration["Jwt:Issuer"]   ?? "ClinicAgent";
    var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "ClinicAgent";
    var jwtKey      = builder.Configuration["Jwt:SigningKey"]
        ?? (builder.Environment.IsProduction()
            ? throw new InvalidOperationException("Jwt:SigningKey must be set via the Jwt__SigningKey environment variable in production.")
            : "dev-only-signing-key-not-for-production-use-changeme");

    // Route authentication to JWT when an Authorization: Bearer header is present,
    // otherwise fall back to the Identity cookie scheme.
    // WebAppFixture uses PostConfigure to override DefaultAuthenticateScheme → "TestScheme"
    // in CI, which plain AddAuthorization (no explicit schemes) correctly picks up.
    builder.Services.AddAuthentication(o =>
    {
        o.DefaultAuthenticateScheme = "CookieOrJwt";
        o.DefaultChallengeScheme    = IdentityConstants.ApplicationScheme;
        o.DefaultSignInScheme       = IdentityConstants.ApplicationScheme;
        o.DefaultSignOutScheme      = IdentityConstants.ApplicationScheme;
        o.DefaultForbidScheme       = IdentityConstants.ApplicationScheme;
    })
    .AddPolicyScheme("CookieOrJwt", "Cookie or Bearer", o =>
    {
        o.ForwardDefaultSelector = ctx =>
        {
            var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
            return !string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : IdentityConstants.ApplicationScheme;
        };
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = jwtIssuer,
            ValidateAudience         = true,
            ValidAudience            = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(30),
        };
    });

    builder.Services.AddAuthorization();

    builder.Services.AddCascadingAuthenticationState();

    // HSTS: one year, subdomains included (only sent on HTTPS responses outside development)
    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(365);
        options.IncludeSubDomains = true;
    });

    // Rate limiting: throttle credential-stuffing on the login endpoints per client IP
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
    });

    // CORS — allow same-origin in production; configure AllowedOrigins in appsettings for external clients
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AppPolicy", policy =>
        {
            if (builder.Environment.IsDevelopment())
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            }
            else
            {
                var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
                if (origins.Length > 0)
                    policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
            }
        });
    });

    // Add API Controllers
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

    builder.Services.AddOpenApi(options =>
    {
        options.AddSchemaTransformer((schema, context, CancellationToken) =>
        {
            if (context.JsonTypeInfo.Type.IsEnum)
            {
                schema.Type = JsonSchemaType.String;
                schema.Enum = context.JsonTypeInfo.Type
                    .GetEnumNames()
                    .Select(name => JsonValue.Create(name))
                    .Cast<JsonNode>()
                    .ToArray();
            }

            return Task.CompletedTask;
        });
        options.AddDocumentTransformer((document, AppContext, CancellationToken) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "ClinicAgent API",
                Version = "v1",
                Description = "REST API for ClinicAgent. Authenticate via POST /api/auth/token to receive a Bearer token.",
            };

            return Task.CompletedTask;
        });
    });

    // Add services to the container.
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents(options =>
        {
            // Detailed errors only in development — never expose stack traces in production
            options.DetailedErrors = builder.Environment.IsDevelopment();
            // Keep circuits alive longer to avoid disconnects during normal use
            options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
        })
        .AddInteractiveWebAssemblyComponents();

    builder.Services.AddScoped(sp => 
    {
        var navManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        return new HttpClient { BaseAddress = new Uri(navManager.BaseUri) };
    });

    builder.Services.AddMudServices();
    var app = builder.Build();

    // Must run first so downstream middleware sees the original scheme and client IP
    app.UseForwardedHeaders();

    // Structured HTTP request logging — replaces the default ASP.NET access log
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
        opts.GetLevel = (ctx, elapsed, ex) =>
        {
            if (ex is not null || ctx.Response.StatusCode >= 500) return LogEventLevel.Error;
            if (ctx.Response.StatusCode >= 400) return LogEventLevel.Warning;
            return LogEventLevel.Information;
        };
        opts.EnrichDiagnosticContext = (diag, http) =>
        {
            diag.Set("RequestHost", http.Request.Host.Value ?? string.Empty);
            diag.Set("UserName", http.User.Identity?.Name ?? "anonymous");
        };
    });

    // Baseline security headers on every response
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        await next();
    });

    // Auto-apply EF migrations on startup (safe to run repeatedly; no-ops when up-to-date)
    // In development, handle database errors gracefully to allow testing without a database
    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            try
            {
                db.Database.Migrate();
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Database migration skipped: {Message}", ex.Message);
            }

            try
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var adminPassword = app.Configuration["SeedAdmin:Password"]
                    ?? throw new InvalidOperationException("SeedAdmin:Password is not configured.");
                await DatabaseSeeder.SeedAsync(db, userManager, roleManager, adminPassword, isDevelopment: true);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Database seed skipped: {Message}", ex.Message);
            }
        }
    }
    else
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var adminPassword = app.Configuration["SeedAdmin:Password"]
                ?? throw new InvalidOperationException(
                    "SeedAdmin:Password must be set via environment variable (SeedAdmin__Password) in production.");
            db.Database.Migrate();
            await DatabaseSeeder.SeedAsync(db, userManager, roleManager, adminPassword, isDevelopment: false);
        }
    }

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseWebAssemblyDebugging();
    }
    else
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    // API docs — available in all environments so external integrators can browse the spec
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "ClinicAgent API v1");
        options.RoutePrefix = "swagger";
    });

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

    // HTTPS termination is handled by the load balancer in production; skip redirect in container
    if (!app.Environment.IsProduction())
        app.UseHttpsRedirection();

    app.UseStaticFiles();
    // Note: UseBlazorFrameworkFiles() must NOT be used here — it's for the legacy
    // hosted-WASM model and dead-ends every /_framework/* request its branched
    // pipeline can't find (including blazor.web.js). In the unified Blazor Web App
    // model, framework assets are served by static web assets / MapStaticAssets.
    app.UseCors("AppPolicy");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseAntiforgery();

    app.MapStaticAssets().AllowAnonymous();

    // Health checks — unauthenticated, safe for load balancer probes.
    // /health/live answers without touching dependencies; /health includes the DB check.
    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    }).AllowAnonymous();
    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapPrometheusScrapingEndpoint();


    // Map API endpoints
    app.MapControllers();

    app.MapPost("/api/agent/chat", async (JsonArray chatHistory, ClinicScheduler.Shared.Services.IAgentService agentService) =>
    {
        var response = await agentService.ProcessMessageAsync(chatHistory);
        return Results.Ok(new { response });
    }).DisableAntiforgery().RequireAuthorization();

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(
            typeof(ClinicScheduler.Shared._Imports).Assembly,
            typeof(ClinicScheduler.Web.Client._Imports).Assembly);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException and not OperationCanceledException)
{
    Log.Fatal(ex, "Clinic Scheduler terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
