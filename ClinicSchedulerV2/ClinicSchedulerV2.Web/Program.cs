using ClinicSchedulerV2.Data;
using ClinicSchedulerV2.Data.Database;
using ClinicSchedulerV2.Data.Model;
using ClinicSchedulerV2.Web.Components;
using ClinicSchedulerV2.Web.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ClinicV2")
    ?? throw new InvalidOperationException(
        "Set ConnectionStrings:ClinicV2 (for example in appsettings.Development.json or the " +
        "ConnectionStrings__ClinicV2 environment variable).");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddDbContextFactory<ClinicDbContext>(options => ClinicDbContext.Configure(options, connectionString));

// Sign-in: ASP.NET Identity over the app_user table. Roles follow from who the account
// belongs to (ClinicClaimsFactory); the database enforces what each kind of user may write.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingAuthStateProvider>();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(cookie =>
{
    cookie.LoginPath = "/login";
    cookie.AccessDeniedPath = "/access-denied";
    cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
});
builder.Services.AddAuthorization();
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = false; // the database enforces it where an email is given
    })
    .AddEntityFrameworkStores<ClinicDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<ClinicClaimsFactory>()
    .AddDefaultTokenProviders();

// One of each per browser session (Blazor circuit), acting as the signed-in user
builder.Services.AddScoped<IActorSource, SignedInActorSource>();
builder.Services.AddScoped<ClinicDb>();
builder.Services.AddScoped<SchedulingService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<CareService>();
builder.Services.AddScoped<DatabaseCatalog>();

var app = builder.Build();

// Bring the database up to date with db/postgres/*.sql: create it on first run, then apply
// only scripts it has not had yet. Nothing is ever dropped or re-run.
if (app.Configuration.GetValue("Database:AutoCreate", false))
{
    var seed = app.Configuration.GetValue("Database:SeedSampleData", false);
    var applied = await DatabaseBootstrapper.EnsureCreatedAsync(connectionString, includeSampleData: seed);
    if (applied.Count > 0)
        app.Logger.LogInformation("Applied database scripts: {Scripts}", string.Join(", ", applied));

    if (seed && app.Configuration["Database:DemoPassword"] is { Length: > 0 } demoPassword)
        await DemoAccounts.SeedAsync(app.Services, demoPassword, app.Logger);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Sign-in and sign-out post here from plain HTML forms, so the auth cookie is set on a
// normal HTTP response rather than over the interactive connection.
app.MapPost("/account/login", async (
    [FromForm] string userName, [FromForm] string password, [FromForm] string? returnUrl,
    SignInManager<AppUser> signIn) =>
{
    var result = await signIn.PasswordSignInAsync(userName.Trim(), password, isPersistent: false, lockoutOnFailure: true);
    var back = IsLocal(returnUrl) ? returnUrl! : "/";
    if (result.Succeeded) return Results.LocalRedirect(back);
    var error = result.IsLockedOut ? "locked" : "invalid";
    return Results.Redirect($"/login?error={error}&returnUrl={Uri.EscapeDataString(back)}");
});

app.MapPost("/account/logout", async (SignInManager<AppUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.LocalRedirect("/login");
});

app.Run();

static bool IsLocal(string? url) =>
    !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");
