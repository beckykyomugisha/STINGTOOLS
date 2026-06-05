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
