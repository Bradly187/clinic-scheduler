# Architecture Overview

> This is the Clean-Architecture / layered deep-dive. For the product-level picture (orchestrator,
> workflow packs, multi-tenancy, three front doors) see [system-architecture.md](system-architecture.md)
> and [vision.md](vision.md). The project is **no longer a capstone**; `ClinicScheduler.*` names
> are legacy (see vision.md).

## Layered Project Structure

ClinicScheduler follows a Clean Architecture layout. The solution holds nine projects:

```
ClinicScheduler/
├── ClinicScheduler.Core            # Domain entities (incl. Clinic tenant root), enums, interfaces, services
├── ClinicScheduler.Infrastructure  # EF Core DbContext, repositories, migrations, audit, encryption, FHIR sync
├── ClinicScheduler.Shared          # Razor components, pages, layouts (shared UI, incl. AgentChat)
├── ClinicScheduler.Web             # ASP.NET Core host, API, Identity/JWT, orchestrator + packs + skills
├── ClinicScheduler.Web.Client      # Blazor WebAssembly client project
├── ClinicScheduler.Mcp             # Standalone Model Context Protocol server (stdio)
├── ClinicScheduler (MAUI)          # .NET MAUI hybrid app (Android, iOS, macOS, Windows)
├── ClinicScheduler.Core.Tests      # xUnit + FsCheck property-based tests
└── ClinicScheduler.Web.Tests       # xUnit unit tests + Testcontainers integration tests
```

All projects target **net10.0**.

### Dependency Flow

```
Web ──► Shared ──► Core
 │        │          ▲
 │        ▼          │
 │    Infrastructure─┘
 │
 ├──► Web.Client ──► Shared
 │
 ├──► Core (direct reference for services)
 │
 └──► External Services (Gemini API)

Mcp  ──► Core / Infrastructure        (reuses domain logic; separate process)
MAUI ──► Shared ──► Core / Infrastructure
```

Dependencies point inward: `Core` has zero project references, `Infrastructure` depends only on `Core`, and the host projects (`Web`, `Web.Client`, `MAUI`) depend on the inner layers.

### Project Responsibilities

| Project | Responsibility |
|---------|---------------|
| **Core** | Domain entities (`Clinic` tenant root, `Patient`, `Therapist`, `Appointment`, `TreatmentPlan`, `Location`, `Room`, `Encounter`, `WaitlistEntry`, `TherapistShift`, `Notification`, `AuditLog`, etc.), enumerations, interfaces (`IRepository<T>`, `ICurrentUserService`), auth claim types, and domain services (`AppointmentSchedulingService`, `WaitlistService`, `MissedAppointmentService`, `TreatmentPlanScheduleService`). |
| **Infrastructure** | `ClinicDbContext` (EF Core + ASP.NET Core Identity), `Repository<T>`, migrations, automatic audit logging, at-rest PHI encryption, optimistic concurrency, and `IFhirSyncService` + FHIR resource mapper. |
| **Shared** | All Razor pages and components (Home, Appointments, Patients, Therapists, Locations, Rooms, TreatmentPlans, TherapyTypes), `MainLayout`, shared services (`IFormFactor`), `AgentChat` UI, and static assets. |
| **Web** | ASP.NET Core host with `Program.cs` (DI, middleware), REST API controllers (`/api/*`), Identity + JWT auth (with the tenant `clinic` claim), OpenAPI/Swagger, the multi-agent orchestrator + workflow packs + self-registering skills, OpenTelemetry, and background services. |
| **Web.Client** | Blazor WebAssembly entry point. Shares UI components from `Shared` and runs interactively in the browser. |
| **Mcp** | Standalone Model Context Protocol server (stdio) exposing scheduling tools to any MCP client, reusing Core/Infrastructure so business rules are identical to the web app. |
| **MAUI** | .NET MAUI Blazor Hybrid app targeting Android, iOS, macOS, and Windows. Reuses the `Shared` UI layer via `BlazorWebView`. |
| **Core.Tests / Web.Tests** | xUnit suites; Web.Tests adds Testcontainers + `WebApplicationFactory` integration tests against real PostgreSQL. |

## Key Design Patterns

### Repository Pattern

All data access goes through `IRepository<T>`, defined in `Core`:

```csharp
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(T entity, CancellationToken ct = default);
}
```

`Infrastructure` provides `Repository<T>`, a generic EF Core implementation registered as a scoped service.

### Domain Entities

Entities follow a consistent pattern:
- **Private parameterless constructor** for EF Core materialization.
- **Public constructor** with validation for application code.
- **`CreatedAt` / `UpdatedAt` timestamps** managed automatically.
- **Domain methods** for state transitions (e.g., `Appointment.Cancel()`, `TreatmentPlan.Suspend()`), keeping business rules inside the entity.

### Clean Architecture

Business logic lives in `Core` with no dependency on infrastructure or UI concerns. The `AppointmentSchedulingService` validates scheduling rules (time slots, capacity, conflicts) using repository interfaces, not EF Core directly. The outer layers (Web, Infrastructure) provide implementations and wire everything together via dependency injection.

### Automatic Audit Logging

`ClinicDbContext.SaveChangesAsync` intercepts all tracked entity changes (Added, Modified, Deleted) and creates `AuditLog` entries before persisting. This provides an immutable change trail without requiring callers to explicitly log changes.

### AI Integration: orchestrator, workflow packs, and skills

The `AgentChat` interface is served by a multi-agent **orchestrator** (`OrchestratorAgentService`)
powered by the Gemini API. A coordinator routes each request to one specialist sub-agent; the
specialists are contributed by self-registering **workflow packs** (`IWorkflowPack` —
scheduling, treatment-plan, triage, intake), registered via `AddClinicWorkflows()`. Adding a
workflow is "register a pack" — no orchestrator edit.

Each tool is a self-registering **`ISkill`** that owns its own OpenAI-format function schema and
C# execution; `SkillExecutor` is a thin dispatcher (no central switch), and `AddClinicSkills()`
wires them up. **Authorization is enforced in C#**, so a misbehaving model cannot escalate a
read-only role into a destructive one.

Separately, a standalone **Model Context Protocol** server (`ClinicScheduler.Mcp`) exposes the
scheduling tools (`list_therapists`, `get_appointments`, `schedule_appointment`,
`cancel_appointment`) to any MCP client, reusing the same domain logic. See
[system-architecture.md](system-architecture.md) for the full picture.

### Multi-tenancy

Each user belongs to a clinic (`Clinic` is the tenant root; `AppUser.ClinicId` is the source of
truth). The tenant is resolved server-side from the principal's `clinic` claim (emitted on both
the Identity cookie and the API JWT) and read via `ICurrentUserService.TenantId` — never passed
as a caller argument. Data-layer enforcement (global query filters, per-entity `ClinicId`) is
staged in [multi-tenancy-design.md](multi-tenancy-design.md).

### Distributed Tracing (Observability)

OpenTelemetry (OTLP) is integrated into the web host, exporting telemetry data (traces, metrics) to a local Jaeger instance for deep observability into database queries, API requests, and AI interactions.

## Technology Stack

| Category | Technology | Version |
|----------|-----------|---------|
| Runtime | .NET | 10.0 |
| Web Framework | ASP.NET Core | 10.0 |
| UI Framework | Blazor (Server + WebAssembly hybrid) | 10.0 |
| Component Library | MudBlazor | 9.0.0-preview.2 |
| ORM | Entity Framework Core | 10.0 |
| Database | PostgreSQL | 17 (Alpine) |
| Identity | ASP.NET Core Identity | 10.0 |
| AI Integration | Google Gemini API (gemini-2.5-flash) | — |
| Extensibility | Model Context Protocol (MCP) | — |
| Observability | OpenTelemetry + Jaeger | — |
| Mobile | .NET MAUI Blazor Hybrid | 10.0 |
| API Docs | OpenAPI + Swashbuckle (Swagger UI) | 10.1.4 |
| Testing | xUnit, FsCheck, FluentAssertions, Moq, Testcontainers | — |
| Containerization | Docker (multi-stage build) | — |

## Render Modes

The Blazor app uses a hybrid render mode:
- **Interactive Server** — components run on the server with SignalR for UI updates.
- **Interactive WebAssembly** — components can also run in the browser via WebAssembly.
- Both modes are registered in `Program.cs` and the `Web.Client` project provides the WASM entry point.

The MAUI project uses `BlazorWebView` to host the same Shared components natively on mobile and desktop platforms.
