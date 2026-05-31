# Planscape.Server — EF schema workflow (Z-2 decision)

**Status:** CreateTables / EnsureCreated + idempotent SQL patchers is the
**canonical** schema-init path. EF Core *migrations* are **non-functional dead
weight** and must not be relied on. Read this before adding an entity or running
any `dotnet ef` command.

## Audit (read-only findings)

| Check | Result |
|---|---|
| `OnModelCreating` entity configs in `PlanscapeDbContext` | **113** (`modelBuilder.Entity<…>`) + 113 `DbSet<…>` |
| Model snapshot (`PlanscapeDbContextModelSnapshot.cs`) | **~58** entities (P1-A) — independently confirmed well under 113 |
| **Gap** | **~55 entities stale** in the snapshot |
| Migration `.cs` files | 73 (+ snapshot) |
| …with `.Designer.cs` companions | **0** |
| …with `[Migration("…")]` attribute | **0** |
| …with `Up()`/`Down()` bodies | 73 |

Because **no migration carries the `[Migration]` attribute or a Designer file**,
EF's `IMigrationsAssembly` discovers **zero** migrations. `db.Database.Migrate()`
therefore recognises nothing to apply — it is a **no-op**, not a schema builder.

## Boot path (`Planscape.API/Program.cs` ~1284-1340)

```
useEnsureCreated = IsDevelopment() || PLANSCAPE_USE_ENSURE_CREATED == "true"
if (useEnsureCreated):
    if Tenants table absent -> RelationalDatabaseCreator.CreateTables()  // from OnModelCreating, always current
else:
    db.Database.Migrate()                                                // NO-OP (0 recognised migrations)
# BOTH branches then run, idempotently:
PatchDevSchemaAsync(conn)            // ADD COLUMN IF NOT EXISTS
PlatformSchemaPatcher.ApplyAsync()   // CREATE TABLE IF NOT EXISTS (platform-event/meeting/twin)
```

The code itself documents this (Program.cs:1278-1283): *"the hand-authored
migrations … are missing their .Designer.cs companions and the model snapshot is
stale, so Migrate() cannot apply them in order."*

## Does any environment deploy via migrations?

**No.** `Migrate()` is reachable only in the non-dev branch, but with 0
recognised migrations it applies nothing. No environment can be building schema
from the migration set. Fresh schema everywhere comes from `CreateTables()`
(materialised from the current `OnModelCreating`) plus the two idempotent
patchers. The migration folder + snapshot are inert artifacts.

## Decision: PATH A — formalise CreateTables-only

- **Canonical workflow:** schema = `OnModelCreating` → `CreateTables()` /
  `EnsureCreated` (fresh DB) + `PatchDevSchemaAsync` / `PlatformSchemaPatcher`
  (additive patches to existing DBs). This always matches the entity classes.
- **EF migrations are retired** (not regenerated). PATH B (regenerate snapshot)
  / PATH C (baseline) were rejected: nobody deploys via migrations, so the
  effort buys nothing and a naive `dotnet ef migrations add` would diff the
  ~55-stale snapshot and emit a ~55-table monster (the P1-A trap).
- **Migration files + stale snapshot are LEFT IN PLACE** (inert, harmless).
  Deletion is deferred — it is irreversible and a fresh prod that ever flips to
  the `Migrate()` branch with a real migration set would depend on history.
  Delete only after confirming no environment's `__EFMigrationsHistory` is
  authoritative (see Flag below).

## How to add a new entity (going forward)

1. Add the entity class under `Planscape.Core/Entities`.
2. Add `public DbSet<T> Ts => Set<T>();` to `PlanscapeDbContext` and configure
   it in `OnModelCreating`.
3. **Do NOT run `dotnet ef migrations add`** against the stale snapshot.
4. Fresh DBs get the table from `CreateTables()`. For **existing** DBs, add an
   idempotent `CREATE TABLE IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS` to
   `PlatformSchemaPatcher` / `PatchDevSchemaAsync` so deployed databases pick it
   up. This is exactly what **Z-1** did for the Photo DbSets (added the DbSets,
   no migration) — consistent with this workflow.

## ⚠ FLAG — verify before any further change

The non-dev branch calls `db.Database.Migrate()`. Today that is a no-op, so a
**fresh** non-dev / production database would get **no tables** from it — it
relies on `PLANSCAPE_USE_ENSURE_CREATED=true` (or a pre-existing schema + the
patchers). Confirm production's env var / DB-provisioning before changing the
boot path. A safe follow-up (separate, sign-off-gated PR) is to make the non-dev
branch use the same `CreateTables()` + patcher path so the boot is honest; this
PR does **not** change boot behaviour. No destructive migration was run; no
migration files were deleted.
