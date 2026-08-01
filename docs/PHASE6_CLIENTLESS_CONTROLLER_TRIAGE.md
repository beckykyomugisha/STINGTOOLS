# Phase 6A — clientless controller triage (amended twice)

**Originally measured 2026-08-01; amended 2026-08-02 after review; runtime pass added 2026-08-02,
on `claude/phase6-runtime` @ `bf7cbb19a`.** Triage only — no feature code.

> **The runtime column is now MEASURED, and it overturns B1.** Docker Desktop started on this
> attempt, so the pass ran against a real Postgres. **All fourteen controllers serve traffic and not
> one returns `relation … does not exist`** — including all seven the previous revision rated
> "Tier 3 — blocked". See [Runtime pass](#runtime-pass-measured).
>
> The reason is [B1-R](#b1-r--why-the-unmigrated-tables-exist-anyway): this codebase does **not**
> build its schema from the migration folder. `PlatformSchemaPatcher` creates those six tables at
> boot with `CREATE TABLE IF NOT EXISTS`, in **both** the Development and Production branches, and
> ADR 0001 declares that the official mechanism. The "Backed" column below was measuring the wrong
> thing — and in practice it is **inverted**.
>
> **No production database was queried.** Everything below is local: a fresh empty DB, the API's own
> boot path, and the compose stack.

---

## Amendment summary

| Review finding | My position after re-verifying |
|---|---|
| S1 MaterialSync — confirmed, worse than reported (write too, path leak) | **Verified and fixed.** Removed from this triage — it is PR #542. |
| B1 — five unmigrated tables, not two | ~~Verified, and it is six.~~ **Half right, and the half that mattered was wrong.** Six tables genuinely have no `CreateTable` — but they exist at runtime anyway, so nothing was blocked. See B1-R. |
| C1 ApprovalChains duplication | **Verified** (`endpoints.ts:1004-1015`). |
| Docstring lies (Mfa, DeviceTwins) | **Verified.** Unchanged from the first pass. |
| DataRights — I was WRONG, re-rate Tier 1 | **Verified; I was wrong.** See DR1. |
| Capabilities endpoint approved, sequenced after Phase 1 | Taken on trust — it is a decision, not a measurement. |

**Independently re-verified:** the migration sweep, the DataRights export/erase split, the
`DataErasureJob` loop, the twin-cluster service→table mapping, and both docstring claims.
**Taken on trust:** the sequencing decision for the capabilities endpoint, and the statement that
MaterialSync is referenced nowhere in `stingtools-core` (I checked the other five clients myself).

**Third pass (2026-08-02, runtime).** The `CreateTable` sweep was re-verified twice and was correct
both times — but it was the wrong question, and re-running it more carefully could never have
revealed that. What did was booting the thing. Two reviews of a static finding agreed with each
other; the first execution overturned them.

---

## DR1 — DataRights: I was wrong, Tier 1 confirmed

My first pass said `POST erase` "touches ten tables including Invoices and Subscriptions". That was
the controller's **whole DbSet list**, which I attributed to the erase path without reading the erase
body. The review is right, and the split is clean:

- **Export** (`DataRightsController.cs:54-66`) reads the ten tables — `Tenants`, `Users`, `Projects`,
  `ProjectMembers`, `Issues`, `Documents`, `ProjectModels`, `AuditLogs`, `Subscriptions`, `Invoices` —
  to build the subject-access ZIP. Read-only.
- **Erase** (`:87-99`) touches **one**:

  ```csharp
  tenant.IsActive = false;
  tenant.PendingErasureAt = DateTime.UtcNow.AddDays(30);
  ```

  guarded by `ConfirmationPhrase != "ERASE EVERYTHING"` → 400, and `tenant.Slug == "planscape"` →
  `400 platform_tenant_protected`.
- **Cancel** (`:101-110`) resets **both** fields — fully reversible for 30 days.
- **The loop closes.** `DataErasureJob.cs:47-49` sets `BypassTenantFilter`, selects
  `PendingErasureAt <= now`, and hard-deletes; wired as a daily Hangfire job at 04:00 UTC
  (`Program.cs:1791`), with a comment noting the timing lets a same-day `cancel-erase` land first.

So the UI copy is describable, which was the bar I set: **"freezes the tenant now; permanent deletion
runs in 30 days; reversible until then."** → **Tier 1.**

---

## B1 — six unmigrated tables, not five

Re-ran the `CreateTable` sweep independently. The review's five reproduce exactly, **plus one it
missed**:

| Table | Migration | In model snapshot |
|---|---|---|
| ApprovalChains | ✅ `20260513000000_AddPost204ServerEnhancements` | — |
| AssetDataSheets | ✅ `20260513100000_AddAssetDataSheetEngine` | — |
| AssetDataSheetTemplates | ✅ `20260513100000_AddAssetDataSheetEngine` | — |
| CdeContainers | ✅ `20260517000003_AddCdeFolderHierarchy…` | — |
| MfaEnrollments | ✅ `20260517000000_AddBoq…SsoMfaDashboard` | — |
| **DeviceTwins** | ❌ none | yes |
| **TwinAlerts** | ❌ none | yes |
| **TwinRules** | ❌ none | yes |
| **TelemetryPoints** | ❌ none | yes ← **not in the review's list** |
| **GlobalIdRegistry** | ❌ none | yes |
| **WorkOrders** | ❌ none | yes |

All six are present in `PlanscapeDbContextModelSnapshot.cs` (so EF believes they exist) with no
`CreateTable` anywhere in a non-Designer migration.

> ~~On a database built by `dotnet ef database update`, every action reaching one throws
> `relation "X" does not exist`.~~
>
> **This sentence is wrong and is the load-bearing error of the previous revision.** The `CreateTable`
> sweep itself reproduces — no migration creates those six — but the inference does not, because
> **no environment is built by `dotnet ef database update`.** Measured, not argued: see B1-R.

### ~~The twin cluster is entirely unreachable~~ — the service→table mapping, which stands

The review correctly warned that these controllers reach tables through services, so I traced each
service to the tables it actually touches rather than assuming:

| Controller | Route | Reaches | Verdict |
|---|---|---|---|
| DeviceTwins | `…/twins` | `IDeviceTwinService` → `DeviceTwins`, `TelemetryPoints` | **both unmigrated** |
| TwinAlerts | `…/twins/alerts` | `_db.TwinAlerts` directly | **unmigrated** |
| TwinRules | `…/twins/rules` | `_db.TwinRules` directly | **unmigrated** |
| TwinProvisioning | `…/twins/provision` | `ITwinProvisioningService` → `DeviceTwins`, `TaggedElements` | **DeviceTwins unmigrated** |
| TelemetryIngest | `…/telemetry` | `IDeviceTwinService` → `DeviceTwins`/`TelemetryPoints`; `ITwinRuleEvaluator` (`TwinRuleEngine`) → `DeviceTwins`, `TwinAlerts`, `TwinRules` | **all unmigrated** |

The service→table mapping above is correct and I leave it standing — it is useful. What I drew from
it was not. I concluded **"none of them has a table to write to"**; the runtime pass shows every one
of them has a table and writes to it successfully (`TwinRules` = 8 rows after `seed-defaults`,
`DeviceTwins` and `TelemetryPoints` = 1 row each after `POST …/telemetry/ingest`).

**STOP item, still respected — and now clearly the right call.** I did not author the six migrations.
Had I done so it would have been wasted work in the most misleading way: adding files to a folder EF
does not read (B1-R). The precondition the standing rule sets — establish at runtime that the tables
are really missing — is exactly what caught this, and the answer is that they are **not** missing.

---

## B1-R — why the "unmigrated" tables exist anyway

Three measurements, all local, in the order I made them.

**1. The migration folder is inert.** EF discovers migrations by reflecting over types carrying
`[Migration("id")]`, normally emitted into a `.Designer.cs` companion. In
`src/Planscape.Infrastructure/Data/Migrations/`:

```
migration .cs (excl. Designer/snapshot) : 80
.Designer.cs companions                 :  2
files carrying [Migration(              :  2   (MeetingMedia, SustainabilitySnapshots)
```

So `Migrate()` / `ef database update` applies **2 of 80**. This is not new breakage —
[`docs/adr/0001-schema-management.md`](adr/0001-schema-management.md) (Accepted, 2026-06-04) records
it and **adopts `EnsureCreated` + idempotent patchers as the official, supported mechanism**. It
measured 0 of 75 then; the count has since drifted to 2 of 80. I did not consult that ADR in either
earlier pass, which is how the wrong inference survived two reviews.

**2. A patcher creates all six, on every boot, in both branches.**
`PlatformSchemaPatcher.cs` issues `CREATE TABLE IF NOT EXISTS` for `DeviceTwins` (`:91`),
`TelemetryPoints` (`:115`), `TwinRules` (`:126`), `TwinAlerts` (`:143`), `WorkOrders` (`:162`) and
`GlobalIdRegistry` (`:260`). `Program.cs:1585` calls it **outside** the `if (Development) … else
Migrate()` split, guarded only by `db.Database.IsRelational()` — the in-file comment says it runs in
both branches precisely because "the hand-authored `Migrate()` set is also incomplete".

**3. Fresh-database boot on the Production path — the decisive test.** Empty DB (0 tables), API
booted with `ASPNETCORE_ENVIRONMENT=Production` so it takes the `Migrate()` branch:

```
Unhandled exception. System.InvalidOperationException: Schema drift detected:
112 missing table(s), 0 table(s) with missing column(s).   (Database:SchemaDriftStrict=true)
  at Planscape.API.SchemaDriftChecker.AssertAsync(...) Program.cs:line 1592
```

The boot **aborts**. Of the 23 tables that did get created before it died, the patcher's are all
present and the migrations' are not:

| Present after a migration-path boot | Missing (of 112) |
|---|---|
| `DeviceTwins`, `TelemetryPoints`, `TwinRules`, `TwinAlerts`, `WorkOrders`, `GlobalIdRegistry` — **all six** | `ApprovalChains`, `AssetDataSheets`, `AssetDataSheetTemplates`, `CdeContainers`, `MfaEnrollments` — **all five "✅ backed"** |
| `PlatformEvents`, `MeetingSessions`, `ClashRecords`, … (patcher-created) | `Tenants`, `Users`, `Projects` — the core of the app |
| `__EFMigrationsHistory` → exactly 2 rows: `MeetingMedia`, `SustainabilitySnapshots` | |

**So the Backed column was inverted.** Every table B1 marked ❌ exists; every table it marked ✅ does
not. And the corollary settles the prod question without touching prod: a database built only from
this repo's migrations **cannot run the app at all** — it has no `Tenants` and the boot refuses. Any
environment that is up therefore reached that state through the `EnsureCreated`/patcher path, and
that path creates all six tables. No manual DDL needs to be hypothesised, and no production query
could have told us anything this doesn't.

**The real gap is not six missing migrations — it is that 78 of 80 migration files are invisible to
EF.** That is an existing, documented, accepted architectural decision (ADR 0001), not a Phase 6A
finding, and it is out of scope here. Flagging it, not fixing it.

---

## Amended triage table

Legend — **Backed**: table exists on a booted database (was: "a migration creates it" — see B1-R for
why that was the wrong test). **DI**: every injected service resolves (static check of `Program.cs`).
**Runtime**: measured — status code from the pass below.

| # | Controller | Ln / actions | Auth gate | Tenant scoping | Backed | DI | Runtime | Verdict |
|---|---|---|---|---|---|---|---|---|
| 1 | **DataRights** | 114 / 3 | `[Authorize(Roles="Owner,Admin")]` | `ITenantContext` | n/a | ✅ | 200 | **Tier 1** — DR1 |
| 2 | **CdeContainers** | 287 / 6 | `[Authorize]` + role gate `:255` | `TenantId` + `ProjectMembers` | ✅ patcher | ✅ | 200 | **Tier 1** |
| 3 | **ApprovalChains** | 317 / 4 | `[Authorize]` + `[ProjectAccess]` | `[ProjectAccess]` + `TenantId` | ✅ patcher | ✅ | 200 | Tier 2 — blocked on C1 |
| 4 | **AssetDataSheets** | 276 / 7 | `[Authorize]` + `[ProjectAccess]` | `RequireProjectMemberAsync` | ✅ patcher | ✅ | 200 | Tier 2 |
| 5 | **OfflineManifest** | 227 / 4 | `[Authorize]` | `TenantId` | ✅ patcher | ✅ | 400¹ | Tier 2 — mobile-only by nature |
| 6 | **Mfa** | 256 / 7 | `[Authorize]` (+`TenantAdmin` on one) | via `Users` | ✅ patcher | ✅ | 200 | Tier 2 — works; fix M1 |
| 7 | **WorkOrders** | 98 / 3 | `[Authorize]` | global filter (`ITenantScoped`) | ✅ patcher | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2** |
| 8 | **GlobalIdRegistry** | 275 / 6 | `[Authorize]` | `TenantId` | ✅ patcher | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2** |
| 9 | **DeviceTwins** | 108 / 5 | `[Authorize]` | `TenantId` | ✅ patcher | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2**; fix M2 |
| 10 | **CaseStudy** | 150 / 1 | `[Authorize]` | `TenantId` | read-only | ✅ | 200 | Tier 3 — sales tool |
| 11 | **TwinAlerts** | 53 / 3 | `[Authorize]` | `TenantId` | ✅ patcher | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2** |
| 12 | **TwinRules** | 129 / 4 | `[Authorize]` | `TenantId` + `Projects` | ✅ patcher | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2** |
| 13 | **TwinProvisioning** | 75 / 2 | `[Authorize]` | via service | ✅ patcher | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2** |
| 14 | **TelemetryIngest** | 98 / 1 | `[Authorize]` | via services | ✅ patcher | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2** |
| — | ~~MaterialSync~~ | — | — | — | — | — | — | **Removed — shipped as PR #542** |

¹ `400` is ordinary model validation, not a schema failure: `{"errors":{"deviceId":["The deviceId
field is required."]}}`. The pass sent no query string. Route shape, not breakage.

**DI is clean across all fourteen.** The known-prior failure mode (a controller 500ing on every call
from a missing registration, misreported by the browser as CORS) does not apply to any of them.

**Seven rows moved off Tier 3.** They were rated blocked on a schema premise that does not hold. They
are now ordinary Tier 2: the backend works, there is no client. That is a *product* gap, not a
*platform* one, and it is a materially different piece of work.

---

## Unchanged findings

**C1 — ApprovalChains duplicates a live mechanism.** Re-verified: `Planscape/src/api/endpoints.ts`
drives `DocumentsController`'s flat approval (`POST …/approvals` at `:1005`, `PUT …/approval/{id}` at
`:1012` — the file comments on the singular in the decide route). `ApprovalChainsController` adds
multi-step and parallel approval and has no client. **Which is canonical must be decided before any
UI**, or one document gets two approval records.

**M1 — Mfa docstring is stale.** `:13` says "Production implementation would use a TOTP library (e.g.
Otp.NET)". It already does: `using OtpNet;` (`:5`), `IDataProtector` (`:23`),
`totp.VerifyTotp(req.Code, out _, new VerificationWindow(2, 2))` (`:119`).

**M2 — DeviceTwins docstring is false.** `:11-12` claims it "Powers the mobile Live tab (RAG list)".
There is no Live tab in `Planscape/app/`; the only case-insensitive match is `de`**live**`rables`.

Both to be fixed in whichever PR next touches those files, per the review.

---

## Runtime pass (measured)

**Closed.** The earlier blocker was self-inflicted: Docker Desktop had never actually been launched,
only probed. Starting `"C:\Program Files\Docker\Docker\Docker Desktop.exe"` brought the daemon up
first try (`docker version` → server `29.4.3`, `docker-desktop` distro `Running`), no elevation or
dialog required. The previous revision's "needs a human" diagnosis was wrong.

**Environment.** Local only — `docker-postgres-1` (`postgres:16-alpine`) and `docker-api-1`, plus a
throwaway API container against a scratch database for the fresh-boot test. **No production database
was contacted at any point.**

**Method.** Log in as `admin@planscape.demo`, then call every controller's primary action with a real
project id, classifying each body for `does not exist` / `42P01` / a stack trace so a schema failure
could not be mistaken for a benign non-200.

```
=== The six "unmigrated" tables — the disputed rows ===
7  WorkOrders          GET  /api/projects/{p}/work-orders          200
8  GlobalIdRegistry    GET  /api/projects/{p}/global-id-registry    200
9  DeviceTwins         GET  /api/projects/{p}/twins                 200
9  DeviceTwins overlay GET  /api/projects/{p}/twins/overlay         200
11 TwinAlerts          GET  /api/projects/{p}/twins/alerts          200
12 TwinRules           GET  /api/projects/{p}/twins/rules           200

=== Write paths ===
12 TwinRules seed      POST /api/projects/{p}/twins/rules/seed-defaults      200
13 TwinProvisioning    POST /api/projects/{p}/twins/provision/seed-from-model 200
14 TelemetryIngest     POST /api/projects/{p}/telemetry/ingest               200
```

**Zero `RELATION-MISSING`. Zero 5xx. Across all fourteen.**

Reads alone would only prove the tables exist, so the writes were checked for persistence rather than
trusted from a 200:

```
DeviceTwins=1  TelemetryPoints=1  TwinRules=8  TwinAlerts=0  WorkOrders=0  GlobalIdRegistry=26
```

`TwinRules=8` is `seed-defaults` landing eight rules; `DeviceTwins`/`TelemetryPoints` are the ingest
auto-provisioning a twin and storing its reading. The write path is live, not a no-op returning 200.

**The three unknowns this was meant to resolve, resolved:**

1. *Status codes per action* — measured above; thirteen 200s and one validation 400.
2. *Whether the six tables really are absent* — **they are present**, and B1-R shows why they are
   present on any booted database, without needing to look at a deployed one.
3. *Whether anything 500s for a reason not visible statically* — nothing 500s.

**Residual limits, stated rather than papered over.** The pass exercises each controller's primary
action, not all 46 actions; destructive ones (`DataRights erase`, alert `ack`/`resolve`) were
deliberately not fired. `TwinAlerts` and `WorkOrders` returned 200 over **empty** tables — their read
path and schema are proven, their populated behaviour is not. And the single-tenant demo login does
not exercise the cross-tenant isolation each row claims; that wants a two-tenant fixture and is worth
doing separately.
