# STING Wire Annotation Workflow Guide

**Branch**: `claude/variable-wire-slash-count-Dan9D`  
**Standard**: BS 7671:2018 / BS 9999:2017 / IEEE 1584-2018  
**Status**: Gaps 1–15 implemented. ARC-01 (IEEE 1584-2018 polynomial coefficients) pending licensed-standard verification — do not use arc flash output for engineering sign-off until confirmed by a qualified electrical engineer.

---

## Overview of Capabilities

The STING wire annotation system adds 10 new commands and a set of inline dock-panel controls to the electrical workflow. Together they provide:

| Area | What STING does |
|---|---|
| **Parameter stamping** | Copies circuit data (phase, cores, panel name, demand current) from Revit's native ElectricalSystem to STING shared parameters on conduit elements |
| **Cable sizing** | Runs BS 7671 Appendix 4 derating against install method and derives CSA, ampacity, and circuit-breaker rating |
| **Voltage drop** | Calculates VD% from conduit length × CSA × conductor material; flags exceedances in the annotation label |
| **CPC / earth sizing** | Applies BS 7671 Table 54.7 to compute minimum protective conductor CSA |
| **Variable slash annotations** | Places 1–4 oblique slash marks on each conduit in the active view; slash count driven by `ELC_WIRE_CORE_COUNT_INT` (clamped 0–4) |
| **Full label** | Appends a human-readable text label: cores × CSA, material, FR/SWA/SCR flags, phase, circuit number, panel, type, install method, conduit OD, fill%, Iz/Ib, and VD alarm suffix |
| **Style control** | 3-layer style hierarchy: project JSON → per-conduit parameter overrides → view-scale auto-factor; all major dimensions editable via inline dock-panel controls |
| **Home-run arrows** | BFS traversal of the full conduit run to place a directional arrow at the panel-side endpoint |
| **Routing validation** | Checks BS 9999 fire-segregation and BS 7671 SWA armour rules; produces a structured issues list with standard references |
| **Cable schedule** | Generates `cable_schedule.csv` and a Revit `ViewSchedule` named "STING Cable Schedule" |
| **Selective coordination stamp** | Reads TCC data and stamps `ELC_SEL_COORD_OK` on panels |
| **Arc flash labels** | Calculates incident energy per IEEE 1584-2018 polynomial model and places a NFPA 70E PPE-category warning label |

---

## New Shared Parameters

These four parameters must be bound before the new commands will write successfully. Run **Load Params** (CREATE tab) after adding them to `MR_PARAMETERS.txt`.

| Parameter | Type | Group | Purpose |
|---|---|---|---|
| `ELC_PNL_NAME_TXT` | TEXT | 4 (Electrical) | Source panel name stamped on conduit by W-Stamp |
| `ELC_PANEL_MAIN_BREAKER_TXT` | TEXT | 4 (Electrical) | Main incomer rating for TCC lookup (e.g. "250A 65kA MCCB") |
| `ELC_SEL_COORD_OK` | YESNO | 4 (Electrical) | Selective coordination pass/fail result |
| `ELC_WIRE_ARMOUR_CONT_OK_BOOL` | YESNO | 4 (Electrical) | SWA armour continuity test sign-off |

---

## New Command Tags

| Tag | Class | Gap |
|---|---|---|
| `Electrical_WireParamStamp` | `WireParamStampCommand` | 1 — stamp single conduit from circuit |
| `Electrical_BatchWireParamPopulate` | `BatchWireParamPopulateCommand` | 2 — batch stamp view/selection |
| `Electrical_WireVDSync` | `WireVDSyncCommand` | 3 — VD calculate + write back |
| `Electrical_WireCableSizerSync` | `WireCableSizerSyncCommand` | 4 — cable size + write back |
| `Electrical_CableScheduleBuild` | `CableScheduleBuilderCommand` | 5 — schedule view + CSV |
| `Electrical_HomeRunFull` | `WireHomeRunFullCommand` | 9 — BFS home-run arrow |
| `Electrical_WireSaveStyle` | `HandleWireSaveStyleFromPanel` | 7 — save inline style to project JSON |
| `Electrical_WireCpcSizer` | `WireCpcSizerCommand` | 11 — CPC / earth sizing |
| `Electrical_WireRoutingValidation` | `WireRoutingValidationCommand` | 12 — routing rule validation |
| `Electrical_WireCoordStamp` | `WireCoordStampCommand` | 13 — coordination stamp on panels |

Existing commands retained: `Electrical_WireAnnotate` (W-Ann), `Electrical_WireAnnotateBatch` (W-Batch), `Electrical_RefreshWireAnnotations` (W-Rfsh), `Electrical_ClearWireAnnotations` (W-Clr), `Electrical_WireHomeRun` (H-Run).

---

## Step-by-Step Manual Workflow

### Prerequisites

1. Open your Revit project (`.rvt`).
2. Ensure conduits are routed and circuit-connected: drawn with the Conduit tool and attached to panels via Revit's ElectricalSystem (Modify → Create Circuit or similar).
3. Open the STING dock panel: ribbon → **STING Tools** → **STING Panel**.
4. Bind shared parameters: **CREATE tab → Load Params**. Confirm "Bound N parameter(s)". Re-run after adding the four new parameters above.

---

### Step 1 — Stamp circuit data onto conduits

**Purpose**: Populate `ELC_WIRE_PHASE_TXT`, `ELC_WIRE_CORE_COUNT_INT`, `ELC_CIRCUIT_NR_TXT`, `ELC_PNL_NAME_TXT`, `ELC_WIRE_CIRCUIT_TYPE_TXT`, `ELC_WIRE_COND_MAT_TXT`, and `ELC_WIRE_MAX_DEMAND_A` from the connected ElectricalSystem.

**Single conduit (W-Stamp):**
1. BIM tab → **W-Stamp** (`Electrical_WireParamStamp`).
2. Pick one conduit in the active view.
3. TaskDialog confirms: circuit, panel, phase (1Ø / 3Ø), core count, demand current.
4. If "No connected ElectricalSystem found": the conduit is not circuit-connected. Attach it to a panel via Revit's Systems tab first.

**Batch (W-Batch♻):**
1. Optionally pre-select conduits. If none selected, all conduits in the active view are processed.
2. BIM tab → **W-Batch♻** (`Electrical_BatchWireParamPopulate`).
3. A progress dialog shows with cancel (Escape) support.
4. Result: "Stamped: N / Skipped (no circuit): M."

**Phase detection logic** (implemented via `ElectricalSystem.SystemType`):

| SystemType | Phase written | Cores |
|---|---|---|
| `PowerCircuit`, `UPS` | 1Ø | 2 |
| `LightingCircuit` | 1Ø | 2 |
| `ThreePhase`, `ThreePhaseDelta`, `ThreePhaseWye` | 3Ø | 4 |

Core count can be overridden manually in the Properties palette (`ELC_WIRE_CORE_COUNT_INT`). This directly controls slash count (clamped 0–4).

---

### Step 2 — Size cables

**Purpose**: Apply BS 7671 Appendix 4 derating to select cable CSA; derive ampacity and circuit-breaker rating.

**Inputs required** (set in Properties palette before running):
- `ELC_WIRE_MAX_DEMAND_A` — stamped by W-Stamp/W-Batch♻
- `ELC_WIRE_INSTALL_METHOD_TXT` — set manually: A1, A2, B1, B2, C, E, or F (per IEC 60364-5-52 / BS 7671 Table 4A2)

**Cable sizer (Csz-Sync):**
1. Optionally select conduits; if none selected, all in active view are processed.
2. BIM tab → **Csz-Sync** (`Electrical_WireCableSizerSync`).
3. Writes:
   - `ELC_WIRE_CSA_MM2_NUM` — selected cable CSA (mm²)
   - `ELC_WIRE_AMPACITY_A` — derated current rating (A)
   - `ELC_WIRE_CIRCUIT_BREAKER_A` — next standard breaker above demand current
   - `ELC_WIRE_VD_PCT_NUM` — initial VD% estimate

**kW / kVA derivation**:
- 3-phase: `kW = √3 × V × Ib × PF / 1000` (PF defaults to 0.85 if not set)
- 1-phase: `kW = V × Ib × PF / 1000`

---

### Step 3 — Calculate voltage drop

**Purpose**: Refine VD% from actual conduit length after routing. Produces the `** VD=X.X%` alarm suffix in annotations.

**VD sync (VD-Sync):**
1. Optionally select conduits; if none selected, all in active view are processed.
2. BIM tab → **VD-Sync** (`Electrical_WireVDSync`).
3. Reads conduit length from `LocationCurve`, applies material resistivity (Cu = 17.2 nΩ·m, Al = 28.3 nΩ·m), writes updated `ELC_WIRE_VD_PCT_NUM`.
4. VD alarm threshold is set in the wire annotation style (default 3%). Conduits that exceed it display `** VD=X.X%` in the annotation label in red (or the auto-colour override).

---

### Step 4 — Size the protective conductor (earth / CPC)

**Purpose**: Apply BS 7671 Table 54.7 to determine the minimum CPC cross-section.

**CPC sizer (CPC-Sz):**
1. `ELC_WIRE_CSA_MM2_NUM` must be populated (run Csz-Sync first).
2. BIM tab → **CPC-Sz** (`Electrical_WireCpcSizer`).
3. Writes `ELC_WIRE_EARTH_CSA_MM2` using:

| Phase CSA (S) | Minimum CPC |
|---|---|
| S ≤ 16 mm² | = S |
| 16 < S ≤ 35 mm² | 16 mm² |
| S > 35 mm² | S/2, rounded up to next standard size |

4. After sizing, validate adiabatically: `S_min = √(I²t) / k` where k = 143 (Cu/70°C PVC) or 115 (Al). This calculation is not automated — perform it manually or via the formula evaluator.

---

### Step 5 — Place wire annotations

**Annotation label format** (auto-built from parameters):
```
N × CSAmm² Mat [FR] [SWA] [SCR]  |  Phase  |  Circuit#  |  Panel  |  Type  |  Meth.X  |  ØD mm  |  Fill F%  |  Iz=A Ib=A  |  ** VD=X.X%
```

**Slash marks** represent core count:
- 1 slash = 1 conductor
- 2 slashes = 2-core (line + neutral)
- 3 slashes = 3-core
- 4 slashes = 4-core (3-phase + neutral)

Slashes are oblique detail lines drawn across the conduit centreline; angle, length, and spacing are all configurable (see Step 6).

**Single conduit (W-Ann):**
1. BIM tab → **W-Ann** (`Electrical_WireAnnotate`).
2. Pick one conduit.
3. Slash marks and text label are placed in the active view.

**Batch (W-Batch):**
1. Optionally pre-select conduits; if none selected, all conduits in the active view are processed.
2. BIM tab → **W-Batch** (`Electrical_WireAnnotateBatch`).
3. Each label position is checked against a per-view collision registry. When the preferred position (perpendicular offset from midpoint) overlaps an existing label or element bounding box, the label shifts along the conduit until a clear slot is found.
4. Progress dialog with Escape cancellation.

**Refresh existing (W-Rfsh):**
- **W-Rfsh** (`Electrical_RefreshWireAnnotations`) — updates all existing STING wire annotations in the active view to reflect current parameter values. Use after any Csz-Sync / VD-Sync / CPC-Sz run. Does not delete and re-place; updates text in-situ.

**Clear (W-Clr):**
- **W-Clr** (`Electrical_ClearWireAnnotations`) — removes all STING wire annotations from the active view. Conduit element parameters are not affected.

---

### Step 6 — Control annotation style

#### Style hierarchy (lowest number wins)

1. Project defaults from `STING_WIRE_ANNOT_STYLE.json` (alongside the `.rvt` file).
2. Per-conduit overrides from `ELC_WIRE_ANNOT_*` shared parameters on each conduit element.
3. View-scale auto-factor (Gap 6): when no per-conduit override and `ScaleFactor = 1.0`, the engine multiplies lengths and gaps by `viewScale / 100` so slashes remain legible at different drawing scales.

#### Inline dock-panel controls

In the BIM/Electrical section of the dock panel:

| Control | Parameter | Default | Notes |
|---|---|---|---|
| Slash length | `tbWireSlashLen` | 6 mm | Real-world model-space mm |
| Slash gap | `tbWireSlashGap` | 3 mm | Spacing between adjacent slashes along conduit axis |
| Slash angle | `tbWireSlashAngle` | 60° | 30–90°; 60° = BS 7671 oblique convention; 90° = perpendicular tick |
| Scale factor | `tbWireScaleFactor` | 1.0 | Multiplies length, gap, and label offset; also suppresses view-scale auto-factor when set per-conduit |
| Line weight | `tbWireLineWeight` | 3 | 1–16 (Revit line-weight index) |
| Label offset | `tbWireLabelOffset` | 600 mm | Perpendicular distance from conduit centreline to text label |
| Colour code | `cbWireColorCode` | 0 (auto) | 0=auto / 1=red / 2=blue / 3=orange / 4=green |
| Show VD | `chkWireShowVD` | ✓ | Show `** VD=X.X%` suffix when VD exceeds alarm |
| Show fill | `chkWireShowFill` | ✓ | Show `Fill X.X%` and ⚠ when conduit fill exceeds alarm |
| Compact | `chkWireCompact` | ☐ | Omit panel name, circuit type, install method from label |

**Auto-colour logic** (colour code = 0):
- VD > alarm threshold **or** fill > alarm → red
- Fire-rated cable → orange
- Armoured or shielded → blue
- Default → black

#### To save style as project default
1. Set values in the inline controls.
2. Click **W-Styl** / "Save style to project JSON" (`Electrical_WireSaveStyle`).
3. Values write to `STING_WIRE_ANNOT_STYLE.json` alongside the project file.
4. Click **W-Rfsh** to apply to all existing annotations.

#### To set per-conduit overrides
1. Select conduit(s) in Revit.
2. In the Properties palette, find the `ELC_WIRE_ANNOT_*` parameters and set as needed.
3. Per-conduit values override the project JSON on next W-Rfsh.

---

### Step 7 — Home-run arrows

**Purpose**: Indicate the direction back to the source panel on long conduit runs.

**Full run (HR-Full):**
1. BIM tab → **HR-Full** (`Electrical_HomeRunFull`).
2. Pick any conduit segment in the run.
3. The engine performs BFS through the connector graph (using `(owner.Id, conn.Id)` tuples as visited keys to avoid looping) to collect all connected conduit segments, then identifies the panel-side endpoint by finding the end connected to `OST_ElectricalEquipment`.
4. A detail-line arrow (shaft 150 mm, arrowhead 30 mm) is placed at that endpoint. Lines are tagged comment "STING_HOME_RUN" for later identification / cleanup.
5. If no panel endpoint found: verify the conduit run is circuit-connected and the panel is categorised as `OST_ElectricalEquipment`.

**Simple (H-Run):**
- **H-Run** (`Electrical_WireHomeRun`) — places an arrow from the selected end of a single picked conduit segment, without BFS traversal.

---

### Step 8 — Validate routing

**Purpose**: Check fire-rated and armoured cable routing per BS 9999:2017 §19.3 and BS 7671:2018.

**Set conduit flags** (Properties palette):
| Parameter | Value | Meaning |
|---|---|---|
| `ELC_WIRE_FIRE_RATED_BOOL` | 1 | Fire-survival cable (MICC, FP200, BS 6387 CWZ) |
| `ELC_WIRE_ARMOURED_BOOL` | 1 | SWA / SWBA / AWA armoured cable |
| `ELC_WIRE_ARMOUR_CONT_OK_BOOL` | 1 | Armour continuity tested at both terminations |
| `ELC_WIRE_INSTALL_METHOD_TXT` | A1/A2/B1/B2/C/E/F | IEC 60364-5-52 installation method |

**Validate (Rt-Val):**
1. BIM tab → **Rt-Val** (`Electrical_WireRoutingValidation`). Read-only — no transaction.
2. Review the structured issues list:

| Rule | Trigger condition | Standard |
|---|---|---|
| FR-1 | Fire-rated cable routed in method A1 (shared conduit with other circuits) | BS 9999:2017 §19.3 / BS 7671 Reg 422.2.1 |
| FR-2 | Fire-rated cable on surface (method C) — advisory to verify ≥150 mm clearance from heat sources | BS 7671:2018 §422.2 |
| SWA-1 | SWA cable without `ELC_WIRE_ARMOUR_CONT_OK_BOOL = 1` | BS 7671:2018 §543.3 |
| SWA-2 | SWA cable in metallic containment (method A1 or A2) without bonding confirmation | BS 7671:2018 §542.2 |

**Resolve and re-validate:**
- Set `ELC_WIRE_ARMOUR_CONT_OK_BOOL = 1` after testing armour continuity.
- Change fire-rated cables from method A1 to a dedicated fire-rated containment.
- Re-run Rt-Val until zero issues.

---

### Step 9 — Generate cable schedule

**Purpose**: Produce a Revit schedule view and a CSV file for procurement / O&M handover.

**Prerequisites**: Run Steps 1–4 to ensure all `ELC_WIRE_*` parameters are populated.

**Cable schedule (Cbl-Sched):**
1. BIM tab → **Cbl-Sched** (`Electrical_CableScheduleBuild`).
2. The command:
   - Writes `cable_schedule.csv` to `<project>/_BIM_COORD/cable_schedule.csv`
   - Creates or refreshes a Revit `ViewSchedule` named "STING Cable Schedule" on the conduit category
   - Schedule columns: Length (m), Circuit Nr, Phase, Cores, CSA (mm²), Material, Ampacity, VD%, Install Method, Max Demand (A), Circuit Breaker (A), Earth CSA (mm²), Circuit Type
   - Sorted by `ELC_CIRCUIT_NR_TXT`
3. Open the schedule from Project Browser → Schedules/Quantities → "STING Cable Schedule".

Note: the schedule is recreated from scratch on each run (delete + recreate pattern) to avoid Revit's restriction on removing managed schedule fields.

---

### Step 10 — Selective coordination stamp

**Purpose**: Stamp `ELC_SEL_COORD_OK` on each panel to record that upstream/downstream TCC curves have been checked per BS 7671 §536.

**Prerequisites:**
- Place `STING_TCC_DATABASE.json` in the project data folder.
- Set `ELC_PANEL_MAIN_BREAKER_TXT` on each `OST_ElectricalEquipment` element via Properties palette (e.g. `"250A 65kA MCCB"`).

**Stamp (Crd-Stmp):**
1. BIM tab → **Crd-Stmp** (`Electrical_WireCoordStamp`).
2. Collects all electrical equipment in the project.
3. For each panel: looks up `ELC_PANEL_MAIN_BREAKER_TXT` in the TCC database.
   - Match found → `ELC_SEL_COORD_OK = 1`
   - No match → `ELC_SEL_COORD_OK = 0`
4. Full upstream/downstream device-pair coordination (log-log TCC interpolation via `SelectiveCoordEngine`) is run from the BIM Coordination Center commands.

TCC interpolation uses log-log linear interpolation: `ln(y0) + t·(ln(y1)−ln(y0))` where `t = (ln(x)−ln(x0)) / (ln(x1)−ln(x0))` — this is appropriate for both time-current curves and prospective fault current axes.

---

### Step 11 — Arc flash labels

**Purpose**: Calculate incident energy and PPE category per IEEE 1584-2018 and place a warning label on each panel.

**⚠ WARNING — ARC-01 OPEN ISSUE**: The polynomial coefficients in `ArcFlashEngine.cs` have not been verified against a licensed copy of IEEE Std 1584-2018 (Table 1, Equations 1 and 4). The LV arcing current coefficient appears to be from the 2002 edition. Do not use this output for engineering sign-off or label production until a qualified electrical engineer has reviewed and corrected the coefficient arrays.

**Inputs** (set in Properties palette on the panel element):
- `ELC_FAULT_KA_NUM` — available fault current (kA)
- System voltage (read from connected ElectricalSystem)
- Clearing time (ms) — from connected protective device parameters

**Engine parameters** (`ArcFlashEngine.IncidentEnergy_CalCm2`):
- `enclosureType`: 0 = open air (factor 1.0), 1 = switchgear (1.473), 2 = MCC (1.637), 3 = cable box (2.0)
- `workingDistMm`: NFPA 70E Table 130.5(C) — ≤600 V → 455 mm; ≤15 kV → 910 mm
- `gapMm`: electrode gap — typical 25 mm (LV DB), 32 mm (HV switchgear)

**Label format** (placed as TextNote adjacent to the panel):
```
⚠ ARC FLASH HAZARD
Panel: [name]
Voltage: [V] V
Incident Energy: [X.XX] cal/cm²
Arc Flash Boundary: [X] mm
Working Distance: [X] mm
[PPE Category N] or [DANGER — EXCEEDS CAT 4]
WEAR APPROPRIATE PPE BEFORE ENERGIZING
NFPA 70E / IEEE 1584-2018
```

**PPE categories** (NFPA 70E Table 130.5(G)):

| Energy | Category |
|---|---|
| ≤ 1.2 cal/cm² | Cat 0 |
| ≤ 4.0 cal/cm² | Cat 1 |
| ≤ 8.0 cal/cm² | Cat 2 |
| ≤ 25.0 cal/cm² | Cat 3 |
| ≤ 40.0 cal/cm² | Cat 4 |
| > 40.0 cal/cm² | DANGER — de-energise before working |

Arc flash boundary (the distance at which incident energy = 1.2 cal/cm²) is found by binary search (30 iterations) in the engine.

---

## Recommended Project Workflow Sequence

For a new project, run commands in this order:

```
Load Params → W-Batch♻ → Csz-Sync → VD-Sync → CPC-Sz → Rt-Val → W-Batch → Cbl-Sched → Crd-Stmp
```

| Step | Command | What it does |
|---|---|---|
| 1 | Load Params | Bind ELC_WIRE_* shared parameters to conduit elements |
| 2 | W-Batch♻ | Stamp circuit data (phase, cores, panel, demand) from ElectricalSystems |
| 3 | Csz-Sync | Size cables per BS 7671 derating; write CSA and ampacity |
| 4 | VD-Sync | Calculate VD% from actual conduit lengths |
| 5 | CPC-Sz | Size protective conductors per BS 7671 Table 54.7 |
| 6 | Rt-Val | Validate fire-rated and SWA routing rules; resolve all issues |
| 7 | W-Batch | Place wire annotations on all conduits in active view |
| 8 | Cbl-Sched | Generate cable schedule CSV and Revit schedule view |
| 9 | Crd-Stmp | Stamp selective coordination results on panels |

After any parameter changes (re-routing, circuit changes, load updates): re-run W-Batch♻ → Csz-Sync → VD-Sync → W-Rfsh.

---

## Known Limitations and Caveats

1. **ARC-01** — IEEE 1584-2018 polynomial coefficients unverified. See Step 11 warning.
2. **SYNC-12 (deferred)** — W-Rfsh is not triggered automatically after Csz-Sync / VD-Sync / CPC-Sz. Run W-Rfsh manually after any parameter update to refresh visible annotations.
3. **CPC adiabatic check** — `S_min = √(I²t) / k` must be performed manually. k = 143 for Cu/70°C PVC earth, 115 for Al.
4. **TCC database** — `STING_TCC_DATABASE.json` is not shipped; populate from manufacturer data sheets.
5. **Arc flash** — panel families must be category `OST_ElectricalEquipment` and circuit-connected for fault current to be readable.
6. **SWA bonding** — SWA-2 rule flags any SWA cable in metallic containment (method A1/A2). Set `ELC_WIRE_ARMOUR_CONT_OK_BOOL = 1` after confirming both-end bonding and continuity test.
7. **View-scale auto-factor** — applies only when no per-conduit `ELC_WIRE_ANNOT_SCALE_FACTOR` override is present and the project JSON `ScaleFactor = 1.0`. Setting a non-1.0 project default disables the auto-factor globally.
