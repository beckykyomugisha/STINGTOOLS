# Phase 6A — clientless controller triage (amended four times)

**Originally measured 2026-08-01; amended 2026-08-02 after review; runtime pass added 2026-08-02;
corrected 2026-08-02 (fourth pass) after a second review; corrected again 2026-08-02 (fifth pass)
after `render.yaml` was read.** Triage only — no feature code.

> ## Fifth pass — the correction that voids B1 outright and reinstates the runtime pass
>
> Every earlier revision of this document assumed **production runs `db.Database.Migrate()`**. It
> does not. `render.yaml` sets, on **both** services — `planscape-api` (`:51-52`) and the worker
> (`:104-105`):
>
> ```yaml
> - key: PLANSCAPE_USE_ENSURE_CREATED
>   value: "true"
> ```
>
> with a comment stating that the incomplete migration set is that way **by design**, that
> `EnsureCreated` + idempotent patchers is the official mechanism, and that "without this flag a
> FRESH database takes the `Migrate()` branch and comes up nearly empty."
>
> `Program.cs:1522-1564` is a three-way branch:
>
> ```csharp
> var useEnsureCreated = app.Environment.IsDevelopment()
>     || string.Equals(Environment.GetEnvironmentVariable("PLANSCAPE_USE_ENSURE_CREATED"),
>                      "true", StringComparison.OrdinalIgnoreCase);
>
> if (!db.Database.IsRelational()) { /* tests */ }
> else if (useEnsureCreated)       { /* probe Tenants; creator.CreateTables() */ }   // ← production
> else                             { db.Database.Migrate(); }                        // ← dead in prod
> ```
>
> `useEnsureCreated` is **true in production**. The `Migrate()` branch is unreachable there.
>
> **Three consequences, each of which supersedes an earlier conclusion in this document:**
>
> **1. The runtime pass is reinstated as broadly representative of production.** The fourth pass
> demoted it on the grounds that `docker-api-1` runs Development and therefore takes a branch
> production would not. That reasoning was wrong: production takes **the same branch**, for a
> different reason (the env var rather than `IsDevelopment()`), converging on the identical
> `creator.CreateTables()` call at `:1559`. A local stack and production build their schema by the
> same mechanism. The pass is still not a *proof* about production — it is a different database with
> empty tables and one tenant — but "it says nothing about prod" was too strong and is **retracted**.
>
> **2. B1 is VOID — in all three of its forms.** Not "restated", not "downgraded": void.
> - as **"five/six unmigrated tables"** — void, they are created by `EnsureCreated` and the patcher;
> - as **"migration hygiene"** — void, because there is no hygiene defect to report. The migration
>   set is *deliberately* not the mechanism. ADR 0001 documents this and `render.yaml` enforces it.
>   The 78 inert migrations are **vestigial, not broken**;
> - as **the `DocumentApprovals` question** — void, see the appendix. Production never runs the
>   migration that would have touched it.
>
> **3. The M3 measurement is correct but is expected behaviour, not a defect.** "2 of 78 migrations
> visible; `dotnet ef database update` yields 2 tables and no `Tenants`" is exactly what
> `render.yaml`'s own comment predicts for the `Migrate()` path, which is why the flag exists. It is
> preserved as [Appendix A](#appendix-a--the-migration-measurement-expected-behaviour-not-a-defect),
> re-labelled.
>
> **What survives all of this.** One finding, and it is not in this document: Postgres Row-Level
> Security is not merely unapplied but **unreachable by the mechanism production runs** — filed as
> **#545**. That is the only item from this workstream still open on the schema axis.
>
> **No production database was queried, at any point, in any pass.**

---

## Amendment summary

| Review finding | My position after re-verifying |
|---|---|
| S1 MaterialSync — confirmed, worse than reported (write too, path leak) | **Verified and fixed.** Removed from this triage — it is PR #542. |
| B1 — five unmigrated tables, not two | ~~Verified, and it is six.~~ ~~Half right — they exist at runtime anyway.~~ ~~Restated as migration hygiene.~~ **VOID.** Production runs `EnsureCreated` + patchers by design (`render.yaml:51-52`), so the migration set is not the schema mechanism and its incompleteness is not a defect. Void as missing-tables, as hygiene, and as the `DocumentApprovals` question. See the fifth-pass banner. |
| C1 ApprovalChains duplication | **Undecided, and downgraded from "verified".** The two mechanisms are an **OR**, not competing records — `DocumentsController.cs:1554`. See C1. |
| Docstring lies (Mfa, DeviceTwins) | **Verified, and now FIXED in this PR.** `MfaController` said a "production implementation would use a TOTP library (e.g. Otp.NET) and IDataProtectionProvider" — it already uses both (`using OtpNet;`, `new Totp(secretBytes).VerifyTotp(…)`, injected `IDataProtector`). `DeviceTwinsController` said it "powers the mobile Live tab (RAG list)" — there is no Live tab in `Planscape/app/(tabs)/`, the two `live.tsx` files are a LiveKit meeting screen and a healthcare pressure view, and a repo-wide grep for `device-twins`/`DeviceTwins` across `Planscape/` and `planscape-web/` returns zero hits. Both docstrings rewritten with the measurement inline. |
| DataRights — I was WRONG, re-rate Tier 1 | **Verified; I was wrong.** See DR1. |
| Capabilities endpoint approved, sequenced after Phase 1 | Taken on trust — it is a decision, not a measurement. |

**Independently re-verified:** the migration sweep, the DataRights export/erase split, the
`DataErasureJob` loop, the twin-cluster service→table mapping, and both docstring claims.
**Taken on trust:** the sequencing decision for the capabilities endpoint, and the statement that
MaterialSync is referenced nowhere in `stingtools-core` (I checked the other five clients myself).

**Third pass (2026-08-02, runtime).** The `CreateTable` sweep was re-verified twice and was correct
both times — but it was the wrong question, and re-running it more carefully could never have
revealed that.

**Fourth pass (2026-08-02, correction).** The third pass then drew the right conclusion from the
wrong evidence. It credited the *runtime pass* with overturning B1, when the runtime pass ran on the
`EnsureCreated` path and could not have failed. The thing that actually overturns B1 is **reading
`PlatformSchemaPatcher` and noticing where `Program.cs` calls it** — a static fact that holds in
Production, which the third pass did record (§2 of B1-R) but buried under a runtime result that was
true by construction. Booting the thing was still the right instinct; the error was in what the boot
was allowed to prove. One measurement in this pass is genuinely new: [Appendix A](#appendix-a--the-migration-measurement-expected-behaviour-not-a-defect).

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

## B1 — VOID (retained as the audit trail of how it was retired)

> **Superseded by the fifth pass.** Everything below is left in place because it is *how* B1 was
> retired, and because the underlying measurements (the `CreateTable` sweep, the patcher call-site
> reading, the service→table mapping) are all still correct. But the conclusion it reaches —
> "restated as a migration-hygiene finding" — **no longer holds.** There is no hygiene defect:
> production runs `EnsureCreated` + idempotent patchers *by design* (`render.yaml:51-52`), the
> migration set is not the schema mechanism, and its incompleteness is the documented, intended state.
> Read this section as history, not as an open finding. Nothing in it requires action.

The `CreateTable` sweep reproduces and is accurate. The question it answers is not the one B1 asked.
**Three** mechanisms in this codebase can put a table in a database, and the sweep consulted one:

| # | Mechanism | Invoked at | Covers |
|---|---|---|---|
| 1 | EF model — `OnModelCreating` → `…ModelSnapshot.cs` | `creator.CreateTables()`, `Program.cs:1559` (only when `useEnsureCreated`, `:1522`) | the whole model — 134 tables on a virgin Development boot |
| 2 | Migration folder — `Data/Migrations/` | `db.Database.Migrate()`, `Program.cs:1564` (Production path) | 78 files, of which **EF can see 2** |
| 3 | `PlatformSchemaPatcher` — raw `CREATE TABLE IF NOT EXISTS` | `Program.cs:1585`, **outside** the `if/else`, so **both** branches | 19 tables |

Mechanism 3 is the one B1 missed, and it is why the verdict flips. Adding it as a column:

| Table | Migration `CreateTable` | Patcher | In EF model | Present in prod? |
|---|---|---|---|---|
| ApprovalChains | ✅ `20260513000000_AddPost204ServerEnhancements` | — | yes | yes |
| AssetDataSheets | ✅ `20260513100000_AddAssetDataSheetEngine` | — | yes | yes |
| AssetDataSheetTemplates | ✅ `20260513100000_AddAssetDataSheetEngine` | — | yes | yes |
| CdeContainers | ✅ `20260517000003_AddCdeFolderHierarchy…` | — | yes | yes |
| MfaEnrollments | ✅ `20260517000000_AddBoq…SsoMfaDashboard` | — | yes | yes |
| **DeviceTwins** | ❌ none | ✅ `:91` | yes | **yes — patcher** |
| **TelemetryPoints** | ❌ none | ✅ `:115` | yes | **yes — patcher** |
| **TwinRules** | ❌ none | ✅ `:126` | yes | **yes — patcher** |
| **TwinAlerts** | ❌ none | ✅ `:143` | yes | **yes — patcher** |
| **WorkOrders** | ❌ none | ✅ `:162` | yes | **yes — patcher** |
| **GlobalIdRegistry** | ❌ none | ✅ `:260` | yes | **yes — patcher** |
| **DocumentApprovals** | ❌ none | ❌ none | yes | **model only — see M3** |

> ~~On a database built by `dotnet ef database update`, every action reaching one throws
> `relation "X" does not exist`.~~
>
> ~~The inference fails because no environment is built by `dotnet ef database update`.~~
>
> **Both of those are superseded.** The first was wrong; the second was *right about prod for the
> wrong reason* — it leaned on a runtime pass that could not have failed. The correct statement is
> narrower and stronger: **the patcher call site is unconditional across environments**
> (`Program.cs:1585` is outside the branch at `:1538`/`:1562`, guarded only by
> `db.Database.IsRelational()`), so any instance that boots at all has those six tables. That is read
> off the source, holds in Production, and needs no runtime pass and no prod query to assert.

**So B1 is a hygiene finding, not an availability one.** One logical schema is defined in three
places that no build step reconciles. Nothing follows from "no migration creates X" in either
direction — as the table shows, the five B1 rated ✅ are precisely the five a migrations-only
database *lacks* (B1-R §3), and the six it rated ❌ are present everywhere. The real defect is that
the three sources can disagree silently; `SchemaDriftChecker` (`Program.cs:1610`) is the only thing
catching that, at boot, per environment.

**The one table where they do disagree is `DocumentApprovals`** — in the model, in no migration, in
no patcher. That is the residue of B1 worth acting on, and it is measured in [Appendix A](#appendix-a--the-migration-measurement-expected-behaviour-not-a-defect).

### The twin cluster — service→table mapping, which stands

The review correctly warned that these controllers reach tables through services, so I traced each
service to the tables it actually touches rather than assuming:

| Controller | Route | Reaches | Creating mechanism |
|---|---|---|---|
| DeviceTwins | `…/twins` | `IDeviceTwinService` → `DeviceTwins`, `TelemetryPoints` | patcher `:91`, `:115` |
| TwinAlerts | `…/twins/alerts` | `_db.TwinAlerts` directly | patcher `:143` |
| TwinRules | `…/twins/rules` | `_db.TwinRules` directly | patcher `:126` |
| TwinProvisioning | `…/twins/provision` | `ITwinProvisioningService` → `DeviceTwins`, `TaggedElements` | patcher `:91`; model |
| TelemetryIngest | `…/telemetry` | `IDeviceTwinService` → `DeviceTwins`/`TelemetryPoints`; `ITwinRuleEvaluator` (`TwinRuleEngine`) → `DeviceTwins`, `TwinAlerts`, `TwinRules` | patcher, all |

The mapping is correct and stands. The conclusion I drew from it — *"none of them has a table to
write to"* — does not: every table in that column is created by the patcher on every boot, in both
branches.

### Re-assessed on their own merits, not as B1-blocked

Dropping the B1 premise entirely, and *not* relying on the by-construction runtime pass, what is
actually established about these five:

- **Schema** — present in any booted environment, from the unconditional patcher call site. Static,
  Production-valid.
- **DI** — every injected service (`IDeviceTwinService`, `ITwinRuleEvaluator`, `ITwinProvisioningService`)
  is registered; static check of `Program.cs`. This matters more than usual here: the known-prior
  failure mode is a controller 500ing on every call from a missing registration.
- **Auth + tenant scoping** — `[Authorize]` on all five, `TenantId` filtering as per the triage table.
- **Client** — none, in any of the six clients checked.

That is a **product** gap — a working backend with no consumer — not a platform one, and it is Tier 2
for that reason alone. The dev-path pass is corroboration that they serve traffic and persist writes;
it is not the basis of the rating.

**Not yet established, and not claimed:** populated-table behaviour for `TwinAlerts`/`WorkOrders`
(both read 200 over *empty* tables), and cross-tenant isolation (single-tenant demo login).

**STOP item still respected.** I have not authored migrations for the six. That remains correct, for
a plainer reason than before: the tables already exist, so a migration adding them would be a no-op
at best and a `42P07` at worst. If the hygiene finding is ever actioned, the unit of work is
reconciling the three sources — not adding six files to a folder EF does not read.

---

## B1-R — why the "unmigrated" tables exist anyway

Three measurements, all local, in the order I made them.

**1. The migration folder is inert.** EF discovers migrations by reflecting over types carrying
`[Migration("id")]`, normally emitted into a `.Designer.cs` companion. In
`src/Planscape.Infrastructure/Data/Migrations/`:

```
migration .cs (excl. Designer/snapshot) : 78     ← previous revision said 80; it counted
.Designer.cs companions                 :  2       the 2 Designer files as migrations
files carrying [Migration(              :  2     (MeetingMedia, SustainabilitySnapshots)
```

So `Migrate()` / `ef database update` applies **2 of 78** — confirmed directly in
[Appendix A](#appendix-a--the-migration-measurement-expected-behaviour-not-a-defect) by `dotnet ef migrations list`, which
enumerates exactly those two. This is not new breakage —
[`docs/adr/0001-schema-management.md`](adr/0001-schema-management.md) (Accepted, 2026-06-04) records
it and **adopts `EnsureCreated` + idempotent patchers as the official, supported mechanism**. It
measured 0 of 75 then; the count has since drifted to 2 of 78. I did not consult that ADR in either
earlier pass, which is how the wrong inference survived two reviews.

**2. A patcher creates all six, on every boot, in both branches. ← this is the load-bearing one.**
`PlatformSchemaPatcher.cs` issues `CREATE TABLE IF NOT EXISTS` for `DeviceTwins` (`:91`),
`TelemetryPoints` (`:115`), `TwinRules` (`:126`), `TwinAlerts` (`:143`), `WorkOrders` (`:162`) and
`GlobalIdRegistry` (`:260`). `Program.cs:1585` calls it **outside** the `if (useEnsureCreated) … else
Migrate()` split, guarded only by `db.Database.IsRelational()` — the in-file comment says it runs in
both branches precisely because "the hand-authored `Migrate()` set is also incomplete".

This single fact is the whole of the B1 correction, and it is **static**: it is true of Production
because of where the call sits, not because of anything observed on a dev stack. Measurements 3 and 4
below are supporting colour; this is the argument. The previous revision had it — as item 2 of a list
— and then credited the overturn to the runtime pass, which is the error this pass fixes.

One ordering caveat, since it matters for M3: the patcher runs at `:1585`, **after** `Migrate()` at
`:1564`. It cannot rescue a migration that throws first — the boot is already dead. And
`SchemaDriftChecker` runs later still (`:1610`), strict by default outside Development
(`:1604-1609`, nothing overrides it in `appsettings*.json` or `render.yaml`). So the boot sequence is
**migrate → patch → assert-no-drift**, and a Production instance that is serving traffic has passed
all three. That last point is worth more than any single-table argument: on a booted non-dev
instance, *every* table in the EF model exists, or the process would have exited at `:1610`.

**3. Fresh-database boot on the `Migrate()` path.** *(Fifth pass: re-labelled. This was headed
"on the Production path", which is wrong — production sets `PLANSCAPE_USE_ENSURE_CREATED=true` and
never takes this branch. It exercises the `Migrate()` branch, which no deployed service reaches. The
measurement stands; it demonstrates why the flag exists rather than a production failure mode.)*
Empty DB (0 tables), API booted with `ASPNETCORE_ENVIRONMENT=Production` **and the env var unset**,
so it takes the `Migrate()` branch:

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

**4. Independent replication, on a separate scratch stack.** The three measurements above were
re-run from scratch in a second session against a *different* database and a *different* API process
— a throwaway `postgres:16` on port 5433 and the API on `:5099`, with the shared compose stack left
untouched — to check they were not artefacts of one environment. They reproduce exactly: 2 of the
migration files carry `[Migration(`; `PlatformSchemaPatcher` creates the six at the same six line
numbers; all six exist afterwards.

The replication also booted the **Development** path against a virgin database:

```
createdb planscape3            -> 0 tables, `ef database update` never run against it
boot API (Development)         -> starts clean, /health 200, 0 unhandled exceptions
tables in public               -> 134, including all six disputed tables
"__EFMigrationsHistory"        -> ERROR: relation "__EFMigrationsHistory" does not exist
```

> **Corrected reading.** The previous revision called this "the decisive test" and glossed it as *"on
> the path every live environment actually takes, the migration mechanism never runs."* The first
> half of that is unsupported — nothing here establishes which path a deployed environment takes, and
> `useEnsureCreated` is `IsDevelopment() || PLANSCAPE_USE_ENSURE_CREATED=true` (`:1522`), which is
> false for a default Production deployment.
>
> What it *does* establish, cleanly: on the `EnsureCreated` path the migration mechanism never runs
> at all (no history table), and `creator.CreateTables()` materialises the **entire** model — all 134
> tables — so the presence of the six disputed tables here is **true by construction** and carries no
> information about which mechanism would have created them. Read it as a description of the dev
> path, not as evidence about prod. The evidence about prod is §2.

*(One incidental defect found while doing this, recorded but not fixed and not a Phase 6A item:
running `ef database update` **first** and then booting Development is a hard startup crash —
`CreateTables()` is not conditioned per-table, so it aborts on `42P07: relation
"SustainabilitySnapshots" already exists`. The two supported paths are each fine alone; mixing them
bricks the boot.)*

**So the Backed column was measuring the wrong thing.** Every table B1 marked ❌ is created by the
patcher; the five it marked ✅ are precisely the five a migrations-only database *lacks*. Migration
presence carries no information about schema presence in this codebase, in either direction.

Two things follow, and only the first is safe:

- **Sound.** A database built *only* from this repo's migrations cannot run the app — it has no
  `Tenants`, and the drift assert (`:1610`, strict outside Development) refuses the boot. So no
  environment that is currently serving traffic got its schema from the migration folder alone. That
  is a statement about code and boot order, and it needs no prod query.
- **Not sound, and withdrawn.** It does *not* follow that the six exist "because every booted
  environment took the `EnsureCreated` path". Whether a given deployment sets
  `PLANSCAPE_USE_ENSURE_CREATED` is not established here. The six exist for a simpler reason that
  doesn't depend on which branch is taken: **the patcher call site is outside the branch.**

### The by-construction exchange — where each side landed

Worth recording, because the same mistake was available to both sides and the correction came from
neither's original argument.

| Claim | Origin | Verdict |
|---|---|---|
| No migration creates the six tables | review, replicated here | **True**, and inert — it licenses no conclusion about any database. |
| Every environment is built by `ef database update`, so the tables must be absent | review | **Withdrawn by the reviewer.** No environment is; that path applies 2 of 78 (M3). |
| The runtime pass shows the tables exist, so B1 is refuted | this doc, third pass | **Withdrawn here.** The pass ran on the `EnsureCreated` path where all 134 model tables exist by construction; it could not have come out otherwise. |
| The patcher creates six of them at an unconditional call site | reviewer's correction; present but buried in §2 of the third pass | **This is the actual answer**, and it is static and Production-valid. |

The instructive part is that a static finding survived two careful re-verifications, was then
"overturned" by a runtime result that was true by construction, and was finally settled by reading
twenty lines of `Program.cs` around the call site. Neither re-grepping nor booting harder would have
got there.

**The real gap is not six missing migrations — it is that 76 of 78 migration files are invisible to
EF.** That is an existing, documented, accepted architectural decision (ADR 0001), not a Phase 6A
finding, and it is out of scope here. Flagging it, not fixing it.

---

## Appendix A — the migration measurement: expected behaviour, not a defect

> **Read this framing first.** The measurement below is **correct and reproducible**, and it is
> retained for that reason. What changed in the fifth pass is what it *means*.
>
> It was originally run to answer "does the **Production** schema path die, and where?" — on the
> assumption that production calls `db.Database.Migrate()`. **Production does not call `Migrate()`
> at all.** `render.yaml:51-52` sets `PLANSCAPE_USE_ENSURE_CREATED=true`, so production takes the
> `EnsureCreated` + patcher branch (`Program.cs:1538-1559`). The configuration used below —
> `PLANSCAPE_USE_ENSURE_CREATED` **unset** — is therefore not the production path. It is a path no
> deployed service takes.
>
> So the headline result — **"2 of 78 migrations visible; `dotnet ef database update` yields 2 tables
> and no `Tenants`"** — is not a finding. It is precisely what `render.yaml`'s own comment predicts:
>
> > *"…the EF migration set is incomplete BY DESIGN … without this flag a FRESH database takes the
> > `Migrate()` branch and comes up nearly empty."*
>
> The measurement **confirms the documented rationale for the flag**. Re-labelled accordingly:
> **expected behaviour, explained by `render.yaml`.** No action follows from it, and the 78 inert
> migrations are vestigial rather than broken.
>
> Two smaller conclusions below also fall away: the `20260501000000` "latent trap" cannot fire on any
> deployed service, because no deployed service runs migrations at all; and the `DocumentApprovals`
> question is answered by `EnsureCreated` materialising the whole model, not by migration order.
>
> The one thing in this area that *is* live is filed separately as **#545** — RLS is unreachable by
> the `EnsureCreated` mechanism, so the obvious-looking fix (add the missing `[Migration]` attribute)
> would deploy and change nothing.

**The original question.** `20260501000000_AddTenantIdToAllScopedEntities` runs an **unguarded**
`UPDATE "DocumentApprovals"` (table listed at `:37`, SQL emitted at `:62-65`) — and
`DocumentApprovals` is created by no migration and no patcher. The patcher runs at `Program.cs:1585`,
*after* `Migrate()` at `:1564`, so it cannot rescue a migration that throws first. Does the
`Migrate()` schema path therefore die, and if so where?

**Configuration — the `Migrate()` path, which no deployed service takes.** Fresh `postgres:16-alpine`, empty
(`0` tables in `public`), `ASPNETCORE_ENVIRONMENT=Production`, `PLANSCAPE_USE_ENSURE_CREATED` unset,
running `dotnet ef database update` **alone — not the app**.

> Recorded because it strengthens the isolation rather than weakens it: `dotnet ef` resolves the
> context through `PlanscapeDbContextFactory` (`IDesignTimeDbContextFactory`), and logged
> *"An error occurred while accessing the Microsoft.Extensions.Hosting services. Continuing without
> the application service provider."* — it failed to build the app host (missing `Jwt:Key`) and used
> the factory. So **no line of `Program.cs` executed**: no `EnsureCreated`, no patcher, no drift
> check. The factory reads only `CONNECTION_STRING`/`PG*`, which also means `ef database update` is
> environment-independent — the two env vars above cannot influence it. This is the migration
> mechanism in isolation, which is exactly what was asked for.

**Result — it completes.**

```
$ dotnet ef migrations list
20260605041920_MeetingMedia (Pending)
20260626203153_SustainabilitySnapshots (Pending)      ← 2 of 78 files

$ dotnet ef database update
... CREATE TABLE "SustainabilitySnapshots" ... ; INSERT INTO "__EFMigrationsHistory" ...
Done.                                                  ← exit 0
```

End state of the database:

| | |
|---|---|
| tables in `public` | **2** — `SustainabilitySnapshots`, `__EFMigrationsHistory` |
| `__EFMigrationsHistory` rows | `20260605041920_MeetingMedia`, `20260626203153_SustainabilitySnapshots` |
| `DocumentApprovals` | **absent** |
| `Tenants` | **absent** |

**It does not stop at `20260501000000` — it never reaches it.** That file carries no `[Migration]`
attribute and has no `.Designer.cs` companion, so EF does not enumerate it. Its `Up()` never runs and
its unguarded `UPDATE` never executes. The two migrations EF *can* see are both idempotent by
construction: `MeetingMedia` is `ALTER TABLE IF EXISTS … ADD COLUMN IF NOT EXISTS` (its own comment
notes a plain `AddColumn` "would therefore fail at this migration on a fresh DB"), and
`SustainabilitySnapshots` is a plain `CreateTable`.

**The counterfactual, also measured.** If that migration were made discoverable — which is precisely
what "fixing the migration hygiene" would do — it fails, but **not** at `DocumentApprovals`. Step 1
(`:47-48`) calls `AddNullableTenantId`, which emits an unguarded `mb.AddColumn<Guid>`, and its first
table is `TaggedElements`, created by no migration either. Both statements run against the
just-migrated database:

```sql
ALTER TABLE "TaggedElements" ADD COLUMN "TenantId" uuid NULL;
  ERROR:  relation "TaggedElements" does not exist
UPDATE "DocumentApprovals" c SET "TenantId" = p."TenantId" FROM "Documents" p WHERE …;
  ERROR:  relation "DocumentApprovals" does not exist
```

So `DocumentApprovals` is not a special case — it is one of the **26** tables that migration touches,
none of which a migrations-only database has. The migration is unrunnable from its first statement.

**What this settles, and what it leaves open.**

- **The hazard is latent, not live.** No boot can hit it today, because EF cannot see the migration.
  It is armed the moment someone regenerates the migration set or hand-adds the `[Migration]`
  attribute — the well-intentioned fix for the hygiene finding is exactly the trigger. Worth a
  comment in the file; that is a one-line change and is *not* in this docs-only PR.
- **`DocumentApprovals` on a live instance.** Still model-only — but it is in `OnModelCreating`
  (`PlanscapeDbContext.cs:148`) and in the snapshot, and `SchemaDriftChecker` asserts the full model
  against the live schema at `:1610`, strict by default outside Development. So any non-dev instance
  that is serving traffic has the table, or it would have exited at boot. That is the same
  code-and-boot-order argument as B1-R §2, and it is as far as this can be taken without a prod query.
- **The "2 of 78 + drift-checker" pairing is the actual system.** Migrations are inert, the patcher
  covers 19 tables, the model covers 134, and the only thing reconciling them is a boot-time assert.
  That is the hygiene finding, now with a measured failure mode attached.

---

## Amended triage table

Legend — **Schema from**: which of the three mechanisms creates the controller's tables (B1). The
previous revision labelled this column "Backed / ✅ patcher" for every row, which was wrong for rows
2–6: those are migration-created, not patcher-created. Corrected and individually verified below.
**DI**: every injected service resolves (static check of `Program.cs`). **Dev runtime**: status code
from the pass below — measured on the `EnsureCreated` path, so it evidences *reachability*, not
schema provenance.

| # | Controller | Ln / actions | Auth gate | Tenant scoping | Schema from | DI | Dev runtime | Verdict |
|---|---|---|---|---|---|---|---|---|
| 1 | **DataRights** | 114 / 3 | `[Authorize(Roles="Owner,Admin")]` | `ITenantContext` | model | ✅ | 200 | **Tier 1** — DR1 |
| 2 | **CdeContainers** | 287 / 6 | `[Authorize]` + role gate `:255` | `TenantId` + `ProjectMembers` | migration | ✅ | 200 | **Tier 1** |
| 3 | **ApprovalChains** | 317 / 4 | `[Authorize]` + `[ProjectAccess]` | `[ProjectAccess]` + `TenantId` | migration | ✅ | 200 | Tier 2 — **undecided, C1** |
| 4 | **AssetDataSheets** | 276 / 7 | `[Authorize]` + `[ProjectAccess]` | `RequireProjectMemberAsync` | migration | ✅ | 200 | Tier 2 |
| 5 | **OfflineManifest** | 227 / 4 | `[Authorize]` | `TenantId` | migration | ✅ | 400¹ | Tier 2 — mobile-only by nature |
| 6 | **Mfa** | 256 / 7 | `[Authorize]` (+`TenantAdmin` on one) | via `Users` | migration | ✅ | 200 | Tier 2 — works; fix M1 |
| 7 | **WorkOrders** | 98 / 3 | `[Authorize]` | global filter (`ITenantScoped`) | **patcher** `:162` | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2**² |
| 8 | **GlobalIdRegistry** | 275 / 6 | `[Authorize]` | `TenantId` | **patcher** `:260` | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2**² |
| 9 | **DeviceTwins** | 108 / 5 | `[Authorize]` | `TenantId` | **patcher** `:91`, `:115` | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2**²; fix M2 |
| 10 | **CaseStudy** | 150 / 1 | `[Authorize]` | `TenantId` | model (read-only) | ✅ | 200 | Tier 3 — sales tool |
| 11 | **TwinAlerts** | 53 / 3 | `[Authorize]` | `TenantId` | **patcher** `:143` | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2**² |
| 12 | **TwinRules** | 129 / 4 | `[Authorize]` | `TenantId` + `Projects` | **patcher** `:126` | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2**² |
| 13 | **TwinProvisioning** | 75 / 2 | `[Authorize]` | via service | **patcher** `:91` | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2**² |
| 14 | **TelemetryIngest** | 98 / 1 | `[Authorize]` | via services | **patcher** (all) | ✅ | 200 | ~~Tier 3 blocked~~ → **Tier 2**² |
| — | ~~MaterialSync~~ | — | — | — | — | — | — | **Removed — shipped as PR #542** |

¹ `400` is ordinary model validation, not a schema failure: `{"errors":{"deviceId":["The deviceId
field is required."]}}`. The pass sent no query string. Route shape, not breakage.

² **Re-rated on their own merits, not on the runtime pass.** The basis is the static one in
[B1](#re-assessed-on-their-own-merits-not-as-b1-blocked): an unconditional patcher call site
(`Program.cs:1585`) puts the tables on any booted instance including Production, DI resolves, auth
and tenant scoping are as tabulated, and no client exists in any of the six checked. The dev-path
200s corroborate reachability; they are not what moves the row.

**DI is clean across all fourteen.** The known-prior failure mode (a controller 500ing on every call
from a missing registration, misreported by the browser as CORS) does not apply to any of them.

**Seven rows moved off Tier 3.** They were rated blocked on a schema premise that does not hold. They
are now ordinary Tier 2: the backend works, there is no client. That is a *product* gap, not a
*platform* one, and it is a materially different piece of work. Row 3 is the exception — it is Tier 2
on the same grounds but its *scope* is undecided pending C1.

---

## Other findings

**C1 — ApprovalChains vs the flat path: UNDECIDED, and "duplication" was too strong.**

Downgraded from "verified". The client half reproduces: `Planscape/src/api/endpoints.ts` drives
`DocumentsController`'s flat approval (`POST …/approvals` at `:1005`, `PUT …/approval/{id}` at
`:1012`), and `ApprovalChainsController` has no client. What does not reproduce is the consequence I
attached to it — *"or one document gets two approval records."*

The gate is `DocumentsController.CheckApprovalGate` (`:1544-1566`), and it is an **OR**:

```csharp
var hasLegacyApproval  = await _db.DocumentApprovals.AnyAsync(a => … a.Status == "APPROVED"
                             && (a.RevisionSnapshot == null || a.RevisionSnapshot == currentRevision));   // :1551-1553
var hasCompletedChain  = await _db.ApprovalChains.AnyAsync(c => … c.Status == "COMPLETED");               // :1554-1555
if (!hasLegacyApproval && !hasCompletedChain) return BadRequest(…);                                       // :1556
```

Either one alone satisfies the transition; the docstring says so deliberately (`:1536-1538` — "Documents
may use the legacy single-approver path or the new multi-step chain **interchangeably**"). They are
alternative satisfiers of one gate, not competing records, and nothing here writes two.

**What remains, and it is a real decision:** two mechanisms for one concept, one with a client and one
without. Which the UI drives is a product call, not a correctness bug — and the interchangeable gate
means shipping a chain UI would not corrupt anything, it would just leave two supported routes to the
same state.

**What M3 contributes.** Schema provenance does not break the tie: `ApprovalChains` is
migration-created, `DocumentApprovals` is the one table in B1's list that **neither** a migration nor
the patcher creates — model-only. Both nonetheless exist on any booted non-dev instance (the drift
assert would have refused the boot otherwise), so neither path is at risk and neither is favoured.

**To close it** someone has to pick the canonical mechanism and say whether the OR stays. That is the
one open item this triage cannot settle by measurement.

**M1 — Mfa docstring is stale.** `:13` says "Production implementation would use a TOTP library (e.g.
Otp.NET)". It already does: `using OtpNet;` (`:5`), `IDataProtector` (`:23`),
`totp.VerifyTotp(req.Code, out _, new VerificationWindow(2, 2))` (`:119`).

**M2 — DeviceTwins docstring is false.** `:11-12` claims it "Powers the mobile Live tab (RAG list)".
There is no Live tab in `Planscape/app/`; the only case-insensitive match is `de`**live**`rables`.

Both to be fixed in whichever PR next touches those files, per the review.

---

## Runtime pass (measured on a stack equivalent to production)

> **Reinstated in the fifth pass.** The previous revision demoted this pass on the grounds that
> `docker-api-1` runs `ASPNETCORE_ENVIRONMENT=Development` and therefore takes a schema branch that
> production would not. **That reasoning was wrong and is retracted.** `render.yaml:51-52` sets
> `PLANSCAPE_USE_ENSURE_CREATED=true` on the production API (and `:104-105` on the worker), so
> production evaluates `useEnsureCreated` to **true** as well and lands on the *same*
> `creator.CreateTables()` call at `Program.cs:1559`. Development reaches it via `IsDevelopment()`;
> production reaches it via the env var. Same branch, same mechanism, same resulting schema.
>
> **Valid conclusions:** the fourteen controllers are routable, their DI graph resolves at runtime,
> their handlers do not throw, their write paths persist rows — and, because the schema is built the
> same way in both places, these hold for production too, modulo data.
>
> **Still not established, and these are real limits:** the pass ran against **empty tables** and a
> **single-tenant** login. It says nothing about behaviour at volume, nothing about cross-tenant
> isolation, and nothing about rows that already exist in production. "Broadly representative" is a
> statement about the *schema*, not about the data or the tenancy.

**Closed.** The blocker did not reproduce. `Start-Process "C:\Program Files\Docker\Docker\Docker
Desktop.exe"` brought the daemon up first try — `docker version` → server `29.4.3`, `docker-desktop`
distro `Running` — with no elevation and no dialog. I cannot say why the previous revision's two
attempts failed where this one succeeded, so I am not going to guess; what is now established is
that the "needs a human to accept a blocking dialog" diagnosis was **not** the barrier.

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
2. *Whether the six tables really are absent* — **not resolved by this pass**, and it was wrong to
   claim it was. On the `EnsureCreated` path they are present by construction. The question is
   answered instead by B1-R §2 (the patcher's call site is outside the environment branch, so they
   are present in Production too) — a static argument that does not need this pass at all.
3. *Whether anything 500s for a reason not visible statically* — nothing 500s, on this path.

**Re-measured independently.** The five disputed reads were re-run in a second session against the
scratch stack of B1-R §4 — a different database, a different API process, a fresh login — rather than
carried over from the first run:

```
work-orders          200   relation-missing=0
global-id-registry   200   relation-missing=0
twins                200   relation-missing=0
twins/alerts         200   relation-missing=0
twins/rules          200   relation-missing=0
```

Same result on a database that has never seen a migration — which rules out the first stack's seed
data as the explanation, and nothing more. Both stacks booted Development, so both had the full model
materialised; the replication controls for environment artefacts, not for the by-construction
limitation above.

**Residual limits, stated rather than papered over.** The pass exercises each controller's primary
action, not all 46 actions; destructive ones (`DataRights erase`, alert `ack`/`resolve`) were
deliberately not fired. `TwinAlerts` and `WorkOrders` returned 200 over **empty** tables — their read
path executes, their populated behaviour does not. And the single-tenant demo login does not exercise
the cross-tenant isolation each row claims; that wants a two-tenant fixture and is worth doing
separately.

**And the limit this revision adds, which is the important one:** every number in this section comes
from the `EnsureCreated` path. Nothing here — not the 200s, not the row counts, not the replication —
is evidence about a schema built any other way. Where this document makes a claim about Production it
rests on `Program.cs` source and boot order (B1-R §2, M3), never on this pass.

---

## Still open after this pass

| Item | Why it is open | What would close it |
|---|---|---|
| **C1 — canonical approval mechanism** | A product decision; both paths work and the gate accepts either. | Someone picks one, and says whether the OR stays. |
| ~~Migration hygiene (B1, restated)~~ | **CLOSED — VOID.** Not a defect. Production runs `EnsureCreated` + patchers by design (`render.yaml:51-52`); the migration set is not the schema mechanism and its incompleteness is intended. ADR 0001 documents it. | Nothing. Closed. |
| ~~`20260501000000` is a latent trap~~ | **CLOSED — cannot fire.** It was only a trap on the `Migrate()` path, and no deployed service takes that path. | Nothing. Closed. |
| **RLS is unreachable, not merely unapplied** | Tracked separately as **#545**, not here. Because production never calls `Migrate()`, adding the missing `[Migration]` attribute would deploy and change nothing — the obvious fix is a no-op. | Sign-off on one of the three options in #545. Not started; no code written. |
| **Populated + cross-tenant behaviour** | Dev pass used empty tables and a single-tenant login. | A two-tenant fixture with seeded rows. |
