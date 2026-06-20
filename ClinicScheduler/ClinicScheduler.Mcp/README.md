# ClinicScheduler.Mcp — Model Context Protocol server

A standalone **MCP server** that exposes the clinic's scheduling operations as tools to any
MCP-capable agent — **Claude Desktop**, the **MCP Inspector**, an **ADK** agent, etc.

It reuses the application's own domain logic: `ClinicScheduler.Core`'s
`AppointmentSchedulingService` and the EF Core repositories in `ClinicScheduler.Infrastructure`.
That means every booking goes through the **same business rules** the web app enforces —
operating hours, slot alignment, therapist/room/patient conflict detection, and per-location
daily capacity.

## Tools exposed

| Tool | Purpose |
|---|---|
| `list_therapists` | List therapists and specialties (to resolve a name before booking). |
| `get_appointments` | Look up a patient's upcoming scheduled appointments by name. |
| `schedule_appointment` | Book an appointment (patient, therapist, date, time, optional therapy type). |
| `cancel_appointment` | Cancel by ID — **two-step**: previews first, only cancels when called again with `confirmed=true`. |

> **Security note:** these are staff/admin-style operations (they act by patient name), mirroring
> how the web app gates `cancel_any_appointment` behind Staff/Admin roles. Expose this server only
> to trusted operators. The destructive `cancel_appointment` tool enforces confirmation **in code**,
> not just in its prompt — so an agent cannot cancel without an explicit second, confirmed call.

## Configuration

The server needs a PostgreSQL connection string (the same database the web app uses). Provide it
via the `ConnectionStrings__DefaultConnection` environment variable:

```
ConnectionStrings__DefaultConnection=Host=localhost;Database=clinic_scheduler;Username=postgres;Password=postgres
```

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
      "args": [
        "run",
        "--project",
        "C:\\painpi\\clinic-scheduler\\ClinicScheduler\\ClinicScheduler.Mcp"
      ],
      "env": {
        "ConnectionStrings__DefaultConnection": "Host=localhost;Database=clinic_scheduler;Username=postgres;Password=postgres"
      }
    }
  }
}
```

Restart Claude Desktop; the four clinic tools appear under the 🔌 tools menu. Try:
*"List the clinic's therapists,"* then *"Book Jane Doe with Dr. Smith next Tuesday at 2pm."*

### Inspect / test with the MCP Inspector

```bash
npx @modelcontextprotocol/inspector dotnet run --project ClinicScheduler/ClinicScheduler.Mcp
```

## How it's built

- `Program.cs` — generic host; registers `ClinicDbContext` (Npgsql), the generic `Repository<>`,
  and `AppointmentSchedulingService`, then `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`.
- `ClinicTools.cs` — the `[McpServerToolType]` class; each `[McpServerTool]` method receives its
  dependencies (DbContext, scheduling service) via DI and its arguments from the calling agent.
- SDK: [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) (official C# SDK).
