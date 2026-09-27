# STING MEP design engines — Revit smoke-test checklist

**Purpose:** run, in a live Revit session, the Revit-side code from the MEP gap review (ROADMAP
MEPG-1 … MEPG-10) that has only been compiled and unit-tested. The calculations themselves are
covered by `StingTools.Mep.Tests`. What is NOT covered, and what this checklist exists for, is
everything between the model and those calculations: connector walks, parameter reads and
writes, drafting-view drawing, dialogs and dispatch.

**Written:** 2026-09-26, branch `claude/laughing-mccarthy-mgxwyh`. Hand-maintained and not
CI-gated. Check the date before trusting a step.

**Legend:** `[RO]` read-only · `[M]` modifies the model · **Pre** what must exist or be selected ·
**Expect** the pass criterion · **Note** a known limit, so a correct result is not scored as a
bug. Mark each **P** (pass) / **F** (fail) / **B** (blocked, precondition missing) / **N** (n/a).
Record the StingTools log line for every **F** (`StingTools_yyyyMMdd.log` next to the DLL).

---

## Step 0 — Build, deploy, load

| ✓ | Step | Expect | Note |
|---|---|---|---|
| ☐ | Build: `dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"` | 0 errors, 0 warnings | |
| ☐ | Run `deploy.bat` from this checkout, then `grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin` | The manifest points at this checkout's `CompiledPlugin\` | Close Revit and the Planscape Companion first. Copying into any other folder fails silently. |
| ☐ | Open Revit, then ribbon **❄ HVAC → STING HVAC** and **💧 STING Plumbing** | Both panels open | |
| ☐ | Main panel → Load Shared Params | `NRG_HEATING_LOAD_W`, `HVC_PEAK_*`, `PLM_PUMP_*` bound | Several steps below write these. |

## Step 1 — Test model

| ✓ | Seed item | Feeds |
|---|---|---|
| ☐ | ≥ 3 **MEP Spaces** with areas, on two levels, with exterior walls and windows; a climate site set (`PRJ_CLIMATE_SITE_ID`) | Block load |
| ☐ | A **supply duct run**: AHU (mechanical equipment) → duct → elbow → damper → duct → diffuser, all connected, with flows | NC prediction |
| ☐ | A **domestic water system**: pipes on two levels with a riser, drawn in 3D, with slopes on a drainage branch | Isometric |
| ☐ | One **pump** family (name contains PUMP) on a piping system with flows | Pump selection |
| ☐ | A **sprinkler system**: an alarm valve (pipe accessory) → riser → range pipes → ≥ 4 heads, all connected, heads with a K-factor (built-in *K-Factor* or a `K-Factor` parameter) | Sprinkler hydraulics |
| ☐ | A **gas installation**: a meter or ECV (pipe accessory or equipment) → copper pipes → 2 appliances; each appliance carries a heat input in a parameter named `Gas Load (kW)` or `Heat Input (kW)` (or `HVC_CAPACITY_KW`) | Gas sizing |
| ☐ | A **stair Room** named e.g. "Stair 1" with ≥ 4 doors on its boundary, some swinging into it, one double-leaf | Stair pressurisation |

---

## HVAC panel

| ✓ | Button / tag | Mode | Pre → Action → **Expect** | Note |
|---|---|---|---|---|
| ☐ | LOADS → **Block load** (`Hvac_BlockLoad`) | [M] | Spaces exist → run → **Expect** result panel says "Cooling"; `HVC_PEAK_SENS_W` / `HVC_PEAK_LAT_W` / `HVC_PEAK_HOUR` set on each Space; LOADS grid Cooling column filled | MEPG-1 |
| ☐ | LOADS → **Heating load** (`Hvac_BlockLoadHeating`) | [M] | after the cooling run → **Expect** panel says "Heating" and "Block (peak) heat loss" as a **positive** kW; `NRG_HEATING_LOAD_W` set (positive); `HVC_PEAK_SENS_W` **unchanged** from the cooling run; grid shows **both** columns and no row reads "no load" | MEPG-1. The failure this guards: heating overwrote the cooling stamp with a negative number. |
| ☐ | LOADS → **Propagate** after both runs | [M] | → **Expect** duct flows follow the COOLING peak | Proves downstream readers still see cooling. |
| ☐ | CALCS → **NC predict** (`Hvac_NcPredict`) | [RO] | select the whole AHU → diffuser run → **Expect** elements listed fan-first in the per-element breakdown regardless of selection order; a **Confidence** line; a **BASIS** section | MEPG-4 |
| ☐ | same, with a fan family matched in `STING_FAN_SPECTRA.json` and a silencer matched in `STING_SILENCER_DATA.json`, run in a Space | [RO] | → **Expect** "DESIGN BASIS" only when no input was assumed | |
| ☐ | same, selecting two disconnected runs | [RO] | → **Expect** a BASIS line "Selection is not one connected duct run" | |
| ☐ | same, a rectangular duct running through the receiving Space | [RO] | → **Expect** a BASIS line on breakout with the wall mass assumed, a "Breakout Lw" row, and a higher NC than the same run outside the room | MEPG-4 |
| ☐ | CALCS → **Refrigerant size** (`Hvac_RefrigSize`) | [RO] | Liquid leg, lift −10 m, mode **Reversible** → note ΔP; repeat with **Cooling only** → **Expect** the trace shows a static head **debit** in both (outdoor unit below, liquid rising); with lift **+10 m** and Cooling only → a **credit** | MEPG-5 |
| ☐ | same, Suction leg, allowance 1.10 then 1.30 | [RO] | → **Expect** subtitle shows the allowance; ΔP rises by the ratio | |
| ☐ | CALCS → **Psychro coil** (`Hvac_PsychroCoil`) | [RO] | defaults → **Expect** outdoor defaults from the climate site; four states in the table; sensible + latent = total; ADP below the off-coil dry bulb; bypass factor between 0 and 1 | Repeat with no climate site set and the outdoor fields untouched: **Expect** a warning that the outdoor air is a placeholder, not a design day. |
| ☐ | same, room sensible load 10 kW | [RO] | → **Expect** a room supply airflow and "enough" / "SHORT" against the coil airflow | |
| ☐ | same, off-coil RH 40 % | [RO] | → **Expect** warning "No moisture removed", ADP "—", SHR 1.00 | |
| ☐ | SYS → **Stair press.** (`Fire_StairPressurisation`) | [RO] | select the stair Room first → **Expect** the form prefilled with door counts from the model (into / out of / double) and typical door size; "Doors open" left at 0 uses the chosen class's figure | Lift landing doors are not modelled as doors — enter them. Doors are read in the stair room's phase. |
| ☐ | run with Class A, then Class B | [RO] | → **Expect** Class B governed by "open-door velocity"; the door opening force line; a BASIS line saying the class criteria are marked verify | MEPG-8 |
| ☐ | RPRT → **Push snapshots** (`Hvac_PushSnapshot`) after the load runs | [RO] | server connected → **Expect** the loads snapshot `totalKw` equals the larger of Σ heating / Σ cooling, not their sum | |

## Plumbing panel

| ✓ | Button / tag | Mode | Pre → Action → **Expect** | Note |
|---|---|---|---|---|
| ☐ | DOCS → **Plumbing Isometric** (`Plumb_Isometric`) | [M] | select one pipe on the water system → **Expect** a drafting view `STING ISO - <system>` opens; one line per pipe; DN labels; riser drawn vertical; valves as bow-ties along their pipe; tees as dots; fixtures labelled; "NOT TO SCALE" title | MEPG-3 |
| ☐ | same, run again | [M] | → **Expect** the SAME view redrawn ("(redrawn)" in the result), no second view, no doubled lines | |
| ☐ | same, drainage branch with a fall | [M] | → **Expect** labels such as `DN100 1:40` on graded runs only | |
| ☐ | same, nothing selected in a 3D view | [M] | → **Expect** one view per system in that view | |
| ☐ | Pump select (`Plumb_PumpSelect`), no catalogue | [M] | → **Expect** "Duty only (no match)" rows; `PLM_PUMP_DUTY_HEAD_M` / `_FLOW_LPS` written; `PLM_PUMP_MODEL_TXT` **not** written; CATALOGUE section says none loaded | MEPG-2. The failure this guards: "STING Placeholder" pumps written as a selection. |
| ☐ | same, with `_BIM_COORD/pump_catalogue.json` holding one pump that covers the duty | [M] | → **Expect** that pump selected and its model written; the file listed under CATALOGUE | |
| ☐ | same, pump on a system with no pipe flow | [M] | → **Expect** "no design flow" and nothing written | |
| ☐ | same, catalogue pump with `curve` points whose rated point covers the duty but whose curve does not | [M] | → **Expect** that pump NOT selected; a pump whose curve does cover it selected with "m on curve at duty" | MEPG-2 |
| ☐ | Booster set (`Plumb_BoosterSet`), no catalogue | [RO] | → **Expect** the RECOMMENDED PUMP section states that no catalogue pump covers the duty | |
| ☐ | Placement Center → **Plumbing Router** (uses `PlumbingFixtureRouter`) in a project with a pipe type but **no piping system type** | [M] | → **Expect** a failure line "no piping system type", no exception | Router guard. Repeat with a fixture hosted off-level: the pipe lands on the nearest level below. |
| ☐ | SPECIALTY → **Sprinkler Hydraulics** (`Fire_SprinklerHydraulics`) | [RO] | select the design-area heads + the alarm valve → OH1 → **Expect** source flow ≈ Σ head flows; source pressure; most remote head = the furthest head; heads table lowest pressure first; CSV path shown | MEPG-7 |
| ☐ | Open the CSV | — | → **Expect** one row per element on the route; pipes carry bore, length, velocity; heads carry K | Check a K read from a US-unit family is ×14.4 (e.g. 5.6 → 80.6). |
| ☐ | same, one head deselected so heads × area < area of operation | [RO] | → **Expect** a warning naming the shortfall | |
| ☐ | same, supply pressure below the demand | [RO] | → **Expect** "INADEQUATE" in red | |
| ☐ | same, selection = heads only (no source) | [RO] | → **Expect** a message asking for exactly one source element | |
| ☐ | same, on a gridded (looped) system | [RO] | → **Expect** subtitle "network method (N loops)"; every head at or above "needs"; source flow = Σ head flows; the CSV has a `_network` suffix | MEPG-9 |
| ☐ | same, a head screwed straight into a tee | [RO] | → **Expect** the head still listed (merged with the tee node), not lost | |
| ☐ | SPECIALTY → **Gas Pipe Sizing** (`Gas_SizePipes`) | [RO] | select the meter → NG, copper, "Check the modelled sizes" → **Expect** every appliance listed with kW and m³/h; worst-appliance drop vs 1 mbar | |
| ☐ | same, "Size and report only" | [RO] | → **Expect** a size per pipe; worst appliance within 1 mbar; nothing changed in the model | |
| ☐ | same, "Size and apply the sizes to the pipes" | [M] | → **Expect** "Pipes resized N"; pipes now at the reported sizes; any size the pipe type lacks listed as a warning and that pipe left at its modelled size, not silently snapped | Undo afterwards. |
| ☐ | same, an appliance with no heat-input parameter | [RO] | → **Expect** it listed as "NO LOAD" and named in a warning | |
| ☐ | same, on a ring main (looped) | [RO] | → **Expect** subtitle "looped installation … network check"; a sizing request is refused with a warning and the modelled sizes checked; the CSV has a `_network` suffix | MEPG-9 |

## Electrical panel

| ✓ | Button / tag | Mode | Pre → Action → **Expect** | Note |
|---|---|---|---|---|
| ☐ | Conduit auto-route → **Avoid structure (A\*)** | [M] | cables in the manifest; a structural column between a load and its panel → **Expect** the conduit goes round the column in straight, axis-aligned runs (not one conduit per 200 mm); the report lists "A\* obstacle-avoiding × N" | MEPG-11 |
| ☐ | same, load on a different level from the panel | [M] | → **Expect** a vertical riser through the slab (floors are not obstacles); slab penetration stamped | |
| ☐ | same, **Rectilinear L/Z** | [M] | → **Expect** the old L/Z runs; report says no obstacle avoidance | |
| ☐ | CABLE → Calculate, BS 7671, PVC, Multicore, method **B2**, 20 A load, 10 m | [RO] | → **Expect** 2.5 mm², basis names Table 4D2A method B, no VERIFY | ELEC-3 |
| ☐ | same, **XLPE (90°C)**, method C, 20 A | [RO] | → **Expect** 1.5 mm², 31 mV/A/m, VERIFY naming mV/A/m (Table 4E2B) | 4E2B has a single source. |
| ☐ | same, XLPE, **armoured**, 110 A | [RO] | → **Expect** 25 mm², z 1.90, no VERIFY | 4E4A C and 4E4B 25 mm² are two-source checked. |
| ☐ | same, PVC, **single-core**, 110 A | [RO] | → **Expect** 35 mm² from Table 4D1A method C, 1.25 mV/A/m, no VERIFY | 4D1B ≥ 25 mm² checked against a scan of the printed table. |
| ☐ | same, PVC, cable type **Multicore armoured SWA**, method C | [RO] | → **Expect** 1.5 mm² from Table 4D4A, no VERIFY | 4D4A and 4D4B ≤ 16 mm² are two-source checked. |
| ☐ | same, armoured, method **D2** (direct in ground) | [RO] | → **Expect** 1.5 mm² (22 A) from Table 4D4A method D2, no VERIFY | D2 checked against IEC 60364-5-52. |
| ☐ | same, cable type **Single-core**, method A1 | [RO] | → **Expect** basis names Table 4D1A method A | |
| ☐ | Arc Flash (after Fault Current) on a three-phase 400 V board | [M] | → **Expect** label "Basis: IEEE 1584-2018 …", "Electrodes: VCB", notes naming the assumed electrode configuration and enclosure; `ELC_ARC_FLASH_IE_CAL_CM2` set | ELEC-1 |
| ☐ | same, `ELC_ARC_FLASH_ELECTRODE_TXT` = `HCB` on the board | [M] | → **Expect** "Electrodes: HCB"; no electrode-assumed note; a different energy | Load Shared Params first so the parameter exists. |
| ☐ | same, an 11 kV switchboard | [M] | → **Expect** calculated (the 2002 model refused MV); gap 152 mm, working distance 914 mm | |

## Drawing types

| ✓ | Step | Mode | **Expect** | Note |
|---|---|---|---|---|
| ☐ | DOCS → Drawing Types → Inspect | [RO] | `fire-sprinkler-layout-A1-1to100`, `fire-section-A1-1to50`, `fire-detail-A3-1to20` listed as corporate, no checksum drift | MEPG-10 |
| ☐ | Produce a sprinkler layout for one level (discipline `FP`, doc type `SPRINKLER`) | [M] | Sheet `FP-SP-<lvl>-001`, view template `STING - Fire Protection Plan`, sprinklers red and bold, other services halftone, sprinklers / pipes / valves tagged | Needs the `STING - Sprinkler Tag`, `Pipe Tag` and `Pipe Accessory Tag` families loaded. |
| ☐ | Doctor on the produced sheet | [RO] | No missing view template, title block or tag family | |

---

## After the run

- Record results in ROADMAP MEPG-7 and MEPG-10: close a row only when every step for it passed.
- Any **F**: open an issue with the step, the model state, the result panel text and the log line.
- MEPG-8 (the `verify` figures in `STING_SPRINKLER_DESIGN.json` and `STING_SMOKE_CONTROL_DESIGN.json`) is a
  review against the printed standards, not a Revit step — it stays open until someone does that review.
