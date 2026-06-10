using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClinicScheduler.Web;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Web.Components;
using ClinicScheduler.Web.Services;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Structured logging: human-readable console in development, compact JSON in
// production so CloudWatch can index fields. A "Serilog" config section, when
// present, overrides these defaults.
builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext();

    if (context.HostingEnvironment.IsProduction())
        loggerConfiguration.WriteTo.Console(new CompactJsonFormatter());
    else
        loggerConfiguration.WriteTo.Console();
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
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register the repositories
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

// Audit attribution: resolve the acting user from the ambient HTTP context
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICurrentUserService, HttpContextCurrentUserService>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();

// Register business logic services
builder.Services.AddScoped<AppointmentSchedulingService>();
builder.Services.AddScoped<MissedAppointmentService>();
builder.Services.AddScoped<AppointmentNotificationService>();

// Outbound email (no-op until the Email section is configured)
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddSingleton<IClinicEmailSender, SmtpEmailSender>();

// Background services
builder.Services.AddHostedService<AppointmentReminderService>();

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

builder.Services.AddAuthorization();

builder.Services.AddCascadingAuthenticationState();

// Health checks: /health (includes DB connectivity) is the ALB target
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// HSTS: one year, subdomains included (only sent on HTTPS responses outside development)
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

// Rate limiting: throttle credential-stuffing on the login endpoint per client IP
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
            Title = "Clinic Scheduler API",
            Version = "v1"
        };
        
        return Task.CompletedTask;
    });
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = true;
    })
    .AddInteractiveWebAssemblyComponents();

// Keep circuits alive longer to avoid disconnects during normal use
builder.Services.AddServerSideBlazor(options =>
{
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
    options.DetailedErrors = true;
});

builder.Services.AddMudServices();
var app = builder.Build();

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
// Must run first so downstream middleware sees the original scheme and client IP
app.UseForwardedHeaders();

app.UseSerilogRequestLogging();

// Baseline security headers on every response
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();

    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "ClinicScheduler API v1");
        options.RoutePrefix = "swagger";
    });
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// HTTPS termination is handled by the load balancer in production; skip redirect in container
if (!app.Environment.IsProduction())
    app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseBlazorFrameworkFiles();
app.UseCors("AppPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();

// Health endpoints: /health/live answers without touching dependencies;
// /health includes the database check and is the ALB health-check target
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

// Map API endpoints
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(
        typeof(ClinicScheduler.Shared._Imports).Assembly,
        typeof(ClinicScheduler.Web.Client._Imports).Assembly);

app.Run();