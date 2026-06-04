# Follow-up migration: `MeetingMedia` (WS3)

WS3 adds two columns to `MeetingSessions` (entity `MeetingSession`):

| Column | Type | Null | Default | Purpose |
|---|---|---|---|---|
| `ActiveSurface` | text | NOT NULL | `'model'` | Active surface every client shows: `model` \| `document` \| `screen` |
| `ActiveDocumentId` | uuid | NULL | — | Shared `DocumentRecord` id when `ActiveSurface = document` |

## Why this isn't auto-scaffolded in-repo
`dotnet ef migrations add MeetingMedia` in this checkout scaffolds a **whole-schema
diff** (hundreds of CreateTable/AlterColumn ops) because the committed
`PlanscapeDbContextModelSnapshot.cs` has drifted from the entity model
(accumulated un-migrated changes on `main` — many feature branches ship without a
migration). Committing that giant scaffold would be destructive, so it is **not**
included here. Generate the real migration on a checkout whose snapshot matches the
model, or apply the surgical DDL below.

## Generate it properly (deploy step)
```bash
cd Planscape.Server
dotnet ef migrations add MeetingMedia \
  --project src/Planscape.Infrastructure \
  --startup-project src/Planscape.API \
  --output-dir Data/Migrations
# review that Up() only AddColumn ActiveSurface + ActiveDocumentId, then:
dotnet ef database update --project src/Planscape.Infrastructure --startup-project src/Planscape.API
```

## Or apply the surgical DDL directly (PostgreSQL)
```sql
ALTER TABLE "MeetingSessions"
  ADD COLUMN IF NOT EXISTS "ActiveSurface" text NOT NULL DEFAULT 'model',
  ADD COLUMN IF NOT EXISTS "ActiveDocumentId" uuid NULL;
```
Both columns are additive + safe on existing rows (existing sessions default to the
`model` surface).
