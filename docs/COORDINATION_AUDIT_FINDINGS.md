# Cross-Host Coordination & Live-Sync Audit — Findings

**Date:** 2026-06-03
**Scope:** End-to-end coordination + live sync across Revit, ArchiCAD, Tekla, and the Planscape cloud spine (Planscape.Server, StingBIM.Server, Planscape.Desktop, web viewer).
**Method:** Read-only investigation verified against source (Linux sandbox — C#/C++/Py signatures reasoned about, not compiled). Every claim cites `file:line` and is labelled **VERIFIED** (read from code) or **ASSUMED** (inferred from docs/partial trace).
**Status:** No code changed. Fix plan in §5 awaits approval.

---

## 0. Executive summary

The platform has an impressive *surface area* of cross-host machinery — `ExternalElementMapping`, `IfcController`, `ModelTransformController`, `GlobalIdRegistryController`, `FederatedCoherenceJob`, SignalR hubs, a web viewer with meeting co-presence — but the **load-bearing parts of "full sync + model coordination" are not connected end-to-end**. Three independent failure lines run through the whole system:

1. **Cross-host element identity does not round-trip.** The canonical key is the 22-char `IfcGlobalId`, but Revit geometry sync sends the 45-char Revit `UniqueId` in the `ifcGuid` slot (`GeometrySyncHandler.cs:148`), Revit tag sync leaves `IfcGlobalId` null until a separate stabilise+export step (`PlanscapeServerClient.cs:2070-2076`), and ArchiCAD mints its key by an *unverified* GUID compression (`StingBridge/sync/engine.py:42-46`). The cross-host resolve endpoint (`IfcController.cs:138-152`) therefore silently returns nothing for most real elements. **Cross-host issue/clash resolution is broken at the root.**

2. **The coordinate transform is computed and stored but never applied to rendered geometry.** Geometry is tessellated in each host's raw internal coordinates (`IfcTessellationJob`, `GlbSerializer.cs:101-113`), `getModelTransform()` has zero callers in the web client, and the coherence job only *detects* drift while telling the user to "apply a transform" that has no geometric effect (`FederatedCoherenceJob.cs:225`). **Revit / ArchiCAD / Tekla(IFC) models do not federate into one world space — they render at their own origins and drift apart.**

3. **The "live / bidirectional" parts are stubs or orphaned.** The ArchiCAD C++ add-on is a non-compiling scaffold; the realtime SignalR push path has no producer; Revit has no `/ifc/data` producer; Tekla does not exist as a plugin; sync is one-way host→cloud and full-re-ingest, not incremental deltas.

Layered on top: meeting join races model load (the "stuck at 0%" symptom), prod schema bootstrap is broken for several tables, and duplicate model rows arise from a missing uniqueness guard. Net: today the system is a **one-way, per-host upload-and-view tool with unverified identity and unaligned coordinates**, not a federated bidirectional coordination platform. This is corroborated by the repo's own `CONTRACT_ALIGNMENT.md:42` ("Drift 1 — Bonsai → POST /ifc/data (HARD BROKEN)") and `docs/CROSS_HOST_ROUND_TRIP_RUNBOOK.md:3-5` ("live run pending… never executed").

---

## 1. Per-host sync maturity matrix

Legend: **Real** = wired end-to-end & functional · **Partial** = works with significant gaps · **Stub** = scaffold/no producer · **Missing** = does not exist.

| Host | Authoring→export | Cloud ingest | Identity → `ExternalElementMapping` | Geometry → viewer | Bidirectional | Overall | Deciding evidence |
|---|---|---|---|---|---|---|---|
| **Revit** | Partial | Partial | **Stub** (wrong key) | Partial (no transform) | Missing | **Partial** | element data rides *obsolete* `/api/tagsync/sync` (`PlanscapeServerClient.cs:377,391`); geometry delta `federated-model/delta` (`:1234`) sends `UniqueId` as `ifcGuid` (`GeometrySyncHandler.cs:148`); no `/ifc/data` caller in C# (only Python posts it) |
| **ArchiCAD** | Partial (Python only) | Real (`/ifc/data`) | Partial (null HostDocGuid; unverified GUID) | Partial (separate GLB rail, no transform) | Stub (token-only, broken heuristic) | **Partial** | Python `SyncEngine`→`/ifc/data` works (`StingBridge/planscape/client.py:129`, test `tests/test_ifc_ingest.py:88-100`); C++ add-on all `"to be implemented"` (`StingPlanscapeAddon.cpp:10,68,81`); realtime `PlanscapeCloudPush.Enqueue` has no caller |
| **Tekla** | Missing | (generic IFC only) | Missing (column exists, no producer) | Missing | Missing | **Missing / Roadmap** | only the literal `MappingHosts.Tekla = "tekla"` (`MappingHosts.cs:15`) + dangling `TeklaGuid` column (`GlobalIdRegistryController.cs:111`); docs say "Phase 188/189, ~2 weeks, future" (`CLAUDE.md:1750-1753`) |
| **Cloud spine** | — | Real (ingest endpoints exist) | Real (table + resolve API correct) | Partial (renders raw coords) | Missing | **Partial** | `IfcController`/`IfcIngestService` upsert correctly; transform layer never applied to geometry (§3 BLK-2) |

**Bottom line by host:** Only **ArchiCAD via the Python bridge** has a genuinely working host→cloud→viewer chain, and even that is one-way, tag-only, and identity-fragile. Revit's "IFC ingest" is unfed; Tekla is vapor.

---

## 2. Actual cross-host sync + identity flow (as it exists today)

```
                         ┌──────────────────────── REVIT (C# plugin) ────────────────────────┐
                         │ tag data ──► POST /api/tagsync/sync   [OBSOLETE]                    │
                         │   payload carries revitElementId + UniqueId(45ch) + ifcGlobalId     │
                         │   ifcGlobalId = NULL until StabilizeIfcGuids + IFC export run        │
                         │ geometry ──► POST /api/projects/{id}/federated-model/delta           │
                         │   GLB node.ifcGuid = el.UniqueId  (45ch, NOT a real IfcGlobalId)     │
                         │   vertices = raw Revit-internal feet→m, NO basepoint/true-north      │
                         └─────────────────────────────────────────────────────────────────────┘
                                                       │
   ┌──────── ARCHICAD ────────┐                        ▼
   │ C++ add-on = STUB        │        ┌──────────────────────────────────────────────┐
   │ Python bridge (REAL):    │        │            PLANSCAPE.SERVER                    │
   │  ArchiCAD JSON API / IFC │  ───►  │  /ifc/data → IfcIngestService.IngestAsync      │
   │  drop folder (full       │ /ifc/  │    • upsert ExternalElementMapping             │
   │  re-ingest, poll@300s)   │  data  │      key=(Proj,IfcGlobalId,Host,HostDocGuid)   │
   │  key=compress(ACGuid)    │        │      HostDocGuid ALWAYS NULL from bridge        │
   │  [UNVERIFIED vs export]  │        │    • upsert TaggedElement (UniqueId=IfcGlobalId)│
   │  HostDocGuid=null        │        │  GLB ──► POST /models (separate, hash-deduped, │
   └──────────────────────────┘        │           no TenantId, no unique constraint)   │
                                        │  ModelTransform: computed+stored, NEVER applied│
   ┌──────── TEKLA ───────────┐         │  CoherenceJob: DETECTS drift only              │
   │ does not exist           │         └──────────────────────────────────────────────┘
   └──────────────────────────┘                        │
                                                        ▼
           ┌──────────── WEB VIEWER (coordination-viewer.js + meeting-sync.js) ────────────┐
           │  loads GLB per model in RAW coords (getModelTransform = 0 callers)              │
           │  → multi-host models render at their own origins → DRIFT                        │
           │  meeting join NOT gated on model load → "stuck at 0%" blank live session        │
           │  cross-host highlight: resolve(IfcGlobalId)→host id … returns ∅ for most elems  │
           └────────────────────────────────────────────────────────────────────────────────┘

   DESKTOP (Planscape.Desktop): chokidar file-watch → uploads WHOLE file to /documents
        … but watcher events are NEVER wired to the upload queue (detection ⊥ upload).
```

**Identity reality (VERIFIED):**
- Canonical key is `IfcGlobalId` (22-char) — `ExternalElementMapping.cs:4-7,40`.
- Revit geometry puts `UniqueId` (45-char) in the `ifcGuid` field — `GeometrySyncHandler.cs:148`, serialized at `GlbSerializer.cs:107-109`. **Cannot join the 22-char key.**
- Revit tag sync's true `ifcGlobalId` is null pre-stabilisation — `PlanscapeServerClient.cs:2070-2076`.
- ArchiCAD key = `ifcopenshell.guid.compress(element_guid)`, self-described "unverified against a live round-trip" — `StingBridge/sync/engine.py:42-46`.
- `HostDocumentGuid` is always null from the bridge, so federated ArchiCAD docs collapse onto one row — `StingBridge/planscape/client.py:101`, matched by `IfcIngestService` `m.HostDocumentGuid == hostDocumentGuid` (null==null).
- The cross-host round-trip that would *prove* identity holds has **never been run** — `docs/CROSS_HOST_ROUND_TRIP_RUNBOOK.md:3-5`; `tools/tests/cross_host_round_trip.py:185-188` (all tests SKIP, exit 0 with no inputs/fixtures).

---

## 3. Ranked issue list

### BLOCKERS

**BLK-1 — Cross-host element identity does not round-trip.** *(A. Identity)*
Three hosts emit three incompatible keys into a table whose only stable key is `IfcGlobalId`:
- Revit geometry: `GeometrySyncHandler.cs:148` (`string ifcGuid = el.UniqueId;`) → `GlbSerializer.cs:107-109`.
- Revit tags: `PlanscapeServerClient.cs:2070-2076` (`ifcGlobalId` null until stabilise+export).
- ArchiCAD: `StingBridge/sync/engine.py:42-46,320` (unverified compress).
**Impact:** `IfcController.Resolve` (`IfcController.cs:138-152`) returns ∅ for most elements → an issue/clash raised in one host cannot be located in another; meeting element-highlight no-ops cross-host. This is the central premise of the platform and it is broken. **VERIFIED.**
**Fix:** Make every host emit the *same* 22-char `IfcGlobalId`. Revit: use `ExportUtils.GetExportId` / read `IFC_GLOBAL_ID_TXT` in `GeometrySyncHandler` and gate sync on stabilisation. ArchiCAD: verify `compress(ACGuid)` equals the actual IFC-export GlobalId or key on the export GlobalId directly. Reject non-22-char ids server-side.

**BLK-2 — Coordinate transform is never applied to rendered geometry → no shared world space.** *(A. Coordinates)*
Geometry is tessellated/serialized in each host's raw internal coordinates: `IfcTessellationJob` accumulates raw IFC vertices; `IfcIngestController.cs:40-42` defers geometry build; `GlbSerializer.cs:101-113` writes nodes with no root transform and no base-point/true-north (`GeometrySyncHandler.cs:177-179`, no `BasePoint`/`TrueNorth`/`ProjectPosition` anywhere). `getModelTransform()` (`Planscape/src/api/endpoints.ts:2482`) has **zero callers**; `ModelTransformController` PUT only re-computes AABBs (`ModelTransformController.cs:146-166`); `FederatedCoherenceJob.cs:225` only *detects* drift.
**Impact:** Revit/ArchiCAD/Tekla models render at their own origins and survey points → they do not coincide in the viewer. Federated coordination (the whole point) is visually wrong. Units (m vs mm vs per-model scale) are also unreconciled (`CoordinateSystemController.cs:149-156` metres vs `ProjectModelTransform.cs:21` mm; dead scale branch `IfcIngestController.cs:621-623`). **VERIFIED.**
**Fix:** Either bake the survey-origin/transform into SceneNode+GLB coords at tessellation time, or have the viewer fetch `getModelTransform` per model and set the model group's matrix (translate + Z-rot + scale). Drive one canonical project unit and compute a real `ScaleFactor`.

**BLK-3 — Cross-host round-trip has never been run; identity contract unverified.** *(A. Substrate)*
`docs/CROSS_HOST_ROUND_TRIP_RUNBOOK.md:3-5` (live run pending), `:219-263` (results tables blank), `:89-108` (flags R1: "Revit may re-map GUIDs on export" — which would break BLK-1). `tools/tests/cross_host_round_trip.py:185-188,237-240,316-319` (every test SKIPs, exit 0, no committed fixtures). The single-host `tools/tests/round_trip.py` *is* really wired to `ifctester` (`:235-252`) but ships no fixture (`:49,338-341` → exit 4) and has dead code after `return` (`:224-228`).
**Impact:** The assumption underpinning BLK-1 (that Revit's export GlobalId is stable and equals what's mapped) is untested. **VERIFIED.**
**Fix:** Run the runbook once against real Revit + ArchiCAD + server; commit the evidence JSON and a `round_trip.py` fixture so CI can validate IDS conformance offline.

**BLK-4 — Prod schema bootstrap is broken for live tables.** *(C/D. Migrations)*
The migration set lacks `.Designer.cs` companions / current snapshot, so `Database.Migrate()` is documented as non-workable; prod relies on `EnsureCreated` short-circuit (`Program.cs:1284-1322`). Tables with **neither a migration nor a `PlatformSchemaPatcher` entry**: `HvacLoadSnapshots`/`HvacNcSnapshots`/`HvacRefrigerantSizing` (`PlanscapeDbContext.cs:1255-1270`), `ArchiCADEventLogs` (`:103` literal `// MIGRATION REQUIRED` comment, `:681-690`), `ElementGlobalIdRegistry` (`:215,1382`).
**Impact:** On a real prod DB, first write to these throws `relation does not exist`. The ArchiCAD ingest writes `GlobalIdRegistry` (`IfcIngestController.cs:548-561`) → cross-host registry fails in prod. **VERIFIED.**
**Fix:** Regenerate the migration set from the current model and ship `Migrate()`, or (stop-gap) add `CREATE TABLE IF NOT EXISTS` for these 5 tables to `PlatformSchemaPatcher`.

**BLK-5 — Meeting join is not gated on model load ("stuck at 0%" blank live session).** *(B. Meetings)*
`meeting-sync.js:48-54` `ready()` waits only for the viewer engine + signalR, not `modelRoot`, then immediately `JoinSession` (`:113-115`). The coordination layer downloads the whole GLB via `fetch().blob()` (`coordination-viewer.js:297-325`) with a 60s abort; during that download `#loadingProgress` is never updated (GLTFLoader gets a finished blob) → frozen "0%". Remote camera/highlight/section arrive against an empty scene; `selectAndZoomByGuid` silently no-ops (`viewer.html:1051-1061`).
**Impact:** A participant joining while their model downloads is "live" (presence, camera traffic) with a blank viewer — exactly the reported symptom. **VERIFIED.**
**Fix:** Defer `JoinSession` until the engine emits a `loaded` event (or `STING_VIEWER.modelRoot` exists); stream download progress to `#loadingProgress` during the blob fetch.

### HIGH

**H-1 — Revit has no `/ifc/data` producer.** Cross-host IFC ingest substrate is unfed by the flagship host; Revit element data rides the `[Obsolete]` `/api/tagsync/sync` (`PlanscapeServerClient.cs:377,391`; only Python posts `/ifc/data` per `Planscape.Server/docs/element-ingest-paths.md:78`). **Impact:** the "any host → /ifc/data → ExternalElementMapping" design (Phase 186) is half-built. **VERIFIED.** **Fix:** add `PostIfcDataAsync` + save-trigger, or formally bless tagsync as the Revit path and de-obsolete it.

**H-2 — ArchiCAD C++ add-on is a non-compiling stub; realtime push orphaned.** All menu handlers are `"to be implemented"` (`StingPlanscapeAddon.cpp:10,68,81,93,109`); `CMakeLists.txt:20-22` references `.cpp` files absent from disk → cannot link. `PlanscapeCloudPush.Enqueue` has no caller; `ArchiCADChangeListener.OnChanged` exists only in a comment. **Impact:** advertised "native ArchiCAD live integration" does not exist; only the Python bridge works. **VERIFIED.** **Fix:** route users to the Python bridge and document it as the only path, or delete the dead C++/realtime layers so they stop implying live sync.

**H-3 — Duplicate model rows on upload/sync.** No DB uniqueness on `(ProjectId, ContentHash)` — index is non-unique (`PlanscapeDbContext.cs:362-363`); dedup is app-level `FirstOrDefaultAsync`→`Add` (`ModelsController.cs:115-117,236`) → two concurrent same-hash uploads both insert. `req.Force` documented (`:106-109`) but never read. Separately, `IfcIngestController.cs:152-161` mints a *filename-keyed* stub model id and notifies the viewer to load it (`:222-227`) **without creating a `ProjectModel` row** → viewer fetch 404 / a second viewer entry for the same physical model. **Impact:** the reported duplicate rows + fileless rows that 404 on open. **VERIFIED.** **Fix:** unique filtered index `(ProjectId, ContentHash) WHERE DeletedAt IS NULL`, catch `DbUpdateException`→return existing; have IFC-ingest resolve/create the real hash-keyed `ProjectModel`.

**H-4 — `ModelsController.Upload` and BCF imports omit `TenantId` on insert.** `ProjectModel : ITenantScoped` but inserted with `TenantId = Guid.Empty` (`ModelsController.cs:210-235`); dedup query also omits TenantId (`:116`). BCF imports likewise (`BcfController.cs:116-129`, `IssuesController.cs:1147-1161`). **Impact:** tenant-scoped filtering/RLS misses these rows; cross-tenant exposure risk if filters are ever enforced on these tables. **VERIFIED.** **Fix:** set `TenantId` from the resolved project/claim on every insert; add to dedup predicate.

**H-5 — Federated/BCF endpoints are unbounded + N+1.** `BcfController.Export` loads ALL issues (`:56-79`); `BcfApiController.ListTopics` no pagination (`:69-74`); `FederatedModelController.GetElements` loads every element (`:167-176`); N+1 in `FederatedModelController.PostDelta` (`:108-138`, per-node query) and `ModelsController.GetFederationManifest` (`:580-590`). **Impact:** OOM / timeouts on real-size models; PostDelta is on the geometry sync hot path. **VERIFIED.** **Fix:** paginate/clamp; preload existing rows into a dictionary keyed `(SourceDocGuid, ElementId)` for delta upsert.

**H-6 — Desktop bridge: detection ⊥ upload, and whole-file re-upload.** `watcher.ts:82-121` emits `watcher:file` but `App.tsx:20-22` only stores it in `recentEvents` — nothing wires it to `uploadQueue.enqueue` (only the unreferenced `sync:enqueue` IPC does). When upload does run it reads & POSTs the entire file (`uploader.ts:161-178`), no delta/chunk/hash-skip. **Impact:** folder-watch produces UI events but no actual sync; manual syncs re-upload full models (≤500 MB) each save. **VERIFIED.** **Fix:** subscribe `fileWatcher.on('file')`→`enqueue` for syncable categories; hash-then-skip + resumable chunked upload.

**H-7 — Meeting reliability: no disconnect cleanup, no late-join/reconnect hydration, default-only backoff.** No `OnDisconnectedAsync` on `MeetingHub` (`MeetingHub.cs:1-95`) → ghost participants on crash/drop. Joiner receives no current camera/section/highlight/roster (`MeetingHub.cs:38-52`; client never calls `GET …/participants`). Bare `.withAutomaticReconnect()` (~42s, no `onclose`) (`meeting-sync.js:81,115-116`), and reconnect re-joins but does not rehydrate state. **Impact:** desync, ghosts, silent dead meetings. **VERIFIED.** **Fix:** add `OnDisconnectedAsync` emitting `ParticipantLeft`; push a room snapshot on join/reconnect; explicit backoff schedule + `onclose` handler.

### MEDIUM

**M-1 — ArchiCAD bidirectional conflict resolution is effectively a no-op.** `tagsync/timestamps` likely 404 → conflict detection silently skipped (`StingBridge/planscape/client.py:188-190`); the "<60s ⇒ Planscape wins" branch compares the PS timestamp to `now`, not a real lastModified (`StingBridge/sync/engine.py:445-451`). **Impact:** AC always wins; genuine cloud-origin edits lost. **VERIFIED.** **Fix:** deploy the timestamps endpoint and compare real AC vs PS lastModified.

**M-2 — Clash↔Issue loop is not cross-host identity-preserving + race/no-transaction.** `ClashRecord.ElementAGuid = a.Id.ToString()` (the FederatedElement row id, not IfcGlobalId) (`ClashDetectionJob.cs:103,106`); `PromoteToIssue` sets no `ModelElementGuid` (`ClashesController.cs:100-112`) → BCF viewpoint component IfcGuid empty; promote is check-then-act with no transaction (`:92-121`). No unique `(ProjectId, BcfGuid)` on `BimIssue` (`PlanscapeDbContext.cs:801-803`) + 3 racy import paths → duplicate issues. **Impact:** clashes can't be located cross-host; duplicate/duplicated issues. **VERIFIED.** **Fix:** carry `IfcGuid` into `ClashRecord`→`BimIssue.ModelElementGuid`; add `(ProjectId, BcfGuid)` unique filtered index; wrap promote in a transaction.

**M-3 — Meeting hub authorises tenancy but not project membership.** `HubTenantGuard.OwnsSessionAsync` checks `TenantId` only (`HubTenantGuard.cs:26-33`); no `ProjectVisibility.CanSeeProjectAsync` (cf. `ComplianceHub.cs:44-47`). `HubConnectionRegistry` is not wired to `MeetingHub`. **Impact:** any tenant user can join any meeting in that tenant; removed members keep receiving until disconnect. No cross-tenant leak. **VERIFIED.** **Fix:** add project-membership check on `JoinSession`; wire connection registry for eviction.

**M-4 — Deliverable state vs document CDE state not reconciled.** `InformationDeliverable.Status` and `DocumentRecord.CdeStatus` are independent (`DeliverablesController.cs` state machine is itself sound at `:225-269`); issue attachments hard-code `CdeStatus="WIP"` (`IssuesController.cs:786-787`). **Impact:** dashboards disagree across subsystems; inconsistent ISO 19650 status cross-host. **VERIFIED.** **Fix:** couple deliverable transitions to the linked document's CDE state.

**M-5 — Full re-ingest on every poll/file-save (no deltas) on the ArchiCAD path.** `StingBridge/sync/engine.py:159` (loop over all types each 300s poll), `watch/ifc_watcher.py:328` (parse whole IFC each save). **Impact:** costly/noisy on large models. **VERIFIED.** **Fix:** hash element props, skip unchanged; or revive a real delta path.

**M-6 — Missing `CancellationToken` + no `BimIssue` tenant query filter.** `IssuesController` core methods omit `ct` (`:72,122,467,726`); no global query filter on `BimIssue` (relies on `[ProjectAccess]`/`[ProjectInTenant]`); `BcfApiController.CreateTopic` NREs on a token without `tenant_id` (`:175`). **Impact:** abandoned requests keep running; isolation depends on attribute discipline. **VERIFIED.** **Fix:** thread `ct`; add a tenant query filter; use safe `GetTenantId()`.

### LOW

- **L-1** Raw-36-char GUID fallback violates `IfcGlobalId varchar(22)` when ifcopenshell absent → batch insert fails (`StingBridge/sync/engine.py:49,56` vs `PlanscapeDbContext.cs:668`). **Fix:** reject non-22-char before posting.
- **L-2** Deprecated `/api/tagsync/sync` Python path fabricates an md5 Revit id, writes no mapping (`StingBridge/planscape/client.py:138-172`). **Fix:** remove once no callers.
- **L-3** `round_trip.py` ships no fixture + dead code after return; bSDD 0% published (all 52 entries `proposed:true`). **Fix:** commit fixture; publish or relabel bSDD.
- **L-4** Dangling `TeklaGuid` column with no producer (`GlobalIdRegistryController.cs:111`). **Fix:** note manual-only until Phase 188.
- **L-5** Upload/watcher errors land in `console.error` / empty catches (`uploader.ts:130`, `sync.ts:61,69`) — never surfaced as toast. (The HTTP `client.ts:67-83` does surface typed errors.) **Fix:** surface error jobs + `watcher:error` to the UI.

---

## 4. Proposed flexible-sync design (Revit ⇄ ArchiCAD ⇄ Tekla)

Target: bidirectional federation through `shared/ifc` + `ExternalElementMapping` with shared coordinates and incremental deltas. Four pillars:

**4.1 One canonical identity, verified.**
- Every host emits the **22-char IFC GlobalId** as the only cross-host key. Revit derives it via `ExportUtils.GetExportId` (or `IFC_GLOBAL_ID_TXT`) at sync time, not the Revit `UniqueId`; ArchiCAD emits its IFC-export GlobalId directly (not a re-compressed element GUID); Tekla (when built) emits `Identifier.GUID`→IfcGuid.
- Server **rejects** any mapping/element whose `IfcGlobalId` is not exactly 22 chars (close L-1, surface the rejection).
- Always populate `HostDocumentGuid` (stable per-document hash) so federated sub-models keep distinct rows.
- Gate the whole pipeline behind the committed cross-host round-trip (BLK-3) so "GlobalId is stable across re-export" is a tested invariant, not an assumption.

**4.2 Shared world space, applied.**
- At ingest, each model stores a `ProjectModelTransform` derived from its IFC `IfcMapConversion`/site placement (survey origin, true-north, units→mm). Extend it to a full 4×4 (or translate + quaternion) so non-planar imports can be corrected.
- **Apply** it in exactly one place — either bake into SceneNode + GLB coords during tessellation, or have the viewer set each model group's matrix from `getModelTransform()`. Pick one; never both.
- `FederatedCoherenceJob` stays as a detector but its "apply a transform" advice now actually changes the render.

**4.3 Incremental deltas, both directions.**
- Host→cloud: send only changed elements (content-hash per element); the geometry path already has a delta drain (`GeometrySyncHandler.cs:67`) — extend the same model to tags and to the ArchiCAD bridge (replace the 300s full re-ingest).
- Cloud→host: a delta-pull (`GET …/elements?lastSyncUtc=`) already exists (`PlanscapeServerClient.cs:438-450`); add a **transaction-wrapped applier** in each host that writes server-origin changes back into the model (tokens, issue status, parameter edits) keyed by IfcGlobalId. This is what makes a clash/issue raised in the viewer or one host propagate to the others.
- Realtime fan-out via SignalR (`FederatedModelHub`) carries "element X changed in host H" so other hosts/viewer refresh incrementally instead of re-downloading.

**4.4 Cross-host coordination loop.**
- Clash/issue/BCF records carry `IfcGlobalId` end-to-end (close M-2), so `IfcController.Resolve` answers "which Revit/ArchiCAD element is this" for every record. Assignment/resolution/re-check then round-trips: raise in viewer → resolve to each host's element → push back via 4.3 → re-run clash on next delta.

This keeps `shared/ifc` as the contract every host speaks, `ExternalElementMapping` as the identity spine, and adds the two missing edges (apply-transform, write-back-applier) that turn today's one-way upload tool into a federated bidirectional platform.

---

## 5. Prioritized fix plan (awaiting approval — step-by-step)

Ordered so each step unblocks the next. Nothing implemented yet.

**Phase 0 — Prove the ground truth (no code risk).**
- P0.1 Run `docs/CROSS_HOST_ROUND_TRIP_RUNBOOK.md` against real Revit + ArchiCAD + a running server; commit the evidence JSON + a `round_trip.py` fixture. *Resolves BLK-3; tells us whether BLK-1's fix can rely on Revit export GlobalId stability.*

**Phase 1 — Make it not break in prod (DB integrity).**
- P1.1 Add unique filtered indexes: `ProjectModels (ProjectId, ContentHash) WHERE DeletedAt IS NULL`; `BimIssue (ProjectId, BcfGuid) WHERE BcfGuid IS NOT NULL`. Catch `DbUpdateException`→return existing. *(H-3, M-2)*
- P1.2 Set `TenantId` on every insert (ModelsController, BCF imports) + add to dedup predicates. *(H-4)*
- P1.3 Regenerate the migration set from the current model and switch prod to `Migrate()`; stop-gap: add the 5 missing tables to `PlatformSchemaPatcher`. *(BLK-4)*

**Phase 2 — Fix identity round-trip (the core).**
- P2.1 Revit `GeometrySyncHandler`: emit true IfcGlobalId (`ExportUtils.GetExportId`/`IFC_GLOBAL_ID_TXT`), gate on stabilisation. *(BLK-1)*
- P2.2 ArchiCAD bridge: emit the IFC-export GlobalId directly; always pass `HostDocumentGuid`; reject non-22-char ids server-side. *(BLK-1, L-1)*
- P2.3 Carry `IfcGlobalId` through clash→issue→BCF (`ClashRecord`→`BimIssue.ModelElementGuid`); wrap promote in a transaction. *(M-2)*

**Phase 3 — Fix shared coordinates.**
- P3.1 Store a real per-model transform from IFC site placement (units→mm, true-north); extend to 4×4. *(BLK-2)*
- P3.2 Apply it in one place (bake at tessellation OR viewer matrix via `getModelTransform`); reconcile units. *(BLK-2)*

**Phase 4 — Make sync live + incremental.**
- P4.1 Add a Revit `/ifc/data` producer (or de-obsolete tagsync as the blessed Revit path). *(H-1)*
- P4.2 Wire desktop `watcher:file`→`enqueue`; hash-then-skip + chunked upload. *(H-6)*
- P4.3 Element-level deltas on the ArchiCAD path; add cloud→host write-back appliers. *(M-5, §4.3)*

**Phase 5 — Meetings + resilience.**
- P5.1 Gate meeting join on model-loaded + stream download progress. *(BLK-5)*
- P5.2 `OnDisconnectedAsync` + join/reconnect state hydration + explicit reconnect backoff. *(H-7)*
- P5.3 Project-membership check on `MeetingHub.JoinSession`; wire `HubConnectionRegistry`. *(M-3)*

**Phase 6 — Performance + hygiene.**
- P6.1 Paginate/clamp unbounded fetches; fix N+1 in PostDelta & federation manifest. *(H-5)*
- P6.2 Thread `CancellationToken`; add `BimIssue` tenant query filter; safe `GetTenantId()`. *(M-6)*
- P6.3 Surface upload/watcher errors to UI; remove dead `Force`/deprecated tagsync/ArchiCAD C++ stubs (or finish them); publish/relabel bSDD. *(L-2, L-3, L-5, H-2)*

**Suggested first approval:** Phase 0 + Phase 1 (zero behavioural risk, prevents data corruption, and P0.1 tells us how much of BLK-1 we can fix mechanically). Then Phase 2.

---

### Appendix — verification confidence

- **Directly read by lead auditor:** `IfcController.cs`, `ExternalElementMapping.cs`, `ModelsController.cs:100-244` (TenantId omission + app-level dedup + unused `Force` confirmed), `IfcIngestService` upsert (HostDocumentGuid null-collapse confirmed).
- **Verified by focused sub-audits (file:line in each finding):** ArchiCAD/StingBridge chain; meetings/viewer/SignalR; Tekla/substrate/coordinates; coordination workflow/duplicates/migrations; Revit chain/desktop bridge.
- **Cross-corroboration:** the repo's own `CONTRACT_ALIGNMENT.md:42,326` ("Drift 1 — Bonsai → POST /ifc/data HARD BROKEN"; bridge posts legacy tagsync) and `CROSS_HOST_ROUND_TRIP_RUNBOOK.md:3-5` independently confirm BLK-1 and BLK-3.
- **ASSUMED (not fully traced):** existence/absence of a cloud→Revit model write-back applier (read getters + `ModelUpdated` hook found; no applier found — M-5/§4.3 treat write-back as missing); exact prod DB state vs `EnsureCreated` history.

---
---

# PART II — FIXES APPLIED (this session)

The audit above (Part I) is the as-found state. This part records what was
**changed**, with the verification status of each change. Constraint: Linux
sandbox, no Revit/.NET-Revit/ArchiCAD SDKs and no running Postgres — so JS/TS
and Python edits are syntax/compile-verified here, C# edits are written against
documented signatures but **not compiled**, and anything needing a real host or
DB is flagged explicitly.

## 6. Status of every ranked issue

| ID | Area | Status | Verification |
|---|---|---|---|
| **E (viewer 0%)** | Viewer load | **Fixed** | `node --check` clean; deps confirmed on disk |
| **BLK-1** Revit identity | Identity | **Fixed (unbuilt)** | C# uses `BuiltInParameter.IFC_GUID` (RevitAPI.dll) — needs Revit to run |
| **BLK-2** transform apply | Coords | **Fixed (viewer half)** | `node --check` clean; axis/unit convention needs a real federated model |
| **BLK-3** round-trip unrun | Substrate | **Documented only** | Needs real Revit+ArchiCAD+server+Bonsai |
| **BLK-4** prod schema | Migrations | **Fixed (patcher)** | C#; table names matched to EF convention — needs a prod DB to confirm |
| **BLK-5** meeting gating | Meetings | **Fixed** | `node --check` clean |
| **H-1** Revit `/ifc/data` | Sync | **Documented only** | Larger producer; see §9 |
| **H-2** ArchiCAD C++ | Sync | **Documented only** | Needs ArchiCAD SDK |
| **H-3** duplicate models | Workflow | **Fixed (unbuilt)** | C# unique index + race catch — needs DB |
| **H-4** TenantId on insert | Workflow | **Fixed (unbuilt)** | C# — needs DB |
| **H-6** desktop watcher⊥upload | Sync | **Fixed** | TS edit; bridges `fileWatcher.on('file')`→`enqueue` |
| **H-7** meeting disconnect | Meetings | **Fixed (unbuilt)** | C# `OnDisconnectedAsync` — needs server |
| **M-2** clash↔issue identity | Workflow | **Fixed (promote half)** | C#; job-side `ElementAGuid`→IfcGuid still open |
| **identity (ArchiCAD doc)** | Identity | **Fixed (watcher)** | `py_compile` clean; engine-side still open |
| M-1/M-3/M-5/M-6, L-* | various | **Documented only** | see §9 |

## 7. What changed, file by file

**E — viewer 0%-loading (all verified with `node --check`):**
- `wwwroot/coordination-viewer.js` — replaced `await res.blob()` with
  `streamGlbWithProgress(res)` (ReadableStream reader, real Content-Length %),
  extracted the load into a re-callable `loadModelGlb()`, added
  `showBootError/resetBootLoader/setBootProgress` that surface 401/404/timeout/
  parse failures **on the `#bootLoader` overlay with a Retry button** (not a
  transient toast), explicit "Not signed in" + `storage_missing` 404 messages.
- `Planscape/assets/viewer/*` — **divergence reconciled (E.1):** the build
  source-of-truth was the *older* r123 viewer; folded the newer r169-ESM
  `viewer.html` + `coordination-viewer.js` + the wwwroot-only `meeting-sync.js`
  back into `assets/viewer/` (now byte-identical to wwwroot), and added
  `meeting-sync.js` to the `SyncCoordinationViewer` MSBuild item list in
  `Planscape.API.csproj` so a `dotnet build` can no longer regress the deployed
  viewer or drop meeting-sync.
- E.4 verified: `/file` endpoint returns `File(..., enableRangeProcessing:true)`
  (Content-Length present → progress works); vendored `three.module.js` + DRACO
  (`vendor/three/addons/libs/draco/gltf/`) + meshopt + es-module-shims all exist.

**BLK-2 — coordinate transform applied in viewer (verified `node --check`):**
- `viewer.html` — `loadModel(url, transform)` now calls `applyModelTransform()`
  (translate mm→m, Z-rotation, scale; composed T·R·S) on the model root, guarded
  so absent/unconfirmed/identity = no-op (single-model projects unaffected).
- `coordination-viewer.js` — fetches `GET …/models/{id}/transform` (best-effort)
  and passes it in the `load` payload.

**BLK-5 + H-7 — meetings:**
- `meeting-sync.js` — `ready()` now also waits for `STING_VIEWER.modelReady`
  (set by the engine on load success **or** failure, and by the coordination
  layer when there's no model), with a 90s degrade-and-join fallback. No more
  "live but blank against an empty scene". (verified `node --check`)
- `viewer.html` — added `modelReady` flag + `markModelReady()` + exposed both on
  `STING_VIEWER`.
- `MeetingHub.cs` — added `OnDisconnectedAsync` that emits `ParticipantLeft` to
  every authorized session group on any disconnect (kills ghost participants).

**H-3/H-4 — duplicate models + tenant:**
- `ModelsController.cs` — dedup query + new-row insert now set/scope `TenantId`
  (`project.TenantId`); insert wrapped to catch the concurrent-duplicate
  `DbUpdateException` and return the winning row.
- `PlanscapeDbContext.cs` — unique filtered index `(TenantId, ProjectId,
  ContentHash) WHERE DeletedAt IS NULL` on `ProjectModel`.

**BLK-4 — prod schema bootstrap:**
- `PlatformSchemaPatcher.cs` — added idempotent `CREATE TABLE IF NOT EXISTS` for
  `HvacLoadSnapshot`/`HvacNcSnapshot`/`HvacRefrigerantSizing` (singular — these
  entities have no `DbSet`, so EF uses the CLR type name), `ArchiCADEventLogs`,
  `GlobalIdRegistry`, plus the H-3 unique index (non-fatal in `ApplyAsync` so a
  DB with pre-existing dupes doesn't abort the patcher).

**BLK-1 + M-2 + ArchiCAD identity:**
- `GeometrySyncHandler.cs` — `ifcGuid` now reads `BuiltInParameter.IFC_GUID`
  (the canonical 22-char key, same source as tag-sync + `ParamStampEventHandler`)
  and only falls back to `UniqueId` when absent.
- `ClashesController.cs` — promoted issue now carries `ModelElementGuid` from the
  clash element.
- `StingBridge/watch/ifc_watcher.py` — passes a stable `host_document_guid`
  (sha1 of resolved IFC path) so federated ArchiCAD docs keep distinct mapping
  rows instead of collapsing onto one. (verified `py_compile`)

**H-6 — desktop sync:**
- `Planscape.Desktop/src/main/index.ts` — `fileWatcher.on('file')` now calls
  `uploadQueue.enqueue(...)` (add/change only). Watching a folder now actually
  syncs instead of just emitting UI events.

## 8. As-built cross-host edges added

The two missing edges that turn "one-way upload + view" toward "federated
coordination" are now present (Revit/ArchiCAD halves needing host verification):

- **Identity edge:** Revit geometry + ArchiCAD ingest now emit the *same*
  22-char `IfcGlobalId` (Revit `IFC_GUID`; ArchiCAD doc-scoped), so
  `IfcController.Resolve` can join a viewer/issue/clash to a host element.
  (Revit half unbuilt; ArchiCAD watcher half verified.)
- **Coordinate edge:** the viewer now *applies* `ProjectModelTransform` instead
  of ignoring it, so multiple models can register in one world space. (Bake-at-
  ingest remains the more robust alternative — see §9.)

## 9. Verified vs needs-a-real-machine, and what's still open

**Verified in this sandbox (syntax/compile):**
- All viewer JS (`coordination-viewer.js`, `meeting-sync.js`) — `node --check`.
- Python bridge (`ifc_watcher.py`, `client.py`, `engine.py`) — `py_compile`.
- `viewer.html` engine module — extracted + `node --check`.

**Needs a real machine/DB to confirm (written, not run):**
- All C# (`ModelsController`, `PlanscapeDbContext`, `PlatformSchemaPatcher`,
  `MeetingHub`, `ClashesController`, `GeometrySyncHandler`) — needs `dotnet build`
  + a Postgres + a Revit session. Specific things to verify:
  - BLK-1 `BuiltInParameter.IFC_GUID` is populated on the elements you sync
    (run **Stabilize IFC GUIDs** first); confirm it equals the ArchiCAD/Bonsai
    `/ifc/data` key for the same physical element (this is BLK-3).
  - BLK-2 axis convention (Z-up vs Y-up) and the mm→m translation on a real
    two-model federation; adjust if models land rotated/offset.
  - BLK-4 table names — if any of the 3 HVAC entities actually has a `DbSet`
    elsewhere (none found), EF would pluralize; the `IF NOT EXISTS` makes a
    wrong guess harmless but the real table would still 404 until migrations are
    regenerated.
- BLK-3 cross-host round-trip — run `docs/CROSS_HOST_ROUND_TRIP_RUNBOOK.md`.

**Still open (documented, not applied — recommend next):**
- **H-1** add a real `PostIfcDataAsync` Revit producer (or de-obsolete tagsync as
  the blessed Revit path) so Revit feeds `ExternalElementMapping` directly.
- **M-2 job-side** set `ClashRecord.ElementAGuid/BGuid` to the `FederatedElement`
  IfcGuid in `ClashDetectionJob` (currently the row id) so the promote carry is
  truly cross-host; add a partial-unique on `ClashRecords.IssueId` for the race.
- **BLK-2 bake-at-ingest** alternative — apply the transform in `IfcTessellationJob`
  so AABBs/clash/GLB all share one space (viewer-apply covers the render only).
- **H-2** ArchiCAD C++ add-on — finish or formally retire; route users to the
  Python bridge meanwhile.
- **identity engine-side** pass `host_document_guid` from `StingBridge/sync/engine.py`
  (live ArchiCAD path) as well as the watcher.
- **M-1** (ArchiCAD conflict heuristic), **M-3** (meeting project-membership
  check), **M-5** (element-level deltas), **M-6** (CancellationToken + BimIssue
  tenant filter), **H-5** (pagination/N+1), **L-1..L-5** — as ranked in Part I.
- **EF migrations** — regenerate the set so prod uses `Migrate()` instead of the
  `EnsureCreated`+patcher path (the patcher is a stop-gap, not the cure).

## 10. Recommended next approval

Run `dotnet build` on `Planscape.Server` + a Postgres smoke test to validate the
C# changes (H-3/H-4/BLK-4/H-7/M-2), then a Revit session to validate BLK-1 and
the BLK-3 round-trip. After that, H-1 (Revit producer) + BLK-2 bake-at-ingest are
the highest-value remaining edges.
