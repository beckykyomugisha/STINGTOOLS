# STING HVAC — A Layman's Guide for First-Time HVAC Designers

**Audience.** You've never produced a real HVAC design before. You've
opened Revit a few times. Someone has handed you a project, said "do the
HVAC", and pointed you at the STING Tools plugin. This guide takes you
from the *first idea* of an HVAC design all the way to a finished,
issue-ready package, explaining **what** every step is, **why** it
exists, and **how** STING does the heavy lifting for you.

> **How to read this guide.** Each section has three parts:
> - *What it is* — the engineering concept in plain English.
> - *Why it matters* — what goes wrong if you skip or fudge it.
> - *How to do it in STING* — the actual buttons / commands, in order.
>
> If you only have an hour, read sections 1, 3, 5, 7, 11 and 21.

---

## Table of contents

1. [What "HVAC design" actually means](#1-what-hvac-design-actually-means)
2. [Why Revit + STING (and not spreadsheets + TRACE)](#2-why-revit--sting-and-not-spreadsheets--trace)
3. [The five things every HVAC design must answer](#3-the-five-things-every-hvac-design-must-answer)
4. [Anatomy of the STING HVAC dock panel](#4-anatomy-of-the-sting-hvac-dock-panel)
5. [Step-by-step: from blank Revit file to issued drawings](#5-step-by-step-from-blank-revit-file-to-issued-drawings)
6. [Climate site and design conditions](#6-climate-site-and-design-conditions)
7. [Block load calculation](#7-block-load-calculation)
8. [Space-type load profiles and DCV](#8-space-type-load-profiles-and-dcv)
9. [Equipment placement](#9-equipment-placement)
10. [IDU selection (VRF / split / FCU)](#10-idu-selection)
11. [Duct design](#11-duct-design)
12. [Pipe design (chilled water, hot water, condensate)](#12-pipe-design)
13. [Refrigerant pipe sizing](#13-refrigerant-pipe-sizing)
14. [Acoustic design](#14-acoustic-design)
15. [Validators](#15-validators)
16. [Commissioning checklist](#16-commissioning-checklist)
17. [Drawings](#17-drawings)
18. [Server integration — Planscape](#18-server-integration)
19. [Comparison with TRACE / HAP / IES — gbXML round-trip](#19-comparison-with-trace--hap--ies)
20. [Workflow presets](#20-workflow-presets)
21. [Common first-timer mistakes (and how STING catches them)](#21-common-first-timer-mistakes)
22. [Glossary of acronyms](#22-glossary)

---

## 1. What "HVAC design" actually means

HVAC stands for **H**eating, **V**entilation and **A**ir **C**onditioning.
The design process for a building's HVAC is the work of deciding:

1. **How much heating or cooling each room needs** — the "load". A
   south-facing glass meeting room in July may need 4 kW of cooling; the
   same room in January may need 2 kW of heating.
2. **How that heating or cooling is produced** — central plant
   (chillers, boilers, heat pumps) versus distributed plant (split
   systems, VRF units, electric heaters).
3. **How it gets to each room** — by air (ducts, diffusers, AHUs), by
   water (pipes, fan-coil units, radiators), or by refrigerant (split
   indoor units, VRF cassettes).
4. **How the rooms breathe** — fresh-air ventilation rates, exhaust from
   toilets and kitchens, pressure relationships between rooms (e.g. an
   operating theatre stays positive relative to the corridor).
5. **How the system is controlled** — thermostats, CO₂ sensors,
   variable-speed drives, the building management system (BMS).
6. **How it complies** — with codes like **ASHRAE 90.1** (US energy),
   **ASHRAE 62.1** (US ventilation), **CIBSE Guide A/B/C** (UK design,
   sizing and noise), **EN 1505/1506** (EU duct sizes), **Part L**
   (UK building regulations), or local equivalents.

In practice you produce three categories of output:

| Category    | What's in it                                                                |
|-------------|-----------------------------------------------------------------------------|
| Drawings    | HVAC plan layouts, duct routes, plantroom plans, riser diagrams, schematic flow diagrams (P&IDs) |
| Schedules   | Equipment schedules (AHUs, FCUs, chillers), duct schedules, pipe schedules, diffuser schedules, control schedules |
| Calculations| Block loads, zone loads, ventilation rates, pressure-drop calcs, NC (noise) predictions, refrigerant charge, commissioning targets |

> **Why this matters for STING.** STING's HVAC workflow is built so that
> the *Revit model* is the source of truth and the drawings + schedules
> + calculations fall out automatically. You don't run a separate load
> calc in TRACE. You don't size ducts in Excel. You design the system
> in 3D, put spaces in the rooms, put equipment on the floor plate, and
> STING does the load calc, the duct sizing, the noise prediction and
> the commissioning checklist. That's the whole point.

---

## 2. Why Revit + STING (and not spreadsheets + TRACE)

A first-timer often asks: "couldn't I just run TRACE 3D Plus or Carrier
HAP and import the equipment list?". Yes — and you'd produce a load
table that *looks* right but is disconnected from the model. When the
architect moves a wall, your TRACE file doesn't know. When you change a
glazing spec, you re-key it manually. When the QS asks how many metres
of 600×400 duct there are, you go and count.

**Revit** is a *Building Information Modelling* tool. Every wall, space,
duct, pipe, AHU and diffuser is an **object** with parameters (flow,
pressure drop, U-value, manufacturer, system). When you change a glass
type, the load updates. When you add a space, it appears in the load
calc. When you re-route a duct, the pressure drop and noise change.

**STING Tools** is a plugin we wrote on top of Revit because Revit out
of the box is a great drawing engine but only a mediocre engineering
engine — it has a built-in System Analysis tool but it's hidden, slow
and misses things like real solar geometry, refrigerant pipe sizing,
NC prediction and commissioning. STING adds:

- A **registered parameter library** (~3,200 shared parameters) so
  every space, AHU, FCU, duct and pipe carries the same data fields
  no matter who placed it.
- A **41-city climate registry** with ASHRAE 2021 design days (0.4 %
  cooling, 99.6 % heating, 99 % and 98 % alternates), elevation-
  corrected air density, time zones and DST flags.
- A **24-hour block load engine** that peaks each system at the *system*
  level (not Σ zone peaks), uses ASHRAE Radiant Time Series for thermal
  mass, real ASHRAE Clear Sky solar geometry, stack-effect + wind
  infiltration, and 12 space-type profiles (Office, MeetingRoom,
  PatientRoom, OperatingRoom, Lab …).
- **Duct sizing engines** for velocity, equal-friction, static-regain
  and constant-pressure strategies — per-role velocity targets, per-
  region standard sizes (UK, US, EU, DE, SE), DW/144 pressure class
  audit, manufacturer fitting Cs.
- **Pipe sizing** with Hardy Cross balancing, PICV characteristic
  curves, pump head curves and the Belimo / Danfoss / Siemens valve
  catalogue.
- **Refrigerant pipe sizing** for R410A, R32, R134a and CO₂ — with
  vendor pipe-length limits (Daikin VRV, Mitsubishi City Multi,
  Toshiba SHRMe), charge calculation and REFNET branch sizing.
- **NC prediction** (VDI 2081 + ASHRAE A48 + Bullock regenerated
  noise) with full octave-band cross-talk auditing and room
  direct + reverberant modelling.
- **Validators** that tell you "duct DC-23 runs at 9.2 m/s — above
  the 7 m/s branch target" or "this OR is balanced for +25 Pa but
  the corridor is also +25 Pa — no cascade".
- **Drawing automation** that produces HVAC plans, plantroom layouts
  and riser diagrams on the right paper, with the right title block,
  at the right scale, with the right view template.
- **Commissioning checklist** generation — a CSV you hand to the
  commissioning agent on day one of witnessing.
- **gbXML round-trip** so TRACE / HAP / IES users can either push their
  loads INTO STING (overwriting STING's calc with the simulator's
  authoritative number) or compare STING's calc against theirs.

You are still the engineer. STING is the apprentice that does the
typing, the math and the cross-referencing.

---

## 3. The five things every HVAC design must answer

Before you click anything, know what you're trying to produce. A
complete HVAC design answers five questions:

1. **What's the load?** Cooling and heating peaks per room, per system,
   and for the whole building. Expressed in kW (sensible) and kW
   (latent). Drives the size of chillers, boilers, AHUs, FCUs and the
   incoming electrical supply.
2. **How is it distributed?** A tree of plant rooms, AHUs, VAVs, FCUs,
   chiller pumps and heating circuits. STING calls each branch a
   **system** and tags it with a system code (`HVAC`, `CHW`, `HWS`,
   `REFRIG`, `EXHAUST`, `OA` for outdoor air).
3. **What's the ventilation rate?** Fresh-air L/s per person plus L/s
   per m² of floor — driven by ASHRAE 62.1 or CIBSE Guide A depending
   on the project. CO₂-driven Demand Controlled Ventilation (DCV)
   reduces fan energy by 30–50 % when occupancy varies.
4. **How big are the ducts, pipes and refrigerant lines?** Sized so
   that (a) they deliver the design flow, (b) the fan/pump can push
   the air/water against the pressure drop, (c) the noise stays
   below NC targets, and (d) refrigerant velocity stays above the
   oil-return floor.
5. **What's the noise level?** Predicted NC in each occupied space —
   typically NC 30 for patient rooms, NC 35 for offices, NC 25 for
   recording studios. Plus cross-talk between adjacent rooms via the
   shared duct system (BB93 / ASHRAE A48 = 30 dB minimum privacy).

STING tracks each of these via parameters. If you populate the
parameters — most importantly `HVC_SPACE_TYPE_TXT` on every space,
`HVC_PROD_REF_TXT` on every equipment family, and
`PRJ_CLIMATE_SITE_ID` on the project — the loads, sizes, noise
predictions and checklists work. If you don't, nothing works. That's
why §6 (climate), §7 (loads) and §8 (space profiles) are the most
important sections of this guide.

---

## 4. Anatomy of the STING HVAC dock panel

When you load STING into Revit, three dockable panels are available:
the **main STING panel** (9 tabs — for tagging, drawings, BIM
management), the **STING Electrical panel** (electrical engineering),
and the **STING HVAC panel** which is what you'll spend most of your
time in.

The HVAC panel is opened via the ribbon: **STING Tools → ❄ HVAC →
STING HVAC**. It docks tabbed behind Revit's Properties palette by
default.

### 4.1 The header context strip

Above the tabs sits a header that drives every calculation. **Set
this before doing anything else** — every command on every tab reads
from it:

| Setting          | Options                                            | Why                                              |
|------------------|----------------------------------------------------|--------------------------------------------------|
| Standard         | CIBSE Guide B/C, ASHRAE 90.1, EN 1505/1506, DIN 4740, SS-EN | Drives table lookups (velocity limits, sizes)    |
| Region           | UK_SI / US_IP / EU_SI / DE_SI / SE_SI              | Picks `standardSizesMm` and `standardBoreMm`     |
| Pressure class   | DW/144 A (≤ 500 Pa) / B (≤ 1000) / C (≤ 2500) / D (≤ 7500) | Sets gauge breakpoints and audit pass/fail       |
| Air density      | 1.20 kg/m³ default + warm / hot / cold presets     | Replaced by climate registry once site is set    |
| Sizing strategy  | Velocity / Equal friction / Static regain / Constant pressure | One radio replaces three separate commands       |
| Scope            | All / Selection / Active view                       | Every action button respects this without re-asking |

Whatever you pick is snapshotted before the command runs on the Revit
API thread, so values stay consistent through long operations.

### 4.2 The seven tabs

| Tab     | Purpose                                                          | When to use                                            |
|---------|------------------------------------------------------------------|--------------------------------------------------------|
| **EQPT** | AHU / FCU / VAV / Chiller / Boiler / Heat pump inventory grids   | Browsing equipment, checking acoustics, COBie data     |
| **SYS**  | Mechanical systems (Supply / Return / Exhaust / OA / CHW / HW / Refrigerant / Condensate) | Fan pressure budget, zones, fire dampers |
| **CALCS**| Sizing strategy + per-role velocity/friction targets + results   | The main calc workspace — block load, sizing, NC       |
| **DUCT** | Duct types, standard-size tables, gauge breakpoints, insulation  | When picking duct types or auditing fab readiness      |
| **LOADS**| Spaces × envelope × internal gains × ventilation × computed loads| Running block loads, importing gbXML, comparing TRACE  |
| **FAB**  | Spool grid, hangers, fab outputs                                  | When the design is frozen and ready for shop drawings   |
| **RPRT** | KPIs, drift, workflow runs, server publish, climate inspect, Cx  | Quality checks before issue                            |

### 4.3 EQPT tab — what each grid carries

| Grid / Expander         | Columns                                                         |
|-------------------------|-----------------------------------------------------------------|
| Equipment inventory     | Tag, type, room, capacity (kW), flow (L/s), manufacturer, system |
| Identity expander       | Type mark, manufacturer, model, serial, COBie tag                |
| Performance expander    | Cooling kW, heating kW, SFP (W/L/s), EER/COP, design fluid temps  |
| Acoustics expander      | Sound power per octave band, NC at 3 m, supplier IL spectrum      |
| Connections expander    | Supply, return, OA, condensate, refrigerant, electrical          |
| COBie expander          | Maintenance interval, warranty start, supplier, drawing ref      |

The grids are populated by `Hvac_RefreshGrids` (RPRT tab). They start
empty on first open.

### 4.4 SYS tab — air, water and refrigerant systems

Air systems table breaks down into Supply / Return / Exhaust / Outdoor
Air / Relief. Water systems into CHW (chilled water) / HW (heating water)
/ LHW (low-temperature heating water) / DCW / DHW / Condensate.
Refrigerant systems into Gas / Liquid lines per refrigerant type.

Each row shows: system name, type, design flow, design pressure drop,
fan / pump curve, served zones, fire damper count.

The fan-pressure budget panel walks you through ESP allocation:
filter (200 Pa) + coils (300 Pa) + duct distribution (300 Pa) + fittings
(100 Pa) + terminal (50 Pa) = 950 Pa total external static pressure.
If your duct sizing pushes any one number out, the panel turns amber.

### 4.5 CALCS tab — the calculation workspace

This is where you live during design. The strategy radio (Velocity /
Equal friction / Static regain / Constant pressure) controls which
algorithm runs. The per-role table lets you override velocity limits
(default: main 8 m/s, branch 6 m/s, runout 4 m/s).

Buttons:

- **Block load** (`Hvac_BlockLoad`) — runs the 24-h design-day calc
  for every space in scope
- **Propagate loads** (`Hvac_PropagateLoads`) — pushes per-space loads
  upstream into duct flow stamps
- **Auto-size** (`Mep_AutoSizeDuct` / `MepAutoSizePipeCommand` /
  `MepAutoSizeConduitCommand`) — applies the active strategy
- **Balance** (`HardyCrossCommand`) — hydronic / aeraulic balance
- **NC predict** (`Hvac_NcPredict`) — fan-to-terminal noise
- **Cross-talk audit** (`Hvac_CrossTalkAudit`) — speech privacy between
  rooms sharing the same duct system
- **Refrigerant size** (`Hvac_RefrigSize`) — gas / liquid pipe sizing
- **Pressure-class audit** (`Hvac_PressureClassAudit`) — DW/144 compliance
- **Full design pass** (`Hvac_FullDesignPass`) — runs every calc in
  sequence with status rows

The live-results panel below the buttons shows per-system totals.
The issues grid surfaces validator warnings — over-velocity ducts,
under-spec pipes, NC exceedances, oil-return failures.

### 4.6 DUCT tab — duct types and fab defaults

Lists every `DuctType` in the project alongside its insulation, gauge
breakpoints (read from the active region in the registry), seam type
and acoustic lining. Per-region standard-size tables (UK_SI: 100, 125,
150, 200, 250, 300, 400, 500, 600, 800, 1000, 1200, 1500, 1800, 2000
mm — full set per the registry). Lets you pick "round vs rectangular
vs flat oval" priority per system. Drives `CreateDuctsCommand` and
`AutoDropCommand`.

### 4.7 LOADS tab — spaces and envelope

The biggest grid in the panel. Lists every Space, with:

- Room name + number
- Space type (HVC_SPACE_TYPE_TXT — `Office`, `MeetingRoom`,
  `PatientRoom`, `OperatingRoom`, `Lab`, `Classroom`, `Retail`,
  `Restaurant`, `Kitchen`, `Warehouse`, `Plantroom`, `Corridor`)
- Envelope segments (walls / windows / roof) — area + U-value + SHGC
- Internal gains — occupants × W/person, lighting W/m², equipment W/m²
- Ventilation — L/s per person + L/s per m²
- Computed loads — peak sensible, peak latent, peak hour, design OA

A "Block load" primary button runs the engine across the whole grid.
A "Import gbXML" button (`Hvac_ImportGbxmlLoads`) overwrites the
columns with values from a TRACE / HAP / IES export.

### 4.8 FAB tab — fabrication

Spool grid (one row per assembly), hanger schedule, output buttons
for cut list, isometrics, weld map, and NC code export. Mostly
inactive until you click `Fabrication_OpenWorkspace` and group
ducts/pipes into assemblies.

### 4.9 RPRT tab — quality + integration

The control room. KPI tiles for total cooling kW, total heating kW,
total OA L/s, predicted building NC, total refrigerant charge.

Buttons:

- **Refresh grids** (`Hvac_RefreshGrids`) — re-scan project to populate panels
- **Climate inspect** (`Hvac_ClimateInspect`) — show active climate site
- **Reload climate** (`Hvac_ClimateReload`) — drop cache after JSON edit
- **Reload rules** (`Hvac_ReloadRules`) — drop MepSizingRegistry cache
- **Detect stale sizes** (`Hvac_DetectStaleSizes`) — find ducts whose
  size doesn't match current flow
- **Generate Cx checklist** (`HvacGenerateCxChecklistCommand`) —
  ASHRAE Guideline 0 / CIBSE TM39 commissioning forms
- **Publish to server** (`Hvac_PublishToServer`) — push grid contents
  to Planscape `/hvac/*` endpoints
- **RTS benchmark** (`Hvac_RtsBenchmark`) — regression-grade RTS check
- **TRACE/HAP compare** (`Hvac_CompareLoads`) — non-destructive load diff

---

## 5. Step-by-step: from blank Revit file to issued drawings

Here is the canonical HVAC workflow. Do these in order.

### 5.1 Open or create the Revit project

If your team uses a federated model, the architect will give you a
**central file** to attach to and an **architectural link** to host
your work against. If you're starting from scratch, open Revit's
*Mechanical Template* and save a new file.

### 5.2 Run **Project Setup Wizard** (main panel → TEMP → ★ Project Setup Wizard)

**Why.** Revit out of the box doesn't know what `HVC_PEAK_SENS_W` or
`HVC_PROD_REF_TXT` is. The wizard binds STING's ~3,200 shared
parameters to the correct categories, creates standard view
templates, filters, worksets and phases, and seeds the project with
`PRJ_ORG_*` parameters so every drawing's title block fills itself in.

**How.** Click `★ Project Setup Wizard`, follow the 7 pages: project
info, disciplines (tick **Mechanical**, **HVAC**, **Plumbing** as
appropriate), location codes, level codes, output paths, review. The
wizard runs `MasterSetupCommand` under the hood (15 sub-steps).

If a colleague has already set up the project, run **TEMP → Setup →
Check Data Files** to confirm the parameter pack is loaded.

### 5.3 Link the architectural model

In Revit: `Insert → Link Revit → architecture.rvt`. Pin the link
(`Modify → Pin`). Copy / Monitor the levels so your HVAC levels stay
in sync.

### 5.4 Set the climate site

**Why.** Every load calculation needs design dry-bulb and wet-bulb
temperatures, solar radiation, latitude (for solar geometry), and
elevation (for air density). Without a climate site, STING falls back
to London.

**How.** When you first open the project, STING's `DocumentOpened`
auto-stamp runs `ClimateRegistry.ActiveSite(doc)`, fuzzy-matches your
`ProjectInformation.Address` against the 41-city corporate catalogue,
and stamps `PRJ_CLIMATE_SITE_ID` and `PRJ_CLIMATE_SITE_LABEL_TXT` on
Project Information. Verify by clicking **RPRT → Climate inspect**.
If the auto-match got it wrong, edit `PRJ_CLIMATE_SITE_ID` directly
(e.g. `manchester`, `tokyo`, `dubai`) and re-stamp.

If your site isn't one of the 41 shipped, drop a project override at
`<project>/_BIM_COORD/climate_data.json` with your site's design day
data — see §6 for the schema.

### 5.5 Set the construction profile

**Why.** Block load reads U-values and SHGCs from a *construction
profile* — `PartL2021` (UK Building Regs current), `PartL2013`,
`PreRegs1990`, `Passivhaus`, `IECC2021_CZ4`, `ASHRAE901_2019_CZ5` or
`EnEV2014_DE`. The choice changes the wall U-value from 0.18 W/m²K
(PartL2021) to 0.45 W/m²K (PreRegs1990) — a 2.5× swing on the
fabric load.

**How.** Set `PRJ_CONSTRUCTION_PROFILE_TXT` on Project Information
to the profile that matches your building's specification. STING
ships 7 profiles in `STING_CONSTRUCTION_PROFILES.json`; you can add
your own via `<project>/_BIM_COORD/construction_profiles.json`.

### 5.6 Place Revit Spaces (NOT just rooms)

**Why.** Revit has two spatial concepts: *Rooms* (architectural,
used for finishes and area schedules) and *Spaces* (MEP, used for
heating and cooling loads). STING's BlockLoad runs on Spaces.

**How.** `Analyze → Spaces → Space` (or `Space Naming → Place Spaces
Automatically` to bulk-place one Space per Room). Place a Space in
every conditioned room. Make sure the upper boundary is the
underside of the next slab (Revit defaults to "Computation Height"
which is often wrong — set space upper boundary to "Up to slab" or
similar so cooling-load volume is correct).

### 5.7 Assign space types

**Why.** A 100 m² office (12 W/m² equipment, 7 L/s/person OA, office
occupancy schedule) loads completely differently from a 100 m² lab
(40 W/m² equipment, 12.5 L/s/person OA, 24/7 schedule). STING's 12
space-type profiles (`STING_LOAD_PROFILES.json`) cover the common
ones; you can add custom profiles via override.

**How.** Select every Space, set `HVC_SPACE_TYPE_TXT` to the matching
profile id: `Office`, `MeetingRoom`, `Classroom`, `PatientRoom`,
`OperatingRoom`, `Retail`, `Restaurant`, `Kitchen`, `Lab`,
`Warehouse`, `Plantroom`, or `Corridor`. Or set the Revit `Space
Type` enum and STING will pick up the closest matching profile.

For unusual spaces, leave `HVC_SPACE_TYPE_TXT` blank and STING falls
back to `Office` with a warning in the result panel.

### 5.8 Run **Block Load** (CALCS → Block load)

**Why.** This is the foundation. Block load tells you, per space and
per system: peak sensible cooling, peak latent cooling, peak heating,
peak hour of the day, design outdoor air L/s. Every subsequent step
(equipment selection, duct sizing, pipe sizing, NC prediction) reads
these numbers.

**How.** Click `Hvac_BlockLoad`. Pick scope (typically "All Spaces").
STING runs a 24-h design-day for every space using:

- Climate site dry-bulb (sinusoidal swing from cooling DB to a daily
  range of ~10 K, anchored at solar noon adjusted for DST + longitude)
- ASHRAE Clear Sky direct + diffuse solar with real geometry
  (declination × latitude × hour angle → altitude + azimuth →
  cosine projection on each envelope orientation)
- Construction profile U-values + SHGCs
- Space-type internal gains (lighting + equipment + occupants)
- Space-type ventilation rates
- Optional stack-effect + wind infiltration (CIBSE Guide A §4.6) when
  `LoadZone.Q4PaM3PerHperM2` is set
- ASHRAE Radiant Time Series with per-orientation conduction lag

System-level peak-pick: STING finds the hour at which each *system*
peaks (which may differ from each zone's peak — that's the whole point
of block load) and reports both ΣZone peaks AND the block peak. The
ratio is the **diversity factor** (typically 0.7–0.9 for office
buildings — meaning you can size the central plant 70–90 % of the sum
of zone peaks because not every room peaks at the same hour).

Per-space stamps land on every Space: `HVC_PEAK_SENS_W`,
`HVC_PEAK_LAT_W`, `HVC_PEAK_HOUR`, `HVC_OA_LS`. The panel grid
populates. Top-10 worst zones surface in the result panel.

### 5.9 Place equipment (EQPT tab)

**Why.** Now you know the loads, you can pick equipment. Central AHU
for ventilation, FCUs / VRFs for room-by-room cooling, boilers /
chillers for the central plant, exhaust fans for toilets and kitchens.

**How.** Main STING panel → MODEL → MEP → Place Fixture, pick the
family (`STING - AHU`, `STING - FCU - Ceiling Concealed`, `STING -
Chiller Air-Cooled`, etc.). Click the location on the plan.

Tag with main panel → CREATE → Tag & Combine (`TagAndCombine`).

Set the key equipment parameters:
- `Capacity (kW)` — matches the served system's peak load
- `Design Flow (L/s)` — for air equipment
- `External Static Pressure (Pa)` — for fans
- `HVC_PROD_REF_TXT` — `brand:productCode` for manufacturer
  fitting + acoustic lookup (e.g. `daikin:VRV-5-FXSQ-50P`)

### 5.10 Select IDUs (VRF / split / FCU systems)

For systems based on room-level indoor units (VRF, splits, ducted
FCUs), let STING pick:

**CALCS → Select IDUs** (`Hvac_SelectIdus`). For each Space, STING:

1. Reads peak load (sensible + latent)
2. Reads mounting preference (HVC_IDU_MOUNTING_TXT per-space, or
   PRJ_REFRIG_IDU_MOUNTING_TXT default — `Ducted`,
   `CeilingCassette`, `WallMounted`)
3. Picks the smallest IDU from `STING_IDU_CATALOGUE.json` that
   satisfies duty + min flow + max NC
4. Stamps `HVC_SELECTED_IDU_ID_TXT` and `HVC_SELECTED_IDU_LABEL_TXT`
   on the Space

### 5.11 Propagate loads (CALCS → Propagate loads)

**Why.** The block load engine stamps loads on Spaces. The duct
sizing engine needs flow on Ducts. The bridge is
`Hvac_PropagateLoads`.

**How.** Click the button. STING walks every duct in scope, finds
the served Space via the connector graph (terminal at the downstream
end, falling back to `GetSpaceAtPoint` at the duct mid-point),
reads `HVC_PEAK_SENS_W` + `HVC_OA_LS`, and stamps:

```
HVC_FLOW_LS = max(peak_W / (ρ · cp · ΔT), OA_Ls)
```

with ΔT = 11 K (CIBSE Guide B3 supply-air ΔT). The provenance string
goes into `HVC_LOAD_SOURCE_TXT` so you can audit where each duct's
flow came from.

For ducted refrigerant systems (VRF + ducted IDU), run also
**Propagate Refrig to Duct** (`Hvac_PropagateRefrigToDuct`) — walks
every ducted IDU, computes Q_ls from capacity / ρcpΔT, walks the
downstream HVAC connector graph stamping every duct.

### 5.12 Auto-size ducts (CALCS → Auto-size)

**Why.** Until now your ducts are placeholders. Auto-size picks the
real size for each duct from the registry's standard-size table per
the active strategy.

**How.** Pick the sizing strategy in the header (Velocity / Equal
friction / Static regain / Constant pressure). Click `Mep_AutoSizeDuct`.
STING walks every duct in scope, computes the required size from
flow + velocity / friction target (per-role: 8 m/s main, 6 m/s
branch, 4 m/s runout by default; override per-role in the registry),
and snaps to the nearest standard size from `standardSizesMm` for the
active region.

Per-element segment-role detection (does this duct serve a single
diffuser or feed a branch?) runs via
`HvacSegmentRoleDetector.DetectRolesBatch` so the right velocity
target applies automatically. Override `HVC_SEGMENT_ROLE_TXT`
per-duct if you disagree.

### 5.13 Size pipes (CHW / HW)

Same pattern: `MepAutoSizePipeCommand` reads `HVC_FLOW_LS` (or
LpsParams equivalent for water), looks up per-service velocity +
Pa/m limit (chw 1.5 m/s, hws 1.2 m/s, lhw 1.0 m/s, dhw 1.5 m/s),
picks the nearest standard bore from `standardBoreMm`.

### 5.14 Balance (CALCS → Balance — Hardy Cross)

**Why.** A pump pushes water around a network. The flow that lands
in each branch isn't what the design says — it's what the network's
resistance allows. Hardy Cross iteratively redistributes flows until
the network is self-consistent (Σ flow at every node = 0, Σ
pressure drop around every loop = 0).

**How.** Click `HardyCrossCommand`. STING:

1. Builds a network graph from your pipes
2. Seeds initial flows via `InitializeFromDemand` (per-node demand
   split equally across incident pipes) — no need for you to
   pre-compute flows
3. If a pump is in the loop, reads its head curve
   (`PumpCurve.FromQuadraticThreePoints` from shut-off / BEP /
   run-out)
4. Bisects system curve against pump curve to find the operating point
5. Iterates Hardy Cross until residual < tolerance (default 50 Pa)

PICV characteristic curves from `STING_MEP_SIZING_RULES.json`
`pipe.picvCurves` (Belimo, Danfoss, IFC) tell STING the authority
window per valve — if your design ΔP falls outside `dpMinKpa –
dpMaxKpa`, the PICV's constant-Q behaviour breaks down and STING
warns.

### 5.15 Size refrigerant (CALCS → Refrigerant size)

For VRF or split systems. Click `Hvac_RefrigSize`. Dialog asks for:

- Refrigerant (R410A / R32 / R134a / CO₂)
- Leg (Gas line / Liquid line / Suction)
- Capacity kW
- Run length m (actual route)
- Static lift m (vertical, signed — positive when condenser above
  evaporator, negative below)
- ΔP budget kPa (defaults to vendor budget per leg)
- Vendor series (Daikin VRV IV-S / VRV 5 / IV-H, Mitsubishi City
  Multi Y/R2, Toshiba SHRMe, Generic)

STING runs Darcy-Weisbach with Blasius friction factor across the
ACR size list, applies oil-return velocity floor (~3 m/s for gas,
2 m/s for liquid), credits negative lift back to budget, checks the
subcooling reserve against saturation-pressure-to-temperature slope
(flash-gas warning when ΔT_sat > reserve), and picks the smallest
OD that passes. Vendor pipe-length limits checked against
`STING_REFRIG_VENDOR_LIMITS.json` — total run, first-branch-to-far-
IDU actual + equivalent, ODU↔IDU + IDU↔IDU vertical.

For VRF, run also **REFNET branch sizing** (`Hvac_RefnetSize`) —
walks the refrigerant tree depth-first post-order, computing
downstream connected capacity at each node, and picks the smallest
joint whose `maxKw ≥ downstream` from
`STING_REFNET_JOINTS.json` (Daikin KHRP, Mitsubishi CMY, Toshiba
RBM-BY series).

Finally **Refrigerant charge** (`Hvac_RefrigerantCharge`) — walks
project refrigerant pipes grouped by OD, applies vendor charge tables
from `STING_REFRIG_CHARGE_TABLES.json`, adds the vendor short-system
offset, reports total kg per system.

### 5.16 Predict NC (CALCS → NC predict)

**Why.** A duct that's "the right size" hydraulically might still
deliver 50 dB(A) to a conference room that needs NC 30. NC prediction
catches it before construction.

**How.** Select the worst case ducts (or pick "All systems"). Click
`Hvac_NcPredict`. STING:

1. Identifies the upstream-most member as fan source — either reads
   the fan's Lw spectrum from `STING_FAN_SPECTRA.json` if its family
   name matches a registered fan, or synthesises Lw from Q + ΔP
2. Walks every duct, applies attenuation per ASHRAE A48 (straight,
   lined, elbow, tee, end-reflection)
3. Adds Bullock regenerated noise per fitting velocity
4. At the terminal, picks up the silencer IL from
   `STING_SILENCER_DATA.json` if matched
5. Reports predicted NC at the diffuser + per-element attenuation breakdown

Office target NC 35; patient room NC 30; OR NC 35; classroom NC 30;
recording studio NC 20. If your prediction exceeds the target, the
fix is usually: lower the duct velocity at the terminal end (oversize
the final two runs) or add a silencer.

### 5.17 Cross-talk audit (CALCS → Cross-talk audit)

**Why.** Speech in one room transmits through the shared duct system
to the next room. Healthcare and education buildings specifically
target 30 dB minimum privacy.

**How.** Click `Hvac_CrossTalkAudit`. STING walks every air terminal,
finds the upstream-most common element between every pair of rooms,
accumulates octave-band attenuation across the path, treats the
talker's Lp + 11 dB as Lw at the receiver terminal, applies the
receiver room's direct + reverberant model
(`Lp = Lw + 10·log10(Q/4πr² + 4/R)`), rates the result against NC
curves. Flags pairs with NC > 35 OR 1 kHz attenuation < 30 dB.
Full octave breakdown to CSV at
`_BIM_COORD/acoustic/crosstalk_<ts>.csv`.

### 5.18 Audit pressure class (CALCS → Pressure-class audit)

**Why.** DW/144 Class A ducts are gauged for ≤ 500 Pa. If your design
SP runs at 800 Pa but the contractor builds to Class A, the duct
leaks like a colander.

**How.** Click `Hvac_PressureClassAudit`. STING reads every duct's
flow, role, velocity and air density (from the climate site —
location-aware, replaces the old hardcoded 1.20 kg/m³), estimates
friction including adjacent-fitting losses (half-credit to avoid
double-counting), reports pressure class violations AND
role-velocity violations as separate failure modes.

### 5.19 Run the full design pass (CALCS → Full design pass)

`Hvac_FullDesignPass` chains: block load → propagate → auto-size →
balance → NC predict → pressure-class audit → stale-size detection.
Use it after any meaningful change — moving walls, adding spaces,
swapping equipment. Each step status appears in the result panel.

### 5.20 Generate the commissioning checklist (RPRT → Cx checklist)

**Why.** The commissioning agent needs a witness sheet per equipment
class — pre-install / pre-startup / startup / functional / handover
tasks per ASHRAE Guideline 0 + CIBSE TM39.

**How.** Click `Hvac_GenerateCxChecklist`. STING walks every
Mechanical Equipment, classifies it (AHU / Chiller / Boiler / VRF
/ Pump / FCU / VAV / CoolingTower / HeatPump / Fan / HeatExchanger
/ Damper / Generic), loads tasks from `STING_CX_TASKS.json` per
class, and emits a per-equipment CSV under `<project>/_BIM_COORD/cx/`.

Project teams can override tasks via
`<project>/_BIM_COORD/cx/cx_tasks_override.json` with either REPLACE
semantics (default — bare array) or APPEND semantics
(`{ "_merge": "append", "tasks": [...] }`).

### 5.21 Produce drawings

Main STING panel → DOCS → Drawing Type Manager / Sheet Manager. HVAC
drawing types:

- `mep-hvac-duct-A1-1to100` — HVAC ductwork layout
- `mep-plantroom-A1-1to50` — plantroom plan
- `mep-coord-A1-1to50` — services coordination
- `mep-plan-A1-1to100` — generic MEP plan

See §17 for full drawing setup.

### 5.22 Issue

Main panel → BIM → Issue Deliverable (template engine v1.1). A cover
letter, transmittal note and revision history are rendered automatically.

---

## 6. Climate site and design conditions

### What it is

Every load calculation needs a *site climate*. For each city you need:

| Field             | Used for                                  |
|-------------------|-------------------------------------------|
| Latitude          | Solar declination + altitude              |
| Longitude         | Solar azimuth + time zone                 |
| Elevation         | Air density (ρ corrected via ISA)         |
| UTC offset hours  | Local clock → solar time                  |
| Observes DST      | Local clock → solar time in summer        |
| Cooling 0.4 % DB  | 0.4 %-of-year dry-bulb exceeded (peak)    |
| Cooling 0.4 % MCWB| Mean coincident wet-bulb at the DB peak   |
| Cooling 1 % / 2 % | Alternative percentiles (LEED, etc.)      |
| Heating 99.6 % DB | Coldest 0.4 %-of-year (peak heating)      |
| Heating 99 % / 98 %| Alternative percentiles                  |
| HDD18 / CDD10     | Heating / cooling degree-days (annual)    |
| Design wind m/s   | Stack-effect + wind infiltration          |

### Why it matters

Use Singapore's design day for a London building and you'll design AHUs
that condense water and cooling coils that freeze. The climate site
determines the entire peak.

### How STING does it

STING ships `STING_CLIMATE_DATA.json` with **41 cities** spanning UK
(London, Manchester, Edinburgh, Belfast), EU (Paris, Berlin, Madrid,
Stockholm, Copenhagen, Amsterdam, Vienna, Rome, Athens, Helsinki,
Oslo, Warsaw, Prague, Budapest), US (NYC, LA, Chicago, Miami, Phoenix,
Seattle, Houston, Denver), Asia (Tokyo, Singapore, Mumbai, Beijing,
Shanghai, Dubai, Bangkok, Hong Kong, Seoul), AUS (Sydney, Melbourne,
Perth, Brisbane) and Africa (Cairo, Lagos, Nairobi, Johannesburg).

**Climate resolution priority** (`ClimateRegistry.ActiveSite(doc)`):

1. `PRJ_CLIMATE_SITE_ID` on Project Information (e.g. `london`,
   `tokyo`) — direct lookup
2. Fuzzy match of `ProjectInformation.Address` against site labels
   (token-level intersection, ≥ 4-char words, skips noise words)
3. First site in the project override file at
   `<project>/_BIM_COORD/climate_data.json`
4. Hard fallback to `london`

### Adding a custom site

Drop a file at `<project>/_BIM_COORD/climate_data.json`:

```json
{
  "sites": [
    {
      "id": "kampala",
      "label": "Kampala — Entebbe Airport",
      "latitudeDeg": 0.05,
      "longitudeDeg": 32.45,
      "elevationM": 1155,
      "utcOffsetHours": 3,
      "observesDstInSummer": false,
      "cooling04DbC": 30.5,
      "cooling04MCWBC": 21.4,
      "heating996DbC": 12.8,
      "designWindMs": 3.5
    }
  ]
}
```

Click `Hvac_ClimateReload` to flush the cache, then `Hvac_ClimateInspect`
to verify.

### Multi-percentile

For projects that want a different design-day percentile (e.g. LEED
projects often want 1.0 %, hospitals 0.4 %, residential 2.0 %),
`ClimateSite` exposes `CoolingDbCFor(percentile)` and
`HeatingDbCFor(percentile)`. Set the project percentile via
`PRJ_DESIGN_PERCENTILE_PCT` (default 0.4).

### Air density

`ClimateRegistry.AirDensityKgM3(site)` returns the elevation-corrected
density at the cooling design DB:

```
ρ = ρ_0 × (1 - 0.0065·z/T_sea)^(g·M/(R·0.0065))
```

per the standard atmosphere model. London at 25 m: 1.20 kg/m³. Mexico
City at 2240 m: 0.96 kg/m³. The 20 % swing is *exactly* what your
high-altitude HVAC must compensate for — more CFM for the same kW.

The pressure-class audit now reads this automatically; you don't have
to override the header air-density combo unless you want a specific
value.

---

## 7. Block load calculation

### What it is

A *block load* is the peak heating or cooling demand of a SYSTEM at
the hour of day when all its served zones' loads add up to the
highest total. It is **not** the sum of each zone's peak.

Example: A west-facing office peaks at 4 PM. An east-facing office
peaks at 9 AM. Σ Zone peaks = 4 kW + 3 kW = 7 kW. But the shared
AHU never sees 7 kW — at 9 AM the east room is at 3 kW but the west
is at 1.5 kW (block = 4.5 kW); at 4 PM the west is at 4 kW but the
east is at 1.2 kW (block = 5.2 kW). The SYSTEM block load is 5.2 kW
— a 26 % diversity saving on equipment sizing.

### Why it matters

Sum-of-peaks oversizes plant by 15–30 %, which:
- Costs the client 15–30 % more in chiller / boiler / AHU capital
- Drops part-load efficiency (chillers and boilers are most efficient
  at 60–80 % load — oversize them and they cycle)
- Increases refrigerant charge → bigger leak-detection requirements
- Increases pump / fan power (fixed losses from oversized impellers)

Conversely, sum-of-AVERAGES undersizes plant and the building runs
hot. You need a real hour-by-hour calc to get it right.

### How STING does it

`BlockLoadEngine.Run(zones, climate, strategy, options)` runs a 24-h
simulation for each space, then peak-picks at the SYSTEM level for
each system's served zones.

**Per-hour gain components** (each multiplied by occupancy / lighting
/ equipment schedule from the space-type profile):

| Component                  | Source                                              |
|----------------------------|-----------------------------------------------------|
| Conduction (walls / roof)  | U × A × ΔT, lagged per RTS                          |
| Solar (windows)            | SHGC × A × cos(θ) × E_DN + E_diffuse, lagged per RTS |
| Occupants                  | N × (sensible + latent W/person)                    |
| Lighting                   | LPD × A × schedule                                   |
| Equipment                  | EPD × A × schedule                                   |
| Ventilation (sensible)     | ρ·cp·V·(T_OA - T_room)                              |
| Ventilation (latent)       | ρ·hfg·V·(w_OA - w_room)                             |
| Infiltration               | Same as ventilation but at uncontrolled flow         |

Sensible and latent are accumulated separately. Each component is
convolved with the active RTS series before summing.

### Radiant Time Series (RTS)

Heavy mass (concrete / masonry buildings) absorbs heat during the
day and re-emits it slowly. A 1 kW solar gain at 11 AM might land
on the air-loop as 0.6 kW at 11 AM, 0.3 kW at noon, 0.1 kW at 1 PM,
etc. — the *time series*.

ASHRAE Handbook of Fundamentals 2021 Ch.19 ships RTF (Radiant Time
Factor) tables for **Light / Medium / Heavy** construction. STING
ships these tables plus Tier-3 *per-construction-layer* CTF coefficients
in `STING_CTF_COEFFICIENTS.json` for 5 construction types (Light
stud, Medium masonry cavity, Heavy concrete frame, Very-heavy
composite, Glass DGU).

**Selecting the right class:**

| Class      | Use for                                                       |
|------------|---------------------------------------------------------------|
| Reactive   | No lag — use only for hydronic loops / small spaces             |
| Light      | Steel stud + plasterboard, < 200 kg/m² mass                    |
| Medium     | Masonry cavity, 200–400 kg/m² (typical UK office)              |
| Heavy      | Solid concrete frame, > 400 kg/m² (hospitals, prisons)         |
| Per-construction CTF | When every wall has known construction layers     |

Set `PRJ_RTS_CLASS_TXT` on Project Information. When envelope segments
carry `EnvelopeSegment.ThermalMassKJperM2K`, STING derives a *zone-
specific* RTF by area-weighting the corresponding CTF Y-series and
renormalising. Otherwise it falls back to the project-wide class.

Heavy-mass buildings peak ~15–25 % lower with RTS enabled. Use
`Hvac_RtsBenchmark` to regression-test against 4 worked ASHRAE
examples.

### Per-orientation conduction lag

Heat that lands on a south wall at noon re-emits to the air over the
next 4–6 hours. Heat on a west wall lands at 3 PM. East walls peak
at 9 AM. The peak hour of a room facing two cardinal directions is
the sum of two phase-shifted curves — you can't get it right by
summing across orientations before convolving.

STING bins envelope gains into **8 cardinal orientations** (N, NE,
E, SE, S, SW, W, NW) and convolves each bin separately before
aggregation. Tightens the RTS calibration on west-glass-heavy zones
by ~5 %.

### Solar geometry

`ClearSkyDirectNormalWm2(dayOfYear, lat, hourSolar)` returns the
direct-normal solar irradiance per ASHRAE Handbook of Fundamentals
Ch.14 Table 7 (seasonally interpolated A and B coefficients).

`IncidenceFactor(orientationDeg, hourSolar, lat, dayOfYear)` computes
cos(θ) where θ is the angle between the sun ray and the surface
normal — uses true ASHRAE solar-angle formulae (declination from DOY,
hour angle from solar hour, altitude from sin·sin + cos·cos·cos,
azimuth measured from south).

### DST + timezone

`localToSolarShiftH = -dstShift + (lon - 15·utcOffset)/15`

Local clock 1 PM in summertime London (UTC+1 DST) is solar noon at
0°W. In Tokyo (UTC+9, no DST) at 139°E, local 1 PM is solar 12:44 PM.
Both convert to the correct sun position before computing solar gain.

### Stack-effect + wind infiltration

In a tall, leaky, cold building, warm air rises inside and exits
through cracks at the top, drawing cold air in at the bottom. This
"stack effect" can double infiltration vs the design value.

Per CIBSE Guide A §4.6:

```
ΔP_stack = ρg·h·(Tin - Tout)/Tin
ΔP_wind  = 0.5·Cp·ρ·v²
Q_inf    = Q4Pa·A·(√(ΔPs² + ΔPw²)/4)^0.65
```

Set `LoadZone.Q4PaM3PerHperM2` (the q₄ leakage per Passivhaus / Part
L testing — typically 5 m³/h/m² for Part L 2021, 0.6 for Passivhaus)
and `LoadZone.InfiltrationEnvelopeAreaM2` for STING to use this model;
otherwise it falls back to the space-type profile's `InfiltrationAch`.

### Demand-Controlled Ventilation (DCV)

Per ASHRAE 62.1 §6.2.7, in spaces with variable occupancy you can
reduce OA proportionally to actual CO₂ load. The per-person component
`R_p × N(t) × OccupancySchedule(t)` scales with occupancy; the
per-area `R_a × A` stays constant.

`DcvVentilationCalc.HourlyOa(zone)` computes the 24-h OA profile.
`ZoneLoadResult` carries `HourlyOaLs[24]`, `AverageOaLs` (vs design
max), and `DcvSavingsPct`. Office buildings see typical 30–50 %
savings on outdoor-air fan energy.

### Per-space stamps after block load

Every Space gets:

| Param                     | Meaning                                  |
|---------------------------|------------------------------------------|
| `HVC_PEAK_SENS_W`         | Peak sensible cooling W                  |
| `HVC_PEAK_LAT_W`          | Peak latent cooling W                    |
| `HVC_PEAK_HOUR`           | Hour of day at peak (0–23)               |
| `HVC_OA_LS`               | Design OA L/s                            |
| `HVC_LOAD_SOURCE_TXT`     | Provenance (e.g. "BlockLoad:london:Office:Medium") |
| `HVC_LOAD_STALE_BOOL`     | 1 when envelope geometry changed since calc |
| `HVC_LOAD_STALE_REASON_TXT`| What envelope element changed           |

### Scenario: 500 m² open-plan office in London

Setup:

- Climate site `london`, design DB 28.1 °C (0.4 %), MCWB 19.5 °C, lat 51.5°
- Construction profile `PartL2021`: walls U=0.18, roof U=0.13, windows U=1.4 SHGC 0.4
- Space type `Office`: 10 m²/person occupancy, 8 W/person sensible, 60 W/person latent, lighting 8 W/m², equipment 12 W/m², OA 10 L/s/person
- 500 m² floor, 30 % WWR (window-to-wall ratio), south + west exposure
- Medium RTS class (concrete frame)

Block load output:
- Peak sensible: ~28 kW at 4 PM (RTS lag pushes peak from solar noon
  to mid-afternoon; west glass peaks later than south)
- Peak latent: ~6 kW (50 occupants × 60 W = 3 kW latent gain + 3 kW
  from OA dehumidification)
- Design OA: 500 L/s (50 occupants × 10 L/s/person)
- With DCV: average OA ~280 L/s — fan energy saving ~45 %

For a single-AHU + VAV system, equipment sizing:
- AHU coil: 28 kW cooling + 5 kW reheat + 25 kW heating
- AHU supply fan: 1700 L/s (sensible-driven at 11 K ΔT)
- AHU OA fan: 500 L/s peak / 280 L/s typical
- 5 VAV zones at 200–400 L/s each

---

## 8. Space-type load profiles and DCV

### What it is

A *load profile* is a JSON record per space type covering:

- Occupant density (m² per person)
- Per-occupant sensible + latent gains (W)
- Lighting power density (W/m²)
- Equipment power density (W/m²)
- OA rate per person (L/s) + per area (L/s/m²)
- Setpoints (cooling / heating)
- Infiltration (ach)
- 24-h occupancy / lighting / equipment schedules

### Why it matters

Office defaults (12 W/m² equipment, 8 W/m² lighting, 7 L/s/p OA) are
right for an office. Apply them to a patient room (where the right
numbers are 3 W/m² equipment, 7 W/m² lighting, 12.5 L/s/p OA AND a
24/7 schedule) and you'll under-size the AHU and over-design the
lighting energy.

### How STING does it

`STING_LOAD_PROFILES.json` ships 12 profiles. Set
`HVC_SPACE_TYPE_TXT` on each Space to one of:

| Profile id     | Occ density | Equip W/m² | OA L/s/p | OA L/s/m² | Setpoint °C |
|----------------|-------------|------------|----------|-----------|-------------|
| Office         | 10 m²/p     | 12         | 7        | 0.3       | 24 / 22     |
| MeetingRoom    | 1.5 m²/p    | 8          | 7.5      | 0.3       | 24 / 22     |
| Classroom      | 2 m²/p      | 6          | 5        | 0.6       | 24 / 22     |
| PatientRoom    | 10 m²/p     | 3          | 7        | 0.5       | 24 / 22     |
| OperatingRoom  | 10 m²/p     | 30         | 30       | 4.0       | 19 / 19     |
| Retail         | 8 m²/p      | 12         | 5        | 0.6       | 25 / 21     |
| Restaurant     | 2 m²/p      | 8          | 7.5      | 1.0       | 24 / 22     |
| Kitchen        | 10 m²/p     | 30         | —        | 5.0       | 26 / 18     |
| Lab            | 10 m²/p     | 40         | 7.5      | 1.0       | 22 / 22     |
| Warehouse      | 100 m²/p    | 5          | —        | 0.6       | 28 / 15     |
| Plantroom      | 50 m²/p     | 50         | —        | 1.5       | 35 / 12     |
| Corridor       | 50 m²/p     | 5          | —        | 0.3       | 24 / 21     |

Numbers above are sample values; the actual ones live in the JSON
and follow ASHRAE 90.1-2019 Table 9.6.1 (lighting), 62.1-2019 Table
6.2.2.1 (ventilation) and CIBSE Guide A 2015. Add custom profiles via
`<project>/_BIM_COORD/load_profiles.json` (additive by id).

### How DCV works in STING

Set the space's `HVC_DCV_ENABLED_BOOL = 1`. STING reads the profile's
hourly occupancy schedule, scales `R_p × N(t)` proportionally, holds
`R_a × A` constant, and computes a 24-h OA profile. The block load
engine integrates against actual occupancy, so design ventilation
heat load reflects average use, not peak.

The block-load result panel reports building-aggregate DCV savings
(Σ avg OA / Σ design max OA) and per-zone savings on the top-10 list.

### Scenario: 50-bed hospital ward in Manchester

Setup:

- Climate `manchester`, cooling DB 24.3 °C, heating DB -3.5 °C
- Construction `PartL2021`
- 25 patient rooms (HVC_SPACE_TYPE_TXT = `PatientRoom`) at 20 m² each
- Each room: 1 patient + 1 staff occupancy, 3 W/m² equipment, 7 W/m²
  lighting, 12.5 L/s/p OA, 24/7 schedule, +5 Pa to corridor
- Corridor (`Corridor` profile) at 0 Pa baseline

Block load output:
- Per-room peak sensible: ~1.2 kW (envelope-dominated, west-facing)
- Per-room peak latent: ~0.2 kW (low occupancy)
- Per-room OA: 25 L/s (2 occupants × 12.5 L/s/p) — 24/7
- Building-block peak: ~22 kW (diversity ~0.85 — high coincidence
  because patient rooms have constant gain)

Pressure cascade requires net 25 L/s exhaust from each patient room
(supply 25 L/s OA, no recirculation per HTM 03-01); supply 30 L/s,
exhaust 25 L/s, transfer 5 L/s to corridor → corridor 0 Pa, room
+5 Pa. STING's plumbing/healthcare pack (separate, see HEALTHCARE_PACK)
handles pressure-cascade validation.

---

## 9. Equipment placement

### What it is

You need to physically locate every AHU, FCU, VAV, chiller, boiler,
pump, fan and damper in the 3D model. Until you do, ductwork has no
endpoints to connect to.

### Why it matters

- Plantroom layout drives the architecture (slab depths, access
  routes, ventilation louvres, drainage)
- Equipment location drives ductwork length, which drives fan power,
  which drives running cost
- AHU intake/exhaust separation drives outside-air quality
- FCU / VRF cassette placement drives ceiling void coordination

### How to do it in STING

Two scales:

**Per-piece** (main panel → MODEL → MEP → Place Fixture). Pick the
family (`STING - AHU - Horizontal`, `STING - FCU - Ceiling
Concealed`, `STING - Chiller Air-Cooled`, etc.), click the location.

**Per-zone** (HVAC panel → EQPT → Place HVAC equipment) — bulk-place
FCUs across all selected spaces; reads the active space-type profile,
sizes each FCU to the space load, picks ceiling-cassette / wall-mount
/ ducted per `HVC_IDU_MOUNTING_TXT`.

After placing, tag with main panel CREATE → Tag & Combine. Set:

- `Capacity (kW)` — matches the served zone's peak load
- `Design Flow (L/s)` — for air equipment
- `External Static Pressure (Pa)` — for fans (fan power = Q × ΔP / η)
- `HVC_PROD_REF_TXT` — `brand:productCode` for manufacturer Cs and
  acoustic lookup (e.g. `daikin:VRV-5-FXSQ-50P`, `trox:RSK-100-200`)

The EQPT tab populates automatically on the next `Hvac_RefreshGrids`.

### Manufacturer fitting Cs and valve Kvs

`STING_MEP_SIZING_RULES.json` ships:

- `duct.manufacturerFittings` — Lindab / Trox / Halton catalogue C
  values keyed by product code
- `pipe.valveCv` — Belimo / Siemens / Danfoss Kvs values (m³/h at 1
  bar)

When STING computes pressure drop across a fitting it reads
`HVC_PROD_REF_TXT` on the fitting, looks up the manufacturer's
specific C, and uses that instead of the generic SMACNA C value.
This is the difference between a 5 % accurate pressure-drop calc
and a 25 % accurate one.

`MepFittingLossReportCommand` splits results into "Manufacturer-
specified" vs "Generic fallback" so you can see at a glance how much
of the model is on registry-backed values vs falling back to SMACNA
defaults.

---

## 10. IDU selection

### What it is

VRF, split and ducted FCU systems use room-level **Indoor Units**
(IDUs). Vendors ship catalogues of 10–30 sizes per series. Picking
the right one per space — by capacity, mounting, NC limit, throw —
is repetitive and error-prone if done by hand.

### Why it matters

- Pick an IDU too small → room overheats / undercools
- Pick too big → short-cycles + poor latent control + uneven coverage
- Wrong mounting → ceiling void won't accommodate / wall obstacle
- NC too high → uncomfortable acoustic environment
- Mismatched IDU sizes across a zone → balancing nightmares

### How STING does it

`STING_IDU_CATALOGUE.json` ships 11 sample IDU records across:
- Daikin VRV-5 FXSQ (ducted), FXFQ (ceiling cassette), FXAQ (wall)
- Mitsubishi City Multi Y PEFY (ducted), PLFY (cassette)
- Toshiba SHRMe MMD (ducted), MMU (cassette)

Each carries: nominal cooling kW, nominal heating kW, sensible
fraction, mounting type, min/max flow L/s, NC at 3 m, weight, supply
voltage.

**Per-space mounting override:** `HVC_IDU_MOUNTING_TXT` —
`Ducted`, `CeilingCassette`, `WallMounted`.

**Project default:** `PRJ_REFRIG_IDU_MOUNTING_TXT`.

**Select:** `Hvac_SelectIdus`. For each Space with a peak load,
STING:

1. Reads peak load + latent load + mounting preference + NC target
   from the space profile
2. Filters catalogue to records matching mounting + within ±20 %
   of duty
3. Picks the smallest record satisfying capacity + min flow + max NC
4. Stamps `HVC_SELECTED_IDU_ID_TXT` and `HVC_SELECTED_IDU_LABEL_TXT`
   on the Space

Result panel reports per-mounting summary: "12 spaces → FXSQ-32P
(3.6 kW), 8 spaces → FXSQ-50P (5.6 kW), 2 spaces → FXSQ-71P
(8.0 kW)" so you know your stocking SKU count.

### Vendor scoring

For VRF, each space's IDU selection should match the project's chosen
vendor series (`PRJ_REFRIG_VENDOR_SERIES_TXT`). If a space requires
20 kW cooling but the largest VRV-5 ducted IDU is 16 kW, STING flags
the space "exceeds maximum IDU capacity" and suggests splitting the
space into multiple zones.

---

## 11. Duct design

### What it is

Sizing ductwork means picking the right cross-section (round / oval /
rectangular) and size (mm) for every segment so that:

1. Air velocity stays within the role's target (lower velocity →
   quieter + less pressure drop, but bigger duct + more material cost)
2. Pressure drop across the system stays within the fan's external
   static pressure
3. Pressure class (DW/144 A / B / C / D) matches the system static
   pressure
4. Standard sizes are used (you can't order a 137 mm duct — only
   125 or 150 from the regional standard set)
5. Aspect ratio (width:depth) stays reasonable (< 4:1 for service-
   zone ducts, lower is better for low fan power)

### Why it matters

- Undersized duct → high velocity → high noise + high pressure drop →
  underpowered fan can't deliver design flow
- Oversized duct → big ceiling void requirement → architectural fight
- Wrong pressure class → site contractor builds to Class A but design
  needs Class C → leaky duct, fails commissioning
- Wrong aspect ratio → high friction + high fan power + complains
  about "rumble"
- Non-standard sizes → custom fabrication costs 3× standard

### Four sizing strategies

| Strategy           | When to use                                    | Result                              |
|--------------------|------------------------------------------------|-------------------------------------|
| Velocity           | Quick first pass; small systems                | Constant velocity per role            |
| Equal friction     | Most common — balance simplicity vs efficiency | Constant Pa/m per branch              |
| Static regain      | High-velocity long runs (long-throw concert)   | Pressure recovery between fittings    |
| Constant pressure  | When all terminals must see same dP            | Sized for equal balance valve setting |

Set the strategy in the header context strip. The CALCS tab shows
per-role targets — velocity m/s, friction Pa/m, aspect max. Override
the defaults per role:

| Role         | Default velocity (m/s) | Default Pa/m | Notes                          |
|--------------|------------------------|--------------|--------------------------------|
| Main         | 8.0                    | 1.5          | Risers, plantroom distribution |
| Branch       | 6.0                    | 1.2          | Off the main                   |
| Runout       | 4.0                    | 1.0          | Last 2–3 m to terminal         |
| Outdoor air  | 5.0                    | 1.0          | Quieter (intake fans)          |
| Exhaust      | 8.0                    | 1.5          | OK to be noisy                 |
| Kitchen      | 10.0                   | 2.0          | Hood — high velocity OK        |
| Smoke        | 15.0                   | 3.0          | Fire — short duration          |

### Pressure classes (DW/144)

| Class | Max SP (Pa)  | Use for                              |
|-------|--------------|--------------------------------------|
| A     | ≤ 500        | Low-pressure supply / return / exhaust|
| B     | ≤ 1000       | Medium-pressure VAV / smoke          |
| C     | ≤ 2500       | High-pressure long-throw VAV / spec  |
| D     | ≤ 7500       | Industrial / aggressive exhaust      |

Set the class in the header. The pressure-class audit
(`Hvac_PressureClassAudit`) flags violations.

### Regional standard sizes

`MepSizingRegistry` ships per-region `standardSizesMm`:

| Region | Round sizes (mm)                                           |
|--------|------------------------------------------------------------|
| UK_SI  | 100, 125, 150, 200, 250, 300, 400, 500, 600, 800, 1000, 1200, 1500, 1800, 2000 |
| US_IP  | 100, 125, 150, 200, 250, 300, 350, 400, 500, 600, 750, 900, 1050, 1200, 1500 |
| EU_SI  | EN 1506 series                                              |
| DE_SI  | DIN 24190 series                                             |
| SE_SI  | SS-EN 1506 series                                            |

Pick the region in the header. STING snaps to the nearest standard
size.

### Gauge breakpoints

DW/144 gauges duct walls by size + pressure class. STING reads
`duct.gaugeBreakpoints` from the registry — e.g. for Class A:

| Width (mm) | Thickness (mm) | Seam        |
|------------|----------------|-------------|
| ≤ 300      | 0.6            | Pittsburgh  |
| 301–600    | 0.7            | Pittsburgh  |
| 601–900    | 0.8            | Pittsburgh  |
| 901–1200   | 1.0            | Pittsburgh  |
| 1201–1500  | 1.2            | Snap-lock TDC |
| > 1500     | 1.5            | Flanged    |

Drives the FAB tab + cut-list export.

### Insulation

External insulation defaults to 25 mm fibreglass + foil for supply,
50 mm for OA, 0 mm for exhaust + return. Adjust per project at
`<project>/_BIM_COORD/duct_insulation.json`.

### How to size in STING

1. Place ductwork in Revit (manually, or via `AutoDropCommand`)
2. Run `Hvac_PropagateLoads` so each duct carries `HVC_FLOW_LS`
3. Set the active strategy + class + region in the header
4. Click **CALCS → Auto-size** (`Mep_AutoSizeDuct`)
5. STING walks every duct, computes target size from `flow / (velocity
   × A)`, snaps to nearest standard size, writes the size
6. Run `Hvac_PressureClassAudit` to flag violations

### Stale-size detection

If a designer changes a space load (or you swap an IDU) without re-
running auto-size, ducts run at the old size. `Hvac_DetectStaleSizes`
flags every duct whose computed size differs from current size by
≥ 1 standard increment.

If envelope geometry changes (someone moves a wall), the space's
load is now wrong. `HvacEnvelopeStaleUpdater` (IUpdater) fires
automatically on wall / window / door / roof / floor geometry
changes, resolves the affected Space, and stamps
`HVC_LOAD_STALE_BOOL = 1` + `HVC_LOAD_STALE_REASON_TXT`. Block-load
auto-clears the flag when it next stamps a space. Toggle the updater
on/off via `Hvac_EnvelopeStaleToggle`; clear flags via
`Hvac_EnvelopeStaleClear`.

---

## 12. Pipe design

### What it is

Hydronic systems (chilled water, hot water, low-temperature hot water,
condensate) need pipe sizing for:

1. Velocity (low enough to avoid erosion + noise)
2. Pressure drop (low enough that the pump can push it)
3. Bore (standard nominal pipe size)
4. Balance (every branch gets the design flow)

### Per-service limits

`MepSizingRegistry` ships `pipe.services`:

| Service     | Max velocity (m/s) | Max Pa/m  | Notes                       |
|-------------|--------------------|-----------|----------------------------- |
| chw         | 1.5                | 250       | Chilled water 6/12 °C       |
| hws         | 1.5                | 250       | Heating 82/71 °C            |
| lhw         | 1.2                | 200       | Low-temp 55/45 °C           |
| dcw         | 2.0                | 300       | Domestic cold               |
| dhw         | 1.5                | 250       | Domestic hot                |
| dhw_circ    | 1.0                | 200       | DHW recirc                  |
| condensate  | 0.6                | 100       | Gravity-flow                |
| refrig_gas  | 15                 | (oil-return floor) | Gas R410A           |
| refrig_liq  | 1.2                | (subcool reserve)  | Liquid R410A        |
| steam       | 25                 | (separate calc)    |                             |
| natural_gas | 6                  | 50         |                              |

### Hardy Cross balancing

A simple pumped loop with one resistance loop converges by hand. A
real building's hydronic system has tens of loops. Hardy Cross
iteratively re-distributes flows.

**STING's solver** (`HardyCrossSolver`):

1. `InitializeFromDemand(pipes, demandLpsByNode)` — seed flows by
   splitting per-node demand equally across incident pipes (or
   `InitializeUniform` for single-source tree)
2. Per iteration: compute Δflow per loop = -ΣhL / Σ(2|hL|/Q),
   apply with damping factor (default 0.5)
3. Stop when residual < tolerance (default 50 Pa) or
   max iterations (default 100)
4. If a `PumpCurve` is supplied (3-point quadratic from shut-off /
   BEP / run-out), bisect system curve against pump curve to find
   the **operating point** — the actual Q + H the pump delivers on
   this system

Click `HardyCrossCommand`. Result panel shows per-loop residuals,
operating point, and any out-of-tolerance branches.

### PICV — Pressure-Independent Control Valves

A PICV holds branch flow constant regardless of pressure drop across
it, within an "authority window" (e.g. 30–250 kPa). Outside the
window, the constant-Q behaviour breaks down.

`STING_MEP_SIZING_RULES.json` `pipe.picvCurves` ships:

| Brand   | Series         | Min ΔP (kPa) | Max ΔP (kPa) | Sizes |
|---------|----------------|--------------|--------------|-------|
| Belimo  | EPIV / PICV    | 30           | 400          | DN15-DN50 |
| Danfoss | AB-QM          | 16           | 600          | DN10-DN150 |
| IFC     | ePIV           | 25           | 800          | DN15-DN100 |

When you tag a valve with `HVC_PROD_REF_TXT = "belimo:EPIV-DN25"`,
the balance check reads the PICV's authority window and flags
branches outside it (typically: design ΔP < 30 kPa = no constant-Q,
must use balance valve instead).

### Pump curves

Manufacturers provide pump curves as 3+ points: shut-off (Q=0, H=H₀),
best efficiency (Q=Qbep, H=Hbep), run-out (Q=Qro, H=Hro). STING's
`PumpCurve.FromQuadraticThreePoints` fits `H(Q) = a₀ + a₁Q + a₂Q²`
through them.

`HardyCrossSolver.OperatingPoint(seriesPath, pump, …)` bisects the
system curve against the pump curve. Result: the actual Q + H the
pump delivers — usually within ±15 % of the pump's stated duty
point. If your operating point sits at the run-out, the pump runs
hot and cavitates; if at shut-off, it overheats. The Hardy Cross
result panel surfaces both warnings.

### How to size pipes in STING

1. Place pipes in Revit
2. Run `Hvac_PropagateLoads` (or stamp `HVC_FLOW_LS` manually)
3. **CALCS → Auto-size pipe** (`MepAutoSizePipeCommand`)
4. STING walks every pipe, looks up the service from MEP system
   name (or `HVC_PIPE_SERVICE_TXT` per-pipe), computes required
   bore from `flow / (velocity × A)` and `Pa/m × L < remaining
   pump head`, snaps to nearest standard bore from
   `standardBoreMm`
5. **CALCS → Balance** (`HardyCrossCommand`) to verify

---

## 13. Refrigerant pipe sizing

### What it is

VRF, split and packaged DX systems use refrigerant lines — gas, liquid
and (in some cases) suction. Sizing must satisfy:

1. ΔP budget (sum of all leg ΔPs < vendor's stated max system ΔP)
2. Oil-return velocity floor (gas line ≥ ~3 m/s, liquid ≥ ~2 m/s —
   so refrigerant oil entrained in the gas stream actually returns
   to the compressor)
3. Subcooling reserve on liquid lines (line ΔP × dT_sat/dP < reserve
   — else flash gas forms upstream of the expansion valve, killing
   capacity)
4. Vendor pipe-length limits (total run, first-branch-to-far-IDU,
   ODU↔IDU vertical, IDU↔IDU vertical — each refrigerant series has
   different limits)
5. Static head on liquid leg (signed — positive lift adds to budget,
   negative lift CREDITS the budget back)

### Why it matters

- Undersized → high ΔP → low compressor suction pressure → low
  capacity + high power
- Oversized → low velocity → no oil return → compressor seizes after
  6 months
- Exceeds vendor pipe-length limit → manufacturer voids warranty
- Insufficient subcooling reserve → flash gas → expansion valve
  hunts → IDU surges + low capacity
- Wrong charge calc → leak detection sensors trigger at the wrong
  threshold

### How STING does it

`RefrigerantPipeSolver` ships 4 refrigerants in
`RefrigerantProperties`:

| Refrig | Sat properties              | Per-system ΔP budget (kPa) | Per-leg budget (kPa) |
|--------|------------------------------|----------------------------|-------------------------|
| R410A  | 7.3 bar liquid / 22 bar gas | 50                         | gas 30 / liq 50 / suc 50 |
| R32    | 7.5 / 27                    | 50                         | gas 25 / liq 50 / suc 50 |
| R134a  | 4.0 / 14                    | 50                         | gas 20 / liq 40 / suc 30 |
| CO₂    | 30 / 50 (transcritical)     | 80                         | gas 50 / liq 100 / suc 80 |

The solver:

1. Sweeps the ACR copper-pipe size list (1/4"... 1-5/8" OD per
   ASTM B280)
2. For each size, computes Darcy-Weisbach friction with Blasius f =
   0.316 / Re^0.25 (smooth copper)
3. Applies static head (signed lift × ρ × g) — negative lift
   *expands* the ΔP budget
4. Applies suction-line two-phase multiplier (flat 10 %; not
   Lockhart-Martinelli)
5. Reports ΔP per size, vendor pipe-length compliance, oil-return
   compliance, subcooling/flash-gas margin
6. Picks the smallest OD that satisfies all four

### Vendor pipe-length limits

`STING_REFRIG_VENDOR_LIMITS.json` ships 7 vendor series:

| Vendor / Series       | Total run (m) | First-branch-to-far-IDU (actual / equiv) (m) | ODU↔IDU vert (m) | IDU↔IDU vert (m) |
|-----------------------|---------------|----------------------------------------------|------------------|------------------|
| Daikin VRV IV-S       | 300           | 40 / 90                                     | 50               | 30               |
| Daikin VRV 5          | 1000          | 90 / 165                                    | 110              | 40               |
| Daikin VRV IV-H       | 300           | 40 / 90                                     | 50               | 30               |
| Mitsubishi City Multi Y | 1000        | 165 / 220                                   | 90               | 40               |
| Mitsubishi City Multi R2| 510         | 110 / 160                                   | 50               | 15               |
| Toshiba SHRMe         | 235           | 40 / 90                                     | 50               | 30               |
| Generic Split         | 30            | —                                            | 15               | —                |

Set `PRJ_REFRIG_VENDOR_SERIES_TXT` on Project Info to one of the ids
(`daikin-vrv-5`, `mitsubishi-cm-y`, etc.). Set
`PRJ_REFRIG_FLUID_TXT` to the refrigerant (`R410A`, `R32`).

### REFNET branch sizing

VRF systems branch off the main with proprietary **REFNET joints**
(Daikin KHRP, Mitsubishi CMY, Toshiba RBM-BY). The joint size depends
on the downstream connected capacity.

`Hvac_RefnetSize` walks the refrigerant tree depth-first post-order,
computing downstream connected capacity at every node, picks the
smallest joint whose `maxKw ≥ downstream` from
`STING_REFNET_JOINTS.json` (15 joint records across the 3 main vendors).

### Refrigerant charge calculation

Vendor-specific. `STING_REFRIG_CHARGE_TABLES.json` ships 6 vendor
charge tables with per-OD kg/m factors plus a vendor short-system
offset (typically 1.5–4 kg for VRF systems < 50 m).

`Hvac_RefrigerantCharge`:

1. Walks every refrigerant pipe (filters by system name containing
   REFRIG / RFRG / VRV / VRF)
2. Groups by OD
3. For each OD, multiplies length × kg/m factor for the active
   vendor table
4. Adds the vendor short-system offset
5. Reports per-OD breakdown + total system charge in kg

Use the charge total to size leak-detection thresholds per
EN 378 / ASHRAE 15. A 25 kg R32 system in a 10 m² mechanical room
exceeds practical limits and needs forced ventilation.

### Scenario: VRF retrofit in 200 m² conference centre, Birmingham

Setup:

- Climate `birmingham` (use `manchester` if not in the list — same
  CTZ)
- 1 ODU on the roof (50 m up), 8 IDUs in the ceiling void at
  ground+first floor
- Total IDU capacity 90 kW cooling
- Refrigerant R32, Daikin VRV-5 H series

Hvac_SelectIdus picks:
- 6 × FXSQ-50P (5.6 kW ducted, ducted FCU mounting), 2 × FXFQ-71P
  (8 kW ceiling cassette)

Hvac_RefnetSize picks:
- 3 KHRP25 + 2 KHRP33 + 1 KHRP55 joint at the trunk

Hvac_RefrigSize per leg:
- Trunk gas line (90 kW, 60 m, +50 m lift): 1-5/8" OD, ΔP 26 kPa,
  velocity 5.5 m/s ✓
- Trunk liquid line (90 kW, 60 m, +50 m lift): 5/8" OD, ΔP 22 kPa
  (incl. 6 kPa static head — but evap-above-condenser configurations
  would credit lift back), velocity 0.6 m/s ✓
- Branch to FXFQ-71P (8 kW, 25 m, +5 m): 1-1/8" gas / 3/8" liquid

Hvac_RefrigerantCharge:
- 5/8" liquid × 60 m × 0.190 kg/m = 11.4 kg
- 1-5/8" gas × 60 m × 0.029 kg/m = 1.7 kg
- Branches sum to ~3 kg
- Vendor offset: 2.5 kg
- **Total: ~18.6 kg R32**

Compliance: VRV-5 limit total 1000 m (used 250 m ✓), ODU↔IDU vertical
110 m (used 50 m ✓), per-vendor charge limit OK.

EN 378 practical limit for R32 in a 50 m³ machine room: 18 kg ×
LFL safety factor ~~ OK with mechanical ventilation interlock.

---

## 14. Acoustic design

### What it is

Predicting and controlling the noise that the HVAC system generates
in occupied spaces. Two scales:

1. **Direct path** — fan → ducts → terminals → room. Predict NC
   (Noise Criterion).
2. **Cross-talk path** — talker in room A → terminal A → shared duct
   → terminal B → listener in room B. Predict speech privacy.

### Why it matters

Office occupants tolerate NC 40 background — beyond that, productivity
drops. Patients need NC 30 — louder than that, sleep quality drops.
Recording studios need NC 20 — louder than that, the studio is useless.

For cross-talk, BB93 (UK schools) and ASHRAE A48 require minimum 30 dB
attenuation across the duct path between rooms — speech in one
classroom should not be intelligible in the next.

### NC targets

| Space               | NC target | Reason                              |
|---------------------|-----------|-------------------------------------|
| Recording studio    | 20        | Microphone pickup                   |
| Concert hall        | 20        | Performance                         |
| Library             | 25        | Reading focus                       |
| Patient room        | 30        | Sleep                               |
| Hospital ward       | 30–35     | HTM 08-01                           |
| Classroom           | 30        | Speech intelligibility (BB93)       |
| Conference room     | 35        | Voice clarity                        |
| Office (cellular)   | 35        | Phone conversation                   |
| Office (open)       | 40        | Speech privacy lower                 |
| Operating room      | 35        | Concentration                        |
| Restaurant          | 40        | Social hum acceptable                |
| Kitchen             | 50        | Equipment noise dominant             |
| Plantroom           | 75        | Wearing PPE                          |

### How STING predicts NC

`NcPredictionEngine` (VDI 2081 + ASHRAE A48 + Bullock) walks every
duct path from fan source to terminal:

**Source.** Either reads fan Lw spectrum (63 Hz – 8 kHz octaves)
from `STING_FAN_SPECTRA.json` if the fan family name matches, or
synthesises Lw from Q + ΔP using ASHRAE Handbook of Fundamentals
formula:
```
Lw = 20·log₁₀(Q) + 25·log₁₀(ΔP) + K_blade_type
```

**Attenuation per element:**

| Element            | Attenuation source                    |
|--------------------|---------------------------------------|
| Straight unlined   | VDI 2081 table per size + frequency    |
| Straight lined     | Lining type + thickness × length      |
| Elbow              | Per-frequency dB drop                  |
| Tee branch         | Branch / through-flow ratio            |
| End reflection     | Terminal area × wavelength            |
| Silencer IL        | From `STING_SILENCER_DATA.json` per family name |

**Regenerated noise (Bullock):** velocity-dependent broadband noise
generated at each fitting. Added in quadrature to the attenuated
source Lw at each point.

**Diffuser:** Bullock's diffuser correlation already represents
post-reflection terminal noise — STING does NOT additionally add
end-reflection on top of diffuser regen (a common bias of ~3–5 dB).

**Receiver:** at the terminal, the path Lw is converted to room Lp
via the direct + reverberant model:
```
Lp = Lw + 10·log₁₀(Q/(4·π·r²) + 4/R)
```
where Q = directivity (= 2 for ceiling diffuser), r = listener
distance, R = room constant (= S·α / (1 - α)). The per-band Lp is
rated against NC curves to find the controlling NC.

### Click Hvac_NcPredict

Select the worst case ducts (or "All systems"), click. Result panel
shows:

- Predicted NC at each terminal
- Per-band Lp at receiver
- Per-element attenuation + regen breakdown
- "Hot path" — which element contributes most

Common fixes:
- NC too high at terminal → add an in-line silencer
- Mid-band frequency peak → lined elbow at the source end
- Low-frequency hum → flexible connector at fan + ducted silencer
- Cross-talk → add splitter silencer in the trunk between zones

### Cross-talk audit

`Hvac_CrossTalkAudit` walks every air terminal's connector graph
upstream, tracks `OctaveBand` attenuation at every walked element,
pairs every (talker, receiver) across different host Spaces sharing
an upstream element, and applies the full octave-band room
direct + reverberant model at the receiver.

Receiver room is built per Space:
- Volume = Space.Volume
- Surface area = Σ Space.boundary segments × height
- Absorption (heuristic from HVC_SPACE_TYPE_TXT):
    - patient 0.25 (curtains, bedding)
    - classroom 0.30 (chairs, books)
    - auditorium 0.40 (full upholstered seating)
    - plantroom 0.10 (bare concrete, no soft surfaces)
- Listener distance defaults to 2 m
- Directivity Q = 2 (ceiling diffuser)

Pairs flagged when NC > 35 OR 1 kHz attenuation < 30 dB. CSV at
`_BIM_COORD/acoustic/crosstalk_<ts>.csv` with full octave breakdown.

### Adding fan + silencer data

`STING_FAN_SPECTRA.json` and `STING_SILENCER_DATA.json` (project
overlay at `<project>/_BIM_COORD/acoustic/`). Each entry keyed by
family name; carries Lw per octave (fan) or IL per octave (silencer).
Default fallback IL = 12 dB midband (conservative).

---

## 15. Validators

STING's validators are gates between design and issue.

### Pressure-class audit (`Hvac_PressureClassAudit`)

Checks every duct for:
- Pressure class violation (SP at duct exceeds class limit)
- Role-velocity violation (v > role.MaxVelocityMs)

Now reads air density from the climate registry (location-aware) and
estimates friction with adjacent-fitting losses (half-credit to avoid
double-counting between connected ducts). Manufacturer-aware
`FittingLossCalculator.ResolveFittingLoss` reads `HVC_PROD_REF_TXT`
on fittings — vendor C beats SMACNA when registered.

### Stale-size detection (`Hvac_DetectStaleSizes`)

Walks every duct, recomputes the target size from current flow under
the active strategy, compares to actual size. Flags ducts where the
recomputed size differs by ≥ 1 standard increment.

### Envelope-stale flagging (auto via `HvacEnvelopeStaleUpdater`)

IUpdater fires on `Element.GetChangeTypeGeometry()` for OST_Walls,
OST_Windows, OST_Doors, OST_CurtainWallPanels, OST_Roofs, OST_Floors.
Resolves the affected Space via bounding-box centre →
`GetSpaceAtPoint` (with Wall-endpoint and level-wide fallbacks),
stamps `HVC_LOAD_STALE_BOOL = 1` + `HVC_LOAD_STALE_REASON_TXT`.

Bulk edits (>30 elements per trigger) fall back to project-wide stamp
so a "select all walls + nudge" doesn't open a 200-stamp transaction.

Registered at startup but **OFF by default**. Enable via
`Hvac_EnvelopeStaleToggle`. Clear all flags via
`Hvac_EnvelopeStaleClear`. Block-load auto-clears the flag on each
Space it stamps.

### Connectivity audit (`MEPConnectionAuditCommand`)

Every duct should have both ends connected (to another duct, an
equipment, or a terminal). Orphans = silent dead loads.

### NC predict + cross-talk audit

See §14.

### Run all (`RunAllValidatorsCommand`)

Runs every validator in sequence, aggregates result panel.

### RTS calibration benchmark (`Hvac_RtsBenchmark`)

`STING_RTS_REFERENCE_CASES.json` ships 4 worked examples from
ASHRAE Handbook Fundamentals 2021 Ch.18 + Daikin VRV design guide
+ CIBSE Guide A 2015, each with expected block sensible kW per RTS
class. The benchmark builds synthetic LoadZones matching each case,
runs the engine under Reactive / Light / Medium / Heavy, and flags
any comparison outside ±10 %. Regression-grade — runs in seconds —
not a TRACE / HAP head-to-head, but catches unit errors and sign
flips in the RTS convolution.

CSV at `_BIM_COORD/acoustic/rts_benchmark_<ts>.csv`. Project teams
extend via `_BIM_COORD/rts_reference_cases.json`.

---

## 16. Commissioning checklist

### What it is

The commissioning agent (Cx agent) is the third-party witness who
signs off that each piece of HVAC equipment is installed correctly,
started correctly, runs correctly under design conditions, and meets
the specification. They use witness sheets — one per equipment class
— covering Pre-Install, Pre-Startup, Startup, Functional, Handover
phases per ASHRAE Guideline 0 / CIBSE TM39.

### Why it matters

A building without commissioning is a building where 30 % of HVAC
equipment is broken on day one. AHU fans rotate backwards. Chillers
have refrigerant leaks. Boilers have wrong gas pressure. VRF systems
have crossed superheat lines. None of these are catastrophic but each
one wastes 5–20 % of design capacity and is invisible until somebody
reads the controls log six months later.

A properly commissioned building runs at 100 % of design capacity on
day one. A poorly commissioned one runs at 70 %.

### How STING does it

`HvacGenerateCxChecklistCommand` (`Hvac_GenerateCxChecklist`) walks
every mechanical equipment in the project, classifies it per
family-name pattern + category (AHU / Chiller / Boiler / VRF / Pump
/ FCU / VAV / CoolingTower / HeatPump / Fan / HeatExchanger / Damper
/ Generic), loads the task library from `STING_CX_TASKS.json`, and
emits a per-equipment CSV at `<project>/_BIM_COORD/cx/`.

The shipped task library covers ASHRAE Guideline 0 + CIBSE TM39 phase
sequence: PreInstall (manufacturer doc review, factory test), 
PreStartup (alignment, lubrication, electrical PAT), Startup (rotation
check, initial flow), Functional (full-load test, part-load test),
Handover (training, document handover).

### Per-equipment task counts

| Class           | Phases | Tasks |
|-----------------|--------|-------|
| AHU             | 5      | 11    |
| Chiller         | 5      | 9     |
| Boiler          | 5      | 9     |
| VRF (ODU)       | 5      | 8     |
| Pump            | 4      | 6     |
| FCU             | 4      | 5     |
| VAV             | 4      | 5     |
| CoolingTower    | 5      | 8     |
| HeatPump        | 5      | 8     |
| Fan             | 4      | 4     |
| HeatExchanger   | 4      | 4     |
| Damper          | 3      | 4     |
| Generic         | 3      | 4     |

### Override semantics

Project teams override per-equipment-class via
`<project>/_BIM_COORD/cx/cx_tasks_override.json`. Two shapes
supported:

**REPLACE** (default — bare array): override replaces the corporate task
list verbatim.

```json
{
  "AHU": [
    {"Phase": "Functional", "Task": "Verify economiser changeover at 18 °C OA"}
  ]
}
```

**APPEND** (additive): corporate rows kept, override rows appended
below, dedup on `Phase + Task` so re-runs stay idempotent.

```json
{
  "AHU": {
    "_merge": "append",
    "tasks": [
      {"Phase": "Functional", "Task": "Run client-specific economiser test"}
    ]
  }
}
```

### Cache

Tasks are cached per project-dir on first load. `InvalidateTaskCache()`
exposed for manual reloads. Cache invalidates on document close.

---

## 17. Drawings

### What it is

A drawing is the legal contract. STING produces them via the
**Drawing Template Manager** (see `docs/guides/DRAWING_TYPE_MANAGER_GUIDE.md`
for the full deep dive — this section covers HVAC specifics).

### Drawing types relevant to HVAC

| ID                              | Purpose                                                                |
|---------------------------------|------------------------------------------------------------------------|
| `mep-plan-A1-1to100`            | MEP services overlay (generic — falls back here when more specific isn't found) |
| `mep-hvac-duct-A1-1to100`       | HVAC ductwork layout                                                   |
| `mep-plantroom-A1-1to50`        | Plantroom plan (1:50 to show equipment detail)                          |
| `mep-coord-A1-1to50`            | Services coordination (HVAC + plumbing + electrical)                    |
| `mep-plan-A1-1to100` (with HVAC tagFamilies) | HVAC plan via discipline filter                            |

### How to make a sheet

**A) From scope boxes** (recommended).

1. Architect provides scope boxes named `STING::mep-hvac-duct-A1-1to100::L02::west`
2. **DOCS → Drawing Types → From Scope Boxes** (`DrawingTypes_FromScopeBoxes`)
3. STING creates the views, applies the profile (scale, view template,
   crop, filters), creates the sheet, places the viewport, stamps the
   title block

**B) From templates.**

1. **DOCS → Sheet Manager → Create From Template** (`CreateFromTemplate`)
2. Pick a profile from the dropdown — HVAC profiles appear next to
   built-in templates
3. STING creates the sheet, places the duplicated views, applies the profile

### View Style Pack for HVAC

`corp-coordination` is the right pack for HVAC plans. It defines:
- Discipline = Mechanical (Revit dims non-mechanical items)
- Filters: SUP (blue), RTN (green), EXH (orange), OA (purple), CHW
  (cyan), HW (red), Refrigerant (purple), Condensate (light blue)
- Halftones architectural / structural so HVAC reads as foreground

### Plantroom layout tips

`mep-plantroom-A1-1to50` requires:
- Detailed view template showing every connection
- Equipment tagged with capacity + electrical load + weight
- Pipe + duct insulation shown in cross-section
- Maintenance clearance zones shown via Detail Components
- Title-block params include "AS BUILT" status when issued as such

### HVAC riser diagram

Like an electrical riser but vertical sections of the HVAC services:
- One Section View oriented vertically through the riser shaft
- View range top = top of building, bottom = lowest level
- Apply `mep-hvac-duct-A1-1to100` (or a custom profile)
- Annotation pass tags every AHU, FCU, pump, chiller it crosses

---

## 18. Server integration

### What it is

STING runs on each engineer's desktop in Revit. Planscape Server is
the cloud backend that aggregates per-project data across engineers,
holds it for the Coordinator + Project Manager dashboards, and feeds
the mobile commissioning app.

### Endpoints

`Planscape.Server/src/Planscape.API/Controllers/HvacController.cs`:

| Method | Route                                                | Purpose                              |
|--------|------------------------------------------------------|--------------------------------------|
| POST   | `/api/projects/{id}/hvac/loads`                      | Push a single space's load snapshot   |
| POST   | `/api/projects/{id}/hvac/loads/bulk`                 | Bulk-push the load grid               |
| POST   | `/api/projects/{id}/hvac/nc`                         | Push an NC prediction snapshot        |
| POST   | `/api/projects/{id}/hvac/refrigerant`                | Push a refrigerant sizing snapshot    |
| GET    | `/api/projects/{id}/hvac/dashboard`                  | Server-side aggregated dashboard      |
| GET    | `/api/projects/{id}/hvac/loads?systemId&since`       | Query loads since timestamp           |
| GET    | `/api/projects/{id}/hvac/nc?overTargetOnly`          | Query NC predictions over target      |
| GET    | `/api/projects/{id}/hvac/refrigerant?refrigerantId`  | Query refrigerant sizings by fluid    |

### Entities

- `HvacLoadSnapshot` — per-Space block load result (peak sens / lat /
  hour / OA + provenance)
- `HvacNcSnapshot` — per-terminal NC prediction (predicted NC + target
  + over/under)
- `HvacRefrigerantSizing` — per-leg refrigerant sizing (OD / velocity
  / ΔP / vendor compliance)

DbContext registers all three sets with composite indexes on
(ProjectId, CapturedAt), (ProjectId, SystemId), (ProjectId,
PredictedNc), (ProjectId, RefrigerantId).

### Publishing

`Hvac_PublishToServer` (RPRT tab) bundles every grid in the panel into
a single bulk push. The engineer signs in via Planscape SSO, picks the
project, clicks Publish. Server stores the snapshot under the
authenticated tenant + project.

### Deployment

EF migration is the next step before first deploy:
```
dotnet ef migrations add HvacEngineSnapshots --project Planscape.Server
```

Once migrated, the Planscape mobile app can pull HVAC data for site
walks ("which spaces have stale loads?", "which terminals failed NC?").

---

## 19. Comparison with TRACE / HAP / IES

### Why these tools exist

TRACE 3D Plus (Trane), Carrier HAP, IES VE, EnergyPlus and DesignBuilder
are dedicated thermal simulation tools. They have:

- More refined glazing models (multi-pane with frame heat-bridges)
- Hour-by-8760 dynamic simulation (not 24-h design days)
- Annual energy consumption + carbon
- LEED + BREEAM + Energy Star certification reports
- HVAC system templates (CV-Reheat, VAV-Reheat, Heat Recovery Wheel,
  ground-source HP, etc.)

STING's BlockLoadEngine is a design-day calc — accurate for peak
sizing but doesn't do 8760 annual energy. For projects that need both,
the workflow is:

1. **Design in STING** — geometry + space types + climate + equipment
2. **Export gbXML** — Revit `File → Export → gbXML`
3. **Import to TRACE / HAP / IES** — runs annual simulation
4. **Compare back** — `Hvac_CompareLoads` reads the simulator's CSV
   output, diffs against STING's block load
5. **Import back if needed** — `Hvac_ImportGbxmlLoads` overwrites
   STING's `HVC_PEAK_*` stamps with the simulator's authoritative
   numbers

### gbXML import (`Hvac_ImportGbxmlLoads`)

Reads a TRACE / HAP / IES / EnergyPlus gbXML export, parses
`<Zone>/PeakCooling…`, `PeakLatent…`, `OutdoorAir…` elements, joins on
Space Number → Name → ElementId, stamps:

- `HVC_PEAK_SENS_W`
- `HVC_PEAK_LAT_W`
- `HVC_OA_LS`
- `HVC_LOAD_SOURCE_TXT = "gbXML:<filename>"`

Unit conversion handles W / kW / Btu/h / tons for cooling, CFM / m³/h /
m³/s / L/s for flow.

After import, run `Hvac_PropagateLoads` to push the simulator's
numbers downstream into the duct flow stamps. The rest of the workflow
(auto-size, balance, NC) proceeds identically.

### Non-destructive compare (`Hvac_CompareLoads`)

Reads a TRACE 3D Plus / HAP CSV export with header row
`ZoneId, SensibleKw, LatentKw, OutdoorAirLs`, joins on STING Space
Number → Name → ElementId, compares per-zone sensible loads against
the `HVC_PEAK_SENS_W` stamps. Reports:

- Mean |Δ| %
- Max |Δ|
- R²
- Count within tolerance (default ±15 %, override via
  `PRJ_TRACE_TOLERANCE_PCT`)
- Top-20 outside-band zones

Does NOT overwrite STING's stamps — purely diagnostic. CSV at
`_BIM_COORD/acoustic/trace_compare_<ts>.csv`.

This is STING's first in-tree validation path against an
industry-reference engine — useful for QA-ing a STING block load
before committing to it.

---

## 20. Workflow presets

### What they are

Workflow presets are JSON files in `Data/` that chain commands into
named sequences. Run with **main panel → BIM → Workflow Preset**.

### HVAC workflows shipped

**`WORKFLOW_HVACDesign.json`** — the full design pass:

1. `Hvac_BlockLoad` — peak loads per space + system
2. `Hvac_PropagateLoads` — stamp duct flows from space peaks
3. `Mep_AutoSizeDuct` — apply current strategy
4. `MepAutoSizePipeCommand` — pipe sizing
5. `HardyCrossCommand` — balance hydronic
6. `Hvac_NcPredict` — NC at every terminal
7. `Hvac_PressureClassAudit` — DW/144 + role-velocity audit
8. `Hvac_DetectStaleSizes` — flag any ducts now-stale
9. `RunAllValidatorsCommand` — full validator suite

**`WORKFLOW_HVACCommissioning.json`** — pre-issue Cx workflow:

1. `Hvac_PressureClassAudit`
2. `Hvac_NcPredict`
3. `Hvac_CrossTalkAudit`
4. `Hvac_DetectStaleSizes`
5. `Hvac_GenerateCxChecklist`
6. `Hvac_PublishToServer`

### Conditional steps

Steps support gates:
- `MinCompliancePct` — only run when project compliance is at least N %
- `MaxCompliancePct` — only run when below N %
- `RequiresStaleElements` — skip when no stale items
- `MinSpaceCount` — skip on tiny projects

### Result persistence

Each workflow run logs to `STING_WORKFLOW_LOG.json` (capped at 100
records). Use `Hvac_WorkflowTrend` to see how compliance has changed
over the last 30 days.

---

## 21. Common first-timer mistakes

| Mistake | What goes wrong | How STING catches it |
|---------|------------------|----------------------|
| Placing Rooms instead of Spaces | Block load has nothing to run on | LOADS tab shows 0 spaces |
| Spaces without `HVC_SPACE_TYPE_TXT` set | All spaces load as `Office` defaults | Block load result panel warns |
| Not setting climate site | Loads computed at London — meaningless for Singapore/Dubai | `Hvac_ClimateInspect` reveals fallback |
| Not setting construction profile | Wall U-values are wrong by 2–3× | Result panel "no PRJ_CONSTRUCTION_PROFILE_TXT — defaulting to PartL2021" |
| Setting RTS = Reactive on a concrete building | Loads peak at solar noon instead of 4 PM, oversized AHUs | `Hvac_RtsBenchmark` flags ±10 % drift |
| Forgetting to run `Hvac_PropagateLoads` | Ducts have flow 0 L/s → autosize picks 100 mm everywhere | Stale-size detection flags |
| Running auto-size before block load | Ducts sized for default flows (10 L/s) — undersized | Status bar warns "block load not run since YYYY-MM-DD" |
| Mixing UK_SI and US_IP region in one project | Duct sizes flip between metric and imperial sets | Pressure-class audit flags non-standard sizes |
| Not setting `HVC_PROD_REF_TXT` on equipment | All pressure drops use generic SMACNA Cs (~25 % less accurate) | `MepFittingLossReport` shows manufacturer vs generic split |
| Setting per-leg ΔP > per-system ΔP for refrigerant | Single leg fits, but multi-leg system fails vendor limit | Refrigerant size result panel |
| Ignoring oil-return velocity floor | VRF system installs fine, fails after 6 months | Refrigerant size result panel |
| Sizing condensate at hot-water velocities | Pipe is way oversized, gravity-flow stalls | Pipe service lookup uses correct condensate limit (0.6 m/s) |
| Predicting NC without setting fan family Lw | Synthetic Lw used — predictions ±5 dB | NC result panel says "synthetic Lw — install fan spectrum sidecar" |
| Forgetting silencer in long ducts | NC overshoots by 10 dB at terminal | `Hvac_NcPredict` shows attenuation breakdown |
| Not running cross-talk audit on schools/healthcare | Speech privacy fails post-construction | `Hvac_CrossTalkAudit` flags BB93 violations |
| Trusting block load after envelope changes | Loads stale, sizes wrong | `HvacEnvelopeStaleUpdater` IUpdater stamps `HVC_LOAD_STALE_BOOL` automatically |
| Forgetting to re-run after `Hvac_SelectIdus` updates | Sized duct doesn't match IDU's required flow | Stale-size detection |
| Not using DCV on variable-occupancy spaces | OA fan runs at design max all day | DCV savings % in block-load result |
| Ignoring `Hvac_PropagateRefrigToDuct` on ducted VRF | Duct sized for 0 flow, doesn't connect | Stale-size detection |
| Not creating REFNET joints in the model | Charge calc misses branch joints | Refrigerant charge result warns "REFNET sizing not run" |
| Not running `Hvac_RefrigerantCharge` | Leak-detection threshold sized wrong | Result panel shows total kg per system |
| Issuing without `Hvac_GenerateCxChecklist` | Cx agent shows up on day one with no witness sheets | RPRT tab task list shows missing checklist |
| Skipping `Hvac_FullDesignPass` before issue | Drift between block load + duct sizes + NC + pressure class | `Hvac_DetectStaleSizes` + pressure-class audit catch it |
| Comparing STING block load to TRACE without setting tolerance | Default ±15 %, may flag many zones | Set `PRJ_TRACE_TOLERANCE_PCT` to your actual project tolerance |
| Editing manufacturer Cs by hand in MEP_SIZING_RULES.json | Lost on next plugin update | Edit `<project>/_BIM_COORD/mep_sizing_rules.json` instead — project override stays put |

---

## 22. Glossary

| Acronym | Meaning |
|---------|---------|
| ACH | Air Changes per Hour |
| AFFL | Above Finished Floor Level |
| AHU | Air Handling Unit |
| ASHRAE | American Society of Heating, Refrigerating and Air-Conditioning Engineers |
| BB93 | UK Building Bulletin 93 — acoustic design of schools |
| BCO | British Council for Offices |
| BEP | Best Efficiency Point (pump curve) |
| BIM | Building Information Modelling |
| BMS | Building Management System |
| BOQ | Bill of Quantities |
| CDD10 | Cooling Degree Days base 10 °C |
| CHW | Chilled Water (typically 6/12 °C flow/return) |
| CIBSE | Chartered Institution of Building Services Engineers |
| COBie | Construction Operations Building information exchange (FM data format) |
| COP | Coefficient of Performance (heating efficiency) |
| CTF | Conduction Transfer Function (per-layer thermal time series) |
| Cx | Commissioning (the activity) |
| Cv / Kvs | Valve flow coefficient (m³/h or gpm @ 1 bar / 1 psi) |
| DB / DCW / DHW | Distribution Board / Domestic Cold Water / Domestic Hot Water |
| DCV | Demand-Controlled Ventilation (CO₂ / occupancy reset) |
| DOAS | Dedicated Outdoor Air System |
| DST | Daylight Saving Time |
| DW/144 | UK ductwork specification (HVAC Manual) — pressure classes A/B/C/D |
| EER | Energy Efficiency Ratio (cooling efficiency) |
| EnEV | German energy regulations (Energieeinsparverordnung) |
| ESP | External Static Pressure (fan duty point) |
| FCU | Fan-Coil Unit |
| gbXML | Green Building XML — open standard for building simulation data exchange |
| GSA | US General Services Administration (federal building codes) |
| HAP | Carrier Hourly Analysis Program (HVAC sim) |
| HDD18 | Heating Degree Days base 18 °C |
| HFG | Latent heat of vaporisation (water — 2450 kJ/kg @ 20 °C, varies with T) |
| HVAC | Heating, Ventilation and Air Conditioning |
| HWS | Hot Water Service (heating loop, typically 82/71 °C) |
| HX / HEX | Heat Exchanger |
| IDU / ODU | Indoor Unit / Outdoor Unit (split / VRF) |
| IECC | International Energy Conservation Code (US) |
| IES VE | Integrated Environmental Solutions Virtual Environment (HVAC sim) |
| IL | Insertion Loss (silencer attenuation, dB) |
| ISO | International Organization for Standardization |
| ISO 19650 | International BIM standard |
| LEED | Leadership in Energy and Environmental Design (US green-build certification) |
| LFL | Lower Flammability Limit (refrigerant) |
| LHW | Low-Temperature Hot Water (typically 55/45 °C — heat pumps) |
| Lp | Sound Pressure Level (dB, at receiver) |
| Lw | Sound Power Level (dB, of source) |
| MCWB | Mean Coincident Wet-Bulb (at design DB peak) |
| MEP | Mechanical, Electrical, Plumbing |
| MGS | Medical Gas System |
| NC | Noise Criterion (background noise rating) |
| OA | Outdoor Air (fresh air rate) |
| OD | Outside Diameter (refrigerant pipe sizing) |
| OR | Operating Room (US) / Operating Theatre (UK) |
| PIC / PICV | Pressure-Independent Control Valve |
| Pset | IFC Property Set |
| PSC / Pa·s/m | Pressure drop per metre |
| Q (flow) | Volumetric flow (m³/s, L/s, CFM) |
| R²  | Coefficient of determination (statistical fit) |
| RAH | Returned Air Hood |
| REFNET | Daikin refrigerant branch network (also generic term for VRF tree joints) |
| RTS | Radiant Time Series (ASHRAE thermal mass model) |
| RTF | Radiant Time Factor (per-hour weighting) |
| SAT | Supply Air Temperature |
| SFP | Specific Fan Power (W/L/s of supply air) |
| SHGC | Solar Heat Gain Coefficient (glass — fraction of incident solar transmitted) |
| SI / IP | Système International / Inch-Pound (metric / imperial) |
| SMACNA | Sheet Metal and Air Conditioning Contractors' National Association (US duct std) |
| SP | Static Pressure (Pa, in.w.g.) |
| TRACE 3D Plus | Trane Air Conditioning Economics simulation |
| TM39 | CIBSE Technical Memorandum 39 — commissioning |
| TR | Tonnage of Refrigeration (1 TR = 3.52 kW cooling) |
| TWA | Time-Weighted Average |
| U-value | Thermal transmittance W/m²K (lower = better insulation) |
| UFAD | Underfloor Air Distribution |
| UV | Unit Ventilator |
| VAV / CAV | Variable / Constant Air Volume |
| VDI 2081 | German guideline on duct acoustics |
| VRV / VRF | Variable Refrigerant Volume / Flow (Daikin brand / generic term) |
| W/m² | Watts per square metre (load density, lighting density, equipment density) |
| WSFU | Water Supply Fixture Unit (Hunter's method) |
| Z | Acoustic impedance |

---

## Recommended first-week practice

If you have one Revit model and one week to learn STING HVAC, do this:

| Day | Task |
|-----|------|
| 1   | Run Project Setup Wizard. Link the architect's model. Place Spaces in every conditioned room. Set `HVC_SPACE_TYPE_TXT` on each Space. |
| 2   | Set `PRJ_CLIMATE_SITE_ID` and `PRJ_CONSTRUCTION_PROFILE_TXT`. Run `Hvac_BlockLoad`. Read the result panel — does the diversity factor make sense (0.7–0.9 for offices)? |
| 3   | Place AHUs / chillers / FCUs / VRFs. Tag with `Tag & Combine`. Set `HVC_PROD_REF_TXT` on each. Run `Hvac_SelectIdus` for room-level units. |
| 4   | Run `Hvac_PropagateLoads`, then `Mep_AutoSizeDuct` and `MepAutoSizePipeCommand`. Run `HardyCrossCommand` to balance. Fix any over-velocity ducts. |
| 5   | Run `Hvac_RefrigSize` (if VRF), `Hvac_RefnetSize`, `Hvac_RefrigerantCharge`. Verify vendor pipe-length compliance. |
| 6   | Run `Hvac_NcPredict` on critical zones. Run `Hvac_CrossTalkAudit` if you have healthcare or education spaces. Add silencers or upsize where NC fails. |
| 7   | Run `Hvac_FullDesignPass` for a clean re-run. Run `Hvac_GenerateCxChecklist`. Generate plantroom + HVAC duct drawings via the Drawing Type Manager. Issue. |

Once you've done that round-trip, you'll know more about practical HVAC
BIM than 80 % of mechanical grads. Welcome to the discipline.

---

*Document version 1.0 — May 2026. Maintained alongside CLAUDE.md.*
