# ClinicAgent — Multi-Tenancy Design

**Status:** Proposal · **Date:** 2026-06-30 · **Owner:** Brad Tarver

> ClinicAgent is becoming a SaaS product serving many clinics. This document defines how
> tenant (clinic) data is isolated. The governing rule: **the model — and any caller —
> never decides which clinic's data it can touch. Tenant identity is resolved server-side
> from the authenticated principal and enforced by the data layer.**

---

## 1. Why this comes first

Every other "make it a product" decision (external MCP exposure, scoped API tokens, FHIR
multi-EHR sync) depends on a tenant boundary that does not yet exist. As of today:

| Concern | Current state | File |
|---|---|---|
| Tenant on the user | `AppUser : IdentityUser` adds only `DisplayName` | `ClinicScheduler.Infrastructure/Data/AppUser.cs` |
| Tenant in the token | JWT carries `sub` / `email` / `role` only | `ClinicScheduler.Web/Api/TokenController.cs` |
| Tenant in the request context | `ICurrentUserService` exposes `UserId` / `UserName` / `Principal` | `ClinicScheduler.Core/Interfaces/ICurrentUserService.cs` |
| Read isolation | **No EF query filters** — every query sees every clinic | `ClinicScheduler.Infrastructure/Data/ClinicDbContext.cs` |
| Uniqueness | `Patient.Email`, `Therapist.Email`, `Therapist.NpiNumber` are **globally** unique | `ClinicDbContext.OnModelCreating` |

There is no `TenantId` anywhere in the domain. Isolation is therefore impossible to enforce
today, and the globally-unique indexes are an active blocker (two clinics cannot share a
patient email). This is a data-layer problem upstream of the MCP server, the API, and the UI.

---

## 2. Tenancy model: shared database, shared schema, `ClinicId` discriminator

**Decision: single database, single schema, a `ClinicId` column on every tenant-owned table,
isolated by EF Core global query filters.**

Rejected alternatives:

- **Database-per-tenant** — strongest isolation, but N connection pools, N migration runs, and
  operational cost that does not fit a pre-revenue team running on Activate credits.
- **Schema-per-tenant** — middle ground, but EF Core migrations across many schemas are painful
  and buy little over row-level filtering at this scale.

Shared-schema + discriminator is the standard SaaS starting point. A noisy or compliance-bound
tenant can be promoted to its own database later **without changing the application model** —
only the connection-resolution strategy changes.

---

## 3. The tenant boundary: a new `Clinic` entity (not `Location`)

`Location` is a *physical site*; one clinic can operate several. So the tenant root is a new
top-level entity:

```
Clinic (tenant root)
 └── Location (1..*)        physical sites
      └── Room (1..*)
 └── Patient, Therapist, Appointment, TreatmentPlan, TherapyType,
     Encounter, AppointmentRequest, Notification, WaitlistEntry, AuditLog ...
```

Every tenant-owned entity gets `int ClinicId` + a `Clinic` navigation. `AppUser` also gets
`ClinicId` — **this is the source of truth** from which every request's tenant is derived.

Reference / non-tenant data: ASP.NET Identity tables remain global except for the `ClinicId`
column added to `AppUser`. A future "platform admin" role may be cross-tenant; that is handled
by *bypassing* the filter under an explicit, audited code path — never by omitting it.

---

## 4. Resolution chain — token → service → DbContext

Tenant flows one direction only, from identity down to the query:

```
AppUser.ClinicId
   │  (login)
   ▼
"clinic" claim embedded in the JWT            TokenController
   │  (each request)
   ▼
ICurrentUserService.TenantId                  reads Principal claim
   │  (DbContext construction — already injected for audit)
   ▼
ClinicDbContext._tenantId                      drives query filter + insert stamp
```

The DbContext **already** receives `ICurrentUserService` (for audit attribution), so threading
tenant through adds no new plumbing — only a new property on the interface.

`ClinicId` is **never** a method parameter, API body field, or MCP tool argument. A caller
supplying a clinic id is ignored.

---

## 5. Enforcement — two automatic mechanisms

Both live in the DbContext so isolation is the default, not per-query discipline (one forgotten
`.Where(x => x.ClinicId == id)` is a cross-tenant leak).

**Read isolation — global query filter:**
```csharp
modelBuilder.Entity<Patient>().HasQueryFilter(p => p.ClinicId == _tenantId);
// ...applied to every tenant-owned entity, by convention over a shared interface (ITenantOwned).
```

**Write isolation — auto-stamp on insert:** in `SaveChangesAsync`, the same loop that already
sets `UpdatedAt` stamps `ClinicId = _currentUser.TenantId` on every `Added` tenant entity. The
model cannot persist a row into another clinic.

Guard rails:
- A `null` tenant in a request-scoped context is a hard error, not "see everything."
- Background/seed/migration contexts (no user) use an explicit, logged filter-bypass.

---

## 6. Schema corrections forced by tenancy

The current globally-unique indexes become **composite with `ClinicId`**:

| Index today | Becomes |
|---|---|
| `Patient.Email` unique | `(ClinicId, Email)` unique |
| `Therapist.Email` unique | `(ClinicId, Email)` unique |
| `Therapist.NpiNumber` unique (filtered) | `(ClinicId, NpiNumber)` unique (filtered) |

(NPI is globally unique to a provider in the real world, but we keep one `Therapist` row per
clinic and scope the index per clinic; provider de-duplication across tenants is out of scope.)

`AuditLog` also carries `ClinicId` so the compliance trail is tenant-scoped.

---

## 7. Migration & backfill

Existing production data is one implicit clinic. The migration is staged so it is non-destructive:

1. Create `Clinics`; insert one default clinic row.
2. Add **nullable** `ClinicId` to every tenant-owned table and to `AspNetUsers`.
3. Backfill all existing rows and users to the default clinic id.
4. Alter `ClinicId` to **non-nullable**; add FKs and the composite unique indexes.

---

## 8. Impact on the MCP server

`ClinicScheduler.Mcp` (stdio) registers **no** `ICurrentUserService`, and its tools are `static`
methods taking primitives — so once query filters exist, its queries would see nothing. Resolved
in two phases:

- **Stdio / demo (now):** register a **fixed-tenant** `ICurrentUserService` from config/env
  (`Clinic__Id`). One server process serves exactly one clinic. Honest and safe for the capstone.
- **HTTP / SSE (later):** tenant is resolved per request from the bearer token's `clinic` claim —
  the identical resolution chain. No tool ever takes a tenant argument.

---

## 9. Staged delivery (each stage shippable)

| Stage | Deliverable |
|---|---|
| **0** | `Clinic` entity + `AppUser.ClinicId`; non-destructive migration + backfill to a default clinic. |
| **1** | `TenantId` on `ICurrentUserService`; `clinic` claim in `TokenController`; composite unique indexes. |
| **2** | `ITenantOwned` marker + `ClinicId` on all tenant entities; global query filters + auto-stamp in `SaveChangesAsync`. |
| **3** | Fixed-tenant `ICurrentUserService` wired into the MCP server (`Clinic__Id`). |
| **later** | Per-request HTTP/SSE tenant resolution; platform-admin filter bypass; optional DB-per-tenant promotion. |

---

## 10. Open decisions

- **`ClinicId` type:** `int` (matches existing keys) vs `Guid` (non-enumerable, friendlier for
  public/external token subjects). Leaning `int` internally for FK consistency; expose an opaque
  public slug if a clinic id ever appears in a URL or token audience.
- **Cross-tenant platform admin:** explicit audited bypass vs a dedicated unfiltered DbContext.
- **Provider identity across clinics:** per-clinic `Therapist` rows (chosen) vs a shared provider
  directory keyed by NPI (future).
