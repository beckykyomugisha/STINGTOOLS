# On-Site Construction Update Sharing — Gap Analysis

**Date:** 2026-04-17
**Branch:** `claude/analyze-update-sharing-gaps-QoVY0`
**Use case:** *A site user opens the Planscape phone app, captures a construction update or live issue (photo + description + location), and the relevant project members are notified in real time.*
**Scope:** Verified against current source on `master` + this branch. Supersedes the broad `PLANSCAPE_GAPS.md` (2026-04-10) for this specific user journey.

---

## 1. End-to-End Workflow vs Reality

| # | Step the site user expects | Server | Mobile | Verdict |
|---|----------------------------|--------|--------|---------|
| 1 | Log in on phone | ✅ JWT + refresh + SecureStore | ✅ Wired (`client.ts`) | **Works** |
| 2 | Open project, see members | ✅ `GET /projects/{id}/members` | ❌ No wrapper, no UI | **Blocked** |
| 3 | Tap "New Issue" | — | ✅ Modal in `issues.tsx` (title/desc/type/priority) | **Works (text only)** |
| 4 | Take a photo with phone camera | ✅ Multipart `POST /issues/{id}/attachments` + ImageSharp thumbs + EXIF GPS extraction | ❌ `expo-image-picker` not installed; no upload code path | **Blocked** |
| 5 | Auto-tag GPS to the issue | ⚠️ EXIF parsed but **not persisted** (`BimIssue` has no Lat/Lng); `MobileContextMiddleware` reads `X-Latitude/X-Longitude` for geofence only | ❌ `expo-location` not installed | **Blocked** |
| 6 | Pick assignee from project members | ⚠️ Stored as free-text string, **no validation** against `ProjectMembers` | ❌ No picker; assignee is read-only display | **Blocked** |
| 7 | Submit; if no signal, queue locally | ✅ — | ✅ `offlineQueue.ts` drains on `AppState` foreground; `CREATE_ISSUE` replayed | **Partial — no NetInfo trigger** |
| 8 | Server notifies project members | ⚠️ Broadcasts to whole `tenant_{tenantId}` group, not project-scoped | — | **Blocked (over-broadcasts)** |
| 9 | Assignee receives push instantly | ✅ FCM v1 wired; `SendToUserAsync(assignee)` fires from `IssuesController:145` | ❌ `expo-notifications` token retrieved but **never POSTed to `/notifications/subscribe`** (`settings.tsx:110`) | **Blocked at last mile** |
| 10 | Members see it appear in real time without refresh | ✅ SignalR `NotificationHub` with project/tenant/user groups + JWT-in-querystring auth | ❌ `@microsoft/signalr` not installed | **Blocked** |
| 11 | Linked element (scan QR on site) | ⚠️ `lookupElement` returns metadata only | ❌ Scanner is a stub; `Alert.alert` only on physical device, no flow into issue creation | **Blocked** |

**Bottom line:** Of the 11 steps, **3 work, 1 partial, 7 blocked**. The most surprising blockers are at the very last mile — push token registration and SignalR client are both ~half-day jobs but neither is wired.

---

## 2. What's Already Built (don't rebuild)

The server side is in much better shape than the broad April 10 doc implied. Verified present and wired:

- **Issue creation + attachment upload** with multipart, 50 MB cap, multi-tenant path isolation, IssueAttachment join entity (`IssuesController.cs:84,193`)
- **ImageSharp thumbnailing pipeline** generating 150 / 300 / 600 px JPEGs alongside originals (`ImageSharpThumbnailService.cs`)
- **EXIF GPS extraction** with DMS→decimal conversion (`ImageSharpThumbnailService.cs:45–79`) — *parsed but discarded because `BimIssue` has no Lat/Lng columns*
- **Geofence validation** on issue create using `X-Latitude`/`X-Longitude` headers and project `BoundaryPolygon` (`IssuesController.cs:91–98`, `GeofenceValidationService.cs`)
- **Push to assignee** via Firebase HTTP v1 + JWT (`FirebasePushService.cs`); broadcast via SignalR fires on every issue create (`IssuesController.cs:133,145`)
- **DevicePushToken registration endpoint** with FCM/APNs/Web platform enum and `(UserId, Token)` unique index (`NotificationsController.cs:28–40`)
- **SignalR NotificationHub** with `JoinProject`, `JoinTenant`, `RegisterUser` groups and JWT-in-query-string for WebSocket upgrade (`NotificationHub.cs`, `Program.cs:43–52`)
- **Mobile-aware infrastructure** — `MobileContextMiddleware`, `mobile` rate-limit policy partitioned by `X-Device-Id`, audit log enrichment (`Program.cs:183–195`)
- **Project members API** returning DisplayName / Email / ProjectRole / ISO 19650 role (`ProjectMembersController.cs:32–55`)
- **EF migrations applied** — `20250407_InitialCreate`, `20250410_AddPlanscapeDocumentFields` cover all entities above
- **Mobile auth + offline plumbing** — SecureStore for tokens, 401-triggered refresh, `AppState`-driven offline queue replay (`client.ts`, `offlineQueue.ts`, `useOfflineQueue.ts`)

---

## 3. What's Actually Blocking the Use Case

Ranked by impact on the site-sharing journey, with effort estimates.

### 3.1 BLOCKERS — fix these or the use case doesn't exist

| ID | Gap | Where | Effort | Why it blocks |
|----|-----|-------|--------|---------------|
| **B1** | `BimIssue` has no `Latitude` / `Longitude` / `LocationAccuracy` columns | `Planscape.Core/Entities/BimIssue.cs` + new EF migration | 0.5 d | EXIF extraction code already runs but writes nothing; map view, "issues near me", and audit trail are impossible |
| **B2** | Mobile cannot capture or attach a photo | `Planscape/package.json` (add `expo-image-picker`), new `attachIssuePhoto()` in `endpoints.ts`, photo button in `issues.tsx` create modal | 1.5 d | Step 4 is dead. Server-side upload + thumbnail pipeline is unreachable |
| **B3** | Mobile cannot capture GPS | `package.json` (add `expo-location`), permissions in `app.json`, capture call in issue submit handler, send via `X-Latitude`/`X-Longitude` headers | 1 d | Step 5 dead; geofence middleware also has no input |
| **B4** | Push token retrieved but never sent to server | `Planscape/app/(tabs)/settings.tsx:110` — has the Expo token but does not call `subscribe()` | 0.5 d | The whole notification system is wired *except this one POST*. Pushes will never reach the device until this is fixed |
| **B5** | No assignee picker; no project members API wrapper | `endpoints.ts` (add `listMembers`), new picker component, replace read-only field in `issues.tsx:315` | 1 d | Step 6 dead. Without a real assignee, push routing in `IssuesController.cs:145` has nothing to target |
| **B6** | Server validates assignee as free-text string | `IssuesController.CreateIssue` — lookup `ProjectMembers` and reject unknown assignees | 0.5 d | Silent typos send issues into the void; wrong member gets pushed |
| **B7** | `NotifyAsync` broadcasts to whole tenant, not the project's members | `NotificationService.NotifyAsync` + new `NotifyProjectMembersAsync` that resolves `ProjectMembers` and fans out via `SendToUserAsync` | 1 d | Other tenants' members on the same SignalR group see foreign-project issues; over-notification fatigue |

**Subtotal: ~6 days to make the core flow work end to end.**

### 3.2 HIGH — needed before real site rollout

| ID | Gap | Effort | Note |
|----|-----|--------|------|
| H1 | No SignalR client on mobile (`@microsoft/signalr` not installed) | 2 d | Without this, members must pull-to-refresh; push covers assignee but not the wider team |
| H2 | QR scanner stub doesn't open issue creation prefilled with element | 2 d | `lookupElement` exists; needs scan → lookup → "Raise issue on this element" flow |
| H3 | No connectivity listener (`@react-native-community/netinfo` absent); queue only drains on app foreground | 0.5 d | Issues created during a long offline session don't auto-sync until the app is reopened |
| H4 | No image compression on device | 0.5 d | Modern phone photos are 4–12 MB; site cell links saturate quickly |
| H5 | No HTTPS / TLS termination in `docker-compose.yml`; Kestrel HTTP only | 1 d | Cannot ship to a site without TLS |
| H6 | No global state store for optimistic create + cross-screen invalidation | 1 d | Without Zustand/Tanstack Query, the new issue flashes on/off as the list refetches |
| H7 | No EXIF→DB write — once `BimIssue` has GPS columns, persist `ExtractGpsFromExif` result instead of just logging | 0.25 d | Tiny but high value; auto-tags issues uploaded after the fact from a gallery |

**Subtotal: ~7 days for production-grade rollout.**

### 3.3 MEDIUM — nice to have for the same use case

| ID | Gap | Effort |
|----|-----|--------|
| M1 | Voice-to-text for issue description (site noise + gloves) | 1 d (`expo-speech`/`expo-av`) |
| M2 | Multi-photo attachment per issue (currently 1 endpoint call per file) | 0.5 d |
| M3 | Map view of project issues with pins | 2 d (`react-native-maps`) |
| M4 | Sketch / annotate on photo before upload | 2 d (`react-native-sketch-canvas`) |
| M5 | Notification preferences per user (mute non-critical) | 1 d server + 0.5 d mobile |
| M6 | Storage migration to MinIO/S3 (currently local FS) | 1 d (abstraction is in place via `IFileStorageService`) |

---

## 4. Critical-Path Implementation Plan

Ten working days to a usable on-site demo.

### Day 1–2: Server schema + assignee correctness
- B1 — add `Latitude` / `Longitude` / `LocationAccuracy` to `BimIssue`, EF migration, persist `ExtractGpsFromExif()` result (covers H7)
- B6 — validate assignee in `CreateIssue` against `ProjectMembers`
- B7 — replace tenant broadcast with project-member fan-out

### Day 3: Mobile push wiring
- B4 — add `subscribe()` wrapper, call from `settings.tsx` after token retrieval, also on first login

### Day 4–5: Mobile camera + GPS
- B2 — `expo-image-picker`, attachment upload, multipart with progress
- B3 — `expo-location`, request permissions, attach coordinates as headers + body

### Day 6: Members + assignee picker
- B5 — `listMembers()` wrapper, picker UI, replace read-only assignee field

### Day 7–8: Real-time + connectivity
- H1 — `@microsoft/signalr` install, JWT-in-querystring connect, `JoinProject` on screen mount, refetch on `IssueCreated`
- H3 — NetInfo-driven queue drain in addition to AppState

### Day 9: Polish + safety
- H4 — image compression to ~1280 px long edge, JPEG q75 before upload
- H6 — minimal Zustand store for issues + members + connectivity flag

### Day 10: Production prerequisite
- H5 — nginx + Let's Encrypt in `docker-compose.yml`, redirect HTTP→HTTPS

After day 10, the site user journey in §1 works end to end. Everything in §3.3 is incremental.

---

## 5. Out of Scope (intentionally)

These were called out in the broad April 10 doc but are **not** required for "site user shares an update + issue":

- Plugin↔server dual-sync resolution (`PluginSync` dead code) — affects Revit plugin, not phone app
- TagSync conflict resolution — only relevant when a Revit edit and a mobile edit race on the same element parameter; mobile doesn't edit elements yet
- COBie / IFC / BCF mobile flows — not in the user's stated journey
- Document viewer (PDF/DWG) — orthogonal use case
- Biometric / PIN re-auth on app resume — security hardening, not flow-blocking
- CI/CD, load testing, monitoring — needed for scale, not for the demo

---

## 6. Risk Notes

- **EXIF GPS reliability:** iOS strips GPS from camera roll photos shared via picker by default; B3 must capture coordinates *at the moment of attachment* via `expo-location`, not rely on EXIF.
- **Geofence false negatives:** Concrete-walled basements show low-accuracy GPS; the geofence check in `IssuesController.cs:91–98` may reject legitimate site issues. Recommend a 50 m buffer on the boundary polygon and a "report poor GPS" override.
- **Push token rotation:** FCM tokens rotate; the mobile app must call `subscribe()` on every cold start, not just first login (B4 should handle this).
- **Tenant broadcast leak (B7):** This is a multi-tenancy bug, not just a UX issue. Until B7 ships, do not enable real-time push for any second tenant on a shared server.

---

## Appendix: Verified file references

| Reference | What lives there |
|-----------|------------------|
| `Planscape.Server/src/Planscape.API/Controllers/IssuesController.cs:84,133,145,193` | Create + notify + push + attachment upload |
| `Planscape.Server/src/Planscape.Core/Entities/BimIssue.cs` | Schema — no GPS columns |
| `Planscape.Server/src/Planscape.Infrastructure/Services/ImageSharpThumbnailService.cs:45–79,255–271` | Thumbs + EXIF (logged, not persisted) |
| `Planscape.Server/src/Planscape.Infrastructure/Services/FirebasePushService.cs:44–47` | Push to user via DevicePushToken |
| `Planscape.Server/src/Planscape.Infrastructure/SignalR/NotificationHub.cs` | Project / tenant / user groups |
| `Planscape.Server/src/Planscape.API/Middleware/MobileContextMiddleware.cs:18–24` | `X-Latitude`/`X-Longitude` headers + device id |
| `Planscape.Server/src/Planscape.API/Controllers/ProjectMembersController.cs:32–55` | Members list available, unused by mobile |
| `Planscape/package.json` | Missing: `expo-image-picker`, `expo-location`, `@microsoft/signalr`, `@react-native-community/netinfo` |
| `Planscape/app/(tabs)/issues.tsx:102–124,315` | Create modal — text fields only, assignee read-only |
| `Planscape/app/(tabs)/scanner.tsx:177–190` | QR is a mock alert |
| `Planscape/app/(tabs)/settings.tsx:81–119` | Push token obtained, `subscribe()` never called |
| `Planscape/src/api/endpoints.ts` | Missing: `listMembers`, `attachIssuePhoto`, `subscribe` |
| `Planscape/src/utils/offlineQueue.ts:62–75,98–119` | Replays `CREATE_ISSUE`, drains on `AppState` foreground only |
