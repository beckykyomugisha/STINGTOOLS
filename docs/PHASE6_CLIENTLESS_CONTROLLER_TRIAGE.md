# Phase 6A — clientless controller triage (amended)

**Originally measured 2026-08-01; amended 2026-08-02 after review, on `main` @ `a5109e3e5`.**
Triage only — no feature code.

> **The runtime column is STILL NOT MEASURED.** The review assumed Docker was up. It is not: the
> `docker-desktop` WSL distro stays `Stopped` across two different launch invocations and the daemon
> pipe never appears. See [Runtime gap](#runtime-gap). Nothing below is presented as a runtime
> result. Where a static proxy exists it is used and labelled as such.

---

## Amendment summary

| Review finding | My position after re-verifying |
|---|---|
| S1 MaterialSync — confirmed, worse than reported (write too, path leak) | **Verified and fixed.** Removed from this triage — it is PR #542. |
| B1 — five unmigrated tables, not two | **Verified, and it is six.** `TelemetryPoints` is also unmigrated; see B1. |
| C1 ApprovalChains duplication | **Verified** (`endpoints.ts:1004-1015`). |
| Docstring lies (Mfa, DeviceTwins) | **Verified.** Unchanged from the first pass. |
| DataRights — I was WRONG, re-rate Tier 1 | **Verified; I was wrong.** See DR1. |
| Capabilities endpoint approved, sequenced after Phase 1 | Taken on trust — it is a decision, not a measurement. |

**Independently re-verified:** the migration sweep, the DataRights export/erase split, the
`DataErasureJob` loop, the twin-cluster service→table mapping, and both docstring claims.
**Taken on trust:** the sequencing decision for the capabilities endpoint, and the statement that
MaterialSync is referenced nowhere in `stingtools-core` (I checked the other five clients myself).

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
`CreateTable` anywhere in a non-Designer migration. On a database built by `dotnet ef database
update`, every action reaching one throws `relation "X" does not exist`.

### The twin cluster is entirely unreachable — measured, not assumed

The review correctly warned that these controllers reach tables through services, so I traced each
service to the tables it actually touches rather than assuming:

| Controller | Route | Reaches | Verdict |
|---|---|---|---|
| DeviceTwins | `…/twins` | `IDeviceTwinService` → `DeviceTwins`, `TelemetryPoints` | **both unmigrated** |
| TwinAlerts | `…/twins/alerts` | `_db.TwinAlerts` directly | **unmigrated** |
| TwinRules | `…/twins/rules` | `_db.TwinRules` directly | **unmigrated** |
| TwinProvisioning | `…/twins/provision` | `ITwinProvisioningService` → `DeviceTwins`, `TaggedElements` | **DeviceTwins unmigrated** |
| TelemetryIngest | `…/telemetry` | `IDeviceTwinService` → `DeviceTwins`/`TelemetryPoints`; `ITwinRuleEvaluator` (`TwinRuleEngine`) → `DeviceTwins`, `TwinAlerts`, `TwinRules` | **all unmigrated** |

So the Tier-2 concern that DeviceTwins' downstream is dead is now **measured**: it is not that
telemetry ingest is missing — `TelemetryIngestController`, `IDeviceTwinService`, `ITwinRuleEvaluator`
and `ITwinProvisioningService` all exist and are DI-registered — it is that **none of them has a
table to write to.**

**STOP item, respected:** I have not authored the six migrations. Per the standing rule, whether the
tables genuinely do not exist in the deployed database has to be established in the runtime pass
first, and the migrations then *proposed* with a rollout note. Since the runtime pass could not run,
that stays open.

---

## Amended triage table

Legend — **Backed**: DbSet + a migration that creates the table. **DI**: every injected service
resolves (static check of `Program.cs`). **Runtime**: not measured for any row.

| # | Controller | Ln / actions | Auth gate | Tenant scoping | Backed | DI | Verdict |
|---|---|---|---|---|---|---|---|
| 1 | **DataRights** | 114 / 3 | `[Authorize(Roles="Owner,Admin")]` | `ITenantContext` | n/a | ✅ | **Tier 1** — DR1 |
| 2 | **CdeContainers** | 287 / 6 | `[Authorize]` + role gate `:255` | `TenantId` + `ProjectMembers` | ✅ | ✅ | **Tier 1** |
| 3 | **ApprovalChains** | 317 / 4 | `[Authorize]` + `[ProjectAccess]` | `[ProjectAccess]` + `TenantId` | ✅ | ✅ | Tier 2 — blocked on C1 |
| 4 | **AssetDataSheets** | 276 / 7 | `[Authorize]` + `[ProjectAccess]` | `RequireProjectMemberAsync` | ✅ | ✅ | Tier 2 |
| 5 | **OfflineManifest** | 227 / 4 | `[Authorize]` | `TenantId` | ✅ | ✅ | Tier 2 — mobile-only by nature |
| 6 | **Mfa** | 256 / 7 | `[Authorize]` (+`TenantAdmin` on one) | via `Users` | ✅ | ✅ | Tier 2 — works; fix M1 |
| 7 | **WorkOrders** | 98 / 3 | `[Authorize]` | global filter (`ITenantScoped`) | ❌ B1 | ✅ | Tier 3 — blocked |
| 8 | **GlobalIdRegistry** | 275 / 6 | `[Authorize]` | `TenantId` | ❌ B1 | ✅ | Tier 3 — blocked |
| 9 | **DeviceTwins** | 108 / 5 | `[Authorize]` | `TenantId` | ❌ B1 | ✅ | Tier 3 — blocked; fix M2 |
| 10 | **CaseStudy** | 150 / 1 | `[Authorize]` | `TenantId` | read-only | ✅ | Tier 3 — sales tool |
| 11 | **TwinAlerts** | 53 / 3 | `[Authorize]` | `TenantId` | ❌ B1 | ✅ | Tier 3 — blocked |
| 12 | **TwinRules** | 129 / 4 | `[Authorize]` | `TenantId` + `Projects` | ❌ B1 | ✅ | Tier 3 — blocked |
| 13 | **TwinProvisioning** | 75 / 2 | `[Authorize]` | via service | ❌ B1 | ✅ | Tier 3 — blocked |
| 14 | **TelemetryIngest** | 98 / 1 | `[Authorize]` | via services | ❌ B1 | ✅ | Tier 3 — blocked |
| — | ~~MaterialSync~~ | — | — | — | — | — | **Removed — shipped as PR #542** |

**DI is clean across all fourteen.** The known-prior failure mode (a controller 500ing on every call
from a missing registration, misreported by the browser as CORS) does not apply to any of them.

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

## Runtime gap

**Not measured.** The review's premise that Docker is up does not hold on this machine:

```
wsl -l -v          ->  docker-desktop    Stopped    2
docker version     ->  failed to connect to the docker API at
                       npipe:////./pipe/dockerDesktopLinuxEngine
```

Two launch attempts (`cmd /c start` and a direct invocation, each followed by a wait) left the distro
`Stopped` and no `Docker Desktop.exe` in `tasklist`. It most likely needs interactive elevation or a
first-run dialog. There is no local Postgres on 5432, and SQLite is not a substitute — `Program.cs`'s
schema block issues a Postgres-only `information_schema` query with no try/catch, so the host dies
before serving a request.

**What therefore remains unknown:**

1. actual status codes and response bodies per action;
2. whether the six unmigrated tables genuinely do not exist in the deployed database — which is the
   precondition the standing rule sets before proposing the migrations;
3. whether anything 500s for a reason not visible statically. DI being clean removes the known-prior
   cause but does not prove the actions run.

**To close it:** the Docker daemon started (likely needs a human to accept whatever dialog is
blocking it), or a Postgres reachable on 5432 plus a connection string. The pass is scripted and
ready to run the moment either exists — including the `relation "X" does not exist` vs other-500
distinction the review asked for, which is the whole point of running it rather than inferring it.
