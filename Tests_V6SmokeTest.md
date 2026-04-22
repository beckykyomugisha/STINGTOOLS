# STING v6 MVP — manual smoke test

Manual test plan for the 22 v6 commits on branch
`claude/heredoc-large-files-6h5P9`. Run this in Revit 2025 / 2026 /
2027 against a medium-sized MEP sample model (≥ 2,000 tagged
elements) after deploying the compiled plug-in per the existing
deployment steps in CLAUDE.md.

Expected total runtime end-to-end: about 90 minutes on a 16 GB
workstation. Individual sections can be run in isolation to verify
a specific engine.

## 1. Pre-flight

- [ ] Revit launches without addin load errors
- [ ] `STING Tools.log` shows no ERROR lines on startup
- [ ] Dock panel opens via ribbon toggle
- [ ] v4 MVP commands still appear on the TAGS tab (Placement,
      Routing, Fabrication) — regression check
- [ ] Open an existing v5 project; compliance scan runs without
      exceptions (regression check)

## 2. Phase 1 — parameters and performance

### S1.1 / S1.2 — v6 parameter constants

- [ ] Run `Load Shared Params`, point at
      `Data/Parameters/STING_PARAMS_V6.txt`
- [ ] Open Project Parameters — all 20 v6 parameters appear under
      5 new groups (CLASH_MNG, ACC_SYNC, IFC_EXCH, HEALTH_METRICS,
      ASBUILT)
- [ ] Select a wall; confirm each new parameter appears on the
      Properties palette with the correct datatype

### S1.3 / S1.4 — performance audit

- [ ] Open `Performance_AuditNotes.md` — 8 antipatterns listed
- [ ] Run Excel Export on a project with ≥ 10,000 elements; time
      the operation. Should be noticeably faster than a pre-v6 run
      of the same command on the same model (expected 5-10× on
      large projects)

### S1.5 — TransactionHelper

- [ ] Trigger any v6 engine (Placement / Clash / Carbon); after it
      finishes, Ctrl+Z once — the entire batch rolls back in one
      undo step, not N steps

## 3. Phase 2 — placement extensions

### S2.15 — ceiling grid snap

- [ ] Open a floor plan of a room with a suspended ceiling
- [ ] Run Place Fixtures against that room; luminaires land on
      ceiling-tile intersections (not halfway across a tile)
- [ ] Change the Ceiling type Tile Width to 600 mm; rerun — tiles
      re-snap to the new grid
- [ ] Rotate the room (long axis swap) — luminaires reorient to
      the long axis

### S2.16 — obstruction index

- [ ] Place a sprinkler + an air diffuser in a test room
- [ ] Run Place Fixtures for the same room — no luminaire lands
      within 350 mm of either obstruction
- [ ] Increase the buffer parameter to 500 mm; rerun — exclusion
      zones visibly grow

## 4. Phase 3 — advanced routing

### S3.7 — voxel grid

- [ ] Build a VoxelGrid over a small bounding box via
      Routing_PreviewVoxels (or debug build)
- [ ] Cells near walls/slabs are 100 mm; plenum cells are 200 mm;
      open volumes are 400 mm
- [ ] Cells that intersect walls marked IsObstacle=true with
      CostMultiplier=infinity

### S3.8 — A* solver

- [ ] Run AutoDrop in a clear plenum — path matches the pre-v6
      vertical drop (regression)
- [ ] Run AutoDrop with an obstacle directly below the origin —
      path routes around the obstacle, NodesExpanded < 200k

### S3.9 — ACO refiner

- [ ] Take an A* path through a densely-obstacle plenum — ACO
      reduces bend count and length vs seed
- [ ] ConvergedIterations field populated; stagnation counter
      triggers by iteration 20-30 on typical problems

### S3.10 — 3-opt smoother

- [ ] Force-create a path with a visible crossing; run
      ThreeOptSmoother.Smooth — output has no crossing

### S3.11 — Bezier fitting snap

- [ ] Angles at corners on the final path are all in {45, 60, 90,
      135}° within ±2°
- [ ] Each corner is a visibly-curved Bezier, not a sharp vertex

### S3.12 — service corridors

- [ ] Run any AutoDrop for an HVAC supply service; path prefers
      the FFL+2500-2900 band over alternative elevations
- [ ] Run AutoDrop for gas; path rises to FFL+2900-3000 (highest
      service band)

## 5. Phase 4 — validation extensions

### S4.8 / S4.9 — separation validator

- [ ] Place a power cable tray parallel to a data cable tray at
      150 mm spacing — run Validation_RunAll; a
      `BS EN 50174-2 power-data parallel` error appears (required
      200 mm not met)
- [ ] Increase spacing to 250 mm; rerun — error clears
- [ ] Place a medical gas pipe 200 mm from electrical — HTM 02-01
      300 mm violation flagged

### S4.10 — live standards IUpdater

- [ ] Run `LiveStandardsUpdater.Enable()` (via debug command or
      BCC toggle); place a new power cable tray 100 mm from an
      existing data tray — a warning balloon appears within ~200 ms
      in WarningsManager
- [ ] Move the cable to 250 mm; the balloon clears on next trigger
- [ ] `LiveStandardsUpdater.Disable()` stops new balloons from
      appearing

## 6. Phase 6 — v6 gap engines

### S6.1 — clash triage

- [ ] Run Navisworks-style clash detection (existing) to populate
      a ClashSession
- [ ] Call `ClashTriageEngine.Triage`; output has exactly TopN
      entries (default 20), sorted by Score descending
- [ ] Structural-vs-structural clashes score higher than service-
      vs-service

### S6.2 — clash resolution

- [ ] Pass a ScoredClash to `ClashResolutionSuggester.Suggest`; 3
      candidates returned (MOVE / REROUTE / ACCEPT)
- [ ] `Apply` on the MOVE candidate offsets the smaller element
      by the discipline step (50 mm for HVAC, 25 mm for pipe,
      10 mm for conduit)
- [ ] CLASH_RESOLUTION_STATUS_TXT parameter is written

### S6.3 — federation walker

- [ ] Link an architectural model into the host; call
      `FederationLinkedWalker.CollectFederatedElements` — two
      entries appear (host + link), link BBs are in host coord frame
- [ ] Unload the link — walker skips it silently with a Warn log

### S6.4 — ACC Issues round-trip

- [ ] Configure OAuth in `%APPDATA%\Planscape\acc_credentials.json`
- [ ] Call `PushIssueAsync` with a test issue; ACC Issue appears
      in the Autodesk console
- [ ] Call `PullIssuesAsync`; list includes the pushed issue
- [ ] Force-expire access_token; `EnsureAuthAsync` refreshes
      automatically with 5-min buffer

### S6.5 — as-built reconciler

- [ ] Place a mock `{project}_asbuilt_captures.json` with 3
      deviations (5 mm, 30 mm, 120 mm) next to the RVT
- [ ] Run `ReconcileFromSidecar`; report shows TotalCaptures=3,
      Applied=3, OutOfToleranceCount=2 (30 mm and 120 mm >10 mm)
- [ ] Select any reconciled element — ASBUILT_DEVIATION_MM reads
      back the captured value in LENGTH (feet) internal units

### S6.6 — sheet matrix

- [ ] Author a STING_SHEET_MATRIX.json with 2 rows (Plans by Level
      and Sections by Axis)
- [ ] Run `SheetMatrixGenerator.Generate`; one sheet per level +
      one sheet per grid axis appears in the Project Browser
- [ ] Sheet numbers match the pattern (A-1001, A-1002, ...)
- [ ] Title block on each sheet matches the matrix specification

### S6.7 — 4D Gantt reader

- [ ] Export a tiny MS Project XML (5 tasks); run
      `ParseMsProjectXml` — 5 GanttTask objects returned with
      Start / Finish / Name populated
- [ ] Same for a Primavera XER file
- [ ] Run `AssignPhasesToModel` with a pickTask lambda that
      matches all walls to task "Shell"; a phase named "Shell"
      gets written to PHASE_CREATED on the walls

### S6.8 — carbon stage tracker

- [ ] Run `CarbonStageTracker.Compute` on a model with ≥ 100
      elements
- [ ] CBN_A1_A3_KG_CO2E populated on every element
- [ ] Report file `STING_CARBON_ISO14064_*.csv` appears next to
      the RVT with stage totals + benchmarks
- [ ] TotalLifecycleOver60y ≈ A1-A3 + A4 + A5 + B6·60 + C1 + C2 +
      C3-C4 (spot-check one element)

### S6.9 / S6.10 — IFC PSet mapping

- [ ] Call `IfcPsetMapping.GetMapping("ASS_TAG_1")` — returns
      (Pset_ManufacturerTypeInformation, Tag, IfcIdentifier)
- [ ] `FormatStepPropertyValue` produces syntactically valid STEP
      (verify manually against an IFC 4.3 sample)

### S6.11 — Excel bidirectional

- [ ] Call `ExcelBidirectionalSync.Export(doc, path)`; workbook
      appears with 12-column header row
- [ ] Add an Excel formula to a cell (e.g. `=UPPER(B2)` on TAG1)
- [ ] Run `Import` — the computed UPPER value pushed to the model,
      and `{workbook}.formulas.json` sidecar captures the formula
- [ ] Run `Export` again — the formula is restored to its cell

## 7. Cleanup

- [ ] Log file `STING Tools.log` shows zero ERROR lines from v6
      engines for the duration of the test
- [ ] No uncommitted changes in the RVT that the user can't
      account for (each v6 engine runs under TransactionHelper so
      should always be visible to Ctrl+Z)
- [ ] Close the project without a save — no dialog about pending
      changes beyond what the user expects
