# ClinicAgent — Multi-Workflow Expansion Strategy

**Status:** Proposal · **Date:** 2026-06-29 · **Owner:** Brad Tarver

> ClinicAgent today is an agentic clinic *scheduler*. This document is the strategy for
> turning it into an agentic *healthcare workflow platform* — many workflows, one
> orchestration engine, one interoperable domain.

---

## 1. Thesis

The hard part of an agentic healthcare product is not any single workflow — it is the
**orchestration engine** that routes a natural-language request to the right specialist,
runs a constrained tool loop, and enforces role-based access. **ClinicAgent already has
that engine.** Scheduling is simply the first workflow built on it.

The expansion strategy is therefore *not* "build more apps." It is: **generalize the one
seam we already have so that each new healthcare workflow is a drop-in "pack," and adopt
FHIR as the shared domain vocabulary so every workflow is interoperable by construction.**

---

## 2. Where we are today

| Layer | Implementation | Assessment |
|---|---|---|
| **Orchestration** | `OrchestratorAgentService` — a Coordinator LLM routes to one of 5 specialist sub-agents via `route_to_*` tools | **Strong.** This is already a workflow router. Comment in code: *"Add a specialist here to give the clinic a new workflow."* |
| **Tool loop** | `AgentService.RunLoopAsync` — reusable primitive powering both single- and multi-agent flows | **Strong.** Clean, reusable, cap on tool rounds. |
| **Skills (tools)** | 12 `SKILL.md` folders + a hand-written `switch` in `SkillExecutor` | **Adequate now, won't scale.** Fine at 12 tools; a wall at 60. |
| **Domain** | `ClinicScheduler.Core/Entities` — Appointment, Therapist, TreatmentPlan, Waitlist… | **Scheduling-only.** No vocabulary for intake, orders, meds, coverage. |
| **Interop** | `IFhirSyncService` — syncs Patient + Appointment to a FHIR server | **Thin but the right idea.** Only 2 of ~dozen relevant resources. |
| **Front doors** | Blazor chat (`IAgentService`) **and** a standalone MCP server (`ClinicTools`) | **Strong.** Two channels into the same domain logic. |

### The three coupling points that block expansion

1. **Domain framing is hardcoded.** Every specialist `SystemPrompt` and the coordinator
   prompt say "pain-management clinic." Specialists are a `static readonly` array — they
   are *code*, not *data*. A new workflow can't be added without editing this file.
2. **Skills are centrally switched.** `SkillExecutor.GetToolSchema`/`ExecuteAsync` is one
   growing `switch`. Every new tool edits a shared file — merge conflicts and a single
   point of bloat.
3. **The domain is scheduling-shaped.** New workflows need new nouns (Encounter, Order,
   Medication, Coverage). Without a shared vocabulary, each workflow reinvents its data
   model and the FHIR mapping is bespoke each time.

None of these are flaws — they are exactly the right shape for a v1. They are the seams
to generalize for v2.

---

## 3. Target architecture: the Workflow Pack

A **Workflow Pack** is a self-contained, registrable unit that bundles everything one
healthcare workflow needs:

```
WorkflowPack
  ├─ Specialists[]      one or more SpecialistAgent definitions (prompt + allowed skills)
  ├─ Skills[]           self-registering ISkill tools (schema + handler)
  ├─ Domain / FHIR      entities + their FHIR resource mapping
  └─ Roles / Policies   who may invoke this workflow
```

The Coordinator routes across the **union of all packs' specialists**. Adding a workflow
becomes "register a pack," not "edit four files." The orchestration engine, tool loop,
auth, audit, and MCP/REST exposure are all inherited for free.

This is the single highest-leverage idea in this document: **the cost of the Nth workflow
should approach zero.** That is the platform story — and the AWS Activate / SaaS story.

---

## 4. FHIR as the shared domain vocabulary

Adopting FHIR resource models as the internal domain (not just the sync target) makes
every new workflow interoperable the moment it ships. The mapping is natural:

| Workflow | Primary FHIR resource(s) | What the agent does |
|---|---|---|
| Scheduling *(today)* | `Appointment`, `Slot`, `Schedule` | book / cancel / reschedule |
| Patient intake & registration | `Patient`, `Encounter`, `Coverage` | register, verify demographics, start a visit |
| Referral management | `ServiceRequest`, `Practitioner` | create/track referrals, route to specialty |
| Medication refills | `MedicationRequest` | request renewals, surface refill status |
| Prior auth / eligibility | `Coverage`, `CoverageEligibilityRequest` | check eligibility, initiate prior-auth |
| Lab / imaging results triage | `Observation`, `DiagnosticReport` | summarize results, flag abnormals, route follow-up |
| Care-plan adherence | `CarePlan`, `Goal` | follow up on adherence, nudge, re-book |

**Pitch value:** "Built on FHIR R4" is a credibility marker for any healthcare buyer and
de-risks integration with Epic/Cerner/Athena-style EHRs. It is a stronger differentiator
than any single workflow feature.

---

## 5. Staged refactor plan

Each stage is independently shippable and leaves the app working. No big-bang rewrite.

**Stage 0 — Decouple framing (small, do first).**
Move the `Specialists[]` array out of `OrchestratorAgentService` into configuration/DI;
parameterize the "pain-management clinic" framing so the coordinator prompt is templated.
*Outcome:* specialists become data; nothing functional changes yet.

**Stage 1 — Self-registering skills.**
Introduce an `ISkill` interface (`Name`, `GetSchema()`, `ExecuteAsync()`); register skills
in DI and have `SkillExecutor` dispatch by lookup instead of `switch`. Migrate the 12
existing skills.
*Outcome:* new tools no longer touch a shared file. The `switch` wall is gone before we
hit it.

**Stage 2 — Define the WorkflowPack abstraction.**
A pack contributes its specialists + skills + roles. Refactor today's scheduling capability
into a `SchedulingPack` as the reference implementation. Coordinator builds its route table
from all registered packs.
*Outcome:* the seam is generalized and proven against existing functionality.

**Stage 3 — FHIR-ify the domain.**
Broaden `IFhirSyncService` into a resource-mapping layer covering the resources in §4 as
workflows need them (lazily — don't model what no pack uses yet).
*Outcome:* shared vocabulary; new packs inherit interoperability.

**Stage 4 — Ship the first NEW pack.**
Pick one workflow (recommend **patient intake** or **medication refills** — see §6) and
build it as a pack end-to-end. This is the proof that the Nth workflow is cheap.
*Outcome:* two workflows on one engine — the platform claim is now demonstrable.

---

## 6. Recommended first new workflow

**Patient intake & registration** is the recommended first expansion:

- **Highest demo value** — it is the front of every patient journey and pairs naturally
  with the scheduling you already show.
- **Modest domain surface** — `Patient` already exists; intake mainly adds `Encounter`
  and `Coverage`.
- **Clean agentic story** — conversational registration ("let's get you set up") is an
  obvious, sympathetic use of an LLM and showcases multi-turn flows.

**Medication refills** is the strong runner-up: very high real-world demand, tightly
scoped (`MedicationRequest`), and an easy-to-grasp value prop ("ask for a refill in plain
English"). Good as the *second* pack to prove the pattern repeats.

---

## 7. Risks & guardrails

- **Clinical safety / scope.** Agents must stay advisory for anything clinical (the Triage
  specialist already models this: *"never give a diagnosis"*). Keep diagnosis, dosing, and
  results *interpretation* human-in-the-loop. Write-actions stay behind explicit confirm
  steps (the cancel/reschedule two-step is the existing pattern to follow).
- **Role enforcement per skill.** As skills multiply, verify each new skill enforces roles
  at execution time, not just in the prompt. This belongs in the `ISkill` contract.
- **Multi-tenancy is deferred — by decision.** No `TenantId` on entities. Do not let the
  pack work smuggle multi-tenancy back in; per-instance onboarding remains the model until
  explicitly revisited.
- **Don't over-model FHIR.** Add resources only when a shipping pack needs them. A complete
  FHIR domain with no consumers is wasted effort.

---

## 8. One-paragraph version (for the pitch)

> ClinicAgent is an agentic healthcare workflow platform. A coordinator AI routes each
> plain-language request to a specialist sub-agent that runs a constrained, auditable tool
> loop against a FHIR-backed domain — with role-based access and human-in-the-loop
> confirmation for every write. Scheduling shipped first; intake, referrals, refills, and
> prior-authorization are drop-in "workflow packs" on the same engine. Built on .NET 10,
> Blazor, PostgreSQL, and AWS, exposed over both MCP and REST. The platform's value is that
> each new clinical workflow costs a fraction of the last.
