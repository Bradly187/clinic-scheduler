using ClinicScheduler.Mcp;
using ClinicSchedulerV2.Data;
using ClinicSchedulerV2.Data.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ─────────────────────────────────────────────────────────────────────────────
// Clinic Scheduler — Model Context Protocol (MCP) server.
//
// Exposes the clinic's scheduling operations as MCP tools so any MCP-capable
// agent (Claude Desktop, the MCP Inspector, an ADK agent, etc.) can look up,
// book and cancel visits. It works against the ClinicSchedulerV2 database, where
// PostgreSQL itself enforces every scheduling rule (constraints, triggers and
// routines in ClinicSchedulerV2/db/postgres). The tools contain no rules: they
// send each change to the database and report what it decided.
//
// The server acts as one staff member (Mcp:ActingEmployeeId). That employee is
// recorded as the booker on new visits and as the actor in the audit log.
//
// Transport: stdio. IMPORTANT: stdout carries the MCP protocol, so every log
// line must go to stderr (configured below).
// ─────────────────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

// MCP stdio uses stdout for the protocol — route all logging to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var connectionString = builder.Configuration.GetConnectionString("ClinicV2")
    ?? throw new InvalidOperationException(
        "Connection string 'ClinicV2' is required: the ClinicSchedulerV2 database. Set the environment " +
        "variable ConnectionStrings__ClinicV2 (e.g. in your MCP client config).");

var actingEmployeeId = builder.Configuration.GetValue<int?>("Mcp:ActingEmployeeId")
    ?? throw new InvalidOperationException(
        "Mcp:ActingEmployeeId is required: the staff member this server books and cancels as. Set the " +
        "environment variable Mcp__ActingEmployeeId (e.g. 10 for the sample data's scheduler).");

builder.Services.AddDbContextFactory<ClinicDbContext>(options => ClinicDbContext.Configure(options, connectionString));
builder.Services.AddSingleton<IActorSource>(new FixedActorSource(Actor.Employee(actingEmployeeId, "MCP server")));
builder.Services.AddScoped<ClinicDb>();
builder.Services.AddScoped<SchedulingService>();
builder.Services.AddScoped<DatabaseCatalog>();

// Register the MCP server: stdio transport + auto-discover [McpServerToolType] classes.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();
await StartupCheck.RunAsync(host.Services, actingEmployeeId);
await host.RunAsync();
