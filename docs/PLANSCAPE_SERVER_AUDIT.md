# Planscape Platform — Deep Audit Manifest

**Date:** 2026-05-30
**Branch:** `audit/planscape-deep-review`
**Method:** Read-only audit. No fixes applied. 7 parallel investigation agents (server wiring, controllers, web shell, mobile, Revit BCC, Bonsai/ArchiCAD/Tekla, end-to-end workflow traces) + direct verification of the crux findings.
**Running surface audited:** the authenticated app shell served at `http://localhost:5000`.

---

## 0. The single most important finding (verified)

> **The "dead body" is not a server problem. The authenticated web shell served at `/app/` is a hand-written vanilla-JS app (`wwwroot/app/index.html` + `wwwroot/js/dashboard.js`) — and `dashboard.js` does not parse.**

Verified directly with `node --check`:

```
wwwroot/js/dashboard.js:872
  function openNewProjectModal() {
  ^^^^^^^^
SyntaxError: Unexpected token 'function'
```

**Mechanism (copy-paste corruption):**
- `trendSparklineHtml()` closes correctly at `dashboard.js:788`.
- `dashboard.js:789–~1039` is an **orphaned duplicate of the `initProjectsMap` body** spliced into IIFE-body scope (Mapbox guard + `new mapboxgl.Map(...)` + a `for (const p of located)` marker loop) with no enclosing `function` header. Its surplus `}` (full-file brace balance is net **−1**, 554 `{` vs 555 `}`) prematurely closes the IIFE.
- `openNewProjectModal` (`:872` and again `:1043`), `showToast` (`:939` and `:1110`), and `initProjectsMap` (`:949`) are all **duplicate declarations** — proof of a paste accident, not intent.

Because the whole file is one IIFE (`dashboard.js:7 … :1684`), a parse error kills **everything**: `boot()` never runs → `#main` never populated (empty body on every tab) → the `userChip` "Signing in…" placeholder (`app/index.html:19`) is never overwritten (stuck spinner) → no `addEventListener` ever binds (no-op buttons). **One defect fully explains symptoms 1, 2, and 4.**

**Second, independent client bug (also verified):** even after the syntax error is fixed, `boot()` calls `await loadPublicConfig()` at `dashboard.js:124`, and `loadPublicConfig` **is never defined anywhere** (grep returns the single call site). `boot()` would reject with `ReferenceError`, and the bootstrap `boot().catch(() => showLogin())` (`:1683`) bounces to the login overlay — body still empty. The matching server endpoint `GET /api/public-config` **is** wired and `[AllowAnonymous]` (`PublicConfigController.cs:29`); only the client helper is missing.

**Symptom 3 (no new-meeting / camera / photo in the web shell): by design, not a bug.** The dashboard is explicitly a "read-only coordinator view". Meetings/Warnings are read-only `renderList` tables; there is no create-meeting form, no camera, no photo capture in `dashboard.js` at all. Those features live only in the Expo mobile client. This is a **feature-parity gap**, not a regression.

**Symptom 5 (marketing site fine):** consistent — `marketing-site/` and `planscape-site/` are separate static Next.js apps with no dependency on `dashboard.js` or the auth handshake.

### Surface map (clears up the "which webapp" confusion)
| Path at :5000 | Code | Role |
|---|---|---|
| `/` | `wwwroot/index.html` + inline JS | Marketing landing (static) — **fine** |
| `/app/` | `wwwroot/app/index.html` + `wwwroot/js/dashboard.js` | **The broken authenticated dashboard** |
| `/viewer.html` | `wwwroot/viewer.html` + `three.min.js` + `coordination-viewer.js` | Three.js 3D viewer — self-contained, **wired but unreachable behind the dead shell** |
| `wwwroot/_next/*` | Next.js static export | **Marketing page only** — does NOT contain a portal route |
| `planscape-site/app/portal/` | Next.js source | A separate **read-only client photo portal** (gallery/timeline) — not deployed to :5000; has an env-var bug (below) |
| `Planscape/` (Expo) | React Native + Expo Router | The **mobile app** (also `expo start --web` capable) — separate surface |

---

## A. Planscape.Server host wiring

| Concern | Status | Evidence / Notes |
|---|---|---|
| **CORS** | ✅ wired (not a cause) | `Program.cs:886-909` two policies, applied `1042-1043`. The dashboard calls same-origin (`apiBase:"/api"`, `dashboard.js:15`) so CORS is irrelevant to it. `Mobile` policy covers Expo dev origins. |
| **Auth scheme** | ✅ wired (JWT bearer only) | `Program.cs:138-254`. No cookie auth. Startup **throws** if `Jwt:Key` missing/<32 chars/known placeholder (`:103-127`). Dashboard uses `localStorage` + `Authorization: Bearer` (`dashboard.js:52`) — matches. |
| **Middleware order** | ✅ correct | StaticFiles → SecurityHeaders → RateLimiter → CORS → Authentication → Authorization → MapControllers/hubs. |
| **Static / SPA serving** | ⚠ partial | `UseDefaultFiles()` + `UseStaticFiles()` serve `/app/index.html`. **No `MapFallbackToFile`** — fine for the current hash-router shell, latent risk if HTML5 routing is ever introduced. |
| **SignalR hubs** | ✅ wired | 10 hubs mapped `Program.cs:1223-1236` (`/hubs/notifications`, `/hubs/compliance`, `/tagsync`, `/crdt`, `/healthcare`, `/archicad`, `/model`, `/events`, `/meeting`, `/twin`). Redis backplane `427-430`. JWT-via-querystring `186-192`. **No `/hubs/clash`** (clash kernel is in-Revit). |
| **Hangfire** | ✅ wired | Dashboard `/hangfire` (`Program.cs:1084-1087`), Postgres storage, API/worker role split. |
| **MinIO / S3** | ⚠ conditional | `Program.cs:285-293`: `Storage:Provider=="S3"` → `S3FileStorageService`, else **defaults to `LocalFileStorageService`**. No `Storage:*` keys in dev config → local disk. Prod compose ships placeholder secret (below). |
| **Swagger** | ✅ | On in Development. |
| **Startup blocking gate** | ⚠ note | `ConnectionMultiplexer.Connect(redisConn)` is **synchronous** (`Program.cs:424`). Redis down → app fails to start (not a hung handshake). `AuthController.Login` touches Redis first (`:93/:103/:150`) — a post-boot Redis drop would stall login, the only plausible *server-side* contributor to a stuck spinner. |

### Placeholder / secret inventory
| Location | Value | Severity |
|---|---|---|
| `wwwroot/js/dashboard.js:16` | `mapboxToken: "PLANSCAPE_MAPBOX_TOKEN"` | Low — graceful fallback panel; cosmetic |
| `wwwroot/index.html:135`, `app/index.html:9` | `PLANSCAPE_MAPBOX_TOKEN` | Low — cosmetic |
| `appsettings.Development.json:4` | committed dev `Jwt:Key` (`DEV-LOCAL-ONLY-...`) | Low (dev only) — never use in prod |
| `appsettings.json:3-4` | dev DB password literal `Planscape2026!` (self-flagged `TODO-SEC`) | Med — rotate via env for any shared deploy |
| `docker/docker-compose.prod.yml:26,55` | `MINIO_ROOT_PASSWORD` default `PlanscapeMinio2026!` | Med — prod placeholder secret |
| `planscape-site/.env.example:11,17` | `NEXT_PUBLIC_MAPBOX_TOKEN=` / `NEXT_PUBLIC_PLANSCAPE_API=` empty | — |
| No `STRIPE_*` placeholders found | Stripe/Flutterwave providers (`Program.cs:598-602`) no-op without config keys | — |

### EF migrations / schema (reconciled — the "empty migrations" claim is half-true)
- `Planscape.Infrastructure/Migrations/` is **empty (0 files)** — a decoy folder.
- The real set is `Planscape.Infrastructure/Data/Migrations/` — **73 migration files** incl. `HealthcarePack`, `AddIfcAlignmentReports`, coordination tables, `AddBridgeKeyHashToProject`.
- **Dev does NOT run `Migrate()`.** `Program.cs:1243-1281`: in Development (or `PLANSCAPE_USE_ENSURE_CREATED=true`) it uses `RelationalDatabaseCreator.CreateTables()` from `OnModelCreating`, gated on whether `Tenants` exists. The in-code comment says the hand-authored migrations lack `.Designer.cs` companions and the snapshot is stale, so `Migrate()` can't order them.
- **Consequence:** in **dev**, all entities registered in `OnModelCreating` (Healthcare ×4, HVAC, IFC alignment, `ExternalElementMapping`) get tables via reflection → schema is complete **for a fresh DB**. But:
  - **`ExternalElementMapping` / IfcIngest has NO migration** (verified: no `*ExternalElement*`/`*IfcIngest*` file in the 73; not in the model snapshot). In **prod** (`Migrate()`), `IfcController` reads on `_db.ExternalElementMappings` → **Postgres "relation does not exist" → HTTP 500**.
  - HealthcarePack / Penetration tables exist as migrations but rely on dev `EnsureCreated`; prod `Migrate()` path is broken per the in-code comment.
- **Minor schema inconsistency:** DbContext exposes `DbSet<HvacSnapshot>` (`:175`) but `OnModelCreating` configures `HvacLoadSnapshot`/`HvacNcSnapshot`/`HvacRefrigerantSizing` (`:1175-1189`). Follow-up, not symptom-causing.

---

## B. Controllers — per-controller status

All listed controllers are **wired with real DB-backed implementations** (no NotImplemented stubs found among the UI-facing set). Exceptions and gates noted.

| Controller | Key endpoints | Status | Evidence / Notes |
|---|---|---|---|
| AuthController | login / refresh / register / me / accept-invitation / tenants / switch-tenant / license / forgot+reset-password | ✅ wired | Public endpoints not `[Authorize]` (reachable pre-token). BCrypt + Redis + DB live. JWT emits claim **`tenant_id`** (snake_case) — `AuthController.cs:862`. |
| PublicConfigController | GET /public-config | ✅ wired, `[AllowAnonymous]` | Returns `{mapboxToken}` (`""` if unset). The web client never calls it (`loadPublicConfig` missing). |
| ProjectsController | list / get / CRUD / pin / `{id}/dashboard` | ✅ wired | Reads `tenant_id` claim (`:316`). |
| ProjectMembersController | members / invite / bulk-invite | ✅ wired | |
| IssuesController | CRUD + attachments | ✅ wired + SignalR + push | Broadcasts `IssueCreated`/`IssueUpdated` to `project-{id}` (`:383,:666`). |
| DocumentsController | CRUD + transition + revisions + approvals | ✅ wired | ISO 19650 state machine; real file storage. |
| TransmittalsController | CRUD + bulk + send + acknowledge + respond | ✅ wired + SignalR | Broadcasts `TransmittalUpdated`. |
| MeetingsController | CRUD + status + agenda + iCal export | ✅ wired + push | No camera/photo here (those = SitePhotos). |
| WorkflowsController | run / history / trend | ✅ wired | All 3 routes real. |
| WarningsController | report / trend | ✅ wired + SignalR | Broadcasts `WarningsReported` — **see SignalR section: no client subscribes.** |
| NotificationsController | subscribe / unsubscribe / devices | ✅ wired | |
| ComplianceController | post / history / trend | ✅ wired + SignalR | |
| TagSyncController | sync (bulk upsert) | ✅ wired + SignalR | |
| SeqSyncController | sync / export | ✅ wired | Max-merge. |
| SearchController | GET /search | ✅ wired | ILike across tags/issues/docs/meetings. |
| PlatformController | CRUD + test + sync | ✅ wired | Connector factory dispatch. |
| AdminController | org / users / activate / elevate | ✅ wired | `[Authorize(Roles="Admin,Owner")]`. |
| MimController | assets / jobs / conditions | ✅ wired, tenant-flag gated | 403 when `tenant.MimEnabled==false`. |
| HealthcareController | dashboard / pressure-logs / mgas / anti-ligature / rds | ✅ wired | **No `[ProjectAccess]`** — any authenticated token, no project-membership check. **No `water-flush` endpoint.** |
| PenetrationsController | signoff (PUT/GET) / dashboard | ✅ wired | **No `[ProjectAccess]`.** |
| IfcController | POST /ifc/data, GET /ifc/mappings | ⚠ wired but **500 in prod** | Reads `ExternalElementMappings` — **table has no migration** (§A). |
| ModelsController | list / get / upload / file / element-map / thumbnail | ✅ wired | `IModelConverter` defaults to `NullModelConverter` — **no IFC→glTF conversion** unless `ModelConverter:Provider` set. |
| SitePhotosController | capture / list / file / audience / approve / reject / bulk | ✅ wired + Hangfire redaction | |
| PhotoAlbumsController | CRUD + photos + lock + reorder | ✅ wired + SignalR | |
| ScheduleController / CostController | tasks / items / summary | ✅ wired | |
| TenantKeywordsController / TenantBimManagerRolesController | get / put | ✅ wired | Policy-gated. |
| MeetingRoomController / MeetingSnapshotController | sessions / snapshots | ✅ wired + MeetingHub | |
| **DashboardController** | kpi / summary / heatmap / widget-config | ❌ **broken** | `GetTenantId()` reads claim **`"tenantId"`** (camelCase) but JWT emits **`"tenant_id"`** → `InvalidOperationException` → **HTTP 500 on every call** (`DashboardController.cs:23` vs `AuthController.cs:862`). |

### Claim-name mismatch cluster (real latent bug)
Controllers reading the claim as camelCase `"tenantId"` (which the JWT never emits) will 500 on every authenticated request: **DashboardController, BoqController, ClassificationController, MfaController, ModelChecksController, OfflineManifestController, SsoController, SuitabilityController**. The correct claim is `tenant_id`. Not the cause of the dashboard symptoms (the current `dashboard.js` doesn't call KPI endpoints), but breaks those features once the shell is revived.

---

## C. Web shell & 3D viewer

| Item | Status | Evidence |
|---|---|---|
| `wwwroot/js/dashboard.js` parse | ❌ **DEAD** | `node --check` SyntaxError `:872`; orphaned dup map block `:789-1039`; dup `openNewProjectModal`/`showToast`/`initProjectsMap`. |
| `loadPublicConfig()` | ❌ **UNWIRED** | Called `:124`, never defined. Server endpoint exists. |
| Dashboard API client | ✅ wired | `apiBase:"/api"` same-origin; login contract matches `AuthController`. |
| 3D viewer (`viewer.html`) | ✅ wired, unreachable | Local Three.js (`:629-631`), `THREE` guard (`:655`), JWT via `?access_token=` (`coordination-viewer.js:1627`). Opened from `dashboard.js:1148` — dead. |
| Mapbox token | ⚠ placeholder | Graceful fallback; cosmetic. |
| `_next` build vs `planscape-site` | ⚠ mismatch | Deployed `_next` = marketing page only; **no `/portal` route** compiled. |
| `planscape-site` portal client | ⚠ env-var bug | `components/portal/api.ts:7` reads `NEXT_PUBLIC_API_BASE`, but `.env.example:17` defines `NEXT_PUBLIC_PLANSCAPE_API` → local builds silently hit prod `api.planscape.app`. Not deployed at :5000, so not a :5000 symptom. |
| Web SignalR in dashboard | ❌ unwired | `signalr-shim.js` is correct but referenced **only by `viewer.html`/`coordination-viewer.js`**, not by `app/index.html`. The dashboard has no realtime at all; tabs are REST-poll `renderList`. Shim also lacks `WarningsReported`/`TransmittalUpdated` subscriptions. |

---

## D. Mobile (Expo) app

The Expo app is a **separate, healthier surface** than the broken dashboard. The "Signing in…" symptom is the dashboard's, not the mobile login (which uses an `ActivityIndicator`, not a persistent label). But the mobile app has its own concrete bugs that would blank content when run via `expo start --web`:

| Bug | Status | Evidence |
|---|---|---|
| No `EXPO_PUBLIC_API_BASE` set; fallback `http://localhost:5000` | ⚠ config | `client.ts:17-22`; `app.config.js:14` placeholder host. No `.env` in repo. Points at the Expo dev server, not the API → 404/CORS → empty content. |
| Duplicate `<Tabs.Screen name="models">` | ❌ bug | `app/(tabs)/_layout.tsx:97` and `:129` — crashes the tab navigator; buttons become no-ops. |
| `activeProject` null deref | ❌ bug | `index.tsx:55` `getProjectDashboard(activeProject.id)` with no null guard → throws on fresh install → retry screen, empty body. |
| `@/src/components/MemberPicker` import | ❌ bug | `meetings/index.tsx:15` resolves to `src/src/...` (tsconfig maps `@/*`→`src/*`). Should be `@/components/MemberPicker` → meetings bundle fails. |
| Offline queue → missing endpoints | ❌ bug | `offlineQueue.ts` dynamically imports `postIssueComment`/`placeIssuePin`/`deleteIssuePin`/`upsertDiaryEntry` — none exist (actual: `addIssueComment`, `createSiteDiary`/`updateSiteDiary`; no pin fns). Offline comments/pins/diary silently fail on drain. |
| `realtimeClient.ts` hub URL default | ⚠ placeholder | `:4` `…/hubs/project` (no such route); overridden by `app.config.js:16`/`eas.json:15` to `/hubs/notifications` in configured builds. |

**Camera / photo / QR — all WIRED on mobile:** `expo-camera` plugin + iOS/Android permissions (`app.config.js:46-72,91-95`); live QR scan (`scanner.tsx:247-274`); penetration sign-off QR+GPS+photo (`penetrations/signoff.tsx`); site-photo capture + offline queue (`site-photos/capture.tsx`, `SitePhotoFab`). **Photo upload is multipart POST** to `/api/.../attachments` — **no MinIO pre-signed-URL path in the client** (server handles storage).

**Feature-parity gap (mobile-only, absent from any web surface):** QR scanner, penetration sign-off, healthcare commissioning screens, site-photo capture, site diary, biometric re-auth, offline queue, voice notes, 3D presence/camera-share, punchlist.

---

## E. Revit BCC ↔ Server integration

The Revit `PlanscapeServerClient` (invoked from 45 files) is the **healthy spine**: login (`/api/auth/login`), refresh, auto-refresh <5 min, DPAPI session persistence, `ConfigureAwait(false)` + `Task.Run` to avoid WPF deadlock. **No placeholder secrets.** It cannot cause the browser symptoms.

| Integration | Status | Evidence |
|---|---|---|
| Auth + core sync (projects/issues/docs/meetings/transmittals/tagsync/compliance) | ✅ wired | `PlanscapeServerClient.cs` per-endpoint |
| BCC 13-tab dialog | ⚠ local-file-first | Renders from local JSON under `STING_BIM_MANAGER\*.json`. Server data enters only via narrow pull-on-demand (issue activity, `SyncIssuesFromServerAsync`). **No general "Push all / Sync all" button.** |
| `Hvac_PublishToServer` (Phase 187d) | ❌ **DEAD STUB** | `PushHvacLoadsBulkAsync`/`PushHvacNcAsync` are `Task.FromResult(false)` stubs in `MergeRecoveryStubs.cs:289-290`. `PushHvacLoadAsync`/`PushHvacRefrigerantAsync` don't exist at all. Command advertises `/hvac/loads`,`/hvac/nc`,`/hvac/refrigerant` but server only has `POST /hvac/snapshots` → route-mismatched too. Clicking "Publish" is a silent no-op. |
| `RestoreSessionAsync` | ❌ dead | Defined `:1715`, never called — session persistence writes but never reloads (no re-login-after-restart). |
| `PushWarningsAsync`/`GetWarningsAsync` | ❌ unwired | Defined `:944,:955`, zero callers — warnings sync plumbed but never invoked. |
| SignalR client (Revit) | ✅ wired, inbound-only | `PlanscapeRealtimeClient.cs` → `/hubs/notifications`, subscribes Issue/Compliance/Transmittal/Document/Meeting/Model events. **No warnings/clashes subscription.** |
| Site Photos methods | ⚠ partial | Core review actions real; NDA/album/checklist/export are stubs (`MergeRecoveryStubs.cs:301-322`). |

---

## F. Cross-host plugins (Bonsai / ArchiCAD / Tekla)

| Host | Verdict | Evidence |
|---|---|---|
| **Bonsai** | SCAFFOLD-ONLY (matches Phase 186 roadmap) | Manifest schema 1.0.0 valid; `bl_info`+register valid; 3 diagnostic ops exist (`sting.about`/`reload_substrate`/`bonsai_probe`). IFC substrate found in-repo (needs `STINGTOOLS_SHARED_IFC` when packaged). **16 production operators ABSENT**; `handlers/`+`workflows/` empty; **`stingtools-bonsai/planscape/client.py` ABSENT** — the core `stingtools_core/planscape/client.py:86` has `ingest_ifc_data()` → `POST /ifc/data` but **no add-on call site**. |
| **ArchiCAD** | SCAFFOLD-ONLY | `CMakeLists.txt:18-23` lists 4 sources; only `StingPlanscapeAddon.cpp` exists — `PlanscapeClient.cpp`/`LoginDialog.cpp`/`SettingsDialog.cpp` + `resources/` **absent**. ACAP entry points valid; **all menu handlers are `DGAlert` stubs**; `CollectElements()` returns empty. Hardcoded `https://api.planscape.app`. **Unaware of the server's `/api/archicad/*` push/keygen contract** — which is itself fully implemented server-side (`ArchiCADController.cs:87-277` + `ArchiCADHub.cs` + applied `BridgeKeyHash` migration). |
| **Tekla** | ABSENT (matches Phase 188 roadmap) | Zero `*tekla*` files repo-wide; only a comment-level host string. |

---

## Workflow end-to-end traces (producer → API → DB → SignalR → consumers)

| Workflow | Producer (Revit/mobile) | API | DB | SignalR broadcast | Web consumer | Mobile consumer | Verdict |
|---|---|---|---|---|---|---|---|
| **Issues** | ✅ (offline-queueable) | ✅ | ✅ | ✅ `IssueCreated/Updated` | ❌ dead shell + no SignalR; no photo surface | ✅ (configured build) | **partial** — web consumer broken |
| **Transmittals** | ✅ | ✅ | ✅ | ✅ `TransmittalUpdated` | ❌ correct code, dead at runtime; no realtime | ✅ | **partial** |
| **Warnings** | ✅ | ✅ | ✅ | ✅ `WarningsReported` | ❌ dead + unwired | ❌ **no subscriber** | **dead-event** — server broadcasts, nobody listens |
| **Healthcare commissioning** | mgas/pressure/anti-lig ✅; **water-flush ❌ local-only** (no endpoint, no persistence) | ✅ (3 of 4) | ⚠ dev-only tables | — | ❌ none | write-only | **partial** — no read-back surface |
| **Penetration sign-off** | ✅ QR+GPS+signature | ✅ | ⚠ dev-only table | — | ❌ none | write-only | **partial** — no read-back surface |

**SignalR summary:** hubs broadcast correctly from controllers; **`ClashNotifications` hub has no route** (clash kernel is in-Revit); there is no `WorkflowStateUpdates`/`IssueRaised` hub (issue events ride `NotificationHub`). The **dashboard shell loads no SignalR client**; `WarningsReported`/`TransmittalUpdated` have **no web subscriber**, and `WarningsReported` has **no mobile subscriber** either.

---

## Verdict roll-up

- **The dead body and stuck spinner are one client-side defect** in a shipped static asset (`dashboard.js` parse error) + one missing client function (`loadPublicConfig`). The server, auth, CORS, and controllers are overwhelmingly healthy.
- **The web shell is feature-thin by design** (read-only coordinator view) — meetings-create / camera / photo / 3D-viewer-entry / live updates were never built into it.
- **Real latent bugs** that surface once the shell is revived: the `tenantId` vs `tenant_id` claim mismatch (8 controllers → 500), missing `ExternalElementMapping`/IfcIngest migration (IFC ingest → 500 in prod), `NullModelConverter` default (no IFC→glTF), dashboard with no SignalR.
- **Integration gaps:** HVAC publish is a dead stub + route-mismatch; Revit session-restore and warnings-push are unwired; ArchiCAD/Bonsai plugins are scaffolds (match documented roadmap); Tekla absent (matches roadmap).

See `PLANSCAPE_ENHANCEMENT_BACKLOG.md` for the prioritized fix list.
