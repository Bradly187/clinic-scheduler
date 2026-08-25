# ClinicAgent — Website / Portfolio Handoff

**Purpose:** ready-to-use copy and facts for updating your personal site. Everything here is
grounded in the actual codebase as of 2026-06-30. Pull what you need; the **Honesty guardrails**
section at the end lists claims to avoid so the site stays defensible.

> Not part of the product docs — this is a personal handoff artifact. Move, edit, or delete freely.

---

## At a glance

- **Name:** ClinicAgent (repo/assemblies still named `ClinicScheduler.*` for legacy reasons)
- **Tagline:** *An agentic healthcare workflow platform for outpatient clinics.*
- **One-liner:** Clinic staff and patients work in natural language; an LLM orchestrator routes
  each request to a specialist sub-agent that runs a constrained tool loop against the clinic's
  real domain logic — scheduling, intake, treatment plans, and more as drop-in workflow packs.
- **Status:** Active development; evolved from a university capstone into a multi-tenant SaaS product.
- **Category tags:** AI agents · Healthcare SaaS · .NET · Multi-tenant · FHIR · MCP

---

## Ready-to-paste copy blocks

### Tweet / one-liner (~25 words)
> ClinicAgent — an agentic healthcare platform where clinic staff and patients work in plain
> language. An LLM orchestrator routes each request to a specialist agent over real domain logic.

### Project-card blurb (~40 words)
> An agentic healthcare workflow platform for outpatient clinics. A multi-agent orchestrator turns
> natural-language requests into safe, rule-checked actions — scheduling, patient intake, treatment
> plans — with multi-tenant isolation, FHIR interoperability, and a standalone MCP server. Built on
> .NET 10 and PostgreSQL.

### Medium description (~110 words)
> ClinicAgent is an agentic healthcare workflow platform that lets clinic staff and patients
> interact in natural language. A coordinator LLM routes each request to a specialist sub-agent
> that runs a bounded tool loop against the clinic's real domain services, so business rules
> (operating hours, conflicts, capacity, two-step confirmation for destructive actions) are
> enforced in code rather than left to the model. Each workflow is a drop-in "pack," and the same
> domain logic is reachable through three front doors: an in-app chat, a REST API, and a standalone
> Model Context Protocol server. The platform is multi-tenant by design, maps its domain to FHIR
> for EHR interoperability, and ships with audit logging, at-rest PHI encryption, and real database
> integration tests. Built on .NET 10, Blazor, EF Core, and PostgreSQL.

### Long description (~240 words)
> ClinicAgent began as a university capstone scheduler and is being rebuilt as a real multi-tenant
> SaaS product for outpatient clinics. Its thesis: the hard part of agentic healthcare isn't any
> single workflow — it's the orchestration engine that routes a natural-language request to the
> right specialist, runs a constrained tool loop, and enforces role- and tenant-scoped access.
>
> A coordinator LLM classifies each request and delegates it to exactly one specialist sub-agent
> (info, scheduling, waitlist, treatment-plan, intake, triage). Each specialist sees only the tools
> relevant to its job, and every tool is a self-registering skill that owns its schema and its C#
> execution — with authorization enforced in code so a misbehaving model can't escalate a read-only
> role into a destructive one. New workflows are added by registering a "pack," never by editing the
> orchestrator.
>
> The same domain logic is exposed through three channels — an in-app Blazor chat, a REST API, and a
> standalone Model Context Protocol server — so the rules stay identical everywhere. Tenant context
> is resolved server-side from the authenticated user (never a model argument), the domain maps to
> FHIR resources for EHR interoperability, and the system ships with automatic audit logging,
> at-rest PHI encryption, optimistic concurrency, JWT + cookie auth, rate limiting, and health
> checks. The test suite includes real-PostgreSQL integration tests via Testcontainers.
>
> Built with .NET 10, ASP.NET Core, Blazor (Server + WebAssembly), .NET MAUI, Entity Framework Core,
> PostgreSQL, MudBlazor, the Google Gemini API, and OpenTelemetry.

---

## Headline highlights (bullet form)

- **Multi-agent orchestration** — a coordinator LLM routes each request to a specialist sub-agent;
  each runs a bounded tool loop with an iteration cap to prevent runaway calls.
- **Pluggable workflow packs** — adding a healthcare workflow means registering a pack, not editing
  the orchestrator (scheduling, treatment-plan, triage, and patient-intake packs today).
- **Safety in code, not prompts** — role-based authorization and a two-step confirmation for
  destructive actions are enforced in C#, so they hold even if the model ignores its instructions.
- **Three front doors, one contract** — in-app chat, REST API, and a standalone MCP server all call
  the same domain services, so business rules are identical across channels.
- **Multi-tenant by design** — clinic = tenant; the tenant is resolved server-side from the
  authenticated principal's claim and never accepted as a tool/API argument.
- **FHIR interoperability** — domain entities map to FHIR resources (Patient, Appointment,
  Practitioner, Location, Encounter) behind a stable internal contract.
- **Production posture from day one** — automatic audit logging, at-rest PHI encryption, optimistic
  concurrency, JWT/cookie auth, rate limiting, health checks, and Testcontainers integration tests.

---

## Technical deep-dive (for an engineering-audience section)

- **Orchestration engine:** `OrchestratorAgentService` + a reusable `AgentService.RunLoopAsync`
  tool-loop primitive shared by the coordinator and every specialist.
- **Extensibility seams:** self-registering `ISkill` tools (no central switch) and drop-in
  `IWorkflowPack`s discovered via DI.
- **Multi-tenancy:** a `Clinic` tenant root + `AppUser.ClinicId`; tenant flows from an Identity
  claim → `ICurrentUserService` → `DbContext`, with EF Core global query filters + insert
  auto-stamping as the enforcement mechanism (staged rollout).
- **Data layer:** EF Core on PostgreSQL with automatic audit interception in `SaveChanges`,
  DataProtection-backed column encryption for PHI, and `xmin` row-version optimistic concurrency.
- **Interop:** an `IFhirSyncService` resource-mapping layer keyed by per-entity `FhirId`.
- **Clean Architecture:** a dependency-inverted core with zero outward project references.

---

## Tech stack (for a "Built with" row)

`.NET 10` · `ASP.NET Core 10` · `Blazor (Server + WebAssembly)` · `Entity Framework
Core 10` · `PostgreSQL` · `MudBlazor` · `Google Gemini API` · `Model Context Protocol` ·
`OpenTelemetry + Jaeger` · `Docker` · `xUnit / FluentAssertions / Moq / FsCheck / Testcontainers` ·
`AWS (EC2)`

---

## By the numbers (verifiable)

- **8 projects** in one Clean-Architecture solution (Core, Infrastructure, Web, Web.Client, Shared,
  Mcp, Core.Tests, Web.Tests).
- **292 automated tests** passing — 157 domain/unit (Core) + 135 web (92 unit + 43 real-PostgreSQL
  integration via Testcontainers).
- **3 delivery channels** over one domain contract (chat, REST API, MCP).
- **5 FHIR resource types** mapped (Patient, Appointment, Practitioner, Location, Encounter).
- **6 specialist agents / 4 workflow packs** in the current roster.

*(Re-verify counts before publishing — they grow as the project does.)*

---

## Status & roadmap (shipped vs. next)

**Shipped**
- Multi-agent orchestrator + workflow-pack and self-registering-skill architecture
- Scheduling, treatment-plan, triage, and patient-intake workflows
- Standalone MCP server exposing scheduling tools
- FHIR mapping for Patient / Appointment / Practitioner / Location / Encounter
- Multi-tenancy foundations: tenant root, tenant identity claim on cookie + JWT, tenant-aware seeding
- Production hardening: audit logging, PHI encryption, concurrency, JWT/cookie auth, rate limiting,
  health checks, HSTS

**Next**
- Finish tenant *enforcement* (per-entity scoping + EF global query filters)
- Harden the MCP server for external multi-tenant agents (HTTP/SSE + OAuth + scoped tokens)
- Additional workflow packs (e.g., referrals, medication refills)
- Managed multi-tenant deployment topology

---

## Skills / keywords (resume & SEO)

LLM agents, multi-agent orchestration, tool/function calling, Model Context Protocol (MCP),
retrieval and RAG-adjacent design, multi-tenant SaaS architecture, FHIR / healthcare
interoperability, .NET 10, ASP.NET Core, Blazor, Entity Framework Core, PostgreSQL, Clean
Architecture, dependency injection, role-based access control, audit logging, data-at-rest
encryption, integration testing with Testcontainers, OpenTelemetry, Docker, AWS.

## Suggested resume bullets

- Designed and built an agentic healthcare platform where a coordinator LLM routes natural-language
  requests to specialist sub-agents over real domain services, enforcing business rules and
  authorization in code rather than in prompts.
- Architected a pluggable workflow-pack + self-registering-skill model so new clinical workflows
  are added without modifying the orchestration engine.
- Implemented a multi-tenant data layer with server-side tenant resolution from auth claims and EF
  Core global query filters, plus audit logging, at-rest PHI encryption, and optimistic concurrency.
- Exposed one domain contract through three channels (Blazor chat, REST API, and a standalone MCP
  server) and a FHIR mapping layer for EHR interoperability.
- Backed the system with 290+ automated tests, including real-PostgreSQL integration tests via
  Testcontainers.

---

## Assets to link / capture

- **Repo:** https://github.com/Bradly187/clinic-scheduler  *(confirm public/private before linking)*
- **Architecture diagrams:** `docs/diagrams/` (Mermaid — system architecture, deployment, database/ER)
- **Vision & architecture write-ups:** `docs/vision.md`, `docs/system-architecture.md`
- **Screenshots to capture for the site:** the AgentChat panel mid-conversation; the appointments
  calendar/grid; an architecture diagram rendered from Mermaid; the Swagger UI.
- **Optional demo:** the EC2 demo box (confirm it's up and seeded before linking).

---

## Honesty guardrails (so the site stays defensible)

- **Say "HIPAA-readiness," not "HIPAA-compliant."** The app has compliance-oriented controls
  (RBAC, audit, encryption) but has not been formally certified or audited.
- **Multi-tenancy is "by design / foundations shipped," not "fully enforced."** Tenant identity
  flows end-to-end, but per-entity query-filter enforcement is a staged, in-progress rollout — don't
  imply hardened isolation between paying customers yet.
- **The MCP server is single-clinic / trusted-operator today.** Don't describe it as an
  externally-exposed multi-tenant API; that hardening is on the roadmap.
- **Deployment is a single demo box, not a scaled SaaS.** The multi-tenant topology in the diagrams
  is a *target*, labeled as such.
- **Attribution:** the project originated as a CSCI-440 (Group 7) **team** capstone. The
  product direction, multi-tenancy/agent architecture, and recent work are yours — phrase team-era
  work as "team capstone" and the product rebuild as your own to stay accurate.
- **"Agentic" ≠ autonomous.** It's a constrained, tool-using, human-in-the-loop assistant with
  code-enforced guardrails — a good thing to say plainly, since it's a credibility signal in health tech.
