# ClinicScheduler.Mcp — Model Context Protocol server

A standalone **MCP server** that exposes the clinic's scheduling operations as tools to any
MCP-capable agent — **Claude Desktop**, the **MCP Inspector**, an **ADK** agent, etc.

It works against the **ClinicSchedulerV2** database, where PostgreSQL enforces every scheduling
rule itself (constraints, triggers and routines in `ClinicSchedulerV2/db/postgres`). The tools hold
no rules of their own: they resolve names, send the change to the database, and hand its answer
back to the agent word for word. A booking from an agent is therefore checked exactly like one
made in the web app: weekdays 8 AM–5 PM on the half hour, no provider or patient double booking,
room type and capacity, the provider's qualifications and work locations, and the 12-patient limit.

## Tools exposed

| Tool | Purpose |
|---|---|
| `list_providers` | Providers with credential, the services they are qualified for, and where they work. |
| `list_services` | Bookable services with usual length, room type and fee; the clinic's locations. |
| `get_appointments` | A patient's upcoming scheduled visits, by name, with appointment IDs. |
| `schedule_appointment` | Book a visit: patient, provider, service, location, date, time (optional length). The database picks the first suitable room. |
| `cancel_appointment` | Cancel by ID on the clinic's behalf — **two-step**: previews first, cancels only when called again with `confirmed=true`. The patient and provider are notified by the database. |
| `list_pending_requests` | Appointment and cancellation requests waiting for staff review. |

> **Security note:** these are staff operations acting on any patient by name. Expose this server
> only to trusted operators. The destructive `cancel_appointment` tool enforces confirmation **in
> code**, not just in its prompt, so an agent cannot cancel without an explicit second call.

## Configuration

| Setting | Environment variable | Value |
|---|---|---|
| Database | `ConnectionStrings__ClinicV2` | The ClinicSchedulerV2 database, e.g. `Host=localhost;Database=clinic_scheduler_v2;Username=postgres;Password=...` |
| Acting staff member | `Mcp__ActingEmployeeId` | The employee the server books and cancels as; must be an active staff member. In the sample data, `10` is Kevin Brooks, scheduler. |

The acting employee is recorded as the booker of new visits and as the actor in the audit log.
The server checks both settings at startup and refuses to run if the database is missing the
V2 scripts (run the ClinicSchedulerV2 web app once with `Database:AutoCreate=true`) or the
employee is not active staff.

## Run it

```bash
# from the repo root
dotnet run --project ClinicScheduler/ClinicScheduler.Mcp
```

The server speaks MCP over **stdio** (stdout = protocol, stderr = logs).

### Wire into Claude Desktop

Add to `claude_desktop_config.json` (Settings → Developer → Edit Config):

```json
{
  "mcpServers": {
    "clinic-scheduler": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\clinic-scheduler\\ClinicScheduler\\ClinicScheduler.Mcp"],
      "env": {
        "ConnectionStrings__ClinicV2": "Host=localhost;Database=clinic_scheduler_v2;Username=postgres;Password=YOUR_PASSWORD",
        "Mcp__ActingEmployeeId": "10"
      }
    }
  }
}
```

Restart Claude Desktop; the clinic tools appear under the tools menu. Try:
*"Which providers do infusions at Clear Lake?"*, then *"Book Susan Brooks for an infusion with Luis Herrera at Clear Lake next Monday at 1pm."*

### Inspect / test with the MCP Inspector

```bash
npx @modelcontextprotocol/inspector dotnet run --project ClinicScheduler/ClinicScheduler.Mcp
```

## How it's built

- `Program.cs` — generic host; registers the V2 data layer (`ClinicDbContext`, `ClinicDb`,
  `SchedulingService`, `DatabaseCatalog`) acting as the configured staff member, then
  `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`.
- `StartupCheck.cs` — refuses to start against the wrong database or a non-staff employee.
- `ClinicTools.cs` — the `[McpServerToolType]` class; each `[McpServerTool]` method receives its
  services via DI and its arguments from the calling agent. Bookings call `sp_book_appointment`.
- Tested in `ClinicSchedulerV2/ClinicSchedulerV2.Tests/McpToolTests.cs`, against a real database.
