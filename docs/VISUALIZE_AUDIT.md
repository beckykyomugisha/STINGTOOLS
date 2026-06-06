# Viewer Visualize — audit & change log

### P1 — full 8-token ISO 19650 tag in Properties · marker `iso-tokens` (coordination-viewer) · served + Revit-build verified
New **"ISO 19650 Tag"** group in the selected-element panel: DISC · LOC · ZONE · LVL · SYS · FUNC · PROD · SEQ
+ the assembled tag string. Present tokens show their value (DISC falls back to the derived discipline);
absent ones show **"— re-export from Revit"**. `tokenValue` extended for LOC/ZONE/SEQ/TAG.
- **Exporter (needs RE-PUBLISH):** `PublishModelCommand` tagged-element map now also emits `zone/func/prod/seq`
  (was only disc/loc/lvl/sys/status/tag) so the missing tokens populate after a re-publish. Build 0 warnings
  (Revit 2025).
- Served proof: minified bundle keeps "ISO 19650 Tag" + "re-export from Revit" + marker `iso-tokens`.
  PENDING-HUMAN-VERIFY: select an element — present tokens show, ZONE/LOC/SEQ hint until a re-publish.

### Realistic exposure + depth tune · marker `realistic-depth` (coordination-viewer) · served-verified
Realistic was washing flat white-plaster models out. Tuned for form: `toneMappingExposure` 0.8→**0.7**,
`environmentIntensity` 0.55→**0.5**, and the flat fill is **dampened** while Realistic is on (hemisphere ×0.35,
ambient ×0.25, key directional ×1.1 — stored as `_legacyFill` and restored exactly on OFF) so the IBL's soft
directional variation provides an ambient-occlusion-like depth gradient. An in-app toast on entering Realistic
sets expectations: **"Realistic — lit view. Full detail needs textures (re-publish with
PLANSCAPE_EXPORT_TEXTURES)."** colour-only materials are matte by nature.
- **TODO (postprocessing):** true SSAO / contact shadows need an EffectComposer pass — deferred (the
  fill-damp + IBL is the low-risk depth improvement; no postprocessing dependency added).
- **Textures embedding?** The `[tex]` SUMMARY only exists after a Revit re-publish with
  `PLANSCAPE_EXPORT_TEXTURES=1` (current served models were published with textures OFF, so there's no
  `[tex]` log yet) — confirm on the Revit machine per `docs/EXPORTER_TEXTURES.md`.
- Served proof: viewer.html has `toneMappingExposure = 0.7` + `_legacyFill` + the hemi-damp; minified
  coordination-viewer.js keeps the texture-note + marker `realistic-depth`. PENDING-HUMAN-VERIFY (incognito):
  white-plaster model reads with form, not washed; OFF restores the legacy look exactly.

### Navigation — "← Back" + clickable breadcrumb · marker `nav-back` (coordination-viewer) · served-verified
Added a visible header **`← Back`** button: native `history.back()` when there's a prior entry (dashboard
restores from bfcache — no forced refresh), else falls back to the project dashboard / projects home (and
posts `navigateBack` inside the RN WebView). The breadcrumb was already clickable (PLANSCAPE → `/app/projects`,
project name → `/app/projects/{id}`); opening the viewer is a normal `location.href` navigation, so browser
Back already leaves a history entry. Served proof: viewer.html has `id="btnBack"`; minified bundle keeps the
`#btnBack` binding + marker `nav-back`. PENDING-HUMAN-VERIFY (incognito): Back returns to the dashboard
without a refresh; brand/project crumbs navigate.

### Clash/issue marker toolbar toggles · marker `marker-toolbar` (coordination-viewer) · served-verified
The clash/issue marker show-hide was buried in View ▾. Now surfaced as labelled toolbar buttons
**`▣ Clashes`** / **`⬤ Issues`** (next to View), in addition to the View-menu items — they call the same
`toggleClashMarkers`/`toggleIssueMarkers` and reflect on/off state (dim + strike when off, via
`paintMarkerBtn`). Default on. Served proof: viewer.html has `id="tbClashMarkers"` + `id="tbIssueMarkers"`;
minified `coordination-viewer.js` keeps the `#tbClashMarkers` binding + marker `marker-toolbar`. (View ▾
items still present — no regression.) PENDING-HUMAN-VERIFY (incognito): clicking each hides/shows the
red/orange boxes + issue markers and dims the button.

### Realistic render mode · marker `realistic-mode` (coordination-viewer) · served-verified
Adds **Realistic** to the View menu (alongside Shaded / Wireframe / X-Ray / Ghosted). Procedural IBL via
`PMREMGenerator.fromScene(new RoomEnvironment())` (no asset) → `scene.environment` + `ACESFilmicToneMapping`
+ `SRGBColorSpace` while active; OFF restores the legacy `NoToneMapping` + `LinearSRGBColorSpace`.
- **ESM importmap path:** `RoomEnvironment` vendored to `vendor/three/addons/environments/RoomEnvironment.js`
  (imports only from `three`, importmap-resolvable; PMREMGenerator is core, already in the full
  `three.module.js`). Tree-shaking guard: it's a verbatim-served vendored file under the importmap — no
  bundler/minify touches it; served `RoomEnvironment.js` → **HTTP 200**, no "undefined" crash.
- **Renderer-global, not a per-mesh lens:** `STING_VIEWER.setRealistic(on)` builds the env once + caches it;
  `coordination-viewer.setRenderMode('realistic')` calls it. `applyVizModes` treats `'realistic'` like
  `'shaded'` for per-mesh resolution, so it's the **base appearance** and colour-by / ghost / hide still
  override on top. **Clear / resetVisualization** turns it OFF (returns to the original look).
- Served proof: viewer.html carries the import + `setRealistic` + `id="vRealistic"`; minified
  `coordination-viewer.js` keeps `setRealistic` + the `'realistic'` literal + marker `realistic-mode`.
- PENDING-HUMAN-VERIFY (browser): select Realistic → reflections/lighting appear on MeshStandard materials;
  colour-by/ghost still override; Clear returns to base; pairs with Phase-2 textures once published.

### Discipline misclassification — exporter root fix + viewer safety-net · marker `disc-safetynet` · served + Revit-build verified
The exporter stamped the WRONG DISC (bare "fixture" → P before E; no lighting→E; toposolid → S), and the
viewer's `discOf` trusts the stamped token — so Lighting Fixtures read as Plumbing, Toposolid as Structural,
and the P count was inflated.
- **A1 root fix (`PublishModelCommand.DeriveDisciplineFromCategory`, StingTools — needs RE-PUBLISH):** rewritten
  to mirror `discOf`'s RULES exactly — order: M → **E (incl. lighting/luminaire/conduit/cable/data/switch/
  socket/panelboard)** → FP → **P (specific; never bare "fixture")** → S → A (incl. toposolid/site). Build
  0 warnings against Revit 2025.
- **A2 viewer safety-net (`discOf`, marker `disc-safetynet` — no re-publish needed):** new
  `categoryOverrideDisc(category)` lets the category override a STALE wrong stamp ONLY for the known-mis-stamped
  categories (lighting/electrical-devices → E; toposolid/site → A) — always-correct overrides; every other
  category still trusts the stamp. So on the CURRENT export: shade-only Electrical now includes lighting
  fixtures+devices, toposolid ghosts under Architecture, and the P count drops to real plumbing.
- Served proof: minified bundle keeps `toposolid` override + marker `disc-safetynet`. PENDING-HUMAN-VERIFY
  (incognito): BY-DISCIPLINE — lighting under E, toposolid under A, P = real plumbing only; (Revit) re-publish
  → stamped DISC correct at source.

### Properties "no data" hints + full field coverage · marker `props-hints` (coordination-viewer) · served-verified
When Materials / Quantities / Property-sets are ABSENT from the element-map, the selected-element panel now
shows a muted per-group hint ("— no material data — re-export from Revit with PLANSCAPE_EXPORT_TEXTURES to
populate" / "— no area/volume/quantity — re-export with quantities" / "— no IFC psets / classification —
re-export") so it's clear it's a DATA GAP, not a viewer bug. All PRESENT fields are already shown (N6 generic
Identity / Dimensions / Performance / Properties / Relationships + nested psets/classification path); Cost
already shows "— no cost data". Served proof: minified bundle keeps `PLANSCAPE_EXPORT_TEXTURES` + "no IFC
psets" + marker `props-hints`. PENDING-HUMAN-VERIFY: an element with no quantities shows the hints; one with
psets renders them (no hint).

### Realistic exposure tune + glass STOPGAP · marker `realistic-glass` (coordination-viewer) · served-verified
- **Exposure:** Realistic now sets `toneMappingExposure = 0.8` + `scene.environmentIntensity = 0.55` so flat
  untextured white materials read as LIT, not blown out (OFF restores 1.0 / NoToneMapping / LinearSRGB).
- **Glass STOPGAP (heuristic, labelled):** until the exporter ships alpha (Phase 2 `generic_transparency` →
  `alphaMode=BLEND`), a `◇ Glass (heuristic)` View toggle (and Realistic itself) makes glass-ish CATEGORIES
  (Windows / Curtain Panel / Curtain Wall / Glazing / Storefront / Glass; mullions excluded) semi-transparent
  (`transparent`, opacity 0.3, `depthWrite=false`). Decoupled from the appearance engine — `maybeApplyGlass()`
  runs after `applyVizModes`, clones the material the engine just assigned per glass mesh (reversible via
  `_glassSrc`), so it never fights the layered model / colour-by / ghost / selection. Toast clearly flags it as
  "not real data — export from Revit for true alpha".
- Served proof: viewer.html has `id="vGlass"` + `toneMappingExposure = 0.8` + `environmentIntensity = 0.55`;
  minified `coordination-viewer.js` keeps `glassMode` + marker `realistic-glass`.

### Clash / issue marker toggles · marker `markers-toggle` (coordination-viewer) · served-verified
Two independent View-menu toggles (default ON) to show/hide the clash markers and the issue markers.
- Clash markers are red (`0xEF4444` hard) / orange (`0xF59E0B` soft) **wire boxes** (`clashPins`); issue
  markers are priority-coloured **spheres** (`issuePins`) — both already distinct shapes from the
  orbit-pivot indicator, and the toggle only flips those two marker groups' `.visible`, never the pivot.
- `state.clashMarkersVisible` / `state.issueMarkersVisible` flip via `toggleClashMarkers()` /
  `toggleIssueMarkers()`; `placeClashPins`/`placeIssuePins` re-apply the flag so a data refresh/rebuild
  keeps the chosen visibility. New menu items `#vClashMarkers` / `#vIssueMarkers`.
- Served proof: viewer.html carries `id="vClashMarkers"` + `id="vIssueMarkers"`; minified
  `coordination-viewer.js` keeps the `clashMarkersVisible` state key + marker `markers-toggle`.
- PENDING-HUMAN-VERIFY (browser): toggle each off/on → the red/orange boxes and the issue spheres
  hide/show independently; the pivot indicator is unaffected.

## Phase 2 — real Revit material textures in the glTF exporter · COMPILE-UNVERIFIED (verify in Revit + re-publish)
`StingTools/BIMManager/RevitGltfExporter.cs` previously wrote only flat `baseColorFactor`. Phase 2 adds a
real PBR texture path (Revit plugin code — **cannot `dotnet build` in this sandbox**; signature-checked
against the documented Revit API, version-sensitive bits marked `TODO-VERIFY-API`).
- **Toggle** `RevitGltfExporter.ExportTextures` / `Export(…, exportTextures)` / env `PLANSCAPE_EXPORT_TEXTURES=1`
  (wired in `PublishModelCommand`). **Default OFF** → lean coordination / low-bandwidth exports unchanged;
  ON for presentation / as-built. Cost/area/volume (E4) emission untouched.
- **Appearance resolve** (`OnMaterial`): Material → `AppearanceAssetId` → `AppearanceAssetElement.
  GetRenderingAsset()` → `generic_diffuse` colour + connected `unifiedbitmap_Bitmap` (diffuse image),
  `generic_glossiness`→roughness, `generic_is_metal`→metallic, `generic_transparency`→alpha,
  `generic_bump_map`→normal. Per-material cache; ANY failure → flat-colour fallback (never breaks export).
  Missing image paths resolve-or-skip gracefully.
- **UVs** (`OnPolymesh`): `TEXCOORD_0` from `GetUVs()`/`NumberOfUVs`, padded (0,0) for alignment; real-world
  scale/offset/rotation applied via **KHR_texture_transform** (scale = 1/realWorldScale) rather than baking.
- **glTF texture graph** (`WriteGlb`): GLB-embedded `images[]` (PNG/JPEG; others skipped; >8 MB skipped —
  downscale TODO), `samplers[]` REPEAT, `textures[]`, `pbrMetallicRoughness.baseColorTexture` (+ optional
  `normalTexture`), `alphaMode=BLEND` when translucent. **Dedupe** images by path + materials by appearance
  hash (`MaterialDef.Key`).
- **Verify in Revit 2025/2026/2027** on a textured model (brick/wood/tile): re-publish → load in the web
  viewer → patterns at correct scale; colour-only materials unchanged; transparency reads; GLB size
  reasonable via dedupe; resolver hit-rate + cost path unaffected. Pair with viewer Realistic mode (NOT yet
  implemented — see below) for lighting.
- Caveats: Revit appearance-asset API is version-sensitive; some texture paths resolve only where the
  material library is installed; UVs exist only for textured materials; KHR scale/rotation units need a
  Revit check.



Living record of the visualize/interaction work and the proactive audits (Parts C–E of the
combined viewer prompt). Each entry: what changed / what was found, root cause, fix, and the
served-artifact proof. The deployed bundle is the minified `dist/` (build.mjs) served by the
container — every item is verified with `docker compose build --no-cache api` then a marker
grep on the **served** file, not the source.

## Invariants (must not regress)
- Layered model: appearance (`_vizMode`, single `applyAppearance()` resolver, one `_trueOrig`
  store) ⊥ selection (emissive clone overlay, own set). Deterministic colours from sorted
  distinct values.
- Orbit (modelPivot Y-up + native OrbitControls), camera **getter** (ortho-safe), minimap
  inset, true Z-up coordinate readout, and the FITFIX (size-based fit — `Sphere` is tree-shaken
  from the bundle, never call `getBoundingSphere`).

## Marker
`coordination-viewer.js` logs `[viewer] STING_VIZ_BUILD <tag>` at init; the `<tag>` is bumped
per commit so the STEP 0 gate can prove the running container serves that exact change.

---

## Part C — Visualize flexibility

### C1 — per-discipline/-category continuous transparency  ✅ served `C1-transparency`
Each BY-DISCIPLINE / BY-CATEGORY row gains a continuous opacity slider (0–100%, replacing the
binary ghost). Wired through the layered resolver: precedence `hide > transparency(slider) >
ghost(button) > colour > show`. `state.vizTransp` (key→opacity) drives a cached shared
transparent material per opacity bucket (`transMaterial`, tagged `stingColour` so it's never
disposed per-mesh). 100% = no override (entry removed). Persisted per model (B3) + cleared by
Reset. One traverse per change; selection re-overlays on top.

### C2 — selection-driven Isolate / Hide / Hide-others / Show-all  ✅ served `C2-selisolate`
These set raw `o.visible` before, which a later `applyAppearance` would clobber. Now they set a
transient `state.vizIsolation = { mode, guids }` and call `applyAppearance`; the resolver
composes it on top of the normal resolution — in-set elements keep their colour/transparency,
out-of-set get ghosted (isolate) or hidden (hide-others); hide-selection hides the in-set.
Show-all clears isolation + any per-group hide. Wired from the context menu (all four) + the
multi-select toolbar. Transient (not persisted); cleared by Reset.

### C3 — legend interactivity (consistency + hover safety)  ✅ served `C3-legend`
Click swatch → isolate value (ghost rest), shift-click → hide, hover → highlight, per-value
count — all already routed through the layered resolver via `col.isolate` / `col.hidden`. Fixed
one hazard: `clearHoverHighlight` forced emissive to **black** on mouse-leave, which would stomp
a true-original GLTF material's own emissive (and the orange selection emissive). Now captures
each touched material's prior emissive and restores exactly that. Materials deduped by uuid so a
shared colour material is touched once.

### C4 — colour-by Clash status, Issue status, robust numeric gradient  ✅ served `C4-statuscolour`
New schemes resolve per element GUID via `col.byGuid` (new arg to `colourValueOf`). **Clash
status** = worst clash status per element (NEW>OPEN>RESOLVED), rest "Clear" (muted). **Issue
status** = Open / Resolved / No issue via `elementGuids[]` (Open wins). Both add a status legend
with counts and compose with isolate/hide/hover/transparency like any scheme; persisted +
re-derived on reload. Gradient robustness: replaced `Math.min(...nums)` / `Math.max(...nums)`
(a 12k-element spread overflows the call stack) with a single reduce loop.

### C5 — named visualize presets in Saved Views + meeting broadcast  ✅ served `C5-presets`
Factored `serializeViz()` / `applyVizSnapshot()` (shared by B3 persistence + presets). Saved
Views' `captureViewState` now embeds the full visualize snapshot; recalling a view restores
camera + disciplines + levels + the entire appearance (scheme re-derived deterministically) and
mirrors it to a live meeting. `broadcastAppearance` now sends the WHOLE appearance over the WS2
overlay channel (`source:'appearance'`), echo-guarded by `applyingRemoteViz`/`restoringViz`;
meeting-sync routes it to `sting:remoteAppearance` → `applyRemoteVizSnapshot`. Followers see the
presenter's exact colour/ghost/transparency/render-mode state.

### C6 — search/filter → act  ✅ served `C6-search`
New "Search → act" section: a text box + field selector (Any / DISC / SYS / LVL / FUNC / PROD /
CAT / any param). `searchElementGuids` finds elements whose chosen field (or any scalar value)
contains the query; the four action buttons route the matches through the layered model —
**Isolate** (ghost rest), **Hide**, **Colour** (matches orange via a guid-keyed `search` scheme,
rest muted), **Select** (drives the selection toolbar). Query/field kept in state across panel
re-renders; Enter = isolate. One pass over the element map per action.

## Part C complete
C1 transparency · C2 selection-isolate · C3 legend · C4 status colour · C5 presets+broadcast ·
C6 search→act — all served + gated. Next: Part D audit.

---

## Part D — proactive audit (code-trace; browser-verify in the pause)

Probed the layered model across controls × interactions × combinations by tracing the code
(no browser here — flagged items need a human click). Confident fixes applied; ambiguous ones
flagged.

### Fixed
- **D-1 (real bug) — per-model persistence collided across models.** `vizStateKey()` keyed off
  `state.modelId`, which is **never assigned**, so every model wrote/read
  `planscape_viz_default` — model A's visualize state loaded onto model B. Now keyed by the real
  module-scope `modelId` (from `?model=`). B3 is now genuinely per-model.
- **D-2 (minor) — colour-scheme toast fired during restore.** `colourBy*` toast on every restore
  (load / preset recall / remote apply). Guarded with `restoringViz` so silent restores stay
  silent.

### Verified composing (layered model holds)
- colour + ghost + select — selection is an emissive clone over the resolved appearance (Part A).
- render-mode + colour — render lens composes under colour/ghost/hide (A4).
- isolate/hide (selection or search) + colour/transparency — `vizIsolation` composes on top (C2).
- legend isolate/hide/hover + scheme — routed through `col.isolate`/`col.hidden` (C3).
- minimap reflects appearance — it is a scissor inset of the SAME scene, so ghost/hide/colour
  show automatically (M6); no second material set.
- performance — one traverse per state change; ghost/colour/transparency/render-mode materials
  are shared + cached (never disposed per-mesh); search/colour iterate the element map once.

### Flagged for human review / decision
- **D-F1 — Viewer is single-model by design.** Only the active `?model=` element-map + GLB load;
  the federation (e.g. 3×MBALWA + 2×Tendo) is **not co-rendered**. Per-model viz state is keyed
  by model id and restored on switch, but cross-model aggregation / per-model checkbox toggling /
  "isolate whole-federation vs one model" are **out of scope** for this viewer. Decision: confirm
  whether federated co-render is wanted (large effort) or one-model-at-a-time is acceptable.
- **D-F2 — Fit/Home frames the whole model even when elements are hidden/isolated** (`fitCamera`
  uses `modelBounds`). Some users expect Fit to frame only visible geometry. Left as-is (framing
  the full model is also a defensible default); change to visible-bounds on request.
- **D-F3 — `searchAct('select')` / colour on a query matching thousands of elements** clones an
  emissive per matched mesh (select) — fine for typical queries, potentially heavy for a
  match-all. No cap today; add one if it bites on the 12k model.
- **D-F4 — meeting broadcast fires on every appearance change** (no debounce). Fine for normal
  interaction; could flood the overlay channel under rapid slider drags. Debounce if needed.

---

## E2 — elevation nav buttons  ✅ served `E2-elevation`
Two buttons (⤒ Up / ⤓ Down) next to Pan in #navControls. One-shot (like Fit/Home/Level) — they
call `STING_VIEWER_EXTRAS.elevateCamera(±1)`, which moves `camera.position` AND `controls.target`
by the same model-scaled delta along the rendered up axis (+Y) via the camera getter, so heading
+ pitch stay fixed (altitude only). Surfaces the M5 Ctrl+↑/↓ gesture as buttons.

---

## E3 — section system (verify + flexibility)  ✅ served `E3-section`
Verified the Commit-4 pipeline LOADS in the deployed build — its THREE classes (`Plane`,
`PlaneGeometry`, `TransformControls`, `Mesh`, `Line`) ARE in the bundle (not tree-shaken like
`Sphere` was), so it's not the fitCamera trap. The core is present: oblique plane
(`setSectionPlane`), axis planes (`openSectionPlane`/`addSectionPlane`/`removeSectionPlane`),
section BOX with 6 per-face sliders + draggable per-face TransformControls gizmo (axis-locked,
faces clamped so they can't cross, dragging-changed disables orbit) + cap-fill, From-selection,
Clear. Clipping is renderer-level so it composes with the appearance layer + ortho.
Added flexibility: **Flip** (show outside the box via `clipIntersection`) and **per-model
save/restore** — `getSectionBox()`/`applySectionState()` (fraction-based, model-relative) now
ride inside `serializeViz()`/`applyVizSnapshot()`, so the section travels with B3 persistence,
Saved-Views presets, and the meeting broadcast. Step-along-normal is covered by the sliders.

---

## E4 — rich properties + actions + cost  ✅ served `E4-props` (client) · exporter unverified (Revit)
**Client (`renderProperties`):** grouped + filterable — Element title · STING tag · Cost (real
currency in green, or "— no cost data" + hint when absent) · Identity · **Materials** (per
material: name + area m² + volume m³) · **Quantities** (area / volume / length / count) ·
Dimensions · Performance · all remaining params. New **Actions** grid composing through the
layers: Isolate · Hide · Hide others · Show all · Fit · Zoom · Set pivot · Section box · Measure ·
Colour-like (same category) + Create issue · Find clashes · Copy tag · Link to sheet. Materials/
Quantities only render when the exporter supplied them (graceful on metadata-poor models).
**Data path (`PublishModelCommand.BuildElementMap`):** new `AddQuantitiesAndMaterials(doc, el,
entry)` emits `area`/`volume`/`length` (metric) + a `materials[]` breakdown (name + area +
volume via `GetMaterialIds`/`GetMaterialArea`/`GetMaterialVolume`) into each element-map entry,
alongside M3's `discipline` + `cost`. `ModelsController` already merges a `<model>-costs.json`
sidecar by GUID. **Caveat:** the C# is unbuilt here (no Revit/.NET in sandbox) — verify in Revit;
every Revit API call is best-effort + try/caught so a publish never fails on quantities.

---

## E5 — broader discovery audit (beyond visualize)  ✅ served `E5-audit`

### Fixed
- **E5-1 (real, self-introduced) — `clipIntersection` leaked across sections.**
  `clearSectionBox` (viewer-extras) didn't reset the E3 Flip (`renderer.clipIntersection`), so a
  flipped box, once cleared, would invert the NEXT section / clash box. Now resets
  `clipIntersection=false` + `sb.invert=false` on clear.
- **E5-2 (E1 fragility class) — `setupSectionCard` unguarded bindings.** It did
  `$('#sectionClose').addEventListener(...)` etc. with no null-guard; a single missing element
  would abort the rest of the section-card wiring. Optional-chained every binding (per-control
  containment; fault-isolated init already stops the cascade).

### Verified working (traced)
- `setActiveTool` exits markup (`stopMarkup` → restores rotate) + section (`exitSectionTool`)
  when switching; engine pick raycaster gated per tool (no select while measure/markup/section).
- Section gizmo `dragging-changed` toggles `controls.enabled`; `clearSectionBox` →
  `detachSectionGizmo` re-enables orbit. No orbit-disabled leak after exit.
- Toolbar menus (`bindMenu` for Measure/Section/View/Markup/Meet) guard trigger/menu null,
  attach to static HTML (not re-rendered) — bindings can't be orphaned.
- `setupViewCube` / `setupMinimap` guard their root element (`if (!x) return`). All ~22 init
  setups are fault-isolated (E1) so one failure can't cascade + the culprit is logged.

### Flagged
- **E5-F1 — `measure → section` switch doesn't explicitly stop measure** (only markup/section get
  teardown in setActiveTool). The engine `setTool` changes the raycaster so measure effectively
  stops, but any measure overlay state isn't torn down by name. Low risk; confirm with a click.
- Carried from Part D: single-model-by-design, Fit-frames-whole-model-when-hidden,
  select-all-matches clone cost, meeting-broadcast no debounce.

## Prompt complete
A · B · E1 · 4 console fixes · C1–C6 · D · E2 · E3 · E4 · E5 — all committed + STEP-0 gated
(SERVED + livekit 200 on the running container).

---

## Design decisions implemented (audit flags → RESOLVED)

The Part-D / E5 flags below were design calls; all three are now implemented + STEP-0 gated.

- **RESOLVED D-F1 — VIZ SCOPE = ALL LOADED MODELS (federation).** `e741cf2f9` · `F1-federation`.
  Engine loads every project model into the shared `modelPivot` (`addModel`, shared recenter →
  relative positions); the viz layer traverses `modelGroup` so resolver / index / picking /
  discipline+category aggregation + legend / colour / isolate / hide / transparency span all
  loaded models. Element-maps merged; model checkboxes toggle a root by id + re-apply; viz
  persistence re-keyed per **project** (`planscape_viz_proj_<projectId>`) so federation state
  doesn't fragment. Supersedes the D-1 per-model key (correct only while single-model).
- **RESOLVED D-F2 — Fit frames only VISIBLE elements.** `51cb480cf` · `F2-fitvisible`.
  `fitCamera` (no target) now uses `visibleModelBounds()` (meshes with self + all ancestors
  visible), FITFIX size math; nav Fit/Home → `fitVisibleOrToast` (no-op + toast when nothing
  visible). Isolate/hide/model-toggle then Fit frames what's shown.
- **RESOLVED D-F4 / E5 broadcast — coalesced near-real-time meeting sync.** `96f6fa461` ·
  `F3-coalesce`. `broadcastAppearance` is leading-edge immediate + ≤1 send/100 ms carrying the
  LATEST snapshot (intermediate dropped, final always sent), echo-guarded. Effectively live,
  no SignalR flood.

Still flagged (not requested): **D-F3** select-all-matches clone cost (no cap), **E5-F1**
measure→section explicit teardown.

---

## TRACK A — stability + correctness fixes

### BUG 1 — discipline misclassification (electrical → plumbing)  ✅ served `A1-disc-keys`
**Root cause** (`coordination-viewer.js` `discOf`): the plumbing rule `/pipe|plumb|sanitary|fixture|
valve|sprinkler pipe/` ran BEFORE the electrical rule AND matched the bare word **"fixture"**, so
"Lighting Fixtures" / "Electrical Fixtures" derived to **P**. **Fix:** (a) the real DISC token still
wins first; (b) reordered Electrical + Fire-protection BEFORE Plumbing; (c) Electrical now matches
lighting/luminaire/light-fixture/conduit/cable/wire/data/fire-alarm/switch/socket/receptacle/
panelboard (not bare "panel" → curtain panels stay A); (d) Plumbing made SPECIFIC — plumb/sanitary/
WC/lavatory/urinal/basin/sink/cistern/soil/waste/drainage/pipe/valve, **never bare "fixture"**.
Category cross-check: Lighting Fixtures→E, Electrical Fixtures→E, Electrical Equipment→E, Plumbing
Fixtures→P, Pipes→P, Cable Trays→E, Conduits→E, Sprinklers→FP, Fire Alarm Devices→E, Curtain
Panels→A, Mechanical Equipment→M.

### BUG 3 — search/isolate/hide key consistency across the federation  ✅ served `A1-disc-keys`
**Root cause** (`searchElementGuids`): search-by-DISC used the raw `tokenValue` while colour-by /
shade-only / the resolver use `discOf` (derived) — so on as-built models search-by-DISC returned
zero where colour-by-discipline showed values. **Fix:** search now uses `discOf` for DISC + `catKey`
for CAT — the SAME normalisation the resolver + colour-by + counts use, so the buckets are
consistent across all federated element-maps. (The F1 merge already unions the maps; rebuildGuidIndex
re-runs after federation lands to index every root, so isolate/hide/select resolve across all models.)
PENDING-HUMAN-VERIFY: shade-only Electrical keeps lighting fixtures shaded; shade-only Plumbing leaves
electrical untouched; isolate/hide/select act on the right elements across all 5 models.

### BUG 2 — deterministic / idempotent controls  ✅ served `A2-determinism`
Every colour control already routed through the one state model (`state.vizColour` / `vizPreset`)
→ `applyAppearance`; colours are deterministic from sorted distinct (A5). Added the missing UX
contract:
- **Idempotent toggle:** clicking the ACTIVE scheme again clears it back to base —
  colourByToken / colourByParam / colourByClashStatus / colourByIssueStatus (by kind+token),
  applyDisciplinePreset (by name), shadeOnlyDiscipline / shadeOnlyCategory (detect "already only
  this" → show-all). So on→off→on is identical every time.
- **Pressed state:** the active scheme's button renders pressed (blue) so current state is
  visible; switching schemes replaces `vizColour` wholesale (fresh isolate/hidden), so A→B→A
  reproduces the first A exactly (mutually-exclusive colour modes).
- Cross-modal composition (colour + ghost + transparency + isolate) is the intended layered model
  (precedence hide > transparency > ghost > colour > show), unchanged.

### Deep cross-check (code-trace; browser-verify pending)
- colour-by Discipline/System/Level/Function/Product → all `colourByToken` (deterministic A5,
  toggle, pressed). Clash/Issue → `byGuid` schemes (toggle, pressed). Gradient → `colourByParam`
  numeric (stack-safe min/max, toggle).
- shade-only, isolate/hide/show-all, transparency, ghost tint/opacity, legend click/shift/hover,
  search→act, presets, reset — every one mutates the single state model then calls
  `applyAppearance` (one traverse), so combinations compose deterministically and idempotently.
- All value buckets (distinctDisc / distinctTokens / searchElementGuids / colour value-of) now
  share `discOf` (DISC) + `catKey` (CAT) normalisation → consistent across the federated maps.
PENDING-HUMAN-VERIFY: exercise every control × value × other-control on the 5-model federation;
each stable, idempotent, acting on correctly-classified elements.

---

## VIEWER COMBINED FIXES

### V1 — ghost / x-ray elements non-pickable (click-through)  ✅ served `V1-pickthrough`
The pick raycaster returned ghosted/x-rayed elements, so a click selected the transparent element
instead of the solid one behind it. Engine `raycast()` (viewer.html) + the coordination context
raycast now filter via `isPickableMesh` / `pickableHits`: skip a hit whose mesh (or any ancestor)
is invisible OR whose `_vizMode` is `ghost` / `trans:*` / `rmode:xray` / `rmode:ghost`; pins +
solid + colour-overridden stay pickable. A click now passes THROUGH to the solid behind (ACC
behaviour). Also fixed a federation pick-bug: the engine raycast targeted only `modelRoot`
(primary) — now targets `modelPivot` (every loaded model).

### V2 — draggable resize + collapse handles on both side panels  ✅ served `V2-panelresize`
E1's rail handles only collapsed. Extended `setupPanelHandles`: each handle now **drags to resize**
the panel width live (pointer drag → sets `--panel-left-width` / `--panel-right-width`, clamped
180–560 px) and **clicks to collapse/expand** (a drag is distinguished from a click by >3px move).
`savePanelState` persists BOTH the collapsed flags AND the widths (`lw`/`rw`); restored on load.
During drag the grid transition is disabled (`.app-shell.resizing`) so the panel tracks the mouse,
and `V.sizeRenderer()` (ortho-aware) runs each move so the canvas + camera reframe live and the
viewport reclaims/yields space. Handle CSS: `cursor:ew-resize`, taller 72px grab area,
`touch-action:none`, highlight while resizing.

### V3 — SignalR long-session auth (no 401 + reconnect storm)  ✅ served `V3-signalr`
Both `signalr-shim.js` (notifications) and `meeting-sync.js` (meeting hub) used
`accessTokenFactory: () => token` — a STATIC token captured at init, so once the JWT TTL passed a
(re)negotiate 401'd and SignalR retried with the same dead token → negotiate storm. Replaced with a
dynamic, refresh-aware `tokenFactory`: read the current `planscape_token` from localStorage each
call; when the JWT is expired/within 60 s, exchange `planscape_refresh` via `POST /api/auth/refresh`
for a fresh access+refresh pair (coalesced so concurrent calls share one request) before handing it
to SignalR. So a long session reconnects cleanly with a live token; if the refresh token is also
dead, `withAutomaticReconnect`'s finite backoff gives up (no infinite storm). A0: the shim now
registers `JoinedProject` + `PresenceChanged` so the "No client method" warnings stop (dashboard
side was already done in the console-fixes round).

### V4 — federation load performance (don't freeze while loading 5 models)  ✅ served `V4-loadperf`
The old federated loader fired all `addModel`s then polled — letting up to 5 GLTF parses pile onto
single frames (avgFps → ~0.2). Now models STREAM one at a time: each model's parse must land
(`waitForRoot`) before the next is fetched, with a 250 ms breather so the render loop keeps
delivering frames — the primary stays orbitable and siblings pop in progressively. The federation
co-load is also DEFERRED via `requestIdleCallback` (1.5 s/3 s fallback) so the primary model is
interactive first. `state.federationLoading` suppresses the coalesced broadcast while streaming;
the heavy `rebuildGuidIndex` + `applyAppearance` + panel refresh run ONCE at the end, not per model.
PENDING-HUMAN-VERIFY: loading the 5 models doesn't lock the UI; steady-state orbit on the full
federation stays ~30–50 fps (low-end target).

### N6 — expanded element properties panel  ✅ served `N6-properties`
The selected-element panel **dropped every nested value** (`renderProperties` did
`if (… typeof v === 'object') return;`), so IFC **property sets**, **classification**, and
**type/instance parameter** bundles never displayed — only flat scalars plus the hand-rolled
Identity / Dimensions / Performance / Materials / Quantities / Cost groups. The element-map is an
exporter-produced JSON sidecar (`/models/{id}/element-map`, served as-is + cost merge) whose schema
is **opaque and varies**, so N6 renders nested groups **generically**: every plain-object key becomes
its own labelled section (`psets`→"Property sets", `classification`→"Classification",
`typeParams`→"Type parameters", `parameters`→"Instance parameters", else humanised); a *group of
objects* (e.g. psets keyed by set name) gets a sub-head per member + its scalar rows; a flat object
becomes a key/value section. Added a **Relationships** section (host / assembly / room-space / IFC
type / IFC GUID / Revit id) from whatever scalar relationship fields the exporter supplied. Scalar
keys that now have dedicated sections (qty / cost / materials / relationship ids) were added to
`RESERVED` so they don't double-render in the generic "Properties" bucket. Everything stays inside the
existing `#propRows` scroll + live `propFilter` (new rows carry `data-search`) and degrades to
omitted/"—" when a field is absent. Real material **textures** still need the Phase-2 exporter
(baseColorTexture + UVs) — out of scope. Marker `STING_VIZ_BUILD N6-properties` on
`coordination-viewer.js`; "Property sets" / "Relationships" / "Instance parameters" labels verified
present on the **served minified** bundle. PENDING-HUMAN-VERIFY: select an element from a model whose
element-map carries psets/classification → those groups render, filter narrows them, absent groups
just don't appear.
