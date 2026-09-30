# MEP Modelling and Drawing Production with StingTools: the Fastest Order

**Updated:** 2026-09-30, re-checked line by line against `main` (first written 2026-09-24).
**Nothing here has been run end-to-end in Revit.** It is a reading of the code. Where a step
depends on code nobody has run, or where the code does less than its dialog suggests, it says
so. Button names are quoted exactly as the panels show them.

The one rule behind the order: **every step feeds the next.**
- Levels name the sheets, and project codes number them.
- Parameters must exist before anything can write them.
- Tags must exist before the annotation pass reads them.
- Sheets must exist before match lines can quote their numbers.

Do the steps out of order and the later ones run without errors, but produce blanks.

**Contents**
- Part A: The fastest route from empty project to issued MEP set
- Part B: Modelling from DWG (structural, architectural and MEP)
- Part C: MEP modelling → data, discipline by discipline
- Part D: What a typical comprehensive MEP set contains, and what STING produces
- Part E: The do-not list, known gaps and next automations

---

## Critical path (one line)

named levels → project codes → **Project Setup Wizard** → **★ Set up drawing production** →
backgrounds (link, or model from DWG) → seeds → place → route → systems / circuits → size →
**tag** → scope boxes → **produce** → panel schedules / SLD / schedules on sheets → QA → issue

---

# Part A: The fastest route

## A0. Before you open STING (10 minutes, saves hours)

| Do | Why |
|---|---|
| Name levels as short ISO codes with **no spaces**: `B1`, `GF`, `L01` … | `{lvl}` in sheet numbers is the raw level name, and scope-box names forbid spaces. The wizard's level audit accepts `L02 - Office`, so it will not warn you. |
| Fill in **Project Information → Number**, plus `PRJ_PROJECT_COD_TXT` and `PRJ_ORG_ORIGINATOR_CODE_TXT` | Otherwise `{project}` / `{originator}` stay as literal braces. Revit rejects that number and the sheet keeps its default. This hits `mep-plan`, `elec-power` and the spool types. |
| Decide on sheet numbering: per drawing type (default) or ISO 19650-2 | One project setting, `PRJ_ORG_SHEET_NUMBER_POLICY_TXT` (`profile` or `iso`), which the wizard now sets. A typo reads as `profile`; production warns about it. |
| Get the consultant DWGs **imported**, not only linked, if you will model from them | The MEP converter and Explode work only on imports (Part B). |

## A1. SETUP → ★ Project Setup Wizard

The wizard has 8 pages: Project Info, Levels, Grids, Disciplines, Disc. Config, Automation,
Region, Review. On Run it goes: pre-flight → foundation (units, project info, levels, grids,
worksets) → infrastructure (**Load Params**, materials, MEP family types) → standards
(schedules, styles, filters, templates) → drawing production (if ticked) → documentation →
intelligence.

On the Automation page:
- **Tick** "Set up drawing production (title blocks, tag families, view types, AEC filters,
  managed templates, pre-flight)". It is off by default because its unattended run has not
  been checked in Revit yet (ROADMAP DT-6). The alternative is to run it yourself in A2.
- **Set the sheet-number policy** in the picker. If you leave the picker as it opened, nothing
  is written and the report says SKIPPED.
- **Untick "Create views" and "Create sheets"**. Both are on by default. That is the older
  production path: its sheets carry no drawing-type stamp, so Doctor, Renumber, Heal TBs and
  Produce & Export ignore them, and A7 then makes a second, duplicate set.
- **Leave "Rename scope boxes" off.** Its pattern `{BLD}-{ZONE}-{INDEX}` is read by nothing
  else in STING.

## A2. SETUP → DRAWING PRODUCTION → ★ Set up drawing production

This is one click for everything drawing production needs, run as a 12-step workflow
(`WORKFLOW_DrawingProductionSetup.json`). The numbered buttons beside it ("1 Params" …
"12 Pre-flight") run one step each.

1. Load Params
2. Title blocks: create all
3. Load tag families
4. Text styles
5. Dimension styles
6. View templates
7. AEC filters
8. View types (STING - Section / Elevation / Interior Elevation / Callout)
9. Presentation fills (optional)
10. MEP flow-arrow family (optional)
11. Regenerate managed templates (optional)
12. Pre-flight (`DrawingTypes_Inspect`), which lists every missing title block, template,
    view type and tag family

If the wizard already ran it, you don't need to run it again, except after a plugin update:
new features add parameters, and **a parameter that isn't bound makes every write to it a
silent no-op.**

Then, once, for MEP colours: **HVAC panel → SYS → "Build types"**, then **"Gen filters"**.

> **Known gap:** the MEP view templates the drawing types name (`STING - HVAC Duct`, `STING -
> Power Layout`, `STING - Lighting Layout`, `STING - Fire Alarm`, `STING - Drainage`, `STING -
> MEP Plan`, `STING - Fire Protection Plan`) are created by nothing. Production warns once per
> view and falls back to the style pack. The drawings still come out, and the warnings are
> noise. Step 11 (managed templates) is the way to get real templates.

## A3. Backgrounds

Link the architectural and structural models. If you only have DWGs, model the structure and
walls from them first (**Part B**). MEP placement, hosting and routing need real walls,
ceilings and levels to work against.

## A4. Model → data

Part C covers this discipline by discipline. In short:

**SETUP → "★ Build Seeds"** → **MODEL → "★ Placement Centre" → Run & Routing → "Run All Rules"**
→ routing → **HVAC panel → SYS → "Build systems"** → circuits → sizing → panel schedules.

## A5. Tag before you produce

**TAGGING → "Tag+Combine"** (or **CREATE TAGS → "★ Full AutoPop"**), then **CREATE TAGS →
"Token Conf"** (Token Confidence Audit) to find elements that fell back to a default ZONE or
LOC. The production annotation pass and the rich
TAG7 tags read these tokens, so tagging after production means producing again.

## A6. Scope boxes: the automation multiplier

Everything is under **DOCS → 📐 DRAWING TYPES → Scope boxes**.

**Area boxes are the fast way.** One area box serves every plan drawing type that fits it, on
every level it spans. So you draw one box per area of the building, not one per type × level
× zone.

1. **Draw one seed per box size.** Revit's API cannot create or resize a scope box, so STING
   copies seeds.
   - In a **3D view**, draw a scope box of about the size the planner asks for.
   - Make it **tall enough to cross every level**.
   - Select it and click **"Register Seeds"**: it is renamed `STING-SEED::<w>x<d>`.
   - Or **"Import Seeds"** from a template that already has them.
2. **"Scope Box Planner"**: tick the MEP plan types and the levels.
   - It groups types into size classes and tiles each building's footprint (grid-aligned, 2 m
     overlap, 1 m padding).
   - It lists each box as New / Exists / Moved / Mismatch / NoSeed.
3. **Create boxes**: copies the seeds as `STING-AREA::<class>-<nn>[::<level>]` and saves the
   plan to `_BIM_COORD/scope_box_plan.json`.
4. **"Produce From Areas"** (or **"Produce views…"** in the planner): views and sheets for
   every area box × level × type, cropped to the box. A plan that would not crop to its box is
   rolled back and reported.
5. **"Colour Boxes"** tints boxes by size class, discipline, building, level or kind, so you
   can check the layout. **"Clear Box Colours"** removes the tint.

Limits:
- The planner offers **Plan and RCP types only**. `mep-coord-A1-1to50` (Coordination) is not
  offered; produce it per level or from a `STING::` box.
- **Match lines probably will not generate between area boxes.** Match Lines pairs boxes whose
  faces touch within 1 mm, and area boxes overlap by 2 m and are turned to the grid. This is
  inferred from the code, not tested.

**Named boxes (the older way)** are still supported, and still the way to get match lines:

| Name | Read by | Effect |
|---|---|---|
| `STING::<drawing-type-id>::<level>::<zone>` | "From Scope Boxes (Produce)" | One cropped view and sheet per box. Draw them **unrotated and edge to edge** so match lines generate. The Scope Box Manager validates names. |
| `STING-AREA::<code>[::<level>]` | "Produce From Areas" | See above |
| `STING-SEED::<w>x<d>` | The planner | A seed; never produced from |
| `STING-LOC::<code>` | Tagging | Sets the LOC (building) token when room and workset detection fall back to the default. Smallest containing box wins; must be unrotated. |

ZONE is **never** read from scope boxes. It comes from room Department/Name/Number or the
workset name, otherwise `Z01`.

## A7. Produce

**DOCS → 📐 DRAWING TYPES → Production**:
- **"Produce From Areas"**: area boxes (A6).
- **"From Scope Boxes (Produce)"**: `STING::` boxes.
- **"Produce Per Level"**: whole floors, no scope box.

Tick **only** the MEP types you need (Part D lists them).

One click creates the view, stamps it, crops it, applies the template / style pack,
auto-annotates, creates the sheet and fills the title block. **Re-runs reuse** existing views
and sheets, because they are matched by stamp, not by name. So produce again freely after
model changes.

Then, in this order:
1. **DOCS → "Match Lines: Generate"**. It needs the sheets first, because the captions quote
   sheet numbers.
2. **Electrical panel → PNLS → "▶ Place Schedules on Sheets".**
3. **Electrical panel → SLD → "▶ Generate SLD Drafting View"**, and **"▶ Generate Riser
   Diagram"**. Build the SLD symbols first.
4. The schedule drawing types: `mech-equip-schedule-A3`, `elec-panel-schedule-A3`,
   `valve-schedule-A3`, `plumb-pressure-schedule-A3`, `penetration-register-A1`.

## A8. QA

1. **DOCS → 📐 DRAWING TYPES → "Advanced drawing-type ops" → "Doctor"**.
2. **DOCS → "ISO Check"** (sheet compliance).
3. **Placement Centre → Run & Routing → "All Validators"** (or HVAC CALCS → "Run all
   validators").
4. **CREATE TAGS → "Valid"** (tag validation).
5. **DOCS → "Match Lines: Validate"**.

Run **"Heal TBs"** and **"Renumber"** (same expander as Doctor) only if something was
reported.

## A9. Issue

1. Run `WORKFLOW_RevisionIssue` (Create Revision → Auto Revision Cloud → Issue Sheets →
   Revision Sync → Revision Schedule).
2. **"⚡ Produce & Export" → option 2, "Finalize + Export (existing sheets only)"**. This
   writes PDFs plus a register CSV of the stamped sheets.
3. **BIM → Issue Deliverable / Create Transmittal**.

---

# Part B: Modelling from DWG

Most MEP jobs start from the architect's and structural engineer's CAD. STING has three
converters, all on the **MODEL** tab. **Model the building first (structure, then walls), and
the MEP last**: MEP fixtures host on walls and ceilings, and routes run between levels.

## B0. Prepare the DWG

- **Import it** (Insert → Import CAD) into the plan view of the level it belongs to.
  - The structural and architectural tools also accept a linked DWG.
  - The MEP converter and "Explode" need an import.
- **Units come from the import's scale.** There is no unit prompt, so import in the right
  units.
- **Explode nested blocks and xrefs first.** Geometry inside them is not read.
  - The "Explode" button uses the API's explode when the API exposes it.
  - If not, it tells you to run Modify → Explode → Full Explode yourself.
- **3D DWG content is flattened.** Z is ignored.
- **Loaded families.** Load a column family and a framing family before a structural
  conversion. "Prereqs" reports what is missing.

## B1. MODEL → "DWG Wizard" (the automatic router)

It counts the DWG's layer names and routes to the right converter:
- structural layers (`col`, `beam`, `struct`, `found`, `slab`) → the CAD Wizard (B2)
- architectural layers (`door`, `wind`, `wall`, `part`, `a-`) → DWG → Model (B3)
- MEP layers (`duct`, `pipe`, `mech`, `elec`, `plumb`, `m-`, `e-`, `p-`) → the MEP Wizard (B4)

*Fixed 2026-09-30.* Before this, it routed to two tags no handler knew, so every run ended in
"Unknown Command". MEP drawings were also sent to the walls-and-rooms converter.

## B2. Structure: MODEL → DWG → Structural BIM → "★★ CAD Wizard"

A single scrolling dialog, "STING Structural DWG-to-BIM Conversion". The fastest route
through it:

1. **DWG Import:** pick the drawing, then click **Analyze Layers**. The grid shows each layer
   with its entity counts, an auto-detected role, a confidence score and a "Map To" choice.
2. **Auto-Map Layers**, then correct the "Map To" column where the detection is wrong.
   - Layer names are matched by keyword, including German and French variants. Examples:
     `column` / `s-col` / `str_col`, `beam` / `lintel`, `slab` / `deck`, `found` / `fdn` /
     `footing` / `pile` / `raft`, `shear_wall` / `s-wall`.
   - Grids: layers containing `grid`, `axis` or `raster`.
   - The keyword rules are built in. The Map To column is the only override.
3. **Element-layer mapping:** confirm the Column, Beam, Wall, Slab, Foundation and Grid layers.
4. **Levels and properties:** Base Level, and the fallback sizes in mm (column height 3000,
   beam 450 × 250, wall 3000 × 200, slab 200, foundation depth 600).
5. **"Re-analyse (dry-run)"**: counts what would be created, and creates nothing. **Always do
   this first.**
6. **"Convert to BIM"**: creates grids, columns (circles become round columns), beams, walls
   and slabs on the base level, then tags them.
   - Each element type is a separate undo step.
   - The report lists warnings and a load-path audit.
   - The created elements are left selected.

**What Convert does not do yet (ROADMAP CAD-1).** The dry-run uses the complete pipeline,
with every option in the dialog. Convert still runs an older pipeline that reads only:
- the selected layers
- Base Level
- the column, beam, slab and grid switches
- beam depth, slab thickness and column height

So these are **ignored on Convert**:
- Top Level and "Repeat to other levels" (model **one level at a time**)
- foundations (use **"★ Auto Footings"** afterwards)
- the Wall and structural-wall switches (walls are always created, structural, at column height)
- the Dry-run tick box (**Convert always creates**)
- numbering
- the size-detection options

*Fixed 2026-09-30:* Convert now converts the DWG picked in the dropdown. It used to take the
first import in the project.

Size detection:
- Pad sizes are read from **block names** such as `PAD 1500x1500`.
- DWG text is not read, so section sizes come from your types, or from the dialog's "Create
  new Revit types" option (a dry-run feature today; see above).

Other buttons in the same group:

| Button | What it does |
|---|---|
| **Dry-Run** | Counts only, using the first import |
| **Preview** | Layer list with classification and confidence (read-only) |
| **Prereqs** / **Catalog** | What's missing / the available types (read-only) |
| **Pick Wall** / **Pick Column** / **Pick Beam** | Click one element in the view. Each uses the first available type on the lowest level. Pick Wall measures thickness but does not apply it yet. |
| **Openings** | Cuts openings in **every** basic wall in the project, not only selected ones |
| **Legacy** | The old one-click conversion with default settings |

Then **MODEL → Analysis & Auto-Sizing**: "★ Auto Footings", "Load Paths", "Detect Bays",
"Edge Beams".

## B3. Walls, rooms and floors: MODEL → DWG TO MODEL → "DWG → Model"

1. Pick the import.
2. Choose "Walls + Rooms + Floors (Full conversion)", "Walls only" or "Preview only".
3. Parallel lines become walls, enclosed spaces become rooms, and closed loops become floors.
   Everything goes on the lowest level.
   - Layer keywords: `wall`, `slab` / `floor`, `door`, …
   - The keyword rules are built in.

**"Preview"** beside it is read-only. Rooms matter to MEP: placement rules, ZONE / LOC
detection and the room data sheets all read them.

## B4. MEP from DWG: MODEL → DWG → MEP (fixtures) → "★ MEP Wizard"

This turns a consultant's MEP layout into real Revit MEP.

**Fixtures** come from **blocks**:
- Block names are matched against `Data/STING_DWG_FIXTURE_MAP.json` (24 rules).
- Add your consultant's block names in `_BIM_COORD/dwg_fixture_map.json`. A project entry with
  the same id wins.

**Runs:**
- **Straight duct / pipe / conduit / cable tray runs** come from lines on MEP layers. Lines
  shorter than 500 mm are skipped.
- **Sizes** are read from the layer name: `400x250`, `DN100`, `Ø150`. Otherwise a default is
  used.
- **Default heights:** duct 2700, pipe 2500, conduit 2800, tray 2900 mm.
- **Risers** come from `UP`, `DN` or `RISER` blocks.
- **Fittings** are added where run ends meet.
- **Drainage** gets a 1.25 % fall.
- The run rules are in `Data/STING_DWG_RUN_RULES.json`. The project override is
  `_BIM_COORD/dwg_run_rules.json`.

**The wizard:**
- Controls: Level, "Host-snap fixtures (wall/ceiling)", and a per-layer grid (Include, Role:
  Auto / Skip / Duct / Pipe / Conduit / CableTray, Offset mm).
- **"Place"** runs the whole conversion as **one undo step**.
- Running it again on the same import offers Replace or Add.
- Everything created is assigned to a workset and auto-tagged.
- A fixture with no loaded family is skipped and counted, so **build the seeds (C0) first**.

**"Preview"** is read-only. **"Convert"** is the same conversion without the per-layer grid.

> The hosting, break-curve and routing-preference API calls in this converter are marked
> "to verify in Revit". Treat the first run on a real drawing as a trial, and check the
> fixtures and fittings it made.

## B5. Fastest order from CAD

| Step | What |
|---|---|
| 1 | Levels and grids: the Setup Wizard, or the CAD Wizard's grid layer |
| 2 | Structure, one level at a time: CAD Wizard, dry-run then convert → Auto Footings |
| 3 | Walls, rooms, floors: DWG → Model |
| 4 | Ceilings: model them by hand. Ceiling-hosted lights and diffusers need them. |
| 5 | Seeds (C0), then MEP from the consultant DWGs: MEP Wizard, per level |
| 6 | Continue with Part C (systems, circuits, sizing) and Part A (tag → produce) |

---

# Part C: MEP modelling → data

Where things are:
- The main dock's MEP tools are under **TAG STUDIO** (sub-tabs Fixtures, Routing, MEP,
  Fabrication).
- The discipline panels are the **Electrical**, **HVAC** and **Plumbing** dockable panels.
- **MODEL → "★ Placement Centre"** is the placement hub.

## C0. Common start (all disciplines)

1. **SETUP → SYMBOLS & DEVICES:**
   - "Preflight", then **"★ Create All"** (annotation symbols).
   - **"★ Build Seeds"** builds parametric families from the 33 seed specs (lighting,
     electrical, fire alarm, data, security, plumbing, air terminal, mechanical, sprinkler,
     medical gas, junction box, and more).
   - These are the placeholders placement uses until manufacturer families arrive (**Swap to
     Manufacturer**).
2. **MODEL → "★ Placement Centre" → Run & Routing → "Run All Rules"** places fixtures by room
   type from the rule packs:
   - baseline, architecture, mechanical, electrical, healthcare-education, toilet fixtures,
     ceiling pendants, MK electrical, accessibility, glazing, extensions
   - "Preview" shows first what would be placed
   - "Learn from Model" records your own placements as project rules
3. **"All Validators"**, "BS 6465 Audit", "Clearance Scan" and "Penetration Coverage" (same
   tab).

## C1. Electrical: lighting, power, fire alarm, data

1. **Place:** "Run All Rules" (sockets, switches, devices) and **"Lighting Grid"** (BS EN
   12464-1 lumen method). Fire alarm and data devices come from the rule packs and seeds;
   there is no separate button.
2. **Circuits:** Electrical panel → CIRCTS.
   - **"▶ Propose Circuits (in panel)"** or **"▶ Launch Wizard…"**.
   - **"🤖 Auto-assign Unassigned Circuits"**.
   - **"▶ Apply Balance"**. Review this: grouping does no load or phase balancing on its own.
3. **Containment:** CABLE → **"🗺 Auto-Route Conduit (rectilinear)"**.
   - Plain L/Z runs, or "Avoid structure (A*)" (columns and beams only).
   - **Neither checks clashes with other services.**
   - Then **"▶ Validate containment fill in model"**.
4. **Calculations:** CALCS.
   - **"▶ Recalculate Load Summary"**.
   - Voltage drop: **"▶ Recalculate All"**, then "▶ Flag Exceedances", then "▶ Auto-Upsize
     Failing".
   - **"▶ Size All Feeders"**, then breaker sizing.
   - **"▶ Calculate Fault Levels"**, then "▶ Stamp to Panels", then "▶ Run BS 7671 Audit".
   - CABLE → **"▶ Calculate"** / **"▶ Apply to Circuit"** for single cables.
5. **Panel schedules:** PNLS.
   - 📐 Create / update the STING templates.
   - ⚡ **Batch Create Schedules**.
   - ✅ **Compliance check** (BS 7671 per circuit).
   - **"▶ Place Schedules on Sheets"**.
6. **SLD:** **"▶ Generate SLD Drafting View"**, **"▶ Generate Riser Diagram"**, "▶ Annotate
   All (full set)".
7. **Lighting data:** LITE → **"▶ Create Lighting Schedule in Revit"**, "▶ Emergency Circuit
   Audit", "▶ Calculate W/m²".

Presets: `WORKFLOW_ElectricalRoughIn` (seeds → place → sleeves → drops → validate),
`WORKFLOW_ElectricalQA`, `WORKFLOW_ElectricalSubmission` (20 steps, schedules to EasyPower
export).

## C2. HVAC

1. **Loads:** HVAC panel → LOADS → **"Block load"**, "Propagate", or **"Full design pass"**.
2. **Equipment:** EQPT → **"Place equipment"**.
3. **Ductwork:**
   - DUCT → **"Create types"**, "Place duct", **"Auto-drop"**.
   - "Generate layout" is a **preview only**: it draws detail lines and creates no ducts.
4. **Systems:** SYS → **"Build types"** → **"Build systems"** → "Gen filters" → "Colour view".
   The SYS button labelled "Build system" is an audit, not a builder.
5. **Sizing:** CALCS → **"Auto-size"** (duct), "Friction", "Static regain" / "Equal friction",
   **"Balance"** (Hardy Cross), "NC predict", "Run all validators".
6. **Fire dampers:** **"Auto-FD"**. Stair pressurisation: "Stair press.".
7. **Fabrication:** FAB → "Build assemblies", "Cut list", "Isometrics", "Weld map", "NC export".

Presets: `WORKFLOW_HVACDesign` (11 steps, block load → equipment schedule; duct auto-size is
the manual step), `WORKFLOW_HVACCommissioning`, `WORKFLOW_DuctSpoolProduction`,
`WORKFLOW_MEPSystemsValidate`.

## C3. Plumbing and drainage

1. **Fixtures:** Placement Centre → **"Toilet Rooms"** / "Run All Rules".
2. **Supply:** SUPPLY → **"Scan Fixtures (DU / LU / WSFU)"** → **"▶ Size DCW / DHW pipes"** →
   "Pressure Check (per level)".
3. **Routing:** ROUTE → **"▶ Auto-Route Selected"** / "Route All Fixtures" / "Connect to
   Stack". Then **"Insert P-Traps"**, "Place Sleeves", "Plan Hangers".
   - P-traps go only on a fixture's Sanitary connector.
   - Medical-gas outlets are skipped.
   - Fixtures without a Sanitary connector are listed as warnings.
4. **Drainage:** DRAINAGE → **"▶ Auto-Size Drainage"** → "Fix Slopes (auto-correct)" →
   **"Design Vents"** / **"▶ Create Vents"** → **"Calculate Invert Levels"**.
5. **Storm water:** STORM → roof drainage, SuDS, rainwater harvesting.
6. **Documents:** DOCS → "Pipe Schedule", "Plumbing BOQ", "Plumbing Isometric",
   **"Drainage Schematic"**, "Generate Spools".

Presets: `WORKFLOW_PlumbingDesign` (20 steps), `WORKFLOW_PlumbingRoughIn`,
`WORKFLOW_PlumbingAudit`.

## C4. Fire protection

- **Sprinkler heads:** the baseline sprinkler placement rules plus the Sprinkler seed.
- **Hydraulics:** Plumbing panel → SPECIALTY → **"Sprinkler Hydraulics"**.
- **Drawings:** `fire-sprinkler-layout-A1-1to100`, `fire-section-A1-1to50`,
  `fire-detail-A3-1to20`.

## C5. Medical gas

- **Outlets:** `Placement_MedGasOutlets` places one outlet per gas listed in each room's
  `MGS_GAS_REQUIREMENT_TXT`, using the STING outlet seed or a manufacturer family.
  - **It has no button**; run it from a workflow.
  - The Placement Centre does **not** load the medical-gas rule pack (ROADMAP MDP-1).
- **Audit:** HEALTHCARE → MGPS → **"Run audit"**, **"Full verify"** (`WORKFLOW_MgasVerification`).
- **Drawings:** `health-medgas-pln-A1-1to100`, `health-medgas-schem-A1`.

## C6. Penetrations, sleeves, hangers

- TAG STUDIO → MEP → **"Auto-sleeve"**.
- Plumbing ROUTE → "Place Sleeves".
- `WORKFLOW_PenetrationSweep` (seeds → detect and place → coverage audit → schedule).
- Hangers: TAG STUDIO → Routing → **"Hangers"**, and the HVAC and Plumbing hanger buttons.

## C7. Running modelling unattended

**SETUP → WORKFLOW AUTOMATION → "Run preset"** runs a chain without clicks, but only for
commands the workflow engine can resolve.

These run headless:
- `Seeds_Build`, `Placement_PlaceFixtures`, `Placement_MedGasOutlets`
- `Routing_AutoDrop`, `Plumb_*`, `MEP_Build*`
- `Cable_Calculate`, `Calc_VoltageDrop`, `Calc_FaultCurrent`
- `Panel_BatchSchedules`, `Panel_ComplianceCheck`, `Panel_BalanceApply`
- `SLD_Generate`, `Fire_SprinklerHydraulics`, `BOQExport`, `COBieExport`
- `DrawingTypes_SetupProduction`, the `ScopeBox_*` commands, `MatchLine_*`, `DrawingTypes_Doctor`

These are panel-only:
- `Placement_LightingGrid`, `Placement_ToiletRoom`
- `Hvac_AutoSizeDuct`, `Elec_AutoRoute`, `BatchMEPSchedules`
- the producers "Produce Per Level", "From Scope Boxes (Produce)" and "⚡ Produce & Export"

---

# Part D: A typical comprehensive MEP set

What a full MEP information package for a building normally contains, and how STING produces
each item.

**Key:**
- ✅ **Type**: a STING drawing type produces it (view, crop, template, annotation, sheet,
  title block).
- 🟡 **Tool**: a STING command makes the content, but there is no drawing type for the sheet.
  Place it with Sheet Manager.
- ❌ **Manual**: no drawing type and no tool. Draw it in Revit.

## D1. Model (the deliverable behind the drawings)

| Item | How |
|---|---|
| All services modelled at the agreed LOD, on the right worksets, with systems and circuits connected | Part C. LOD check: **LOD_Verify** |
| Every element tagged (ISO 19650 8-segment tag) with complete tokens | "Tag+Combine" + "Token Conf" |
| Coordinated: no clashes | Clash run → `coord-clash-A1-1to50` sheets, BCF export |
| Data out | COBie (**★ COBie Export Dashboard**), BOQ (**★ Tender BOQ**), IFC export |

## D2. Drawings

| Drawing | Status | STING |
|---|---|---|
| **General** | | |
| MEP drawing list / register | ✅ | Sheet register CSV from Produce & Export option 2 |
| Legend / symbols sheet | ✅ | `legend-A3` |
| Combined services coordination plans | ✅ | `mep-coord-A1-1to50`, `elec-coord-A1-1to50` |
| Clash / coordination issues | ✅ | `coord-clash-A1-1to50` |
| Builder's work / penetrations | ✅ | `penetration-register-A1` (schedule) |
| General MEP layouts | ✅ | `mep-plan-A1-1to100` (+ presentation / technical / fabrication variants) |
| **HVAC** | | |
| Ductwork layouts | ✅ | `mep-hvac-duct-A1-1to100` |
| Plant rooms | ✅ | `mep-plantroom-A1-1to50` |
| Chilled / LTHW pipework layouts | ❌ | No type. Produce `mep-plan` and filter by system. |
| HVAC schematics (air, water, controls) | ❌ | No type |
| Equipment schedules | ✅ | `mech-equip-schedule-A3` |
| Duct spools / isometrics | ✅ | `duct-spool-A1-1to50`; HVAC FAB "Isometrics" |
| **Electrical** | | |
| Lighting layouts (incl. RCP-style) | ✅ | `elec-lighting-A1-1to100` |
| Emergency lighting layouts | 🟡 | LITE "▶ Emergency Circuit Audit"; no separate type |
| Power / small power layouts | ✅ | `elec-power-A1-1to100` (one type for both) |
| Fire alarm layouts | ✅ | `elec-fire-alarm-A1-1to100` |
| Fire alarm schematic | ❌ | No type |
| Data / comms / telecoms, security / access control | ❌ | Devices exist as seeds; no drawing type |
| Containment / cable tray layouts | ❌ | No type. `mep-plan` + filters. |
| Single line diagram | 🟡 | "▶ Generate SLD Drafting View": a drafting view, placed by hand |
| Riser diagrams | ✅ / 🟡 | `elec-riser-A3-1to200`, `health-ess-power-riser-A1`; "▶ Generate Riser Diagram" |
| Panel / distribution board schedules | ✅ | `elec-panel-schedule-A3`; PNLS "▶ Place Schedules on Sheets" |
| Earthing and lightning protection | ❌ | LPS commands calculate and report; no drawing type |
| **Plumbing and drainage** | | |
| Above-ground drainage layouts | ✅ | `plumb-ag-drainage-A1-1to100` |
| Below-ground drainage layouts | ✅ | `plumb-drainage-A1-1to100` |
| Rainwater | ✅ | `plumb-rwd-layout-A1-1to100`; SuDS `plumb-suds-A1-1to500` |
| Cold / hot water layouts | ❌ | Plan type missing; only schematics |
| Cold / hot / LTHW schematics | ✅ | `plumb-dcw-schematic-A1-NTS`, `plumb-dhw-schematic-A1-NTS`, `plumb-lthw-schematic-A1-NTS` |
| Drainage schematic, vent riser | ✅ | `plumb-drainage-schematic-A1`, `plumb-vent-riser-A3-NTS` |
| Water treatment plant | ✅ | `plumb-water-treatment-A1-1to50` |
| Valve and pressure schedules | ✅ | `valve-schedule-A3`, `plumb-pressure-schedule-A3` |
| Pipe spools / isometrics | ✅ | `pipe-spool-A1-1to50`; Plumbing DOCS "Plumbing Isometric" |
| **Fire protection** | | |
| Sprinkler layouts, sections, details | ✅ | `fire-sprinkler-layout-A1-1to100`, `fire-section-A1-1to50`, `fire-detail-A3-1to20` |
| Fire riser / wet-dry riser schematic | ❌ | No type |
| **Medical gas (healthcare)** | | |
| MGPS layouts and schematic | ✅ | `health-medgas-pln-A1-1to100`, `health-medgas-schem-A1` |
| **Sections and details (all services)** | | |
| Services sections, typical details | ❌ | Only the fire-protection section and detail types exist |

Set it up once and the ✅ rows cost one click each on every re-issue. The ❌ rows are the
drawings to plan hand time for. They are also the next drawing types worth adding
(ROADMAP MDP-2).

---

# Part E: Do-not list, gaps, next automations

## Do-not list

| Don't | Because |
|---|---|
| Choose **option 1** of "⚡ Produce & Export" | It produces **every** Plan drawing type (50) × every level: architectural, structural, presentation, healthcare. |
| Use **HVAC panel → SYS → "Per-level + sheets"** for issue sheets | Unstamped sheets that Export and Renumber skip. Use it for coordination views only. |
| Leave the wizard's "Create views" / "Create sheets" ticked **and** produce drawing types | You get two sets of views and sheets. |
| Tick "Dry-run" in the CAD Wizard and click Convert | Convert ignores it and creates (CAD-1). Use "Re-analyse (dry-run)". |
| Convert a DWG with nested blocks or xrefs | Their geometry is not read. Explode first. |
| Generate match lines before sheets exist | The captions are left empty. |
| Tag after producing | The annotations are blank. |

## Gaps (open)

- **Drawing types missing:** data / comms, security, containment, CHW / LTHW pipework, HVAC
  schematics, cold / hot water plans, fire alarm schematic, fire riser, general services
  sections and details (MDP-2).
- **`E / PLAN` routes to `elec-riser-A3-1to200`**, a section (MDP-3).
- **No command creates the MEP view templates** the types name (A2).
- **"Duplicate as Dependent"** in the production dialog is still ignored.
- **`STING_SCOPE_BOX_TAG_TXT` is not a registered parameter**, so the `::<zone>` stamp is a
  no-op.
- **Match lines pair every view on one box with every view on the next**, across
  disciplines. Area boxes probably get none (A6).
- **The producers can't be chained in a workflow preset**, and `ScopeBox_ProduceAreas` still
  asks a question, so there is no one-click "produce the MEP set" preset.
- **CAD Wizard Convert ignores most of its dialog** (CAD-1). "Pick Wall" ignores the thickness
  it measures (CAD-2).
- **The medical-gas, routing, commissioning, conduiting and in-wall-chase placement packs are
  not loaded** by the Placement Centre (MDP-1).

## Next automations (highest payoff first)

1. **CAD Wizard Convert on the complete pipeline** (CAD-1): multi-level, foundations, a real
   dry-run and numbering in one pass.
2. **Workflow presets once the producers run headless:** `WORKFLOW_MEPDrawingSetup`,
   `WORKFLOW_MEPDrawingProduction`, `WORKFLOW_MEPPreIssue`.
3. **The missing MEP drawing types** (Part D ❌ rows), and the `E / PLAN` routing fix.
4. **Match lines for area boxes**, and discipline-aware pairing.
5. **ZONE from `STING-ZONE::` boxes**, mirroring LOC.
6. **The wizard warns on level names with spaces.**
