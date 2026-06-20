using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ─────────────────────────────────────────────────────────────────────────────
// Clinic Scheduler — Model Context Protocol (MCP) server.
//
// Exposes the clinic's scheduling operations as MCP tools so ANY MCP-capable
// agent (Claude Desktop, the MCP Inspector, an ADK agent, etc.) can read, book,
// and cancel appointments. The tools reuse the same domain logic the web app
// uses — ClinicScheduler.Core's AppointmentSchedulingService and the EF Core
// repositories in ClinicScheduler.Infrastructure — so business rules (operating
// hours, slot alignment, conflict + capacity checks) are enforced identically.
//
// Transport: stdio. IMPORTANT: stdout carries the MCP protocol, so every log
// line must go to stderr (configured below).
// ─────────────────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

// MCP stdio uses stdout for the protocol — route all logging to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is required. Set the environment variable " +
        "ConnectionStrings__DefaultConnection (e.g. in your MCP client config) or add it to " +
        "appsettings.json next to the server executable.");

// Reuse the app's data layer and business logic.
builder.Services.AddDbContext<ClinicDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<AppointmentSchedulingService>();

// Register the MCP server: stdio transport + auto-discover [McpServerToolType] classes.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
