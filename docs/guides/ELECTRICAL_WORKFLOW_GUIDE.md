# STING Electrical Workflow Guide

> **Version note:** This guide reflects the STING Tools plugin as shipped on branch
> `claude/merge-branches-update-docs-SvA31` (Phase 176 + v4 MVP). All button names,
> dialog labels, and parameter names are written exactly as they appear in STING.

---

## How to use this guide

Read it from beginning to end the first time. Each chapter builds on the one before it.
After that, use the chapter headings as a reference — you can jump straight to "Chapter 4 —
Panel Schedules" or "Chapter 6 — Conduit Routing" without reading everything again.

Every step in a numbered list tells you exactly which button to press, which field to fill
in, and what you will see when it works. Every time a step could go wrong, there is a
`> **Stuck?**` blockquote that tells you what the confusing screen means and what to do.

Code-style text like `ELC_PANEL_ID_TXT` means a parameter name — it is written exactly as
it appears in Revit's parameter list.

---

## Who this guide is for

You are a qualified electrician or electrical engineer. You understand what a distribution
board is, what a circuit breaker does, and what voltage drop means. You may have used Revit
before, but you have never used STING's electrical tools. You want to produce a proper set
of electrical drawings — panel schedules, lighting plans, fire alarm layouts, and riser
diagrams — for a building project.

This guide does not assume you know anything about BIM software conventions, shared
parameters, or drawing type registries. All of those are explained as they come up.

---

## The Big Picture

Here is the complete electrical workflow, from an empty Revit model to issued drawings.
Every box in this diagram is covered by at least one chapter in this guide.

```
┌─────────────────────────────────────────────────────────────┐
│  BEFORE YOU START                                           │
│  Load shared parameters → Create view templates             │
│  (Chapter 2 — Project Setup)                                │
└───────────────────────┬─────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│  CHAPTER 3 — PLACE EQUIPMENT                                │
│  Load families → Place panels, fixtures, devices            │
│  → Auto-tag everything with DISC / SYS / PROD codes         │
└───────────────────────┬─────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│  CHAPTER 5 — ASSIGN CIRCUITS                                │
│  BatchAssignCircuits → assign every fixture to a panel       │
│  PhaseBalance → spread load across A/B/C phases              │
└───────────────────────┬─────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│  CHAPTER 4 — PANEL SCHEDULES  ← most important chapter      │
│  BatchPanelSchedules → one schedule per distribution board   │
│  Audit → Export to Excel → Contractor fills in → Import     │
└───────────────────────┬─────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│  CHAPTER 6 — CONDUIT ROUTING                                │
│  AutoConduitDrop → route conduits between panel and device   │
│  Junction boxes auto-placed → slab penetrations detected     │
└───────────────────────┬─────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│  CHAPTER 7 — LIGHTING DESIGN                                │
│  LightingGrid → automatic layout → emergency designation     │
│  → lighting schedules                                        │
└───────────────────────┬─────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│  CHAPTER 8 — WIRE ANNOTATIONS                               │
│  Add homerun callouts, circuit labels, conductor marks       │
└───────────────────────┬─────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│  CHAPTER 11 — VALIDATION AND QA                             │
│  ValidateTags → BS 7671 validation → RunAllValidators         │
└───────────────────────┬─────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│  CHAPTER 12 — PRODUCE AND ISSUE DRAWINGS                    │
│  → See DRAWING_PRODUCTION_SYSTEM_GUIDE.md                   │
│  → See DOCUMENT_MANAGER_GUIDE.md                            │
└─────────────────────────────────────────────────────────────┘
```

---

## Prerequisites (before you start any electrical work)

Before you place a single socket outlet, three things must be in place in your Revit
project. If any of them are missing, STING's electrical tools will either fail silently
or produce tags and schedules that look wrong.

### Shared parameters must be loaded

**What shared parameters are:** Revit has a concept called "shared parameters." Think of
them as a standardised list of data fields — like columns in a spreadsheet — that all
STING projects use in exactly the same way. `ELC_PANEL_ID_TXT` is a shared parameter that
stores which distribution board a device belongs to. `ELC_BREAKER_SIZE_A` stores the
breaker rating. Without these fields existing in the project, STING cannot write to them
or read from them.

**The parameter file lives at:** `StingTools/Data/MR_PARAMETERS.txt`

**Which parameter groups matter for electrical work:**

| Prefix | What it covers |
|--------|----------------|
| `ELC_*` | Electrical power: panels, circuits, conduit, cable |
| `LTG_*` | Lighting: lumens, lux targets, emergency flag, controls |
| `ELE_*` | Large electrical equipment: switchgear, transformers, UPS |
| `SEC_*` | Security systems: CCTV, access control, intruder |
| `FLS_*` | Fire and life safety: detectors, call points, sounders |
| `COM_*` | Communications and data: network, patch panels |

**To load shared parameters:**

1. Open your Revit project.
2. Find the STING dockable panel on the right side of the screen. If it is not visible,
   go to the ribbon, find the **STING Tools** tab, and click the **STING Panel** button.
3. In the STING panel, click the **TEMP** tab.
4. Find the **Setup** section at the top of the TEMP tab.
5. Click **Load Shared Params**.
6. A progress dialog will appear. STING runs two passes — the first pass binds universal
   parameters to all categories; the second pass binds discipline-specific parameters to
   the correct MEP categories.
7. When it finishes, a summary appears. You should see several hundred parameters bound
   successfully. For a full electrical project expect 200–400 electrical parameters.

> **Stuck?** If you see "0 parameters bound" or an error saying the shared parameter
> file could not be found, STING cannot locate `MR_PARAMETERS.txt`. This usually means
> the plugin was not installed correctly. Contact your BIM manager and ask them to
> verify the `StingTools/Data/` folder is in the plugin's assembly path.

**To verify it worked:**

1. Select any piece of electrical equipment already in your model (or place a temporary
   Electrical Equipment family just for this test).
2. Open its Properties panel (press `E` on the keyboard or use the Properties sidebar).
3. Scroll down until you see parameter groups starting with `ELC_`. If you can see
   `ELC_PANEL_ID_TXT`, `ELC_BREAKER_SIZE_A`, and `ELC_CIRCUIT_NUM_TXT` in the list,
   the parameters loaded correctly.

### Electrical view templates must exist in the project

**What a view template is:** A view template in Revit is a saved set of display rules for
a particular type of drawing. An electrical power plan template turns off structural
elements, shows cables and panels clearly, and sets a scale of 1:100. Without view
templates, every new plan you create looks identical — a cluttered mess of every element
in the building all at once.

STING ships four electrical view templates. To create them:

1. In the STING panel, go to the **TEMP** tab.
2. Find the **Templates** section.
3. Click **View Templates**.
4. STING creates 23 standard view templates including the four electrical ones:
   - `STING - Electrical Plan` (for power and distribution drawings)
   - `STING - Lighting Plan` (for lighting layout drawings)
   - `STING - Fire Alarm Plan` (for fire detection and alarm drawings)
   - `STING - Electrical Riser` (for riser diagrams and schematic views)

> **Stuck?** If the command runs but you cannot find the templates, go to
> **View → View Templates → Manage View Templates** in the Revit ribbon. Sort by name
> and look for entries starting with `STING -`. If they are there, the command worked.

**Electrical drawing types** — STING also ships pre-configured drawing type profiles that
define paper size, scale, and annotation rules for each electrical output. To verify they
are registered:

1. In the STING panel, go to the **DOCS** tab.
2. Find the **Drawing Types** section.
3. Click **Inspect Drawing Types**.
4. A result panel opens. Scroll through the list and look for these IDs:
   - `elec-power-A1-1to100` (electrical power plan, A1, 1:100)
   - `elec-lighting-A1-1to100` (lighting layout, A1, 1:100)
   - `elec-fire-alarm-A1-1to100` (fire alarm drawing, A1, 1:100)
   - `elec-riser-A2-1to100` (electrical riser, A2, 1:100)
   - `elec-panel-schedule-A3` (panel schedule sheet, A3)

If those five appear in the list, your drawing type registry is ready.

### Electrical filters must exist in the project

STING uses Revit filters — colour-coded rules that highlight or hide categories of
elements — to make electrical elements visible and distinct in each view type.

1. In the STING panel, go to the **TEMP** tab.
2. In the **Templates** section, click **Create Filters**.
3. STING creates 28 standard filters. The ones relevant to electrical work are:
   - `STING - Electrical Equipment` (orange fill, electrical panels and gear)
   - `STING - Lighting Fixtures` (yellow fill, light fittings)
   - `STING - Electrical Fixtures` (blue fill, sockets, switches, outlets)
   - `STING - Fire Alarm Devices` (red fill, detectors and call points)
   - `STING - Communication Devices` (purple fill, data and telecom)
   - `STING - Conduits` (grey lines, conduit runs)

---

## Chapter 1 — Understanding STING's Electrical Disciplines

Before you start clicking, it helps to understand how STING categorises electrical work.
The tagging system uses short code words to identify what discipline an element belongs to,
what system it is part of, and what the element actually is. These codes show up everywhere:
in tags on the drawing, in parameter values, in schedule columns.

### The three electrical discipline codes

STING uses single or double letters to identify each engineering discipline. For electrical
work, three codes matter:

| Code | Discipline | What it covers |
|------|-----------|----------------|
| `E` | Electrical | All power distribution: panels, distribution boards, socket outlets, switches, conduit, cable tray |
| `ELC` | Electrical (long form) | Same as `E` — used in parameter names (e.g. `ELC_PANEL_ID_TXT`) rather than tag fields |
| `FLS` | Fire and Life Safety | Fire detection, sprinklers, sounders, call points, emergency lighting circuits |
| `SEC` | Security | CCTV, door access, intruder detection |
| `ICT` | ICT / Data | Network cabling, Wi-Fi access points, patch panels |
| `LV` | Low Voltage | General LV systems not covered by the above codes |

When STING auto-tags an element, it looks at the Revit category of the element and assigns
the discipline code automatically. A `Lighting Fixtures` category element gets `DISC = E`
(or `FLS` if it is an emergency luminaire). A `Fire Alarm Devices` category element gets
`DISC = FLS`.

### System codes for electrical work

The system code tells you which specific system an element belongs to within its
discipline. An electrical engineer reading a drawing needs to know not just that a device
is "electrical" but whether it is on the lighting system, the power system, the UPS, or
the generator.

| System code | What it means | Typical elements | When to use |
|-------------|---------------|-----------------|-------------|
| `LV` | Low-voltage power distribution | Distribution boards, sub-boards, final circuits, socket outlets, switches | General building electrical power |
| `HV` | High-voltage supply | HV switchgear, transformers, incoming supply equipment | When the project includes HV infrastructure |
| `UPS` | Uninterruptible power supply | UPS units, UPS panels, critical circuit outlets | Server rooms, operating theatres, critical plant |
| `GEN` | Standby generator | Generator sets, auto-transfer switches, generator-fed panels | Buildings with standby generation |
| `EMS` | Energy metering and monitoring | Sub-meters, BMS-linked metering panels, smart meters | Projects with energy management requirements |
| `LTG` | Lighting | Light fittings, dimmer panels, lighting control panels | All artificial lighting — both normal and emergency |
| `FLS` | Fire and life safety | Smoke detectors, heat detectors, call points, sounders, fire alarm panels | Fire alarm and emergency systems |
| `FP` | Fire protection | Sprinklers, dry risers (used in some project configurations) | Sprinkler systems |
| `SEC` | Security | CCTV cameras, door access readers, intruder alarm panels | Security systems |
| `ICT` | Information and communications | Network switches, Wi-Fi APs, patch panels, structured cabling | Data and telecommunications |
| `COM` | Communications | Intercom, PA, nurse call | Building communications |
| `NCL` | Nurse call / clinical comms | Nurse call panels, bedhead units (healthcare projects) | Healthcare nursing stations |

### Parameter families used in electrical work

STING's parameters are named with a prefix that tells you which group they belong to.
Here are the groups you will use most often in electrical work:

| Prefix | Purpose | Key parameters |
|--------|---------|----------------|
| `ELC_` | Panel and circuit data | `ELC_PANEL_ID_TXT`, `ELC_CIRCUIT_NUM_TXT`, `ELC_BREAKER_SIZE_A`, `ELC_LOAD_KW`, `ELC_WIRE_GAUGE_TXT`, `ELC_PNL_PHASE_TXT`, `ELC_PNL_TYPE_TXT`, `ELC_PNL_LOCATION_TXT`, `ELC_PNL_VOLTAGE_TXT`, `ELC_PNL_RATING_A` |
| `ELC_CDT_` | Conduit routing data | `ELC_CDT_BEND_COUNT_NR`, `ELC_CDT_RUN_LENGTH_M`, `ELC_CDT_CABLE_COUNT_NR`, `ELC_CDT_INSTALL_METHOD_TXT` |
| `ELC_JB_` | Junction box data | `ELC_JB_TYPE_TXT`, `ELC_JB_SIZE_MM`, `ELC_JB_IP_RATING_TXT`, `ELC_JB_AUTO_PLACED_BOOL` |
| `LTG_` | Lighting design data | `LTG_EMERG_BOOL`, `LTG_FIX_TAG`, `ELC_PHOTO_LUMENS`, `ELC_LIGHTING_UF_FACTOR`, `ELC_LIGHTING_TARGET_LUX_TXT` |
| `FLS_` | Fire and life safety device data | `FLS_DEV_TAG`, fire device type and zone identifiers |
| `SEC_` | Security device data | `SEC_DEV_TAG`, zone and access-level identifiers |
| `PEN_` | Penetration (fire-stop) records | `PEN_FIRE_RATING_TXT`, `PEN_OD_MM`, `PEN_CONTROL_NUMBER_TXT`, `PEN_INSTALL_STATUS_TXT` |
| `STING_PENETRATION_` | Conduit crossing records | `STING_PENETRATION_REF_TXT`, `STING_PENETRATION_FIRE_RATING_TXT` |

---

## Chapter 2 — Setting Up Your Electrical Project

### Step 1: Use Master Setup (recommended for new projects)

The quickest way to set up a new project for electrical work is to use STING's
Master Setup command. This runs all the setup steps in the correct order, in a single
operation.

1. In the STING panel, go to the **TEMP** tab.
2. In the **Setup** section, click **Master Setup**.
3. A progress dialog runs through 15 steps automatically. These include loading shared
   parameters, creating filters, creating view templates, creating worksets, setting up
   phases, and checking the data files.
4. When it finishes, read the summary. Any steps that failed are listed in red with a
   reason. Most failures at this stage are because certain families are not yet loaded —
   that is normal; the families come later.

**Manual alternative** (if you need more control):

Run these three commands in order, each from the **TEMP** tab:

| Step | Button | What it does |
|------|--------|-------------|
| 1 | **Load Shared Params** | Binds all electrical shared parameters to the correct categories |
| 2 | **Create Filters** | Creates the VG filters that highlight electrical elements |
| 3 | **View Templates** | Creates the four electrical view templates |

### Step 2: Verify your electrical drawing types

1. In the STING panel, go to the **DOCS** tab.
2. Click **Inspect Drawing Types**.
3. In the result panel that appears, look for the five electrical drawing type IDs listed
   in the Prerequisites section above. If all five appear, you are ready.

> **Stuck?** If the command shows zero drawing types, the `STING_DRAWING_TYPES.json`
> data file is not being found. Go to **TEMP → Check Data Files** — this lists every
> expected data file and flags any that are missing or have the wrong checksum.

### Step 3: Verify the tag configuration for electrical elements

STING uses a lookup table to assign the correct DISC, SYS, and PROD codes to each element
automatically. Before you start tagging, confirm that this lookup is working:

1. In the STING panel, go to the **CREATE** tab.
2. Click **Tag Config**.
3. A TaskDialog shows the loaded configuration: number of discipline mappings, system
   mappings, and product code mappings. For a standard electrical project you should see
   at least 41 discipline mappings and 17 system codes.

If the numbers are very low (single digits), the config file did not load correctly. In
this case, go to **CREATE → Configure** to open the configuration editor and check the
path to `project_config.json`.

---

## Chapter 3 — Placing Electrical Equipment

This chapter is deliberately short. The mechanics of building families, loading them,
and placing them are covered in depth in separate guides that apply to all MEP disciplines.
This chapter points you to those guides and tells you what is specifically important for
electrical work.

### Families must be placement-ready

Before STING can auto-tag or auto-route from an electrical element, the family must be
set up correctly. The things that matter for electrical families are:

- The family must be in the correct Revit **category** — for example, a distribution board
  must be in the `Electrical Equipment` category, not `Generic Models`. STING uses the
  category to assign the DISC code automatically.
- The family must have a **connector** attached (for panels, distribution boards, and
  any element that circuit assignment will attach to).
- The family must have a **hosting type** that matches its real installation context —
  wall-hosted for wall-mounted panels, face-based for ceiling-mounted detectors.

> See **MEP_FOUNDATION_GUIDE.md, Chapter 2 — Placement Family Authoring** for the
> complete guide to setting up families correctly.

### Creating or sourcing electrical symbols

STING ships a set of seed families — basic placeholder shapes for every electrical
category. You can use these seeds to start working immediately, then swap them for real
manufacturer families when procurement decisions are made.

To build the seed families:

1. In the STING panel, go to the **TEMP** tab.
2. In the **Templates** section, click **Build Seed Families**.
3. STING creates 11 seed families and loads them into the project. The ones relevant to
   electrical work are:
   - `STING_SEED_LightingFixture` (with 5 type variants: RECESSED_LED_600x600, DOWNLIGHT,
     PENDANT, LINEAR_LED, EMERGENCY)
   - `STING_SEED_ElectricalFixture` (SOCKET_2G, SOCKET_1G, SWITCH_2G, DATA_OUTLET_2G,
     FLOOR_BOX, FCU)
   - `STING_SEED_ElectricalEquipment` (DISTRIBUTION_BOARD_DB, MAIN_SWITCHBOARD_MSB,
     CONSUMER_UNIT, ISOLATOR, TRANSFORMER)
   - `STING_SEED_FireAlarmDevice` (SMOKE_OPTICAL, HEAT_DETECTOR, CALL_POINT_MCP,
     SOUNDER_BEACON_VAD)
   - `STING_SEED_CommunicationDevice` (WIFI_AP, DATA_OUTLET_RJ45, CCTV_CAMERA)
   - `STING_SEED_JunctionBox` (PULL_BOX, DRAW_IN_BOX, ADAPTABLE_BOX)

> See **MEP_FOUNDATION_GUIDE.md, Chapter 1 — MEP Symbol Creation** for guidance on
> creating custom electrical symbols if the seed shapes do not match your drawing standard.

### Placing equipment using the Placement Centre

For large projects with many rooms, the Placement Centre lets you write rules like "place
one DISTRIBUTION_BOARD_DB per electrical cupboard at 1800 mm above floor level" and
execute them across hundreds of rooms automatically.

> See **MEP_FOUNDATION_GUIDE.md, Chapter 3 — The Placement Centre** for the complete
> guide to rule-based placement.

For smaller projects, you can place families manually using Revit's standard placement
tools (`Insert → Load Family`, then click in the view). STING will tag them the same way
regardless of how they were placed.

### Tagging after placement

After you place electrical equipment, tag it immediately. Tagging writes the ISO 19650
identifier codes onto each element — these codes are what link elements to panel schedules,
circuit assignments, and the cable schedule.

1. In the STING panel, go to the **CREATE** tab.
2. Click **Auto Tag**.
3. A dialog asks for the scope: **Active View**, **Selection**, or **Entire Project**.
   For a first pass on a new project, use **Entire Project**.
4. STING tags every untagged electrical element. For each element it writes:
   - `DISC = E` (or `FLS` for fire alarm, `SEC` for security, `ICT` for data)
   - `SYS` = derived from the MEP system name (e.g. `LV` for power, `LTG` for lighting)
   - `LOC` = detected from the room the element sits in
   - `ZONE` = detected from the room's department or zone
   - `LVL` = the floor level code (e.g. `GF`, `L01`, `L02`)
   - `PROD` = product code derived from the family name (e.g. `DB` for distribution board,
     `SOCKET` for socket outlet, `DET` for detector)
   - `SEQ` = a 4-digit sequence number unique within the group

5. To verify the tags are correct, select a few elements and press `E` to open Properties.
   Scroll to the parameter group starting with `ASS_` — you should see `ASS_TAG_1`
   showing a tag like `E-BLD1-Z01-GF-LV-PWR-DB-0001`.

To check all tags at once:

1. In the STING panel, go to the **CREATE** tab.
2. Click **Validate Tags**.
3. The validation report shows any elements with missing or incorrect codes. Common
   electrical issues: wrong DISC code (usually because the family is in the wrong
   category), or blank SYS code (usually because the element is not connected to an
   MEP system yet).

---

## Chapter 4 — Panel Schedules: The Core of Electrical Documentation

This is the most important chapter in the guide. Panel schedules are the document that an
electrician carries when working on a distribution board. The law requires them to be
accurate, up to date, and readable. STING automates their creation, filling in, and
maintenance — but you need to understand what they are and how the automation works.

### What is a panel schedule?

A panel schedule is like a register for a single distribution board. Imagine a distribution
board with 20 circuit breakers in it. The panel schedule is a table that lists every one of
those 20 breakers: which circuit number it is, what it powers (e.g. "Ground floor office
lighting — Zone A"), what size breaker it has (e.g. 16A), and how much current the circuit
actually draws.

An electrician uses the panel schedule to:
- Identify which breaker controls which circuit before switching anything off
- Verify that the load on each circuit is within limits
- Record the installed state of the distribution board for the O&M manual
- Plan future circuit additions (which slots are spare)

A building regulations inspector uses the panel schedule to verify that the installation
complies with BS 7671 (the IET Wiring Regulations).

In a Revit BIM model, a panel schedule is a special type of Revit view called a
`PanelScheduleView`. It is linked directly to the Revit electrical systems in the model —
so when you assign a circuit to a panel, the panel schedule updates automatically.

### How STING panel schedules work

STING does not just create a blank panel schedule and leave you to fill it in manually.
It automates the entire chain:

1. **Creates the schedule:** STING calls `PanelScheduleView.CreateInstanceView` for every
   electrical panel in the model that does not already have a schedule.
2. **Chooses the right template:** Every panel schedule in Revit uses a template that
   defines its layout — how many columns it has, whether it shows three phases or one,
   whether it has a summary section. STING picks the right template automatically based on
   the type of panel (see the template selection rules below).
3. **Fills in panel-level data:** STING reads the panel's parameters and writes them into
   the schedule: panel name, voltage, total load, main breaker rating, which board it is
   fed from, and how many ways it has.
4. **Writes circuit back-references:** For every circuit connected to the panel, STING
   stamps the parameter `ELC_PANEL_SCHEDULE_REF_TXT` on that circuit, so you can trace
   from any element in the model back to its panel schedule.
5. **Stamps the drawing type:** Every created schedule gets stamped with the drawing type
   `elec-panel-schedule-A3` — this means STING's Browser Organiser groups all panel
   schedules together, and the SyncStyles command keeps them visually consistent.

### The panel schedule template selection system

Revit requires you to choose a panel schedule template before creating a schedule. The
template defines the table layout. STING automates this choice using a rules file called
`STING_PANEL_SCHEDULE_TEMPLATES.json`. The rules are checked in priority order — the first
rule that matches the panel wins.

| Priority | Rule name | Panel type it matches | Template name needed in Revit |
|----------|----------|----------------------|-------------------------------|
| 1 | Switchboard | Panel name contains "MSB", "SWBD", "switchboard", or "MAINS" | `STING - Switchboard Schedule` |
| 2 | Three-phase DB | Panel name contains "DB" and panel is 3-phase | `STING - 3Ph Distribution Board` |
| 3 | One-phase consumer unit | Panel name contains "CU", "consumer unit", or single-phase panel | `STING - Consumer Unit Schedule` |
| 4 | Data / comms panel | Panel name contains "UPS", "ICT", "DATA", or "COMMS" | `STING - Data Panel Schedule` |
| 5 | Catch-all | Everything else | `STING - General Panel Schedule` |

**The important implication:** These five Revit panel schedule templates must exist in your
project before you run Batch Panel Schedules. If they do not exist, STING falls back to
"first available template" — which may give you the wrong layout.

**How to check:** Go to **View → View Templates → Panel Schedule Templates** in the Revit
ribbon. You should see at least the five names listed above. If they are missing, your
office BIM manager or template author needs to add them to the project template (`.rte`
file) or load them from a template file.

**Project-level override:** If your project uses non-standard panel names, you can override
the rules for just this project. Create a file called `panel_schedule_templates.json` in
the folder `<project>/_BIM_COORD/` alongside your `.rvt` file. Copy the format from the
corporate file and add rules that match your project's naming convention.

**The global fallback:** If no rule matches and no template can be found at all, STING
shows a warning in the result panel saying "GlobalFallback — no matching template found.
Using first available." The schedule is still created but may have the wrong layout.

### Step by step: Creating panel schedules with Batch Panel Schedules

This is the main command for panel schedule creation. Run it once at the start of the
project to create all the schedules, and again whenever you add new panels to the model.

**Before you run this command, make sure:**
- Your panels are placed in the model as `Electrical Equipment` families
- Each panel has its `ELC_PANEL_ID_TXT` parameter filled in (e.g. "DB-L01-A")
- The five STING panel schedule templates exist in the project (see above)
- You have run Auto Tag at least once so the panels have STING tag codes

**Steps:**

1. In the STING panel, go to the **BIM** tab.
2. Scroll down to the **Panel Schedules** section.
3. Click **Batch Panel Schedules**.
4. STING scans the model for all `Electrical Equipment` elements. For each one it checks
   whether a `PanelScheduleView` already exists with that panel as its base equipment.
5. For panels without a schedule, STING:
   a. Matches the panel against the template rules (priority 1 to 5)
   b. Creates a `PanelScheduleView` named after the panel
   c. Fills in the schedule header: panel name, voltage (from `ELC_PNL_VOLTAGE_TXT`),
      main breaker rating (from `ELC_PNL_RATING_A`), total load (from `ELC_PNL_LOAD_KW`),
      fed-from reference (from `ELC_PNL_FED_FROM_TXT`), and number of ways
   d. Stamps the drawing type `elec-panel-schedule-A3`
   e. Writes `ELC_PANEL_SCHEDULE_REF_TXT` on every `ElectricalSystem` whose base
      equipment is this panel
6. A result panel shows the outcome: schedules created, schedules already existing
   (skipped), and any errors.

> **Stuck?** If you see "0 schedules created, N panels found but skipped — template not
> available," the named templates are not loaded in your project. See the template
> selection section above and add the five templates. Then run Batch Panel Schedules again
> (it is idempotent — safe to run multiple times).

> **Stuck?** If you see "0 panels found," check that your distribution boards are in the
> `Electrical Equipment` Revit category. Select one, press `E` to open Properties, and
> look for the **Category** line near the top. If it says `Generic Models` or
> `Specialty Equipment`, the family is in the wrong category and STING cannot find it.

**Finding the created schedules in the Project Browser:**

After the command runs, open the Project Browser (View → Browser Organisation → select
"STING - by Drawing Type" if it is available, otherwise use the default). Look for a
group called **Panel Schedules** or scroll through the **Schedules/Quantities** section.
Each schedule will be named after its panel, for example "DB-L01-A Panel Schedule."

**Placing panel schedules on sheets:**

Revit does not allow automatic placement of panel schedule views onto sheets through the
API (this is a known Revit limitation). You must drag each schedule onto a sheet manually:

1. Open the sheet where the panel schedule should appear.
2. In the Project Browser, find the panel schedule view.
3. Drag it from the Project Browser onto the sheet.
4. Position it in the available space.

> **Why does STING not do this automatically?** The Revit API method
> `PanelScheduleSheetInstance.Create` is broken in Revit 2024, 2025, and 2026 — it
> raises a Revit exception. Autodesk has not fixed this. The result panel that appears
> after Batch Panel Schedules runs will remind you of this and tell you to place manually.

**What the completed schedule looks like:**

Once placed on a sheet, a panel schedule shows:
- A header row with the panel name, voltage, and main breaker rating
- One row per circuit breaker slot showing: slot number, phase, circuit number,
  description, load in watts/kW, and breaker size in amps
- Empty slots shown as SPARE or SPACE depending on how you have configured them
- A summary section showing total connected load and the balance across A, B, and C phases

---

### Auditing panel schedules (Panel Schedule Audit)

The audit command is a read-only check that tells you what is missing or out of date
without making any changes. Run it at the start of a project to see the current state,
and again before issue to confirm everything is complete.

**To run the audit:**

1. In the STING panel, go to the **BIM** tab.
2. Click **Panel Schedule Audit**.
3. A result panel opens with three sections:

**Section 1 — Panels without schedules:**
Every `Electrical Equipment` element that does not have a `PanelScheduleView` linked to
it. These panels need to be run through Batch Panel Schedules.

**Section 2 — Template drift:**
Schedules whose layout template no longer matches the rule that would select them today.
This happens when someone renames a panel after its schedule was created, or when the
template rules change. The fix is to delete the drifted schedule and re-run Batch Panel
Schedules, which will re-create it with the correct template.

**Section 3 — Missing panel parameters:**
Panels that have a schedule but are missing key parameter data — for example, the
`ELC_PNL_VOLTAGE_TXT` is blank, so the schedule header cannot show the voltage. The
fix is to fill in the missing parameters on the panel element. Select the panel in the
model, press `E` to open Properties, and fill in the blank `ELC_PNL_*` fields.

**Reading the audit traffic-light status:**

| Colour | Meaning |
|--------|---------|
| Green | This panel has a schedule with all parameters filled in. No action needed. |
| Amber | This panel has a schedule but some parameters are missing or template has drifted. |
| Red | This panel has no schedule at all, or the schedule exists but the template is unavailable. |

---

### Exporting panel schedules to Excel (Export Panel Schedules to Excel)

This is for sending panel schedule data to a contractor for them to fill in circuit
descriptions and load data, or for including in a client report or quantity surveyor
submission.

**Steps:**

1. In the STING panel, go to the **BIM** tab.
2. Click **Export Panel Schedules to Excel**.
3. A file dialog opens. Choose a location and name for the Excel file.
4. STING creates an `.xlsx` file with:
   - One worksheet per panel, named after the panel (e.g. "DB-L01-A")
   - A **Header** section on each worksheet with the panel name, voltage, rating, and
     fed-from reference
   - A **Body** section with one row per circuit slot: slot number, phase, circuit number,
     description, load, breaker size
   - A **Summary** section with totals and phase balance
   - An **INDEX** worksheet listing all panels with links to their worksheets

**Typical uses:**
- Send to the contractor with the message "please fill in circuit descriptions and
  measured load values in the Body section"
- Send to the QS for take-off of breaker sizes and ratings
- Attach to the O&M manual as a deliverable

---

### Importing panel schedules from Excel (Import Panel Schedules from Excel)

After a contractor has filled in circuit descriptions, measured loads, or breaker sizes in
the Excel workbook, import it back into Revit.

**The anti-erasure guard — read this before importing:**

STING will NOT overwrite a Revit cell with a blank Excel cell. This is intentional. It
means that if the contractor receives a file with some data already filled in and they
accidentally delete it, the import will not wipe out what was in Revit. The rule is:

> **If the Excel cell is blank and the Revit cell has data, keep the Revit data.**
> **If the Excel cell has data, write it to Revit regardless.**

This prevents disasters from partial edits. If you genuinely need to clear a cell in
Revit, do it directly in the Revit panel schedule view — do not try to blank it in Excel
and import.

**Steps:**

1. In the STING panel, go to the **BIM** tab.
2. Click **Import Panel Schedules from Excel**.
3. A file dialog opens. Navigate to the filled-in Excel workbook.
4. STING opens the file, reads each worksheet, and matches it to the corresponding
   `PanelScheduleView` by name.
5. For each body cell in the worksheet, STING writes the value into the corresponding
   cell in the Revit schedule using `TableSectionData.SetCellText`.
6. A result panel shows:
   - **Cells updated:** how many schedule cells were written
   - **Cells skipped (blank in Excel):** how many cells were protected by the anti-erasure
     guard
   - **Cells rejected:** cells that Revit refused to write — these are computed fields
     like total load (calculated automatically by Revit from connected circuit data) and
     cannot be written manually. This is normal and expected.
   - **Worksheets not matched:** panel names in the Excel file that do not correspond to
     any panel schedule in the project. Check the spelling matches exactly.

> **Stuck?** If you see a large number of "Cells rejected," do not be alarmed. Revit
> computes certain schedule cells itself (total apparent load, power factor, phase totals)
> and blocks any attempt to write to them from the API. This is Revit's design, not a
> STING bug. The rejected cells will show the correct computed values automatically once
> the circuit data is in Revit.

---

### Managing spare and space slots

When a distribution board has more slots than active circuits, the empty slots should be
declared as either SPARE (a circuit breaker is fitted but the circuit goes nowhere — ready
for future use) or SPACE (the slot is physically empty — no breaker fitted). This
distinction matters for safety and for the O&M manual.

| Button | What it does | When to use |
|--------|-------------|-------------|
| **Fill Empty Slots (Spares)** | Marks every empty slot in the active panel schedule as SPARE using `AddSpare` | When all unfitted slots have breakers installed, just no circuits connected |
| **Fill Empty Slots (Spaces)** | Marks every empty slot in the active panel schedule as SPACE using `AddSpace` | When the slots are physically empty (no breakers) |
| **Fill All Spares (Project-Wide)** | Marks every empty slot in every panel schedule in the project as SPARE | One-click operation to fill all empty slots across the whole building — run this as part of the Panel Schedule Production Workflow |
| **Convert Spaces to Spares** | Changes SPACE declarations to SPARE using `RemoveSpace` then `AddSpare` | When breakers have been fitted to previously empty slots |
| **Clear Spares and Spaces** | Removes all SPARE and SPACE declarations from the active schedule | When you need to reset and start again — use with care |

**Where to find these buttons:**

All five are in the **BIM** tab of the STING panel, in the **Panel Schedules** section.

---

### The Panel Schedule Production Workflow (all steps in order)

This is the end-to-end sequence that takes you from raw panels in the model to fully
populated, contractor-approved panel schedules ready to go on drawings. Run these steps
in order.

```
Step 1 — Audit
   Button: Panel Schedule Audit
   Purpose: Find which panels have no schedule, which have template drift,
            and which are missing parameter data.
   What to do with the result: fix any red items (missing templates, wrong
   family categories) before proceeding.

Step 2 — Batch Create
   Button: Batch Panel Schedules
   Purpose: Create one PanelScheduleView for every panel that does not have one.
   Expected result: "N schedules created" where N = number of panels with
   no existing schedule.

Step 3 — Fill Spares
   Button: Fill All Spares (Project-Wide)
   Purpose: Mark all empty slots as SPARE so the schedule looks complete.
   Expected result: Every panel schedule shows SPARE in the unfilled rows.

Step 4 — Export to Excel
   Button: Export Panel Schedules to Excel
   Purpose: Send the schedules to the contractor or client for data fill-in.
   Expected result: One .xlsx file with one worksheet per panel.

Step 5 — Contractor fills in data
   (manual step — outside of Revit)
   Contractor fills in: circuit descriptions, measured loads, confirmed
   breaker sizes, and any corrections to phase assignments.

Step 6 — Import from Excel
   Button: Import Panel Schedules from Excel
   Purpose: Write the contractor's data back into the Revit schedules.
   Expected result: Circuit descriptions and loads populated in all schedules.

Step 7 — Re-Audit
   Button: Panel Schedule Audit
   Purpose: Confirm that all panels are green — schedules created, parameters
   filled, no template drift.
   Expected result: All panels green.
```

**Running the workflow as a single preset:**

You can run the entire Batch Create → Fill Spares → Export → Re-Audit chain as an
automated workflow preset:

1. In the STING panel, go to the **BIM** tab.
2. In the **Workflows** section, click **Workflow Preset**.
3. In the list that appears, select **PanelScheduleProduction**.
4. Click **Run**.
5. The workflow engine runs each step in sequence, pausing between steps to show you
   the result of each one. You can cancel at any point.

Note that the Import step is not included in the preset because it requires you to supply
an Excel file — you run that separately after the contractor returns the workbook.

---

## Chapter 5 — Circuit Assignment

### What circuit assignment means

Every light fitting, socket outlet, and electrical device in a building is powered by a
circuit. That circuit connects to a breaker in a distribution board. Circuit assignment
is the process of recording which device is on which circuit and which circuit connects
to which panel.

This matters for three reasons:
1. The panel schedule cannot be complete until every circuit is assigned to a panel.
2. The voltage drop calculation (BS 7671 Appendix 4) needs to know the circuit length,
   which STING calculates from the conduit route — but it can only calculate the route
   if it knows which panel the circuit goes to.
3. BS 7671 requires circuit identification: every circuit must be identifiable from
   the panel schedule. Without assignment, this is impossible.

### How STING records circuit assignment

The two key parameters:

| Parameter | What it stores | Example value |
|-----------|---------------|---------------|
| `ELC_CIRCUIT_NUM_TXT` | The circuit identifier — the number as it appears on the breaker | `L1.1` (meaning: panel L, section 1, circuit 1) |
| `ELC_PANEL_ID_TXT` | Which panel this circuit belongs to | `DB-L01-A` |
| `ELC_PANEL_REF_TXT` | Written on the Revit ElectricalSystem — links the circuit object to the panel | Set automatically by BatchAssignCircuits |

When `ELC_CIRCUIT_NUM_TXT` and `ELC_PANEL_ID_TXT` are filled in on an element, that
element's row in the panel schedule will show its circuit identifier automatically.

### Automatic circuit assignment with Batch Assign Circuits

For large projects with dozens of panels and hundreds of circuits, manual assignment is
impractical. STING's automatic assignment algorithm places every unassigned circuit onto
the most suitable panel.

**Before running auto-assignment:**
- All panels must be placed in the model and tagged
- Panels must have their `ELC_PNL_VOLTAGE_TXT` and number-of-circuits parameters filled

**Steps:**

1. In the STING panel, go to the **BIM** tab.
2. In the **Circuit Management** section, click **Batch Assign Circuits**.
3. STING scans every `ElectricalSystem` in the model. For any circuit with no
   `BaseEquipment` (no assigned panel), it runs the assignment algorithm.
4. A **preview** panel appears first showing you the proposed assignments. For each
   circuit it shows: circuit name → proposed panel, reasoning (same room, same voltage
   band, available slots).
5. Review the preview. If any assignment looks wrong, note it — you can correct it
   manually after the main run.
6. Click **Apply** to commit the assignments. STING calls
   `ElectricalSystem.SelectPanel` for each circuit, which is the Revit API call that
   actually attaches the circuit to the panel.
7. After apply, STING stamps:
   - `ELC_PANEL_REF_TXT` on every `ElectricalSystem` with the panel ID
   - `ELC_CIRCUIT_GROUP_TXT` on elements to record which logical group they belong to

**The grouping rules:**

STING uses four built-in grouping rules to keep circuits together:

| Group | How it identifies elements | Why it matters |
|-------|--------------------------|----------------|
| `kitchen` | Room name contains "kitchen", "kitchenette", "tea point", or "pantry" | Kitchen circuits should share a panel for HVAC zoning |
| `emergency-lighting` | System name contains "emergency" or "EM_" | Emergency lighting must be on a dedicated supply per BS 5266-1 |
| `fire-alarm` | Category is Fire Alarm Devices | Fire alarm supply must be dedicated per BS 5839-1 |
| `comms-room` | Room name contains "server", "MER", "IDF", "comms", or "patch" | Critical IT circuits share a UPS-backed panel |

Circuits in the same group are assigned to the same panel wherever possible.

> **Stuck?** If the preview shows "No fit — no panel with matching voltage band has
> free slots," you have run out of panel capacity. Either add more panels to the model,
> or expand the `Number of Circuits` parameter on an existing panel, then run the command
> again.

### Phase balance

A three-phase distribution board has three buses: A, B, and C. If most circuits are on
phase A and few are on phase C, the supply transformer is unbalanced — it runs hotter
on one phase and cooler on the others. BS 7671 and good engineering practice require
the load to be distributed roughly equally across all three phases.

**To balance phases:**

1. In the STING panel, go to the **BIM** tab.
2. Click **Phase Balance**.
3. The command shows a **preview** of the current imbalance (the difference in kVA between
   the most-loaded and least-loaded phase) and the proposed improvement.
4. Review the preview. If the improvement looks reasonable, click **Apply best-effort**.
5. STING redistributes 1-pole circuits across phases A, B, and C.

> **Important limitation:** The Revit API does not allow direct writing of a circuit's
> starting phase through code (it is a read-only computed value). After applying, STING
> reports how many circuits it could reassign. For circuits where the reassignment failed,
> you need to move them to the correct slot column in the panel schedule view manually
> (left column = Phase A, middle = Phase B, right = Phase C in a standard 3-phase layout).

### Manual circuit assignment

For small projects or corrections to auto-assignment:

1. Select the element you want to assign (e.g. a lighting fixture).
2. In the STING panel, go to the **CREATE** tab.
3. In the **Tokens** section, use the **Bulk Parameter Write** button to open the
   bulk operations dialog.
4. Set `ELC_CIRCUIT_NUM_TXT` to the circuit number (e.g. `L1.1`).
5. Set `ELC_PANEL_ID_TXT` to the panel ID (e.g. `DB-L01-A`).
6. Click **Apply to Selection**.

Alternatively, you can use Revit's native Electrical Systems tools to connect a fixture
directly to a panel via a circuit — STING will read the resulting `ElectricalSystem` link
automatically.

---

## Chapter 6 — Conduit and Cable Tray Routing

### The two routing approaches

STING supports two ways to create the conduit and cable tray runs that connect distribution
boards to devices:

| Approach | What it does | Best for |
|----------|-------------|---------|
| **AutoConduitDrop** (Auto Drop) | Automatically calculates and creates conduit runs from the panel to each connected device | Large projects where manual routing would take days; design-stage coordination drawings |
| **Manual routing** with STING tagging | You draw conduits using Revit's native MEP tools; STING tags them and records their data | Small projects, complex routing situations where the algorithm cannot find a good path |

### Using Auto Drop to route conduits automatically

Auto Drop creates conduit elements in the model based on the circuit assignments you
completed in Chapter 5. For each unrouted cable in the manifest, it calculates a
Manhattan L or Z path between the device and its panel, then creates the conduit segments.

**Before running Auto Drop:**
- Circuit assignment must be complete (Chapter 5)
- At least one `ConduitType` family must be loaded in the project
- The seed junction box family (`STING_SEED_JunctionBox`) should be loaded — if it is
  not, Auto Drop will still route conduits but cannot place junction boxes automatically

**Steps:**

1. In the STING panel, go to the **TAGS** tab.
2. Click the **Routing** sub-tab.
3. Click **Auto Drop**.
4. A dialog asks for routing settings:
   - **Drop type:** Conduit (rigid), Conduit (flexible), or Cable Tray — choose the
     appropriate containment for the cable type
   - **Offset from ceiling:** the distance the conduit runs below the ceiling slab or
     within the ceiling void (in mm, default 300 mm)
   - **Conduit type family:** select from the loaded `ConduitType` families
5. Click **Route All** to route every unrouted cable, or **Route Selected** to route
   only the circuits connected to selected elements.
6. STING creates the conduit segments and runs three post-processing passes:

**Pass 1 — Junction box placement:**
For every conduit run that exceeds 3 bends between pull-points (per BS 7671 §522.8.5)
or exceeds 6 metres between pull-points (per BS 7671 §522.8.4), STING places a
`STING_SEED_JunctionBox` family instance at the violation point. Each placed box gets
stamped with `ELC_JB_AUTO_PLACED_BOOL = 1` and `ELC_JB_REASON_TXT` (either
`BENDS_EXCESS` or `RUN_TOO_LONG`).

**Pass 2 — Slab penetration detection:**
For every conduit segment that passes vertically through a floor slab, STING records the
penetration and places a fire-stop symbol (`STING_SEED_SpecialityEquipment`) on the slab
face. Each penetration gets a sequential control number in `PEN_CONTROL_NUMBER_TXT`
(e.g. `FRP-0001`, `FRP-0002`) and a fire rating read from the slab's
`STING_FIRE_RATING_TXT` parameter (default `FR60` if not set).

**Pass 3 — Cable fill stamping:**
STING calculates the fill percentage of each conduit (how much of its internal area is
used by the cables inside it) and stamps this in `ELC_CDT_CBL_FILL_PCT`. Conduits over
the BS EN 61386 limit (40% for a single cable, 35% with bends, 31% for two cables) are
flagged in red.

> **Stuck?** If "Auto Drop: 0 cables routed" appears and all cables are skipped with
> "no panel," circuit assignment is not complete. Go back to Chapter 5 and run
> **Batch Assign Circuits** first.

> **Stuck?** If junction boxes are not being placed ("Junction boxes auto-placed: 0"
> in the result), the `STING_SEED_JunctionBox` family is not loaded. Go to
> **TEMP → Build Seed Families** to create and load it.

### Creating conduit runs manually (and tagging them)

When you draw conduits using Revit's native `Systems → Electrical → Conduit` tool, STING
can tag them and record their data automatically:

1. Draw the conduit run using Revit's standard tools.
2. Select the drawn conduit elements.
3. In the STING panel, go to the **CREATE** tab.
4. Click **Auto Tag**.
5. Choose **Selection** scope.
6. STING tags the conduits with:
   - `DISC = E` (electrical conduit)
   - `SYS = LV` (or the system code matching the circuit carried)
   - `ELC_CDT_INSTALL_METHOD_TXT` (the installation method — surface, flush, soffit, etc.)
   - `ELC_CDT_RUN_LENGTH_M` (calculated from the conduit geometry)

### Conduit consolidation

When multiple circuits share the same route between a panel and a zone, it is inefficient
(and physically wrong) to have a separate conduit for each one. Conduit consolidation
groups circuits sharing the same route and merges them into a single larger conduit.

1. In the STING panel, go to the **TAGS** tab.
2. In the **Routing** sub-tab, click **Consolidate Conduits**.
3. A **preview** panel shows consolidation opportunities: groups of cables going to the
   same panel from the same source equipment, with the proposed single conduit size and
   fill percentage.
4. Review the preview to confirm the groupings make sense.
5. Click **Apply Consolidation** to merge the conduits.

> The apply operation is a single undo step — if the result looks wrong, press `Ctrl+Z`
> once to undo the entire consolidation.

### Cable tray routing

Cable trays follow the same workflow as conduits. The **Auto Drop** command offers cable
tray as a routing option. When you choose cable tray:
- STING creates `CableTray` elements instead of `Conduit` elements
- The fill calculation uses the cable tray's cross-section area
- The resulting elements are tagged with `ELC_CTR_*` parameters instead of `ELC_CDT_*`

Cable tray is typically used for large bundles of cables in plant rooms, risers, and
main distribution routes. Individual final circuits to devices (sockets, lights) normally
use conduit.

---

## Chapter 7 — Lighting Design

### The lighting workflow at a glance

```
1. Place light fitting families (Placement Centre or manual)
2. LightingGrid — automatic layout across rooms
3. Auto Tag — DISC=E, SYS=LTG, PROD=fitting type
4. Assign emergency lighting (LTG_EMERG_BOOL)
5. Assign circuits (Chapter 5)
6. Create lighting schedule (MEP Schedules)
7. Produce lighting plan drawings (Chapter 12)
```

### LightingGridCommand — automatic lighting layout

The Lighting Grid command calculates how many light fittings a room needs to meet its
target illuminance (measured in lux — lux is a unit of light intensity, like how bright
something is). It then places fittings at an even grid across the room floor area.

**How it calculates the number of fittings:**

STING uses the lumen method — the standard calculation method from BS EN 12464-1
(the British Standard for workplace lighting). The formula is:

```
Number of fittings = ceiling((target lux × room area m²) ÷ (lumens per fitting × UF × MF))
```

Where:
- **Target lux** — the required illuminance for the room type, from BS EN 12464-1.
  STING looks up the room type from its name (e.g. "Office" → 500 lux, "Corridor" → 100
  lux, "Operating Theatre" → 1000 lux).
- **Lumens per fitting** — the light output of the chosen fitting, read from the
  family's `ELC_PHOTO_LUMENS` parameter. If this parameter is not set, STING uses a
  conservative default of 3500 lm.
- **UF (Utilisation Factor)** — how efficiently the light reaches the working plane.
  Default 0.60 (60%). Can be overridden per family with `ELC_LIGHTING_UF_FACTOR`.
- **MF (Maintenance Factor)** — accounts for dirt and ageing. Default 0.80 (80%).
  Can be overridden per family with `ELC_LIGHTING_MF_FACTOR`.

**Steps to use Lighting Grid:**

1. Select the rooms you want to illuminate. You can select room elements in the model,
   or leave nothing selected to run across all rooms in the project.
2. In the STING panel, go to the **TAGS** tab.
3. Click the **Fixtures** sub-tab.
4. Click **Lighting Grid**.
5. A dialog appears with the following settings:
   - **Fixture family:** select which `Lighting Fixtures` family to use. This should
     be the family with `ELC_PHOTO_LUMENS` filled in.
   - **Grid spacing X (mm):** the distance between fitting centres in the long direction
     of the room. Leave at 0 to let STING calculate the spacing from the lumen method
     automatically.
   - **Grid spacing Y (mm):** spacing in the short direction. Leave at 0 for automatic.
   - **Boundary type:** how STING defines the area to fill. `Room boundary` uses the
     Revit room boundary lines (most accurate). `Crop region` uses the view crop boundary.
   - **Offset from boundary (mm):** keeps fittings this far from walls (default 600 mm,
     matching CIBSE guidance for edge-of-room fittings).
6. Click **Place Fittings**.
7. STING calculates the grid and places the fittings. Fittings that would clash with
   existing ceiling MEP (sprinkler heads, diffusers, detectors) are automatically moved
   to the nearest clear position.
8. The result panel shows: rooms processed, fittings placed, average lux achieved
   (based on the lumen method calculation), and any rooms where the target could not be
   achieved (because the fitting output is too low or the room too large).

> **Stuck?** If the command places fittings but they all land at the wrong height (e.g.
> on the floor instead of the ceiling), the fitting family's origin is at the bottom of
> the element rather than the top. This needs to be corrected in the Family Editor.
> See **MEP_FOUNDATION_GUIDE.md, Chapter 2** for how to check and correct the family
> origin point.

**The lux target lookup:**

STING classifies rooms by matching the room name against patterns in its internal
`ROOM_TYPE_CLASSIFIER.csv` file. Common mappings:

| Room name pattern | Target lux | Standard reference |
|-------------------|-----------|-------------------|
| Office, open plan | 500 lux | BS EN 12464-1 Table 5.3 |
| Meeting room | 500 lux | BS EN 12464-1 |
| Reception, lobby | 300 lux | BS EN 12464-1 |
| Corridor, stairway | 100 lux | BS EN 12464-1 |
| Toilet, WC | 200 lux | BS EN 12464-1 |
| Warehouse, store | 100 lux | BS EN 12464-1 |
| Workshop, laboratory | 500 lux | BS EN 12464-1 |
| Plant room, roof | 200 lux | CIBSE LG7 |
| Classroom | 300 lux | BS EN 12464-1 |
| Operating theatre | 1000 lux | BS EN 12464-1 healthcare annex |

### Emergency lighting

Emergency lighting must be on a dedicated supply circuit, separate from the normal
lighting circuits, per BS 5266-1. STING tracks which fittings are emergency fittings
using the `LTG_EMERG_BOOL` parameter (1 = emergency, 0 = normal).

**To designate fittings as emergency:**

1. Select the fittings that are emergency luminaires.
2. In the STING panel, go to the **CREATE** tab.
3. Click **Bulk Parameter Write**.
4. In the dialog:
   - **Operation:** Set Value
   - **Parameter:** `LTG_EMERG_BOOL`
   - **Value:** `1`
5. Click **Apply to Selection**.

**What changes after setting this flag:**
- The element's `DISC` tag includes an emergency marker
- Auto-assign circuits will route emergency fittings to an emergency-lighting group panel
  (or flag them if no dedicated panel exists)
- The lighting schedule shows these fittings in a separate section

### Creating a lighting schedule

A lighting schedule is a table listing all light fittings in the project: their type,
location, circuit, lux level, and emergency status.

1. In the STING panel, go to the **BIM** tab.
2. In the **MEP Schedules** section, click **Lighting Fixture Schedule**.
3. STING creates a Revit schedule named "STING - Lighting Fixtures" with columns for:
   - Room name and level
   - Fitting type and model
   - Circuit number (`ELC_CIRCUIT_NUM_TXT`)
   - Panel ID (`ELC_PANEL_ID_TXT`)
   - Emergency status (`LTG_EMERG_BOOL`)
   - Target lux (`ELC_LIGHTING_TARGET_LUX_TXT`)
   - Lumens per fitting (`ELC_PHOTO_LUMENS`)
4. The schedule appears in the Project Browser under **Schedules/Quantities**.

---

## Chapter 8 — Wire Annotations

### What wire annotations add to an electrical drawing

A floor plan shows you where electrical devices are located in a room. But it does not
tell you how the wiring runs between them, which way the homerun (the cable going back
to the panel) goes, how many conductors are in the cable, or what size the conductors
are. Wire annotations add this information directly onto the drawing.

An electrician on site reads wire annotations to know:
- Which cables to pull through which conduits
- How many conductors to use and what size
- Where the circuit feeds from (the homerun direction)
- Which circuit number the cable carries

Wire annotations are 2D line symbols placed directly over the conduit route on the floor
plan, with text showing the circuit number and conductor count.

### Creating wire annotation families

Wire annotation families are specialised Revit annotation families (`.rfa` files with the
Generic Annotation template) that display circuit information next to a conduit or cable
route.

> See **MEP_FOUNDATION_GUIDE.md, Chapter 1, Section 1.4 — Wire Annotation Workflow** for
> the complete guide to creating wire annotation families from scratch, including the
> standard slash-mark symbols for 3-conductor, 4-conductor, and 5-conductor cables, and
> the homerun arrow symbol.

### Placing wire annotations in your electrical drawings

Wire annotations should be placed after circuit assignment is complete (Chapter 5) and
after Auto Drop has created the conduit runs (Chapter 6), because the annotations label
the conduits and circuits.

**Sequence:**

1. Open the electrical floor plan view at the correct level. Ensure the view uses the
   `STING - Electrical Plan` view template.
2. Place the homerun annotation first. The homerun annotation goes at the point where
   a group of circuits leaves a room and heads back toward the panel. This is typically
   at the room wall closest to the panel.
   - Go to **Annotate → Component → Detail Component** in the Revit ribbon.
   - Select your homerun arrow annotation family.
   - Click at the homerun departure point.
   - Rotate to show the direction toward the panel.
   - Type the circuit numbers in the annotation's parameter field.
3. Add conductor slash marks. These short diagonal marks across a conduit line show
   how many conductors are in the cable.
   - Use the same **Detail Component** placement tool.
   - Select the conductor-count annotation family (3-slash for a 3-core cable, etc.).
   - Click on the conduit line.
4. Verify the circuit number label populates. If `ELC_CIRCUIT_NUM_TXT` is filled in on
   the conduit (written by Auto Tag), the annotation should display the circuit number
   automatically. If it shows "?" or is blank, the parameter binding in the annotation
   family may not be set up correctly — see the wire annotation guide referenced above.

### Batch wire annotation

For large plans with many circuits, placing annotations one by one is slow. STING's Smart
Tag Placement system can place annotation families in bulk:

1. In the STING panel, go to the **CREATE** tab.
2. In the **Smart Placement** section, click **Smart Place Tags**.
3. In the scope dialog, choose **Active View**.
4. The command uses the tag placement template. If you have set up a placement template
   that includes your wire annotation family (see the Smart Placement section in the
   MEP_FOUNDATION_GUIDE.md), it will place annotations at the correct positions along
   conduit runs automatically.
5. A result panel shows how many annotations were placed and how many positions were
   skipped due to collision with existing annotations or elements.

---

## Chapter 9 — Fire Alarm and Security Systems

### STING disciplines for safety systems

Fire alarm and security systems use different Revit categories and different STING
discipline codes from general electrical power, even though they are fed from the same
incoming supply.

| System | STING discipline code | Revit category | Tag container |
|--------|----------------------|----------------|---------------|
| Fire alarm | `FLS` | Fire Alarm Devices | `FLS_DEV_TAG` |
| Sprinklers | `FP` | Sprinklers | (general tag) |
| Security CCTV | `SEC` | Communication Devices | `SEC_DEV_TAG` |
| Door access | `SEC` | Communication Devices | `SEC_DEV_TAG` |
| Nurse call | `NCL` | Nurse Call Devices | `NCL_DEV_TAG` |

### The fire alarm workflow

**Step 1 — Place fire alarm devices:**

Use the Placement Centre or manual placement to place detector, call point, sounder, and
beacon families. All fire alarm families must be in the `Fire Alarm Devices` Revit
category (not `Specialty Equipment` or `Generic Models`).

> See **MEP_FOUNDATION_GUIDE.md, Chapter 3 — The Placement Centre** for the placement
> workflow.

**Step 2 — Auto-tag fire alarm elements:**

1. In the STING panel, go to the **CREATE** tab.
2. Click **Auto Tag**.
3. Choose scope: **Active View** or **Entire Project**.
4. STING assigns:
   - `DISC = FLS` to all `Fire Alarm Devices` elements
   - `SYS = FLS` (fire and life safety system)
   - `PROD` codes from the family name: `DET` for detectors, `CPT` for call points,
     `SND` for sounders, `BCN` for beacons, `FAP` for fire alarm panels

**Step 3 — Create fire alarm drawings:**

Fire alarm drawings use the drawing type `elec-fire-alarm-A1-1to100`. This drawing type
applies:
- The `corp-standard-plan` view style pack with fire alarm device categories highlighted
  in red
- Scale 1:100 on A1 paper
- The `STING - Fire Alarm Plan` view template

To produce the drawing:

> See **DRAWING_PRODUCTION_SYSTEM_GUIDE.md** for the complete drawing production workflow.

**Step 4 — Create a fire alarm device schedule:**

1. In the STING panel, go to the **BIM** tab.
2. In the **MEP Schedules** section, click **Fire Alarm Schedule** (or use the generic
   **Device Schedule** and filter for `DISC = FLS`).
3. The schedule lists every fire alarm device with its tag, type, room location, and zone.

### Security system workflow

The security system workflow follows the same steps as fire alarm, with these differences:
- Families must be in the `Communication Devices` Revit category
- DISC code is `SEC`
- Tag container is `SEC_DEV_TAG`
- Parameters use the `SEC_*` prefix group

For CCTV cameras, the most important security-specific parameter is the coverage zone:
stamp `SEC_CAM_COVERAGE_TXT` with the zone identifier (e.g. "Zone A - Ground Floor
Reception") using **Bulk Parameter Write**.

---

## Chapter 10 — Riser Diagrams

### What a riser diagram is

A riser diagram (also called a schematic or single-line diagram) shows how electrical
power flows through the building from the incoming supply down to every distribution
board on every floor. Unlike floor plans — which show where things are physically located
in plan view — a riser diagram is schematic: it is a logical diagram showing the
connections and relationships.

Imagine the building's electrical system as a tree. The root of the tree is the mains
incoming supply. The trunk is the main switchboard. The branches are the sub-main cables
going to distribution boards on each floor. The leaves are the final circuits going to
individual sockets and lights. A riser diagram draws this tree from top to bottom,
showing the cable size, the breaker rating, and the protection device at each junction.

An electrical engineer uses the riser diagram to:
- Verify that the protection discrimination is correct (each downstream breaker is smaller
  than the upstream one, so only the correct breaker trips on a fault)
- Calculate voltage drop from the supply to each final circuit
- Show the building owner and the distribution network operator how the supply is
  distributed
- Provide a schematic to accompany the panel schedules in the O&M manual

### The elec-riser-A2-1to100 drawing type

Riser diagrams in STING use the drawing type `elec-riser-A2-1to100`:
- Paper size: A2
- Scale: 1:100
- Orientation: typically landscape
- View type: **Drafting View** (not a floor plan) — because a riser diagram is a 2D
  schematic, not a model view

### Creating a riser diagram in STING

Because riser diagrams are schematic rather than model-based, they are created as
Drafting Views with 2D detail components:

1. In the Revit ribbon, go to **View → Drafting View**. Give it a meaningful name
   such as "Electrical Riser Diagram — Ground Floor to Roof."
2. In the STING panel, go to the **DOCS** tab.
3. Click **Inspect Drawing Types** and confirm `elec-riser-A2-1to100` is in the list.
4. With your new Drafting View open, go to the **DOCS** tab.
5. In the **Drawing Types** section, click **Sync Styles**. This applies the correct
   view style (line weights, annotation standards) to the active view based on its
   drawing type stamp.
6. Place 2D detail components from your electrical symbol library into the Drafting View.
   Use:
   - Rectangle or filled region for distribution boards
   - Lines for cables/conduits between boards
   - Text or tag annotations for cable sizes and breaker ratings
   - The detail component symbol families from your office standard

> **Tip:** Your office's riser diagram symbol families should be loaded in the project
> before you start. If you are using the STING seed families, the
> `STING_SEED_ElectricalEquipment` family with the `DISTRIBUTION_BOARD_DB` type can be
> placed in a Drafting View as a 2D schematic symbol.

---

## Chapter 11 — Electrical Validation and QA

Before a set of electrical drawings is issued, you need to verify that everything is
complete and correct. STING provides several validation commands for electrical work.

### ValidateTagsCommand — checking all electrical elements are tagged correctly

This is the basic completeness check: every electrical element in the model must have all
8 tag segments filled in correctly.

**Steps:**

1. In the STING panel, go to the **CREATE** tab.
2. Click **Validate Tags**.
3. The validation report opens with a table. Each row is a finding. The columns are:
   - **Element ID:** the Revit element's unique ID — you can click this to zoom to the
     element in the model
   - **Category:** what kind of element it is
   - **Tag:** the current tag value (may be partial or empty)
   - **Issue:** what is wrong
   - **Severity:** Error (must fix) or Warning (should review)

**Common electrical validation errors:**

| Error message | What it means | Fix |
|---------------|--------------|-----|
| DISC code invalid: "M" for Electrical Equipment | The element got a mechanical discipline code because its family is in the wrong Revit category | Change the family category to `Electrical Equipment` in the Family Editor |
| SYS code blank | The element is not connected to a Revit MEP system | Connect it to a system, or manually set `ELC_CIRCUIT_NUM_TXT` and re-run Auto Tag |
| PROD code "GEN" (generic) | The family name doesn't match any known product code pattern | Set the family name to include a recognisable product abbreviation (see the Tag Configuration for the list) |
| SEQ duplicate: 0042 | Two elements have the same sequence number in the same DISC/SYS/LVL group | Run **Repair Duplicate Seq** in the CREATE tab to auto-fix duplicates |

### Panel Schedule Audit — checking panel schedules are complete

This was covered in detail in Chapter 4. Run it again before issue as the final check.

> See **Chapter 4 — Panel Schedules, Auditing panel schedules** for full instructions.

### RunAllValidatorsCommand — the full validation chain

This is the comprehensive QA run. It executes every validator in sequence and produces a
single combined report.

1. In the STING panel, go to the **TAGS** tab.
2. In the **Routing** sub-tab, click **Run All Validators**.
3. The validators run in this sequence:
   - Connectivity validator (checks all MEP connections are valid)
   - Fill validator (checks conduit and tray fill percentages)
   - Spec validator (checks parameters against project specs)
   - Termination validator (checks all circuits terminate at a panel)
   - Slope validator (applies to pipework, not electrical — completes quickly)
   - BS 7671 standards validator (runs the electrical-specific checks below)
4. A result panel shows the total finding count by severity, with a breakdown by
   validator. Click any finding to zoom to the element in the model.

### BS 7671 Validation — the electrical-specific standards check

The BS 7671 validator specifically checks IET Wiring Regulations compliance for conduit
installations.

1. In the STING panel, go to the **TAGS** tab.
2. In the **Routing** sub-tab, click **Validate Fills**.
3. The validator checks every conduit in the model:

| Finding code | Severity | What it means | Fix |
|-------------|----------|--------------|-----|
| `ELEC.RUN.LONG` | Warning | A conduit run is longer than 6 metres between pull-in points (BS 7671 §522.8.4) | Add a junction box or draw-in point along the run |
| `ELEC.BENDS.EXCESS` | Error | A conduit has more bends than allowed between pull-in points (BS 7671 §522.8.5) | Add a junction box at the violation point — the result panel tells you where |
| `ELEC.FILL.OVER` | Error | Cable fill in the conduit exceeds the BS EN 61386 limit (40% for 1 cable, 31% for 2 cables, 35% for 3+ cables) | Either increase the conduit diameter or reduce the number of cables in the conduit |
| `ELEC.FILL.NEAR` | Warning | Fill is within 10% of the limit — marginal | Consider upsizing the conduit |
| `ELEC.BEND.ANGLE` | Warning | A fitting has a non-standard bend angle | Swap the fitting for the nearest standard angle (11.25°, 22.5°, 30°, 45°, 90°) |

**Fill rate standards explained:**

The BS EN 61386 fill limits exist to make sure you can actually pull cables through the
installed conduit. The more cables you put in a conduit, the harder they are to pull, and
the more heat builds up inside. The limits differ based on how many cables are in the
conduit:

| Cables in conduit | Maximum fill (straight run) | Maximum fill (with bends) |
|-------------------|-----------------------------|--------------------------|
| 1 cable | 53% | 43% |
| 2 cables | 31% | 20% |
| 3 or more cables | 40% | 35% |

### ValidateFillsCommand — dedicated fill check

For a project where you only want to check the fill rates without running the full
validation chain:

1. In the STING panel, go to the **TAGS** tab.
2. In the **Routing** sub-tab, click **Validate Fills**.
3. STING calculates fill for every conduit and cable tray, stamps the value in
   `ELC_CONDUIT_FILL_PCT`, and highlights failing elements in red in the active view.
4. The result panel shows a summary table: conduit size → number passing → number failing
   → worst-case fill percentage.

---

## Chapter 12 — Producing and Issuing Electrical Drawings

### Which drawing types apply to electrical work

| Drawing type ID | Paper size | Scale | Purpose | When to produce |
|-----------------|-----------|-------|---------|----------------|
| `elec-power-A1-1to100` | A1 | 1:100 | Electrical power distribution plan | Shows panel locations, socket layout, circuit routes |
| `elec-lighting-A1-1to100` | A1 | 1:100 | Lighting layout plan | Shows fitting positions, emergency designation, lux zones |
| `elec-fire-alarm-A1-1to100` | A1 | 1:100 | Fire detection and alarm plan | Shows detector, call point, sounder, and panel locations |
| `elec-riser-A2-1to100` | A2 | 1:100 | Electrical riser / schematic diagram | Shows supply hierarchy from incoming to final boards |
| `elec-panel-schedule-A3` | A3 | n/a (schedule) | Panel schedule sheet | One sheet per distribution board |

### Producing electrical drawings

The complete drawing production workflow — including how to set up title blocks, apply
drawing types, run the sheet batch command, and manage scale — is covered in the drawing
production guide.

> See **DRAWING_PRODUCTION_SYSTEM_GUIDE.md** for the complete drawing production workflow,
> including how to assign drawing types, produce sheets in batch, and apply view style
> packs to electrical views.

### Issuing electrical drawings

Once the drawings are produced and the panel schedules are on their sheets, issue the
drawing package using the Document Manager.

> See **DOCUMENT_MANAGER_GUIDE.md** for the complete document issue workflow, including
> CDE status transitions, transmittal creation, and revision control.

### The WORKFLOW_PanelScheduleProduction.json preset

This workflow preset chains the five panel schedule production steps into a single
automated run. It is the fastest way to bring a project's panel schedule package to
issue-ready status.

**To run it:**

1. In the STING panel, go to the **BIM** tab.
2. In the **Workflows** section, click **Workflow Preset**.
3. In the workflow list, select **PanelScheduleProduction**.
4. Click **Run**.

**What happens at each step:**

| Step | Command | What to expect |
|------|---------|---------------|
| 1 | Panel Schedule Audit | Result panel shows current state — panels with and without schedules, parameter gaps |
| 2 | Batch Panel Schedules | Creates any missing schedules. A "N schedules created" message appears. If N = 0 and you were expecting new schedules, check that your panels are tagged correctly |
| 3 | Fill All Spares (Project-Wide) | Fills empty slots with SPARE declarations. No interactive step — runs silently and reports the count |
| 4 | Export Panel Schedules to Excel | A file-save dialog appears asking where to save the Excel workbook. Choose a location and click Save |
| 5 | Panel Schedule Audit (re-run) | Final check. Every panel should now show green status |

After Step 4, the workflow pauses and waits. Send the Excel file to the contractor or
client for review. When it comes back filled in, run **Import Panel Schedules from Excel**
separately, then run the workflow again from step 1 to re-audit and confirm convergence.

---

## Troubleshooting

| Problem | Likely cause | Fix |
|---------|-------------|-----|
| Panel schedule not created after running Batch Panel Schedules | None of the five STING panel schedule templates are loaded in the project | Open **View → View Templates → Panel Schedule Templates** in the Revit ribbon and confirm the five templates are present. If missing, ask your BIM manager to load them from the office template file |
| Circuit back-references not populating in the panel schedule | `ELC_PANEL_ID_TXT` is blank on the panel element itself | Select the panel element, open Properties, fill in `ELC_PANEL_ID_TXT` with the panel's identifier, then re-run Batch Panel Schedules |
| Auto Tag gives the wrong DISC code (e.g. "M" on a lighting fitting) | The lighting family is in the `Mechanical Equipment` Revit category instead of `Lighting Fixtures` | Open the family in the Family Editor, check the family category (use **Manage → Settings → Object Styles** to see the category), and change it to the correct one. Reload the family |
| LightingGrid places fittings at floor level instead of ceiling level | The family's origin point is at the bottom of the solid geometry | In the Family Editor, move the geometry so that the reference level plane is at the mounting surface (ceiling or slab underside). See **MEP_FOUNDATION_GUIDE.md, Chapter 2** |
| Wire annotation shows "?" instead of the circuit number | `ELC_CIRCUIT_NUM_TXT` is not bound to the annotation family's label parameter | Open the annotation family in the Family Editor, check the label parameter, and bind it to `ELC_CIRCUIT_NUM_TXT`. See **MEP_FOUNDATION_GUIDE.md, Chapter 1, Section 1.4** |
| Panel schedule template drift warning in audit | A panel was renamed after its schedule was created, so the template rule no longer matches | Delete the drifted schedule, rename the panel back (or update the template rules), and re-run Batch Panel Schedules |
| Auto Drop produces conduits that go through walls | The wall's `STING_WALL_ROUTING_FLAG` parameter is set to `ALLOW` | Set the flag to `DENY` on walls that the routing pathfinder should not pass through. Select the wall, open Properties, find `STING_WALL_ROUTING_FLAG`, and change it to `DENY` |
| Batch Assign Circuits puts all circuits on one panel | All panels except one are full (not enough free slots) | Add more panels or expand the `Number of Circuits` parameter on existing panels, then re-run the command |
| The `ELC_PHOTO_LUMENS` parameter does not appear on the lighting family | The shared parameters were not loaded before the family was placed | Run **Load Shared Params** in the TEMP tab, which binds `LTG_*` and `ELC_PHOTO_*` parameters to the `Lighting Fixtures` category |
| Penetration fire stops are not placed after Auto Drop | `STING_SEED_SpecialityEquipment` family is not loaded | Run **Build Seed Families** in the TEMP tab. The SpecialityEquipment seed includes the FR30/FR60/FR90/FR120 fire-stop types |
| Phase balance command reports "0 circuits reassigned" | All circuits are multi-pole (2-pole or 3-pole) and cannot be moved to a different phase individually | Multi-pole circuits are deliberately excluded from phase balancing. Manually move them to the desired phase column in the panel schedule view |
| Validation finds `ELEC.BENDS.EXCESS` on every conduit | The conduit routing produced too many bends because the panel and fixtures are on different floors with complex paths | Re-run Auto Drop with a larger ceiling offset to give the router more room, or route those conduits manually using a simpler path |

---

## Quick Reference

### All electrical commands (complete table)

| Button label | Panel tab | Sub-tab or section | What it does | When to use |
|-------------|-----------|-------------------|-------------|-------------|
| Load Shared Params | TEMP | Setup | Binds all electrical shared parameters to project | Once per project, before any electrical work |
| Master Setup | TEMP | Setup | Runs all setup steps in sequence (params, filters, templates) | At project start |
| Build Seed Families | TEMP | Templates | Creates 11 STING seed families and loads them | Once per project, after Master Setup |
| View Templates | TEMP | Templates | Creates STING standard view templates including 4 electrical ones | Once per project |
| Create Filters | TEMP | Templates | Creates VG filters for electrical element display | Once per project |
| Auto Tag | CREATE | (main) | Tags all or selected electrical elements with DISC/SYS/PROD/SEQ | After placing elements; after any batch placement |
| Validate Tags | CREATE | QA | Checks all tags are complete and correctly formatted | Before issuing drawings |
| Repair Duplicate Seq | CREATE | QA | Fixes duplicate sequence numbers in a discipline/system/level group | When Validate Tags reports SEQ duplicates |
| Batch Assign Circuits | BIM | Circuit Management | Automatically assigns all unassigned circuits to panels | After placing all fixtures and panels |
| Phase Balance | BIM | Circuit Management | Redistributes 1-pole circuits to balance load across 3 phases | After circuit assignment |
| Batch Panel Schedules | BIM | Panel Schedules | Creates one PanelScheduleView per panel | After panels are placed and tagged |
| Panel Schedule Audit | BIM | Panel Schedules | Read-only check: missing schedules, template drift, param gaps | Before and after Batch Panel Schedules |
| Export Panel Schedules to Excel | BIM | Panel Schedules | Exports all schedules to a multi-worksheet .xlsx file | Before sending to contractor for data fill-in |
| Import Panel Schedules from Excel | BIM | Panel Schedules | Imports contractor-filled data back into Revit | After contractor returns filled workbook |
| Fill Empty Slots (Spares) | BIM | Panel Schedules | Marks empty slots in active schedule as SPARE | Per-panel spare designation |
| Fill Empty Slots (Spaces) | BIM | Panel Schedules | Marks empty slots in active schedule as SPACE | Per-panel space designation |
| Fill All Spares (Project-Wide) | BIM | Panel Schedules | Fills all empty slots in every schedule as SPARE | Before export and issue |
| Convert Spaces to Spares | BIM | Panel Schedules | Changes SPACE declarations to SPARE | When breakers have been retrofitted to previously empty slots |
| Clear Spares and Spaces | BIM | Panel Schedules | Removes all spare/space declarations from active schedule | When resetting a schedule |
| Workflow Preset | BIM | Workflows | Runs a named workflow chain | Run PanelScheduleProduction for panel schedule batch processing |
| Auto Drop | TAGS | Routing | Routes conduits from panels to all connected devices | After circuit assignment |
| Validate Fills | TAGS | Routing | Checks conduit fill percentages against BS EN 61386 | Before issuing drawings |
| Consolidate Conduits | TAGS | Routing | Merges multiple per-cable conduits sharing a route into one | After initial routing to rationalise the model |
| Run All Validators | TAGS | Routing | Runs full validation chain (BS 7671 + all STING validators) | Final QA before drawing issue |
| Lighting Grid | TAGS | Fixtures | Places light fittings in a grid pattern based on lumen method | When designing a new lighting layout |
| Bulk Parameter Write | CREATE | Tokens | Sets a parameter value on multiple selected elements | For manual circuit assignment and emergency lighting designation |
| Smart Place Tags | CREATE | Smart Placement | Places annotation tags in bulk with collision avoidance | For batch wire annotation and tag placement |
| Inspect Drawing Types | DOCS | Drawing Types | Shows all registered drawing types | To verify electrical drawing types exist |
| Sync Styles | DOCS | Drawing Types | Applies the correct view style to stamped views | When a view looks wrong after template changes |
| MEP Schedules — Lighting Fixture Schedule | BIM | MEP Schedules | Creates a lighting fixture schedule | After lighting design is complete |
| MEP Schedules — Fire Alarm Schedule | BIM | MEP Schedules | Creates a fire alarm device schedule | After fire alarm layout is complete |
| Load Summary | BIM | Calcs | Calculates and stamps connected load per panel | Before voltage drop calculation |
| Voltage Drop | BIM | Calcs | Calculates voltage drop per circuit per BS 7671 Appendix 4 | After routing is complete |
| Breaker Sizer | BIM | Calcs | Suggests breaker rating per circuit from load and cable size | During design development |
| Apply Breakers | BIM | Calcs | Writes suggested breaker ratings to all circuits | After Breaker Sizer has been reviewed |

---

### Electrical parameter reference

| Parameter name | What it stores | Which elements carry it |
|----------------|---------------|------------------------|
| `ELC_PANEL_ID_TXT` | The distribution board identifier (e.g. "DB-L01-A") | Electrical Equipment (panels), also written on connected fixtures |
| `ELC_CIRCUIT_NUM_TXT` | The circuit number as it appears on the breaker (e.g. "L1.1") | All electrical fixtures, lighting fixtures, electrical equipment |
| `ELC_BREAKER_SIZE_A` | Breaker rating in amps (e.g. 16, 32, 63) | Electrical Equipment (panels); written per circuit in the schedule |
| `ELC_LOAD_KW` | Connected load in kilowatts | Electrical fixtures, panels (total) |
| `ELC_WIRE_GAUGE_TXT` | Cable conductor cross-section (e.g. "2.5 mm²", "6 mm²") | Conduits, Electrical Systems |
| `ELC_PNL_PHASE_TXT` | Phase configuration of the panel: "1Ph", "3Ph" | Electrical Equipment (panels) |
| `ELC_PNL_TYPE_TXT` | Panel type: "Switchboard", "DB", "CU", "Data" | Electrical Equipment (panels) |
| `ELC_PNL_LOCATION_TXT` | Physical location description (e.g. "Ground floor electrical cupboard") | Electrical Equipment (panels) |
| `ELC_PNL_VOLTAGE_TXT` | Panel voltage (e.g. "230V", "400V/230V") | Electrical Equipment (panels) |
| `ELC_PNL_RATING_A` | Main breaker/incoming rating in amps (e.g. 100, 200, 400) | Electrical Equipment (panels) |
| `ELC_PNL_LOAD_KW` | Total connected load in kW | Electrical Equipment (panels) |
| `ELC_PNL_FED_FROM_TXT` | Which upstream board feeds this panel | Electrical Equipment (panels) |
| `ELC_PANEL_REF_TXT` | Back-reference to the assigned panel (written on the circuit) | Electrical Systems |
| `ELC_CIRCUIT_GROUP_TXT` | Logical grouping: "kitchen", "emergency-lighting", "fire-alarm", "comms-room" | All fixtures (written by BatchAssignCircuits) |
| `ELC_CDT_BEND_COUNT_NR` | Number of bends in a conduit between pull-in points | Conduits |
| `ELC_CDT_RUN_LENGTH_M` | Length of conduit run in metres | Conduits |
| `ELC_CDT_CABLE_COUNT_NR` | Number of cables inside the conduit | Conduits |
| `ELC_CDT_CBL_FILL_PCT` | Cable fill percentage | Conduits |
| `ELC_CDT_INSTALL_METHOD_TXT` | How the conduit is installed: "SURFACE", "FLUSH", "SOFFIT", "CHASED" | Conduits |
| `ELC_JB_TYPE_TXT` | Junction box type: "PULL_BOX", "DRAW_IN_BOX", "ADAPTABLE_BOX" | Electrical Equipment (junction boxes) |
| `ELC_JB_AUTO_PLACED_BOOL` | 1 if placed automatically by STING | Electrical Equipment (junction boxes) |
| `ELC_JB_REASON_TXT` | Why the junction box was needed: "BENDS_EXCESS" or "RUN_TOO_LONG" | Electrical Equipment (junction boxes) |
| `LTG_EMERG_BOOL` | 1 = emergency luminaire, 0 = normal | Lighting Fixtures |
| `ELC_PHOTO_LUMENS` | Light output in lumens | Lighting Fixtures |
| `ELC_LIGHTING_TARGET_LUX_TXT` | Target illuminance in lux (written by Lighting Grid) | Lighting Fixtures |
| `STING_PENETRATION_REF_TXT` | Fire-rated slab penetration reference | Conduits, Pipes, Ducts |
| `PEN_CONTROL_NUMBER_TXT` | Sequential fire-stop control number (e.g. FRP-0001) | Specialty Equipment (fire stops) |
| `PEN_FIRE_RATING_TXT` | Fire resistance rating of the penetration (e.g. "FR60") | Specialty Equipment (fire stops) |
| `STING_SEED_FAMILY_TXT` | Seed family identifier — written on seed families | All MEP elements placed from seeds |

---

### System codes for electrical (complete list)

| Code | Full name | What goes on this system | Relevant standard |
|------|-----------|------------------------|------------------|
| `LV` | Low voltage | General building power distribution: sockets, switches, small power | BS 7671 |
| `HV` | High voltage | HV switchgear, transformers (11 kV and above) | BS 7671 Part 2 |
| `UPS` | Uninterruptible power supply | Critical circuits fed from UPS: servers, medical equipment, life safety | BS EN 62040 |
| `GEN` | Standby generator | Generator-backed circuits: fire alarm, lifts, critical plant | BS 7671; BS 5839-1 |
| `EMS` | Energy monitoring | Sub-metering, smart metering, BMS power monitoring | — |
| `LTG` | Lighting | All lighting circuits: normal and emergency | BS EN 12464-1 |
| `FLS` | Fire and life safety | Fire alarm, emergency voice alarm, automatic opening vents | BS 5839-1; BS 5839-8 |
| `FP` | Fire protection | Sprinklers, dry risers, wet risers | BS EN 12845; BS 9990 |
| `SEC` | Security | CCTV, access control, intruder alarm | — |
| `ICT` | Information and communications technology | Structured cabling, Wi-Fi, servers | BS EN 50174-2 |
| `COM` | Communications | Intercom, public address, nurse call | — |
| `NCL` | Nurse call | Clinical nurse call, staff attack alarms (healthcare) | HTM 08-03 |

---

### Glossary

**BS 7671:** The IET Wiring Regulations — the British Standard that governs all electrical
installation work in the UK. Panel schedules, circuit protection, cable sizing, and conduit
fill all have requirements in BS 7671.

**Cable fill:** The percentage of a conduit's internal cross-section area that is occupied
by the cables inside. A 20 mm conduit with 35% fill means 35% of its internal area is
cable — the rest is air. BS EN 61386 sets maximum fill percentages to ensure cables can
be drawn through and to prevent overheating.

**Circuit:** A set of cables, a protection device (breaker), and the loads (sockets, lights)
connected to that protection device. A circuit starts at the distribution board and ends
at the last device on the run.

**Distribution board (DB):** A metal enclosure containing circuit breakers. It receives
incoming power from an upstream source (the main switchboard or another distribution
board) and distributes it to individual circuits throughout a zone of the building.

**Draw-in point:** A junction box or pull box along a conduit run where cables can be pulled
in or out. BS 7671 §522.8 limits how far apart draw-in points must be (maximum 6 metres or
3 bends, whichever comes first) to ensure cables can actually be installed.

**Fire stop (FRP — Fire Rated Penetration):** A device that seals the gap around a conduit
or pipe where it passes through a fire-rated floor or wall. The fire stop maintains the
fire resistance of the structural element even though it has been pierced by the service.

**Homerun:** The cable (or conduit containing cables) that runs from a group of devices back
to the distribution board. On a drawing, a homerun arrow shows the point at which individual
device drops are consolidated into a single homerun cable heading back to the panel.

**Lux:** A unit of illuminance — how much light falls on a surface. 100 lux is a brightly
lit corridor. 500 lux is an office at full working light. 1000 lux is a surgery table.

**Lumen method:** A calculation method for lighting design that uses the luminaire's light
output (in lumens) and the room's geometry to predict the average illuminance (in lux).
The method accounts for how efficiently light is distributed (utilisation factor) and for
ageing and dirt (maintenance factor).

**Panel schedule:** A table that records the contents of a distribution board: each circuit
breaker's number, the circuit it protects, the load it carries, and its physical slot
position in the board. Required by BS 7671 to be attached to or provided with the board.

**Phase balance:** The equal distribution of electrical load across the three phases (A, B,
and C) of a three-phase supply. An unbalanced supply runs less efficiently and can cause
problems for sensitive equipment. Good engineering practice aims for less than 10%
imbalance between phases.

**PanelScheduleView:** The Revit object type for a panel schedule. It is a special kind of
view that is linked directly to the electrical systems connected to a specific panel. Unlike
a general schedule, it can only be created for elements in the `Electrical Equipment`
category that have circuits connected to them.

**Seed family:** A generic placeholder family that STING creates automatically. It has the
correct category, hosting type, and shared parameters, but simple placeholder geometry
(a box or a circle). Seed families are used for design and BIM modelling before
manufacturer-specific families are procured. They can be swapped for real manufacturer
families using the **Swap to Manufacturer** command, and all parameter data survives the swap.

**Shared parameter:** A parameter in Revit that has a stable, unique identifier (a GUID)
shared across all projects that use the same shared parameter file. Unlike project
parameters (which only exist in one project), shared parameters can appear in multiple
projects with the same name, GUID, and schedule column alignment. STING uses shared
parameters for all `ELC_*`, `LTG_*`, `FLS_*`, and other engineering data.

**Voltage drop:** The reduction in voltage along a cable run due to the cable's resistance.
BS 7671 Appendix 4 specifies maximum allowable voltage drop: 3% for lighting circuits,
5% for other circuits. Excessively long cable runs or undersized conductors cause more
voltage drop — equipment at the end of the circuit receives less voltage than the supply.

---

## Cross-references

| Guide | Used for |
|-------|---------|
| **MEP_FOUNDATION_GUIDE.md** | Creating MEP symbols (Chapter 1), authoring families correctly (Chapter 2), using the Placement Centre for fixture placement (Chapter 3), wire annotation family creation (Chapter 1, Section 1.4) |
| **DRAWING_PRODUCTION_SYSTEM_GUIDE.md** | Producing electrical floor plans, lighting plans, fire alarm plans, riser diagrams, and panel schedule sheets |
| **DOCUMENT_MANAGER_GUIDE.md** | Issuing electrical drawing packages, transmittal creation, CDE status management, revision control |
| **DRAWING_TYPE_MANAGER_GUIDE.md** | Understanding and customising the drawing type profiles that control electrical view appearance and sheet layout |
| **HEALTHCARE_PACK_DESIGN.md** | Electrical systems in healthcare buildings: HTM 06-01 requirements, EES resilience, UPS zoning, medical gas power supplies |
