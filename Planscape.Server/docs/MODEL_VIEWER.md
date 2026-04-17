# 3D model viewer

End-to-end scaffold that puts a **glTF/GLB 3D model viewer in the Planscape
mobile app**, backed by a Planscape-server model registry and populated by a
**new Revit plugin command** that captures the element-to-ISO-tag map.

## Why this exists

Before this change:
- Mobile app had a PDF viewer but no 3D model support.
- Server stored documents generically — no first-class 3D asset type.
- The Revit plugin exported IFC but didn't publish renderable geometry or
  bridge element identity to STING tags.

After:
- Mobile users tap an issue's model pin or open the model directly, rotate /
  zoom / pan the 3D geometry, tap an element to see its ISO 19650 tag +
  discipline + level, long-press to raise an issue pinned to that exact XYZ.
- Office / coordinator users publish their Revit view directly to Planscape
  from the plugin's BIM tab ("Publish Model to Planscape").
- All assets live behind the tenant-isolated `/api/projects/{id}/models`
  endpoint — same auth + rate limits as every other resource.

## Architecture

```
  Revit plugin                     Planscape server                  Mobile app
  ────────────                     ────────────────                  ──────────
  PublishModelCommand              POST /api/projects/{id}/models    ModelViewer (WebView)
    ├── user picks .glb/.gltf      ├── ProjectModel row created        ├── assets/viewer/viewer.html
    ├── scans visible elements     ├── storage via                     │     (three.js + GLTFLoader
    │   in the active view         │     IFileStorageService           │      + OrbitControls)
    ├── writes element-map JSON    │     (local FS / MinIO / S3)       │
    │   (guid → tag / cat / disc)  └── element-map sidecar             ├── RN ↔ WebView bridge
    └── PlanscapeServerClient      └── thumbnail sidecar               │     (pick / measure /
        .UploadModelAsync                                              │      section / pins)
                                                                       └── long-press → new issue
                                                                             anchored to guid+XYZ
```

## Server

### New entity: `ProjectModel`

See `Planscape.Core/Entities/ProjectModel.cs`. One row per uploaded model.

Columns:
- `ProjectId` — tenant-isolated via `Projects.TenantId` join
- `Name / Description / Discipline / Revision`
- `FileName / Format (Glb|Gltf|Ifc|Rvt|Obj|Fbx) / StoragePath / ContentHash`
- `FileSizeBytes / ThumbnailPath / ElementMapPath / ElementCount`
- Bounds: `BoundsMin{X,Y,Z}` / `BoundsMax{X,Y,Z}` (model units, for viewer auto-frame)
- `Units` (mm / m / ft) / `UploadedBy / UploadedAt / DeletedAt`

### Issue model anchor

`BimIssue` gains nullable `ModelId / ModelElementGuid / ModelX / ModelY /
ModelZ`. Any existing flow ignores them; the viewer reads them to render pins.

### Controller: `ModelsController`

Routes under `/api/projects/{projectId}/models`:
| Method | Path                                     | Role          | Purpose                         |
|--------|------------------------------------------|---------------|---------------------------------|
| GET    | `/`                                      | any authed    | list active models              |
| GET    | `/{modelId}`                             | any authed    | metadata                        |
| POST   | `/`                                      | Admin/Owner/Coordinator | upload (multipart)     |
| GET    | `/{modelId}/file`                        | any authed    | stream geometry (range-enabled) |
| GET    | `/{modelId}/element-map`                 | any authed    | stream JSON sidecar             |
| GET    | `/{modelId}/thumbnail`                   | any authed    | PNG preview                     |
| DELETE | `/{modelId}`                             | Admin/Owner/Coordinator | soft delete            |

Upload limits: 200 MB geometry, 5 MB element map, 2 MB thumbnail.
Content SHA-256 hashed on upload → duplicate uploads return HTTP 409 with the
existing model id (saves bandwidth on repeat publishes).

### Migration

`20260419000000_AddProjectModelsAndIssueAnchors.cs` adds the table + the 5
`Issues.Model*` columns + the `IX_Issues_ModelId` index. EF's model snapshot
will auto-regenerate on the next `dotnet ef migrations add` — we didn't
hand-edit the snapshot here.

## Mobile

### `react-native-webview`

New dep (`package.json`). The viewer is HTML + JS delivered via the WebView
rather than an `expo-gl` native renderer — zero native build steps, works in
Expo Go, and swapping the WebView HTML is a text edit.

### Bundled viewer: `assets/viewer/viewer.html`

Self-contained three.js scene:
- OrbitControls (1-finger rotate, 2-finger pan + pinch zoom)
- GLTFLoader for glb/gltf
- Ray-pick on tap → highlights + emits element GUID + XYZ
- Long-press on mesh → emits `placeIssue` to RN with the mesh GUID + meta
- Measure tool (two-tap distance)
- Horizontal section plane with slider
- Issue pins (billboards, colour-coded by priority)
- Discipline layer toggle (read from the element map)

Three.js itself is pinned to r160 and fetched via `curl` into
`assets/viewer/` (see that directory's README). The viewer shows a clear
error page if those files are missing rather than a blank WebView.

### `ModelViewer` React component

`Planscape/src/components/ModelViewer.tsx`. Imperative handle exposes:
- `setTool("pick"|"measure"|"section"|"pin")`
- `fit()` / `clearMeasure()` / `clearHighlight()`
- `setPins(pins)` / `addPin(pin)` / `clearPins()`
- `setDisciplineVisible(code, visible)`

Event callbacks: `onPick`, `onPlaceIssue`, `onPinTap`, `onMeasure`,
`onToolChanged`, `onError`.

### Screens

- `app/models/index.tsx` — list models with thumbnail / size / uploader / date
- `app/models/[id].tsx` — full-screen viewer, wires API metadata + element-map
  fetch + existing-issue-pin population + "create issue here" navigation to
  `/issues/new` with the 3D anchor params preloaded
- `app/models/_layout.tsx` — stack navigator shell

Routes live outside `(tabs)` so the viewer is full-screen; add a tile / menu
entry on Dashboard or Documents to surface them.

### Dependencies

`package.json` adds:
- `react-native-webview`
- `expo-asset` (for resolving the bundled HTML to a file URI)

Run `npm install` once — no native configuration needed.

### `metro.config.js`

Teaches Metro to treat `.html / .gltf / .glb / .bin / .ifc` as static assets so
`require("../../assets/viewer/viewer.html")` resolves to an asset ref.

## Revit plugin

### `PublishModelCommand`

`StingTools/BIMManager/PublishModelCommand.cs`. Workflow:
1. Refuses if the user hasn't signed in to Planscape.
2. Lists Planscape projects, user picks one.
3. Open-file dialog for the geometry (glb / gltf / ifc / obj / fbx). Revit
   doesn't ship a glTF writer — any external exporter that preserves the
   Revit UniqueId under `userData.extras.guid` works (SimLab, rvt2gltf,
   Dynamo "Rhythm.ExportToGltf", APS Model Derivative).
4. Scans the active 3D view's visible elements via `FilteredElementCollector`,
   writes an element-map JSON (`{ guid → { tag, name, category, discipline,
   level, elementId } }`) alongside the geometry.
5. Uploads both to `/api/projects/{id}/models` via the new
   `PlanscapeServerClient.UploadModelAsync`.

Command tag: `PublishModelToPlanscape` — wired in `StingCommandHandler.cs`.

## Known follow-ups

- **Server-side IFC→glTF conversion.** The upload endpoint accepts IFC, but
  the mobile viewer only renders glTF/GLB. Add a Hangfire job that calls
  `IfcConvert` / Autodesk APS to produce a renderable `.glb` sidecar when a
  user uploads IFC.
- **Thumbnail auto-generation.** Currently the plugin / admin uploads a PNG;
  in production we'd render one headlessly on upload (e.g. with `three.js`
  in a Node sidecar) so mobile list loads a preview without downloading the
  whole glb.
- **Progressive download.** For 100 MB+ models, hand the WebView a
  Draco-compressed + meshopt-encoded glb so the first-paint is quick even on
  3G. The viewer already supports Draco if the loaders include
  `DRACOLoader.js` next to `GLTFLoader.js`.
- **AR mode (iOS).** `viewer.html` could call `WebXR` when available to put
  the model in the user's site — gated by device support, not shipped here.
- **Mobile issue-anchor rendering.** Currently the `/issues/new` screen
  receives `modelId / modelElementGuid / modelX / modelY / modelZ` via query
  params; wire these into the existing create modal once it's refactored to
  accept initial values.
