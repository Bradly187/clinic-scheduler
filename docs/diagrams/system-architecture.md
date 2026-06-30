# System Architecture — ClinicAgent

> Paste into [mermaid.live](https://mermaid.live) to export as PNG/SVG for slides.
> Prose walkthrough: [../system-architecture.md](../system-architecture.md).
>
> **No longer a capstone** — multi-tenant agentic healthcare platform. Assemblies keep the
> legacy `ClinicScheduler.*` names.

```mermaid
graph TB
    subgraph Channels ["Front doors — one domain contract underneath"]
        Chat["<b>Blazor Chat UI</b><br/>Shared · AgentChat<br/>(Server + WASM)"]
        API["<b>REST API</b><br/>/api/* controllers<br/>JWT bearer"]
        MCPc["<b>MCP clients</b><br/>Claude Desktop · Inspector<br/>external agents"]
        MAUI["<b>MAUI app</b><br/>Blazor Hybrid<br/>(mobile/desktop)"]
    end

    subgraph Web ["ClinicScheduler.Web — ASP.NET Core host (net10)"]
        Auth["<b>Identity + Auth</b><br/>cookie + JWT · roles<br/><b>clinic tenant claim</b>"]
        Orch["<b>OrchestratorAgentService</b><br/>coordinator LLM<br/>routes via route_to_*"]
        Packs["<b>IWorkflowPack registry</b><br/>Scheduling · Intake<br/>(next: Referrals · Refills)"]
        Skills["<b>ISkill registry</b><br/>self-registering tools"]
        AgentP["<b>AgentService</b><br/>bounded tool loop"]
        Ctrl["<b>API Controllers</b>"]
    end

    subgraph Mcp ["ClinicScheduler.Mcp"]
        Tools["<b>ClinicTools</b> (stdio)<br/>list_therapists · get_appointments<br/>schedule · cancel"]
    end

    subgraph Core ["ClinicScheduler.Core — domain (net9, zero deps)"]
        Ent["<b>Entities + enums</b><br/>Clinic(tenant) · Patient · Therapist<br/>Appointment · TreatmentPlan · Encounter<br/>Waitlist · Shift · Notification"]
        Svc["<b>Domain services</b><br/>AppointmentScheduling · Waitlist<br/>MissedAppointment · TreatmentPlanSchedule"]
        If["<b>Interfaces</b><br/>IRepository&lt;T&gt; · ICurrentUserService · ISkill"]
    end

    subgraph Infra ["ClinicScheduler.Infrastructure (net9)"]
        Db["<b>ClinicDbContext (EF Core 10)</b><br/>audit logging · at-rest encryption<br/>optimistic concurrency (xmin)"]
        Repo["Repository&lt;T&gt;"]
        Fhir["<b>IFhirSyncService</b><br/>FHIR resource mapper"]
    end

    subgraph Ext ["Storage & external services"]
        PG[("PostgreSQL")]
        Gemini["Google Gemini API (LLM)"]
        EHR["FHIR / EHR server"]
        Jaeger["Jaeger (OpenTelemetry)"]
    end

    subgraph Tests ["Tests"]
        T["Core.Tests (xUnit)<br/>Web.Tests (+ Testcontainers)"]
    end

    Chat --> Orch
    MAUI --> Chat
    API --> Ctrl --> Svc
    MCPc --> Tools
    Orch --> Packs --> Skills --> AgentP --> Gemini
    Skills --> Svc
    Tools --> Svc
    Tools --> Db
    Auth -. "tenant + roles" .-> Svc
    Svc --> If
    Repo -. implements .-> If
    Svc --> Db
    Db --> Repo --> PG
    Db -. sync .-> Fhir --> EHR
    Web -.-> Jaeger
    T -.-> Web

    style Channels fill:#e3f2fd,stroke:#1976d2,stroke-width:1px
    style Web fill:#e8f5e9,stroke:#388e3c,stroke-width:1px
    style Mcp fill:#ede7f6,stroke:#5e35b1,stroke-width:1px
    style Core fill:#fff3e0,stroke:#f57c00,stroke-width:1px
    style Infra fill:#fce4ec,stroke:#c62828,stroke-width:1px
    style Ext fill:#e0f7fa,stroke:#00838f,stroke-width:1px
    style Tests fill:#f3e5f5,stroke:#7b1fa2,stroke-width:1px,stroke-dasharray: 5 5
```

## How to read it

- **Three front doors, one contract.** Chat (orchestrator), REST API (controllers), and the
  standalone MCP server all invoke the **same Core domain services**, so business rules
  (operating hours, slot alignment, conflicts, capacity, two-step cancel) are enforced identically.
- **Dependencies point inward.** `Core` has zero project references; `Infrastructure` depends only
  on `Core`; hosts depend on the inner layers. `Repository<T>` and `ICurrentUserService` are
  Core-defined contracts fulfilled by outer layers (dependency inversion).
- **AI is pluggable.** The orchestrator routes to specialists supplied by registered
  `IWorkflowPack`s; each tool is a self-registering `ISkill`. Adding a workflow registers a pack —
  no orchestrator edits.
- **Tenant is ambient, not an argument.** `Identity` resolves the clinic from the principal's
  `clinic` claim and feeds it to the data layer (dotted line) — never passed by a caller.

## Key design decisions

| Decision | Rationale |
|---|---|
| Business rules in `Core`, not controllers/tools | Channels stay thin; one rule set, testable without HTTP |
| `IRepository<T>` / `ICurrentUserService` in Core | Dependency inversion — Core defines contracts, Infrastructure fulfills |
| Workflow packs + self-registering skills | New workflows are drop-in; no central switch or orchestrator edits |
| Tenant from identity claim, enforced at data layer | The model never decides which clinic it can touch |
| FHIR behind an adapter | EHR/vendor changes don't ripple into tool or agent schemas |
| Testcontainers for integration tests | Real PostgreSQL catches SQL/EF issues mocks miss |
