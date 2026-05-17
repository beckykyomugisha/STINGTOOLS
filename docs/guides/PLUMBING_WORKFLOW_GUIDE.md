# STING Plumbing & Public Health Workflow Guide

---

## How to use this guide

Read the first three sections (this one, "Who this guide is for", and "The Big Picture") before touching any buttons. They explain the overall plan so that every individual step makes sense in context.

After that, work through the chapters in order the first time. The chapters follow the natural sequence of a plumbing project: set up the project, place fixtures, create pipe systems, route pipes, validate, schedule, produce drawings, issue.

Code blocks, table references, and button labels are written exactly as they appear on screen. What you see in Revit and in STING is exactly what is written here.

> **How to use the blockquotes:** anywhere you see a `> **Stuck?**` blockquote, stop and read it before moving on. It explains what a confusing screen means and what to do about it.

---

## Who this guide is for

You are a qualified plumber, public health engineer, or building services designer. You know what a domestic cold water system is. You know why drainage pipes must slope. You know the difference between a soil stack and a vent stack. You have opened Revit before and placed a family or drawn a wall.

What you have not done before is use STING — the plugin that sits inside Revit and adds ISO 19650-compliant tagging, automated pipe routing, validation, scheduling, and drawing production to your Revit workflow.

This guide assumes:

- You know what a Revit project file (`.rvt`) is.
- You know how to open the STING panel (click the **STING Panel** button on the Revit ribbon, or look for the docked panel on the right side of the screen).
- You have not used STING's plumbing tools before.
- You do not need to be a programmer. Every instruction involves clicking buttons and typing values into fields.

If you have never created an MEP symbol family or never used the Placement Centre, read `MEP_FOUNDATION_GUIDE.md` first. This guide will tell you exactly where to go there when you need it.

---

## The Big Picture

A STING plumbing project follows eight stages in sequence. Think of it like installing an actual plumbing system: you do not start soldering joints before you have a schematic, and you do not commission before you have tested.

```
┌─────────────────────────────────────────────────────────────────┐
│  PLUMBING PROJECT WORKFLOW — ALL EIGHT STAGES                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  STAGE 1 ── Set up the project                                  │
│  Load shared parameters, create pipe types, verify              │
│  plumbing drawing types are available.                          │
│            │                                                    │
│            ▼                                                    │
│  STAGE 2 ── Place plumbing fixtures                             │
│  Basins, WCs, taps, floor drains, riser valves — every          │
│  physical piece of equipment that connects to a pipe.            │
│            │                                                    │
│            ▼                                                    │
│  STAGE 3 ── Create Revit pipe systems                           │
│  Tell Revit which pipes carry DCW, which carry SAN,             │
│  which carry GAS. STING reads these to assign system codes.     │
│            │                                                    │
│            ▼                                                    │
│  STAGE 4 ── Route pipes                                         │
│  AutoPipeDrop draws pipe runs from distribution points          │
│  to fixtures automatically. Manual routing fills gaps.          │
│            │                                                    │
│            ▼                                                    │
│  STAGE 5 ── Apply tagging and push system data                  │
│  AutoTagCommand stamps every element with its ISO 19650         │
│  eight-segment tag. SystemParamPushCommand fills pipe           │
│  parameters from the connected Revit system.                    │
│            │                                                    │
│            ▼                                                    │
│  STAGE 6 ── Validate                                            │
│  RunAllValidatorsCommand checks slope, fill, connectivity,      │
│  and termination. Fix every error before proceeding.            │
│            │                                                    │
│            ▼                                                    │
│  STAGE 7 ── Produce schedules and drawings                      │
│  Fixture schedules, pipe schedules, insulation schedules.       │
│  Drawing sheets using the plumb-drainage-A1-1to100 type.        │
│            │                                                    │
│            ▼                                                    │
│  STAGE 8 ── Issue                                               │
│  See DOCUMENT_MANAGER_GUIDE.md                                  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

You cannot skip stages. If you try to run AutoPipeDrop (Stage 4) before creating pipe systems (Stage 3), the engine will not know which system to assign to the pipes it creates. If you try to validate (Stage 6) before tagging (Stage 5), the validators will report every element as untagged.

---

## Prerequisites

### Shared parameters

STING stores plumbing data in Revit "shared parameters" — named fields that exist in a central text file and can be attached to any element in any project. Before STING can write pipe sizes, flow rates, or slope data to your model, these parameters must be bound to the right Revit categories.

The relevant parameter group is **PLM_DRN** (Plumbing and Drainage). This group contains all PLM_* parameters for pipes, fittings, fixtures, valves, and accessories.

**How to load shared parameters:**

1. Open the STING panel.
2. Click the **CREATE** tab.
3. In the Setup section, click **Load Params**. This runs `LoadSharedParamsCommand`.
4. STING will show a progress dialog and a results summary. You should see "PLM_DRN" listed in the bound groups.
5. To verify: select any pipe in your model → open the Properties palette → scroll down. You should see parameters beginning with `PLM_`.

> **Stuck?** If you do not see the **Load Params** button, make sure you are on the **CREATE** tab of the STING panel (not the SELECT or BIM tab). If the button is greyed out, you may not have a Revit document open.

### Key plumbing parameters

The parameters you will encounter most often are:

| Parameter name | What it records | Which elements carry it | Example value |
|---|---|---|---|
| `PLM_PPE_SZ_MM` | Pipe nominal diameter in millimetres | Pipes, pipe fittings | `22`, `28`, `100` |
| `PLM_FLOW_RATE_LPS` | Design flow rate through the pipe in litres per second | Pipes | `0.15` |
| `PLM_PPE_FLW_LPS` | Flow rate on a pipe accessory (valve, strainer) | Pipe accessories | `0.30` |
| `PLM_SYSTEM_TXT` | The system code this element belongs to | Pipes, fixtures, valves | `DCW`, `SAN`, `GAS` |
| `PLM_SLOPE_PCT` | Drainage pipe slope as a percentage | Drainage pipes | `1.25` |
| `PLM_INSULATION_TXT` | Insulation material name | Pipes | `Armaflex`, `Rockwool` |
| `PLM_PPE_INSULATION_THK_MM` | Insulation thickness in millimetres | Pipes | `25`, `38` |
| `PLM_PPE_MAT_TXT` | Pipe material | Pipes | `Copper`, `HDPE` |
| `PLM_PPE_JOINT_TYPE_TXT` | Joint type | Pipes, fittings | `Soldered`, `Compression` |
| `PLM_PPE_PSR_RATING_BAR` | Pressure rating of the pipe | Pipes | `10`, `16` |
| `PLM_EQP_PRESSURE_KPA` | Design pressure at a fixture or equipment item | Plumbing fixtures | `150`, `300` |
| `PLM_HED_M` | Static head in metres (used in pump and gravity drain calculations) | Equipment | `12.5` |
| `PLM_VEL_MPS` | Water velocity in the pipe in metres per second | Pipes | `1.5` |

> **Note on naming:** STING's parameter names follow a prefix pattern. `PLM_` means Plumbing. `PLM_PPE_` means Plumbing Pipe. `PLM_EQP_` means Plumbing Equipment. `PLM_VLV_` means Plumbing Valve. `PLM_DRN_` means Plumbing Drainage. Once you know the prefixes, the names become self-explanatory.

### Plumbing view templates

STING ships a built-in drawing type called `plumb-drainage-A1-1to100`. This drawing type controls sheet size (A1), scale (1:100), the view template applied, the title block used, sheet numbering, and the graphic style pack. Think of it as a saved recipe for how every plumbing drainage drawing should look.

**How to verify the drawing type exists:**

1. Click the **DOCS** tab in the STING panel.
2. In the Drawing Types section, click **Inspect Drawing Types**. This runs `DrawingTypesInspectCommand`.
3. In the results window that appears, look for `plumb-drainage-A1-1to100` in the list.
4. If it is listed without any warnings, you are ready. If it shows a warning about a missing view template, follow the instructions in `DRAWING_PRODUCTION_SYSTEM_GUIDE.md` to create the template before continuing.

---

# Chapter 1 — Understanding Plumbing Systems in STING

---

## 1.1 The plumbing discipline code

Every element in STING carries an eight-segment ISO 19650 tag. The first segment is the discipline code — a single letter or short code that tells you which trade the element belongs to.

For plumbing and public health, the discipline code is **P**.

This means every domestic cold water pipe, sanitary drain, gas supply pipe, and hot water cylinder in your model will carry tags beginning with `P-`. When you run reports, schedules, or validation, you can filter to discipline `P` to see only plumbing elements.

---

## 1.2 System codes for plumbing work

Within discipline P, each element also carries a system code (the fifth segment of the tag, `SYS`). The system code tells you what the element is carrying — cold water, hot water, drainage, and so on.

A pipe system in STING is like a named pipeline in a factory: everything connected to it shares the same label. A DCW system groups all the cold water pipes and fittings feeding domestic outlets. A SAN system groups all the soil and waste pipes leading to the drainage stack.

The full set of plumbing system codes:

| Code | Full name | What it carries | Typical operating conditions | Standards reference | Typical connected elements |
|---|---|---|---|---|---|
| **DCW** | Domestic Cold Water | Cold mains-pressure drinking water | 1–10 bar, 5–20°C | BS EN 806-2, WRAS | Taps, WC cisterns, shower mixers, cold water storage tanks, pressure reducing valves |
| **DHW** | Domestic Hot Water | Hot water from a central heat source (boiler, cylinder, heat pump) | 0.5–4 bar, 60–65°C storage, 55°C distribution | BS EN 806-2, L8 ACOP | Hot taps, shower mixers, thermostatic mixing valves, calorifiers |
| **HWS** | Hot Water Services Circulation | The return leg of a hot water circuit — water circulating back from the distribution pipework to be reheated | Very low flow, 55°C | BS EN 806-2, L8 ACOP | Circulation pump, calorifier return, secondary circuit |
| **SAN** | Sanitary Drainage | Foul water from toilets, basins, baths, showers, kitchen sinks, floor gullies, laboratory sinks | Gravity, 0 bar pressure, typically ≤18 m/s maximum velocity | BS EN 12056-2, Building Regs Part H | WC pan connectors, basin wastes, bath traps, floor drains, gullies, soil stacks, underground drainage |
| **RWD** | Rainwater Drainage | Surface water from roofs, paved areas, car parks | Gravity, 0 bar pressure | BS EN 12056-3, Building Regs Part H | Roof drains, gutters, rainwater downpipes, underground surface water drains |
| **GAS** | Gas Supply | Natural gas, liquefied petroleum gas (LPG), or biogas to boilers, cookers, laboratory equipment | Low pressure: ≤21 mbar; medium pressure: 21 mbar–2 bar | IGEM UP/2, Gas Safety (Installation and Use) Regs, IGE/UP/1B | Gas meters, boilers, cookers, laboratory gas cocks, isolation valves |

### Explaining each system in plain English

**DCW — Domestic Cold Water.** This is mains water coming into the building and being distributed to every cold tap, WC cistern, shower, washing machine, and dishwasher. In most UK buildings it arrives at roughly 3–6 bar. You either use it directly at mains pressure, or you break it to a lower pressure using a pressure reducing valve (PRV). Pipes are typically copper (15mm, 22mm, 28mm for small buildings), CPVC, or stainless steel. Insulation is needed in warm spaces to prevent Legionella.

**DHW — Domestic Hot Water.** This is the hot water system: a boiler or heat pump heats water in a cylinder, and that hot water is pumped out to every hot tap. The cylinder stores water at 60–65°C (hot enough to kill Legionella bacteria), and a thermostatic mixing valve (TMV) blends it with cold at the point of use to a safe 38–45°C. DHW pipes are typically the same size as DCW pipes but insulated throughout.

**HWS — Hot Water Services Circulation.** In any building larger than a house, if hot water simply sat in long pipe runs, it would cool down between uses. HWS is the circulation return: a small pump continuously recirculates hot water around a loop so that hot water is always available immediately at the tap. HWS pipes carry the return leg of this circulation circuit. Without HWS, users would waste water waiting for hot water to arrive.

**SAN — Sanitary Drainage.** This is the foul drainage system: every WC, basin, bath, shower, kitchen sink, and floor gully discharges to a soil or waste pipe that drains by gravity to the sewer. SAN pipes are always gravity pipes — they must slope downwards towards the point of discharge. Horizontal SAN pipes must slope at 1:80 (1.25%) for soil branches and 1:50 (2%) for most wastes, per BS EN 12056-2. A soil stack is the vertical riser that collects branches from multiple floors. An open vent above the stack (and sometimes air admittance valves on branches) prevents the trap seals from being siphoned.

**RWD — Rainwater Drainage.** Surface water from the roof and paved areas must be collected and discharged to a storm sewer or soakaway. Roof drains collect water at the lowest points of flat roofs; gutters collect it from pitched roofs. RWD pipes must also slope to drain. The sizing is governed by BS EN 12056-3 using the rainfall intensity for the location (litres per second per square metre of roof area). Critical detail: in the UK, surface water (RWD) must be discharged separately from foul water (SAN) — it is illegal to connect them together in most new construction.

**GAS — Gas Supply.** Natural gas or LPG arrives at the building via a meter and is distributed to boilers, cookers, gas fires, and laboratory equipment. Gas pipes are run in steel (black steel or stainless) or copper, with joints typically threaded, brazed, or press-fit depending on the application. Gas pipes must be properly supported, bonded to earth, and run in accessible locations. They cannot be run through a void without being sleeved. Gas system design involves pressure drop calculations to ensure adequate pressure at every appliance — STING records gas system data but does not perform these calculations itself (see Chapter 7).

---

## 1.3 How Revit pipe systems map to STING codes

In Revit, a "Pipe System" (accessed through the Systems panel) is a logical grouping: you create a system, name it, and then connect pipes and fittings to it. Revit tracks which elements belong to which system for its own flow and pressure calculations.

STING reads the Revit system name to assign the `SYS` token to every element. The rule is simple: **name your Revit pipe system with the STING system code, and STING will automatically assign the correct system code to all connected elements.**

For example:
- Create a Revit Pipe System named `DCW` → all connected pipes get tagged with `SYS=DCW`
- Create a Revit Pipe System named `SAN` → all connected drainage pipes get tagged with `SYS=SAN`
- Create a Revit Pipe System named `GAS` → all gas pipes get tagged with `SYS=GAS`

If you name the system something like "Cold Water" or "Drainage" instead of the STING code, the tagging engine will not recognise it and will leave the `SYS` token blank. That then appears as a compliance failure when you run `ValidateTagsCommand`.

---

## 1.4 The PLM_* parameter family

| Parameter | What it records | Which elements | Example value |
|---|---|---|---|
| `PLM_PPE_SZ_MM` | Nominal pipe bore in mm | Pipes | `22` |
| `PLM_PPE_MAT_TXT` | Pipe material | Pipes | `Copper` |
| `PLM_PPE_JOINT_TYPE_TXT` | Joint type | Pipes, fittings | `Soldered` |
| `PLM_PPE_LENGTH_M` | Pipe run length in metres | Pipes | `3.65` |
| `PLM_PPE_FLW_LPS` | Flow rate on an accessory (l/s) | Valves, strainers | `0.30` |
| `PLM_PPE_PSR_RATING_BAR` | Pressure rating (bar) | Pipes | `10` |
| `PLM_PPE_INS_TYPE_TXT` | Insulation material | Pipes | `Armaflex` |
| `PLM_PPE_INS_THK_MM` | Insulation thickness (mm) | Pipes | `25` |
| `PLM_PPE_INSULATION_THK_MM` | Insulation thickness — v4 constant | Pipes | `38` |
| `PLM_INSULATION_TXT` | Insulation material — v4 constant | Pipes | `Rockwool` |
| `PLM_SLOPE_PCT` | Drainage slope as a percentage | Drainage pipes | `1.25` |
| `PLM_SYSTEM_TXT` | System code this element belongs to | Pipes, fixtures | `DCW` |
| `PLM_FLOW_RATE_LPS` | Design flow rate (l/s) | Pipes | `0.15` |
| `PLM_VEL_MPS` | Water velocity (m/s) | Pipes | `1.5` |
| `PLM_PSR_KPA` | Pressure at this element (kPa) | Pipes, equipment | `150` |
| `PLM_HED_M` | Static head (m) | Equipment, pumps | `12.5` |
| `PLM_EQP_CAPACITY_L` | Storage volume of equipment (litres) | Hot water cylinders, tanks | `200` |
| `PLM_EQP_PRESSURE_KPA` | Design pressure at equipment (kPa) | All plumbing equipment | `300` |
| `PLM_HOTWTR_TEMP_C` | Hot water storage temperature (°C) | Calorifiers, cylinders | `65` |
| `PLM_HOTWTR_FUEL_TYPE_TXT` | Fuel type for water heater | Boilers, water heaters | `Gas`, `Heat Pump` |
| `PLM_HOTWTR_INPUT_PWR_KW` | Heat input to water heater (kW) | Boilers, immersion heaters | `24` |
| `PLM_VLV_BODY_MAT_TXT` | Valve body material | Valves | `Brass`, `Bronze` |
| `PLM_VLV_END_CONNECTION_TXT` | Valve end connection type | Valves | `Compression`, `Threaded` |
| `PLM_DRN_PPE_SLOPE_PCT` | Slope on a drainage pipe (%) | Drainage pipes | `1.25` |
| `PLM_DRN_FLW_RATE_LPS` | Flow rate in drainage pipe (l/s) | Drainage pipes | `0.08` |
| `PLM_DRN_TRAP_TYPE_TXT` | Trap type | Drainage fittings | `P-trap`, `Bottle trap` |
| `PLM_DRN_TRAP_SEAL_DEPTH_MM` | Trap seal depth (mm) | Drainage fittings | `75` |
| `PLM_EQP_TAG` | Plumbing equipment tag container | Fixtures, equipment | `P-BLD1-Z01-L02-DCW-SUP-BSN-0003` |

---

# Chapter 2 — Setting Up Your Plumbing Project

---

## Step 1: Load shared parameters

Loading shared parameters is the equivalent of wiring up the data fields before you start entering data. Without this step, STING has nowhere to put the pipe sizes, flow rates, and system codes it calculates.

**Step by step:**

1. Open the STING panel. It should be docked on the right side of Revit. If you cannot see it, click **STING Panel** on the STING ribbon tab.
2. Click the **CREATE** tab at the top of the STING panel.
3. In the **Setup** section (near the top of the tab), click **Load Params**. A progress dialog will appear.
4. STING runs two passes: Pass 1 binds all instance parameters; Pass 2 binds type parameters. Both passes should complete successfully.
5. When the dialog says "Done", click OK.
6. To verify: in your Revit project, draw a short test pipe (Systems tab → Pipe). Select it. In the Properties palette on the left, scroll down. You should see parameters like `PLM_PPE_SZ_MM`, `PLM_SYSTEM_TXT`, and others beginning with `PLM_`.

> **Stuck?** If you see "Pass 2: 0 parameters bound" in the results, it usually means the shared parameter file is not at the expected path. Check that your project has a shared parameter file loaded: `Manage → Shared Parameters`. If none is loaded, contact your BIM coordinator to point Revit to the STING shared parameter file (`MR_PARAMETERS.txt`) from the STING data folder.

---

## Step 2: Create plumbing pipe types

STING ships a set of standard pipe types defined in its data CSV file. These are the standard pipe materials and sizes used in UK building services work.

**Step by step:**

1. In the STING panel, click the **TEMP** tab.
2. In the **Families** section, click **Pipes**. This runs `CreatePipesCommand`.
3. STING reads its pipe type definitions and creates the following pipe types in the project:

| Type name | Material | Typical use | Sizes created |
|---|---|---|---|
| Copper — Half Hard | Copper | DCW, DHW, HWS | 15, 22, 28, 35, 42, 54 mm |
| CPVC Pressure | Chlorinated PVC | DCW in commercial buildings | 15, 22, 28, 35 mm |
| HDPE Drainage | High-density polyethylene | SAN underground | 100, 150, 225 mm |
| Cast Iron Soil | Cast iron | SAN above-ground stacks | 100, 150 mm |
| Stainless Steel Pressed | Stainless steel 316 | DHW in food-prep and healthcare | 15, 22, 28, 35, 42, 54 mm |
| Black Steel | Carbon steel | GAS distribution | 15, 20, 25, 32, 40, 50 mm |

4. To verify: in the Project Browser on the left of the screen, expand `Families → Pipes`. You should see the new type families listed under `Pipe Types`.

> **Stuck?** If the **Pipes** button is not visible in the TEMP tab, scroll down within the Families section — there are several family creation buttons and they may be scrolled out of view.

---

## Step 3: Verify plumbing drawing types

STING ships a drawing type profile called `plumb-drainage-A1-1to100` which defines how every plumbing drawing is produced: A1 paper at 1:100 scale, using the `corp-standard-plan` style pack, with a STING corporate title block.

**Step by step:**

1. In the STING panel, click the **DOCS** tab.
2. Click **Inspect Drawing Types**. This runs `DrawingTypesInspectCommand`.
3. In the results dialog, find `plumb-drainage-A1-1to100`. Check for any warnings.
4. Common warnings and what they mean:

| Warning text | What it means | Fix |
|---|---|---|
| "View template 'STING - Drainage Plan' not found" | The Revit view template hasn't been created yet | Follow `DRAWING_PRODUCTION_SYSTEM_GUIDE.md` to create the template |
| "Title block family not loaded" | The STING title block family isn't in the project | Load it from `Insert → Load Family → navigate to STING Families` |
| No warnings | You are ready | Continue to Chapter 3 |

---

# Chapter 3 — Placing Plumbing Fixtures

---

## 3.1 Fixture families must be placement-ready

Before STING can place a basin, WC, shower tray, or floor drain automatically, the family file (`.rfa`) for that fixture must be set up correctly. This is called being "placement-ready."

A placement-ready plumbing fixture family has:
- Its insertion point at the correct connection point (the pipe connector, not the geometric centre of the object)
- Reference planes correctly named
- STING shared parameters bound to it

For the full authoring process, see **MEP_FOUNDATION_GUIDE.md, Chapter 2** — "Placement Family Authoring." The rules described there for MEP families apply equally to plumbing fixtures.

---

## 3.2 Using the Placement Centre for automatic fixture placement

If you need to place dozens of basins, WCs, or floor drains across a building using rules (for example, "one basin per WC cubicle on every floor"), use STING's Placement Centre.

See **MEP_FOUNDATION_GUIDE.md, Chapter 3** — "The Placement Centre." Everything described there applies directly to plumbing fixtures.

---

## 3.3 Manual fixture placement

For small projects or when placing individual fixtures by hand:

**Step by step:**

1. In the STING panel, click the **MODEL** tab.
2. Click **Place Fixture**. This runs `ModelPlaceFixtureCommand`.
3. A list picker dialog appears showing all plumbing fixture families loaded in the project. If you cannot see the family you need, load it first via `Insert → Load Family`.
4. Click the family name you want to place.
5. Click the placement point in the view. For a wall-hung basin, click the wall face at the correct height. For a floor drain, click the floor at the drain location.
6. STING places the family and immediately runs an auto-tag on the new element.

> **Stuck?** If STING places the fixture but the tag shows `P-???-???-??-DCW-???-???-0001` with question marks, it means the spatial auto-detection could not find a room or zone at that location. Make sure your floor plan has rooms placed (Architecture → Room) before placing plumbing fixtures.

---

## 3.4 After placement: tagging

After placing fixtures (whether via the Placement Centre or manually), run the tagger:

1. In the STING panel, click the **CREATE** tab.
2. Click **Auto Tag**. This runs `AutoTagCommand`.
3. STING will tag all untagged plumbing elements in the active view:
   - `DISC` is set to `P` (Plumbing)
   - `SYS` is auto-detected from the connected Revit pipe system name
   - `PROD` is derived from the family name (a basin family named `BSN - Wall Hung` gets PROD code `BSN`)
   - `LOC`, `ZONE`, and `LVL` are detected from the room and level the fixture is in
   - `SEQ` is automatically incremented

4. The `PLM_EQP_TAG` parameter on each fixture receives its full ISO 19650 tag.

**Tip:** If you have placed fixtures across multiple floors, switch to a 3D view or use **Batch Tag** (also on the CREATE tab) to tag all plumbing elements in the entire project at once.

---

# Chapter 4 — Creating Pipe Systems

---

## 4.1 What is a Revit Pipe System?

A pipe system in Revit is a logical container — it groups connected pipes, fittings, and equipment under one name. Think of it as a colour-coded team: every member of the DCW team wears blue, every member of the SAN team wears orange. The system is what Revit uses for its internal calculations (flow balance, pressure drop), and it is what STING reads to assign the SYS token to each element.

When you create a pipe run in Revit, you must assign it to a system. If you do not, the pipe floats unassigned and STING cannot determine what it is carrying.

---

## 4.2 Creating pipe systems in Revit

**Step by step:**

1. Open the **Systems** tab in the Revit ribbon (this is a standard Revit tab, not a STING panel).
2. Click **Piping → Pipe System**. A "Create Pipe System" dialog appears.
3. In the **Name** field, type the STING system code exactly as listed in the table in Chapter 1.2 — for example, `DCW` for domestic cold water.
4. In the **System Classification** dropdown, choose the Revit classification that best matches:
   - DCW, DHW, HWS → `Domestic Cold Water`, `Domestic Hot Water` (or `Other` if those aren't listed)
   - SAN, RWD → `Sanitary`, `Storm` (or `Other`)
   - GAS → `Other`
5. Click OK.

> **Critical rule:** The system name in the **Name** field must exactly match the STING system code (e.g., `DCW`, `SAN`, `GAS`). Capitalisation matters. `dcw` will not be recognised. `Cold Water` will not be recognised. Only `DCW` will produce `SYS=DCW` in the tag.

Repeat this process for every system you need: DCW, DHW, HWS, SAN, RWD, GAS.

---

## 4.3 Assigning elements to systems

When you draw pipes in Revit using the Pipe tool (Systems tab → Pipe), Revit prompts you to select a system. Choose the system you created above.

When you place fixtures, connect them to pipes by using the pipe connector on the fixture family. Once physically connected, Revit automatically includes the fixture in the connected system.

If you have placed fixtures and pipes but they are not connected, use Revit's native connect tool: select a pipe end → right-click → "Draw Pipe" to connect to the next element.

**After connecting elements to systems, push the system data into the STING parameters:**

1. Select all plumbing elements you want to update (or press `Ctrl+A` to select all).
2. In the STING panel CREATE tab, in the Tag Operations section, click **System Push**. This runs `SystemParamPushCommand`.
3. STING traverses the Revit pipe system graph and writes the system code, flow data, and pressure data from the Revit system down into the PLM_* parameters on each element.

> **Stuck?** If **System Push** shows "0 elements updated", the pipes may not be connected to a named system. Select a pipe → look at Properties palette → find the "System Name" field. If it is blank, the pipe is not in any system. Connect it using Revit's pipe drawing tool.

---

# Chapter 5 — Pipe Routing

---

## 5.1 The two routing approaches

STING offers two ways to create pipe runs:

1. **AutoPipeDrop** — STING does the routing for you. You tell it the source point and the destination fixtures, and it calculates and places the pipe route, complete with elbows and fittings. Use this for any regular pressure or gravity system where the route follows a logical path.

2. **Manual routing** — You draw pipes one run at a time using the `ModelCreatePipeCommand` or Revit's native pipe tool. Use this for complex routing around obstructions, or for small point-to-point connections that do not follow a pattern.

Most plumbing projects use AutoPipeDrop for the main distribution runs and manual routing for the final connections to individual fixtures.

---

## 5.2 AutoPipeDrop — automatic pipe drops from distribution points

AutoPipeDrop is STING's pipe routing engine. It is found in the TAGS tab of the STING panel under the **Routing** sub-tab.

### 5.2.1 What AutoPipeDrop does

Imagine you are fitting out a floor of a hospital. The DCW riser comes up through a plant room and you need to branch from it to 14 wash basins spread across four rooms. Normally you would have to trace each individual run, work out where bends go, and place each pipe segment by hand.

AutoPipeDrop automates this: you select the 14 basins, tell STING where the riser connection point is, and STING calculates the most direct route for each drop, places the pipe segments, inserts elbows and tees, and assigns everything to the DCW system.

The engine works for all pipe systems: DCW, DHW, HWS, SAN, RWD, and GAS. For drainage systems (SAN, RWD), it also applies the correct slope to horizontal runs.

Think of AutoPipeDrop as a skilled pipe-fitter working from a schematic. You give it the start point (where the water comes from) and the end points (each fixture). It works out the most efficient route connecting them all.

### 5.2.2 Before you run AutoPipeDrop

Check all of these before clicking the button:

| Prerequisite | How to check | What to do if missing |
|---|---|---|
| Destination fixtures are placed | Select one in the model | Place fixtures (Chapter 3) |
| Destination fixtures are tagged | Properties show a tag in `PLM_EQP_TAG` | Run Auto Tag (Chapter 3.4) |
| Pipe systems exist and are named correctly | Systems tab shows named systems | Create systems (Chapter 4.2) |
| Pipe types exist in the project | Project Browser → Families → Pipe Types | Run Create Pipes (Chapter 2, Step 2) |
| A distribution/riser point is defined | An element exists at the branch-off point | Place a pipe cap or valve at the riser tee position |

### 5.2.3 Step by step: running AutoPipeDrop

1. **Select your destination fixtures.** In the Revit view, click one fixture, then hold `Ctrl` and click the others you want to connect. To select all basins on a floor at once, use the STING panel → SELECT tab → click **Plumbing** under the Category selectors. This selects all plumbing category elements in the active view.

2. **Open the Routing sub-tab.** In the STING panel, click the **TAGS** tab. Then click the **Routing** sub-tab (one of the sub-tabs along the top of the TAGS content area).

3. **Click Auto-drop.** The button is labelled **Auto-drop** and runs `AutoDropCommand` (which internally uses the `AutoPipeDrop` engine for pipe categories).

4. **The Auto Drop dialog appears.** Fill in these fields:

   | Field | What to enter | Example |
   |---|---|---|
   | **System** | The STING system code for this run | `DCW` |
   | **Pipe type** | Select from the dropdown list of pipe types in the project | `Copper — Half Hard` |
   | **Nominal diameter** | The pipe size in mm | `22` |
   | **Source point** | Click "Pick Source Point" then click the riser tee or distribution valve in the model | (you click a point) |
   | **Slope** | For pressure systems: leave as 0. For drainage (SAN, RWD): enter the required slope percentage | `0` for DCW; `1.25` for SAN |
   | **Max horizontal run** | Maximum distance a horizontal branch can travel before STING routes it down a vertical drop | `6000` (6 metres) |
   | **Route preference** | Choose "Ceiling void" to route above ceiling, or "Exposed" to route at fixture level | Depends on project |

5. **Click Generate.** STING calculates the routes and places the pipes. You will see pipes appearing in the view as the engine works.

6. **Review the result report.** A results dialog shows:
   - Number of fixtures connected
   - Total pipe length placed
   - Any fixtures that could not be connected (with reasons)
   - Any fittings that could not be placed (these need manual attention)

### 5.2.4 After AutoPipeDrop: immediate checks

After the engine completes, do these checks before moving on:

1. **Visual check:** Scroll through the view. Look for any pipe segments that appear to be floating, going in the wrong direction, or not connected to the fixture.

2. **Tag the new pipes:** The pipes placed by AutoPipeDrop are not yet tagged. Run **Auto Tag** again from the CREATE tab. The new pipes will be tagged with the correct SYS code from the system you specified.

3. **Validate fills:** Click **Validate fills** on the Routing sub-tab. This runs `ValidateFillsCommand` and checks that the pipe sizes are consistent with the design flow rates. Any oversized or undersized pipes are flagged.

> **Stuck?** If the engine says "No source point found", it means you clicked "Pick Source Point" but did not click on a connectable element. A "source point" must be an element with a pipe connector facing outward — a pipe cap, a valve, or an open pipe end. If you placed a piece of solid geometry (a wall, a room), nothing will connect to it.

> **Stuck?** If some fixtures show as "not connected" in the report, the most common cause is that the fixture's pipe connector is not at the expected height. Open the fixture in the Family Editor, check that the connector is at the correct Z-height for the system type (DCW connectors for a wall-hung basin should be at the centre of the supply pipework, typically 750–900 mm above finished floor level).

---

## 5.3 Manual pipe routing

For single pipe runs, short connections, or any section that AutoPipeDrop cannot handle:

**Using ModelCreatePipeCommand:**

1. In the STING panel, click the **MODEL** tab.
2. Click **Create Pipe**. This runs `ModelCreatePipeCommand`.
3. A dialog appears with fields for:
   - Pipe type (choose from the list)
   - System (choose the STING system code from the dropdown — e.g., `DCW`)
   - Nominal diameter (type the size in mm)
4. Click **Pick Start Point** → click the start point in the model.
5. Click **Pick End Point** → click the end point.
6. STING places the pipe segment and assigns it to the specified system.
7. Run **Auto Tag** to tag the new pipe.

**Using Revit's native pipe tool:**

You can also use Revit's built-in pipe drawing tool (Systems tab → Pipe). When using the native tool, make sure to select the correct pipe system from the Type Selector and the Properties palette. After placing pipes natively, run **System Push** (`SystemParamPushCommand`) to populate the PLM_* parameters from the system data.

---

## 5.4 Slope requirements for drainage pipes

### Why drainage pipes must slope

A sanitary or rainwater drainage pipe carries water mixed with waste solids. For the flow to self-cleanse (carry solids along without blockages), the water must move fast enough — but not so fast that the water runs away and leaves the solids behind.

The British Standard BS EN 12056-2 (for foul water drainage) and BS EN 12056-3 (for rainwater drainage) define the required slopes. Think of it like a playground slide: too gentle and you barely move; too steep and you fly off the end.

For a 100mm soil branch:
- **Minimum slope:** 1:80 (1.25%) — at this slope the flow velocity is approximately 0.7 m/s, just enough to carry solids.
- **Maximum slope:** 1:20 (5%) for branches (steeper and water separates from solids).
- **Ideal slope:** 1:40 to 1:50 (2.0–2.5%).

### The PLM_SLOPE_PCT parameter

STING stores the pipe slope as a percentage in the parameter `PLM_SLOPE_PCT`. A slope of 1:80 is 1.25%. A slope of 1:50 is 2.0%. A slope of 1:40 is 2.5%.

**How to set pipe slope in STING:**

1. Select the drainage pipe in the model.
2. In the Properties palette on the left, find the **PLM_SLOPE_PCT** parameter.
3. Type the required slope percentage (e.g., `1.25`).
4. This records the design slope for STING's validator. You must also set the geometric slope in Revit's pipe properties: in the Properties palette, find the **Slope** field (this is Revit's native field) and set it to match.

> **Important:** STING's `PLM_SLOPE_PCT` is a data parameter — it records the slope value for tagging, scheduling, and validation purposes. The actual geometric slope of the pipe in the 3D model is controlled by Revit's native **Slope** field in pipe properties. Both must be set to the same value for the model to be accurate.

**Recommended slope values by system:**

| System | Pipe type | Recommended slope | Minimum slope | Maximum slope | BS EN reference |
|---|---|---|---|---|---|
| SAN | Branch waste pipe (soil) | 2.0–2.5% | 1.25% | 5.0% | BS EN 12056-2, Table 4 |
| SAN | Long branch (>3 m) | 1.25% | 1.25% | 2.5% | BS EN 12056-2, Table 4 |
| SAN | Stack (vertical) | Vertical — no slope | N/A | N/A | N/A |
| SAN | Underground drain | 1.0–2.5% | 0.6% | 5.0% | BS EN 752, BS EN 1671 |
| RWD | Flat roof drainage branch | 0.5–1.0% | 0.3% | 2.0% | BS EN 12056-3 |
| RWD | Above-ground rainwater pipe | Vertical | N/A | N/A | N/A |
| RWD | Underground surface water | 1.0–1.5% | 0.5% | 5.0% | BS EN 752 |
| DCW / DHW / HWS / GAS | All (pressure pipes) | Not applicable | N/A | N/A | Pressure pipes do not slope to drain |

### STING slope validation

When you run `RunAllValidatorsCommand` (Chapter 9), the slope validator checks:

1. That every drainage pipe (SAN, RWD) has a `PLM_SLOPE_PCT` value set.
2. That the value is within the permitted range for the pipe size and system code.
3. That the geometric slope in Revit matches the declared `PLM_SLOPE_PCT` value (within a small tolerance).

If the validator flags a slope error, it will tell you:
- Which pipe failed
- What slope value was found
- What range is acceptable
- Whether the error is a gradient-too-shallow (risk of blockage) or gradient-too-steep (risk of surcharging)

**How to correct a slope validation failure:**

1. Select the pipe that failed.
2. Open Properties palette.
3. Set the Revit **Slope** field to the correct value (e.g., `2.00 %` or `1:50`).
4. Set `PLM_SLOPE_PCT` to the same numeric percentage (e.g., `2.0`).
5. Run the validator again to confirm the fix.

---

## 5.5 Pipe insulation

### Why pipes get insulated

Different systems need insulation for different reasons:

| System | Why insulate | Target thickness (typical) | Standard |
|---|---|---|---|
| DCW (in warm areas) | Prevent water temperature rising above 20°C — above this, Legionella bacteria can multiply | 25–38 mm | L8 ACOP, HSG274 Part 2 |
| DHW and HWS | Reduce heat loss from distribution pipework — saves energy and maintains temperature at point of use | 25–38 mm | BS 5422 Table 2 |
| SAN and RWD (in cold areas) | Prevent freezing in unheated spaces (roof voids, external walls, car parks) | 25 mm minimum | BS 6700 |
| GAS | Not typically insulated above ground (gas pipework is metal and rusts if moisture is trapped under insulation) | Not insulated | IGEM UP/2 |

### STING insulation parameters

STING records insulation data in two parameters on each pipe:

- `PLM_INSULATION_TXT` — the insulation material name (e.g., "Armaflex", "Rockwool", "PIB")
- `PLM_PPE_INSULATION_THK_MM` — the insulation thickness in millimetres

### Applying insulation data in bulk

Rather than setting these parameters one pipe at a time, use Bulk Parameter Write:

1. Select all the DCW pipes you want to insulate. (Use SELECT tab → Category → **Pipes** to select all pipes in the active view, then filter by system using Properties palette if needed.)
2. In the STING panel SELECT tab, click **Bulk Param Write**. This runs `BulkParamWriteCommand`.
3. In the dialog, choose parameter `PLM_INSULATION_TXT`, set value to `Armaflex` (or your specified material).
4. Click Apply. All selected pipes receive the insulation type.
5. Repeat for `PLM_PPE_INSULATION_THK_MM`, setting the appropriate thickness value.

---

# Chapter 6 — Drainage Design

---

## 6.1 Sanitary drainage vs rainwater drainage

These two drainage systems are separate and must never be connected to each other on new construction in the UK.

**Sanitary drainage (SAN)** carries foul water — the contaminated water from WCs, sinks, basins, baths, and kitchen drains. It discharges to the foul sewer, which takes it to a sewage treatment works.

**Rainwater drainage (RWD)** carries clean surface water — rain falling on the roof and paved areas. It discharges to the storm sewer, which takes it to a watercourse or soakaway. Because it is relatively clean, it must not be contaminated by foul water.

Connecting RWD to SAN (or vice versa) creates a "combined drain" — illegal on new construction under Building Regulations Part H and the Water Industry Act 1991. Always keep them separate from the point of collection all the way to the point of discharge.

---

## 6.2 The SAN system

The sanitary drainage system collects waste from:
- WCs and urinals (soil connection — 100 mm minimum pipe)
- Wash basins (waste connection — typically 32–40 mm branch, running into 50 mm or 100 mm stack)
- Baths and showers (waste connection — 40 mm branch)
- Kitchen sinks (40 mm branch)
- Floor gullies and cleaners' sinks (40 mm or 100 mm branch)

**Creating a sanitary drainage layout in STING:**

1. **Place sanitary fixtures** (Chapter 3) — WCs, basins, baths.
2. **Create a SAN pipe system** (Chapter 4.2) — name it exactly `SAN`.
3. **Route waste branches to the stack:**
   - Use AutoPipeDrop: select the fixtures, choose system `SAN`, set pipe type `HDPE Drainage` or `Cast Iron Soil`, set nominal diameter (40 mm for wastes, 100 mm for soil), set slope to `2.0`%, set source point at the stack connection point.
   - AutoPipeDrop will route each branch from the fixture to the stack, sloped correctly.
4. **Route the stack** — the vertical soil stack is a vertical pipe with no slope. Place it manually using `ModelCreatePipeCommand` or Revit's pipe tool, with a vertical orientation.
5. **Connect branches to the stack** — STING's AutoPipeDrop will have placed the branches at the correct level; use Revit's connection tools to connect each branch to the stack with a swept tee fitting.
6. **Validate** — Run `RunAllValidatorsCommand`. The slope, connectivity, and termination validators will check the entire SAN system.
7. **Create a sanitary drainage schedule** — Chapter 8.3.

**Vent pipes:** BS EN 12056-2 requires that every sanitary system has a vent to atmosphere to prevent trap siphonage. The soil stack itself typically extends above the roof line as a vent. Individual branch pipes longer than 3 m (for 32mm pipes) need additional venting — either by extending them to a vent stack, or by fitting air admittance valves (AAVs). Record vent connection locations in `PLM_DRN_VNT_TERMINAL_LOC_TXT` and `PLM_VENT_CONNECTED_TO_TXT`.

---

## 6.3 The RWD system

Rainwater drainage collects surface water from:
- Flat roof drainage outlets (typically 100 mm or 150 mm)
- Gutters and hoppers on pitched roofs (typically 100 mm diameter, 112 mm half-round gutters)
- Rainwater downpipes (typically 68 mm circular, 65×65 mm square)

**Pipe sizing for RWD** uses the "rational method": multiply the catchment area (m²) by the design rainfall intensity (l/s per m²) to get the design flow rate, then size the pipe to carry that flow at the chosen slope.

In the UK, design rainfall intensities are taken from CIBSE Guide G or BS EN 12056-3: typically 0.014 l/s/m² for 2-minute storms in most of England, up to 0.025 l/s/m² in Scotland.

STING does not calculate pipe sizes automatically — it records the sizes you specify in `PLM_PPE_SZ_MM`. The sizing calculation is done by the engineer using BS EN 12056-3 methods.

---

## 6.4 Trap requirements

Every sanitary appliance must have a trap — a water seal that prevents sewer gases from entering the building. BS EN 274 and the Building Regulations Part H require:

| Appliance | Minimum trap seal depth | Trap type |
|---|---|---|
| WC | 50 mm | Integral (built into pan) |
| Basin | 75 mm | P-trap or bottle trap |
| Bath / shower | 50 mm | P-trap |
| Kitchen sink | 75 mm | P-trap |
| Floor gully | 50 mm | Gully trap |

Record these in `PLM_DRN_TRAP_TYPE_TXT` and `PLM_DRN_TRAP_SEAL_DEPTH_MM`.

---

# Chapter 7 — Gas Systems

---

## 7.1 The GAS system code in STING

STING treats gas supply as system code `GAS`. This covers:
- Natural gas (NG) from the national grid
- Liquefied petroleum gas (LPG) from a bulk storage tank
- Biogas from a site digester

All three share the same STING system code because they follow the same general routing and tagging approach within the building. The specific gas type is recorded in the `PLM_PPE_MAT_TXT` or the equipment parameter field.

---

## 7.2 Key considerations for gas piping

| Consideration | Rule | Where recorded in STING |
|---|---|---|
| Pipe material | Steel (black or galvanised) for above-ground; polyethylene for below-ground | `PLM_PPE_MAT_TXT` |
| Plastic prohibition | No plastic pipe inside a building above ground | N/A — flag in validation if plastic material is selected |
| Pressure tier | Low pressure ≤21 mbar; medium pressure 21 mbar–2 bar | `PLM_PPE_PSR_RATING_BAR` |
| Sleeve through walls | Gas pipe must be sleeved where it passes through a wall or floor | `PLM_DRN_BODY_MAT_TXT` or notes |
| No enclosed ducts | Above-ground gas pipe must not be run in an enclosed duct unless ventilated | Notes only |
| Bonding | All metallic gas pipework must be cross-bonded to earth | Recorded in electrical BIM |
| Emergency control valve | An Emergency Control Valve (ECV) must be accessible near each appliance | Placed as a valve family; tagged with system `GAS` |
| Isolation valve | A gas isolation valve must be fitted to each branch serving an appliance | Same as above |

---

## 7.3 Creating gas pipe layouts

The workflow for gas piping follows the same sequence as DCW:

1. **Place gas appliances** (boilers, cookers, laboratory benches) — Chapter 3.3.
2. **Create the GAS pipe system** — Chapter 4.2. Name it exactly `GAS`.
3. **Route pipes from the meter to each appliance:**
   - AutoPipeDrop works for gas: select appliances → click Auto-drop → choose system `GAS` → choose pipe type `Black Steel` → choose nominal diameter → set source point at the meter outlet.
   - Set slope to `0` (gas pipes do not slope unless serving a condensate trap).
4. **Tag** — Run Auto Tag. All gas pipes get `DISC=P`, `SYS=GAS`.
5. **Validate** — Run `RunAllValidatorsCommand`. The connectivity validator checks every gas appliance is connected to the GAS system.

---

## 7.4 STING does NOT design gas pressure calculations

STING records gas system data and produces drawings, schedules, and tags — but it does not calculate pressure drop through gas pipe networks. Gas pressure design requires:

1. Knowing the appliance gas rates (kW input / calorific value = m³/h)
2. Summing demands with a diversity factor
3. Calculating pressure drop through each run using the Weymouth or Spitzglass formula
4. Ensuring the pressure available at each appliance (after all losses) is above the minimum required

These calculations are performed using specialist gas design software (e.g., CIBSE TM20, or the gas network design calculators in IGEM UP/2) or manually per IGEM/UP/1B. The results — pipe sizes, operating pressures, gas rates — are then entered into STING's parameters for record purposes.

Record gas design results in:
- `PLM_PPE_SZ_MM` — pipe nominal diameter selected from the calculation
- `PLM_PPE_PSR_RATING_BAR` — rated pressure of the pipework
- `PLM_PSR_KPA` — operating pressure at this point in kPa
- `PLM_EQP_CAPACITY_L` — for storage vessels (propane tanks, LPG cylinders)

---

# Chapter 8 — Plumbing Schedules

---

## 8.1 What plumbing schedules are produced

A complete plumbing package typically includes three types of schedules:

| Schedule type | What it lists | Used by |
|---|---|---|
| **Plumbing fixture schedule** | Every sanitary and plumbing fixture: WCs, basins, baths, showers, floor drains — with their tag, flow rates, connection sizes, location, and room | Contractor for ordering and installation |
| **Pipe schedule (by system)** | Every pipe run: system, pipe type, nominal diameter, length, slope (for drainage), insulation — for each of DCW, DHW, HWS, SAN, RWD, GAS | Contractor for material take-off; engineer for checking |
| **Insulation schedule** | All insulated pipes: pipe tag, system, size, insulation material, thickness, area of insulation | Insulation contractor; QS for pricing |

---

## 8.2 Creating a plumbing fixture schedule

1. In the STING panel, click the **TEMP** tab.
2. Scroll to the **Schedules** section.
3. Click **MEP Plumbing Fixtures**. This runs `PlumbingFixtureScheduleCommand` (also accessible via command tag `MEPSchedulePlumb`).
4. STING creates a schedule called "STING - Plumbing Fixture Schedule" with these columns:

   | Column | Parameter | Notes |
   |---|---|---|
   | Tag | `PLM_EQP_TAG` | Full ISO 19650 tag |
   | Family | Family name | The fixture type |
   | Level | `ASS_LVL_COD_TXT` | Level code |
   | Room | Room name | From spatial data |
   | System | `PLM_SYSTEM_TXT` | DCW, DHW, SAN etc. |
   | Hot connection | `PLM_PPE_SZ_MM` | Hot supply pipe size |
   | Cold connection | `PLM_PPE_SZ_MM` | Cold supply pipe size |
   | Waste connection | `PLM_PPE_SZ_MM` | Waste pipe size |
   | Flow rate (l/s) | `PLM_FLOW_RATE_LPS` | Design flow rate |
   | Status | `ASS_STATUS_TXT` | NEW / EXISTING |

5. The schedule opens automatically. You can filter it by system, sort by level, or group by room.

**Exporting to Excel:**

1. Select the schedule in the Project Browser (Views → Schedules → STING - Plumbing Fixture Schedule).
2. In the STING panel BIM tab, click **Export Schedules to Excel**. This runs `ExportSchedulesToExcelCommand`.
3. Choose the schedule name and click Export. The schedule is written to an `.xlsx` file in your project output folder.

---

## 8.3 Creating a pipe schedule by system

1. In the STING panel, click the **TEMP** tab.
2. In the Schedules section, click **Batch Create**. This runs `BatchSchedulesCommand`.
3. STING creates one schedule per system present in the project. You will get:
   - "STING - Pipe Schedule - DCW"
   - "STING - Pipe Schedule - DHW"
   - "STING - Pipe Schedule - SAN"
   - (and so on for every system found)
4. Each schedule lists all pipes in that system with: pipe tag, nominal diameter, material, length, slope (for drainage), insulation type, insulation thickness.

**Filtering by system:** Each schedule is automatically filtered to its system code via `PLM_SYSTEM_TXT`. You do not need to set this up manually.

---

## 8.4 Creating an insulation schedule

The insulation schedule is generated as part of the pipe schedule. It uses the same `BatchSchedulesCommand` output but focuses on the insulation parameter columns. To produce a dedicated insulation schedule:

1. In the Revit Project Browser, right-click one of the pipe schedules.
2. Click Duplicate → Duplicate with Detailing.
3. In the new schedule, open Schedule Properties (View tab → Properties → Edit Fields).
4. Remove all columns except: Tag, System, Nominal Size, Insulation Material, Insulation Thickness, Pipe Length.
5. Add a calculated column for Insulation Area (in m²) = `PLM_PPE_LENGTH_M` × (pipe circumference + insulation thickness).
6. Rename the schedule to "STING - Insulation Schedule".

---

# Chapter 9 — Validation and QA

---

## 9.1 RunAllValidatorsCommand for plumbing projects

STING's validation engine runs five checks on plumbing elements. Think of it as a digital commissioning checklist: the same checks a commissioning engineer would make before signing off the installation.

**How to run:**

1. In the STING panel, click the **TAGS** tab.
2. Click the **Routing** sub-tab.
3. Click **Validate fills**. This runs `ValidateFillsCommand`, which in turn triggers the full `RunAllValidatorsCommand` suite.

Alternatively, the full validator suite can be accessed directly via the command tag `Validation_RunAll`.

**The five validators and what they check:**

| Validator | What it checks for plumbing | Standard reference |
|---|---|---|
| **Slope validator** | Checks `PLM_SLOPE_PCT` on every SAN and RWD pipe. Compares against permitted range for pipe size. | BS EN 12056-2, BS EN 752 |
| **Fill validator** | For drainage pipes: checks fill ratio (calculated flow / maximum flow capacity) is ≤ 0.7 per Maguire's formula. For pressure pipes: checks velocity is ≤ 3.0 m/s (DCW) or ≤ 1.5 m/s (DHW circulation). | BS EN 12056-2, CIBSE Guide G |
| **Connectivity validator** | Checks that every plumbing fixture is connected to an appropriate pipe system (e.g., a WC is connected to SAN, a basin is connected to both DCW and SAN). | ISO 19650-2, BS EN 806-2 |
| **Termination validator** | Checks that every pipe system has a defined termination point (a discharge to sewer, a storage tank, a meter connection). Systems without a termination point are flagged. | Building Regs Part H, BS EN 806-2 |
| **Spec validator** | Checks that key parameters are populated on every pipe: `PLM_PPE_SZ_MM`, `PLM_PPE_MAT_TXT`, and `PLM_SYSTEM_TXT`. Unfilled parameters flag as incomplete. | ISO 19650-1, project BEP |

---

## 9.2 Reading the validation report

When `RunAllValidatorsCommand` completes, a results panel appears. This is what you will see:

```
STING Validation Report — Plumbing (Discipline: P)
═══════════════════════════════════════════════════

[PASS] Connectivity validator: 47 fixtures connected, 0 unconnected
[PASS] Termination validator: 6 systems, all terminated

[FAIL] Slope validator: 3 failures
  ► P-BLD1-Z01-L01-SAN-DRN-PPC-0004: slope 0.80% is below minimum 1.25% for 100mm SAN
  ► P-BLD1-Z01-L01-SAN-DRN-PPC-0007: slope 0.80% is below minimum 1.25% for 100mm SAN
  ► P-BLD1-Z01-L02-RWD-DRN-PPC-0003: no slope set (PLM_SLOPE_PCT is blank)

[WARN] Fill validator: 1 warning
  ► P-BLD1-Z01-L01-SAN-DRN-PPC-0012: fill ratio 0.82 (design flow 4.8 l/s, capacity 5.9 l/s)
    Recommendation: increase pipe from 100mm to 150mm

[FAIL] Spec validator: 5 failures
  ► PLM_PPE_MAT_TXT is blank on 5 DCW pipes on Level 02

═══════════════════════════════════════════════════
SUMMARY: 2 categories failed, 1 warning, 1 category passed
```

**How to read each section:**

- `[PASS]` — No action needed.
- `[FAIL]` — These must be fixed before the drawing can be issued. The tag number tells you exactly which pipe to select.
- `[WARN]` — These do not block issue but should be reviewed by the engineer. The fill ratio warning above is telling you a pipe is oversized (0.82 > 0.7) — it is carrying more flow than it should.

**How to fix failures:**

1. Note the tag numbers of all failing pipes (e.g., `P-BLD1-Z01-L01-SAN-DRN-PPC-0004`).
2. In Revit, use the ORGANISE tab → Analysis → **Highlight Invalid** (`HighlightInvalidCommand`). This colours failing elements in red so you can find them quickly on the drawing.
3. Select the failing pipe → fix the issue (set slope, set material, etc.) → re-run the validator.
4. Repeat until all `[FAIL]` items are cleared.

---

## 9.3 ValidateTagsCommand for plumbing elements

After fixing validator failures, run the tag validator to confirm every plumbing element has a complete, ISO 19650-compliant tag.

1. In the STING panel, click the **CREATE** tab.
2. In the QA section, click **Validate**. This runs `ValidateTagsCommand`.
3. For plumbing elements, the validator checks:
   - `DISC` = `P` (flag if any plumbing element has DISC set to something else)
   - `SYS` is one of the valid plumbing system codes (DCW, DHW, HWS, SAN, RWD, GAS)
   - `PROD` code matches the element category (e.g., a pipe should have a valid pipe product code)
   - All eight segments are populated (no blanks)

**Common tag errors and fixes:**

| Error | Cause | Fix |
|---|---|---|
| `SYS` is blank | Pipe is not connected to a named system | Run System Push (`SystemParamPushCommand`) |
| `SYS` shows "Cold Water" | Revit system was not named using the STING code | Rename the Revit pipe system to `DCW` then re-tag |
| `DISC` shows `M` on a gas pipe | Gas pipe was categorised under Mechanical | Manually set `ASS_DISCIPLINE_COD_TXT` = `P` on affected elements |
| `PROD` is blank | Family name does not match any STING PROD code | Open `ConfigEditorCommand` → check PROD code mappings for your family names |

---

# Chapter 10 — Producing and Issuing Plumbing Drawings

---

## 10.1 Applicable drawing types

STING ships two drawing type profiles relevant to plumbing work:

| ID | Purpose | Scale | Paper | When to use |
|---|---|---|---|---|
| `plumb-drainage-A1-1to100` | Dedicated drainage and plumbing service drawing | 1:100 | A1 landscape | Drainage plans, cold water plans, hot water plans — each shown on a separate sheet |
| `mep-plan-A1-1to100` | Combined MEP services drawing showing all disciplines | 1:100 | A1 landscape | Coordination drawings where plumbing is shown alongside HVAC and electrical |

For most plumbing packages, use `plumb-drainage-A1-1to100`. Use `mep-plan-A1-1to100` for coordination reviews.

---

## 10.2 Producing drawings

For full step-by-step drawing production instructions — creating views, placing them on sheets, applying the drawing type profile, setting crops, numbering sheets — see:

> See **DRAWING_PRODUCTION_SYSTEM_GUIDE.md** for the complete drawing production process.

The drawing type `plumb-drainage-A1-1to100` is already configured in STING with the correct scale, view template, and title block. When you use it via the Sheet Manager or the Drawing Types workflow, these settings are applied automatically.

---

## 10.3 Issuing drawings

For the complete document issue workflow — creating a transmittal, setting the suitability code (for construction, for approval, etc.), distributing to recipients, and maintaining the ISO 19650 document register — see:

> See **DOCUMENT_MANAGER_GUIDE.md** for the complete document issue process.

---

# Troubleshooting

| Problem | Likely cause | Fix |
|---|---|---|
| **Pipe not connecting to fixture** | The fixture's pipe connector is not at the right location, or the pipe type does not match the connector type | Open the fixture family in the Family Editor; check connector location and type. Also check pipe system types match (cannot connect a Hydronic Supply system to a Domestic Cold Water pipe) |
| **AutoPipeDrop places pipe in wrong location** | Source point was not defined correctly — STING routed to the wrong origin | Re-run Auto-drop → click "Pick Source Point" more carefully, clicking directly on the pipe connector of the riser/distribution valve |
| **AutoPipeDrop says "no source point found"** | You clicked on a non-connectable element (a room, a wall, a column) | The source point must be an element with an exposed pipe connector — a pipe cap, a valve with an unconnected port, or an open pipe end |
| **Slope validation fails even though slope is set** | Slope was set on the wrong axis — Revit pipe slope must be on the Z axis (vertical) not X or Y | Select the pipe → Properties palette → check the **Slope** field (not just `PLM_SLOPE_PCT`). If the pipe is running vertically and shows a slope value, delete the slope |
| **Slope validation fails: "PLM_SLOPE_PCT is blank"** | The STING parameter was never set on this pipe | Select the pipe → Properties palette → find `PLM_SLOPE_PCT` → type the slope percentage |
| **PLM_* parameters not appearing in Properties** | Shared parameters were not loaded, or not bound to the Pipe category | Run `LoadSharedParamsCommand` again. Check the results show "PLM_DRN" group bound successfully |
| **System code wrong in tag — shows blank or wrong code** | Revit pipe system was not named with the STING code | In the Revit Systems panel, rename the pipe system to the exact STING code (e.g., `DCW`), then run `SystemParamPushCommand` |
| **Drainage schedule missing fittings** | Fittings were placed but not connected to the SAN system | Select the fittings → check Properties palette for "System Name" field. If blank, drag-connect the fittings to a connected pipe |
| **Gas pipes tagged as P with wrong SYS code** | Gas system was created after pipes were placed and pipes were not assigned to it | Select all gas pipes → in Revit Systems tab, use "Edit System" to add them to the GAS system → run `SystemParamPushCommand` → re-run Auto Tag |
| **Auto Tag assigns wrong PROD code** | The plumbing fixture family name does not contain a recognisable abbreviation | Open `ConfigEditorCommand` (STING panel → CREATE tab → Setup → Configure) → in the PROD code mapping section, add a mapping for your family name |
| **Fill validator shows warning for drainage pipe** | Design flow exceeds 70% of pipe capacity at the specified slope | Either increase the pipe diameter, or increase the slope (if geometry allows) — re-run validator to confirm |
| **Connectivity validator fails for isolated pipe section** | Pipe section is not connected to the rest of the system | Zoom into the failing pipe tag → look for a gap between the pipe end and the connected element → use Revit's connect tool to close the gap |
| **RunAllValidatorsCommand completes instantly with no results** | No plumbing elements are in the active selection or view | Make sure you are in a view that contains plumbing elements, or change to a 3D view showing all levels |

---

# Quick Reference

---

## All plumbing commands

| Button label | Panel tab | Sub-tab | What it does | When to use it |
|---|---|---|---|---|
| **Load Params** | CREATE | Setup | Binds PLM_DRN shared parameters to Revit categories | Once per project, at project setup |
| **Pipes** | TEMP | Families | Creates standard pipe type families from STING CSV | Once per project, at project setup |
| **Inspect Drawing Types** | DOCS | Drawing Types | Lists all drawing types and flags missing assets | At setup and before drawing production |
| **Place Fixture** | MODEL | (main) | Places a single plumbing fixture at a picked point | One-off fixture placement |
| **Auto Tag** | CREATE | (main) | Tags all untagged plumbing elements in active view | After placing fixtures or pipes |
| **Batch Tag** | CREATE | (main) | Tags all plumbing elements in entire project | After any bulk placement operation |
| **System Push** | CREATE | Tag Operations | Writes Revit system data into PLM_* parameters | After connecting pipes to systems |
| **Auto-drop** | TAGS | Routing | Routes pipes automatically from source to fixtures | Main pipe routing operation |
| **Validate fills** | TAGS | Routing | Runs fill, slope, connectivity, and termination validators | After all pipe routing is complete |
| **Bulk Param Write** | SELECT | (main) | Sets any parameter on all selected elements at once | Setting insulation material, slope |
| **MEP Plumbing Fixtures** | TEMP | Schedules | Creates plumbing fixture schedule | After tagging all fixtures |
| **Batch Create** (schedules) | TEMP | Schedules | Creates pipe schedules for every system present | After all pipe routing and tagging |
| **Export Schedules to Excel** | BIM | Excel | Exports any Revit schedule to .xlsx | Preparing handover data |
| **Validate** | CREATE | QA | Validates ISO 19650 tag completeness | Before drawing production |
| **Highlight Invalid** | ORGANISE | Analysis | Colours elements with incomplete tags red/orange | While fixing tag errors |
| **Completeness Dashboard** | CREATE | QA | Shows per-discipline compliance percentages | Progress monitoring |

---

## Plumbing system codes — complete reference

| Code | Full name | Revit system classification to use | Standards | Typical pipe materials | Pressure / gravity |
|---|---|---|---|---|---|
| `DCW` | Domestic Cold Water | Domestic Cold Water | BS EN 806-2, WRAS | Copper, CPVC, stainless | Pressure (1–10 bar) |
| `DHW` | Domestic Hot Water | Domestic Hot Water | BS EN 806-2, L8 ACOP | Copper, stainless | Pressure (0.5–4 bar) |
| `HWS` | Hot Water Services Circulation | Domestic Hot Water (return) | BS EN 806-2, L8 ACOP | Copper, stainless | Pumped low-pressure |
| `SAN` | Sanitary Drainage | Sanitary | BS EN 12056-2, Building Regs Part H | HDPE, cast iron, uPVC | Gravity |
| `RWD` | Rainwater Drainage | Storm | BS EN 12056-3, Building Regs Part H | uPVC, HDPE, cast iron | Gravity |
| `GAS` | Gas Supply | Other | IGEM UP/2, Gas Safety Regs | Black steel, copper | Low/medium pressure |

---

## PLM_* parameter reference

| Parameter name | What it stores | Example value | Which elements |
|---|---|---|---|
| `PLM_PPE_SZ_MM` | Pipe nominal diameter (mm) | `22`, `100` | Pipes |
| `PLM_PPE_MAT_TXT` | Pipe material | `Copper`, `HDPE` | Pipes |
| `PLM_PPE_JOINT_TYPE_TXT` | Joint type | `Soldered`, `Compression` | Pipes, fittings |
| `PLM_PPE_LENGTH_M` | Pipe length (m) | `3.65` | Pipes |
| `PLM_FLOW_RATE_LPS` | Design flow rate (l/s) | `0.15` | Pipes |
| `PLM_VEL_MPS` | Water velocity (m/s) | `1.5` | Pipes |
| `PLM_PSR_KPA` | Pressure at this point (kPa) | `150` | Pipes, equipment |
| `PLM_PPE_PSR_RATING_BAR` | Pressure rating of pipe (bar) | `10` | Pipes |
| `PLM_SYSTEM_TXT` | System code | `DCW`, `SAN` | Pipes, fixtures |
| `PLM_SLOPE_PCT` | Slope percentage for drainage | `1.25`, `2.0` | Drainage pipes |
| `PLM_INSULATION_TXT` | Insulation material | `Armaflex` | Pipes |
| `PLM_PPE_INSULATION_THK_MM` | Insulation thickness (mm) | `25`, `38` | Pipes |
| `PLM_EQP_CAPACITY_L` | Equipment storage volume (L) | `200` | Tanks, cylinders |
| `PLM_EQP_PRESSURE_KPA` | Equipment design pressure (kPa) | `300` | All plumbing equipment |
| `PLM_HOTWTR_TEMP_C` | Hot water storage temperature (°C) | `65` | Calorifiers, cylinders |
| `PLM_HOTWTR_INPUT_PWR_KW` | Water heater heat input (kW) | `24` | Boilers, heaters |
| `PLM_HOTWTR_FUEL_TYPE_TXT` | Fuel type | `Gas`, `Heat Pump` | Boilers, heaters |
| `PLM_HED_M` | Static head (m) | `12.5` | Pumps, tanks |
| `PLM_DRN_TRAP_TYPE_TXT` | Trap type | `P-trap`, `Bottle trap` | Drainage fittings |
| `PLM_DRN_TRAP_SEAL_DEPTH_MM` | Trap seal depth (mm) | `75` | Drainage fittings |
| `PLM_DRN_FLW_RATE_LPS` | Drainage flow rate (l/s) | `0.08` | Drainage pipes |
| `PLM_DRN_PPE_SLOPE_PCT` | Slope on drainage pipe (%) | `1.25` | Drainage pipes |
| `PLM_VLV_BODY_MAT_TXT` | Valve material | `Brass`, `Bronze` | Valves |
| `PLM_VLV_END_CONNECTION_TXT` | Valve connection type | `Compression`, `Flanged` | Valves |
| `PLM_VLV_ACTUATION_TYPE_TXT` | How the valve is operated | `Manual`, `Motorised` | Valves |
| `PLM_EQP_TAG` | Plumbing equipment tag container | `P-BLD1-Z01-L02-DCW-SUP-BSN-0001` | Fixtures, equipment |

---

## Slope requirements by system

| System | Application | Min slope | Recommended | Max slope | BS EN reference |
|---|---|---|---|---|---|
| SAN | WC branch (100 mm) | 1.25% | 2.0% | 5.0% | BS EN 12056-2, Table 4 |
| SAN | Wash basin waste (32–40 mm) | 2.0% | 2.5% | 5.0% | BS EN 12056-2 |
| SAN | Long branch >3 m (any size) | 1.25% | 1.5% | 2.5% | BS EN 12056-2 |
| SAN | Soil stack (vertical) | Vertical | Vertical | Vertical | N/A |
| SAN | Underground foul drain | 0.6% | 1.25% | 5.0% | BS EN 752 |
| RWD | Flat roof drainage branch | 0.3% | 0.5–1.0% | 2.0% | BS EN 12056-3 |
| RWD | Rainwater downpipe (vertical) | Vertical | Vertical | Vertical | N/A |
| RWD | Underground surface water drain | 0.5% | 1.0% | 5.0% | BS EN 752 |
| DCW / DHW / HWS | All (pressure system) | Not applicable | Not applicable | Not applicable | Pressure pipes drain back to lowest point if needed — not a routing constraint |
| GAS | All (pressure system) | Not applicable (condensate traps are special cases) | Not applicable | Not applicable | IGEM UP/2 |

---

## Glossary

**AutoPipeDrop:** STING's automated pipe routing engine. Given a source point (where water comes from) and a set of destination fixtures, it calculates and places the pipe route, fittings, and elbows automatically.

**BS EN 12056:** The British/European Standard for gravity drainage systems inside buildings. Part 2 covers sanitary pipework; Part 3 covers roof drainage.

**CIBSE:** The Chartered Institution of Building Services Engineers. Publishes guides on HVAC, plumbing, and building services design. CIBSE Guide G covers public health engineering.

**DCW:** Domestic Cold Water. Mains cold water supply distributed throughout a building to taps, WCs, and appliances.

**DHW:** Domestic Hot Water. Hot water produced by a boiler or heat pump and distributed to hot taps.

**Discipline code P:** STING's code for plumbing and public health. All elements tagged with `P` as the first segment of their ISO 19650 tag.

**Fill ratio:** The ratio of actual flow in a pipe to the maximum flow capacity of that pipe at the design slope and size. For drainage pipes the maximum recommended fill ratio is 0.7 (70% full) per BS EN 12056-2.

**HDPE:** High-density polyethylene. A plastic pipe material used for underground drainage and sometimes above-ground waste pipework.

**HWS:** Hot Water Services. The circulation return leg of a hot water system — water returning from distribution pipes back to the calorifier to be reheated.

**ISO 19650:** The international standard for managing information over the whole life cycle of a built asset using Building Information Modelling (BIM). The eight-segment tag format used by STING is based on ISO 19650-2.

**L8 ACOP:** The Approved Code of Practice for Legionella control, published by the Health and Safety Executive. Governs hot and cold water system design and maintenance to prevent Legionella bacteria growth.

**Legionella:** A bacterium that grows in water systems where temperatures are between 20–45°C. Can cause Legionnaires' disease (a severe form of pneumonia). DCW must be kept cold (below 20°C) and DHW must be stored hot (above 60°C) to prevent growth.

**Maguire's formula:** The standard formula used in BS EN 12056-2 for calculating the flow capacity of drainage pipes at a given slope. STING's fill validator uses this formula.

**PLM_DRN:** The STING shared parameter group containing all plumbing and drainage parameters.

**PLM_EQP_TAG:** The tag container parameter on plumbing fixtures and equipment. Holds the full ISO 19650 eight-segment tag.

**PRV:** Pressure Reducing Valve. A valve that reduces mains water pressure to a lower design pressure. Used where mains pressure exceeds the safe pressure for the building's plumbing system.

**RWD:** Rainwater Drainage. The system that collects and discharges surface water from roofs and paved areas.

**SAN:** Sanitary Drainage. The foul water drainage system collecting from WCs, sinks, baths, and floor drains.

**Slope validator:** One of STING's five validation checks. It verifies that all drainage pipes (SAN, RWD) have a slope value set, and that the slope is within the permitted range for the pipe size and system type.

**Soil stack:** A vertical pipe collecting soil and waste branches from multiple floors and discharging to the underground drain below ground level.

**System code:** The five-letter (or three-letter) code assigned to a pipe system: DCW, DHW, HWS, SAN, RWD, GAS. STING reads this from the Revit pipe system name.

**TMV:** Thermostatic Mixing Valve. A valve that blends hot and cold water to deliver water at a safe temperature (typically 38–43°C at the outlet) regardless of variations in hot or cold supply temperatures.

**Trap seal:** The water held in the bend of a drainage trap (P-trap, S-trap, bottle trap) that forms a barrier against sewer gases entering the building. The minimum seal depth is 75 mm for most sanitary appliances.

**uPVC:** Unplasticised polyvinyl chloride. The most common above-ground drainage pipe material (white or grey).

---

# Cross-references

> **This guide uses information from:**
> - `MEP_FOUNDATION_GUIDE.md` — MEP symbol creation (Chapter 1), family authoring to make families placement-ready (Chapter 2), and the Placement Centre for automated fixture placement (Chapter 3). Read this guide first if you are creating new plumbing fixture families.
> - `DRAWING_PRODUCTION_SYSTEM_GUIDE.md` — Complete drawing production workflow: creating views, placing them on sheets, applying drawing type profiles, numbering sheets.
> - `DOCUMENT_MANAGER_GUIDE.md` — Document issue workflow: transmittals, suitability codes, the ISO 19650 document register, revision management.

> **This guide is used by:**
> - `HEALTHCARE_WORKFLOW_GUIDE.md` — Medical gas systems (MGPS: oxygen, nitrogen, vacuum) extend the GAS system code. Water safety for Legionella control in healthcare buildings extends the DCW/DHW workflow. This plumbing guide covers the foundations that the healthcare guide builds on.
