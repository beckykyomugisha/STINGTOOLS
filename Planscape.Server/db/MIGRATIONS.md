# Phase 189 — schema deployment (platform spine · meeting viewer · digital twin)

New tables: `PlatformEvents`, `MeetingSessions`, `MeetingViewerParticipants`,
`MeetingSnapshots`, `DeviceTwins`, `TelemetryPoints`, `TwinRules`, `TwinAlerts`,
`WorkOrders`.

## How schema is applied in this repo

The app does **not** rely solely on EF migrations (the migration set is
acknowledged incomplete and the model snapshot is stale — see `Program.cs`).
Schema is materialised by:

1. **Fresh DB (dev / `PLANSCAPE_USE_ENSURE_CREATED=true`)** — `CreateTables()`
   builds everything from `OnModelCreating`, so the new tables appear with no
   action.
2. **Pre-existing DB** — `PatchDevSchemaAsync` (columns) and the new
   `PlatformSchemaPatcher.ApplyAsync` (these tables) run idempotent
   `CREATE TABLE IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS` on every startup.

So **no manual step is required for the relational tables** — they self-heal on
boot in both branches.

## Canonical path (when the EF model is next stabilised)

```bash
cd Planscape.Server
dotnet ef migrations add Phase189Platform \
  --project src/Planscape.Infrastructure --startup-project src/Planscape.API
dotnet ef database update \
  --project src/Planscape.Infrastructure --startup-project src/Planscape.API
```

Once a real migration covers these tables, delete `PlatformSchemaPatcher` and
its call in `Program.cs` (the patcher is a bridge, not the source of truth).

## TimescaleDB (required for telemetry scale)

EF cannot emit Timescale DDL, and a hypertable needs the partition column in
the PK. Run **once**, after the table exists, on a Postgres with the
`timescaledb` extension:

```bash
psql "$PLANSCAPE_DB" -f db/timescale_telemetry.sql
```

Until run, `TelemetryPoints` behaves as a plain table — correct, just
unpartitioned. The script is idempotent (safe to re-run).
