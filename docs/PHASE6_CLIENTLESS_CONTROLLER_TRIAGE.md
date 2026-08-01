# Phase 6A — clientless controller triage

**Measured 2026-08-02 on `main` @ `a5109e3e5`.** Triage only — no feature code in this PR.

> **Runtime column is NOT MEASURED.** Item 2 of the brief requires exercising every action
> against a running server. Docker Desktop is installed but its daemon would not start on this
> machine (`npipe:////./pipe/dockerDesktopLinuxEngine` — no such file; launching the app left no
> process), and there is no local Postgres on 5432. Everything below is *static* measurement.
> Where a static proxy exists for "does it run" — DI registration, migration presence — it was
> used and is labelled as such. See [Runtime gap](#runtime-gap).

---

## 0. Re-derived list — the brief's ten, plus two

Re-derived per instruction, **including** `Planscape.Server/src/Planscape.API/wwwroot` and
**excluding** `wwwroot/_next/**` and `wwwroot/vendor/**`. Clients scanned: `StingTools/`,
`planscape-web/`, `Planscape/` (mobile), `StingBridge/`, `Planscape.Server/.../wwwroot/`,
`marketing-site/` — 1,856 files.

**Your correction is confirmed.** `PublicConfig` (`api/public-config`), `RoleBuckets`
(`api/state-machine/role-buckets`), `TenantKeywords` (`api/admin/tenant-keywords`) and
`TenantBimManagerRoles` (`api/admin/tenant-bim-manager-roles`) are all called from
`wwwroot/js/dashboard.js`; the latter two also from `wwwroot/index.html` and
`wwwroot/app/index.html`. Not clientless. Left alone.
*(One nuance: I found no `public-config` reference under `marketing-site/` in this checkout —
possibly the built bundle isn't committed. Doesn't change the verdict.)*

All ten in the brief are confirmed clientless by exact route-string grep. **Two more are too:**

| Extra | Route | Note |
|---|---|---|
| **MaterialSync** | `api/MaterialSync` | 53 ln, 2 actions. Not on the brief's list. **Has a security finding — see S1.** |
| **PhotoExport** | `api/projects/{id}/photo-export` | Already scheduled as Phase 5; listing for completeness. |

---

## 1. Triage table

Legend — **Backed**: DbSet + a migration that actually creates the table.
**DI**: every injected service resolves (static check of `Program.cs`).

| # | Controller | Ln / actions | Auth gate | Tenant scoping | Backed | DI | Verdict |
|---|---|---|---|---|---|---|---|
| 1 | **DataRights** | 114 / 3 | `[Authorize(Roles="Owner,Admin")]` | `ITenantContext` | n/a (reads + deletes) | ✅ | **Tier 1 — but STOP, see D1** |
| 2 | **CdeContainers** | 287 / 6 | `[Authorize]` + role gate `:255` | `TenantId` + `ProjectMembers` | ✅ `20260517000003` | ✅ | **Tier 1 — confirmed viable** |
| 3 | **ApprovalChains** | 317 / 4 | `[Authorize]` + `[ProjectAccess]` | `[ProjectAccess]` + `TenantId` | ✅ `20260513000000` | ✅ | Tier 2 — real feature, duplicate of a live one (see C1) |
| 4 | **AssetDataSheets** | 276 / 7 | `[Authorize]` + `[ProjectAccess]` | `RequireProjectMemberAsync` | ✅ `20260513100000` | ✅ | Tier 2 |
| 5 | **OfflineManifest** | 227 / 4 | `[Authorize]` | `TenantId` | ✅ `20260517000000` | ✅ | Tier 2 — mobile-only by nature |
| 6 | **Mfa** | 256 / 7 | `[Authorize]` (+`TenantAdmin` on one) | via `Users` | ✅ `20260517000000` | ✅ | Tier 2 — **works; docstring lies (M1)** |
| 7 | **WorkOrders** | 98 / 3 | `[Authorize]` | global filter only (`ITenantScoped`) | ❌ **NO MIGRATION** | ✅ | **Tier 3 — blocked, see B1** |
| 8 | **GlobalIdRegistry** | 275 / 6 | `[Authorize]` | `TenantId` | ❌ **NO MIGRATION** | ✅ | **Tier 3 — blocked, see B1** |
| 9 | **DeviceTwins** | 108 / 5 | `[Authorize]` | `TenantId` | via services | ✅ | Tier 3 — **docstring lies (M2)** |
| 10 | **CaseStudy** | 150 / 1 | `[Authorize]` | `TenantId` | read-only | ✅ | Tier 3 — sales tool, not product |
| 11 | **MaterialSync** | 53 / 2 | `[Authorize]` **only** | **NONE** | filesystem | ✅ | **Security — see S1** |

**No controller in this group is dead from a missing DI registration.** All five injected services
(`IAuditService`, `ITenantContext`, `IDeviceTwinService`, `ITwinBindingService`,
`IPlatformEventService`) are registered in `Program.cs` (`:428`, `:365`, `:727`, `:729`, `:724`);
`IDataProtector` comes from `AddDataProtection()` (`:506`). My first grep reported all six as
unregistered — a false negative from a too-strict pattern, corrected before reporting.

---

## 2. Findings that change the plan

### S1 — MaterialSync is a cross-tenant read (security)

`MaterialSyncController` carries `[Authorize]` and **nothing else**: no `[ProjectAccess]`, no
membership check, no tenant filter. It does not use EF at all — it writes and reads
`ContentRootPath/App_Data/material_snapshots/{projectId}.json` directly
(`MaterialSyncController.cs:31-35, 47-50`).

Because it bypasses EF, **the global tenant query filter cannot protect it**. Any authenticated
user, in any tenant, who knows or guesses a project GUID can `GET api/MaterialSync/snapshot/{id}`
and read that project's material library.

Two secondary problems: the snapshot is on the container filesystem, so it does not survive a
Render redeploy; and the docstring calls this "minimum-viable persistence" with per-row DB rows
"in a follow-up migration" that never landed.

**This is not a UI gap.** Verdict: **fix the authorization**, independent of any client work.
I have not touched it — flagging for a decision.

### B1 — WorkOrders and GlobalIdRegistry have no table

Both appear in the EF model snapshot — `b.ToTable("WorkOrders")` at
`PlanscapeDbContextModelSnapshot.cs:8910`, `b.ToTable("GlobalIdRegistry")` at `:2372` — but **no
migration creates either table**. Grep for `name: "WorkOrders"` / `name: "GlobalIdRegistry"` across
all non-Designer migrations returns nothing.

So on a database built by `dotnet ef database update`, every action on these two throws a Postgres
"relation does not exist". They are **not** a missing-UI problem; they are a missing migration.

*Caveat:* I could not check the production database, so it is possible the tables exist there via
another path. That is exactly what the runtime pass would have settled.

This makes **WorkOrders' otherwise-encouraging story moot for now** — its downstream *is* live
(`StingTools/BIMManager/PlatformEvents/ParamStampEventHandler.cs` is a real handler that writes the
parameter onto the Revit element), so once the table exists this is a genuinely complete loop.

### C1 — ApprovalChains duplicates a live mechanism

There are **two** document-approval implementations:

| | route | client |
|---|---|---|
| `DocumentsController` | `POST /documents/{id}/approval-request`, `PUT /documents/{id}/approval/{aid}`, `GET /documents/{id}/approval-status` | **mobile** (`Planscape/src/api/endpoints.ts:1005,1012`) |
| `ApprovalChainsController` | `/documents/{docId}/approval-chain` (+ `/decisions`) | none |

The chain version supports multi-step and parallel approval, which the flat one does not — so it is
not simply redundant. But "wire a client" is the wrong first question: the right one is **which of
the two is canonical**, because shipping a UI for the second creates two approval records for one
document. Verdict: **decide before building.**

### M1 — MfaController's docstring is stale (confirmed)

Line 13: *"Production implementation would use a TOTP library (e.g. Otp.NET)"*. It already does:
`using OtpNet;` (`:5`), `IDataProtector` for secret storage (`:23`), and
`totp.VerifyTotp(req.Code, out _, new VerificationWindow(2, 2))` (`:119`) — the ±2-step window you
described. **The implementation is real; the comment is wrong.** Trivial fix, not shipped here
because 6A carries no code.

### M2 — DeviceTwinsController's docstring is wrong (confirmed)

Lines 11-12 claim it *"Powers the mobile Live tab (RAG list)"*. There is **no Live tab** in
`Planscape/app/` — the only near-match in a case-insensitive listing is `de`**live**`rables`.
Telemetry ingest does exist (`TelemetryIngestController`, `IDeviceTwinService`,
`ITwinBindingService` all registered), so the downstream is not dead — but the claimed consumer
does not exist.

---

## 3. Tier recommendations

### Tier 1 — complete now

**CdeContainers** — agreed, and I verified your note: the gate at `:255` is
`ProjectRole in {Manager, Admin, Owner}`, a vocabulary that **is** assignable from the web dropdown
today, so unlike the PM gates it is **live**. It must **not** be folded into the Phase 1 capability
helper without a deliberate decision, because `CanCurateProject` also admits `Coordinator` and
three ISO codes — that would *widen* who can restructure the CDE folder tree. Backed, scoped, DI-clean.

**DataRights — STOP, decision needed before any UI.** The brief anticipated this. `POST erase`
touches `Users`, `Projects`, `ProjectMembers`, `Documents`, `ProjectModels`, `Issues`, `Invoices`,
`Subscriptions`, `AuditLogs`, `Tenants`. I have not yet traced whether each is a soft-delete, an
anonymisation, or a hard delete, nor whether `cancel-erase` can actually undo it. **A button must
not be shipped until the UI copy can describe truthfully what the request destroys.** That trace is
the first thing I would do in 6B — it is a read-only exercise and does not need the server.

### Tier 2 — real features, schedule deliberately

`AssetDataSheets`, `OfflineManifest`, `Mfa` (fix M1), `ApprovalChains` (**after C1 is decided**).

### Tier 3 — not a UI job

`WorkOrders` and `GlobalIdRegistry` — blocked on B1 (missing migration).
`DeviceTwins` — fix M2; a UI needs a real telemetry source and a decision on where the RAG list lives.
`CaseStudy` — a one-action sales-deck PDF; a UI is a sales-ops question, not product parity.

### Security, out of band

`MaterialSync` (S1) — fix authorization regardless of client work.

---

## Runtime gap

Not measured, and it matters for exactly these questions:

1. whether `WorkOrders` / `GlobalIdRegistry` tables exist in a real deployed database (B1);
2. actual status codes and error bodies for every action — the brief's item 1 asks for real error
   shapes, and I have only the ones visible in source;
3. whether anything 500s for a reason not visible statically. DI is clean, which removes the
   known-prior cause, but does not prove the actions run.

To close it I need either the Docker daemon started, or a Postgres reachable on 5432 plus a
connection string. SQLite is not a substitute: `Program.cs`'s schema block runs
`SELECT … FROM information_schema.tables`, which is Postgres-only and uncaught, so the host dies
before serving a request.

**Awaiting review of this table before building anything.**
