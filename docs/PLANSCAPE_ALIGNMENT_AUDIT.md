# Planscape Platform — Cross-Surface Alignment Audit

**Date:** 2026-06-02 · **Mode:** read-only (no code modified) · **Scope:** Server API ↔ Revit plugin ↔ Web SPA ↔ Mobile ↔ SignalR ↔ Deployment.

This audit hunts **contract misalignments** — one component expecting something a
different component does not provide in the exact same shape/name/path. Every
finding cites `path:line` on **both** sides. Comment/string content was treated
as untrusted and verified against code.

> **Implementation status (branch `claude/implement-alignment-audit`):** all
> **21/21** findings ✅ RESOLVED. Commits: `925d28187` (mobile #2/#3/#11) ·
> `807917b79` (web #6/#17/#18/#4 + #1/#15 web) · `2ad088532` (server
> #16/#19/#10/#8/#12/#7 + #1 server) · `c24fb13e5` (plugin #9/#21 + #1 picker) ·
> `0d09241d1` (docker/env #5/#13/#14/#20). Each row below carries its resolving
> sha. Verification: mobile `tsc --noEmit` exit 0; web harness
> `Planscape.Server/tests/web-harness/align-audit.mjs` 24/24 pass; `dotnet build`
> Planscape.API + StingTools plugin 0 errors (no new warnings); `docker compose
> config` exit 0 for default/converter/pool. Contract fixes were applied to
> **every** consumer across all four surfaces, not only the cited one.

> **Headline (live bug):** The 3D viewer renders empty after publishing because
> the publish→view pipeline has **no IFC/OBJ/FBX→GLB conversion** and the viewer
> is **GLTFLoader-only**. The standalone-tab "doesn't read its query string"
> hypothesis is **disproven** — the query *is* read (by `coordination-viewer.js`).
> See [§ The Empty 3D Viewer](#the-empty-3d-viewer-conclusive-root-cause) for the
> exact break and one-line fix.

---

## Summary table (sorted by severity)

| # | Surface A (file:line) | Surface B (file:line) | Mismatch | Severity | Proposed fix |
|---|---|---|---|---|---|
| 1 | `ModelsController.cs:98,217,287` (stores `InferFormat`, no convert; `/file` returns raw bytes) | `coordination-viewer.js:323-325` → `viewer.html:782` (`new GLTFLoader().load(blobUrl)`) | Non-glTF publishes (.ifc/.obj/.fbx/.rvt) are never converted; GLTFLoader-only viewer can't parse them → silent "Failed to load model file" → empty canvas | **Blocker** | ✅ RESOLVED — 2ad088532 (server reject), 807917b79 (web format guard), c24fb13e5 (plugin picker) — DECISION: reject non-glTF at publish (converter handlers unwritten; plugin already exports GLB). All 3 surfaces aligned to glTF-only; comment documents how to relax once a converter emits GLB derivatives |
| 2 | `Planscape/src/api/client.ts:87-88` reads `data.token` | `AuthController.cs:391-396` refresh returns `{ accessToken, refreshToken, expiresAt }` (no `token`) | Mobile refresh stores `undefined` → falsy → force-logout on **every** 30-min token expiry despite valid server tokens | **Blocker** | ✅ RESOLVED — 925d28187 — `client.ts:86-88` → `setTokens(data.accessToken, data.refreshToken); return data.accessToken;` |
| 3 | `Planscape/src/services/realtimeClient.ts:4` default `…/hubs/project` | `Program.cs:1264-1277` `MapHub<>` — no `/hubs/project` (events live on `/hubs/notifications`) | Main mobile realtime client points at a non-existent hub route unless `EXPO_PUBLIC_HUB_URL` is set; placeholder host also dead | **Blocker** (if env unset) | ✅ RESOLVED — 925d28187 — `realtimeClient.ts` fallback derives from `EXPO_PUBLIC_API_BASE` + `/hubs/notifications` |
| 4 | `coordination-viewer.js:451,464,4608` navigate `/app/projects`, `/app/projects/{id}`, `/projects` | `wwwroot/app/` contains only `index.html` (hash-routed); `Program.cs:1048-1071` registers **no** `MapFallback` | 3 of 4 "leave viewer / go to project" exits hit non-existent paths → 404 | **High** | ✅ RESOLVED — 807917b79 — chose client-side hash routing (no server routing-model change): brand/CTA → `/app/#overview`, crumb → `/app/#models?project={id}` |
| 5 | `SceneNodesController.cs:108` reads `Converter__ApiBearer` (else `Unauthorized()`) | `docker-compose.yml:8-37` api `environment:` never sets `Converter__ApiBearer` | Converter→API scene-node ingest is always 401; converter sidecar can never deposit derivatives | **High** | ✅ RESOLVED — 0d09241d1 — added `- Converter__ApiBearer=${CONVERTER_API_BEARER:-}` to api env (value already in `.env`); verified via `docker compose config` |
| 6 | `signalr-shim.js:107` `conn.on('NotificationCreated')` | `NotificationService.cs:62,246` `SendAsync("Notification", …)` | Event-name drift (`NotificationCreated` vs `Notification`); viewer in-app notifications never fire | **High** | ✅ RESOLVED — 807917b79 — shim now registers `'Notification'` |
| 7 | `realtimeClient.ts:64` `.on('BoqSnapshotUpdated')` | No `SendAsync("BoqSnapshotUpdated")` anywhere in `Planscape.Server/src` | Mobile cost dashboard subscribes to an event the server never emits → never auto-refreshes | **High** | ✅ RESOLVED — 2ad088532 — DECISION: keep the feature; added `INotificationService.NotifyProjectEventAsync` and emit `BoqSnapshotUpdated` to `project-{id}` from `BoqController.PushSnapshot` + `IfcBoqSeedJob` |
| 8 | `TagSyncController.cs:186-190` `SendAsync("TagsUpdated"/"ComplianceUpdated")` to bare-GUID group on `TagSyncHub` | `dashboard.js:163` `.on('TagsUpdated')` on `/hubs/notifications`, joined to group `project-{id}` (`NotificationHub.cs:67`) | Wrong hub **and** wrong group prefix (`project-` vs bare GUID) → events never reach the dashboard | **High** | ✅ RESOLVED — 2ad088532 — also emit via `IHubContext<NotificationHub>` to `project-{id}` (legacy emits kept); test updated for new ctor arg |
| 9 | `StingCommandHandler.cs:8955` (fallback `http://localhost:5000`) · `WarningsManager.cs:4739` (no fallback, aborts) · `PlatformLinkCommands.cs:2820` (fallback `https://planscape-api.onrender.com`) | Same logical action "Open Web Dashboard" | 3 copies build the same `/app/#models?project=` path but disagree on fallback base URL + source chain → un-connected user gets localhost / a "connect first" dialog / onrender depending on dispatch path | **High** | ✅ RESOLVED — c24fb13e5 — added `PlanscapeServerClient.BuildAppUrl`/`BuildAppUrlForActiveProject` (ServerUrl→saved json→`DefaultAppFallbackUrl=localhost:5000`); routed all 3 through it. DECISION: localhost canonical (onrender is the retired host already dropped at :677) |
| 10 | `PlanscapeServerClient.cs:806` sends `?upcoming=true` | `MeetingsController.cs:48-52` binds only `status/page/pageSize` | `upcoming` filter silently ignored → returns all meetings (most-recent-past first) | **Med** | ✅ RESOLVED — 2ad088532 — added `[FromQuery] bool upcoming` + `Where(ScheduledAt>=UtcNow)`, soonest-first ordering |
| 11 | `realtimeClient.ts:69` `.on('MemberRevoked')` | `ProjectMembershipNotifier.cs:114` emits to `user_{userId}` group; mobile never calls `RegisterUser` | Event delivered to a group the mobile client never joins | **Med** | ✅ RESOLVED — 925d28187 — resolved by #3: `NotificationHub.OnConnectedAsync` auto-joins `user_{userId}` from the JWT, so the mobile client receives `MemberRevoked`/`AclChanged` once it connects to `/hubs/notifications` (no `RegisterUser` needed) |
| 12 | `PlanscapeRealtimeClient.cs:207` `.on("ModelUpdated")` on `/hubs/notifications` | `FederatedModelHub.cs:50` emits `ModelUpdated` on `/hubs/model` only | Plugin's ModelUpdated handler never fires (different hub) | **Med** | ✅ RESOLVED — 2ad088532 — `NotifyUpdate` now re-emits via `IHubContext<NotificationHub>` to `project-{id}` (both consumers — dashboard + plugin — live there); 3 callers pass it, AlignmentController.AutoAlign now passes both hubs (was null) |
| 13 | `.env.template:57` `Smtp__FromEmail=` | `SmtpEmailService.cs:31` reads `Smtp:FromAddress` | Configured From-address ignored → falls back to hard-coded `noreply@planscape.io` | **Med** | ✅ RESOLVED — 0d09241d1 — renamed to `Smtp__FromAddress=` |
| 14 | `.env.template:62` `Push__Firebase__ServiceAccountJson=` | `FirebasePushService.cs:38` reads `Firebase:ServiceAccountJson` | FCM service-account never loaded (Expo path still works) | **Med** | ✅ RESOLVED — 0d09241d1 — renamed to `Firebase__ServiceAccountJson=` + added `Firebase__ProjectId=` |
| 15 | `dashboard.js:1335` opens `/viewer.html?project=&model=` (Three.js GLB) | `issues.tsx:60` / `issue-detail.tsx:412` / `scanner.tsx:421` open `/viewer/index.html?model=*.xkt` (xeokit) | Two parallel 3D viewers with incompatible query grammars; deep-link (element/camera) only on the xkt path | **Med** | ✅ RESOLVED — 807917b79 — DECISION: formalise the shared `{project,model,element,camera}` grammar (lower-risk than consolidating viewers). Web coordination-viewer now reads `element`/`camera` and selects/focuses the element after load — the mobile xkt path already honoured them |
| 16 | `AuthController.cs:600-606` `Me()` emits raw enum `user.Role` (int; no `JsonStringEnumConverter` at `Program.cs:685`) | `Planscape/src/types/api.ts:24` types `role: string` | `/me` returns `"role": 2`; string comparisons silently fail | **Low/Med** | ✅ RESOLVED — 2ad088532 — DECISION: targeted `Me()` `Role = user.Role.ToString()` (matches login + other endpoints) rather than a global converter that would reshape every enum API |
| 17 | `dashboard.js:39-46,109-110` never writes `planscape_tenant` | `coordination-viewer.js:301` reads `planscape_tenant` for `X-Tenant` header | Dashboard→viewer hop has empty tenant header until user fills Settings (JWT carries tenant, so belt-and-braces) | **Low** | ✅ RESOLVED — 807917b79 — `seedTenantFromToken()` decodes JWT `tenant_id` on login + session-restore (tenant confirmed in JWT claims, AuthController.cs:964) |
| 18 | `dashboard.js:1870` `boot().catch(() => showLogin())` | `boot()` already distinguishes 401 vs unreachable (`:370-377`) | Top-level fallback assumes any uncaught boot rejection = "not authenticated" → transient errors bounce user to login | **Low** | ✅ RESOLVED — 807917b79 — top-level catch now mirrors the 401-vs-unreachable split |
| 19 | `Planscape/src/types/api.ts:29-30` comment claims `/me` emits `tenantName` | `AuthController.cs:600-606` `Me()` does not emit `tenantName` | Doc/type drift; `profile.tenantName` always undefined | **Low** | ✅ RESOLVED — 2ad088532 — `Me()` now emits `TenantName = user.Tenant?.Name` |
| 20 | `docker-compose.yml:11,170` set `ConnectionStrings__Migrations` | `Program.cs` only `GetConnectionString("Default")` (`:76,493`) | Env key never read (dead/misleading) | **Low** | ✅ RESOLVED — 0d09241d1 — DECISION: dropped both dead lines (the design-time `PlanscapeDbContextFactory` reads `CONNECTION_STRING`/`PG*`, not this key) |
| 21 | `StingCommandHandler.cs:8937` & `WarningsManager.cs:4658` both build `planscape://dashboard/{name}/{ts}` | Same clipboard action duplicated | Literals agree (not divergent) but un-DRY | **Low** | ✅ RESOLVED — c24fb13e5 — extracted `PlanscapeServerClient.BuildDashboardShareLink`; both call sites use it |

---

## The Empty 3D Viewer — conclusive root cause

**User report:** "viewer opens empty after a successful publish." The task brief
hypothesised that `viewer.html` only loads via a postMessage `load` command, so
the dashboard's `window.open('/viewer.html?…')` new-tab path renders empty.
**That hypothesis is disproven by the code** — but there *is* a real, provable
break. Here is the full trace.

### Pipeline trace

1. **Publish (server).** `ModelsController.cs:77` `POST /api/projects/{id}/models`.
   The handler hashes, stores the bytes (`:189-192`), and sets
   `Format = InferFormat(req.File.FileName)` (`ModelsController.cs:98,217`).
   The entire 95-245 body contains **no conversion, no Hangfire/BackgroundJob
   enqueue, no converter call** — verified line-by-line. Whatever format is
   uploaded is stored verbatim.
2. **Download (server).** `ModelsController.cs:246` `GET …/{modelId}/file`
   returns the raw stored stream with `GetMimeType(row.Format)`
   (`ModelsController.cs:287`) — no transcoding.
3. **Standalone tab loads (client).** `dashboard.js:1335`:
   `window.open('/viewer.html?project=${projectId}&model=${id}', '_blank')`.
4. **`viewer.html` itself does NOT parse its query string.** There is no
   `location.search` / `URLSearchParams` anywhere in `viewer.html` (confirmed by
   grep). Its only entry to geometry is `handleCommand({type:'load',…})`
   (`viewer.html:1115`) → `loadModel(url)` (`viewer.html:781`). **So the brief's
   observation about `viewer.html` is correct in isolation.**
5. **…but `coordination-viewer.js` compensates.** `viewer.html:1445` loads
   `<script defer src="./coordination-viewer.js">`. That script **does** read the
   query string: `coordination-viewer.js:48-49`
   (`params.get('project')` / `params.get('model')`), auto-runs via
   `whenReady(initCoordination)` (`:43`) → `bootstrap()` (`:234`), fetches
   `${apiBase}/api/projects/${projectId}/models/${modelId}/file`
   (`coordination-viewer.js:298,306`), blobs the response
   (`:323`), and posts the host command
   `handleHostCommand({type:'load', payload:{url: blobUrl}})`
   (`coordination-viewer.js:325`).
6. **Loader (client).** `viewer.html:782` — `loadModel` does
   `const loader = new GLTFLoader(); loader.load(url, …)`. **GLTFLoader parses
   only `.glb` / `.gltf`.** `coordination-viewer.js:323-325` performs **no format
   check** — it feeds the raw `/file` bytes straight in regardless of
   `row.Format`.

### The break

```
publish .ifc/.obj/.fbx/.rvt  →  stored verbatim (ModelsController.cs:98,217)
                             →  /file returns those bytes (ModelsController.cs:287)
                             →  coordination-viewer.js:323-325 blobs + load
                             →  viewer.html:782 new GLTFLoader().load(blob)  ✗ throws
                             →  coordination-viewer.js:329 catch → toast
                                "Failed to load model file" + hide bootLoader
                             →  EMPTY canvas
```

- The **GLB happy path works**: the plugin's primary publish option,
  "Export current 3D view to GLB" (`PublishModelCommand.cs:368` →
  `RevitGltfExporter.Export`), produces a real `.glb`; `Format=Glb`; GLTFLoader
  loads it. So a *GLB* publish renders.
- The **empty viewer bites every non-GLB publish.** `PublishModelCommand.cs:414`
  advertises `*.glb;*.gltf;*.ifc;*.obj;*.fbx` in its file picker, and the server
  `InferFormat` (`ModelsController.cs:472-484`) accepts `.ifc/.obj/.fbx/.rvt` — but
  none of those are convertible by the GLTFLoader-only viewer, and **no
  server-side converter ever runs** on the publish path. A converter sidecar +
  `ModelConverter__Provider` exist (docker `converter` profile) and a
  `SceneNodesController` ingest endpoint exists, but (a) the publish handler never
  invokes them and (b) the Three.js viewer consumes `/file`, not scene-nodes — so
  the converter is orphaned relative to this viewer (and is itself broken, see
  Finding 5: `Converter__ApiBearer` is never set on the api container).

### Exact break + one-line fix

- **Break:** `coordination-viewer.js:323-325` feeds non-glTF `/file` bytes into
  `viewer.html:782`'s `new GLTFLoader()`, because `ModelsController.cs:98/217/287`
  stored and returned a non-glTF format with no conversion.
- **Surgical one-line fix (guarantee "successful publish ⇒ viewable"):** reject
  non-glTF at the publish boundary so the empty render can't happen — at
  `ModelsController.cs:98`:
  ```csharp
  var format = InferFormat(req.File.FileName);
  if (format is not (ModelFormat.Glb or ModelFormat.Gltf))
      return BadRequest(new { error = "unsupported_viewer_format",
          message = "Only GLB/glTF are renderable; convert IFC/RVT/OBJ/FBX first." });
  ```
  (Equivalently, narrow `PublishModelCommand.cs:414` to `*.glb;*.gltf`.)
- **Proper fix (keep IFC-first workflow):** enqueue the existing converter on the
  publish path (IFC→GLB), store the GLB derivative, and have `/file` serve the
  derivative — *and* fix Finding 5 so the converter can actually deposit it.
- **Cheap UX hardening regardless:** make `coordination-viewer.js:323-325`
  branch on `activeModel.format` and surface a clear "this model is `<format>`,
  the viewer needs GLB" message instead of the generic load-fail toast (it
  already has `activeModel` at `:267`).

---

## Verified-aligned (checked, no mismatch)

So the report is faithful about what is **not** broken:

- **IFC GUID casing** — plugin `PlanscapeServerClient.CrossHost.cs:67` sends
  `?ifcGuid=`; `IfcController.cs:97` binds `ifcGuid`. The historical
  `ifc_guid` snake_case is gone from both sides.
- **JSON wire casing** — server uses System.Text.Json default **camelCase**
  (`Program.cs:685`, no override); SPA/mobile read camelCase; plugin Newtonsoft is
  case-insensitive and also tolerates PascalCase (`PlanscapeServerClient.cs:1582`).
  Login response fields (`accessToken/refreshToken/expiresAt/userName/role`) match
  on all four surfaces — **only mobile-refresh diverges** (Finding 2).
- **Auth request bodies + `Authorization: Bearer` scheme** — unanimous across
  plugin, SPA, viewer, mobile.
- **401-vs-unreachable in `dashboard.boot()`** — already correctly split at
  `dashboard.js:370-377` (the original freeze bug is fixed; only the
  belt-and-braces `:1870` catch remains, Finding 18).
- **`depends_on` profile-gating** — clean. `api.depends_on` excludes the
  `profiles:["pool"]` pgbouncer (`docker-compose.yml:38-49`); no active service
  depends on a gated target. The pgbouncer-class regression has not recurred.
- **Ports / bind-mounts** — container `:8080` (`Dockerfile:101`) published as
  `5000:8080` (`docker-compose.yml:7`); all wwwroot/storage mounts resolve.
- **Jwt / ConnectionStrings / Redis / ModelConverter / ClamAv / Storage / Cors /
  Billing / PluginUpdates env keys** — every `__`-key has a matching app reader
  (exceptions are Findings 5, 13, 14, 20).
- **Most client query/route params** (`issues?status`, `search?q&type&limit`,
  `documents?documentType&discipline&pageSize`, `photos?…`, `tagsync elements?…`,
  healthcare/penetrations route params) match server `[FromQuery]`/route bindings.
- **Mobile ArchiCAD hub** (`/hubs/archicad`) + plugin `/hubs/events`,
  `/hubs/notifications` subscriptions — event names + hub URLs align.

---

## Safe to auto-fix vs needs-decision

### Safe to auto-fix (pure contract correction, single correct answer)

- **#2** mobile refresh `data.token`→`data.accessToken` (`client.ts:87-88`).
- **#3** mobile hub fallback `/hubs/project`→`/hubs/notifications` (`realtimeClient.ts:4`).
- **#6** shim `'NotificationCreated'`→`'Notification'` (`signalr-shim.js:107`).
- **#10** add `[FromQuery] bool upcoming` to `GetMeetings` (`MeetingsController.cs`).
- **#13** `.env.template:57` `Smtp__FromEmail`→`Smtp__FromAddress`.
- **#14** `.env.template:62` `Push__Firebase__ServiceAccountJson`→`Firebase__ServiceAccountJson`.
- **#16** register `JsonStringEnumConverter` (or `Me()` `.ToString()` on role).
- **#18** `dashboard.js:1870` mirror boot's 401-vs-unreachable split.
- **#19** `Me()` emit `tenantName` (or correct the comment).
- **#20** drop dead `ConnectionStrings__Migrations`.
- **#5** add `Converter__ApiBearer` to api `environment:` (value already in `.env`).

### Needs a decision (product/architecture choice)

- **#1 / Empty viewer** — *reject non-GLB at publish* (fast, blocks IFC-first) **vs**
  *wire the converter to produce GLB derivatives* (keeps IFC-first, more work, also
  needs #5). Pick the workflow you want before fixing.
- **#4** `/app/projects` 404 — *change client links to `/app/#…` hash routes* **vs**
  *add a server SPA fallback for `/app/*`*. Both valid; affects routing model.
- **#9** open-web-dashboard fallback base URL — which default is canonical:
  `localhost:5000` (dev) vs `planscape-api.onrender.com` (prod)? Decide one const,
  then collapse the 3 copies through `PlanscapeServerClient.BuildAppUrl`.
- **#7** `BoqSnapshotUpdated` — implement the server emit **vs** remove the dead
  mobile subscription (depends on whether live BOQ refresh is a shipped feature).
- **#8 / #12** SignalR re-routing (`TagsUpdated`/`ComplianceUpdated`, `ModelUpdated`)
  — emit via `NotificationHub`/`project-{id}` **vs** have clients connect to the
  extra hubs. Hub topology decision.
- **#11** `MemberRevoked` group — mobile joins `user_` group **vs** server fans to
  project group. Authz-scope decision.
- **#15** two 3D viewers (xeokit `.xkt` mobile vs Three.js GLB web) — consolidate to
  one, or formalise a shared deep-link grammar. Roadmap decision.
- **#17** seed `planscape_tenant` from JWT in the dashboard — low risk but touches
  the auth/tenant model; confirm tenant is always in the JWT first.

---

### Method note

Findings were extracted with ripgrep on each surface (server route templates,
client URL literals, `SendAsync`/`.on` event names, compose env keys) and
cross-diffed. The viewer pipeline (Finding 1) was traced manually end-to-end and
is reproducible from the cited lines. No code was modified in this pass.
