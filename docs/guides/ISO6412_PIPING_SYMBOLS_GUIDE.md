# ISO 6412 Piping & Duct Spool Symbols — Layman's Guide

**What this covers**: Every symbol you see on a fabrication spool sheet or shop drawing when the _Generate Fabrication Package_ command runs. These symbols appear on the cross-section / axonometric view of each pre-fabricated spool.

**What this does NOT cover**: Plan-view MEP annotation symbols (see `MEP_SYMBOL_COLOUR_SCALE_GUIDE.md`) or single-line diagram (SLD) symbols (see `SLD_SYMBOLS_LAYMANS_GUIDE.md`).

---

## 1. What Is ISO 6412?

ISO 6412 is an international standard (published by the International Organization for Standardization) that tells engineers exactly which drawing shape to use when showing a pipe fitting, valve, or duct component on a fabrication section drawing.

**Think of it like this:** the same way road signs look the same in every country so drivers recognise them instantly, ISO 6412 shapes mean the same thing to every pipefitter or ductwork fabricator anywhere in the world — even if they speak different languages.

The standard covers three parts:
- **ISO 6412-1**: Pipe fittings and valves (elbows, tees, reducers, flanges, valves)
- **ISO 6412-2**: Duct fittings (HVAC ductwork elbows, tees, reducers, dampers)
- **ISO 6412-3**: Supplementary items (instruments, sensors, drives)

In STING, the catalogue contains **188 symbols** covering all three parts plus conduit/electrical tray items.

---

## 2. Where These Symbols Appear

When you click **Generate Fabrication Package** in the Fabrication tab, STING:
1. Groups your pipes/ducts/conduits into logical spools (assemblies).
2. Creates an **ISO 6412 axonometric section view** for each spool — this is a cut-through view showing every fitting in the spool laid out flat.
3. Drops a **Detail Item family symbol** onto each fitting's location in that section view.

The result looks like a professional pipe-shop isometric drawing, where each fitting is represented by its internationally-recognised symbol rather than a realistic 3D shape.

---

## 3. The 188 Symbol Catalogue

### Pipe Symbols (ISO 6412-1) — ~120 symbols

#### Elbows
| Symbol code | What it is | What it looks like (described) |
|---|---|---|
| `ELBOW_90_BW` | 90° butt-weld elbow | Two lines meeting at a right angle with a curved arc at the corner |
| `ELBOW_45_BW` | 45° butt-weld elbow | Two lines meeting at 45° with a small arc |
| `ELBOW_90_SW` | 90° socket-weld elbow | Same angle but with small rings at ends showing the socket |
| `ELBOW_90_THR` | 90° threaded elbow | Small diagonal cross-hatch marks at ends indicating threaded connection |
| `ELBOW_90_FL` | 90° flanged elbow | Two parallel short lines at each end representing the flange faces |
| `ELBOW_LR` | Long-radius elbow | 90° elbow with a longer sweep radius (smoother bend) |
| `ELBOW_SR` | Short-radius elbow | 90° elbow with tight radius |
| `ELBOW_45_FL` | 45° flanged elbow | Flanges shown at each end |

**Plain English**: An elbow is simply a bend in a pipe. The suffix tells you _how_ the ends connect — butt-weld (BW) means the pipe is welded flush, socket-weld (SW) means one pipe slides into the other then welds, threaded (THR) means it screws on like a bolt, flanged (FL) means it bolts to a matching flanged face.

#### Tees
| Symbol code | What it is | What it looks like |
|---|---|---|
| `TEE_EQ` | Equal tee | T-shape: three equal-diameter outlets |
| `TEE_RED` | Reducing tee | T-shape but with the branch shown thinner than the run |
| `TEE_LATERAL` | Lateral tee | Branch exits at 45° rather than 90° |
| `TEE_SW` | Socket-weld tee | Tee with socket-end symbols |
| `TEE_THR` | Threaded tee | Tee with threaded ends |
| `TEE_FL` | Flanged tee | All three outlets flanged |

**Plain English**: A tee is a T-shaped fitting that splits one pipe into two, or joins two pipes into one. "Equal" means all three openings are the same size. "Reducing" means the side branch is a smaller diameter.

#### Reducers
| Symbol code | What it is | What it looks like |
|---|---|---|
| `RED_CONC` | Concentric reducer | Cone shape, pipe centrelines stay aligned |
| `RED_ECC` | Eccentric reducer | Offset cone — bottom stays flat (used to avoid air pockets in liquid lines) |
| `RED_SW` | Socket-weld reducer | Concentric with socket marks |

**Plain English**: A reducer connects a large pipe to a smaller pipe. Concentric keeps them centred on the same axis (looks like a symmetrical cone). Eccentric keeps one side flat — used in plumbing so gas bubbles can't get trapped at the top of a water pipe.

#### Couplings and Unions
| Symbol code | What it is |
|---|---|
| `COUPLING` | Coupling / full coupling — connects two pipes of the same size end-to-end |
| `COUPLING_RED` | Reducing coupling — connects two different-diameter pipes |
| `HALF_COUP` | Half-coupling — welded to the side of a pipe to create a branch outlet |
| `UNION` | Union — like a coupling but can be unscrewed without cutting the pipe (used for maintenance access) |
| `NIPPLE` | Close nipple — very short length of threaded pipe |

#### Flanges
| Symbol code | What it is |
|---|---|
| `FLANGE_WN` | Weld-neck flange — the most pressure-resistant type, neck butts against pipe |
| `FLANGE_SO` | Slip-on flange — slides over pipe then welds front and back |
| `FLANGE_BL` | Blind flange — solid plate to cap / close the end of a pipe |
| `FLANGE_SW` | Socket-weld flange — pipe inserted into socket then welded |
| `FLANGE_THR` | Threaded flange — screws onto a threaded pipe end |
| `FLANGE_LAP` | Lap joint flange — used with stub-ends for easy alignment |
| `FLANGE_RTJ` | Ring-type joint flange — high-pressure service with a metal ring seal |
| `ORIFICE_FL` | Orifice flange pair — pair of flanges with tapped holes to measure flow |

**Plain English**: A flange is a disc with bolt holes around the outside. You bolt two matching flanges face-to-face to make a connection that can be disassembled (as opposed to a welded joint). The "type" tells you how the flange attaches to the pipe.

#### Caps and Plugs
| Symbol code | What it is |
|---|---|
| `CAP` | Pipe cap — dome-shaped end cap welded or threaded onto a pipe end |
| `PLUG` | Threaded plug — screws into a threaded outlet |
| `BLIND_FL` | Blind flange — same as `FLANGE_BL`, solid flanged end cap |

#### Valves
Valve symbols are the most visually distinctive ISO 6412 shapes:

| Symbol code | What it is | Symbol appearance |
|---|---|---|
| `VALVE_GATE` | Gate valve | Two triangles pointing towards each other at the centreline (parallel seat) |
| `VALVE_GLOBE` | Globe valve | One triangle pointing down to a horizontal line |
| `VALVE_BALL` | Ball valve | Small circle in the middle of the pipe line |
| `VALVE_BUTT` | Butterfly valve | Bow-tie or elongated diamond shape in the pipe line |
| `VALVE_CHECK` | Check valve / non-return valve | Triangle pointing in the flow direction; arrow shows one-way flow |
| `VALVE_NEEDLE` | Needle valve | Triangle with a pointed apex showing fine control |
| `VALVE_DIAPHRAGM` | Diaphragm valve | Curved line (the diaphragm membrane) across the pipe |
| `VALVE_SAFETY` | Safety / relief valve | Gate valve symbol with a small loop (the spring mechanism) |
| `VALVE_CONTROL` | Control valve (actuated) | Globe valve symbol with a circle on top (the actuator / motor) |
| `VALVE_SOL` | Solenoid valve | Electromagnetically operated; square symbol over the gate symbol |
| `VALVE_3WAY` | Three-way valve | T-shape valve symbol with three connections |
| `VALVE_4WAY` | Four-way valve | Cross-shape valve symbol |

**Plain English**: Valves control whether fluid flows and how much. A gate valve is simply open or closed (like a portcullis). A globe valve throttles flow. A ball valve rotates a ball with a hole through it. A butterfly valve rotates a disc. A check valve only lets flow go one way (prevents backflow). Safety valves pop open automatically to prevent dangerous pressure build-up.

#### Instruments and Fittings
| Symbol code | What it is |
|---|---|
| `PRESSURE_GAUGE` | Pressure gauge tapping |
| `TEMP_WELL` | Thermowell / temperature measurement pocket |
| `FLOW_ORIFICE` | Orifice plate for flow measurement |
| `DRAIN_PLUG` | Drain connection |
| `VENT_PLUG` | Air vent / bleed point |
| `STRAINER` | Y-strainer — mesh screen to catch debris in the flow |
| `TRAP_STEAM` | Steam trap — only lets condensate (cooled water) through, blocks live steam |
| `TRAP_P` | P-trap — water-seal trap used in drainage |
| `TRAP_S` | S-trap — drainage trap |
| `EXPANSION_LOOP` | Expansion loop — hairpin shape to absorb thermal growth |
| `FLEX_CONN` | Flexible connector / bellows — absorbs vibration |
| `ANCHOR_POINT` | Fixed pipe support / anchor |
| `SLIDE_GUIDE` | Directional pipe guide (allows sliding, prevents lateral movement) |
| `SPRING_HANGER` | Variable spring pipe hanger |

---

### Duct Symbols (ISO 6412-2) — ~45 symbols

All duct symbols represent rectangular or circular ductwork components:

#### Duct Fittings
| Symbol code | What it is |
|---|---|
| `DUCT_ELBOW_90` | 90° rectangular elbow (square section shown) |
| `DUCT_ELBOW_45` | 45° rectangular elbow |
| `DUCT_ELBOW_RD_90` | 90° round duct elbow |
| `DUCT_TEE` | Rectangular tee — duct splits into two branches |
| `DUCT_TEE_RD` | Round duct tee |
| `DUCT_RED` | Rectangular reducer / transition piece |
| `DUCT_OFFSET` | Duct offset — parallel shift (e.g. to route around an obstruction) |
| `DUCT_WYED` | Y-junction / wye — two ducts branch off at equal angles |
| `DUCT_CROSS` | Cross fitting — four-way intersection |
| `DUCT_END_CAP` | Duct end cap / blanking plate |

#### Duct Accessories (Dampers)
| Symbol code | What it is | How it works |
|---|---|---|
| `DAMPER_VOLUME` | Volume control damper (VCD) | A blade inside the duct rotates to restrict airflow — you manually set the position to balance air distribution |
| `DAMPER_FIRE` | Fire damper | Stays open normally; a fusible link melts at ~72°C and a spring slams it shut to stop fire spreading through the ductwork |
| `DAMPER_SMOKE` | Smoke damper | Motor-actuated; closes automatically when a smoke detector activates |
| `DAMPER_COMB` | Combination fire/smoke damper | Does both — motor-actuated AND has fusible link backup |
| `DAMPER_MOTORISED` | Motorised control damper | Electric or pneumatic actuator for automated building management |
| `DAMPER_BACK` | Backdraft damper | Only opens when air pressure pushes in one direction (like a check valve for air) |
| `DAMPER_RELIEF` | Relief damper | Opens automatically to relieve excess pressure (protects the duct from over-pressurisation) |
| `DUCT_SILENCER` | Silencer / sound attenuator | Lined chamber that absorbs noise from fans and airflow |
| `DUCT_FLEX` | Flexible duct connector | Prevents vibration from fan/AHU reaching the rigid duct system |
| `DUCT_ACCESS` | Access panel / door in duct | Used for cleaning, inspection, and damper maintenance |

---

### Conduit & Cable Tray Symbols — ~23 symbols

These are used on electrical fabrication drawings:

| Symbol code | What it is |
|---|---|
| `CDT_ELBOW_90` | Conduit 90° elbow |
| `CDT_ELBOW_45` | Conduit 45° elbow |
| `CDT_OFFSET` | Conduit offset (saddle) |
| `CDT_TEE` | Conduit tee / junction |
| `CDT_COUP` | Conduit coupling |
| `CDT_BOX_4SQ` | 4-square steel junction box (electrical) |
| `CDT_BOX_OCT` | Octagonal junction box (ceiling fixture support) |
| `CDT_BOX_ROUND` | Round conduit box |
| `CDT_SEAL` | Sealing fitting (for hazardous area conduit runs) |
| `CDT_EXPANS` | Expansion coupling (for thermal movement) |
| `CDT_LB` | LB conduit body (L-shaped with removable cover) |
| `CDT_LR` | LR conduit body (longer radius L-shape) |
| `CDT_LL` | LL conduit body (left-hand L) |
| `CDT_T` | T conduit body |
| `CTR_ELBOW_90` | Cable tray 90° horizontal elbow |
| `CTR_TEE` | Cable tray tee junction |
| `CTR_CROSS` | Cable tray cross |
| `CTR_RED` | Cable tray reducer |
| `CTR_RISER` | Cable tray vertical riser / stair fitting |
| `CTR_END` | Cable tray end cap |
| `CTR_SPLICE` | Cable tray splice plate connector |

---

## 4. How STING Matches Symbols to Fittings

When the fabrication engine creates a spool, each fitting (pipe elbow, duct damper, etc.) has a **name** in Revit. STING's `IsoSymbolPlacer` looks at that name and tries to match it to a row in the CSV catalogue (`STING_ISO_SYMBOLS_INDEX.csv`).

**Match step 1 — keyword search**: Does the Revit element name CONTAIN the symbol code? For example, a fitting named "90 Degree Elbow - Butt Weld" contains "90" and "BW" patterns, so it matches `ELBOW_90_BW`.

**Match step 2 — category fallback**: If no keyword matches, the placer uses the Revit category (Pipe Fitting, Duct Fitting, etc.) and takes the first symbol in the catalogue for that category.

**When no symbol is found**: The fitting is silently skipped. The result dialog lists which families were missing so you know what to author next.

---

## 5. Family Authoring — Building a New Symbol

### The basics

Every ISO 6412 symbol is a Revit **Detail Item family** — a 2D drawing file (.rfa) that, when placed in a view, shows the correct symbol shape.

Think of it like a stamp: when the placer drops the symbol onto the fitting's location, it's like pressing a rubber stamp — the shape always looks the same regardless of the fitting's real 3D shape.

### Required shape at 1:50 scale

At a drawing scale of 1:50, the target plotted size is **8 × 8 mm on paper**. In model space this means the family linework must be **400 × 400 mm** (8 mm × 50 = 400 mm).

The family uses a formula to scale automatically:
```
Detail Line Length = Symbol Scale / 50 × 8 mm
```
At scale 1:50 → 50/50 × 8 = 8 mm plotted.
At scale 1:25 → 25/50 × 8 = 4 mm plotted (symbol gets smaller as the drawing gets larger, which is correct — the pipe itself takes up more room so the symbol needs to stay proportionate).

### Step-by-step authoring checklist

1. **Start from the right template**: Open Revit → New Family → `Metric Detail Item.rft`.
2. **Load the shared parameter file**: Manage → Shared Parameters → Browse → select `Families/ISO6412/STING_ISO_SYMBOL_TEMPLATE.params.txt` → add all 7 parameters as **Instance** parameters.
3. **Draw the linework**: Use Symbolic Lines (not Model Lines) on the `<None>` subcategory. Centre everything at the family origin (0,0). Use parameter-driven dimensions so lines scale with `Symbol Scale`.
4. **Add the formula**: Create a Length parameter (e.g. "Base Size") with formula `Symbol Scale / 50 × 8 mm`. Drive your linework dimensions off this parameter.
5. **Set parameter defaults**: Symbol Scale = 50, STING_PLACED_BY_SYMBOL_PLACER_BOOL = 0, STING_FINALIZATION_CHECKLIST = 0.
6. **Test**: Load into a blank project, place on a Section view at 1:50. Verify it plots at 8 mm. Change view scale to 1:25 — the symbol should shrink to 4 mm.
7. **Mark as complete**: Set STING_FINALIZATION_CHECKLIST = 1 in the family.
8. **Name the file**: Must match the CSV `family_filename` exactly, e.g. `STING_FAM_PIPE_ELBOW_90_BW.rfa`.
9. **Place in folder**: Copy the .rfa to `Families/ISO6412/`.

### What each ISO 6412 shape looks like to draw

**90° butt-weld elbow**: Draw two parallel lines (the pipe walls) turning 90°, with a curved arc connecting the outer corners. The pipe walls should be about 1/6 of the symbol base size apart.

**Gate valve**: Two back-to-back triangles with their points touching at the pipe centreline. Top triangle points down, bottom triangle points up. Both triangles' bases align with the pipe wall lines.

**Check valve**: One triangle with the apex pointing in the flow direction (add a small arrow showing which way is "forward").

**Tee**: Like a capital T. The run pipe goes straight across, the branch goes perpendicular. For a reducing tee, draw the branch slightly narrower.

**Flange pair**: Two short parallel lines perpendicular to the pipe centreline, one on each side. "Weld-neck" adds a small tapered neck between the flange face and the pipe.

---

## 6. Placement Modes

When you run **Generate Fabrication Package** or **Place ISO 6412 Symbols** manually, you can choose one of three modes:

| Mode | What it does |
|---|---|
| **Off** | No symbols placed — use this if you just want the sheet and assembly views without annotation |
| **New Only** (default) | Only places symbols on fittings that don't already have one — safe for adding new fittings to an existing spool |
| **Replace** | Deletes all existing symbols first, then places fresh ones — use this after updating the family library |

---

## 7. Pre-flight Check

Before the fabrication package generates, STING runs a pre-flight check:
- Counts how many of the 188 catalogue families exist on disk in `Families/ISO6412/`.
- If any are missing, shows a warning dialog with the count and a summary.
- If **all** are missing (family library not installed), shows a stronger warning.
- You can click **Cancel** to abort and install the library, or **OK** to continue with partial symbol coverage.

---

## 8. Installing the Symbol Library

The complete ISO 6412 symbol bundle (`PlanscapeISO6412-v1.0.0.zip`) is downloaded and installed by the **Load ISO Symbol Library** button in the Fabrication tab:
1. Click **Load ISO Symbol Library**.
2. STING downloads the bundle from the Planscape CDN.
3. Extracts to `%APPDATA%/Planscape/Families/ISO6412/v1.0.0/`.
4. Loads each .rfa into the current project.
5. Reports how many of the 188 families loaded successfully.

If the CDN is unreachable (offline site), copy the .zip manually to the above folder and run **Reload Family Library** instead.

---

## 9. Reading a Fabrication Spool Sheet

A completed fabrication spool sheet has:
- **Title block** with spool number, system, level, revision, and weight.
- **Axonometric view** (the ISO 6412 view) showing the spool shape with:
  - ISO 6412 symbols at each fitting location.
  - Leader lines when two symbols are too close together.
  - Dimension annotations showing pipe lengths between fittings.
- **Cut list** table listing every piece (pipes by length and diameter, fittings by type and quantity).
- **Bill of materials** with part numbers, quantities, and weights.

The symbols tell the fabricator exactly what type of fitting to use at each position — they don't have to interpret a 3D model; they just read the standardised symbols exactly as they would from a hand-drawn isometric.

---

## 10. Quick Reference: Most Common Symbols

| You see on the drawing | It means |
|---|---|
| Right-angle corner with a curved arc | 90° elbow (pipe bends 90°) |
| Two back-to-back triangles touching at points | Gate valve (fully open or fully closed) |
| Small circle on the pipe line | Ball valve (quarter-turn open/close) |
| Triangle pointing one direction | Check valve (flow goes the same direction as the point) |
| Cone shape changing pipe width | Reducer (pipe gets bigger or smaller) |
| T-shape | Tee fitting (pipe splits or merges) |
| Two short perpendicular lines either side of pipe | Flange pair (bolted connection that can be undone) |
| Bow-tie / elongated diamond on pipe | Butterfly valve |
| Small mesh symbol across pipe | Strainer (catches debris) |
| Curved line across duct | Damper (controls airflow) |

---

*Guide version 1.0 — 2026-05-17*
*Standard: ISO 6412-1:1988, ISO 6412-2:1996, ISO 6412-3:1992*
*See also: MEP_SYMBOL_COLOUR_SCALE_GUIDE.md, SLD_SYMBOLS_LAYMANS_GUIDE.md*
