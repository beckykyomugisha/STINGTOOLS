# EF Migration Backlog & Snapshot-Staleness Caveat

Status note for Prompt 14 task 3 ("EF-migration backlog"). The audit assumed the
restored Photo DbSets and cross-host columns still needed migrations. **They do
not** — but there is a real, separate snapshot-staleness landmine that makes
running `dotnet ef migrations add` unsafe right now. This documents the actual
state so a maintainer with a scratch Postgres can act with the SQL in front of
them (the prompt's "review the generated SQL before committing").

## 1. Already migrated — NO action needed

| Concern | Migration | Notes |
|---|---|---|
| Photo workflow tables incl. `PhotoAlbumPhotos`, `PhotoShareLinks` | `20260514000000_AddPhotoWorkflowEnhancements` | `PK_PhotoAlbumPhotos` is the composite `(AlbumId, PhotoId)` (line ~148). |
| `PhotoNdaAcceptances` | `20260514100000_AddPhotoNdaAcceptances` | `PK_PhotoNdaAcceptances` is the composite `(PhotoId, UserId)` (line ~36) + FKs + `IX_..._UserId`. |
| Cross-host identity (`ExternalElementMappings`, cross-host fields) | `20260519000000_IfcIngestSubstrate`, `20260601000000_CrossHostIdentityFields` | Already in the model snapshot. |

Prompt 5 "restored" the Photo DbSets that were missing from the **DbContext
properties** — the **tables already existed** in the DB via the migrations
above. Prompt 14 task 2 then added the `OnModelCreating` composite-key config for
`PhotoAlbumPhoto` + `PhotoNdaAcceptance`; that config had been lost (a merge
regression) so the runtime model threw "requires a primary key" even though the
DB had the PKs. **The task-2 change re-aligns the runtime model to the
already-migrated schema — it should NOT produce a new migration.** Confirm with
`dotnet ef migrations has-pending-model-changes` (expect: the only model delta is
the key config, which matches what the migrations already created).

## 2. ⚠ Snapshot-staleness landmine — reconcile BEFORE any `migrations add`

`PlanscapeDbContextModelSnapshot.cs` is **stale**: it has **zero** references to
the already-migrated Photo tables (`PhotoAlbumPhotos`, `PhotoShareLinks`,
`PhotoNdaAcceptances`) — the DbContext source even warns about this. Because the
snapshot is the baseline `migrations add` diffs against, running it now would try
to **CREATE those tables again** (they already exist) — the "monster" diff the
DbContext comment predicts. It would fail on apply or corrupt migration history.

**Maintainer procedure (scratch Postgres required):**
1. Stand up a scratch DB and `dotnet ef database update` to the latest migration.
2. `dotnet ef migrations has-pending-model-changes` — see the true delta.
3. If the snapshot is confirmed stale, **regenerate it to match applied
   migrations** (e.g. a no-op reconciliation migration, or rebuild the snapshot)
   rather than letting a new migration re-create existing tables.
4. Only then add genuinely-new migrations, reviewing the generated SQL.

## 3. Genuinely pending — but owned by in-progress WIP

`ArchiCADEventLogPersistence` — `ArchiCADController.cs` (line ~158) carries an
inline `// MIGRATION REQUIRED: dotnet ef migrations add ArchiCADEventLogPersistence`
and writes to `db.ArchiCADEventLogs` (entity `Planscape.Core.Entities.ArchiCADEventLog`).
This is part of the **in-progress ArchiCAD event-log feature** (uncommitted at the
time of writing). Generate this migration as part of completing that feature —
**not** standalone, and only after the snapshot reconciliation in §2 (otherwise
the same stale-snapshot diff bundles in the Photo-table re-creation).

## 4. Task 4 (explicit `IfcGlobalId` column) — intentionally skipped

The optional `TaggedElement.IfcGlobalId` column (retiring the `UniqueId` overload)
was **declined** (Prompt 14 task 4): cross-host resolution already routes through
`ExternalElementMapping`, so it is cleanliness, not correctness, and it is the
highest-risk item (shipped schema + both ingest write paths + backfill). If
revisited, it also lands after the §2 reconciliation.
