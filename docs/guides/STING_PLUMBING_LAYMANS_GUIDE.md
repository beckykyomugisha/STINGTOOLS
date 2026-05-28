# STING Plumbing — A Layman's Guide for First-Time Public Health Designers

**Audience.** You've never produced a real plumbing or public-health design
before. You've opened Revit a few times. Someone has handed you a project,
said "do the plumbing", and pointed you at the STING Tools plugin. This guide
takes you from the *first idea* of a plumbing design all the way to a
finished, issue-ready package, explaining **what** every step is, **why** it
exists, and **how** STING does the heavy lifting for you.

> **How to read this guide.** Each section has three parts:
> - *What it is* — the engineering concept in plain English.
> - *Why it matters* — what goes wrong if you skip or fudge it.
> - *How to do it in STING* — the actual buttons / commands, in order.
>
> If you only have an hour, read sections 1, 2, 3, 5, 9 and 12.

---

## Table of contents

1. [What "plumbing design" actually means](#1-what-plumbing-design-actually-means)
2. [Why Revit + STING (and not AutoCAD + a calculator)](#2-why-revit--sting-and-not-autocad--a-calculator)
3. [The five things every plumbing design must answer](#3-the-five-things-every-plumbing-design-must-answer)
4. [Anatomy of the STING Plumbing dock panel](#4-anatomy-of-the-sting-plumbing-dock-panel)
5. [Step-by-step: from blank Revit file to issued drawings](#5-step-by-step-from-blank-revit-file-to-issued-drawings)
6. [Understanding the ISO 19650 tag for plumbing](#6-understanding-the-iso-19650-tag-for-plumbing)
7. [Setting up Revit pipe systems before you place anything](#7-setting-up-revit-pipe-systems)
8. [Placing plumbing fixtures](#8-placing-plumbing-fixtures)
9. [Cold water supply sizing — Hunter's method and BS EN 806-3](#9-cold-water-supply-sizing)
10. [Hot water, TMVs, dead-legs, and recirculation](#10-hot-water-tmvs-dead-legs-and-recirculation)
11. [Expansion vessel sizing](#11-expansion-vessel-sizing)
12. [Drainage sizing — DU, slope, fall and rise](#12-drainage-sizing)
13. [Vent design — primary, secondary, anti-siphon](#13-vent-design)
14. [P-traps — what they do and why STING inserts them for you](#14-p-traps)
15. [Auto-routing — Plumb_AutoRoute and manual fill-in](#15-auto-routing)
16. [Invert levels — mAOD, cover depth, slope continuity](#16-invert-levels)
17. [Backflow protection — BS EN 1717 categories and air gaps](#17-backflow-protection)
18. [Storm, roof drainage, SuDS, rainwater harvesting](#18-storm-roof-drainage-suds-rainwater-harvesting)
19. [Septic tank and off-mains drainage](#19-septic-tank-and-off-mains-drainage)
20. [Hangers and sleeves — supports and firestopping](#20-hangers-and-sleeves)
21. [Validators — letting STING grade your homework](#21-validators)
22. [Schedules and docs — pipe schedule, BOQ, manhole, isometric, commissioning](#22-schedules-and-docs)
23. [Drawings — plumbing drawing types](#23-drawings)
24. [Healthcare-specific plumbing](#24-healthcare-specific-plumbing)
25. [Common first-timer mistakes (and how STING catches them)](#25-common-first-timer-mistakes)
26. [Glossary of acronyms](#26-glossary)

---

## 1. What "plumbing design" actually means

Plumbing — more formally **public health engineering** — covers everything
that moves water, waste or gas into, around, or out of a building. A
complete design decides:

1. **Where water comes from** — the incoming mains, storage tanks, booster
   sets, hot-water generation (calorifiers, electric heaters, heat pumps).
2. **How clean water is distributed** — the network of cold-water (DCW),
   hot-water (DHW), and hot-water-return (DHWR) pipes that feed every tap,
   shower, dishwasher and basin.
3. **How waste leaves** — soil pipes (the brown stuff), waste pipes (the
   grey stuff from sinks and showers), and the vents that keep traps from
   gurgling dry.
4. **How storm water is managed** — rainwater pipes (RWD) on roofs,
   gutters, gulleys, soakaways, attenuation tanks, SuDS (Sustainable
   Drainage Systems).
5. **How fuel arrives** — natural-gas (GAS) and LPG distribution to
   boilers, hobs and water heaters.
6. **How fire-fighting water is delivered** — wet risers, dry risers,
   sprinklers (these often overlap with the fire-protection discipline).
7. **How it all complies** — with codes like **BS EN 806** (water supply),
   **BS EN 12056** (drainage), **BS 6700** (domestic), **HTM 04-01**
   (healthcare water hygiene), **Approved Document G** (UK Building Regs
   sanitation), and **Approved Document H** (drainage and waste).

In practice you produce three categories of output:

| Category    | What's in it                                                            |
|-------------|-------------------------------------------------------------------------|
| Drawings    | Plumbing plans, drainage layouts, vent risers, schematic isometrics, manhole details, roof drainage plans |
| Schedules   | Fixture schedules, pipe schedules, valve schedules, manhole schedules, insulation schedules, BOQ |
| Calculations| Loading-unit sums, pipe sizing, pressure drop, drainage gradient checks, expansion vessel sizing, rainwater yield, soakaway sizing |

> **Why this matters for STING.** STING's plumbing workflow is built so
> that the *model* is the source of truth and the drawings + schedules + pipe
> sizes fall out automatically. You don't draw a riser by hand. You place
> fixtures, connect them to pipe systems, tag them, and STING produces the
> riser, the pipe schedule, the BOQ and the commissioning pack. That's the
> whole point.

---

## 2. Why Revit + STING (and not AutoCAD + a calculator)

A first-timer often asks: "couldn't I just draw lines in AutoCAD and use a
spreadsheet for the sizes?". Yes — and you'd produce a drawing that *looks*
right but has none of the data behind it, no way of catching the basin you
forgot to vent, and no way of regenerating the pipe schedule when the
architect moves a wall.

**Revit** is a *Building Information Modelling* tool. Every fixture, pipe,
fitting and valve is an **object** with parameters (DN, flow, fluid, design
pressure, manufacturer, level, room). When you move a wall, the basin moves
with it. When you change a basin to a Belfast sink, the loading units
update. When you add a new WC, the drainage stack capacity recomputes.

**STING Tools** is a plugin we wrote on top of Revit because Revit out of
the box is generic — it doesn't know that a clinical wash basin needs
**Cat 5** backflow protection, that a hot dead-leg over 1 m breaches
**HTM 04-01**, or that a 100 mm soil stack can only carry 5.2 L/s before
it gets noisy. STING adds:

- A **registered parameter library** (~2,500 shared parameters, 31 of them
  `PLM_*` for plumbing) so every fixture, pipe and valve carries the same
  data fields no matter who placed it.
- An **ISO 19650 tagging engine** that builds a unique 8-segment tag for
  every element (see §6).
- **Sizing engines** for cold water (Hunter's method, BS EN 806-3),
  drainage (Maguire, BS EN 12056-2), vents, expansion vessels, soakaways
  and rainwater yield — all built into the dock panel.
- **Auto-routing** that draws pipes from a riser to each fixture using
  the connector graph, and **AutoSlope** that adjusts drainage gradients
  to stay within 1:80 (min) and 1:40 (max).
- **Validation engines** that catch dead-legs, missing traps, vents that
  don't reach atmosphere, drainage that goes uphill, and backflow
  classifications that don't match the fluid category.
- **Drawing automation** that produces drainage plans, vent risers and
  isometric schematics on the right paper, with the right title block,
  at the right scale, with the right view template.

You are still the engineer. STING is the apprentice that does the typing,
the tables, the cross-checking and the donkey work.

---

## 3. The five things every plumbing design must answer

Before you click anything, know what you're trying to produce. A complete
plumbing design answers five questions:

1. **What's the load?** Sum of every basin, WC, shower, sink, urinal and
   dishwasher. Expressed in **loading units** (LU for supply, **discharge
   units** or DU for drainage). Drives every pipe size in the building.
   See §9 and §12.
2. **What size are the pipes?** Sized so that (a) supply water arrives at
   each outlet within 1.5–3.5 bar at adequate flow, (b) drainage waste
   leaves without flooding or solid-deposit risk, (c) velocities stay
   inside material limits (1.5 m/s max for cold copper, 1.0 m/s for hot
   to avoid noise + erosion).
3. **What's the slope?** Drainage works by gravity. Every horizontal
   drainage pipe needs a fall (downward gradient) between **1:80 minimum**
   (so solids don't strand) and **1:40 maximum** (so liquid doesn't run
   ahead of solids). Vertical stacks have no slope but have a maximum
   *capacity* before they jump-flow and pull traps dry.
4. **What's vented?** Drainage stacks must be open to atmosphere at the
   top so the air pressure inside follows the water down. Without venting,
   discharging one fixture sucks the water seal out of the trap of
   another fixture, which then lets sewer gas into the building. See §13.
5. **What's the backflow risk?** Water in a supply pipe must never be
   able to flow backwards into the main. A hose left in a swimming pool,
   a clinical sink used to dispose of body fluids, a chemical wash trough
   — all are potential pathways for contamination. **BS EN 1717**
   categorises fluids 1–5 and prescribes the right protection. See §17.

STING tracks each of these via parameters. If you populate the parameters,
the schedules, calculations and drawings work. If you don't, nothing works.
That's why §6 (tagging) and §7 (pipe systems) are the two most important
sections of this guide.

---

## 4. Anatomy of the STING Plumbing dock panel

STING ships a **dedicated Plumbing dock panel**, separate from the main
STING panel and the Electrical panel. It registers as a third
`IDockablePaneProvider` via `StingPlumbingPanelProvider` and dispatches
**37 commands** through `StingPlumbingCommandHandler`. Open it from the
ribbon's *Plumbing* button or via `View → User Interface → STING Plumbing`.

The panel has **eight tabs**, each grouping a phase of the plumbing
workflow:

| Tab         | What's in it                                                                       | When to use                                              |
|-------------|------------------------------------------------------------------------------------|----------------------------------------------------------|
| **SYSTEM**   | Save / load system config (pipe materials, services, defaults, project overrides) | First thing on a new project; whenever you change defaults |
| **SUPPLY**   | Fixture scan, supply sizing, pressure check, expansion vessel, TMV register      | Sizing the cold + hot water supply                       |
| **DRAINAGE** | Drainage sizing, vent design, invert levels, p-trap insertion                    | Sizing the soil + waste + vent system                    |
| **ROUTE**    | Auto-route pipes, fix slopes, place sleeves, place hangers                       | Drawing physical pipework in the model                   |
| **STORM**    | Rainwater harvesting, SuDS, soakaways, septic tanks, roof drainage               | Site-wide drainage; rainwater management                 |
| **SPECIALTY**| (Reserved — currently rolls into Audit) — backflow audit, dead-leg detection     | Healthcare projects, complex commercial work             |
| **AUDIT**    | Full plumbing audit (compliance scanner across all sub-systems)                  | Before every issue                                       |
| **DOCS**     | Pipe schedule, BOQ, manhole schedule, isometric, commissioning pack              | When you start producing deliverables                    |

You'll also use the **main STING panel** for cross-discipline things:

- **TEMP → Setup → Project Setup Wizard** for first-day project setup.
- **CREATE → Auto Tag / Tag & Combine** for tagging fixtures and pipes.
- **DOCS → Sheet Manager / Drawing Types** for sheet production.
- **BIM → Issue Deliverable** for the final issue.

> **Tip.** Every button on the plumbing panel is also accessible by typing
> its command tag (`Plumb_ScanFixtures`, `Plumb_SizeSupply`, etc.). The
> handler `UI/Plumbing/StingPlumbingCommandHandler.cs` maps each tag to
> the right command class — see `CLAUDE.md` if you need a tag.

### The 15 engines that sit behind the buttons

You don't need to know about them, but it helps to know they're there.
Each button delegates to one of these engines under `Core/Plumbing/`:

| Engine                       | What it does                                                              | Standard                            |
|------------------------------|---------------------------------------------------------------------------|-------------------------------------|
| `WaterSupplySizer`           | Hazen-Williams head loss + Hunter's probable simultaneous demand          | BS EN 806-3, BS 6700                |
| `DrainageSizer`              | Maguire formula for horizontal soil + branch sizing                       | BS EN 12056-2, BS EN 752            |
| `FixtureUnitScanner`         | Walks the project and counts per-type fixtures, writes DU / LU / WSFU     | BS 6465, ASPE                       |
| `FixtureUnitAggregator`      | System-level rollup of LU and DU per stack / system                       | BS EN 12056-2                       |
| `ExpansionVesselSizer`       | Sizes pressurised expansion vessels for sealed DHW + heating systems      | BS 7074-1                           |
| `InvertLevelEngine`          | US/DS invert levels in mAOD with cover-depth audit                        | BS EN 752                           |
| `PTrapInserter`              | Idempotent connector-graph p-trap detector + seed family resolver         | BS EN 274                           |
| `VentDesigner`               | Sizes primary, secondary and anti-siphon vents                            | BS EN 12056-2                       |
| `TrapDesigner`               | Trap depth and anti-siphon rules                                          | BS EN 274                           |
| `BackflowClassifier`         | Classifies every outlet's fluid category 1–5 + assigns protection         | BS EN 1717                          |
| `DeadLegDetector`            | Walks the DHW graph reporting any stagnant branch > 1 m                   | HTM 04-01, BS 8558                  |
| `RecircLoopBalancer`         | Balances DHW recirculation flows to keep return temp ≥ 55 °C              | HTM 04-01, BS 6700                  |
| `StackCapacityValidator`     | Audits stack diameter vs. discharge load                                  | BS EN 12056-2                       |
| `RainwaterHarvestingCalc`    | Yield + tank sizing for rainwater harvesting                              | BS 8515, CIRIA C753, BRE 365        |
| `PlumbingComplianceScanner`  | Aggregate RAG (Red/Amber/Green) compliance across all of the above       | All of the above                    |

---

## 5. Step-by-step: from blank Revit file to issued drawings

Here is the canonical plumbing workflow. Do these in order. Sections 6–24
explain each step in depth — this is the bird's-eye view.

### 5.1 Open or create the Revit project

If your team uses a federated model, the architect will give you a central
file to attach to. If you're starting from scratch, open Revit's *Plumbing
Template* and save a new file.

### 5.2 Run **Project Setup Wizard** (TEMP → Setup → ★ Project Setup Wizard)

**Why.** Revit out of the box doesn't know what a `PLM_DRN_DU` is. The
wizard binds STING's ~2,500 shared parameters (31 of them plumbing-specific
`PLM_*`) to the correct categories, creates the standard view templates,
filters, worksets and phases, and seeds the project with `PRJ_ORG_*`
parameters (project code, originator, client, suitability) so every drawing's
title block fills itself in.

**How.** Click `★ Project Setup Wizard`, follow the 7 pages. On the
disciplines page tick **Plumbing** (and **Public Health** if separate at
your office, plus **Fire Protection** if you're doing wet risers). If
unsure, accept defaults. The wizard runs `MasterSetupCommand` under the
hood (15 sub-steps).

If a colleague has already set the project up, you can skip this — but run
**TEMP → Setup → Check Data Files** first to confirm the parameter pack is
loaded and `STING_PLUMBING_DRAINAGE_TABLES.json` /
`STING_PLUMBING_SUPPLY_TABLES.json` / `STING_PIPE_MATERIALS_HYDRAULIC.json`
are present.

### 5.3 Link the architectural model

`Insert → Link Revit → architecture.rvt`. Pin the link (`Modify → Pin`) so
you can't move it by accident. Copy/Monitor the levels and grids so your
plumbing levels stay in sync.

**Why.** Every fixture you place needs a host (a wall, a floor) and every
pipe needs a level. If the architect moves a wall, your basins follow.
Drainage falls are measured between levels — if levels drift, your slopes
go wrong.

### 5.4 Place rooms (or use linked rooms)

Rooms drive `LOC` and `ZONE` auto-detection. STING reads room name /
department to derive these tokens. If your project uses linked rooms (rooms
in the architectural model), STING's `SpatialAutoDetect` reads them through
the link automatically.

Rooms are *especially* important for plumbing because:
- **Wet rooms / WC blocks** need backflow Cat 3+ on outlets.
- **Clinical rooms** need Cat 5 RPZ on every tap.
- **Catering kitchens** need grease interceptors (Building Regs Part H).
- **Plant rooms** need accessible isolators and pressure test points.

### 5.5 Save plumbing system config (SYSTEM tab → `Plumb_SaveSystemConfig`)

A project-wide config file at `<project>/_BIM_COORD/plumbing_systems.json`
records:

- Pipe materials per service (e.g. DCW = Copper Type B + Cu C, DHW =
  Copper Type B with Class 0 lagging, SAN = uPVC, RWD = Cast Iron above
  ground / uPVC below ground, GAS = Mild Steel + Yorkshire fittings).
- Velocity limits (1.5 m/s cold, 1.0 m/s hot, 4 m/s drainage).
- Pa-per-m friction targets (200 Pa/m typical for cold).
- Default static pressure assumption (3.0 bar at incoming mains).
- TMV2 / TMV3 mixing temperatures (38 °C wash, 41 °C shower, 43 °C bath).

You can load other projects' configs with `Plumb_LoadSystemConfig` to
fast-forward setup.

### 5.6 Set up Revit pipe systems (§7)

**You cannot route anything until pipe systems exist.** New starters
constantly trip on this: they place a basin, try to route a pipe, and
Revit silently refuses because it doesn't know whether the pipe is DCW,
DHW or SAN. Do §7 *before* placing anything you'll need to connect.

### 5.7 Place the **fixtures** first (§8)

Every plumbing system terminates at a fixture (basin, WC, sink, shower,
floor drain, tap, urinal). Place these first so when you route pipes, you
know where they're going.

### 5.8 Scan fixtures (SUPPLY tab → `Plumb_ScanFixtures`)

Once fixtures are placed, STING walks every plumbing fixture in the
project and writes `PLM_DRN_DU`, `PLM_SUP_LU`, `PLM_SUP_WSFU` based on
each fixture's type. A WC = 14 DU + 2 LU; a basin = 3 DU + 1.5 LU; a
shower = 9 DU + 3 LU; a wash trough varies by length. This gives you a
per-fixture loading histogram you'll use for every downstream sizing
calc.

### 5.9 Size the supply (SUPPLY tab → `Plumb_SizeSupply`) — §9

For each cold-water main and branch, STING aggregates LUs, looks up the
**probable simultaneous demand** via Hunter's method, sizes the pipe to
stay below 1.5 m/s velocity and within Pa-per-m friction, and writes
`PLM_SUP_DN`, `PLM_SUP_VEL_MS`, `PLM_SUP_DP_PA_M`, `PLM_SUP_FLOW_LS`.

### 5.10 Pressure check (SUPPLY tab → `Plumb_PressureCheck`)

STING traces every supply path from origin (incoming main) to outlet,
summing static head + friction loss, and reports residual pressure at
each outlet. Outlets below 1.0 bar (or below their minimum spec — many
modern showers need 1.5–2.0 bar) are flagged. The fix is to upsize the
main, add a booster set, or relocate the storage tank.

### 5.11 Expansion vessel (SUPPLY tab → `Plumb_ExpVessel`) — §11

For sealed DHW systems, an expansion vessel absorbs the volume increase
of water as it heats. STING sizes per BS 7074-1 from system volume,
acceptance factor and pre-charge pressure.

### 5.12 TMV register (SUPPLY tab → `Plumb_TMVRegister`)

Every TMV (thermostatic mixing valve) needs annual commissioning and
healthcare-grade ones need quarterly checks. STING registers every TMV in
the model with its location, mix temperature, fail-safe direction
(usually cold), and TMV2 vs TMV3 classification. Exports to a CSV that
the FM team holds for the building's life.

### 5.13 Size drainage (DRAINAGE tab → `Plumb_SizeDrainage`) — §12

For each drainage branch and stack, STING aggregates DU, applies the
Maguire formula, picks the next-standard pipe size that keeps the
proportional flow ≤ 0.5 d (half-full), and writes `PLM_DRN_DN`,
`PLM_DRN_SLOPE_PCT`. Stack capacities are cross-checked against
BS EN 12056-2 Table 4.

### 5.14 Vent design (DRAINAGE tab → `Plumb_VentDesign`) — §13

STING walks the drainage graph and:
- Sizes the primary stack vent (typically equal to stack DN above the
  highest branch).
- Identifies branches that need anti-siphon vents (> 6 m branch length,
  > 3 fixtures, or trap-distance violation).
- Picks the right vent size from BS EN 12056-2.

### 5.15 Invert levels (DRAINAGE tab → `Plumb_InvertLevels`) — §16

Every horizontal drainage pipe has an **upstream invert level (US-IL)**
and a **downstream invert level (DS-IL)** in mAOD (metres Above Ordnance
Datum). STING calculates these from the pipe's start and end points + the
slope, then validates that:
- Slope is between 1:80 and 1:40.
- Cover depth above the pipe (top of pipe to finished floor) is ≥ 600 mm
  (BS EN 752, BS 8000).
- Drainage is continuous downhill — no segment can have a US-IL lower
  than its upstream segment's DS-IL.

### 5.16 P-traps (DRAINAGE tab → `Plumb_InsertPTraps`) — §14

STING walks every plumbing fixture's connector graph and finds the
upstream waste path. If no trap exists between the fixture and the soil
stack, it inserts a p-trap of the right DN and orientation. The 75 mm
water seal depth (BS EN 274) is checked, and trap-to-vent distances are
audited so siphoning doesn't pull seals dry.

### 5.17 Auto-route (ROUTE tab → `Plumb_AutoRoute`) — §15

STING draws physical pipes between connected fixtures using the A*
shortest-path solver constrained to the ceiling/floor void. You'll
usually need to manually fix awkward bends and where two services cross
— auto-route gives you 80 % of the run, you do the last 20 % by hand.

### 5.18 Fix slopes (ROUTE tab → `Plumb_FixSlopes`)

For drainage pipes that came out flat or with wrong slope, STING re-applies
the project gradient default (typically 1:80 for 100 mm SAN, 1:60 for
75 mm, 1:40 for 50 mm) to every segment and re-flows the inverts
downstream.

### 5.19 Place sleeves (ROUTE tab → `Plumb_PlaceSleeves`) — §20

Where a pipe penetrates a wall, floor or fire-rated barrier, a sleeve
(slightly larger pipe sleeve, sometimes packed with fire-rated foam) is
required. STING walks every pipe-vs-barrier intersection and places the
right sleeve family with a `PEN_*` tag, ready for the firestop validator.

### 5.20 Place hangers (ROUTE tab → `Plumb_PlaceHangers`)

Pipes need supports at intervals defined by material and DN — copper
22 mm horizontal needs supports every 1.2 m; uPVC 110 mm every 1.0 m.
STING places hanger families at the right spacing along every pipe and
writes `HGR_*` parameters for the schedule.

### 5.21 Place insulation

DHW, DHWR, chilled-water and external CWS pipes need insulation per
BS 5422. STING reads pipe material + service + DN and assigns the right
thickness (e.g. 25 mm Class 0 Armaflex on copper DHW < 25 mm DN).

### 5.22 Tag everything

This is where STING earns its keep on plumbing too. **CREATE → Tag &
Combine** (`TagAndCombine`) does five things in one click:

1. Auto-detects `LOC` (building) and `ZONE` from the room.
2. Populates `DISC` (= `P` for plumbing/public health), `LVL` (level),
   `SYS` (`DCW`, `DHW`, `SAN`, `RWD`, `GAS`, `FP` from the Revit pipe
   system), `FUNC`, `PROD` (e.g. `WC`, `BAS`, `SHO`, `URN`).
3. Assigns the next available `SEQ` number per (DISC, SYS, LVL) group.
4. Builds the 8-segment tag like `P-BLD1-Z01-L02-SAN-DRN-WC-0007`.
5. Writes that tag to all 36 container parameters (`PLM_FIX_TAG`,
   `PLM_DRN_TAG`, `ASS_TAG_1` … `ASS_TAG_7`).

### 5.23 Full audit (AUDIT tab → `Plumb_FullAudit`) — §21

`PlumbingComplianceScanner` aggregates results from every sub-engine
(supply velocity, drainage slope, vent compliance, dead-legs, backflow
categories, stack capacity, trap seals, insulation coverage) into a
single Red/Amber/Green dashboard. Fix every Red before you issue.

### 5.24 Generate schedules and docs (DOCS tab)

- `Plumb_PipeSchedule` — one row per pipe with material, DN, length,
  service, level.
- `Plumb_BOQ` — bill of quantities by pipe + fitting + valve + insulation.
- `Plumb_ManholeSchedule` — manholes with size, IL, cover level.
- `Plumb_Isometric` — schematic isometric for one stack or one branch.
- `Plumb_CommPack` — commissioning pack: TMV register, pressure test
  certificates, chlorination log template.

### 5.25 Produce drawings

**DOCS → Sheet Manager** + **Drawing Type Manager** (§23). The drawing
type for a plumbing layout is `plumb-drainage-A1-1to100`; the vent riser
is `plumb-vent-riser-A3-NTS`; the pressure schedule is
`plumb-pressure-schedule-A3`.

### 5.26 Issue

**BIM → Issue Deliverable** (template engine v1.1). A cover letter,
transmittal note and revision history are rendered automatically and
e-mailed to the client.

---

## 6. Understanding the ISO 19650 tag for plumbing

The single most important concept in STING is the 8-segment tag. Memorise it.

```
DISC - LOC  - ZONE - LVL - SYS  - FUNC - PROD - SEQ
  P  - BLD1 - Z01  - L02 - SAN  - DRN  - WC   - 0007
```

| Segment | Meaning                | Plumbing examples                                                            |
|---------|------------------------|------------------------------------------------------------------------------|
| DISC    | Discipline             | `P` (plumbing / public health), `FP` (fire protection), `G` (gas)            |
| LOC     | Location / building    | `BLD1`, `BLD2`, `EXT` (external — site drainage, soakaways)                  |
| ZONE    | Zone                   | `Z01`–`Z04`, `XX` (zoneless), `RF` (roof zone for RWD)                       |
| LVL     | Level                  | `L01`, `GF`, `B1`, `RF`, `EX` (external/below-ground)                        |
| SYS     | System (CIBSE/Uniclass)| `DCW` (cold water), `DHW` (hot water), `DHWR` (hot return), `SAN` (soil), `WST` (waste), `RWD` (rainwater), `GAS` (natural gas), `FP` (fire), `IRR` (irrigation), `RWH` (rainwater harvest) |
| FUNC    | Function               | `SUP` (supply), `DRN` (drain), `VNT` (vent), `RET` (return), `STO` (storm)   |
| PROD    | Product / device class | `WC` (water closet), `BAS` (basin), `SHO` (shower), `SNK` (sink), `URN` (urinal), `BTH` (bath), `BID` (bidet), `FLD` (floor drain), `GUL` (gulley), `MH` (manhole), `IC` (inspection chamber), `TMV` (mixing valve), `VLV` (valve), `WHE` (water heater), `CAL` (calorifier), `RWH` (rainwater tank), `EXP` (expansion vessel), `TRP` (trap), `VNT` (vent terminal) |
| SEQ     | 4-digit sequence       | `0001`, `0042`, `0500`                                                       |

### Why eight segments?

Because every drawing you produce — every pipe schedule row, every BOQ
line, every commissioning certificate, every manhole entry — needs an
unambiguous link back to one piece of plumbing in the model. With 8
segments you can have hundreds of basins in one zone and still have unique
IDs.

### How STING fills in each segment

| Segment | How it's derived                                                       | What you can override                       |
|---------|------------------------------------------------------------------------|---------------------------------------------|
| DISC    | Category map (plumbing fixtures → `P`, sprinklers → `FP`)              | `Tokens → Set Discipline`                   |
| LOC     | Room name / Project Information                                        | `Tokens → Set Location`                     |
| ZONE    | Room department / name pattern                                         | `Tokens → Set Zone`                         |
| LVL     | Element's host level                                                   | (don't override — change the level)         |
| SYS     | Revit pipe system → SYS code map (DCW → `DCW`, Sanitary → `SAN`, etc.) | `Tokens → Set System`                       |
| FUNC    | System → function map (`SAN` → `DRN`, `DCW` → `SUP`, `DHWR` → `RET`)   | (rarely overridden)                         |
| PROD    | Family-name-aware lookup (35+ specific codes for plumbing)             | (rarely overridden)                         |
| SEQ     | Next available number per (DISC, SYS, LVL) group                       | `Tokens → Assign Numbers`                   |

> **Practical advice for first-timers.** Don't fight the autotagger. Place
> fixtures, place pipes, click **Tag & Combine**, and only override tokens
> when STING guesses wrong (e.g. a bespoke wash trough family that STING
> doesn't recognise as `WTR`). To override a few elements, select them,
> **CREATE → Tokens → Set ___**.

### Why SYS matters more than any other segment for plumbing

The `SYS` segment is what drives every sizing engine. The supply sizer
only operates on elements where `SYS ∈ {DCW, DHW, DHWR}`. The drainage
sizer only operates on `SYS ∈ {SAN, WST, RWD}`. If a basin's pipework is
left untagged or mis-tagged as `XX`, the supply sizer skips it, the
drainage sizer skips it, the BOQ undercounts, and your design is silently
incomplete. **Always run `Plumb_ScanFixtures` immediately after placing
fixtures, before anything else.**

---

## 7. Setting up Revit pipe systems before you place anything

Revit's plumbing engine uses **Piping Systems** to define what each pipe
*is for*. **You cannot route anything until at least one piping system
exists for each service.** First-timers trip on this constantly: they
place a basin, try to "Connect Into" a pipe, and Revit silently refuses
because the basin's `Domestic Cold Water` connector has no matching
system to attach to.

### 7.1 The piping systems you need (typical UK commercial project)

Open `Manage → MEP Settings → Mechanical Settings → Piping Systems`. For
a typical project you need:

| Piping System          | Fluid type     | Use for                                            | Default DN range |
|------------------------|----------------|----------------------------------------------------|------------------|
| Domestic Cold Water    | Water          | DCW — incoming mains to every cold outlet          | 15 – 54 mm       |
| Domestic Hot Water     | Water          | DHW — flow from calorifier to outlets              | 15 – 54 mm       |
| Domestic Hot Water Return | Water       | DHWR — return loop back to calorifier              | 15 – 28 mm       |
| Sanitary               | Sanitary       | SAN — soil from WCs + bidets                       | 100 – 150 mm     |
| Sanitary Waste         | Sanitary       | WST — waste from basins, showers, sinks            | 40 – 100 mm      |
| Sanitary Vent          | Vent           | VNT — primary + branch vents                       | 32 – 100 mm      |
| Storm Drainage         | Storm Drainage | RWD — rainwater pipes                              | 75 – 150 mm      |
| Natural Gas            | Other          | GAS — to boilers, hobs, water heaters              | 15 – 65 mm       |
| Fire Protection Wet    | Other          | FP — wet riser, sprinkler mains                    | 50 – 150 mm      |

### 7.2 Setting graphical overrides (so plans read correctly)

For each system, set:

- **Material** (used for downstream colour-by-system filters):
  - DCW → Blue
  - DHW → Red
  - DHWR → Pink / dashed
  - SAN → Olive Green
  - WST → Light Green
  - VNT → Yellow
  - RWD → Cyan
  - GAS → Magenta
  - FP → Red (solid double-line)
- **Graphical Overrides → Line Color** → match material
- **Calculations** → "All" (so Revit computes flow per fixture for you)
- **Flow Conversion Method** → "Loading Units" for supply, "Fixture Units"
  for drainage

Without this step every pipe in plan looks identical and your reviewer
cannot see at a glance which system is which.

### 7.3 STING's view-style pack does the colour-coding for you

Once `corp-base-plumbing` (a view-style pack shipped with STING) is
applied to a view, filters are auto-created that match each piping system
to the right line colour, weight and pattern. So if you let STING drive
the visual side via the Drawing Type system (§23), you don't have to set
graphical overrides per system by hand.

### 7.4 Pipe types — the material side

Where Piping Systems define "what's inside the pipe", **Pipe Types**
define "what the pipe is made of". You need pipe types for each
(service × material) combination you'll use:

| Pipe Type                       | Material           | For service     | Typical use         |
|---------------------------------|--------------------|-----------------|---------------------|
| Copper – Type B – DCW           | Copper EN 1057     | DCW             | UK cold water       |
| Copper – Type B – DHW (insulated)| Copper EN 1057    | DHW + DHWR      | UK hot water        |
| HDPE PE-100 – buried            | HDPE PE100         | DCW (buried)    | Below ground supply |
| PEX-AL-PEX                      | Composite          | DCW + DHW       | Domestic crimp      |
| uPVC – BS EN 1329               | uPVC               | SAN + WST + VNT | Above-ground waste  |
| Cast Iron – BS EN 877           | Cast Iron          | SAN (commercial)| Acoustically-rated  |
| Vitrified Clay – BS EN 295      | Clay               | SAN (buried)    | Below ground sewer  |
| Galvanised Steel – BS 1387 Medium | Mild Steel       | FP wet, GAS     | Sprinklers + gas    |
| Stainless Steel – BS EN 10312   | Stainless 316      | DCW healthcare  | Cat 5 isolation     |

STING's `STING_PIPE_MATERIALS_HYDRAULIC.json` ships per-material
Hazen-Williams C coefficients and roughness ε values for friction-loss
calculations:

| Material           | C (Hazen-Williams) | ε (mm)     | Max velocity (m/s) |
|--------------------|--------------------|----------:|--------------------|
| Copper Type B      | 140                | 0.0015     | 1.5 cold / 1.0 hot |
| HDPE PE-100        | 150                | 0.007      | 2.5                |
| Stainless 316      | 145                | 0.0015     | 1.5 cold / 1.0 hot |
| Galvanised Steel   | 120                | 0.15       | 1.5                |
| uPVC drainage      | n/a (gravity)      | 0.06       | 4.0 max            |
| Cast Iron drainage | n/a (gravity)      | 0.25       | 4.0 max            |
| Vitrified Clay     | n/a (gravity)      | 0.30       | 3.5 max            |

> **Why this matters.** When you click `Plumb_SizeSupply`, STING uses the
> pipe material to pick the right C value. A 22 mm HDPE branch carries
> noticeably more than a 22 mm galvanised branch because of friction.
> Wrong material → wrong size → wrong pressure at the outlet.

---

## 8. Placing plumbing fixtures

A *plumbing fixture* is any sanitary appliance that takes water in or
sends waste out: WCs, basins, showers, baths, sinks, urinals, bidets,
floor drains, gulleys, dishwashers, washing machines, drinking fountains,
emergency eye-wash stations, lab fume taps, hose bibcocks.

### 8.1 The two essential things every fixture needs

Whatever family you place, two things must be true for STING to do
anything useful:

1. **Its connectors must point at the right Piping Systems.** A basin
   needs a `Domestic Cold Water` connector and a `Sanitary` connector
   (and a `Domestic Hot Water` connector if it's a mixer). If a connector
   isn't typed, Revit can't auto-route and STING can't tag the SYS.
2. **It must declare its DU + LU.** STING's `FixtureUnitScanner` reads
   each fixture's family-name pattern (e.g. "WC", "Basin", "Shower") and
   stamps `PLM_DRN_DU` and `PLM_SUP_LU` from `STING_PLUMBING_DRAINAGE_TABLES.json`
   and `STING_PLUMBING_SUPPLY_TABLES.json`. If the family name doesn't
   match a known pattern, the scanner falls back to "Generic = 1 LU + 2 DU"
   and warns.

### 8.2 Standard fixture loading values (BS EN 12056-2 + BS EN 806-3)

These are baked into STING's tables and consumed by every sizing engine:

| Fixture              | Drainage DU | Cold LU | Hot LU | WSFU |
|----------------------|------------:|--------:|-------:|-----:|
| WC (6-litre flush)   | 14          | 2.0     | —      | 5    |
| WC (4-litre flush)   | 10          | 1.5     | —      | 3.5  |
| Basin (single tap)   | 3           | 0.5     | 0.5    | 1    |
| Basin (mixer)        | 3           | 0.7     | 0.7    | 1.5  |
| Shower               | 9           | 3.0     | 3.0    | 4    |
| Bath                 | 18          | 4.0     | 4.0    | 4    |
| Sink, kitchen        | 18          | 3.0     | 3.0    | 3    |
| Sink, cleaner's      | 18          | 3.0     | 3.0    | 3    |
| Urinal (waterless)   | 0           | 0       | —      | 0    |
| Urinal (cistern, ea) | 9           | 0.5     | —      | 1    |
| Bidet                | 3           | 0.5     | 0.5    | 1    |
| Wash trough (per m)  | 12          | 2.0     | 2.0    | 2    |
| Drinking fountain    | 1           | 0.3     | —      | 0.5  |
| Floor drain (75 mm)  | 12          | —       | —      | —    |
| Dishwasher (domestic)| 6           | —       | 1.0    | 2    |
| Dishwasher (commercial)| 30        | —       | 5.0    | 10   |
| Washing machine      | 12          | 1.5     | 1.5    | 4    |
| Eye-wash (emergency) | 3           | 5.0     | —      | 5    |
| Emergency shower     | 24          | 15.0    | —      | 20   |

### 8.3 How to place them

Use **MODEL → MEP → Place Fixture** with a plumbing-fixture family:

1. **MODEL → Place Fixture** → pick the family (`STING - WC - Wall-hung`,
   `STING - Basin - Pedestal`, etc.).
2. Click on the wall (wall-hosted) or floor (floor-hosted).
3. Tag with **CREATE → Auto Tag** (or wait for the batch tag in §5.22).

### 8.4 Smart-place (the rule-based engine)

For repetitive layouts (a 10-cubicle WC block, a 24-bed ward, a
schoolroom), `Placement_PlaceFixtures` reads
`STING_PLACEMENT_RULES.json` and lays out fixtures by rule. The rule
pack ships with sensible defaults:

- One basin per WC cubicle, mounted 600 mm from cubicle entry, 800 mm
  AFFL.
- One floor drain per shower cubicle.
- One floor drain per WC block + per cleaner's room.
- One drinking fountain per 1000 m² of office (BS 6465-1).
- One emergency eye-wash per chemical lab.

You can edit the rules JSON or write project overrides at
`<project>/_BIM_COORD/placement_rules.json`.

### 8.5 Family conformance (Phase 185 — check before bulk-placing)

If you've been given a vendor's family library (e.g. an Armitage Shanks
or Geberit set), run **TAGS → Family Conformance Check**
(`FamilyConformanceCheck`) before placing anything. The checker scores
each `.rfa` against the STING contract:

- 4 placement params bound by GUID (PLACE_ANCHOR, OFFSET_X_MM,
  OFFSET_Y_MM, PLACE_SIDE).
- Connectors point at correct services (DCW + DHW + SAN for a mixer
  basin).
- Tag-style matrix exists for the right disciplines.
- Connector positions equal actual termination points (not 50 mm inside
  the body).

Result: PASS (≥ 85), WARN (70–84), BLOCK (< 70). A CSV lands in
`<project>/_BIM_COORD/` listing every family with its score, the top-10
worst families surfaced in a TaskDialog. **Run this before stamping
vendor libraries** — it catches the "manufacturer family uses
'Mounting Height' instead of `MNT_HGT_MM`" failure before it costs you
a transaction.

### 8.6 UK mounting-height reference

Defaults baked into STING's plumbing families (override per project under
`<project>/_BIM_COORD/mounting_heights.json`):

| Element                          | Height AFFL (mm) | Standard            |
|----------------------------------|------------------|---------------------|
| WC pan (front of bowl)           | 0 (host)         | BS 6465-2           |
| WC cistern (top)                 | 1300             | BS 6465-2           |
| Basin (rim, standard)            | 850              | BS 6465-2           |
| Basin (rim, accessible Part M)   | 720–740          | BS 8300             |
| Shower head                      | 2000             | BS 6465-2           |
| Shower mixer valve               | 1100             | BS 6465-2           |
| Bath rim                         | 510              | BS 6465-2           |
| Sink, kitchen rim                | 900              | BS 6465-2           |
| Urinal lip                       | 600              | BS 6465-2           |
| Drinking fountain spout          | 1000             | BS 6465-1           |
| Hose bibcock (external)          | 600              | BS 6700             |
| Emergency eye-wash spout         | 850–1150         | BS EN 15154-2       |
| Emergency shower head            | 2100             | BS EN 15154-1       |
| Floor gulley                     | 0 (host floor)   | BS EN 1253          |
| Isolation valve (accessible)     | 1500             | BS 8558             |
| Pressure test point              | 1500             | BS 8558             |

---

## 9. Cold water supply sizing — Hunter's method and BS EN 806-3

### What it is

Cold-water sizing is the process of choosing a pipe diameter for each
branch and main that's just big enough to deliver enough flow at the
right pressure, without being so big that water sits stagnant in the
pipe.

### Why it matters

Get it too small and:
- Outlets run dry when several taps are open.
- Velocity exceeds 1.5 m/s → noise + erosion of copper.
- Hot water temperature drops (the cold side is starving the mixer).

Get it too big and:
- Water sits in the pipe long enough to grow Legionella.
- Excessive material cost.
- Cold water arrives lukewarm because it picked up ambient heat.

### The two algorithms STING uses

**Hunter's method** (probable simultaneous demand) for buildings with
intermittent fixtures (WCs, basins, showers in offices, hotels, homes).
Hunter recognised that not every fixture is on at once — in a 100-WC
office building maybe 8 are flushing simultaneously at peak. He produced
a statistical curve relating *total fixture units* (LU sum) to *probable
simultaneous flow* (L/s).

**BS EN 806-3 (continuous flow)** for buildings with continuous demand
(industrial, laundries, swimming pool fills). Each fixture contributes
its rated demand without diversity.

STING's `WaterSupplySizer` applies Hunter first, switches to BS EN 806-3
when a project flag is set, and uses the higher of the two when fixtures
mix.

### Hunter's LU curve (excerpt, baked into STING)

| Σ LU | Probable simultaneous flow (L/s) | Min pipe DN (Copper Type B) |
|-----:|----------------------------------:|----------------------------:|
| 1    | 0.1                              | 15                           |
| 5    | 0.25                             | 15                           |
| 10   | 0.4                              | 22                           |
| 25   | 0.7                              | 22                           |
| 50   | 1.1                              | 28                           |
| 100  | 1.7                              | 35                           |
| 200  | 2.6                              | 42                           |
| 400  | 4.0                              | 54                           |
| 800  | 6.0                              | 67                           |
| 1500 | 9.0                              | 76                           |

### The four tests every cold-water pipe must pass

A pipe size is **acceptable** only when all four are true:

| Test                | Inequality                            | Why                                            |
|---------------------|---------------------------------------|------------------------------------------------|
| Velocity            | `v ≤ 1.5 m/s` (cold), `1.0 m/s` (hot)| Noise and erosion limit per BS 6700            |
| Friction            | `dp ≤ 200 Pa/m` typical               | Cumulative head loss to outlet                  |
| Pressure at outlet  | `P_outlet ≥ P_min`                    | Min outlet pressure (1.0 bar typical, 1.5 bar shower) |
| Continuity          | branch ≤ main                         | Branches never larger than their feeder         |

### How to do it in STING

1. **SUPPLY → `Plumb_ScanFixtures`** — assigns DU/LU to every fixture
   (you should have done this in §5.8).
2. **SUPPLY → `Plumb_SizeSupply`** — STING walks the supply graph from
   the source (incoming main) and aggregates LU at every node. For each
   segment, it picks the smallest DN where all four tests pass, writes
   `PLM_SUP_DN`, `PLM_SUP_VEL_MS`, `PLM_SUP_DP_PA_M`, `PLM_SUP_FLOW_LS`,
   and reports back per-system.
3. **SUPPLY → `Plumb_PressureCheck`** — walks every path from origin to
   each outlet, sums static head + friction loss, reports residual
   pressure at each outlet. Outlets below their minimum spec are flagged
   in the audit.

### Scenario — 4-storey office with 60 staff

Per BS 6465-2 the toilet provision for 60 mixed staff is:
- 4 WCs (1 per 15 + 1 unisex accessible)
- 4 basins (matching ratio)
- 2 urinals (for the male WC block)
- 1 cleaner's sink
- 1 tea-point sink per floor (4 total)
- 1 drinking fountain per floor

Fixture LU sum (cold):
- 4 WCs × 2.0 = 8.0
- 4 basins × 0.7 = 2.8
- 2 urinals × 0.5 = 1.0
- 1 cleaner's sink × 3.0 = 3.0
- 4 tea-point sinks × 3.0 = 12.0
- 4 fountains × 0.3 = 1.2
**Total = 28 LU**

Hunter at 28 LU → probable flow ≈ 0.75 L/s.

Required main DN (Copper Type B, max 1.5 m/s):
- At DN 28 mm: cross-sectional area ≈ 615 mm² → velocity = 0.75/0.000615 ≈ 1.22 m/s ✓
- Friction at DN 28: ≈ 180 Pa/m ✓

**STING picks DN 28 mm for the incoming main**. Above floor level you
branch down (28 → 22 → 15) as LU diminishes per floor.

---

## 10. Hot water, TMVs, dead-legs, and recirculation

### 10.1 What hot water adds to cold water

Hot water systems sit on top of cold-water sizing with three extra
worries:

1. **Hot water generation** — calorifier (indirect cylinder), electric
   storage, instantaneous heater, heat pump. Sized by **peak hour demand**
   + **recovery rate**.
2. **Dead-legs** — pipework from the DHW main to the outlet. If a tap is
   used rarely (e.g. a guest WC), water sits in the dead-leg cooling +
   stagnating. **HTM 04-01** says < 1 m for healthcare; **BS 8558** says
   < 5 m for commercial; **good practice** says < 2 m always.
3. **Legionella risk** — *Legionella pneumophila* multiplies fastest
   between 20 °C and 50 °C. Hot water must reach the *furthest tap* at
   ≥ 55 °C; the return loop must come back at ≥ 55 °C. Cold water must
   stay below 20 °C (challenging in summer if pipes share risers with hot
   water).

### 10.2 Thermostatic Mixing Valves (TMVs)

A TMV blends hot and cold to a safe outlet temperature (BS EN 1287).
Required:

- At every shower (max 41 °C — scald limit for a healthy adult).
- At every wash basin in care, healthcare, schools (max 38 °C — child /
  elderly scald limit).
- At every bath in care + healthcare (max 43 °C).
- Domestic: optional but increasingly standard.

Two grades:

| Grade | Where used                            | Failsafe direction         | Inspection frequency |
|-------|---------------------------------------|----------------------------|----------------------|
| TMV2  | Domestic, commercial offices, schools | Fails cold (no hot)        | Annual               |
| TMV3  | Healthcare, care homes, hostels       | Fails cold + alarm on fail | Quarterly + annual full strip-down |

### 10.3 STING's TMV register

**SUPPLY → `Plumb_TMVRegister`** walks every TMV family in the project
and writes:
- `PLM_TMV_SET_TEMP_C` — commissioned outlet temperature.
- `PLM_TMV_GRADE_TXT` — TMV2 or TMV3.
- `PLM_TMV_LOCATION_TXT` — room + level.
- `PLM_TMV_LAST_TEST_DATE` — last inspection date.

Exports to a CSV the FM team holds for the building's life. Healthcare
projects export to the same CSV the HTM 04-01 compliance dashboard
consumes.

### 10.4 Dead-leg detection

**SUPPLY → (run `Plumb_FullAudit` from AUDIT tab)** or call
`DeadLegDetector` directly via the workflow engine. STING walks the DHW
+ DHWR graph, computes the length of every branch from its tee on the
main to the outlet, and flags branches over the project threshold:

- Project default: 2.0 m
- Healthcare project (`PRJ_ORG_HEALTH_PACK_PROFILE_TXT` set): 1.0 m
- BS 8558 commercial: 5.0 m

Output: CSV with one row per offending branch — location, length,
recommended fix (extend the return, add a swept tee, relocate the
outlet closer to the main).

### 10.5 DHW recirculation loop balancing

In commercial / multi-storey buildings, hot water is fed *from the
calorifier* to each floor and *returned* via a DHWR loop so any tap
draws hot water immediately. Without recirculation you'd waste 30+
seconds of cold water at each outlet.

A **balanced** recirc loop has flow distributed so every floor's return
arrives at ≥ 55 °C. Unbalanced loops cool down at the furthest floors.
STING's `RecircLoopBalancer`:

1. Walks the DHWR loop to identify each branch.
2. Computes the *heat loss* per branch from pipe DN, length and
   insulation thickness (BS 5422).
3. Calculates the *required circulation flow* per branch to keep return
   temp ≥ 55 °C with calorifier flow at 65 °C.
4. Writes `PLM_RECIRC_FLOW_LS` to each branch.
5. Sizes the recirculation balancing valves (typically `DRV` or
   `STAD` valves) to the right Kvs.

Result: a balancing schedule listing every BV with its required flow
and Kvs setting, which the commissioning engineer dials in on site.

### Scenario — 50-bed hospital ward DHW

50 bedhead basins each with TMV3 set to 38 °C, calorifier flow at
65 °C, return target 60 °C.

- Σ LU = 50 × 0.7 = 35 LU (Hunter) → probable demand 0.85 L/s.
- DHW flow main DN 35 mm copper.
- DHWR loop DN 22 mm with 25 mm Class 0 Armaflex insulation.
- Heat loss per 10 m of insulated DN 22 = approx. 25 W.
- For a 200 m loop → 500 W total heat loss.
- Required recirc flow `Q = P / (ρ·cp·ΔT) = 500 / (1000 × 4180 × 5)
  ≈ 0.024 L/s` per loop.
- STING sizes the DRV at Kvs 0.6 with a 12 kPa drop at design flow.

STING reports: "Loop 50-bed-DHWR: balanced at 0.024 L/s, return temp
predicted 60.2 °C — PASS".

---

## 11. Expansion vessel sizing

### What it is

When you heat water, it expands by about 4 % between 10 °C and 80 °C. In
a **sealed system** (no open expansion vent to atmosphere), that extra
volume has nowhere to go — so you fit a **pressurised expansion vessel**
with a rubber diaphragm and a nitrogen pre-charge on one side. The water
pushes the diaphragm in, compressing the gas, accommodating the volume
increase without bursting a pipe.

### Why it matters

Skip it and:
- Pressure relief valve dumps water every heating cycle.
- The system slowly loses charge until cold-fill pressure drops.
- Make-up valve opens, brings fresh oxygenated water → corrosion.
- Eventually a pipe joint lets go from pressure cycling fatigue.

### The math (BS 7074-1)

```
V_vessel = (V_system × E) / [1 - (P_initial + 1) / (P_final + 1)]
```

| Term         | Meaning                                  | Typical                          |
|--------------|------------------------------------------|----------------------------------|
| V_vessel     | Vessel total volume (L)                  | Output                            |
| V_system     | Total water content of sealed system (L) | Boiler + cylinder + pipes         |
| E            | Expansion factor (cold → hot)            | 0.041 at 80 °C; 0.017 at 50 °C    |
| P_initial    | Cold-fill pressure (bar gauge)           | 1.5 bar typical                   |
| P_final      | Max pressure (= PRV setting – 0.5 bar)   | 2.5 bar for 3-bar PRV             |

### How to do it in STING

1. **SUPPLY → `Plumb_ExpVessel`**.
2. Pick the system (e.g. DHW from calorifier-1).
3. Enter: cold-fill pressure (1.5 bar default), PRV setting (3 bar
   default), max system temperature (65 °C default).
4. STING:
   - Walks the connected pipework, sums pipe volume (πr²L per segment).
   - Adds calorifier nominal capacity from `PLM_CAL_CAPACITY_L`.
   - Applies BS 7074-1 expansion factor for the temperature delta.
   - Picks the next-standard vessel size from a vendor catalogue
     (Flamco, Reflex, Zilmet).
5. Writes `PLM_EXP_VESSEL_L` to the vessel family and reports back:
   "Required 24 L; selected Reflex N25 (25 L)".

### Scenario — domestic combi-boiler system

Combi boiler with 8 L primary content, 50 m of 22 mm copper (volume 13 L)
+ 30 m of 15 mm copper (4 L) — `V_system = 25 L`.

- Cold fill 1.0 bar, PRV 3 bar → P_final = 2.5 bar
- Temperature swing 10 → 80 °C → E = 0.041

`V_vessel = (25 × 0.041) / [1 - (2.0 / 3.5)] = 1.025 / 0.428 = 2.4 L`

STING picks **3 L** (next standard) vessel.

---

## 12. Drainage sizing — DU, slope, fall and rise

### What it is

Drainage sizing picks pipe diameters and gradients for the soil + waste
+ vent system. Unlike supply, drainage works by **gravity** — no pumps,
just slope and atmosphere.

### Why it matters

Get it too small and:
- Pipes block on solid loads (especially WC paper).
- Stack overflows back into the lowest fixture.
- Plumes of foul air vent backwards through traps.

Get it too big and:
- Slow-moving water deposits solids (the pipe self-fouls).
- Excessive cost.

Get the slope wrong:
- Too shallow (< 1:80) → solids strand.
- Too steep (> 1:40) → liquid runs ahead of solids, again solids strand.

### The math — Maguire's formula (UK standard, BS EN 12056-2)

For horizontal drainage pipes flowing partially full:

```
Q = K × DN^(8/3) × √(i)
```

| Term  | Meaning                                  | Typical                       |
|-------|------------------------------------------|-------------------------------|
| Q     | Flow capacity (L/s)                      | Output                         |
| K     | Maguire constant                         | 0.000087 for half-full pipe    |
| DN    | Pipe nominal diameter (mm)               | 32 / 40 / 50 / 75 / 100 / 150  |
| i     | Slope (m/m, so 1:80 = 0.0125)            | 0.025 (1:40) to 0.0125 (1:80)  |

The pipe is sized to run at **half-full at design flow** so air can
travel above the water and pressure surges don't pull traps dry.

### Discharge Unit (DU) table (BS EN 12056-2 Appendix A)

DU is a dimensionless number representing each fixture's contribution to
drainage flow. Sum DU per branch + per stack, look up corresponding
flow in BS EN 12056 Table 4, size the pipe to carry it.

(See §8.2 for the per-fixture DU table — it's the same data.)

### Slope rules per pipe DN (UK practice)

| DN (mm)  | Min slope | Max slope | Typical | Use for                       |
|----------|-----------|-----------|---------|-------------------------------|
| 32–40    | 1:40      | 1:18      | 1:40    | Basin, bidet wastes           |
| 50       | 1:50      | 1:30      | 1:40    | Sink, shower wastes           |
| 75       | 1:60      | 1:30      | 1:40    | Multi-basin branch, urinal    |
| 100      | 1:80      | 1:40      | 1:60    | WC branch, main soil drain    |
| 150      | 1:150     | 1:60      | 1:100   | Public-health main drain      |

### Stack capacity (BS EN 12056-2 Table 4 — System III, UK default)

Vertical drainage stacks can only carry so much before they "jump-flow"
(water bridges the stack and the pipe runs full), at which point air
gets sucked from below and pulls traps:

| Stack DN | Max DU |
|---------:|-------:|
| 50       | 28     |
| 75       | 60     |
| 100      | 240    |
| 125      | 540    |
| 150      | 960    |

When you exceed the limit, you either:
- Upsize the stack.
- Add a **stack vent** to prevent siphoning.
- Split the load across two stacks.

STING's `StackCapacityValidator` audits every stack against these limits.

### How to do it in STING

1. **DRAINAGE → `Plumb_SizeDrainage`** — walks every drainage branch
   and stack, aggregates DU at each node, applies Maguire (horizontal)
   or BS EN 12056-2 Table 4 (vertical), picks the next-standard pipe
   size, writes `PLM_DRN_DN`, `PLM_DRN_SLOPE_PCT`, `PLM_DRN_DU`.
2. **DRAINAGE → `Plumb_InvertLevels`** (§16) — computes US/DS inverts
   in mAOD, validates slope continuity.
3. **ROUTE → `Plumb_FixSlopes`** — bulk-fixes flat or wrong-slope
   pipes to the project default slope per DN.

### Scenario — 50-bed hospital ward drainage

50 bedhead basins (each 3 DU) + 25 WCs (each 14 DU) + 10 showers (each
9 DU) + 10 cleaners' sinks (each 18 DU) on one stack:

- Σ DU = 150 + 350 + 90 + 180 = **770 DU**

From BS EN 12056-2 Table 4:
- 770 DU > 540 (DN 125 limit), ≤ 960 (DN 150 limit) → **stack DN 150**

For the horizontal main collecting two such stacks (Σ 1540 DU at peak
diversity 0.6 → 924 DU equivalent), slope 1:100 (i = 0.01):

`Q = 0.000087 × 150^(8/3) × √0.01 = 0.000087 × 78,400 × 0.1 = 0.68 L/s`

At 924 DU, BS EN 12056 gives ≈ 7.5 L/s required → so DN 150 at 1:100 is
**undersized**. Need DN 200 buried-clay drain at 1:80 (Q = 2.5 L/s
half-full × 4 = OK).

STING reports: "Main drain: required Q 7.5 L/s, DN 150 at 1:100 capacity
0.68 L/s — FAIL. Recommend DN 200 at 1:80 (capacity 8.2 L/s)".

---

## 13. Vent design — primary, secondary, anti-siphon

### What it is

A vent is a pipe open to atmosphere at the top that allows air to enter
or leave the drainage system as water flows down it. Without a vent,
discharging one fixture creates a vacuum that sucks the water seal out
of another fixture's trap, letting sewer gas into the building.

### Why it matters

The smell of sewer gas (a mix of methane, hydrogen sulphide and ammonia)
is unmistakable. Unvented systems guarantee complaints. Worse, in
healthcare or food-processing buildings, ingress of pathogens through
broken trap seals can sicken people.

### Three kinds of vents

| Type             | Where                                    | Standard            |
|------------------|------------------------------------------|---------------------|
| Primary stack vent | Top of every soil stack, through roof  | BS EN 12056-2       |
| Branch vent      | On a long branch (> 6 m or 3+ fixtures) | BS EN 12056-2       |
| Anti-siphon vent | Loop vent on a single fixture's trap   | BS EN 12056-2       |

### Sizing rules

| Stack DN | Primary vent DN (above highest branch) |
|---------:|---------------------------------------:|
| 100      | 100                                    |
| 75       | 75                                     |
| 50       | 50                                     |
| 32–40    | Loop or air admittance valve (AAV)     |

Branch vents are typically one size smaller than the branch but never
less than DN 32. Anti-siphon vents are DN 32 minimum.

### Air Admittance Valves (AAVs)

In retrofits where a roof penetration is impractical, **AAVs** (e.g.
Studor Maxi-Vent, Durgo) replace the vent terminal. They open under
suction (admitting air) and close under positive pressure (preventing
sewer gas escape). Permitted by BS EN 12380 in single-stack systems
serving up to 8 storeys, but **not on the primary stack** of a building
> 4 storeys without engineering justification.

### How to do it in STING

1. **DRAINAGE → `Plumb_VentDesign`** — STING walks the drainage graph
   and:
   - Identifies the highest fixture on each stack → primary vent
     starts above it.
   - Identifies branches > 6 m or > 3 fixtures → flags for branch
     venting.
   - Audits each trap's distance from the nearest vent (max distance
     per BS EN 12056-2 = 2 m for 32 mm trap, 3 m for 50 mm trap).
   - Sizes each vent from BS EN 12056-2 + writes `PLM_VENT_DN`.
2. Vent terminations on the roof get a **vent cowl** family placed
   500 mm above roof level, 3 m from any opening window or air intake.

### Scenario — long basin branch

A 4-basin wash-up trough drain at 50 mm × 8.5 m to the stack with no
vent.

- Trap-to-vent distance for DN 50 = max 3 m.
- 8.5 m run > 3 m → STING flags **siphon risk**.
- Recommends an anti-siphon vent at the upstream end, DN 32, connected
  back into the stack vent (or terminated with an AAV).

STING auto-places the vent if the family is loaded; otherwise reports
the issue and the recommended location.

---

## 14. P-traps — what they do and why STING inserts them for you

### What it is

A **p-trap** (or s-trap, or bottle trap — different shapes, same job) is
a U-bend below every fixture. It holds a water seal (typically 75 mm
deep, BS EN 274) that physically separates the building from the sewer.

### Why it matters

No trap → sewer gas vents through your basin. Trap depth too shallow →
seal lost to evaporation in 2 weeks of disuse. Trap with no vent at the
right distance → seal lost to siphoning when something else discharges.

### Trap requirements (BS EN 274)

| Fixture          | Trap type   | Min seal depth | Min trap DN |
|------------------|-------------|---------------:|------------:|
| WC               | Integral S-trap | 50 mm      | 100         |
| Basin            | P-trap      | 75 mm          | 32          |
| Sink             | P-trap      | 75 mm          | 40          |
| Shower           | Bottle / P  | 50 mm          | 40          |
| Bath             | P-trap      | 50 mm          | 40          |
| Floor drain      | Integral    | 50 mm          | 50          |
| Urinal           | Bottle / S  | 50 mm          | 32          |

### How STING places them automatically

**DRAINAGE → `Plumb_InsertPTraps`** — `PTrapInserter` is idempotent
(safe to re-run). It:

1. Walks every plumbing fixture's connector graph.
2. Finds the upstream waste path (fixture → waste pipe → branch).
3. Checks for an existing trap family on that path.
4. If none, picks the right seed family from `STING_PIPE_MATERIALS_RULES.json`
   (e.g. `STING - P-Trap - 32 mm` for a basin, `STING - Bottle Trap - 32 mm`
   for a tight-space basin).
5. Inserts it at the right orientation between the fixture's waste
   connector and the start of the branch.
6. Writes `PLM_TRAP_TYPE_TXT` and `PLM_TRAP_DEPTH_MM` to the trap and
   tags it with `PROD = TRP`.

The Phase 185 seed family resolver picks the right size + type from a
project's loaded library; if no matching family is loaded, STING warns
and lists the missing families for the next time `LoadFamily` is run.

### Anti-siphon distance check

After p-trap insertion, STING walks each trap → vent path and validates:

| Trap DN | Max distance to vent | Min slope on connection |
|---------|---------------------:|------------------------:|
| 32      | 2.0 m                | 1:40                    |
| 40      | 2.5 m                | 1:40                    |
| 50      | 3.0 m                | 1:50                    |
| 75      | 4.0 m                | 1:60                    |
| 100     | 6.0 m                | 1:80                    |

Out-of-spec traps are flagged in the audit with a recommended fix
(usually "add an anti-siphon vent at trap").

---

## 15. Auto-routing — Plumb_AutoRoute and manual fill-in

### What it does

`Plumb_AutoRoute` finds the shortest physical pipe route between
connected fixtures (or between a fixture and a riser/branch) using an
A* shortest-path solver constrained to the floor void or ceiling void
chosen by the user.

### Why you still need to do work by hand

Auto-route is greedy. It:
- Doesn't know about *aesthetic* runs (some buildings want pipes
  visually aligned with structural grids).
- Tries to route through ductwork and structural beams.
- Doesn't know about *future* maintenance access requirements.
- Doesn't always pick a pipe route a real plumber would install.

So auto-route gives you 70–80 % of the run, and you do the last 20–30 %
manually.

### How to do it in STING

1. Make sure fixtures are placed (§8) and Revit pipe systems exist (§7).
2. **ROUTE → `Plumb_AutoRoute`** — pick:
   - The pipe system (DCW, DHW, SAN, RWD, GAS).
   - The pipe type (Copper Type B, uPVC, etc.).
   - The route zone (Ceiling Void, Floor Void, Above Roof, In Wall).
   - The max slope (for drainage; usually leave at project default).
3. STING walks every fixture with a matching connector and draws a pipe
   from it to the nearest matching distribution point (riser, branch,
   incoming main).
4. Validate visually — fly through the model in 3D, check the runs
   make sense.
5. Manually fix:
   - Long runs that cross structural beams.
   - Awkward bends where two services try to occupy the same space.
   - Places where the auto-route picked a long way around.

### Fix slopes after auto-routing

`Plumb_FixSlopes` re-applies the project default gradient per DN to
every drainage pipe and re-flows inverts downstream. Run it after every
manual edit to keep slopes consistent.

---

## 16. Invert levels — mAOD, cover depth, slope continuity

### What it is

Every drainage pipe has:
- **Upstream invert level (US-IL)** — the height of the inside *bottom*
  of the pipe at its start.
- **Downstream invert level (DS-IL)** — same, at its end.
- **Slope** — `(US-IL – DS-IL) / horizontal length`.

Inverts are measured in **mAOD** (metres Above Ordnance Datum — the UK's
sea-level baseline) or sometimes as **relative levels** (mRL) from a
project datum.

### Why it matters

When the contractor builds the drainage, they dig trenches to fit each
pipe at the right depth. If your model has pipes at random elevations
with inconsistent slopes, the trench can't be dug — the upstream end of
pipe B would have to start *below* the downstream end of pipe A, which is
physically impossible.

### What STING checks

`InvertLevelEngine` computes invert levels for every drainage pipe and
audits:

1. **Slope range** — between 1:80 (or DN-specific min) and 1:40.
2. **Continuity** — `IL_upstream_of_next_pipe == IL_downstream_of_this_pipe`
   (the pipe doesn't "jump down" at a manhole unless designed to via a
   backdrop).
3. **Cover depth** — finished floor level – top of pipe ≥ 600 mm
   (BS 8000 / Building Regs Part H minimum).
4. **Connection levels at manholes** — every inlet to a manhole has its
   IL ≥ the outlet IL; this is a basic geometric check that's a
   surprising number of designs fail.

Outputs written per pipe:
- `PLM_DRN_INV_US_M` — upstream invert (mAOD).
- `PLM_DRN_INV_DS_M` — downstream invert (mAOD).
- `PLM_DRN_SLOPE_PCT` — slope as percentage.

### How to do it in STING

1. Place drainage pipes (auto-routed or manual).
2. Set the project base level / mAOD reference (Project Information →
   `PRJ_DATUM_M`). The wizard does this for you on Day 1.
3. **DRAINAGE → `Plumb_InvertLevels`** — STING computes and stamps
   every pipe + audits.
4. Fix red entries in the audit:
   - Continuity break → drop the upstream pipe to match.
   - Slope < min → re-route or accept a longer horizontal run.
   - Cover < 600 mm → the architect needs to drop the slab, or you
     re-route deeper.

### Scenario — site drainage from a 2-storey building

Building footprint 30 m × 20 m. Discharge from ground-floor WCs to the
public sewer in the road, 25 m away. Manhole at building edge MH-1,
manhole at street MH-2.

- Building drainage exits at MH-1 with IL 98.50 mAOD (slab 99.20 mAOD,
  cover depth 0.7 m).
- Street MH-2 has IL 97.80 mAOD (specified by water utility).
- Horizontal distance MH-1 → MH-2 = 25 m.
- Slope = (98.50 – 97.80) / 25 = 0.028 = 1:36.

STING flags: slope 1:36 exceeds max 1:40 for DN 150.

**Fix**: either accept a steeper drop with a **backdrop manhole** at
MH-1 (slope re-set 1:80 → IL 98.50 – 25 × 0.0125 = 98.19, backdrop
0.39 m), or upsize to DN 200 (max slope still 1:40 — same problem),
or lay at 1:40 (98.50 → 97.875, then 0.075 m backdrop into MH-2).

---

## 17. Backflow protection — BS EN 1717 categories and air gaps

### What it is

Backflow is when contaminated water flows *back* up a supply pipe into
the main, contaminating the wholesome water supply. It happens during
pressure drops (mains burst, fire-fighting drawdown) or when a contaminant
source is connected to the main without protection (e.g. a hose left in
a swimming pool).

### Why it matters

In 1981, a paint factory accidentally backsiphoned 200 kg of paint into
the Liverpool water main. Whole boroughs got coloured water. The legal
duty in the UK is **Regulation 4 of the Water Supply Regulations 1999**
(updated 2018) — non-compliance is a criminal offence.

### BS EN 1717 fluid categories

| Cat | Risk            | Examples                                                | Min protection                       |
|----:|-----------------|---------------------------------------------------------|--------------------------------------|
| 1   | Wholesome       | The supply itself                                       | None                                  |
| 2   | Aesthetic       | Hot water from a domestic cylinder                      | None (taste/smell only)               |
| 3   | Chemical        | Domestic dishwashers, washing machines, garden hose     | Double check valve (DCV), AB or AUK1 air gap |
| 4   | Toxic           | Boilers with antifreeze, commercial chemical wash, irrigation | RPZ valve, AUK2 air gap        |
| 5   | Serious health  | WC pan, clinical wash basin, sluice, mortuary, dental cuspidor | RPZ valve, AUK3 air gap         |

### Air gap classifications (BS EN 1717)

| Type  | Description                                       | Use for             |
|-------|---------------------------------------------------|---------------------|
| AA    | Unrestricted air gap (cistern with overflow)      | Cat 5 — supply tanks |
| AB    | Air gap with non-circular overflow                | Cat 5 — F&E tanks   |
| AUK1  | Air gap at tap above appliance spillover          | Cat 3 — kitchen sink |
| AUK2  | Tap > 20 mm above spillover, restricted use       | Cat 4 — basin       |
| AUK3  | Tap > 300 mm + 2× pipe DN above spillover         | Cat 5 — WC pan, clinical sink |

### Mechanical backflow devices

| Device | Protects to | Where used                            |
|--------|-------------|---------------------------------------|
| Single check valve | Cat 2 | Hot side at TMVs                       |
| Double check valve (DCV) | Cat 3 | Domestic boiler fill, garden tap |
| RPZ (Reduced Pressure Zone) | Cat 4-5 | Commercial chemical dosing, healthcare |
| Verifiable backflow preventer | Cat 4 | High-rise common feed             |

### How STING classifies and audits backflow

`BackflowClassifier` walks every outlet and:

1. Reads the fixture's room → infers contamination risk:
   - Generic office WC → Cat 3
   - Generic basin → Cat 3
   - Catering kitchen sink → Cat 4 (chemical wash + grease)
   - Lab sink → Cat 5
   - Mortuary sink → Cat 5
   - Clinical wash basin (healthcare project) → Cat 5
   - Hose bibcock → Cat 4 (Reg 99 — never less)
2. Walks upstream from the outlet to the first backflow device.
3. Compares: is the device's rating ≥ the fluid category?
4. Writes `PLM_BACKFLOW_CAT` to the outlet.
5. Flags non-compliant outlets in the audit with a recommendation:
   "Clinical basin in Room 2.07: Cat 5 required; DCV in place (Cat 3) —
   replace with RPZ".

### Healthcare-specific: augmented care

In wards housing immunocompromised patients (oncology, transplant, ICU,
neonatal), **HTM 04-01 Part B** requires:
- Every clinical wash basin tap: **RPZ + Cat 5 air gap**.
- Augmented-care outlets: thermostatic mixing at point-of-use with
  daily flushing logged.
- No dead legs > 1 m.
- No mixing of supply pipes between different care zones.

STING's healthcare pack auto-flags these when
`PRJ_ORG_HEALTH_PACK_PROFILE_TXT` is set to `FULL` or `ACUTE`.

---

## 18. Storm, roof drainage, SuDS, rainwater harvesting

### 18.1 What rainwater design covers

Three categories:

1. **Roof drainage** — collect rain off the roof through gutters,
   downpipes, parapet outlets, syphonic drains.
2. **Storm drainage** — convey roof + paved-area water to a point of
   discharge (public sewer, watercourse, soakaway, attenuation tank).
3. **Sustainable Drainage Systems (SuDS)** — slow + filter water before
   discharge: green roofs, permeable paving, bioswales, ponds, soakaways
   (the SuDS hierarchy, in order of preference per CIRIA C753).

### 18.2 Why it matters

Get roof drainage wrong → roof floods → ponding → leaks → ceiling
collapse. Get storm drainage wrong → flash flooding off paved areas →
sewer surcharge → basement floods. Get SuDS wrong → planning refusal +
no flood-risk consent.

### 18.3 Roof drainage sizing — BS EN 12056-3

UK standard rainfall return period: **1-in-2-year storm** for general
roofs, **1-in-100-year** for protected critical areas. London design
rainfall ≈ 75 mm/hr for the 2-year, 5-minute storm; Scotland/Wales/
South West higher.

For a roof area `A` (m²) with rainfall intensity `r` (mm/hr):

```
Q = (A × r) / 3600        (L/s)
```

Outlet capacity (per BS EN 12056-3 Table 7):

| Outlet DN | Single rainwater pipe (L/s) | Syphonic outlet (L/s) |
|-----------|----------------------------:|----------------------:|
| 50        | 1.0                         | 4                      |
| 75        | 2.5                         | 12                     |
| 100       | 5.0                         | 25                     |
| 150       | 12.0                        | 60                     |

### 18.4 How to do it in STING

**STORM → `Plumb_RoofDrainage`** — STING reads roof areas from the
linked architectural model (or asks the user to select roof faces),
applies a project-set rainfall intensity, computes Q per outlet, and
sizes outlets + downpipes.

Writes:
- `PLM_STORM_ROOF_M2` — contributing roof area.
- `PLM_STORM_FLOW_LS` — design flow.
- `PLM_STORM_DN` — outlet/downpipe size.

### 18.5 Soakaway design — BRE 365

A soakaway is a pit (often gravel-filled or perforated-pipe-bedded)
that lets storm water infiltrate the ground. Sized by:

1. **Infiltration rate** `f` (m/hr) — from a percolation test on site.
2. **Design storm** — 10-year return, varying duration.
3. **Soakaway volume**:

```
V_required = (rainfall_depth × catchment_area) - (f × side_area × duration)
```

The catch: at low infiltration rates a soakaway gets huge. Clay soils
(f < 0.001 m/hr) often can't be drained to soakaway and need attenuation
tanks + restricted discharge.

**STORM → `Plumb_Soakaway`** — STING:
1. Asks for infiltration rate (from site investigation).
2. Asks for catchment area.
3. Applies BRE Digest 365 method.
4. Reports required soakaway volume + dimensions for typical shapes
   (rectangular gravel pit, ring-soakaway, perforated crate).

### 18.6 SuDS — the hierarchy

CIRIA C753 mandates designers consider, in order:

1. **Infiltration** (soakaway, swale, raingarden) — best.
2. **Slow discharge to watercourse** (attenuation + outflow control).
3. **Slow discharge to surface-water sewer** (with attenuation).
4. **Slow discharge to combined sewer** — last resort.

**STORM → `Plumb_SuDS`** outputs a SuDS strategy report listing the
chosen approach per sub-catchment, attenuation volumes (if any), and
discharge rates.

### 18.7 Rainwater harvesting — BS 8515

Collect roof rainwater → tank → re-use for WC flushing, garden taps,
laundry. Reduces mains demand by ~30 % in a typical UK home.

**Yield calculation** (BS 8515 Appendix A):

```
Y_annual = A × r_annual × C_runoff × η_filter
```

| Term       | Meaning                              | Typical                |
|------------|--------------------------------------|------------------------|
| Y_annual   | Yield (m³/yr)                        | Output                  |
| A          | Roof area (m²)                       | Project                 |
| r_annual   | Annual rainfall (m)                  | 0.65 (UK average)       |
| C_runoff   | Run-off coefficient                  | 0.9 (tiled pitched roof), 0.7 (flat asphalt), 0.5 (green roof) |
| η_filter   | Filter efficiency                    | 0.9 (mesh + sediment)   |

**STORM → `Plumb_RWH`** — STING applies the formula, sizes the tank to
serve 18-day demand (BS 8515 5 %ile dry period), suggests system layout
(direct-feed vs gravity-feed-cistern), and writes `PLM_RWH_YIELD_M3_YR`
to the harvesting tank family.

### Scenario — domestic dwelling with RWH

3-bed semi, roof area 80 m², London rainfall 650 mm/yr, tiled roof.

- Y_annual = 80 × 0.65 × 0.9 × 0.9 = **42 m³/yr**.
- Demand (WC + garden): 2 adults × 25 L/day × 365 ≈ 18 m³/yr.
- **Yield exceeds demand** — system viable. Tank size: 18 m³/yr / 365
  × 18 days = 0.9 m³ (round up to 1 m³ standard tank).

STING outputs a one-page worksheet ready for the planning application.

### 18.8 Permeable paving + green-roof attenuation

For sites with PPS25 / NPPF flood-risk constraints, SuDS often need
attenuation volume beyond what soakaways can absorb. Permeable paving
holds ~30 % volume in voids beneath a permeable surface course; green
roofs hold 30–60 mm of "blue" storage in growing medium. **STING
doesn't size these directly** — supply the architect's specifier with
the design flow output of `Plumb_RoofDrainage` and let them spec
substrate depth.

---

## 19. Septic tank and off-mains drainage

### What it is

Off-mains drainage is what you do when there's no public sewer to
connect to. The common UK options:

| System            | Treatment | Discharge                  | Approval         |
|-------------------|-----------|----------------------------|------------------|
| Cesspool          | None      | Tanker collection          | Last resort       |
| Septic tank       | Anaerobic | Drainage field (infiltration) | EA general binding rules |
| Package treatment plant | Aerobic + bio | Watercourse or DF   | EA permit (T21)  |
| Reed bed          | Tertiary  | Watercourse                 | EA permit         |

### Why it matters

Wrong choice → planning refusal, Environment Agency enforcement,
groundwater contamination, neighbour-disputes. Septic-tank discharge to
a watercourse is **illegal in England since 2020** unless the system
discharges via a properly-sized drainage field first.

### Septic tank sizing (BS 6297)

```
V_septic = (180 × P) + 2000        (litres, for domestic)
```

where `P` = number of persons served. Minimum tank size 2700 L.

Drainage field sizing per BS 6297:
- Trench length depends on percolation test value `V_p` (sec/mm).
- Typical: 12 m of trench per person at `V_p = 30`.

### How to do it in STING

**STORM → `Plumb_SepticTank`** — STING:
1. Asks number of persons (or reads from `PRJ_ORG_OCCUPANTS`).
2. Asks `V_p` percolation value (default 30).
3. Applies BS 6297 → tank volume + trench length.
4. Outputs a worksheet for the building-control submission.

---

## 20. Hangers and sleeves — supports and firestopping

### 20.1 Hangers (pipe supports)

Pipes need supports at intervals defined by material + DN:

| Material           | DN range  | Horizontal spacing | Vertical spacing  |
|--------------------|-----------|--------------------|-------------------|
| Copper Type B      | 15–22     | 1.2 m              | 1.8 m             |
| Copper Type B      | 28–54     | 1.8 m              | 2.4 m             |
| HDPE PE-100        | 32–90     | 1.0 m              | 1.5 m             |
| uPVC drainage      | 32–110    | 1.0 m              | 1.2 m             |
| Cast Iron drainage | 100–150   | 2.0 m              | 3.0 m             |
| Galvanised Steel   | 15–25     | 2.5 m              | 3.0 m             |
| Galvanised Steel   | 32–65     | 3.0 m              | 4.0 m             |

### How STING places them

**ROUTE → `Plumb_PlaceHangers`** walks every pipe segment and places a
hanger family every (spacing × pipe DN multiplier) along it. The hanger
family carries `HGR_*` parameters (rod size, base plate, fire rating).

### 20.2 Sleeves and firestops

Where a pipe crosses a wall, floor or fire-rated barrier:
- **Through a non-fire-rated wall** → sleeve = pipe DN + 20 mm.
- **Through a fire compartment line** → fire-rated sleeve + collar +
  mastic (typically Hilti, Promat, Rockwool).
- **Through a slab or roof slab** → sleeve + water-stop (puddle flange)
  + lead flashing (if external).

### How STING places them

**ROUTE → `Plumb_PlaceSleeves`** walks every pipe-vs-barrier
intersection (using linked architectural model + structural model) and:

1. Determines barrier type (wall / floor / fire-rated wall / fire-rated
   slab).
2. Picks the right sleeve family (`STING - Sleeve - Standard 22 mm`,
   `STING - Firestop Collar - 110 mm` etc.).
3. Places it at the intersection.
4. Tags it with a `PEN_*` tag and the fire-rating of the host barrier.
5. Writes the certification reference (UL system, EN 1366-3) from
   `STING_FAMILY_SWAP_REGISTRY.json` for the fire-rated cases.

The downstream **penetration validator** (`PenetrationCoverageCommand`)
audits that every pipe-vs-fire-barrier crossing has a sleeve + collar.

---

## 21. Validators — letting STING grade your homework

STING ships nine plumbing validators that run in `Plumb_FullAudit`
(AUDIT tab):

| Validator                  | What it checks                                                      | Standard            |
|----------------------------|---------------------------------------------------------------------|---------------------|
| Connectivity               | Every fixture is on a pipe system; every pipe ends somewhere        | (orphan detection)  |
| Slope                      | Drainage slopes between 1:80 (or DN min) and 1:40                   | BS EN 12056-2       |
| Velocity                   | Supply velocity ≤ 1.5 m/s cold, ≤ 1.0 m/s hot                       | BS EN 806-3         |
| Pressure                   | Every outlet has residual ≥ minimum spec                            | BS EN 806-3         |
| Backflow                   | Every outlet has protection ≥ fluid category                        | BS EN 1717          |
| Dead-leg                   | No DHW branch > project threshold (1 m HC / 2 m commercial)         | HTM 04-01, BS 8558  |
| Stack capacity             | Every stack carries ≤ tabulated max DU for its DN                   | BS EN 12056-2       |
| Trap-to-vent distance      | Every trap within tabulated max distance of nearest vent             | BS EN 12056-2       |
| Penetration coverage       | Every pipe-vs-fire-barrier crossing has a sleeve + firestop          | BS EN 1366-3        |

Each validator writes a `ValidationResult` record per element. The
dialog shows red (blocking), amber (warning) and green (informational)
entries with "Show in view" links so you can jump to the offending pipe
or fixture.

### How to do it in STING

1. **AUDIT → `Plumb_FullAudit`** — runs all nine validators in
   sequence, ~30 seconds for a typical project.
2. Review the dashboard — RAG status per sub-system.
3. Click each red entry → STING zooms to the offending element in the
   active view.
4. Fix.
5. Re-run.

> **First-timer rule.** A clean validation run is your minimum bar
> before issuing. If the slope validator says "main drain at 1:36 —
> too steep", the design is *wrong*, not fixable in Excel. Go re-route
> the pipe or add a backdrop manhole.

---

## 22. Schedules and docs — pipe schedule, BOQ, manhole, isometric, commissioning

### 22.1 Pipe schedule (DOCS → `Plumb_PipeSchedule`)

One row per pipe segment with: pipe tag, material, DN, length, service,
level, room, insulation thickness + material, fixture served.

| Column            | Source                                              |
|-------------------|-----------------------------------------------------|
| Pipe tag          | `ASS_TAG_1` (e.g. `P-BLD1-Z01-L02-DCW-SUP-PPE-0042`) |
| Service           | `PLM_SUP_SERVICE_TXT` or pipe system name           |
| Material          | `PLM_PPE_MAT_TXT`                                   |
| DN (mm)           | `PLM_SUP_DN` or `PLM_DRN_DN`                        |
| Length (m)        | Sum of segment lengths                              |
| From              | Upstream fixture / branch tag                       |
| To                | Downstream fixture / branch tag                     |
| Slope (drainage)  | `PLM_DRN_SLOPE_PCT`                                 |
| Velocity (supply) | `PLM_SUP_VEL_MS`                                    |
| Flow              | `PLM_SUP_FLOW_LS`                                   |
| Insulation        | `PLM_PPE_INSULATION_TXT` + thickness                |

This is the QS handover document and the contractor's pricing schedule.

### 22.2 BOQ (DOCS → `Plumb_BOQ`)

One row per material + fitting + valve + insulation + fixture, totalled
by service and by location. Costs come from `cost_rates_5d.csv`.

### 22.3 Manhole schedule (DOCS → `Plumb_ManholeSchedule`)

One row per manhole with: ref, location (Easting/Northing or grid),
size (depth × plan area), cover type (D400 / B125), cover level (CL),
invert level (IL), each connected pipe DN + IL + direction.

### 22.4 Isometric (DOCS → `Plumb_Isometric`)

Generates a schematic isometric drawing for a selected stack or branch.
Uses ISO 6412 piping symbols, with valve + meter + tap symbols
auto-placed from the connector graph.

### 22.5 Commissioning pack (DOCS → `Plumb_CommPack`)

Bundles:
- TMV register (with test dates).
- Pressure test certificates (one per supply section).
- Chlorination + flushing log template (HTM 04-01 for healthcare).
- Drainage CCTV survey template.
- Backflow device test certificates (RPZ annual test).
- As-built drawing references.

Each project gets one PDF + matching `.docx` so the FM team can populate
test results.

---

## 23. Drawings — plumbing drawing types

A drawing is the legal contract. STING produces them via the **Drawing
Template Manager** (`Core/Drawing/`).

### 23.1 Plumbing drawing types shipped with STING

| ID                                | Purpose                                         | Paper | Scale     |
|-----------------------------------|-------------------------------------------------|-------|-----------|
| `plumb-drainage-A1-1to100`        | Drainage layout (above + below ground)          | A1    | 1:100     |
| `plumb-supply-A1-1to100`          | Supply (DCW + DHW + DHWR) layout                | A1    | 1:100     |
| `plumb-rwd-A1-1to100`             | Roof + storm drainage layout                    | A1    | 1:100     |
| `plumb-gas-A1-1to100`             | Gas distribution layout                         | A1    | 1:100     |
| `plumb-vent-riser-A3-NTS`         | Vent + stack riser schematic (not to scale)     | A3    | NTS       |
| `plumb-supply-riser-A3-NTS`       | DCW + DHW riser schematic                       | A3    | NTS       |
| `plumb-isometric-A3-NTS`          | Per-stack isometric                             | A3    | NTS       |
| `plumb-pressure-schedule-A3`      | Pressure check schedule (sheet)                 | A3    | —         |
| `plumb-manhole-schedule-A3`       | Manhole schedule (sheet)                        | A3    | —         |
| `plumb-roof-A1-1to100`            | Roof plan with outlet positions                 | A1    | 1:100     |
| `plumb-soakaway-detail-A3-1to20`  | Soakaway construction detail                    | A3    | 1:20      |
| `plumb-plant-room-A2-1to50`       | Plant room (calorifier, boosters, valves)       | A2    | 1:50      |

### 23.2 How to make a sheet

Two options:

**A) From scope boxes** (the new way, recommended).

1. The architect provides scope boxes named
   `STING::plumb-drainage-A1-1to100::L02::west`.
2. **DOCS → Drawing Types → From Scope Boxes** (`DrawingTypes_FromScopeBoxes`).
3. STING creates the views, applies the profile (scale, view template,
   crop, filters), creates the sheet, places the viewport, stamps the
   title block.

**B) From templates**.

1. **DOCS → Sheet Manager → Create From Template**.
2. Pick a profile from the dropdown (plumbing profiles appear next to
   the built-in templates).

### 23.3 What's in the plumbing view-style pack

`corp-base-plumbing` (View Style Pack) does the colour-coding:

- DCW pipes → blue, weight 4
- DHW pipes → red, weight 4
- DHWR pipes → pink dashed, weight 3
- SAN pipes → olive green, weight 5
- WST pipes → light green, weight 4
- VNT pipes → yellow, weight 3
- RWD pipes → cyan, weight 4
- GAS pipes → magenta, weight 4
- FP wet → red double-line, weight 5
- Insulation halo → 25 mm offset, light grey

It halftones architectural + structural categories so plumbing reads as
foreground.

### 23.4 Drawing register

**DOCS → Doc Automation → Drawing Register** lists every sheet, its
drawing type, revision, suitability and date. Issue this with every
drop.

### 23.5 SyncStyles — keeping the set tidy

Once you have 50 sheets, someone will accidentally change a view's
scale. **DrawingTypes_SyncStyles** scans every stamped view, detects
drift, and restores the profile values. Run it before every issue.

---

## 24. Healthcare-specific plumbing

Healthcare projects add a stack of extra requirements that STING's
healthcare pack automates when `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` is set
(FULL / ACUTE / COMMUNITY / DENTAL / IMAGING-ONLY / MENTAL-HEALTH).

### 24.1 HTM 04-01 — Water Safety

The single most important document. Mandates:

- **Daily flushing** of every infrequently-used outlet (logged).
- **Weekly thermometer checks** at each TMV.
- **Monthly Legionella culturing** at sentinel taps + cold tank.
- **Quarterly TMV servicing** (TMV3 grade).
- **Annual chlorination** at recommissioning.

STING's **Healthcare → HTM-04-01-Annual workflow preset** chains the
audits + generates the flush schedule + commissioning logs.

### 24.2 Augmented care areas

Wards housing immunocompromised patients (oncology, ICU, neonatal,
transplant) carry the strictest rules:

- **RPZ valve at every clinical wash basin** (Cat 5 backflow).
- **Dead leg < 1.0 m** (vs 2 m commercial baseline).
- **Hospital-grade taps** with anti-splashback spouts and no aerators
  (aerators harbour biofilm).
- **No spare outlets** — if a tap isn't used, it's removed (not capped).

STING flags every clinical wash basin in an augmented-care room as
needing RPZ + Cat 5 + flushing log.

### 24.3 MGPS interface

Some clinical sinks tie into Medical Gas Pipeline System (MGPS) end
points for cleaning gas hand-pieces (dental). STING's **MgasNetwork**
graph builder treats these as a separate domain — see the healthcare
guide for detail.

### 24.4 Endoscope reprocessing — HTM 01-06

Endoscope decontamination rooms have very specific plumbing:
- Reverse-osmosis water feed (RO purified, ≤ 100 µS/cm).
- Drain-side air break to prevent backflow.
- TMV3 hand-wash at exit.

STING's **HTM-01-06-EndoReprocess** workflow chains the audit for
these rooms.

### 24.5 Dental cuspidor and aspirator

Dental cuspidors (the spit-bowl) discharge directly to drainage. They
need:
- Cat 5 backflow protection on the supply (water for rinsing).
- A separate aspirator (suction) line that goes via an amalgam
  separator (ISO 11143).

STING's healthcare pack auto-flags missing amalgam separators on dental
project drainage.

### 24.6 Mortuary plumbing

Mortuary sinks + flushing rinses:
- Cat 5 backflow on every outlet.
- Stainless steel pipework (no plastic — disinfectant attack).
- Buried-trap floor gulleys with anti-flooding non-return valves.
- Discharge to a separated drain line with a sealed manhole near
  building edge.

STING auto-applies these defaults when a room's `RoomType` = "Mortuary".

### Scenario — 50-bed hospital ward water hygiene

Project profile: FULL healthcare pack on, ward area Z03.

- 50 bedhead clinical wash basins → STING flags Cat 5 backflow needed
  on each → recommends RPZ valves at branch entry.
- 25 WCs + 10 shower rooms → standard Cat 5 air gap (AUK3 + air break
  in WC pan).
- All hot dead-legs > 1.0 m flagged → 8 instances → either relocate
  TMV closer to outlet or extend DHWR loop to within 1 m of basin.
- TMV register: 50 TMV3 valves, set 38 °C, quarterly inspection
  scheduled.
- Flushing programme: 50 basins + 25 WCs + 10 showers → 85 daily
  flushes, log template exported as CSV.
- Pressure profile: incoming 4.5 bar, calorifier head loss 0.5 bar,
  riser to L4 minus 0.7 bar static + 0.3 bar friction → 3.0 bar at
  furthest basin → ✓.
- Recirc loop: 12 W/m heat loss across 240 m loop = 2.9 kW; required
  flow 0.14 L/s at ΔT 5 K → DHWR DN 22 mm copper.

The full audit dashboard reads: **GREEN 87 %, AMBER 11 %, RED 2 %** —
the 2 % red being the dead legs to fix before issue.

---

## 25. Common first-timer mistakes

| Mistake                                                                | What goes wrong                                                | How STING catches it                                  |
|------------------------------------------------------------------------|----------------------------------------------------------------|-------------------------------------------------------|
| Placing fixtures before creating Revit pipe systems                    | Cannot connect, cannot route, cannot tag SYS                    | Panel status bar warns "no pipe systems"              |
| Skipping `Plumb_ScanFixtures` after placing fixtures                   | DU + LU never written → sizing engines skip everything          | Pre-sizing audit reports "0 LU detected"              |
| Drawing pipes in wrong system (e.g. SAN system but DCW pipe type)      | Velocity validator passes spuriously                            | `Plumb_FullAudit` flags system/material mismatch      |
| Forgetting to set static pressure at incoming main                     | Pressure-check engine assumes 0 bar — every outlet fails        | `Plumb_PressureCheck` reports "no static pressure set"|
| Using one stack for the whole project                                  | DU exceeds 960, stack jump-flow guaranteed                       | `StackCapacityValidator` flags red                    |
| Not placing rooms (or linking architect's rooms)                       | LOC + ZONE not auto-detected → tags incomplete                  | Compliance scan reports per-discipline gaps           |
| Editing tags by hand                                                   | Duplicate tags, broken SEQ sequences                            | `Find Duplicate Tags`, `Repair Duplicate SEQ`         |
| Issuing before running validators                                      | Embarrassing markups from the reviewer                          | Workflow preset *PlumbingAudit* runs all validators    |
| Hand-creating pipe schedules                                           | Drift between schedule and model                                 | `Plumb_PipeSchedule` re-runs from model                |
| Using `Plumb_AutoRoute` without a max-slope constraint for drainage    | Drains end up at 1:200 — solids strand                          | `Plumb_FixSlopes` + slope validator                    |
| Forgetting trap-to-vent distance check                                 | Trap seals lost to siphoning → sewer gas in building            | `VentDesigner` flags out-of-spec branches             |
| Treating waterless urinals as having LU > 0                            | Cold-water main upsized for no reason                            | `FixtureUnitScanner` zero for waterless                |
| Forgetting expansion vessels on sealed DHW systems                     | PRV dumps water every cycle → corrosion, eventual joint failure | `Plumb_FullAudit` cross-checks for vessel on sealed   |
| Mixing pipe materials in one system                                    | Galvanic corrosion at joint (Cu/Fe couple)                       | `BackflowClassifier` warns on mixed metal joints       |
| Drawing soakaways as a "blue blob" with no calc                        | Planning refusal                                                 | Use `Plumb_Soakaway` to produce BRE 365 worksheet      |
| Forgetting RPZ on healthcare clinical wash basins                      | HTM 04-01 non-compliance, infection-control risk                 | Healthcare pack auto-flags                             |
| Forgetting to extend DHWR loop to within 1 m of all outlets (HC)       | Cold-running taps, Legionella risk in dead leg                   | `DeadLegDetector` flags                                |
| Forgetting AAV vs vent-through-roof distinction                        | AAV used where roof terminal required                            | `VentDesigner` warns on > 4-storey AAV use             |
| Setting wrong rainfall return period for roof drainage                 | Roof floods in a 5-year storm                                    | `Plumb_RoofDrainage` shows assumed intensity           |
| Connecting hose bibcock with only a DCV (Cat 3)                        | Reg 99 violation — must be Cat 4 minimum                          | `BackflowClassifier` flags red                         |
| Letting the autotagger run before placing rooms                        | LOC defaults to project-info, ZONE to "XX"                       | Re-run `TagAndCombine` after rooms placed              |
| Not stamping drainage drawings with the right drawing type             | View doesn't get plumbing colour-coding                          | `DrawingTypes_SyncStyles` warns "no stamped DT"       |
| Forgetting to size the cleaner's sink load (most under-sized fixture)  | Sink branches block from heavy mop discharges                   | `FixtureUnitScanner` correctly weights cleaner sinks  |
| Drawing risers in 2D — no Section View                                 | Riser auto-tagging finds nothing                                 | Use a real Section View per §23                        |
| Not running `Plumb_FullAudit` before every issue                       | Silent compliance failures escape to client                       | (the rule is: don't issue without a green audit)       |
| Letting auto-route push pipes through structural beams                 | Onsite re-routing, abortive work                                 | Run clash detection after auto-route                   |

---

## 26. Glossary

| Acronym | Meaning                                                                |
|---------|------------------------------------------------------------------------|
| AAV     | Air Admittance Valve (BS EN 12380)                                     |
| AFFL    | Above Finished Floor Level                                             |
| AOD     | Above Ordnance Datum (UK sea-level baseline)                           |
| AUK1 / AUK2 / AUK3 | Air gaps per BS EN 1717 (Cat 3 / 4 / 5)                       |
| BIM     | Building Information Modelling                                          |
| BOQ     | Bill of Quantities                                                      |
| BS 5422 | Pipe insulation thickness tables                                        |
| BS 6700 | UK Code of Practice for water supply                                    |
| BS 7074-1 | Expansion vessel sizing                                               |
| BS 7671 | UK Wiring Regulations (electrical — overlaps for pump circuits)         |
| BS 8515 | Rainwater harvesting (UK)                                                |
| BS 8558 | UK guidance to BS EN 806 (water supply)                                  |
| BS EN 274 | Sanitary tappings + traps                                              |
| BS EN 806 | Water supply (multi-part)                                              |
| BS EN 1717 | Backflow prevention — fluid categories                               |
| BS EN 12056 | Gravity drainage (multi-part)                                         |
| BS EN 12380 | Air admittance valves                                                  |
| CDE     | Common Data Environment (the document store)                            |
| CIBSE   | Chartered Institution of Building Services Engineers                     |
| CIRIA   | Construction Industry Research and Information Association              |
| COBie   | Construction Operations Building information exchange (FM data format)  |
| CWS     | Cold Water Supply (often interchangeable with DCW)                      |
| Cat 1–5 | Fluid category per BS EN 1717                                            |
| DCV     | Double Check Valve (Cat 3 backflow protection)                          |
| DCW     | Domestic Cold Water                                                      |
| DHW     | Domestic Hot Water                                                       |
| DHWR    | Domestic Hot Water Return                                                |
| DN      | Nominal Diameter (mm)                                                    |
| DRV     | Double Regulating Valve (commissioning balance valve)                   |
| DT      | Drawing Type — STING profile id (e.g. `plumb-drainage-A1-1to100`)        |
| DU      | Discharge Unit (drainage loading)                                        |
| EA      | Environment Agency (UK)                                                  |
| F&E tank | Feed and Expansion tank (open-vented heating systems)                   |
| FM      | Facilities Management                                                   |
| FP      | Fire Protection                                                          |
| HTM 04-01 | Health Technical Memorandum — water safety                            |
| HTM 01-06 | Endoscope reprocessing                                                |
| IL      | Invert Level                                                             |
| ISO 19650 | The international BIM standard for delivering construction info       |
| Kvs     | Valve flow coefficient at full open (m³/h at 1 bar)                     |
| LU      | Loading Unit (BS EN 806 supply)                                          |
| mAOD    | Metres Above Ordnance Datum                                              |
| MGPS    | Medical Gas Pipeline System                                              |
| MEP     | Mechanical, Electrical, Plumbing                                         |
| MH      | Manhole                                                                  |
| NPPF    | National Planning Policy Framework (UK)                                  |
| Pa/m    | Pascals per metre of pipe (friction loss)                                |
| PIB     | Plumbing Industries Bulletin                                             |
| PPS25   | Planning Policy Statement 25 (flood risk, superseded by NPPF)            |
| PRV     | Pressure Relief Valve                                                    |
| RAG     | Red / Amber / Green compliance status                                    |
| RPZ     | Reduced Pressure Zone valve (Cat 4–5 backflow)                          |
| RWD     | Rainwater Drainage                                                       |
| RWH     | Rainwater Harvesting                                                     |
| SAN     | Sanitary (soil drainage)                                                 |
| SFG20   | Soft FM maintenance task library                                         |
| SuDS    | Sustainable Drainage Systems                                             |
| TMV2 / TMV3 | Thermostatic Mixing Valves (commercial / healthcare grades)         |
| TQ      | Technical Query                                                          |
| U₀      | Phase-earth voltage (electrical — irrelevant here)                       |
| Uniclass| UK construction classification system                                     |
| VLV     | Valve                                                                    |
| Vd      | (electrical) Voltage drop / (plumbing) "Vd-vel" sometimes used for design velocity |
| VNT     | Vent                                                                     |
| WC      | Water Closet                                                             |
| WST     | Waste                                                                    |
| WSFU    | Water Supply Fixture Unit (US — included in STING tables for compat)    |

---

## Recommended first-week practice

If you have one Revit model and one week to learn STING plumbing, do this:

| Day | Task                                                                       |
|-----|----------------------------------------------------------------------------|
| 1   | Run Project Setup Wizard. Link an architect's model. Create Piping Systems. |
| 2   | Place 4 WCs, 4 basins, 1 cleaner's sink. Run `Plumb_ScanFixtures`.          |
| 3   | Create Revit pipe systems. Auto-route DCW + SAN. Insert P-traps.            |
| 4   | Run `Plumb_SizeSupply` + `Plumb_SizeDrainage`. Fix slopes.                  |
| 5   | Run `Plumb_VentDesign` + `Plumb_InvertLevels`. Run `Plumb_FullAudit`.       |
| 6   | Generate pipe schedule + BOQ. Produce a drainage layout sheet.              |
| 7   | Issue the package via the Template Engine. Check the rendered cover.        |

Once you've done that round trip, you will know more about practical
plumbing BIM than 80 % of grads. Welcome to the discipline.

---

*Document version 1.0 — May 2026. Maintained alongside CLAUDE.md.*
