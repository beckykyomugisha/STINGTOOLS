# STING Electrical — A Layman's Guide for First-Time Electrical Designers

**Audience.** You've never produced a real electrical design before. You've
opened Revit a few times. Someone has handed you a project, said "do the
electrical", and pointed you at the STING Tools plugin. This guide takes you
from the *first idea* of an electrical design all the way to a finished,
issue-ready package, explaining **what** every step is, **why** it exists,
and **how** STING does the heavy lifting for you.

> **How to read this guide.** Each section has three parts:
> - *What it is* — the engineering concept in plain English.
> - *Why it matters* — what goes wrong if you skip or fudge it.
> - *How to do it in STING* — the actual buttons / commands, in order.
>
> If you only have an hour, read sections 1, 2, 5, 8, 9 and 12.

---

## Table of contents

1. [What "electrical design" actually means](#1-what-electrical-design-actually-means)
2. [Why Revit + STING (and not AutoCAD + a calculator)](#2-why-revit--sting-and-not-autocad--a-calculator)
3. [The five things every electrical design must answer](#3-the-five-things-every-electrical-design-must-answer)
4. [Anatomy of the STING dock panel](#4-anatomy-of-the-sting-dock-panel)
5. [Step-by-step: from blank Revit file to issued drawings](#5-step-by-step-from-blank-revit-file-to-issued-drawings)
6. [Understanding the ISO 19650 tag (and why electrical lives or dies by it)](#6-understanding-the-iso-19650-tag)
7. [Placing things: lights, sockets, switches, panels](#7-placing-things)
8. [Routing things: conduits, cable trays, circuits](#8-routing-things)
9. [Panel schedules — the most important deliverable nobody teaches you](#9-panel-schedules)
10. [Validation — letting STING grade your homework](#10-validation)
11. [Drawings — turning the model into a contract](#11-drawings)
12. [Fabrication, BOQs and handover](#12-fabrication-boqs-and-handover)
13. [Common first-timer mistakes (and how STING catches them)](#13-common-first-timer-mistakes)
14. [Glossary of acronyms](#14-glossary)

---

## 1. What "electrical design" actually means

Electrical design for a building is the process of deciding:

1. **Where electricity comes from** — the incoming utility supply, generators,
   transformers, the main switchboard.
2. **How it's distributed** — the chain of distribution boards (DBs), sub-mains
   and final circuits that carry power from the source to every outlet.
3. **What it powers** — lights, sockets, fans, heaters, motors, lifts, fire-alarm
   panels, data cabinets, security systems, the kettle in the staff kitchen.
4. **How it's protected** — circuit breakers, RCDs, surge protection, earthing,
   bonding, fire-rated cables.
5. **How it complies** — with codes like **BS 7671** (UK Wiring Regulations),
   IEC 60364, NEC (US), or local equivalents, plus client / brand standards.

In practice you produce three categories of output:

| Category    | What's in it                                                              |
|-------------|---------------------------------------------------------------------------|
| Drawings    | Power layouts, lighting plans, fire-alarm layouts, riser diagrams, panel schedules, single-line diagrams (SLDs) |
| Schedules   | Cable schedules, lighting schedules, panel schedules, load schedules     |
| Calculations| Cable sizing (voltage drop + thermal), short-circuit fault levels, lux levels (lighting), earthing |

> **Why this matters for STING.** STING's electrical workflow is built so
> that the *model* is the source of truth and the drawings + schedules + cable
> sizes fall out automatically. You don't draw an SLD. You design the system
> in 3D, tag everything, and STING produces the SLD, the panel schedule and
> the cable list. That's the whole point.

---

## 2. Why Revit + STING (and not AutoCAD + a calculator)

A first-timer often asks: "couldn't I just draw lines in AutoCAD?". Yes — and
you'd produce a drawing that *looks* right but has none of the data behind it.

**Revit** is a *Building Information Modelling* tool. Every wall, light,
socket and conduit is an **object** with parameters (voltage, wattage, IP
rating, manufacturer, level, room). When you move a wall, the lights move
with it. When you change a fixture's wattage, the panel schedule updates.
When you add a circuit, the cable schedule grows by one row.

**STING Tools** is a plugin we wrote on top of Revit because Revit out of the
box is generic — it doesn't know your office's tagging rules, your client's
panel schedule template, or that a fire-alarm sounder needs an `FLS` system
code. STING adds:

- A **registered parameter library** (~2,500 shared parameters) so every
  fixture, panel and cable carries the same data fields no matter who placed
  it.
- An **ISO 19650 tagging engine** that builds a unique 8-segment tag for every
  element (see §6).
- **Auto-placement** for fixtures (e.g. spread 60 lights evenly across a
  ceiling) and **auto-routing** for circuits (drop a conduit from each light
  back to the panel).
- **Panel schedule automation** that creates one panel schedule per DB, fills
  in the circuits, and exports to Excel and back.
- **Validation engines** that tell you "circuit C12 is over 80 % full" or
  "this fire-alarm sounder isn't connected to anything".
- **Drawing automation** that produces lighting plans, power plans, riser
  diagrams and fabrication packages on the right paper, with the right title
  block, at the right scale, with the right view template.

You are still the engineer. STING is the apprentice that does the typing.

---

## 3. The five things every electrical design must answer

Before you click anything, know what you're trying to produce. A complete
electrical design answers five questions:

1. **What's the load?** Sum of every light, socket, motor and HVAC unit.
   Expressed in kVA (apparent power) and kW (real power). Drives the size of
   the incoming supply and the main switchgear.
2. **How is it split?** A tree of distribution boards. Typically a Main LV
   Panel feeds Sub-Distribution Boards (SDBs), which feed Final Distribution
   Boards (FDBs), which feed final circuits. STING calls each branch a
   **system** and tags it with a system code (`POW`, `LTG`, `FLS`, `ICT`,
   `SEC`, `LV`).
3. **What protects each circuit?** Every final circuit needs an MCB (Miniature
   Circuit Breaker), often combined with an RCD (Residual Current Device).
   The breaker rating must be larger than the load current but smaller than
   the cable's safe carrying capacity.
4. **What size cable?** Sized so that (a) it doesn't overheat under load, (b)
   the voltage at the far end is still high enough (typically ≤ 4 % drop end
   to end), and (c) it survives a short-circuit fault long enough for the
   breaker to trip.
5. **How does it earth and bond?** Every metal enclosure must be tied back to
   earth so a fault current finds an easy path home and trips the breaker
   before anyone gets hurt.

STING tracks each of these via parameters. If you populate the parameters,
the schedules, calculations and drawings work. If you don't, nothing works.
That's why §6 (tagging) is the single most important section of this guide.

---

## 4. Anatomy of the STING dock panel

When you load STING into Revit, a panel appears on the right of the screen
with **9 tabs**. For electrical work you'll mostly use four of them:

| Tab       | What's in it                                                          | When to use                                  |
|-----------|------------------------------------------------------------------------|----------------------------------------------|
| **CREATE** | Tag, populate, combine, validate, smart-place — the tagging workshop | Every time you place new equipment           |
| **MODEL**  | Auto-modelling: walls, rooms, **fixtures, ducts, pipes, MEP**         | Whenever you place electrical equipment      |
| **TAGS**   | "Tag Studio" — `Fixtures`, `Routing`, `Fabrication` sub-tabs (v4 MVP) | Lighting grids, auto-drops, fab packages     |
| **DOCS**   | Sheets, views, panel schedules, drawing register                      | When you start producing drawings            |

There's also a **BIM** tab (issues, documents, transmittals — mostly for the
BIM coordinator, not you) and a **TEMP** tab (project setup — you'll use it
once at the start).

> **Tip.** Every button on the panel is also accessible by typing its command
> tag. The handler `UI/StingCommandHandler.cs` maps ~590 button tags to the
> classes that do the work — see `CLAUDE.md` if you need a tag.

---

## 5. Step-by-step: from blank Revit file to issued drawings

Here is the canonical electrical workflow. Do these in order.

### 5.1 Open or create the Revit project

If your team uses a federated model, the architect will give you a **central
file** to attach to and an **architectural link** to host your work against.
If you're starting from scratch, open Revit's *Electrical Template* and save
a new file.

### 5.2 Run **Project Setup Wizard** (TEMP → Setup → ★ Project Setup Wizard)

**Why.** Revit out of the box doesn't know what an `ELC_PNL_TAG` is. The
wizard binds STING's ~2,500 shared parameters to the correct categories,
creates the standard view templates, filters, worksets and phases, and
seeds the project with `PRJ_ORG_*` parameters (project code, originator,
client, suitability) so every drawing's title block fills itself in.

**How.** Click `★ Project Setup Wizard`, follow the 7 pages: project info,
disciplines (tick **Electrical**, **Lighting**, **Fire Alarm**, **Comms**,
**LV** as appropriate), location codes, level codes, output paths, review.
If unsure, accept defaults. The wizard runs `MasterSetupCommand` under the
hood (15 sub-steps).

If a colleague has already set the project up, you can skip this — but run
**TEMP → Setup → Check Data Files** first to confirm the parameter pack is
loaded.

### 5.3 Link the architectural model

In Revit: `Insert → Link Revit → architecture.rvt`. Pin the link
(`Modify → Pin`) so you can't move it by accident. Copy / Monitor the
levels and grids so your electrical levels stay in sync.

**Why.** Every fixture you place needs a host (a wall, a ceiling) and every
circuit needs a level. If the architect moves a wall, your sockets follow.

### 5.4 Place rooms (or use linked rooms)

Rooms drive `LOC` and `ZONE` auto-detection. STING reads room name /
department to derive these tokens. If your project uses linked rooms (rooms
in the architectural model), STING's `SpatialAutoDetect` reads them through
the link automatically.

### 5.5 Place the **distribution boards** first

Every circuit ends at a DB. Place the boards before anything else so when
you place fixtures, you know where they're going.

- **MODEL** tab → **MEP** category → **Place Fixture** (`ModelPlaceFixture`).
- Pick the panel family (`STING - LV Panel`, `STING - DB 24-way`, etc.).
- Click on the wall in the electrical room.
- Repeat for every SDB and FDB.

### 5.6 Place lighting fixtures

Two ways:

| Method                | When to use                                          | Command                          |
|-----------------------|------------------------------------------------------|----------------------------------|
| Manual (one by one)   | Bespoke layouts, atria, signature lights             | MODEL → Place Fixture            |
| **Lighting Grid**     | Office, retail, classrooms — anywhere with a regular grid | TAGS → Fixtures → `Placement_LightingGrid` |
| **Place Fixtures**    | Rule-based (e.g. "one downlight per 4 m² of room") | TAGS → Fixtures → `Placement_PlaceFixtures` |

**How Lighting Grid works.** You pick a room. STING reads the ceiling area,
the target lux from the room's *function* parameter (office = 500 lx,
corridor = 100 lx etc.), and the chosen fixture's lumen output. It computes
how many fixtures, lays them out on a regular grid centered on the ceiling,
and tags them. You can override spacing afterwards.

### 5.7 Place sockets, switches and other power outlets

Same pattern: MODEL → MEP → Place Fixture, pick the family, click the wall.
Sockets are *wall-hosted*, switches are *wall-hosted*, ceiling fans are
*ceiling-hosted*. Revit will refuse hosts that don't match — that's a
feature, not a bug.

> **Tip.** Use **CREATE → Auto Tag** *before* placing the next batch.
> Half-tagged projects get worse, never better.

### 5.8 Tag everything

This is the moment STING earns its keep. **CREATE → Tag & Combine**
(`TagAndCombine`) does five things in one click:

1. Auto-detects `LOC` (location/building) and `ZONE` from the room.
2. Populates `DISC` (= `E` for electrical), `LVL` (level), `SYS` (`POW`,
   `LTG`, `FLS` etc. from the family / MEP system), `FUNC`, `PROD` (e.g.
   `LFL` for linear fluorescent, `DB` for distribution board).
3. Assigns the next available `SEQ` number (1234 → 1235).
4. Builds the 8-segment tag `E-BLD1-Z01-L02-LTG-LTG-LFL-0042`.
5. Writes that tag to all 36 container parameters (`ELC_EQP_TAG`,
   `LTG_FIX_TAG`, `ASS_TAG_1` … `ASS_TAG_7`).

You now have an unambiguous identifier for every fixture and panel. The
panel schedule, the cable schedule, the drawings and the BOQ all key off
this tag.

### 5.9 Connect fixtures to circuits

In Revit, an *electrical system* is a set of fixtures connected to a single
breaker on a single panel. You create one by:

1. Select the fixtures you want to put on a circuit.
2. `Modify → Power` (or `Systems → Power` depending on your Revit version).
3. Pick the panel and the circuit number.

Revit drafts a wire (the magenta line). You can later swap it for an actual
conduit + cable model.

> **Why this matters.** Once a fixture is on a circuit, Revit knows its
> power rating, the panel knows its load, the panel schedule populates
> automatically, and the cable run can be auto-dropped (§8).

### 5.10 Auto-drop conduits and cable trays

Skip ahead to §8 — **TAGS → Routing → Auto Drop**.

### 5.11 Run validators

**TAGS → Routing → Validate Fills**, **Run All Validators**. STING flags:

- Circuits over 80 % loaded (oversize the breaker or split the circuit).
- Fixtures with no host (orphans).
- Panels with no upstream feed (you forgot to circuit them).
- Cable trays over fill capacity (BS 7671 Appendix 4).
- Fire-alarm sounders not on a Cat A circuit.

Fix the red issues. Amber are warnings — they're optional but usually worth
fixing.

### 5.12 Generate panel schedules

**DOCS → Panel → Batch Schedules** (`Panel_BatchSchedules`). One panel
schedule view per DB, automatically. See §9.

### 5.13 Produce drawings

**DOCS → Sheet Manager** + **Drawing Type Manager** (§11). The drawing
type for an electrical lighting plan is `elec-lighting-A1-1to100` — STING
picks the right scale, view template, title block and slot layout.

### 5.14 Issue

**BIM → Issue Deliverable** (template engine v1.1). A cover letter,
transmittal note and revision history are rendered automatically and
e-mailed to the client.

---

## 6. Understanding the ISO 19650 tag

The single most important concept in STING is the 8-segment tag. Memorise it.

```
DISC - LOC  - ZONE - LVL - SYS  - FUNC - PROD - SEQ
  E  - BLD1 - Z01  - L02 - LTG  - LTG  - LFL  - 0042
```

| Segment | Meaning                | Electrical examples                                          |
|---------|------------------------|--------------------------------------------------------------|
| DISC    | Discipline             | `E` (electrical), `LV` (extra-low voltage), `FP` (fire)      |
| LOC     | Location / building    | `BLD1`, `BLD2`, `EXT` (external)                             |
| ZONE    | Zone                   | `Z01`–`Z04`, `XX` (zoneless)                                 |
| LVL     | Level                  | `L01`, `GF`, `B1`, `RF`                                      |
| SYS     | System (CIBSE/Uniclass)| `POW` (power), `LTG` (lighting), `FLS` (fire alarm), `ICT`, `SEC`, `LV` |
| FUNC    | Function               | `PWR`, `LTG`, `EML` (emergency lighting), `EXT` (external)   |
| PROD    | Product / device class | `DB` (distribution board), `MCB`, `LFL` (linear fluor), `LED`, `SOK` (socket), `SW` (switch), `EMG` (emergency luminaire), `SOU` (sounder), `DET` (detector) |
| SEQ     | 4-digit sequence       | `0001`, `0042`, `1234`                                       |

### Why eight segments?

Because every drawing you produce — every BOQ row, every panel schedule
line, every cable schedule entry — needs an unambiguous link back to one
piece of equipment in the model. With 8 segments you can have hundreds of
sockets in one zone and still have unique IDs.

### How STING fills in each segment

| Segment | How it's derived                                                   | What you can override                       |
|---------|--------------------------------------------------------------------|---------------------------------------------|
| DISC    | Category map (lighting fixtures → `E`, fire alarm → `FP`)          | `Tokens → Set Discipline`                   |
| LOC     | Room name / Project Information                                    | `Tokens → Set Location`                     |
| ZONE    | Room department / name pattern                                     | `Tokens → Set Zone`                         |
| LVL     | Element's host level                                               | (don't override — change the level)         |
| SYS     | MEP system the element is on, falling back to category             | `Tokens → Set System`                       |
| FUNC    | System → function map                                              | (rarely overridden)                         |
| PROD    | Family-name-aware lookup (35+ specific codes for electrical)       | (rarely overridden)                         |
| SEQ     | Next available number per (DISC, SYS, LVL) group                   | `Tokens → Assign Numbers`                   |

> **Practical advice for first-timers.** Don't fight the autotagger. Place
> equipment, click **Tag & Combine**, and only override tokens when STING
> guesses wrong (e.g. a custom luminaire family that STING doesn't recognise
> as `LFL`). To override a few elements, select them, **CREATE → Tokens →
> Set ___**.

### What the containers are for

Every tag value is also written to **discipline-specific** parameters so a
panel schedule that wants `ELC_PNL_TAG` and a luminaire schedule that wants
`LTG_FIX_TAG` both get the same string. Look at the family's *Type
Properties* and you'll see the same tag repeated in several boxes — that's
intentional.

---

## 7. Placing things

### 7.1 Distribution boards (panels)

A panel needs three things to be useful:

1. **A location** — the wall in the electrical / riser room.
2. **An upstream feed** — which panel feeds it. Set in the panel's *Distribution
   System* and *Panel Name* parameters when you connect it.
3. **A schedule** — created automatically by **Panel_BatchSchedules** once
   you've placed circuits.

Use **MODEL → MEP → Place Fixture** with a panel family (`STING - LV
Switchboard`, `STING - DB 24-way`, etc.). After placing, set:

- `Panel Name` (e.g. `LV-MAIN`, `DB-L02-A`).
- `Voltage` (typically 415 V phase-phase, 230 V phase-neutral).
- `Number of Phases` (1 or 3).
- `Distribution System` (the Revit type that defines voltage / phases / number
  of poles).

### 7.2 Lighting fixtures — manual

For one-off luminaires:

1. **MODEL → Place Fixture** → pick a lighting fixture family.
2. Click on the ceiling.
3. Tag with **CREATE → Auto Tag**.

### 7.3 Lighting fixtures — automatic grid

For repeatable rooms:

1. **TAGS → Fixtures → Lighting Grid** (`Placement_LightingGrid`).
2. Pick rooms (or pick a level — STING grids every room on it).
3. Pick a fixture family + lumen output.
4. Choose:
   - **Target lux** — the average illuminance you need on the working plane.
     STING reads room *function* (office, corridor, lab) and suggests a
     value from CIBSE LG7.
   - **Maintenance factor** — usually 0.8 (allows for dirt, lamp ageing).
   - **Utilisation factor** — depends on room shape and reflectance; STING
     defaults to 0.5 for typical rooms.
5. STING computes: `N = (E × A) / (Φ × MF × UF)`
   where `N` = number of fixtures, `E` = target lux, `A` = room area,
   `Φ` = lumens per fixture, `MF` × `UF` = maintenance × utilisation.
6. STING lays them on a regular grid, tags them, and reports back: "Room
   2.07 — 4×3 = 12 luminaires, 510 lx average".

### 7.4 Sockets, switches and accessories

Wall-hosted, placed at standard mounting heights (450 mm AFFL for sockets in
UK domestic, 1200 mm for switches — defaults stored in family types). Use
the same **MODEL → Place Fixture** pattern.

### 7.5 Smart-place (the rule-based engine)

`Placement_PlaceFixtures` reads `STING_PLACEMENT_RULES.json` (43 rules
shipped) and applies them automatically. Examples:

- Smoke detector in every room > 7 m² (BS 5839).
- Fire-alarm sounder in every corridor (audibility ≥ 65 dB(A)).
- Twin socket every 3 m of perimeter wall in offices (BCO Guide).
- Emergency luminaire on every escape route, max 12 m apart.

You can edit the rules JSON or write project overrides at
`<project>/_BIM_COORD/placement_rules.json`.

---

## 8. Routing things

A finished electrical model has *physical* cable routes — conduits in walls,
cable trays in ceilings, busbars in risers. Until v3 STING you drew them by
hand. From v4 onwards there's an **auto-drop** engine.

### 8.1 Conduit drops to fixtures

**TAGS → Routing → Auto Drop** (`Routing_AutoDrop`).

For each circuit, STING:

1. Finds every fixture on the circuit.
2. Plots the shortest route from each fixture, **up to the ceiling void**,
   along a horizontal main, to the panel.
3. Drops the appropriate conduit / cable tray family.
4. Sets the conduit's `CPC_SZ_MM` (circuit protective conductor size) and
   fill capacity per BS 7671 Table 4D5.

### 8.2 Cable tray sizing

Once auto-dropped, **Routing → Validate Fills** checks each tray against
40 % fill (Table 4D4). Over-full trays are flagged red. The fix is either to
upsize the tray or split the cable group.

### 8.3 Containment hierarchy

A typical office has:

- **Cable basket** — high-level support, hidden above the ceiling. Carries
  power and data cables to local distribution.
- **Conduit / trunking** — drops from the basket into the wall to feed
  individual sockets / switches.
- **Modular wiring** — pluggable connectors for lighting (faster install,
  factory-tested).

STING models all three when the auto-drop sees the right family present.

### 8.4 What "auto-drop" can't do

- **3D clash with structure.** Auto-drop is greedy — it'll happily try to
  route through a beam. Run a clash detection (BIM → Coordination → Clash
  Detection) afterwards.
- **Fire compartment penalties.** Cables crossing a fire compartment need
  fire stopping. Auto-drop ignores compartment lines — flag them manually.
- **Aesthetic concerns.** Long horizontal runs in exposed-soffit ceilings
  may need to be moved for visual reasons.

---

## 9. Panel schedules

A **panel schedule** is the document an electrician on site uses to wire up
a distribution board. One row per circuit, with: circuit number, breaker
rating, cable size, load (watts), description, room served. The panel
schedule is the *most reused document* in the entire electrical package.

### 9.1 Why STING has a dedicated subsystem

Revit's built-in panel schedules are powerful but fiddly: you have to pick a
template per panel, manually align columns, and the templates don't survive
across projects. STING's `BatchPanelSchedulesCommand` automates this.

### 9.2 Workflow

1. Place panels (§7.1) and circuit fixtures to them (§5.9).
2. **DOCS → Panel → Batch Schedules** (`Panel_BatchSchedules`).
3. STING reads `STING_PANEL_SCHEDULE_TEMPLATES.json` (5 priority-ordered
   rules) and picks the right template per panel:
   - `STING - Switchboard Schedule` (for the main LV panel).
   - `STING - 3-phase DB` (for sub-DBs).
   - `STING - 1-phase Consumer Unit` (small domestic-style boards).
   - `STING - Data Panel` (comms cabinets).
   - Catch-all fallback if none match.
4. Each schedule is stamped with drawing type
   `elec-panel-schedule-A3` so it lands on the right sheet at the right
   scale.
5. Open the schedule to verify — circuit rows, loads, totals.

### 9.3 Editing in Excel and round-tripping back

If a senior engineer wants to mark up cable sizes in Excel:

1. **DOCS → Panel → Export to Excel** (`Panel_ExportToExcel`) — writes one
   `.xlsx` per panel + an `INDEX` worksheet.
2. Edit cells in Excel.
3. **Panel → Import from Excel** (`Panel_ImportFromExcel`) — round-trips
   changes back. Note: Revit's read-only computed cells (totals, calculated
   loads) are protected; they're reported as `cellsRejected` and you fix the
   source data instead.

### 9.4 Spares, spaces and clean-up

- **Fill Spares** — fill all empty slots with 'spare' breakers.
- **Fill Spaces** — fill empty slots with 'space' (no breaker, room for
  future).
- **Convert Spaces to Spares** — useful at sign-off.
- **Clear Spares & Spaces** — reset before re-issuing.

### 9.5 Audit

`Panel_Audit` lists panels without schedules, panels with template drift
(template was edited after schedule was created), and panels with missing
`ELC_PNL_*` tags. Run it weekly.

---

## 10. Validation

STING ships **five** electrical validators (TAGS → Routing → Run All).

| Validator       | What it checks                                                                | Why                                            |
|-----------------|-------------------------------------------------------------------------------|------------------------------------------------|
| Connectivity    | Every fixture is on a circuit, every panel has an upstream feed               | Orphans cause silent dead loads                |
| Fill            | Cable trays / conduits are ≤ 40 % full                                        | BS 7671 thermal safety                         |
| Spec            | Cable size matches breaker rating; protective device `Iₙ` ≤ cable `Iz`        | Stops cables overheating before breaker trips  |
| Termination     | Both ends of every circuit have valid terminations                            | Catches mislabelled cables                     |
| Slope           | (drainage) — irrelevant for electrical                                        | Reused for cable tray slope where required     |

The validators write a `ValidationResult` record per element. The dialog
shows red (blocking), amber (warning) and green (informational) entries with
"Show in view" links so you can jump to the offending fixture.

> **First-timer rule.** A clean validation run is your minimum bar before
> issuing. If the fill validator says "21 % over capacity", the design is
> *wrong*, not fixable in Excel. Go upsize the tray.

---

## 11. Drawings

A drawing is the legal contract. STING produces them via the **Drawing
Template Manager** (`Core/Drawing/`).

### 11.1 What a "drawing type" is

A *drawing type* is a JSON profile that bundles every presentation decision
for one drawing: paper size, title block, scale, view template, viewport
type, slot layout, scope-box crop, section-marker family, sheet number
pattern. Examples relevant to electrical:

| ID                              | Purpose                                                                |
|---------------------------------|------------------------------------------------------------------------|
| `elec-power-A1-1to100`          | Power layout — sockets, fixed equipment, busbars                       |
| `elec-lighting-A1-1to100`       | Lighting layout — luminaires, switches, emergency lighting             |
| `elec-fire-alarm-A1-1to100`     | Fire-alarm layout — detectors, sounders, call points, panels           |
| `elec-riser-A2-1to100`          | Riser diagram — vertical column showing each floor's panels            |
| `elec-panel-schedule-A3`        | Panel schedule (one per DB)                                            |

### 11.2 How to make a sheet

Two options, depending on how organised your views already are:

**A) From scope boxes** (the new way, recommended).

1. The architect provides scope boxes (named `STING::elec-lighting-A1-1to100::L02::west`).
2. **DOCS → Drawing Types → From Scope Boxes** (`DrawingTypes_FromScopeBoxes`).
3. STING creates the views, applies the profile (scale, view template, crop,
   filters), creates the sheet, places the viewport, stamps the title block.

**B) From templates**.

1. **DOCS → Sheet Manager → Create From Template** (`CreateFromTemplate`).
2. Pick a profile from the dropdown (electrical profiles appear next to
   built-in templates).
3. STING creates the sheet, places the duplicated views, applies the profile.

### 11.3 Title-block parameters

Profiles can declare *title-block parameter bindings* (e.g. `Sheet Number =
"E-{lvl}-{seq:D3}"`, `Suitability = "${PRJ_ORG_SUITABILITY}"`). When STING
mints the sheet, the title block fills itself in. No more manually typing
sheet numbers.

### 11.4 Drawing register

**DOCS → Doc Automation → Drawing Register** lists every sheet, its drawing
type, revision, suitability and date. Issue this with every drop.

### 11.5 SyncStyles — keeping the set tidy

Once you have 200 sheets, someone will accidentally change a view's scale.
**DrawingTypes_SyncStyles** scans every stamped view, detects drift
(`SCALE`, `DETAIL`, `TEMPLATE`, `MANAGED_TEMPLATE`, `TOKEN_PROFILE`), and
restores the profile values. Run it before every issue.

---

## 12. Fabrication, BOQs and handover

### 12.1 Fabrication packages

Site contractors increasingly want pre-made **assemblies** — e.g. a
factory-wired modular containment run with pre-fitted sockets. STING's
`GenerateFabPackageCommand` (TAGS → Fabrication tab) groups elements into
assemblies, creates assembly views, builds shop drawings, places ISO 6412
symbols, and exports a cut list and weld map. Use it once design is frozen.

### 12.2 Bill of Quantities (BOQ)

**TEMP → Data Pipeline → BOQ Export** writes one row per material per
location: cable type, length (read from conduit segments), socket count,
luminaire count, breaker count. Costs come from `cost_rates_5d.csv`. This
is your QS handover.

### 12.3 COBie / handover

For the FM team (the people who will maintain the building), produce a COBie
spreadsheet: **BIM → COBie Export Wizard**. Pick the *Office* preset (or
relevant), STING fills 19 worksheets with every electrical asset, its
maintenance schedule (SFG20), spare parts and warranty info. This often
lives in the building owner's CAFM software for the next 30 years.

---

## 13. Common first-timer mistakes

| Mistake                                                                | What goes wrong                                              | How STING catches it                                    |
|------------------------------------------------------------------------|--------------------------------------------------------------|---------------------------------------------------------|
| Placing fixtures before tagging the project                            | Tags don't auto-fill `LOC` / `ZONE` because there are no rooms | `Pre-Tag Audit` (CREATE → Pre-Tag Audit) flags it        |
| Forgetting to circuit fixtures                                         | They don't appear on any panel schedule, panel loads are wrong | Connectivity validator                                   |
| Using one DB for the whole project                                     | Voltage drop too high, no diversity, breaker crowding        | Spec validator + cable-fill validator                    |
| Not setting `Distribution System` on the panel                         | Revit refuses to circuit fixtures to it                       | Panel audit                                              |
| Drawing wires instead of conduit                                       | No physical containment in the model → BOQ undercount        | The auto-drop won't run if no conduit exists             |
| Editing tags by hand                                                   | Duplicate tags, broken sequence numbers                       | `Find Duplicate Tags`, `Repair Duplicate SEQ`             |
| Issuing before running validators                                      | Embarrassing markups from the reviewer                        | Workflow preset *DailyQA_Enhanced* runs the validators   |
| Hand-creating panel schedules                                          | Drift between schedule and circuit reality                    | `Panel_Audit` reports template drift                     |
| Mixing systems on one circuit (e.g. fire alarm + power)                | BS 7671 violation, fire-alarm dropouts                        | Spec validator                                           |
| Forgetting emergency lighting                                          | Building Control fail                                          | `Placement_PlaceFixtures` rule pack adds them automatically |
| Not setting room functions                                             | Lighting Grid uses default 300 lx everywhere                  | `Spatial Validation` audit                               |

---

## 14. Glossary

| Acronym | Meaning                                                                  |
|---------|--------------------------------------------------------------------------|
| AFFL    | Above Finished Floor Level                                               |
| BCO     | British Council for Offices                                              |
| BIM     | Building Information Modelling                                           |
| BMS     | Building Management System                                               |
| BOQ     | Bill of Quantities                                                       |
| BS 7671 | UK Wiring Regulations (the Bible of UK electrical design)                |
| CDE     | Common Data Environment (the document store)                             |
| CIBSE   | Chartered Institution of Building Services Engineers                     |
| COBie   | Construction Operations Building information exchange (FM data format)   |
| CPC     | Circuit Protective Conductor (the earth wire)                            |
| DB      | Distribution Board (a small panel)                                       |
| FDB     | Final Distribution Board                                                 |
| FCM     | Firebase Cloud Messaging                                                 |
| FLS     | Fire and Life Safety                                                     |
| HVAC    | Heating, Ventilation, Air Conditioning                                   |
| ICT     | Information & Communications Technology                                  |
| IP rating| Ingress Protection (e.g. IP44 = splash-proof)                           |
| ISO 19650| The international BIM standard for delivering construction info        |
| LG7     | CIBSE Lighting Guide 7 (offices)                                         |
| LV      | Low Voltage (sub-1000 V) — but in BIM context often "Extra-Low Voltage" / data |
| MCB     | Miniature Circuit Breaker                                                |
| MIDP    | Master Information Delivery Plan                                         |
| MEP     | Mechanical, Electrical, Plumbing                                         |
| RCD     | Residual Current Device (trips on earth fault)                           |
| RFI     | Request For Information                                                  |
| SDB     | Sub-Distribution Board                                                   |
| SLD     | Single-Line Diagram (the schematic of the whole electrical system)       |
| SOK     | Socket outlet                                                            |
| Uniclass| UK construction classification system                                    |

---

## Recommended first-week practice

If you have one Revit model and one week to learn STING electrical, do this:

| Day | Task                                                                  |
|-----|-----------------------------------------------------------------------|
| 1   | Run Project Setup Wizard. Link an architect's model. Place levels.     |
| 2   | Place 2 distribution boards. Place 30 luminaires using Lighting Grid.  |
| 3   | Place 30 sockets manually. Tag everything (CREATE → Tag & Combine).    |
| 4   | Circuit the lights and sockets to the panels. Run validators.          |
| 5   | Generate panel schedules. Round-trip one to Excel and back.            |
| 6   | Auto-drop conduits. Re-validate. Generate a lighting plan + power plan.|
| 7   | Issue the package via the Template Engine. Check the rendered cover.   |

Once you've done that round trip, you will know more about practical
electrical BIM than 80 % of grads. Welcome to the discipline.

---

*Document version 1.0 — May 2026. Maintained alongside `CLAUDE.md`. For
deeper dives see `docs/MEP_SYMBOL_GUIDE.md`, `docs/AEC_FILTER_LIBRARY.md`
and the CIBSE / BS 7671 references.*
