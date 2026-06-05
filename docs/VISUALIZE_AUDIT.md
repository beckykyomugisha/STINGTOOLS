# Viewer Visualize — audit & change log

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
