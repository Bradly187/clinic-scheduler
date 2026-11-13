using ClinicSchedulerV2.Data;
using ClinicSchedulerV2.Data.Database;
using ClinicSchedulerV2.Web.Components;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ClinicV2")
    ?? throw new InvalidOperationException(
        "Set ConnectionStrings:ClinicV2 (for example in appsettings.Development.json or the " +
        "ConnectionStrings__ClinicV2 environment variable).");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddDbContextFactory<ClinicDbContext>(options => ClinicDbContext.Configure(options, connectionString));
// One of each per browser session (Blazor circuit): who is acting, and the services that use it
builder.Services.AddScoped<ActingUser>();
builder.Services.AddScoped<ClinicDb>();
builder.Services.AddScoped<SchedulingService>();
builder.Services.AddScoped<DatabaseCatalog>();

var app = builder.Build();

// First run: create the database from db/postgres/*.sql. An existing schema is never touched.
if (app.Configuration.GetValue("Database:AutoCreate", false))
{
    var applied = await DatabaseBootstrapper.EnsureCreatedAsync(
        connectionString, includeSampleData: app.Configuration.GetValue("Database:SeedSampleData", false));
    if (applied.Count > 0)
        app.Logger.LogInformation("Created the database from scripts: {Scripts}", string.Join(", ", applied));
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
