# ADR 0001 — Database schema management: EnsureCreated + idempotent patcher (not an EF `Migrate()` baseline)

- **Status:** Accepted (Pre-merge Gate 2, branch `claude/upbeat-cori-vdOPA`)
- **Date:** 2026-06-04
- **Deciders:** Pre-merge gate review
- **Supersedes / relates to:** the "production should regenerate the migration
  set" TODO comments in `Planscape.Server/src/Planscape.API/Program.cs`
  (the `// ── Database schema + seed ──` block) and
  `PlatformSchemaPatcher.cs`.

## Context

`Planscape.Server` materialises its PostgreSQL schema at startup from two
surfaces:

1. **`EnsureCreated` / `RelationalDatabaseCreator.CreateTables()`** — on a fresh
   DB (or in Development), the entire EF model in `OnModelCreating` is
   materialised directly. This always matches the current entity classes.
2. **`PatchDevSchemaAsync` + `PlatformSchemaPatcher.ApplyAsync`** — idempotent
   `ADD COLUMN IF NOT EXISTS` / `CREATE TABLE IF NOT EXISTS` statements that
   bring **pre-existing** databases (where `EnsureCreated` short-circuits once
   `Tenants` exists) up to date with entities/columns added since the DB was
   first created.

There **is** a folder of ~75 migration `.cs` files under
`src/Planscape.Infrastructure/Data/Migrations/`, **but they are not usable as an
EF migration history**:

- They are `partial class … : Migration` with `Up()`/`Down()` bodies but carry
  **no `[Migration("id")]` attribute** and have **no `.Designer.cs` companions**.
  EF Core builds its applicable-migration list by reflecting over types that
  carry the `MigrationAttribute` (normally emitted into the Designer file).
  Without it, `Database.Migrate()` sees **zero** migrations and applies nothing.
- Verified on this branch:
  `ls *.Designer.cs` → 0 files; `grep -rl "[Migration(" .` → 0 files.

So today, in the non-Development branch, `db.Database.Migrate()` is effectively a
no-op against these files; the schema only exists because of surface (1)/(2).

## Decision

**Adopt the EnsureCreated + idempotent-patcher path as the OFFICIAL, supported
schema-management mechanism**, and add a startup/CI **schema-drift self-check**
that asserts the live DB matches the EF model. We do **not** introduce an EF
`Migrate()` baseline in this pass.

## Why not the EF baseline (Option A)?

A model-generated baseline (`dotnet ef migrations add InitialBaseline` from an
empty snapshot, then wire `Database.Migrate()`) was the preferred option *if it
could be made safe*. It cannot, in this pass, for one decisive reason:

**The legacy migrations contain hand-written SQL that is not representable in the
EF model snapshot, and a model-generated baseline would silently drop it.**

Verified on this branch — these features live ONLY as `migrationBuilder.Sql(...)`
in legacy migration files and appear **0 times** in
`PlanscapeDbContextModelSnapshot.cs`:

| Feature | Legacy migration | In EF model snapshot? |
|---|---|---|
| Postgres **Row-Level Security** policies (`CREATE POLICY` / `ENABLE ROW LEVEL SECURITY`) — the tenant-isolation backstop | `20260506200000_EnablePostgresRowLevelSecurity.cs` | **No (0)** |
| Audit-log **hash-chain triggers + table partitioning** (`PARTITION BY` / `CREATE TRIGGER`) — tamper-evidence | `20260501030000_AddAuditLogHashChainAndPartitions.cs` | **No (0)** |
| **GIN / tsvector** full-text indexes | `20260418000000_AddIssueCustomFields.cs` | **No (0)** |

A baseline generated from the EF model (or from `CreateTables`, which is the same
model) would produce tables + B-tree indexes but **none of the RLS policies,
partitions, triggers, or GIN indexes**. Switching the boot path to that baseline
and "retiring the patcher" would therefore be a **silent security/compliance
regression** (tenant isolation + audit tamper-evidence), and reconstructing all
of that raw SQL into a hand-edited baseline is exactly the large, destructive,
unverifiable-in-one-pass "whole-model catch-up" the gate warned against.

(Separately, the EF-recommended "baseline an existing database" pattern —
generate the baseline, then stamp `__EFMigrationsHistory` on existing DBs so
`Migrate()` skips it — solves the *idempotency/destructiveness* concern but does
**nothing** for the raw-SQL gap above. The raw SQL is the blocker, not the
table DDL.)

## How Option B is made rigorous

1. **The patcher is idempotent** — every statement is `… IF NOT EXISTS`, safe to
   run on every boot on any DB state.
2. **The patcher covers the entities this branch added** that have no applicable
   migration. Confirmed present in `PlatformSchemaPatcher.Statements`:
   - `ExternalElementMappings` (+ its 3 indexes incl. the composite unique with
     EF's 63-char truncated name `…HostDocu~`)
   - `IdempotencyRecords` (+ unique `(TenantId, Scope, Key)`)
   - `ClashRecords`, `IfcAlignmentReports` (coordination tables)
   - `HvacLoadSnapshot` / `HvacNcSnapshot` / `HvacRefrigerantSizing`
   - `GlobalIdRegistry`, `ArchiCADEventLogs`
   - `IX_ProjectModels_Tenant_Project_Hash` — the duplicate-model filtered
     unique index
   The added DDL mirrors exactly what `RelationalDatabaseCreator.CreateTables()`
   emits, verified by `pg_dump` of a CreateTables-built DB.
3. **`SchemaDriftChecker`** runs after the patcher on every boot. It enumerates
   every table + column the live EF model expects and diffs it against
   `information_schema`. Any expected-but-absent table/column is logged as
   `[schema-drift] MISSING …`. With `Database:SchemaDriftStrict=true` (or
   `PLANSCAPE_SCHEMA_DRIFT_STRICT=true`) the boot **fails**, so a CI/canary run
   converts drift into a red build instead of a production 500. This guarantees
   that if anyone adds an EF entity in future without mirroring it into the
   patcher, it is caught immediately — the one failure mode of this approach is
   now self-detecting.

## Consequences

- **Positive:** No risk of dropping RLS/partition/trigger/GIN SQL; fresh and
  pre-existing DBs both converge to the EF model; drift is no longer silent;
  the path is the one that is already battle-tested and verified to boot.
- **Negative / future work:** The raw-SQL security features (RLS policies, audit
  partitioning/triggers) still live only in the un-attributed legacy migrations
  and are applied out-of-band (RLS is additionally gated behind
  `Database:RlsEnabled` + its interceptor). A future hardening pass should fold
  those into the patcher (as `… IF NOT EXISTS` / `DO $$ … $$` guarded SQL) or
  into a properly-authored migration baseline that *includes* the raw SQL. Until
  then, operators enabling RLS must ensure the policy SQL has been applied.
- The drift checker only asserts table+column **existence** (not types,
  nullability, FKs, or indexes) — deliberately, to keep it zero-false-positive.
  Type/constraint drift is out of scope for this gate.

## Verification (this branch)

- Fresh DB (CreateTables): boots clean, `[schema-drift] OK`.
- Simulated pre-existing DB (branch tables dropped, then rebooted): patcher
  recreates them, `[schema-drift] OK`.
- Drift detection proven: a table dropped that the patcher does NOT recreate is
  reported as `MISSING TABLE`, and strict mode fails the boot.

See `docs/COORDINATION_AUDIT_FINDINGS.md` → "Pre-merge gates" for the captured
command output.
