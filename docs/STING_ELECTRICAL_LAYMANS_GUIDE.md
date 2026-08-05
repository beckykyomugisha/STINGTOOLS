# STING Electrical — Layman's Guide

A working reference for what STING's electrical engine does, when each workflow fires, what data flows in and out, and what to check when something looks wrong.

This guide reflects everything shipped on the `claude/review-string-electrical-2lZBw` branch through Wave J (commit `6653d4b4`). It supersedes any earlier informal write-ups.

---

## Contents

1. [The big picture](#the-big-picture)
2. [End-to-end happy path](#end-to-end-happy-path)
3. [Panel schedules](#panel-schedules)
4. [Circuit assignment + load balancing](#circuit-assignment--load-balancing)
5. [Phase balance](#phase-balance)
6. [Calcs — load summary, voltage drop, breaker sizing](#calcs--load-summary-voltage-drop-breaker-sizing)
7. [Conduit auto-routing](#conduit-auto-routing)
8. [Conduit consolidation](#conduit-consolidation)
9. [Junction box auto-placement](#junction-box-auto-placement)
10. [Slab penetration detection + FRP placement](#slab-penetration-detection--frp-placement)
11. [False-ceiling soffit snap](#false-ceiling-soffit-snap)
12. [In-wall chase routing](#in-wall-chase-routing)
13. [Slab soffit routing](#slab-soffit-routing)
14. [Cable schedule (BOM)](#cable-schedule-bom)
15. [BS 7671 standards validation](#bs-7671-standards-validation)
16. [Bend-radius validation](#bend-radius-validation)
17. [Cable fill validation](#cable-fill-validation)
18. [Lighting grid + photometric library](#lighting-grid--photometric-library)
19. [Seed families + swap to manufacturer](#seed-families--swap-to-manufacturer)
20. [Workflow JSONs (chained command sequences)](#workflow-jsons-chained-command-sequences)
21. [Parameter reference](#parameter-reference)
22. [Troubleshooting matrix](#troubleshooting-matrix)
23. [Standards cited](#standards-cited)

---

## The big picture

STING's electrical engine has five layers stacked on top of each other. Every workflow you'll run lives in exactly one of them. Each layer reads from the layer below and writes data the layer above can use.

| Layer | What it owns | Key data files / engines |
|---|---|---|
| **5. UI / Workflows** | Buttons on the dock panel, JSON workflow chains, result panels | `WORKFLOW_ElectricalQA.json`, `StingDockPanel.xaml`, `StingResultPanel` |
| **4. Commands** | One command per user-visible action: BatchAssignCircuits, AutoRoute, BuildCableSchedule, etc. | `Commands/Electrical/`, `Commands/Symbols/`, `Commands/Panels/` |
| **3. Engines** | Pure-logic + Revit-calling helpers: routing pathfinder, validators, placers, swap registry | `Core/Routing/`, `Core/Validation/`, `Core/Symbols/` |
| **2. Manifests + cached state** | The cable manifest + compliance cache + audit log | `_BIM_COORD/cables.json`, `_BIM_COORD/audit_log_*.jsonl` |
| **1. Parameters + categories** | Shared parameters, category bindings, stable GUIDs | `MR_PARAMETERS.csv`, `CATEGORY_BINDINGS.csv`, `ParamRegistry.cs` |

A typical workflow walks down the layers (button → command → engine → manifest → param read), the engine writes back up (param stamp → manifest update → result panel), and the result panel offers the next button.

---

## End-to-end happy path

This is the typical Monday-morning sequence for a project that already has panels placed, cables in the manifest, but no routed conduits yet. Roughly an hour of clicking; the engine does the rest.

```
1.  Setup → Load Shared Parameters
       └── binds STING params to the project's shared parameter file

2.  Build Seed Families              (one-time, project-wide)
       └── creates STING_SEED_*.rfa files in _BIM_COORD/Families/Seeds/

3.  Place Fixtures                   (Placement Centre)
       └── lighting, sockets, panels, fire-alarm devices

4.  Auto-assign Circuits             (Wave H groups + voltage-bands)
       └── unassigned circuits → panels (greedy fit)
           ELC_CIRCUIT_GROUP_TXT + ELC_PANEL_REF_TXT stamped

5.  Phase Balance
       └── shifts circuits across A/B/C to minimise imbalance

6.  Auto-Route Conduit               (the big one)
       ├── creates Conduit elements per cable
       ├── runs JunctionBoxAutoPlacer (Wave F1)
       │     ├── BS 7671 §522.8.5 break-points
       │     └── places STING_SEED_JunctionBox seeds
       └── runs SlabPenetrationDetector (Wave D)
             ├── stamps STING_PENETRATION_REF_TXT
             └── places STING_SEED_SpecialityEquipment FRPs

7.  Validation_BS7671
       └── ElectricalStandardsValidator + Healthcare Pack
           ELEC.RUN.LONG / BENDS.EXCESS / FILL.OVER findings

8.  Cable Schedule (BOM)
       └── three rollups + CSV at _BIM_COORD/cable_schedule.csv

9.  Swap to Manufacturer             (when procurement decides)
       └── seed families → real manufacturer families
           parameters survive via stable GUIDs

10. Re-run Validation_BS7671
       └── catches anything the swap broke
```

That's the canonical chain. Every step is also runnable standalone — you don't have to start at the top.

---

## Panel schedules

### What it does
Creates one `PanelScheduleView` per electrical panel, picks the right template via a rule registry, fills `ELC_PNL_*` parameters, and stamps panel-schedule references on every feeding circuit.

### Commands
| Command | Tag | Action |
|---|---|---|
| Batch Panel Schedules | `Panel_BatchSchedules` | Create schedules for every panel that doesn't have one |
| Panel Schedule Audit | `Panel_Audit` | Read-only — finds panels without schedules + drift |
| Export to Excel | `Panel_ExportToExcel` | Round-trip workbook with per-panel sheets |
| Import from Excel | `Panel_ImportFromExcel` | Round-trip back; anti-erasure guard preserves Revit-side values |
| Fill Empty Slots | `Panel_FillSparesAll` | `AddSpare` on every empty slot project-wide |
| Convert Spaces → Spares | `Panel_SpacesToSpares` | `RemoveSpace` + `AddSpare` |

### Inputs / outputs
- **Reads**: `STING_PANEL_SCHEDULE_TEMPLATES.json` rule registry, project's loaded `PanelScheduleTemplate` elements, panel parameters (Voltage, Mains, Number of Circuits, Source).
- **Writes**: `PanelScheduleView` per panel, stamps `ELC_PNL_NAME / VOLTAGE / LOAD / FED_FROM / MAIN_BRK / WAYS`, writes `ELC_PANEL_SCHEDULE_REF_TXT` on every `ElectricalSystem` whose `BaseEquipment` matches the panel.

### Rule resolution
1. Check panel name against `namePatterns` regexes per rule (priority-ordered).
2. First match wins; subsequent rules' `fallbackTemplateNames` queue as alternates.
3. If `panelType` is set on a rule, also append every loaded template whose `PanelConfiguration` matches (configuration-aware fallback).
4. Global fallback: first available template if `useFirstAvailableTemplate: true`.

### Standards
Drawing-type stamp `elec-panel-schedule-A3` is applied so Browser Organizer + drift-detection + SyncStyles work on schedules.

### Troubleshooting
- **No template available** → Required: at least one `PanelScheduleTemplate` loaded with a name matching the rule's `templateName` or `fallbackTemplateNames`.
- **Configuration-aware fallback didn't fire** → `GetPanelConfiguration` reflective probe failed silently. Check `StingLog` for `PanelTypeMatches` warnings.

---

## Circuit assignment + load balancing

### What it does
Auto-assigns every `ElectricalSystem` with no `BaseEquipment` (orphan circuits) to the best-fit panel. "Best fit" = smallest panel with free slots that accepts the circuit's voltage band and matches any logical group.

### Command
- **Tag**: `Circuit_AssignAuto`
- **Class**: `BatchAssignCircuitsCommand`

### Algorithm
1. **Inventory** — collect every `ElectricalSystem` (orphan + already-assigned) and every `Electrical Equipment` `FamilyInstance`.
2. **Pre-group circuits by `BaseEquipment.Id`** in a `Dictionary<long, List<ElectricalSystem>>` so PanelState construction is O(1) per panel instead of O(S).
3. **Build PanelState** for each panel — `TotalSlots`, `RemainingSlots`, `ConnectedVa`, `NominalVoltage`, `RoomName`, `LevelCode`, `GroupTag` (from prior runs).
4. **Resolve circuit group** for each unassigned circuit: manual override on system → manual override on first load → match against `STING_ELECTRICAL_ASSIGNMENT.json` rules (kitchen / emergency-lighting / fire-alarm / comms-room).
5. **Two-stage candidate search**:
   - Stage 1: panels that already carry the same group OR are still empty.
   - Stage 2 (if stage 1 yields nothing AND `AllowFallbackToAnyPanel`): the wider voltage-compatible pool.
6. **Score remaining candidates** by: free-slot count → connected load → same-room bonus → same-level bonus.
7. **Reserve** chosen panel slots on the in-memory PanelState; continue.
8. **Preview** the plan; user confirms.
9. **Apply** — `ElectricalSystem.SelectPanel(FamilyInstance)`, stamp `ELC_PANEL_REF_TXT`, `ELC_CIRCUIT_GROUP_TXT` (instance) and `ELC_PNL_CIRCUIT_GROUP_TXT` (panel type) so re-runs converge.

### Voltage-compatibility bands
Loaded from `STING_ELECTRICAL_ASSIGNMENT.json`:

| Band | Range | Typical use |
|---|---|---|
| ELV | 0–60 V | Door bells, low-voltage controls |
| 120V | 90–140 V | US single-phase |
| 230V | 200–250 V | UK / IEC single-phase |
| 400V | 380–420 V | Three-phase low-voltage |
| 480V | 460–530 V | US three-phase commercial |
| 600V | 580–720 V | Industrial / data centre |

Two voltages are "compatible" if they fall in the same band.

### Grouping rules (shipped defaults)
| GroupId | Rule (regex on room name) | Categories |
|---|---|---|
| `kitchen` | `(?i)kitchen|kitchenette|tea ?point|pantry` | Electrical Fixtures |
| `emergency-lighting` | (any room) + system name `(?i)emergency|EM_|escape` | Lighting Fixtures |
| `fire-alarm` | (any room) | Fire Alarm Devices |
| `comms-room` | `(?i)comms|server|MER|IDF|MDF|patch|bms` | Electrical Equipment, Communication, Data |

### Standards
- BS 7671:2018+A2:2022 §131 — circuit identification
- BS 5266-1 — emergency lighting on dedicated panel
- BS 5839-1 — fire alarm on dedicated supply

### Troubleshooting
- **All circuits show "no fit"** → No panel with the right voltage band has free slots. Add panels or expand `Number of Circuits` parameter on existing panels.
- **Circuits scattered across panels by group** → Group rule didn't fire. Check room names match the regex; manual override stamps `ELC_CIRCUIT_GROUP_TXT` on the system to bypass auto-detection.

---

## Phase balance

### What it does
Greedy largest-first 3-phase load balancer. Iterates 1-pole circuits and reassigns phase (A/B/C) to minimise the imbalance between the three buses.

### Command
- **Tag**: `Circuit_Balance`
- **Class**: `PhaseBalanceCommand`

### API constraint
`ElectricalSystem.StartingPhase` is not writable in any current Revit version. The command runs in two modes:
1. **Preview** — shows before/after totals + delta. Always runs.
2. **Best-effort apply** — attempts the parameter write inside try/catch; reports how many circuits couldn't be reassigned (typically those grouped 2-pole, 3-pole, or read-only by the panel's slot map).

To physically reassign phases, users move circuits to the appropriate slot column in the panel schedule (left = A, middle = B, right = C for the standard 3-phase format).

### Algorithm
1. Collect every 1-pole `ElectricalSystem` whose `BaseEquipment` is a 3-phase panel.
2. Sort by apparent load descending (largest-first heuristic).
3. For each circuit: pick the phase with the lowest current load, assign it.
4. Skip 3-pole circuits (already balanced) and grouped 2-pole circuits (would split a logical pair).

---

## Calcs — load summary, voltage drop, breaker sizing

| Command | Tag | What it does |
|---|---|---|
| Load Summary | `Calc_LoadSummary` | Walks panels, sums apparent loads, applies demand factors, stamps `ELC_PNL_CONNECTED_LOAD_KW` |
| Voltage Drop | `Calc_VoltageDrop` | Per-circuit voltage drop using BS 7671 Appendix 4 method (length × current × cable resistance × correction) |
| Flag VD violations | `Calc_FlagVD` | Re-runs voltage drop and surfaces circuits exceeding 3 % (lighting) / 5 % (other) |
| Breaker Sizer | `Calc_SizeBreakers` | Suggests breaker rating per circuit using BS 7671 Appendix 4 + load-current-uplift rules |
| Apply Breakers | `Calc_ApplyBreakers` | Writes the suggested rating onto every circuit |

These are gated in `WORKFLOW_ElectricalQA.json` so `Calc_VoltageDrop` only fires after `Calc_LoadSummary` has stamped the marker (`ELC_PNL_LOAD_SUMMARY_COMPLETE_BOOL` or any non-empty `ELC_PNL_LOAD`).

---

## Conduit auto-routing

### What it does
Walks every cable in the manifest with no `RouteTrayIds`, computes a Manhattan L/Z path between the load and the panel, creates `Conduit` elements, and runs three post-passes (junction box auto-placement, slab penetration detection, FRP placement).

### Command
- **Tag**: `Cable_AutoRouteConduit` (button) / `Cable_BuildSchedule` (workflow)
- **Class**: `ConduitAutoRouteCommand`

### Pipeline (with Wave A–J integrations)

```
For each unrouted cable:

1. Resolve ElectricalSystem by CircuitId (BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)
2. Resolve start (load location) + end (panel location) from LocationPoint
3. Resolve level from loadEl.LevelId or active view
4. SelectConduitDiameterMm — ≤40% fill (BS EN 61386)
5. ComputeRoute(start, end, diameter, label) — 3-segment L/Z
6. CountBends — pre-flight against BS 7671 §522.8.5
   └── Warn if > 3 bends (or size-aware cap from Wave J4)
7. For each segment: Conduit.Create(doc, type, start, end, level)
   ├── ELC_CONDUIT_ROUTE = "AUTO:<circuitId>"
   ├── ELC_CDT_BEND_COUNT_NR = bend count
   ├── ELC_CDT_RUN_LENGTH_M = segment length / 1000
   └── routeIds.Add(conduit.Id.Value)
8. cable.RouteTrayIds = routeIds
9. routed++
```

After all cables are routed:

```
10. JunctionBoxAutoPlacer.Place
    ├── For each routed conduit: AnalyseConduit
    │     ├── Read ELC_CDT_BEND_COUNT_NR + ELC_CDT_RUN_LENGTH_M
    │     ├── Size-aware cap (Wave J4): MaxBendsForConduit(odMm, material)
    │     ├── If bends > cap OR length > 6 m → break-point
    │     └── Place point: nth-bend (precise) → length-clip → midpoint (fallback)
    ├── Place STING_SEED_JunctionBox at each break-point
    │     ├── Stamp ELC_JB_AUTO_PLACED_BOOL = 1
    │     ├── Stamp ELC_JB_REASON_TXT = "BENDS_EXCESS" / "RUN_TOO_LONG"
    │     └── Stamp ELC_CDT_BREAKPOINT_TXT = "JB:<id>@<reason>" on parent conduit
    └── Wave H4: cable.JunctionBoxIds += placed JB ids per parent conduit

11. SlabPenetrationDetector.Detect
    ├── For each routed conduit:
    │     ├── If ELC_CDT_BREAKPOINT_TXT present (split sub-segment):
    │     │     └── Wave J3: InheritParentPenetration — copy parent's
    │     │         STING_PENETRATION_REF_TXT + STING_PENETRATION_FIRE_RATING_TXT
    │     ├── Vertical-ish filter (≤30° from vertical via dot-product)
    │     ├── For each Floor: bbox-XY containment + Z-straddle test
    │     └── Record PenetrationRecord, stamp:
    │           ├── STING_PENETRATION_REF_TXT = "FLR:<id>@<rating>"
    │           ├── STING_PENETRATION_FIRE_RATING_TXT
    │           └── ELC_CDT_PENETRATION_COUNT_NR (incremented)

12. FrpPenetrationPlacer.Place
    ├── ResolveSeedFamily — STING_SEED_SpecialityEquipment by name
    ├── Index types by FR rating (FR30/60/90/120 + SLEEVE_GENERIC)
    ├── For each PenetrationRecord:
    │     ├── Activate symbol if needed
    │     ├── Try face-based place (resolve Floor's bottom PlanarFace)
    │     │     └── doc.Create.NewFamilyInstance(faceRef, location, +Y, sym)
    │     ├── Fall back to point-instance on Floor host
    │     ├── Stamp PEN_FIRE_RATING_TXT, PEN_OD_MM, PEN_HOST_REF_TXT,
    │     │       PEN_MEMBER_ID_TXT, PEN_CONTROL_NUMBER_TXT (sequential)
    │     └── If seed family not loaded: stamp member-side fallback
    │           STING_PENETRATION_REF_TXT = "FLR:<id>@<rating>#<count>"
```

### Inputs
- `cables.json` (cable manifest) at `<project>/_BIM_COORD/`
- Loaded `ConduitType` (first available)
- Conduit material + diameter from cable's CSA via `EstimateCableOdMm`

### Outputs
- `Conduit` elements per segment
- `STING_SEED_JunctionBox` instances at break-points
- `STING_SEED_SpecialityEquipment` (FRP) instances at slab crossings
- Updated `cables.json` with `RouteTrayIds` + `JunctionBoxIds`
- Audit log entry per cable

### Standards
- BS 7671:2018+A2:2022 §522.8.5 — max 3 bends between draw-in points (size-aware cap per IET GN1 §7.4)
- BS 7671 §522.8.4 — 6 m typical max run length
- BS EN 61386 — manufacturer fill ceilings (40% straight, 35% with bends)
- BS 9999 / BS 476 — fire-rated barrier penetrations

### Troubleshooting
- **"No conduit type found"** → Load a `ConduitType` family before running.
- **All cables skip with "no panel"** → Run `Circuit_AssignAuto` first so cables have `BaseEquipment` set.
- **Junction boxes show "NEEDED:..." instead of placing** → `STING_SEED_JunctionBox` family not loaded. Run `Build Seed Families`, finish per `Families/Seeds/README.md`.

---

## Conduit consolidation

### What it does
Identifies groups of cables sharing `(PanelName, SourceEquipmentId, SegregationClass)` and either previews them or destructively merges multiple per-cable conduits into one larger consolidated run.

### Command
- **Tag**: `Cable_ConsolidateConduits`
- **Class**: `ConduitConsolidatorCommand`

### Two modes
1. **Preview only** — read-only; renders savings opportunities in `StingResultPanel`.
2. **Apply consolidation** (Wave F2) — destructive bulk merge in one `TransactionGroup.Assimilate`.

### Apply algorithm
For each group:
1. Pick representative endpoints from the first member cable that has routed conduits.
2. Delete every member's per-cable `Conduit` segments via `doc.Delete`.
3. Route ONE consolidated conduit at `group.UnionDiameterMm` (computed from `≤40% fill` against the union of all cables).
4. Stamp the new conduit:
   - `ELC_CONDUIT_ROUTE = "CONSOLIDATED:<panel>"`
   - `ELC_CDT_BEND_COUNT_NR` (recomputed)
   - `ELC_CDT_RUN_LENGTH_M`
   - `ELC_CDT_CABLE_COUNT_NR = members.Count` — drives BS EN 61386 fill table row
5. Update every member cable's `RouteTrayIds` to the new consolidated conduit ids.

### Standards
- BS EN 61386 — cable fill table (fill ceiling depends on cable count)
- Practical: takes the BIM model from "every cable has its own conduit" (estimating fiction) to "one conduit serves N cables" (how electricians actually work)

### Troubleshooting
- **"No consolidation opportunities"** → Every routed cable has unique `(panel, source, segregation)`. Either every cable goes to a unique destination, or the manifest hasn't been routed yet.
- **Apply destroyed routing I wanted** → Use Ctrl-Z; the Apply runs as one undo step.

---

## Junction box auto-placement

### What it does
Detects conduits exceeding BS 7671 §522.8.5 (>3 bends) or BS 7671 §522.8.4 (>6 m run length) and places `STING_SEED_JunctionBox` family instances at the precise violation points.

### Engine
- **Class**: `JunctionBoxAutoPlacer`
- **Hooked into**: `ConduitAutoRouteCommand` (runs automatically after routing)

### Wave J4 — size-aware cap
Per IET Guidance Note 1 (2024) §7.4:

| Conduit | Max bends |
|---|---|
| Flexible (any size) | 2 |
| Rigid PVC (any size) | 3 |
| Steel ≤25 mm | 3 |
| Steel 32–40 mm | 4 |
| Steel ≥50 mm | 4 |

Caller can override via `MaxBends` parameter; default is the lookup.

### Wave J2 — placement strategy (priority order)
1. **Bends-exceed**: walk the connector graph forward, count `ConduitFitting`s with `ELC_CDT_BEND_ANGLE_DEG > 0`, place at the outbound connector of the Nth bend. Bounded to 50 hops.
2. **Run-too-long**: `curve.Evaluate(maxRunMm / length)` — places exactly at `MaxRunLengthMm` along the run.
3. **Both violations**: take the EARLIER of the two.
4. **Fallback**: midpoint (only when graph walk fails).

### Wave H4 — manifest sync
After `Place()` returns, `ConduitAutoRouteCommand` walks `jbResult.Points` and appends placed JB ids to each parent cable's `JunctionBoxIds`. Closes the silent staleness in `cables.json`.

### Outputs
- `STING_SEED_JunctionBox` `FamilyInstance` at each break-point
- `ELC_JB_AUTO_PLACED_BOOL = 1`, `ELC_JB_REASON_TXT`, `ELC_JB_TYPE_TXT`, `ELC_JB_SIZE_MM`, `ELC_JB_IP_RATING_TXT`
- `ELC_CDT_BREAKPOINT_TXT = "JB:<id>@<reason>"` on parent conduit
- `cable.JunctionBoxIds` updated in manifest

### Graceful degradation
When `STING_SEED_JunctionBox.rfa` isn't loaded:
- Warning: "STING_SEED_JunctionBox family not loaded. Run 'Build Seed Families'..."
- Fallback stamp: `ELC_CDT_BREAKPOINT_TXT = "NEEDED:<reason>@<x,y,z>"` on the parent conduit so the schedule still surfaces the requirement.

---

## Slab penetration detection + FRP placement

### What it does
Two-step detector → placer pair that records every fire-rated barrier crossing and drops a face-based fire-stop family at each one.

### Engine
- **Detector**: `SlabPenetrationDetector` (Wave D)
- **Placer**: `FrpPenetrationPlacer` (Wave E4)
- **Hooked into**: `ConduitAutoRouteCommand` (runs automatically after JB placement)

### Detect algorithm
1. Collect floors once (typical project: <50).
2. For each member id:
   - Skip non-`MEPCurve` elements.
   - Check `ELC_CDT_BREAKPOINT_TXT` — if present, inherit parent's penetration metadata (Wave J3).
   - Check vertical-ish: `|Z component of direction| ≥ 0.866` (≤30° from vertical).
   - For each floor: bbox Z-straddle test + XY containment (midXy in floor bbox).
   - Record `PenetrationRecord` and stamp:
     - `STING_PENETRATION_REF_TXT = "FLR:<floorId>@<rating>"`
     - `STING_PENETRATION_FIRE_RATING_TXT` (from floor's `STING_FIRE_RATING_TXT`, default `FR60`)
     - `ELC_CDT_PENETRATION_COUNT_NR` incremented (Wave J3)

### Place algorithm
1. Resolve `STING_SEED_SpecialityEquipment` family (by name → fallback to seed-tag scan).
2. Index `FamilySymbol`s by FR rating (`FR30`/`FR60`/`FR90`/`FR120` + `SLEEVE_GENERIC`).
3. For each `PenetrationRecord`:
   - Resolve symbol matching the rating; activate if needed.
   - Try face-based placement: resolve Floor's bottom `PlanarFace` (normal -Z), compute UV midpoint, call `doc.Create.NewFamilyInstance(faceRef, location, +Y, sym)`.
   - Fall back to point-instance on the floor host.
   - Stamp `PEN_FIRE_RATING_TXT`, `PEN_OD_MM`, `PEN_HOST_REF_TXT`, `PEN_MEMBER_ID_TXT`, `PEN_CONTROL_NUMBER_TXT` (sequential `FRP-0001`, `FRP-0002`, ...).

### Standards
- BS 9999 — fire safety in the design, management and use of buildings
- BS 476-20 — fire resistance of building elements
- BS EN 1366-3 — fire resistance tests for service installations (penetrations)
- BS 9999 Appendix L — fire-stop register

### Outputs
- `STING_SEED_SpecialityEquipment` instances on slab faces
- `STING_PENETRATION_REF_TXT` + `STING_PENETRATION_FIRE_RATING_TXT` on every member
- Project-scoped sequential `PEN_CONTROL_NUMBER_TXT` (drives the Penetration Register schedule)

---

## False-ceiling soffit snap

### What it does
When a fixture-to-tray drop's intercept Z would pass through a suspended ceiling, lifts the intercept to (ceiling top + 50 mm CIBSE buffer) so the conduit terminates above the ceiling tile instead of speering through it.

### Engine
- **Class**: `CeilingAwareSnapper`
- **Used by**: `DropEngineBase.TryDropFromFixture` (called on every drop when `SnapToCeilingSoffit = true`, default)

### Algorithm
1. For each `Ceiling` whose XY bounding box contains the drop origin:
2. Check Z: ceiling sits BETWEEN origin Z and intercept Z?
3. If yes: candidate snap target = `bb.Min.Z + 50 mm`.
4. Pick the highest qualifying ceiling (closest to slab if multiple stacked).
5. Refusal: only snap if new Z is closer to origin than original intercept (preserves topology).

### Standards
- CIBSE Guide B4 §3.6 — service-zone clearance from ceiling soffit (50 mm typical for access tile relief)

---

## In-wall chase routing

### What it does
Routes conduit/pipe segments INSIDE a wall's compound structure, parallel to the wall's centre line. Reads finish layers, computes available chase depth, rejects routes that don't fit OD + insulation + cover.

### Command
- **Tag**: `Elec_RunWallChase`
- **Class**: `RunWallChaseCommand`

### Algorithm
1. User picks wall + 2 endpoints.
2. `ResolveChaseDepth`:
   - Read `WallType.GetCompoundStructure()`.
   - Walk layers from interior face inward, accumulating finish thickness until structural core.
3. Compute required depth: `pipeOD + 2×insulation + clearance + cover`.
4. Reject if required > available; warn if no compound structure.
5. `ProjectAndOffset` — project endpoints onto wall LocationCurve, offset by inset.
6. `Conduit.Create` (or `Pipe.Create`) at the projected points.
7. Optional: `SleeveEngine.PlaceSleeves` on the created segments.

### Wire 1 — auto-delegation from `AutoConduitDrop`
When `AutoConduitDrop.UseChaseRoutingWhenAvailable = true` and the fixture's host is a Wall with compound structure, `TryDropViaChase` synthesises a minimal `PlacementRule` (MountingContext=CHASED, NominalDiameterMm, ExposureClass=XC2) and delegates to `InWallChaseRouter`.

### Standards
- Eurocode 2 / UK NA — concrete cover (`ConcreteCoverTable.GetNominalCoverMm`)
- BS EN 13914 — plastering / wall finish layer thickness

---

## Slab soffit routing

### What it does
Mirror of in-wall chase routing for floor slabs. Routes conduit parallel to the slab underside at offset = slab thickness + finish + service zone.

### Engine
- **Class**: `SlabSoffitRouter`
- **Used by**: drop engines when `RoutingMode == "SOFFIT"`

### Algorithm
1. Read `Floor.FloorType.GetCompoundStructure()`.
2. Compute total slab thickness from all layers.
3. Compute available service zone from layers BELOW the structural core.
4. Required: `conduitOD + 2×insulation + 5 mm clearance`.
5. Reject if required > available.
6. Resolve soffit Z: slab top − slab thickness − offset.
7. Manhattan L route at uniform Z (axis-aligned).
8. Stamp `ELC_CDT_INSTALL_METHOD_TXT = "SOFFIT"`, `ELC_CDT_SOFFIT_OFFSET_MM`.

---

## Cable schedule (BOM)

### What it does
Generates a Bill of Materials from the cable manifest + routed conduit graph. Removes the manual cable-take-off step.

### Command
- **Tag**: `Cable_BuildSchedule`
- **Class**: `CableScheduleBuilderCommand`

### Three rollups

#### 1. Cable BOM
- Group by `(CSA × cores × insulation × conductor)` so every wire spec is one line regardless of which circuit uses it.
- Length = sum of each cable's RouteTrayIds curve lengths × **1.05 (5% pull-slack per BS 7671 / IET Wiring Regs §A2)**.
- Weight = length × `WeightPerMetreKg` (Cu 9 kg/km/mm² conductor + insulation, Al 3 kg/km/mm²).
- Output column: `Description = "{cores}c × {csa} mm² {material} {insulation}"`, `SKU = "CABLE_{Mat}_{N}C_{CSA}_{Ins}"`.

#### 2. Conduit BOM
- Dedup ids across all cables (consolidated conduits count once).
- Group by `(ConduitType × diameter)`.
- Length = `LocationCurve.Length` per segment, summed.

#### 3. Box BOM
- Dedup `JunctionBoxIds` across all cables.
- Group by `(family × type × ELC_JB_TYPE_TXT × ELC_JB_SIZE_MM × ELC_JB_IP_RATING_TXT)`.
- Count instances per group.

### Output
- `StingResultPanel` — interactive view in Revit
- CSV at `<project>/_BIM_COORD/cable_schedule.csv` with columns: `Section, SKU, Description, Quantity, UnitOfMeasure, TotalLength_m, TotalWeight_kg, Discipline, Notes` — directly importable into SAP / standard estimating tools.

### Re-run advice
After Apply consolidation / JB placement / Swap to Manufacturer, re-run `Cable_BuildSchedule` to keep the BOM in sync.

---

## BS 7671 standards validation

### What it does
Scans every conduit + conduit fitting in the model and surfaces BS 7671 / BS EN 61386 violations: bend count, run length, cable fill, non-standard bend angles.

### Command
- **Tag**: `Validation_BS7671`
- **Class**: `ElectricalStandardsValidatorCommand` (standalone) / `RunAllValidatorsCommand` (chained)

### Findings produced
| Code | Severity | Trigger | Message + suggested fix |
|---|---|---|---|
| `ELEC.RUN.LONG` | Warning | length > `MaxRunLengthMm` | "Conduit run X m exceeds 6.0 m draw-in spacing (BS 7671 §522.8.4). Add N draw-in box(es) at every Y m." |
| `ELEC.BENDS.EXCESS` | Error | bends > size-aware cap | "X bends between draw-in points (limit N — BS 7671 §522.8.5 + IET GN1 §7.4 size-aware). Add (excess) draw-in boxes." |
| `ELEC.FILL.OVER` | Error | fill % exceeds cable-count limit | "Cable fill X% exceeds Y% (table-row, BS EN 61386). Reduce cable count or upsize the conduit." |
| `ELEC.FILL.NEAR` | Warning | within 10% of fill ceiling | "Cable fill X% within 10% of Y% manufacturer limit." |
| `ELEC.BEND.ANGLE` | Warning | bend angle ≠ standard | "Non-standard bend angle X° (BS EN 61386 standard angles: 11.25/22.5/30/45/60/90/120). Swap for closest standard fitting at Y°." |

### Cable-count-aware fill table (Wave H)
| Cable count | Straight | With bends |
|---|---|---|
| 1 cable | 53% | 43% |
| 2 cables | 31% | 20% |
| 3+ cables | 40% | 35% |

Read from `ELC_CDT_CABLE_COUNT_NR` parameter; falls through to "3+" when unknown (conservative default).

### Threshold source
`STING_FAB_RULES.json` (corporate) → project override at `<project>/_BIM_COORD/aec_filters.json`.

### Wave J4 — size-aware cap
`MaxBendsBetweenDrawIn` defaults to 3, but when set to 3 the validator looks up `JunctionBoxAutoPlacer.MaxBendsForConduit(odMm, material)` per-conduit. A 50 mm steel conduit gets cap = 4; a 25 mm PVC stays at cap = 3.

---

## Bend-radius validation

### What it does
Walks every conduit fitting connected to a routed conduit and checks its bend radius against the manufacturer minimum for the conduit's material × OD.

### Engine
- **Class**: `BendRadiusValidator`
- **Used by**: `AutoConduitDrop.CreateRunBetween` (post-create)

### Material multiplier table
| Material code | Multiplier × OD |
|---|---|
| GS-GALV / STEEL / EMT | 6×OD |
| UPVC / PVC | 10×OD |
| AL-FLEX / FLEX | 5×OD |
| SS316 | 6×OD |

### Findings
- Severity: Warning when actual < (multiplier × OD − 0.5 mm tolerance)
- Message: `"Bend radius X mm < Y×OD Z mm = W mm minimum (BS EN 61386, material=...)"`

### Standards
- BS EN 61386-22 — steel conduit
- BS EN 61386-21 — rigid PVC
- BS EN 61386-23 — flexible

---

## Cable fill validation

### Command
- **Tag**: `Cable_ConduitFill` / `Cable_ValidateConduitFill`
- **Class**: `ConduitFillValidateCommand`

### What it does
Walks every conduit + cable tray, computes fill % via `TrayFillCalculator.Compute(doc, el, manifest)`, stamps `ELC_CONDUIT_FILL_PCT`, applies red graphic override on failing elements.

### Inputs
- Cable manifest (loaded from `_BIM_COORD/cables.json`)
- Conduit / tray geometry

### Outputs
- `ELC_CONDUIT_FILL_PCT` per element
- Red projection-line override on failing elements
- `ConduitFillData` rows cached on `StingElectricalCommandHandler.LastConduitFills` for the BIM Coordination Center electrical tab

---

## Lighting grid + photometric library

### Command
- **Tag**: `Placement_LightingGrid`
- **Class**: `LightingGridCommand`

### What it does
For every selected (or all) `Room`, runs `LightingGridCalculator` to:
1. Classify the room via `ROOM_TYPE_CLASSIFIER.csv` (pattern matching against room name).
2. Look up target lux from `LUX_TARGETS_EN12464.csv` (BS EN 12464-1 / CIBSE LG7).
3. Compute fixture count using the lumen method:
   ```
   N = ceil( (target_lux × area_m²) / (lumens_per_luminaire × UF × MF) )
   ```
   where UF = utilisation factor (default 0.60), MF = maintenance factor (default 0.80).
4. Generate aspect-aware grid of N points across the room bounding box.
5. ObstructionIndex gating — reject points colliding with sprinklers / diffusers / detectors / casework / suspended MEP.
6. Place `Lighting Fixtures` family instances on surviving points.

### Photometric integration
Reads from each loaded `LightingFixture` family symbol:
- `ELC_PHOTO_LUMENS` → overrides `DefaultLumensPerLuminaire`
- `ELC_PHOTO_FILE_PATH` → surfaced in preview ("Dialux .ies/.ldt resolved")
- `ELC_LIGHTING_UF_FACTOR` / `ELC_LIGHTING_MF_FACTOR` → override calculator defaults

### Photometric library audit
Result panel surfaces a "PHOTOMETRIC LIBRARY" section showing:
- Total LightingFixture families loaded
- Count with `ELC_PHOTO_LUMENS` set
- Count with `ELC_PHOTO_FILE_PATH` set

Empty values block a Dialux round-trip.

### Standards
- BS EN 12464-1 — light and lighting of work places (lux targets)
- CIBSE LG7 — lighting guide for offices
- CIBSE Guide B4 — service zones

---

## Seed families + swap to manufacturer

### Two commands
| Command | Tag | What it does |
|---|---|---|
| Build Seed Families | `Seeds_Build` | Scaffolds 11 STING_SEED_*.rfa from JSON specs in `Data/Seeds/` |
| Swap to Manufacturer | `Seeds_SwapToManufacturer` | Bulk-swaps placed seed instances to real manufacturer families |

### Build pipeline (Wave E + G)
For each seed JSON:
1. Resolve `.rft` template by `Hosting` field (face-based / wall-based / ceiling-based / standalone) + `Category` (Lighting Fixtures / Specialty Equipment / etc.).
2. `Application.NewFamilyDocument(rft)`.
3. Inside one transaction:
   - `DrawGeometry` — 2D lines + arcs from `geometry` block.
   - `AddParameters` — every `parameters` entry, with shared-param GUID lookup.
   - `AddConnectors` — MEP connectors at declared XYZ + direction (only for connector-bearing seeds: ElectricalEquipment, MechanicalEquipment, JunctionBox).
   - `AddSolid3D` — extrusion sized from `solid3D` block.
   - `TryAddSeedMarker` — stamps `STING_SEED_FAMILY_TXT` on every type variant.
   - `AddTypeVariants` (Wave G1) — duplicates the default type once per `typeVariants` entry, stamps per-variant parameter overrides.
4. `SaveAs` to `_BIM_COORD/Families/Seeds/<name>.rfa`.
5. Load into project.

### Type variants shipped
| Seed | Variants |
|---|---|
| LightingFixture | RECESSED_LED_600x600 / DOWNLIGHT / PENDANT / LINEAR_LED / EMERGENCY |
| ElectricalFixture | SOCKET_2G / SOCKET_1G / SWITCH_2G / DATA_OUTLET_2G / FLOOR_BOX / FCU |
| ElectricalEquipment | DISTRIBUTION_BOARD_DB / MAIN_SWITCHBOARD_MSB / CONSUMER_UNIT / ISOLATOR / JUNCTION_BOX / TRANSFORMER |
| FireAlarmDevice | SMOKE_OPTICAL / HEAT_DETECTOR / MULTI_SENSOR / CALL_POINT_MCP / SOUNDER_BEACON_VAD / BEAM_DETECTOR |
| SpecialityEquipment | FR30 / FR60 / FR90 / FR120 / SLEEVE_GENERIC |
| PlumbingFixture | WC / BASIN / URINAL / SHOWER / SINK |
| AirTerminal | SUPPLY_DIFFUSER_SQ / SLOT_DIFFUSER / SUPPLY_GRILLE / EXTRACT_GRILLE / LOUVRE |
| MechanicalEquipment | AHU / FCU / CHILLER / BOILER / PUMP |
| Sprinkler | PENDANT / UPRIGHT / SIDEWALL / CONCEALED |
| CommunicationDevice | WIFI_AP / DATA_OUTLET_RJ45 / PATCH_PANEL / CCTV_CAMERA |
| JunctionBox | PULL_BOX / DRAW_IN_BOX / ADAPTABLE_BOX / TEE_BOX |

55 type variants total across 11 seeds, all programmatically generated.

### Manual finishing
After auto-build, the `.rfa` files need ~15 minutes per variant of Family-Editor work for visual polish (2D symbol drawing, 3D mass refinement, connector face attachment). Full guide at `Families/Seeds/README.md`.

### Swap pipeline (Wave E3 + H3 + J1)
For each placed seed instance:
1. **Collect** — model-wide scan (or selection) for instances whose `STING_SEED_FAMILY_TXT` starts with `STING_SEED_`.
2. **BuildPlans** — group by `(SeedId, sourceTypeVariant)` — Wave J1 makes per-variant routing possible (PENDANT lights → manufacturer A, RECESSED → manufacturer B).
3. **ResolveCandidates** — read `STING_FAMILY_SWAP_REGISTRY.json`, match every candidate's `familyNamePattern` (and optional `typeNamePattern`, `seedVariantPattern`) regex against loaded `FamilySymbol`s.
4. **Preview** — show plans + top-priority candidate per group.
5. **Apply confirmation** — TaskDialog CommandLink: "Apply" (legacy) or "Apply + Re-validate" (Wave H3).
6. **Apply** — inside `TransactionGroup`:
   - `el.ChangeTypeId(winner.ResolvedTypeId)` per instance.
   - `STING_DESIGN_REF_TXT = original seed id` (preserves design intent).
   - `STING_SWAP_HISTORY_TXT` prepended with `<ts>|<operator>|<srcFamily>|<destFamily>`.
7. **RestitchSwappedConnectors** (Wave F3) — second transaction inside same TransactionGroup. Walks open connectors on swapped instances, pairs by `(domain match, distance < 600 mm)`, calls `ConnectTo` (co-located) or `NewElbowFitting` / `NewUnionFitting` (displaced).
8. **Optional re-validate** (Wave H3) — runs full validator chain against swapped ids; reports findings count.
9. `TransactionGroup.Assimilate` — entire swap is one undo step.

### Swap registry candidates (excerpt)
For `STING_SEED_LightingFixture`:
| Pattern | Label | Priority |
|---|---|---|
| `(?i)recessed.*led.*600` | 600×600 LED panel | 10 |
| `(?i)downlight` | Downlight | 20 |
| `(?i)pendant` | Pendant luminaire | 30 |
| `(?i)linear.*led` | Linear LED | 40 |
| `(?i)emergency` | Emergency luminaire | 50 |

For `STING_SEED_JunctionBox`:
| Pattern | Label | Priority |
|---|---|---|
| `(?i)hensel.*(box|kvk)` | Hensel KVK | 10 |
| `(?i)wiska` | Wiska Combi | 15 |
| `(?i)spelsberg` | Spelsberg Abox | 20 |
| `(?i)pull.*box` | Pull box | 30 |
| `(?i)draw.*in.*box` | Draw-in box | 35 |
| `(?i)junction.*box|j-?box` | Generic junction box | 50 |
| `(?i)adaptable.*box` | Adaptable box | 60 |

### Survival across swap
Because every STING parameter has a stable shared-param GUID:
- ✅ `ASS_TAG_*` containers — tags don't break
- ✅ `ELC_*` electrical params — schedules don't break
- ✅ `LTG_*` lighting params — UF/MF/lumens carry over
- ✅ `ELC_PHOTO_*` photometric params — Dialux references survive
- ✅ `STING_DESIGN_REF_TXT` — original seed identity audit-trail preserved
- ⚠️ Connectors — Revit re-creates them on `ChangeTypeId`; mismatches drop. RestitchSwappedConnectors rejoins what it can.

---

## Workflow JSONs (chained command sequences)

### `WORKFLOW_ElectricalQA.json` (v1.2)
8-step electrical quality-assurance run with conditional gates.

```
1. Panel_Audit                                   (no gate)
2. Calc_LoadSummary                              (optional)
3. Circuit_AssignAuto      condition=has_unassigned_circuits
4. Circuit_Balance                               (no gate)
5. Calc_SizeBreakers                             (optional)
6. Calc_VoltageDrop        condition=load_summary_complete
7. Validation_BS7671       condition=has_conduits + skipIfDataUnchanged
8. Rprt_Audit                                    (optional)
9. Rprt_ExcelExport                              (optional)
```

### `WORKFLOW_PanelScheduleProduction.json`
End-to-end panel-schedule pipeline:

```
1. Panel_Audit              (find drift)
2. Panel_BatchSchedules     (create missing)
3. Panel_FillSparesAll      (optional — fill empty slots)
4. Panel_ExportToExcel      (optional — round-trip to estimator)
5. Panel_Audit              (re-audit to confirm convergence)
```

### Conditions implemented in `WorkflowEngine.EvaluateSingleCondition`
| Condition | Returns true when |
|---|---|
| `has_conduits` | Any element of category `OST_Conduit` exists |
| `has_unassigned_circuits` | Any `ElectricalSystem.BaseEquipment == null` |
| `load_summary_complete` | Any panel has `ELC_PNL_LOAD_SUMMARY_COMPLETE_BOOL = "1"` OR `ELC_PNL_LOAD` non-empty |
| `has_stale` | Cached: any element flagged `STING_STALE_BOOL = 1` |
| `has_warnings` | `doc.GetWarnings().Count > 0` |
| `has_overdue_issues` | Issue tracker has overdue rows |
| `has_untagged` | Any taggable element with empty `ASS_TAG_1` |
| `has_links` / `has_cad_imports` | Self-explanatory |
| `has_rooms` / `has_sheets` | Self-explanatory |

`skipIfDataUnchanged: true` — checks data-file hashes against the previous run; skip when nothing changed.

---

## Parameter reference

### Electrical conduit params
| Parameter | Bound to | Set by | Read by |
|---|---|---|---|
| `ELC_CDT_BEND_ANGLE_DEG` | Conduits, Conduit Fittings | manual / family | `BendRadiusValidator`, `JunctionBoxAutoPlacer` |
| `ELC_CDT_BEND_COUNT_NR` | Conduits | `ConduitAutoRouteCommand`, `JBAutoPlacer` | `ElectricalStandardsValidator`, `JBAutoPlacer` |
| `ELC_CDT_RUN_LENGTH_M` | Conduits | `ConduitAutoRouteCommand`, `AutoConduitDrop` | `ElectricalStandardsValidator`, `JBAutoPlacer` |
| `ELC_CDT_CABLE_COUNT_NR` | Conduits | `ConsolidatorApply`, manual | `ElectricalStandardsValidator` (fill table row) |
| `ELC_CDT_BREAKPOINT_TXT` | Conduits, Conduit Fittings | `JBAutoPlacer` | `SlabPenetrationDetector` (Wave J3 inheritance) |
| `ELC_CDT_PENETRATION_COUNT_NR` | Conduits, Pipes, Ducts | `SlabPenetrationDetector` | schedule, JBAutoPlacer (avoid-collision) |
| `ELC_CDT_INSTALL_METHOD_TXT` | Conduits, Conduit Fittings | `AutoConduitDrop` | takeoff, schedule |
| `ELC_CDT_FAB_METHOD_TXT` | Conduits | `AutoConduitDrop` | fabrication |
| `ELC_CDT_CBL_FILL_PCT` | Conduits | `ConduitFillValidate` | `ElectricalStandardsValidator` |
| `ELC_CONDUIT_ROUTE` | Conduits | `ConduitAutoRouteCommand`, `Consolidator` | route grouping |

### Junction box params
| Parameter | Bound to |
|---|---|
| `ELC_JB_TYPE_TXT` | Electrical Equipment |
| `ELC_JB_SIZE_MM` | Electrical Equipment |
| `ELC_JB_AUTO_PLACED_BOOL` | Electrical Equipment |
| `ELC_JB_REASON_TXT` | Electrical Equipment |
| `ELC_JB_IP_RATING_TXT` | Family-internal (seed JSON) |
| `ELC_JB_UPSTREAM_REF_TXT` | Family-internal (seed JSON) |
| `ELC_JB_DOWNSTREAM_REF_TXT` | Family-internal (seed JSON) |

### Penetration params
| Parameter | Bound to |
|---|---|
| `STING_PENETRATION_REF_TXT` | Conduits, Pipes, Ducts |
| `STING_PENETRATION_FIRE_RATING_TXT` | Conduits, Pipes, Ducts |
| `STING_FIRE_RATING_TXT` | Walls, Floors |
| `STING_WALL_ROUTING_FLAG` | Walls (ALLOW = pathfinder routes through) |
| `STING_SLAB_ROUTING_FLAG` | Floors (ALLOW = pathfinder routes through) |
| `PEN_FIRE_RATING_TXT` | Specialty Equipment (FRP family) |
| `PEN_OD_MM` | Specialty Equipment |
| `PEN_HOST_REF_TXT` | Specialty Equipment |
| `PEN_MEMBER_ID_TXT` | Specialty Equipment |
| `PEN_CONTROL_NUMBER_TXT` | Specialty Equipment (sequential FRP-NNNN) |
| `PEN_SEALANT_TYPE_TXT` | Specialty Equipment |
| `PEN_CERTIFICATION_TXT` | Specialty Equipment |
| `PEN_INSTALL_STATUS_TXT` | Specialty Equipment (DRAFT / INSTALLED / INSPECTED) |
| `PEN_INSTALLER_TXT`, `PEN_INSTALL_DATE` | Specialty Equipment |
| `PEN_INSPECTOR_TXT`, `PEN_INSPECTION_DATE` | Specialty Equipment |

### Circuit grouping params
| Parameter | Bound to | Purpose |
|---|---|---|
| `ELC_CIRCUIT_GROUP_TXT` | Lighting Fixtures, Electrical Fixtures, Electrical Equipment, Fire Alarm, Communication, Data | Logical group key (kitchen / emergency-lighting / fire-alarm / comms-room) |
| `ELC_PNL_CIRCUIT_GROUP_TXT` | Electrical Equipment | Group tag carried by panel after first BatchAssignCircuits run — pins panel to that group on subsequent runs |
| `ELC_PANEL_REF_TXT` | Electrical Systems | Back-ref to assigned panel |

### Seed identity params (Wave E1)
| Parameter | Bound to | Purpose |
|---|---|---|
| `STING_SEED_FAMILY_TXT` | All MEP categories + Specialty Equipment + Generic Models | Seed identifier (e.g. `STING_SEED_LightingFixture`) — swap registry primary key |
| `STING_DESIGN_REF_TXT` | Same | Original seed identity preserved across swap |
| `STING_SWAP_HISTORY_TXT` | Same | Append-only audit: `ts|operator|src|dst` |

### Photometric params
| Parameter | Bound to | Purpose |
|---|---|---|
| `ELC_PHOTO_LUMENS` | Lighting Fixtures | Luminaire output (drives lumen-method count) |
| `ELC_PHOTO_FILE_PATH` | Lighting Fixtures | Path to .ies / .ldt photometric file |
| `ELC_PHOTO_WATTS` | Lighting Fixtures | Power draw |
| `ELC_PHOTO_CCT` | Lighting Fixtures | Colour temperature K |
| `ELC_PHOTO_CRI` | Lighting Fixtures | Colour rendering index |
| `ELC_LIGHTING_UF_FACTOR` | Lighting Fixtures | Utilisation factor override |
| `ELC_LIGHTING_MF_FACTOR` | Lighting Fixtures | Maintenance factor override |
| `ELC_LIGHTING_TARGET_LUX_TXT` | Lighting Fixtures | Stamped by `LightingGridCommand` |
| `ELC_LIGHTING_GRID_SPC_MM` | Lighting Fixtures | Stamped by `LightingGridCommand` |

---

## Troubleshooting matrix

| Symptom | Likely cause | Fix |
|---|---|---|
| `Auto-Route` skips every cable with "no panel" | Cables have no `BaseEquipment` set | Run `Circuit_AssignAuto` first |
| `Junction boxes auto-placed: 0` after auto-route | `STING_SEED_JunctionBox` family not loaded | Run `Build Seed Families`, finish per `Families/Seeds/README.md`, reload |
| `STING_SEED_SpecialityEquipment family not loaded` warning | Same | Same |
| Validator finds duplicate findings on the same conduit | Pre-Wave-H build | Pull latest; Wave H2 dedups by `(ElementId, code-domain)` |
| Cable schedule shows zero conduits | Manifest not synced | Re-run `Cable_AutoRouteConduit`; `RouteTrayIds` only populates after a successful route |
| Cable schedule shows zero junction boxes | Pre-Wave-H4 build | Pull latest; Wave H4 syncs `cable.JunctionBoxIds` after JB placement |
| Swap leaves orphaned connectors | Manufacturer family has different connector positions | Wave F3 auto-rejoins within 600 mm; if more than that, run `Cable_AutoRouteConduit` again |
| Panel schedule template "no template available" | Required `PanelScheduleTemplate` not loaded | Load a template with name matching `STING_PANEL_SCHEDULE_TEMPLATES.json` rule |
| `MaxBendsForConduit` returns 3 when I expected 4 | Conduit has no `ELC_CDT_MAT_TXT` set | Stamp the material code (GS-GALV / UPVC / etc.) — defaults conservative when unknown |
| `Validation_BS7671` runs slowly on 5000+ conduits | Two FilteredElementCollector passes | Already merged in Wave A; one collector per validator, shared bend count read |
| `Auto-Route` produces conduits inside walls | `STING_WALL_ROUTING_FLAG = ALLOW` set on a wall not meant to be routable | Set wall type's flag to `DENY` (or omit — DENY is default) |
| Lighting grid puts fixtures inside diffusers | Pre-ObstructionIndex build | Pull latest; lights now reject points colliding with ceiling MEP |
| Healthcare validator fires on non-healthcare project | `PRJ_ORG_HEALTH_FACILITY_TYPE_TXT` accidentally set | Clear the parameter — `RunAllHealthcareValidators` is gated on its presence |

---

## Standards cited

| Standard | Where used |
|---|---|
| **BS 7671:2018+A2:2022** | Cable sizing, voltage drop (Appendix 4), §522.8 conduit installation rules, §131 circuit identification |
| **BS 7671 §522.8.4** | Max 6 m run length between draw-in points |
| **BS 7671 §522.8.5** | Max 3 bends between draw-in points (size-aware via IET GN1 §7.4) |
| **IET Guidance Note 1 (2024) §7.4** | Size-aware bend cap (≥40 mm steel allows 4 bends) |
| **IET Wiring Regs §A2** | 5% pull-slack for cable BOM |
| **BS EN 61386** | Conduit + cable trunking systems (fill table, bend radii, standard angles) |
| **BS EN 61386-21** | Rigid PVC conduit |
| **BS EN 61386-22** | Steel conduit |
| **BS EN 61386-23** | Flexible conduit |
| **BS EN 61439** | Low-voltage switchgear and controlgear assemblies (consumer units, DBs) |
| **BS EN 60670** | Electrical accessories — boxes / enclosures |
| **BS EN 50174-2** | Information technology cabling installation (separation distances) |
| **BS EN 12464-1** | Light and lighting of work places (lux targets) |
| **CIBSE LG7** | Lighting guide for offices |
| **CIBSE Guide B3** | Distribution systems |
| **CIBSE Guide B4** | Service zones (50 mm clearance from ceiling soffit) |
| **BS 5266-1** | Emergency lighting on dedicated panel |
| **BS 5839-1** | Fire detection and fire alarm systems |
| **BS 5839-8** | Voice alarm systems |
| **BS EN 12845** | Sprinkler installation (LPC Rules) |
| **BS 9999** | Fire safety in design, management and use of buildings (Appendix L — fire-stop register) |
| **BS 476-20** | Fire resistance tests |
| **BS EN 1366-3** | Fire resistance tests for service installations (penetrations) |
| **BS EN 12056** | Gravity drainage systems (slope minimums) |
| **BS EN 13914** | Plastering / wall finish thickness |
| **BS EN 13830** | Curtain walling |
| **Eurocode 2 / UK NA** | Concrete cover (chase routing) |
| **HTM 06-01** | Electrical services supply and distribution (Healthcare Pack) |
| **HTM 02-01** | Medical gas pipeline systems (Healthcare Pack) |
| **NFPA 99** | Health care facilities code (Healthcare Pack) |
| **BS EN 54** | Fire detection and fire alarm systems components |

---

## Where to read more

- **`Families/Seeds/README.md`** — full visual-polish guide for the 11 seed families (per-seed 2D symbol drawing, 3D mass refinement, type variant naming).
- **`docs/CHANGELOG.md`** — phase-by-phase history of every wave commit.
- **`StingTools/Data/STING_FAMILY_SWAP_REGISTRY.json`** — the swap registry; add manufacturer-specific entries here for your firm.
- **`StingTools/Data/STING_ELECTRICAL_ASSIGNMENT.json`** — voltage bands + circuit-grouping rules; override per project at `<project>/_BIM_COORD/electrical_assignment.json`.
- **`StingTools/Data/Seeds/STING_SEED_*.json`** — seed family specs; drop a new JSON here to add a 12th seed without code changes.
- **`docs/MEP_SYMBOL_GUIDE.md`** — symbol library authoring (companion to seed authoring).
- **`docs/PLACEMENT_CENTRE_GUIDE.md`** — fixture placement workflow (upstream of routing).

---

*Last updated: branch `claude/review-string-electrical-2lZBw` tip `6653d4b4` — Waves A through J + post-merge cleanup.*
