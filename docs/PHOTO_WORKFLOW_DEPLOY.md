# Phase 179 — Site-photo workflow deploy runbook

This runbook lists the deploy-prep steps for the Phase 179 enhancements
that the Linux dev sandbox can't perform. Run these on a Windows box
with the .NET 8 SDK + EF Core CLI installed, against a Postgres dev
instance, before the first production deploy.

Branch: `claude/enhance-photos-workflow-T6vMA`.

## Prerequisites

```powershell
# .NET 8 SDK (any 8.0.x release)
dotnet --version          # expect 8.0.x

# EF Core CLI — installed once per machine
dotnet tool install --global dotnet-ef --version 8.0.11

# Local Postgres for the migration smoke-test (Docker is fine)
docker run --rm -d --name planscape-pg -p 5432:5432 \
  -e POSTGRES_PASSWORD=devpw -e POSTGRES_USER=planscape postgres:16
```

## Step 1 — Build verification

```powershell
cd Planscape.Server
dotnet restore
dotnet build -c Release
```

Expected: zero errors, zero warnings.

## Step 2 — Regenerate the EF model snapshot

The Phase 179 migration was hand-written. EF needs the model snapshot
to be regenerated so future migrations diff against the right baseline.

```powershell
cd Planscape.Server\src\Planscape.Infrastructure
dotnet ef migrations add SnapshotPhase179Refresh `
  --project . `
  --startup-project ..\Planscape.API `
  --no-build
```

EF generates an empty migration plus a refreshed
`PlanscapeDbContextModelSnapshot.cs`. **Delete the empty migration
file** (the refresh migration), keep the updated snapshot, and commit
the snapshot only. The snapshot delta should add the 11 Phase 179
entities to the model graph.

If EF complains about missing entities or extra columns, the
`PlanscapeDbContext.OnModelCreating` overrides are out of sync with the
hand-written migration — fix the snapshot manually (or scripted via
`Get-FileHash` diff).

## Step 3 — Apply the migration locally

```powershell
$env:ConnectionStrings__Default = `
  "Host=localhost;Port=5432;Database=planscape;Username=planscape;Password=devpw"

dotnet ef database update --startup-project ..\Planscape.API
```

Expected:
- 11 new tables appear under `public.*` in psql:
  `\dt` should list `PhotoAlbums`, `PhotoAlbumPhotos`,
  `DistributionGroups`, `DistributionGroupMembers`, `PhotoAccessRules`,
  `PhotoChecklists`, `PhotoChecklistItems`, `PhotoAnnotations`,
  `PhotoVoiceNotes`, `PhotoShareLinks`, `PhotoPolicies`.
- `\d "PhotoAlbums"` shows the unique index `IX_PhotoAlbums_ProjectId_Visibility`
  and the FK to `Projects(Id) ON DELETE CASCADE`.
- Existing `SitePhotos` table is **untouched** — no new columns, no
  changed FKs.

## Step 4 — Smoke-test the new endpoints

```powershell
# Bring the API up against the same Postgres
cd Planscape.Server\src\Planscape.API
dotnet run

# In another shell — auth as the demo admin
curl -s http://localhost:5000/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{"email":"admin@planscape.demo","password":"admin123"}' | jq .accessToken
# … paste the token below

$token="…"
$pid="<project-uuid>"

# Smoke-test album creation
curl -s -X POST "http://localhost:5000/api/projects/$pid/photo-albums" `
  -H "Authorization: Bearer $token" -H "Content-Type: application/json" `
  -d '{"name":"smoke-test-album","visibility":"Members"}' | jq .

# Smoke-test PDF export (empty project will return 0-photo PDF)
curl -s -X POST "http://localhost:5000/api/projects/$pid/photo-export?format=pdf" `
  -H "Authorization: Bearer $token" -H "Content-Type: application/json" `
  -d '{"photoIds":[]}' -o smoke.pdf
file smoke.pdf   # expect "PDF document, version 1.x"
```

## Step 5 — Revit plugin verification

```powershell
cd StingTools
dotnet build StingTools.csproj `
  -c Release `
  -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"
```

Expected: zero errors. Three sub-tab files (`SitePhotosGridSubTab.cs`,
`SitePhotosAlbumsSubTab.cs`, `SitePhotosChecklistsSubTab.cs`,
`SitePhotosAdminSubTab.cs`) and the offline cache helper
(`SitePhotoOfflineCache.cs`) compile against the existing
`PlanscapeServerClient` surface.

Load the plugin in Revit 2025 (or 2026 / 2027), open BCC, click the
SITE PHOTOS tab — five sub-tabs should appear, with the original
review queue as the default.

## Step 6 — Mobile app verification

```bash
cd Planscape
npm install                          # picks up no new deps
npx tsc --noEmit                     # type-check only
npx expo start --no-dev              # cold start
```

Smoke-test:
1. Open the app.
2. Navigate to a site-photo screen (any link from the existing flow).
3. Manually push to `/site-photos/albums` — should render the empty
   list state with the FAB.
4. Same for `/site-photos/checklists` and `/site-photos/annotate`.

## Step 7 — Production deploy

Standard rollout — no special gating beyond the usual canary:

```bash
# Push migrations to the staging Postgres
ConnectionStrings__Default=$STAGING_DB \
  dotnet ef database update --project src/Planscape.Infrastructure \
                            --startup-project src/Planscape.API

# Deploy API + worker images (Hangfire picks up the new
# PhotoRetentionJob recurring schedule on first start)
docker compose pull && docker compose up -d
```

Verify the daily 03:30 UTC `photo-retention` recurring job appears in
the Hangfire dashboard within 60 s of API startup.

## Rollback

The Phase 179 migration's `Down` drops the 11 new tables in reverse
dependency order. Existing v1 readers and writers are untouched so a
rollback is safe at any point — there are no destructive schema
changes to existing tables.

```powershell
dotnet ef database update <previous-migration-name> `
  --project src/Planscape.Infrastructure `
  --startup-project src/Planscape.API
```

## Known follow-ups

- **PDF export limit** — capped at 200 photos per render to keep peak
  memory bounded. Bigger jobs should move to a Hangfire job that
  writes to storage and emails a signed URL. Tracked under Phase
  179.1 (not yet scheduled).

## Caveats cleared by this runbook

| Caveat (CHANGELOG.md Phase 179) | Step |
|---|---|
| `dotnet build` not run on Linux sandbox | Step 1 |
| EF model snapshot not regenerated | Step 2 |
| PDF export deferred (HTML+ZIP only) | Cleared in 179.1; renderer is `PhotoPdfExportService` (QuestPDF). Verify with the smoke-test in Step 4. |
| v1 list endpoint pre-Phase-179 behaviour | Cleared in 179.1; `PhotoAclGate` AND-s the audience state with `PhotoAccessRule` rows on every v1 list / single GET / `/file` fetch. |
| `RequiresNdaAcceptance` not gated at fetch time | Cleared in 179.2; `PhotoAclGate.NdaRequiredAsync` + `PhotoNdaAcceptances` table; v1 returns `ndaRequiredIds` sibling array; mobile `NdaPromptModal` and `gallery.tsx` lock-tile UX. |
| Mobile checklist auto-link absent | Cleared in 179.2; capture flow reads `checklistId`/`checklistItemId` from route params and auto-fulfils online or via the new `FULFIL_CHECKLIST_ITEM` queue action offline. |
