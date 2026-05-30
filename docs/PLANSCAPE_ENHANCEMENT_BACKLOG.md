# Planscape — Enhancement & Fix Backlog (prioritized)

**Source:** `PLANSCAPE_SERVER_AUDIT.md` (2026-05-30, branch `audit/planscape-deep-review`).
**Legend:** Effort = rough single-dev estimate. ⏱S ≤ 0.5d · M ≈ 1–2d · L ≈ 3–5d · XL > 1wk.

---

## P0 — Must-fix to make the authenticated app usable

| # | Item | Effort | Evidence | Notes |
|---|---|---|---|---|
| P0-1 | **Repair `wwwroot/js/dashboard.js` parse error.** Delete the orphaned duplicate `initProjectsMap` block (`:789–~1039`) and the duplicate `openNewProjectModal`/`showToast`/`initProjectsMap` declarations; restore one clean IIFE. Confirm with `node --check`. | S | `node --check` → SyntaxError `:872`; net −1 brace balance | **This one fix resolves symptoms 1, 2, and 4.** Highest ROI in the whole audit. |
| P0-2 | **Define `loadPublicConfig()`** in `dashboard.js` (fetch `GET /api/public-config`, set `CONFIG.mapboxToken`), or wrap the `:124` call in try/catch so `boot()` survives. | S | `dashboard.js:124` call, no definition; `PublicConfigController.cs:29` endpoint exists | Without this, `boot()` rejects even after P0-1. Do together with P0-1. |
| P0-3 | **Verify Redis liveness** for the running server; make `AuthController` Redis calls resilient (catch + degrade lockout pre-check) so a post-boot Redis drop can't stall login. | S | `Program.cs:424` sync connect; `AuthController.cs:93/103/150` | Confirm via `GET /health` (redis check) before assuming P0-1/2 are the whole story. |
| P0-4 | **Fix the `tenantId` vs `tenant_id` claim mismatch** in DashboardController, BoqController, ClassificationController, MfaController, ModelChecksController, OfflineManifestController, SsoController, SuitabilityController (read `tenant_id`). | S–M | `DashboardController.cs:23` vs `AuthController.cs:862` | Every authenticated call to these controllers currently 500s. Blocks KPI/BOQ/etc. the moment the shell is revived. |
| P0-5 | **Seed at least one demo project** for the demo tenant (or make the empty-projects state non-fatal so sidebar tabs still render). | S | `dashboard.js:149` "No projects available."; no non-platform seed | Otherwise a freshly-logged-in user still sees an empty body after P0-1/2. |

> **Branching recommendation:** P0-1 + P0-2 are the same file and the same root cause — **do them as one small branch/PR** (`fix/dashboard-revive`) and ship it first; it's verifiable in minutes and unblocks everything else. P0-3 (ops check), P0-4 (server claim fix), P0-5 (seed) are independent and can each be their own short PR. So: **one branch for P0-1+P0-2 together; separate branches for P0-3, P0-4, P0-5.** Do not bundle all P0s into one branch — they touch unrelated layers and have very different review/verify needs.

---

## P1 — Features visible in CLAUDE.md / mobile but missing from the web shell

| # | Item | Effort | Notes |
|---|---|---|---|
| P1-1 | **Wire SignalR into the dashboard shell.** Reference `signalr-shim.js` from `app/index.html`; subscribe `IssueCreated/Updated`, `TransmittalUpdated`, `WarningsReported`, `ComplianceChanged`. | M | Shim exists & works but is referenced only by `viewer.html`. Add missing event names. |
| P1-2 | **Add a "New meeting" create form** to the web Meetings tab (`POST /api/projects/{id}/meetings` is wired). | M | Currently read-only `renderList`. |
| P1-3 | **Surface site photos in the web shell** (gallery + issue-attachment thumbnails). Either embed `planscape-site/app/portal` or add a photos view to the dashboard. | M–L | `SitePhotos`/`PhotoAlbums` controllers are fully wired; web has no photo surface at all. |
| P1-4 | **Make the 3D viewer reachable** from the dashboard (the `viewer.html` link in `dashboard.js:1148` is dead behind the parse error — verify after P0-1; add a real model-conversion provider). | M | Also set `ModelConverter:Provider` (`ifcconvert`/`aps`) — default `NullModelConverter` means no IFC→glTF. |
| P1-5 | **Replace the `PLANSCAPE_MAPBOX_TOKEN` placeholder** with a real public token (or finish the `/api/public-config` server-driven path so it's configured once). | S | Cosmetic today; needed for the map view. |
| P1-6 | **Decide web↔mobile parity scope.** Document which features are intentionally mobile-only (camera, QR, penetration sign-off, healthcare commissioning, diary, biometric) vs. which need a desktop equivalent. | S | Currently undocumented; users expect parity. |

---

## P2 — Workflow integration gaps (BCC ↔ server, mobile ↔ server, read-back surfaces)

| # | Item | Effort | Notes |
|---|---|---|---|
| P2-1 | **Implement `Hvac_PublishToServer` for real.** Replace the `MergeRecoveryStubs.cs:289-290` `Task.FromResult(false)` stubs; align client routes to the server's `POST /hvac/snapshots` (not `/hvac/loads`,`/hvac/nc`,`/hvac/refrigerant`). | M | Currently a silent no-op + route-mismatch. |
| P2-2 | **Wire Revit warnings push.** Add a caller for `PushWarningsAsync` from `WarningsManager`; add a `WarningsReported` SignalR subscriber on web + mobile (server already broadcasts to nobody). | M | Dead-event today. |
| P2-3 | **Call `RestoreSessionAsync` at Revit startup** so DPAPI session persistence actually restores (no re-login after restart). | S | Defined but never invoked. |
| P2-4 | **Fix mobile `expo start --web` blockers:** set `EXPO_PUBLIC_API_BASE`; remove duplicate `<Tabs.Screen name="models">`; null-guard `activeProject` in dashboard `loadData`; fix `@/src/components/MemberPicker` → `@/components/MemberPicker`. | M | Four concrete bugs; each blanks content. |
| P2-5 | **Fix mobile offline-queue endpoint names:** `postIssueComment`→`addIssueComment`; add/rename `placeIssuePin`/`deleteIssuePin`/`upsertDiaryEntry` (or map to `createSiteDiary`/`updateSiteDiary`). | M | Offline comments/pins/diary silently fail on drain today. |
| P2-6 | **Wire `water-flush` healthcare screen to the server.** Add a `HealthcareController` water-flush endpoint + entity; replace the local-`useState`-only `log()`. | M | Only one of 4 healthcare screens is unwired. |
| P2-7 | **Add web/Revit read-back surfaces** for healthcare commissioning + penetration sign-off (currently mobile-write-only, no downstream consumer). | L | |
| P2-8 | **Add `[ProjectAccess]` membership checks** to HealthcareController + PenetrationsController (currently any authenticated token, any project). | S | Security gap. |
| P2-9 | **Fix `planscape-site` portal env var:** `components/portal/api.ts` should read the same var name as `.env.example` (`NEXT_PUBLIC_PLANSCAPE_API`), not `NEXT_PUBLIC_API_BASE`. | S | Local builds silently hit prod. |
| P2-10 | **Reconcile `DbSet<HvacSnapshot>` vs configured `HvacLoadSnapshot/NcSnapshot/RefrigerantSizing`.** | S | Schema inconsistency. |

---

## P3 — Deployment correctness & forward roadmap (documented scope)

| # | Item | Effort | Notes |
|---|---|---|---|
| P3-1 | **Generate the missing EF migration** for `ExternalElementMapping`/IfcIngest (and regenerate the model snapshot). Without it, `IfcController` 500s in prod. | M | Verified: not in the 73 `Data/Migrations` files, not in the snapshot. |
| P3-2 | **Fix the prod migration pipeline.** Either back-fill `.Designer.cs` companions + a current snapshot so `Migrate()` works, or formally adopt `EnsureCreated`/`CreateTables` as the deployment path. | L | `Program.cs:1243-1248` documents the broken `Migrate()` path. |
| P3-3 | **Rotate prod placeholder secrets:** MinIO `PlanscapeMinio2026!` (`docker-compose.prod.yml`), DB `Planscape2026!`, generate a real `Jwt:Key` via env. | S | |
| P3-4 | **ArchiCAD plugin** — implement `PlanscapeClient.cpp`/`LoginDialog.cpp`/`SettingsDialog.cpp` + resources; align to the server's `/api/archicad/*` push/keygen + `X-StingBridge-Key` contract (server side already done). | XL (~12wk, Phase 187 roadmap) | Scaffold-only today. |
| P3-5 | **Bonsai production operators (16)** + `stingtools-bonsai/planscape/client.py` wrapper to call the existing `ingest_ifc_data()` → `POST /ifc/data`; `handlers/` + `workflows/`. | XL (~8wk, Phase 186 Path B) | Scaffold-only today; diagnostic ops work. |
| P3-6 | **Tekla server-side connector.** | M (~2wk, Phase 188 roadmap) | Absent today. |
| P3-7 | **Add a `/hubs/clash` route + clash event relay** if live clash notifications to web/mobile are desired (clash kernel is currently in-Revit only). | M | |

---

## One-line triage

> Ship `fix/dashboard-revive` (P0-1 + P0-2) **today** — it's a ~1-hour fix that resurrects the entire authenticated web app. Then P0-3/4/5 as separate small PRs. Everything in P1–P3 is real but secondary to that single broken static asset.
