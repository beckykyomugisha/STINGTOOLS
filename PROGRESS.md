# PROGRESS — viewer visualization

Branch `claude/optimistic-bell-EfjJw` (PR #306 tracks it — do **not** open a new PR).
Every item below is committed + STEP-0 gated (SERVED marker + `livekit-av.js` 200 on a freshly
`--no-cache`-rebuilt container). The deployed bundle is the minified `dist/`, so the marker grep
on the *served* file is the proof.

## Design decisions (this round) — PENDING-HUMAN-VERIFY

Verify in a 2nd incognito tab at `localhost:5000` (sign in → open a model in a project that has
several models, e.g. 3×MBALWA + 2×Tendo). Console should log `STING_VIZ_FEDERATION <n> model
roots co-rendered`.

### Item 1 — VIZ SCOPE = ALL LOADED MODELS  ·  `e741cf2f9` · marker `F1-federation`
- [ ] On open, **all** project models render together (federated positions), not just the active one.
- [ ] **By discipline / by category** counts + the colour legend **aggregate across every loaded
      model** (numbers exceed a single model's).
- [ ] Colour-by / Isolate / Hide / transparency / a discipline toggle affect elements in **all**
      models, not just one.
- [ ] Model list checkboxes: unchecking a model **hides that whole model**; re-checking restores it
      with the **active appearance still applied**.
- [ ] Set a scheme, reload → it restores (now keyed per project, shared across the federation).
- [ ] Orbit / minimap / coordinate readout still correct with multiple models loaded.

### Item 2 — Fit frames only VISIBLE elements  ·  `51cb480cf` · marker `F2-fitvisible`
- [ ] Isolate (or hide) some elements, then **Fit** (or Home / F) → camera frames **only what's
      shown**, not the whole model.
- [ ] Hide everything (or toggle all models off) → **Fit** is a no-op + toast "Nothing visible to
      frame" (camera doesn't jump).
- [ ] Normal Fit on a full model still frames it correctly (FITFIX — no console error).

### Item 3 — coalesced live meeting broadcast  ·  `96f6fa461` · marker `F3-coalesce`
- [ ] Two clients in one meeting. Presenter **drags the ghost-opacity / transparency slider
      continuously** → follower tracks it **near-live**, no flicker / feedback loop.
- [ ] During the drag, SignalR isn't flooded (bounded to ~10 msg/s); the **final** state always
      lands on the follower.
- [ ] Switching colour scheme / isolate / render mode on the presenter mirrors to the follower.

## Caveats carried forward
- **E4 exporter** (materials/area/volume/quantity) is C# — unbuilt in this sandbox; verify a real
  Revit publish populates Properties → Materials / Quantities.
- Still flagged (not requested): select-all-matches clone cost (no cap); measure→section explicit
  teardown.

## Done earlier (already human-confirmed or gated)
A (layered model) · B (rows/deselect/persist) · E1 (dead-control regression) · 4 console fixes
(FITFIX / CMDFILTER / SignalR handlers / vendored SignalR) · C1–C6 · D audit · E2 (elevation) ·
E3 (section flip + save/restore) · E4 (rich properties + actions + cost) · E5 (audit).
Full per-item record + markers in `docs/VISUALIZE_AUDIT.md`.
