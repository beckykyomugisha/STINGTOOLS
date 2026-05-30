# Phase Z — Consolidated Audit Findings

**Branch:** `audit/z-findings` (no merges yet — reference doc only)
**Author:** cloud Claude session, audit-only (no compilation possible here)
**Companion to:** `docs/UI_CLEANUP_CAMPAIGN.md` §11 (Future work)

This document consolidates the Z-numbered findings surfaced during the
UI cleanup + Planscape revive campaigns. Each section is a self-
contained brief: symptom, evidence, blast radius, recommended fix,
who's best positioned to do it.

---

## Z-6 — `MergeRecoveryStubs.cs` has dead-returning methods

### Symptom

The file `StingTools/Core/MergeRecoveryStubs.cs` (509 lines, 15 numbered
sections) holds 61 public method-shaped declarations. The Phase Y P1-B
work surfaced one cluster of these (`PushHvacLoadsBulkAsync` etc.)
returning `Task.FromResult(false)` without making HTTP calls — the
"Push to API" button silently no-op'd for months. There are more in
the same shape.

### Evidence

Section 7 of the file (`PlanscapeServerClient` stubs at lines 280-336)
contains at least these silent-no-op methods, each with **1 caller in
non-stub code** (some UI surface assumes they work):

| Method | Returns | Domain |
|---|---|---|
| `FindModelByHashAsync` | `null` | Model dedup |
| `DeleteModelAsync` | `(true, "")` — false success | Model delete |
| `GetPhotoPolicyAsync` | `null` | Photo NDA |
| `AcceptPhotoNdaAsync` (×2 overloads) | `false` | Photo NDA |
| `ListPhotoChecklistsAsync` | empty list | Photo checklist |
| `ListPhotoAlbumsAsync` | empty list | Photo albums |
| `GetPhotoAlbumAsync` | `null` | Photo albums |
| `CreatePhotoAlbumAsync` | `null` | Photo albums |
| `AddPhotosToAlbumAsync` | `false` | Photo albums |
| `LockPhotoAlbumAsync` | `false` | Photo albums |
| `CreatePhotoShareLinkAsync` | `null` | Photo share |
| `ExportPhotosAsync` (×2 overloads) | `null` / `false` | Photo export |
| `BulkReclassifyPhotosAsync` | `0` | Photo bulk ops |
| `BulkReanchorPhotosAsync` | `0` | Photo bulk ops |
| `ListDistributionGroupsAsync` | empty list | Doc distribution |
| `CreateDistributionGroupAsync` | `false` | Doc distribution |

Section 14 (`DropEngineBase.CheckSoffitClash` / `_soffitAwareness`) and
section 15 (`Pipe.SetSlope` extensions, 2 overloads, body `/* stub */`)
are also silent no-ops, with active callers in the slope-correction +
soffit-clash code paths.

### Blast radius

For each method above, EVERY UI surface that calls it is reporting
fake success/empty results. Same shape as the HVAC "Push to API" bug
fixed in P1-B. Photo-pipeline UI features (NDA, albums, export, bulk
ops, distribution groups) are particularly at risk — these were
clearly designed but never wired through.

### Recommended fix (terminal agent — needs `dotnet build`)

Per stub method:

1. Find the canonical server route (`grep -rn "MapPost\|MapPut\|MapGet" Planscape.Server/src/Planscape.API/Controllers/` for the resource).
2. If a real route exists → rewrite the stub to actually HTTP-POST/GET against it via the existing `PostJsonAsync` / `GetJsonAsync` pattern (mirror what P1-B did for `PushHvacSnapshotAsync`).
3. If no real route exists → delete the stub AND remove the calling UI surface (don't leave a dead-promise button).

Sequential PRs recommended (one per resource: Photo NDA, Photo Albums, Photo Bulk Ops, Distribution Groups, Slope/Soffit). Each ~half-day. Same scope discipline as the P1 series.

### Belongs to: terminal agent — every fix touches C# + needs `dotnet build` to verify

---

## Z-7 — Server raises 13+ event types; client subscribes to ONE

### Symptom

P1-C wired the SignalR client to dashboard.js. P1-D extended `KNOWN_EVENTS` to 11 names. But `Hub.on(...)` handlers exist for only **one event**: `WarningsReported`. Every other server-raised event reaches the browser, fires `emit(name, payload)`, and finds no subscriber — silently dropped.

### Evidence

**Server raise sites** (`Planscape.Server/src/Planscape.API/Controllers/`):

| Event | Controller(s) | Raises |
|---|---|---|
| `WarningsReported` | WarningsController:50 | 1 |
| `IssueCreated` / `IssueUpdated` | IssuesController:383, 666 | 2 |
| `CommentAdded` | IssueCommentsController:113 | 1 |
| `DeliverableCreated` / `Updated` / `Transitioned` | DeliverablesController:159, 193, 281 | 3 |
| `SitePhotoCaptured` / `Approved` / `Rejected` / `Withdrawn` / `BulkApproved` | SitePhotosController:267, 672, 734, 765, 834 | 5 |
| `LpsRecordPushed` | LpsController:142 | 1 |
| `MeetingCreated` / `MeetingUpdated` | MeetingsController:209-686 | **8 sites** |
| `PhotoAlbumChanged` | PhotoAlbumsController:296 | 1 |
| `TransmittalUpdated` | TransmittalsController:162-537 | **6 sites** |
| `DocumentUpdated` | DocumentsController:907, 1234 | 2 |
| `ApprovalDecided` | DocumentsController:1060 | 1 |
| `TagsUpdated` / `ComplianceUpdated` | TagSyncController:169, 171 | 2 |
| `ComplianceChanged` | ComplianceController:92 | 1 |
| `WorkflowRunCompleted` | WorkflowsController:79 | 1 |
| `ModelUpdated` | Infrastructure/SignalR/FederatedModelHub:50 | 1 |

**Client KNOWN_EVENTS** (`dashboard.js:149-158`):
`WarningsReported`, `IssueCreated`, `IssueUpdated`, `TransmittalUpdated`, `ComplianceChanged`, `ComplianceUpdated`, `DocumentUpdated`, `MeetingCreated`, `MeetingUpdated`, `WorkflowRunCompleted`, `ModelUpdated`, `NotificationCreated`.

**Client Hub.on subscribers** (`dashboard.js:223`):
**`WarningsReported`** only.

### Gaps

1. **Server events NOT in client KNOWN_EVENTS** (received-but-not-registered): `CommentAdded`, `DeliverableCreated`, `DeliverableUpdated`, `DeliverableTransitioned`, `SitePhotoCaptured`, `SitePhotoApproved`, `SitePhotoRejected`, `SitePhotoWithdrawn`, `SitePhotoBulkApproved`, `LpsRecordPushed`, `PhotoAlbumChanged`, `ApprovalDecided`, `TagsUpdated`.
2. **Client KNOWN_EVENTS NOT raised server-side**: `NotificationCreated` (no SendAsync found).
3. **Registered but no Hub.on handler**: every event in KNOWN_EVENTS except `WarningsReported`. Server raises, message arrives, fires `emit`, no handlers, drop.

### Blast radius

The Issues / Documents / Transmittals / Meetings / Site Photos / LPS / Compliance tabs in dashboard all update only on poll or page-refresh today. P1-C/D delivered the plumbing; P1-D's actual subscribers (per its commit `956a47d3`) wire one per view via `LIVE_VIEW_EVENTS` map — verify whether that map covers everything in §1 above OR if the gap is real.

### Recommended fix (mostly dashboard.js — minimal C#)

1. Verify what P1-D's `LIVE_VIEW_EVENTS` actually covers (grep `LIVE_VIEW_EVENTS` in dashboard.js).
2. For each missing event, decide:
   - Add to `KNOWN_EVENTS` + `LIVE_VIEW_EVENTS` (1-liner each, dashboard.js only)
   - OR if the event has no UI relevance (e.g. `LpsRecordPushed` is for the Revit plugin, not the web), leave unsubscribed
3. Delete `NotificationCreated` from KNOWN_EVENTS (no server raise — orphan).

### Belongs to: cloud OR terminal (dashboard.js + node --check)

This is a `dashboard.js` edit which I CAN do from cloud (`node --check` works in this sandbox). Terminal can also do it. Either works.

---

## Z-8 — `ClashLive` button has misleading name, correct behaviour

### Symptom

Two buttons in `StingDockPanel.xaml` are labelled "Live" (Tag="ClashSessionRefresh") and read as if they wait for live server clash events. They don't — no server-side clash event exists. The clash kernel runs in-Revit.

### Evidence

- `StingTools/UI/StingDockPanel.xaml:2247` and `:3020` — `<Button Content="Live" Tag="ClashSessionRefresh">`
- `StingTools/UI/StingCommandHandler.cs:2125` — `case "ClashSessionRefresh": RunCommand<Core.Clash.ClashSessionRefreshCommand>(app)`
- No `SendAsync.*Clash` anywhere in `Planscape.Server/src/` (audit confirmed: in-Revit kernel, no server raise)

### What it actually does

`ClashSessionRefreshCommand` re-runs the local Revit clash session from the active 3D view. So the function is correct; the LABEL "Live" oversells it as if waiting for streaming server events. The ToolTip explains correctly ("refresh live clash session from active 3D view") but the 4-letter label primes the wrong expectation.

### Recommended fix (cloud — string-only XAML)

Rename label "Live" → "Refresh" (or "Re-run") in both XAML positions. ToolTip already accurate. No handler change.

### Belongs to: cloud (string-only XAML edit)

I can ship this immediately. Adding to my queue below.

---

## Z-10 — Mapbox token literal placeholder reaches production HTML

### Symptom

Banner on the rendered marketing site reads:
> "*Interactive map awaits a Mapbox token — Replace `PLANSCAPE_MAPBOX_TOKEN` in `index.html` with a free token from mapbox.com.*"

### Evidence

Two places reference the placeholder:

```bash
grep -rn 'PLANSCAPE_MAPBOX_TOKEN' Planscape.Server/src/Planscape.API/wwwroot/
```

Expected hits:
- `index.html` — embedded `<script>` placeholder
- `dashboard.js` — `loadPublicConfig()` writes `CONFIG.mapboxToken = cfg.mapboxToken` (P0-2) but if the server's `/api/public-config` returns the placeholder string instead of a real token, the map still won't render.

Server-side: `PublicConfigController.cs` probably reads `MAPBOX_TOKEN` env var; if unset, returns the placeholder.

### Recommended fix

User-task (not agent-task): obtain a free Mapbox token at https://www.mapbox.com/, set the `MAPBOX_TOKEN` env var (or write to `appsettings.Production.json`), restart server. **No code change needed** — P0-2's `loadPublicConfig` already plumbs the value through. Verify by:

```bash
curl http://localhost:5000/api/public-config
# expected: {"mapboxToken":"pk.eyJ1Ijoi..."}
```

If you want a different default behaviour (hide the banner when token absent, show a fallback static-tile provider, etc.), that's a server-side change.

### Belongs to: user — environment config, no code

---

## Z-11, Z-12, Z-15 — already closed

For audit completeness:
- **Z-11**: duplicate `seq:` block in docker-compose.yml — fixed in commit `d51269c05`
- **Z-12**: wwwroot bind-mount for dev iteration — added in commit `b8c0b5a45`
- **Z-15**: forensic — duplicate observability block introduced by merge commit `41ae11234` on April 17, 2026 (a `-X ours` consolidation where both parents had ONE `seq:` each at different line positions; `-X ours` preserved both without YAML semantic dedup). Multiple downstream `-X ours` merges propagated the issue.

---

## Items pending — terminal agent's queue (build-dependent)

1. **Z-4** — Main panel DOCS tab has 6 align buttons identical to the now-collapsed dialog. Same pattern as Phase D triage #2 — collapse to 1, but cross-check external refs first. (StingDockPanel.xaml + maybe handler — needs build verify.)
2. **Z-6 fix per stub** — see §Z-6 above. ~5 short PRs, one per resource.
3. **14 stale `fix/*` branches** still on remote (proxy 403s the cloud session's deletes). `git push --delete` from terminal works because of different auth.
4. **Future P-level work** — anything with C# / EF / Docker / Revit.

---

## Items pending — my queue (audit / YAML / string-only)

5. **Z-8 fix** — rename "Live" → "Refresh" in 2 XAML labels. Ship from cloud, this commit batch.
6. **Z-7 dashboard.js wiring gap** — decide-and-fix the missing `Hub.on` handlers. Can ship from cloud (`node --check` verifies).
7. **Tracker doc update** — append Z-10..Z-16 to `docs/UI_CLEANUP_CAMPAIGN.md` §11.
