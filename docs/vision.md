# ClinicAgent — Product Vision

**Status:** Active · **Date:** 2026-06-30 · **Owner:** Brad Tarver

> **This is no longer a capstone project.** ClinicAgent began life as the CSCI-440 (g7) team
> capstone "ClinicScheduler." It has since outgrown that scope and is being built as a real,
> multi-tenant SaaS product. The academic origin explains some legacy names (`ClinicScheduler.*`
> assemblies, the `csci-440-g7` upstream remote), but the goals, architecture, and roadmap in
> this document are those of a product, not a course deliverable.

---

## 1. What ClinicAgent is

ClinicAgent is an **agentic healthcare workflow platform** for outpatient clinics. A clinic's
staff and patients interact with it in natural language; an LLM orchestrator routes each request
to a specialist sub-agent that runs a constrained tool loop against the clinic's real domain
logic — scheduling, intake, and (next) referrals, refills, prior-auth, and results triage.

It is **not** a chatbot bolted onto a scheduler. The scheduling workflow is simply the first
"workflow pack" built on an orchestration engine designed to host many.

## 2. From capstone to product — what changed

| Dimension | Capstone "ClinicScheduler" | Product "ClinicAgent" |
|---|---|---|
| Scope | One clinic, one workflow (scheduling) | Many clinics, many workflows |
| Tenancy | Single implicit clinic | Multi-tenant; clinic = tenant, isolated server-side |
| AI | A chat feature | The primary interface — orchestrator + specialist packs |
| Extensibility | Hardcoded specialists + `switch` skills | Self-registering `ISkill` + drop-in `IWorkflowPack` |
| Interop | None | FHIR as the shared domain vocabulary; EHR sync layer |
| Surfaces | Web app | Web chat + REST API + standalone MCP server |
| Goal | Pass the course | A sellable SaaS (AWS Activate / marketplace path) |

## 3. Why this is defensible

The hard part of agentic healthcare is not any single workflow — it is the **orchestration
engine** that routes a natural-language request to the right specialist, runs a bounded tool
loop, and enforces role- and tenant-scoped access. ClinicAgent already has that engine, plus
the unglamorous production scaffolding most demos skip:

- Tenant isolation designed at the data layer (not left to the model).
- Audit logging on every mutation, at-rest encryption for PII/PHI, optimistic concurrency.
- Identity + role-based access, JWT for API clients, MCP for external agents.
- Real database integration tests (Testcontainers), not mocks.

## 4. Operating principles

1. **The model never decides what it's allowed to touch.** Tenant and authorization are
   resolved server-side from the authenticated principal and enforced by the data layer. See
   [multi-tenancy-design.md](multi-tenancy-design.md).
2. **One stable internal contract.** Tools and agents talk to ClinicAgent's own domain
   services. FHIR/EHR specifics live behind an adapter so vendor changes don't ripple into
   tool schemas or the model.
3. **Task-oriented tools, not raw CRUD.** Surfaces expose intent ("book the next available
   slot"), enforcing business rules identically across web, API, and MCP.
4. **Every workflow is a drop-in pack.** Adding a workflow means registering specialists +
   skills + domain mapping — never editing the orchestrator. See
   [multi-workflow-strategy.md](multi-workflow-strategy.md).
5. **Production posture from the start.** Audit, encryption, concurrency, and tests are
   table stakes for healthcare, not later polish.

## 5. Roadmap horizon

- **Now:** scheduling + patient-intake packs; multi-tenancy stages 0–1 shipped (tenant root +
  identity claim); standalone MCP server; FHIR sync for Patient/Appointment/Practitioner/Location.
- **Next:** finish tenant enforcement (query filters + per-entity scoping, stages 2–3); harden
  the MCP server for external agents; add the referrals or medication-refill pack.
- **Later:** per-request multi-tenant API/MCP over HTTP, broader FHIR resource coverage, and a
  managed multi-tenant deployment topology (see [diagrams/deployment-architecture.md](diagrams/deployment-architecture.md)).

## 6. Related documents

- [system-architecture.md](system-architecture.md) — how the product is built today.
- [multi-tenancy-design.md](multi-tenancy-design.md) — tenant isolation model + staged plan.
- [multi-workflow-strategy.md](multi-workflow-strategy.md) — workflow-pack expansion strategy.
- [diagrams/](diagrams/) — architecture, deployment, and database/ER diagrams (Mermaid).
