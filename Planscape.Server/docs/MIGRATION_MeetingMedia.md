# Migration: `MeetingMedia` (WS3 / P2)

Adds two **nullable** columns to `MeetingSessions` for the live-meeting active surface.

| Column | Type | Null | App default | Purpose |
|---|---|---|---|---|
| `ActiveSurface` | text | **NULL** | `'model'` (in app logic) | Surface every client shows: `model` \| `document` \| `screen` |
| `ActiveDocumentId` | uuid | NULL | — | Shared `DocumentRecord` id when `ActiveSurface = document` |

**Nullable, app-defaulted.** `MeetingSession.ActiveSurface` is `string?` and
`MeetingRoomController.ToDto` coalesces `null → "model"`. The column is therefore added
**without** a `NOT NULL` + default, so it is non-breaking on existing rows (rows predating
the column read as null and behave as `model`).

## Drift root cause + what this migration does
The committed `PlanscapeDbContextModelSnapshot.cs` had drifted **far** behind the entity
model — re-scaffolding `MeetingMedia` produced a whole-schema diff that tried to
`CREATE TABLE` ~99 tables (including `MeetingSessions` itself). Those tables already exist
in deployed databases: dev builds them with `EnsureCreated`, and pre-existing prod DBs are
patched by `PlatformSchemaPatcher` (the migration set is acknowledged-incomplete by design,
see its header comment). So:

- **The snapshot was refreshed** to match the full current model → future
  `dotnet ef migrations add` produce clean diffs instead of the whole-schema scaffold.
- **The migration `Up()/Down()` was trimmed** to the only real new delta — the two
  `MeetingSessions` columns — so it never drops/recreates existing tables.
- **`PlatformSchemaPatcher`** gained idempotent `ALTER TABLE … ADD COLUMN IF NOT EXISTS`
  for both columns, so pre-existing prod DBs (where `Migrate()` short-circuits / the
  migration history is incomplete) still get them on next boot.

## Surgical DDL (what the migration applies)
```sql
ALTER TABLE "MeetingSessions" ADD COLUMN IF NOT EXISTS "ActiveSurface" text;
ALTER TABLE "MeetingSessions" ADD COLUMN IF NOT EXISTS "ActiveDocumentId" uuid;
```

## Run order (deploy)
1. `dotnet build` — clean (verified here).
2. `dotnet ef database update --project src/Planscape.Infrastructure --startup-project src/Planscape.API`
   — **NOT run here** (no live DB in the sandbox); it is the deploy step. On prod,
   `PlatformSchemaPatcher` also applies the two columns idempotently on boot, so the columns
   land even if the migration is skipped.
