# Deployment Architecture — ClinicAgent

> Paste into [mermaid.live](https://mermaid.live) to export as PNG/SVG for slides.
>
> **No longer a capstone.** The **current** topology is the single-box demo/dev deployment
> inherited from the capstone era. The **target** topology is the multi-tenant SaaS shape the
> product is moving toward — shown separately so the two aren't confused.

## Current (single-box demo / dev)

```mermaid
graph TB
    subgraph Internet
        Browser["🌐 Browser / MAUI app"]
        MCPc["MCP client<br/>(Claude Desktop · Inspector)"]
    end

    subgraph AWS["AWS — EC2 t3.small (us-east-1, Amazon Linux 2023)"]
        subgraph Systemd["systemd service: clinic-scheduler"]
            App["ClinicScheduler.Web<br/>ASP.NET Core 10 · Blazor + API<br/>Identity · Orchestrator · Port 8081"]
        end
        subgraph Docker["Docker"]
            DB["PostgreSQL · Port 5432 (internal only)"]
        end
    end

    subgraph Dev["Operator workstation"]
        McpSrv["ClinicScheduler.Mcp<br/>(stdio · runs beside the client)"]
    end

    subgraph GitHub["GitHub — Bradly187/clinic-scheduler"]
        Repo["branch → GitHub Actions (SSH deploy)"]
    end

    Browser -->|"HTTP :8081"| App
    MCPc -->|stdio| McpSrv
    McpSrv -->|"EF Core / Npgsql"| DB
    App -->|"EF Core / Npgsql"| DB
    App -->|HTTPS| Gemini["Google Gemini API"]
    Repo -->|deploy| App

    style AWS fill:#f9f3e8,stroke:#e8a735,stroke-width:2px
    style Systemd fill:#e8f4e8,stroke:#4caf50,stroke-width:1px
    style Docker fill:#e3f2fd,stroke:#2196f3,stroke-width:1px
    style GitHub fill:#f3e8f9,stroke:#9c27b0,stroke-width:1px
```

### Current details
- .NET app runs **natively** under systemd (not containerized); only PostgreSQL runs in Docker.
- Port 5432 is internal to the Docker network — not publicly exposed.
- The MCP server is a **stdio** process that runs next to the MCP client (single-clinic/trusted
  operator); it is not part of the web deploy.
- HTTPS termination deferred (no load balancer / Nginx yet); secrets come from a non-committed
  `.env`.

## Target (multi-tenant SaaS)

```mermaid
graph TB
    subgraph Users
        Clinic1["Clinic A staff/patients"]
        Clinic2["Clinic B staff/patients"]
        Agents["External agents<br/>(scoped tokens)"]
    end

    subgraph Edge["Edge"]
        ALB["Load balancer<br/>+ TLS termination"]
    end

    subgraph App["App tier (horizontally scalable)"]
        Web1["ClinicScheduler.Web #1"]
        Web2["ClinicScheduler.Web #2"]
        MCPh["MCP over HTTP/SSE<br/>per-request tenant from token"]
    end

    subgraph Data["Data tier"]
        PGm[("PostgreSQL<br/>shared schema · ClinicId row isolation")]
        FHIR["FHIR / EHR integrations"]
        OTEL["OpenTelemetry collector"]
    end

    Clinic1 --> ALB
    Clinic2 --> ALB
    Agents --> ALB
    ALB --> Web1
    ALB --> Web2
    ALB --> MCPh
    Web1 --> PGm
    Web2 --> PGm
    MCPh --> PGm
    Web1 -. sync .-> FHIR
    Web1 -.-> OTEL
    Web2 -.-> OTEL

    style Edge fill:#e3f2fd,stroke:#1976d2
    style App fill:#e8f5e9,stroke:#388e3c
    style Data fill:#fce4ec,stroke:#c62828
```

### Target notes (not yet built)
- **Tenant isolation** is enforced by EF global query filters keyed on the `clinic` claim
  (stages 2–3 of [../multi-tenancy-design.md](../multi-tenancy-design.md)); the shared database
  uses row-level `ClinicId` scoping, with the option to promote a noisy/regulated tenant to its
  own database without app changes.
- **MCP for external agents** moves from stdio to HTTP/SSE with OAuth and per-clinic scoped
  tokens; tenant is resolved per request, never as a tool argument.
- App tier scales horizontally behind the load balancer; TLS terminates at the edge.
