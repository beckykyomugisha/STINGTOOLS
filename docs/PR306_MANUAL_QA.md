# PR #306 — Manual QA script (runtime gates CI can't cover)

CI is green, but the WebGL viewer, cost data flow, and native A/V are not exercised by CI.
Tick every box here **in a browser / on a device** before merging #306. Each step lists the
exact action and the **expected** result (PASS condition). If a step fails, comment on #306
with the step number + what you saw — don't merge.

**Setup**
- Deploy the branch (or run `Planscape.Server` locally) and sign in so a JWT is in
  `localStorage.planscape_token`.
- Pick a project + a published model. Note their ids for the URL:
  `…/viewer.html?project=<projectId>&model=<modelId>` (add `&meeting=<sessionId>` for A/V).
- Open the browser devtools console — a healthy load logs
  `[viz] mesh→meta resolver: <hit>/<total> meshes resolved (NN%)`. **Expect ≥ ~90%.** A low %
  (or a "LOW resolver hit-rate" warning) means the model's GLB lacks per-mesh `uniqueId` and
  several gates below will legitimately fail — re-publish from the plugin first.

---

## 🔴 Gate 1 — Viewer browser pass

### 1a. Orbit stability (incl. over-the-top)
1. Drag **up** on the model. → Camera **tilts up** (not down/inverted).
2. Drag **left**. → Model spins **left**.
3. Keep dragging up past vertical so the camera goes **over the top** of the model.
   → Motion stays smooth; **no roll/flip/snap** at the pole; the model doesn't suddenly spin.
4. Read the on-screen X/Y/Z coordinate readout while hovering a known point. → Values match the
   model's **true Revit (Z-up) coordinates**, not rendered Y-up.

**PASS:** drag senses correct, over-the-top stable, readout is true Z-up.

### 1b. Minimap (render + no drag leak)
1. Look at the minimap inset (bottom-right). → It **shows the plan** of the model (not blank).
2. **Drag inside the minimap.** → The main view **pans/recenters** to the dragged location and the
   main model does **NOT spin/orbit**.
3. **Click** (no drag) a spot in the minimap. → Main view flies to that location.

**PASS:** minimap renders; dragging it never rotates the main model; click = fly-to.

### 1c. Ghost appearance — live slider
1. Open the **Visualize** tab (right panel).
2. Under **Ghost appearance**, drag the **Opacity** slider. → Already-ghosted elements **fade
   live** as you drag (no lag, no need to re-toggle).
3. Change the **Tint** colour. → Ghost tint updates live.

**PASS:** opacity + tint mutate ghosts in real time.

### 1d. BY-CATEGORY / BY-DISCIPLINE toggles + presets
1. In Visualize → **By discipline**, on one discipline row click **○ (ghost)** then **∅ (hide)**
   then **◐ (show)**. → The matching elements ghost / hide / show **each click**.
2. Click a discipline **row label**. → That discipline stays solid, the rest **ghost** (isolate).
3. Repeat under **By category** for one category row.
4. Click a **Discipline preset** (e.g. "Discipline"). → Elements recolour by discipline + a legend
   appears.
   - On an **as-built / untagged** model, **By discipline** is still populated (derived from Revit
     category) — confirm it's not empty.

**PASS:** every toggle changes the scene; label-click isolates; presets recolour; By-discipline
non-empty even without DISC tokens.

### 1e. Colouriser (categorical + numeric gradient + legend interactivity)
1. Visualize → **Colour by tag** → click **Discipline** (or System/Level). → Elements colour by
   value; a **legend** (swatch · value · count) appears.
2. Change the **Palette** dropdown (STING / RAG / Viridis / …). → Colours re-map.
3. Use **Colour by parameter** → pick a **numeric** field (e.g. a dimension or cost). → Elements
   render a **min→max gradient**; the legend shows a value range + unit.
4. Legend interactivity (categorical scheme):
   - **Click** a legend row. → That value stays coloured, the rest **ghost** (isolate). Click again → restores.
   - **Shift-click** a row. → That value **hides**.
   - **Hover** a row. → Matching elements **highlight** (emissive), clear on mouse-out.
   - Elements with no value show the distinct **`<No Value>`** colour.

**PASS:** categorical + gradient both colour correctly with a legend; isolate/hide/hover all work.

### 1f. Properties palette populates on select
1. **Single-click** an element. → It selects (highlight) and the camera **does not move**.
2. The **Properties** tab populates: name/category/family/type/mark, STING tag, dimensions,
   performance, and a filterable list of every scalar. → Not empty.
3. Type in the **filter box**. → Rows filter live.
4. **Double-click** an element. → Camera **zooms-to-fit** that element.

**PASS:** click selects without moving; properties populate + filter; double-click frames.

### 1g. Camera functions
1. Tilt the camera, then click **Level** (nav). → View goes **horizontal** (zero pitch), heading kept.
2. Click **👁 (Human eye)** in the ViewCube (or press **E**). → Camera drops to eye height, looks
   horizontal, heading kept (orbit mode, not walk).
3. **Ctrl + ArrowUp / ArrowDown.** → The eye **rises / lowers** along vertical, heading + pitch kept.

**PASS:** Level flattens, Human-eye drops to eye height, Ctrl+Arrow elevates.

### 1h. Section box + ortho + explode
1. Section: open the section box; drag the **6 sliders** → the cut moves live on each face; the cut
   faces are **capped** (not hollow). Enable the **gizmo** → drag a face handle, the cut + sliders
   stay in sync and opposite faces never cross.
2. Click the **ortho** toggle (ViewCube). → Projection switches perspective↔orthographic with **no
   jump**; sliders/section read cleanly in ortho.
3. **Explode**: open the explode control, drag the **factor** → elements spread from the centre;
   try **Radial / By level / By discipline** grouping; factor **0** fully reassembles. Explode
   coexists with section + selection.

**PASS:** section clips + caps + gizmo sync; ortho toggles cleanly; explode spreads/reassembles.

---

## 🔴 Gate 2 — Cost end-to-end on a real model

Requires a model whose elements carry cost params (publish from the plugin with
`ASS_CST_UNIT_RATE_NR` set, optionally `ASS_CST_CURRENCY_TXT`).
1. Open the model; **single-click** an element you know has a rate.
2. In **Properties → Cost**, the **Estimated cost** row shows a **real currency value** (e.g.
   `UGX 1,250,000` / `$…`), in green — **not "—"**.
3. Click an element with **no** rate. → Cost shows **"—"** (never a fabricated number).
4. (Optional, server merge path) If a `<model>-costs.json` sidecar exists in storage, its
   per-GUID cost overrides/fills the element-map — confirm those elements show the sidecar value.

**PASS:** a real-rate element shows real currency end-to-end; rate-less elements show "—".

---

## 🔴 Gate 3 — Device A/V smoke test (Expo dev-client, NOT Expo Go)

LiveKit needs native WebRTC, so this **must** be a dev-client build:
```
cd Planscape
npx expo run:android        # or: npx expo run:ios
```
Requires `LIVEKIT_*` configured on the server (or the docker `--dev` LiveKit on :7880).

1. Open **Meetings**, open a meeting, tap **🎥 Join live A/V**. → The live screen loads; your
   camera tile appears; a second participant's tile appears when they join.
2. Toggle **mic**, **camera**. → Audio mutes/unmutes; video stops/starts (tile reflects it).
3. As **host/presenter**, start **screen-share**. → The shared screen shows for all participants.
4. Switch the **active surface** (Model / Document / Screen) as presenter. → **Every** participant's
   view switches to the same surface.

**PASS:** join + cam/mic + screen-share + surface-switching all work across two devices.

---

## After all boxes are ticked
Comment on #306 confirming the runtime gates passed (browser + cost + device), then it's
merge-ready. Until then the PR stays **review-only**.
