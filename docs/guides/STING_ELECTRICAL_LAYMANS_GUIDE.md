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
12a. [STING Electrical Panel — sub-system map](#12a-sting-electrical-panel--sub-system-map)
12b. [Cross-discipline integration (HVAC + plumbing)](#12b-cross-discipline-integration--what-the-other-panels-give-you)
12c. [Recent commands worth knowing about](#12c-recent-commands-worth-knowing-about-post-v10-additions)
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

When you load STING into Revit, **three dockable panels** become available:
the main 9-tab panel, the dedicated **STING Electrical Panel**, and (when
you need cross-discipline coordination) the **STING HVAC Panel** and
**STING Plumbing Panel**.

For electrical work you'll mostly use four tabs on the main panel:

| Tab       | What's in it                                                          | When to use                                  |
|-----------|------------------------------------------------------------------------|----------------------------------------------|
| **CREATE** | Tag, populate, combine, validate, smart-place — the tagging workshop | Every time you place new equipment           |
| **MODEL**  | Auto-modelling: walls, rooms, **fixtures, ducts, pipes, MEP**         | Whenever you place electrical equipment      |
| **TAGS**   | "Tag Studio" — `Fixtures`, `Routing`, `Fabrication` sub-tabs (v4 MVP) | Lighting grids, auto-drops, fab packages     |
| **DOCS**   | Sheets, views, panel schedules, drawing register                      | When you start producing drawings            |

There's also a **BIM** tab (issues, documents, transmittals — mostly for the
BIM coordinator, not you) and a **TEMP** tab (project setup — you'll use it
once at the start).

The **STING Electrical Panel** is a second dockable panel dedicated to
MEP electrical engineering. Toggle it from the ribbon's "Electrical"
button. It hosts the dedicated calculation kernels (cable sizer, voltage
drop, fault current, arc flash, busbar, photometrics, SLD, lightning
protection) that would otherwise crowd the main panel. See §15 for the
sub-system map.

If your project also has HVAC and plumbing teams, open the **STING HVAC
Panel** and **STING Plumbing Panel** for visibility on cross-discipline
loads, ventilation OA quantities, and DHW recirculation pumps that show
up on your electrical schedules. Detailed user guides for those panels
live alongside this one at `docs/guides/STING_HVAC_LAYMANS_GUIDE.md` and
`docs/guides/STING_PLUMBING_LAYMANS_GUIDE.md`.

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

### 5.4a Define MEP **Distribution Systems** *before* placing any panel

Revit's electrical engine uses **Distribution Systems** to define
voltage / phases / poles. **You cannot circuit anything until at least one
Distribution System exists.** New starters trip on this constantly: they
place a panel, try to connect a light, and Revit silently refuses.

Open `Manage → MEP Settings → Electrical Settings → Distribution Systems`
and create one per voltage/phase combination on the project:

| Phase config        | L-N (V) | L-L (V) | Use for                                    |
|---------------------|---------|---------|--------------------------------------------|
| 1-phase 2-wire      | 230     | —       | Domestic supply, small consumer units      |
| 1-phase 3-wire      | 230     | —       | Split-phase neutrals (rare in UK)          |
| 3-phase 4-wire WYE  | 230     | 415     | **UK commercial standard**                 |
| 3-phase 5-wire WYE  | 230     | 415     | UK commercial with separate earth          |
| 3-phase 4-wire WYE  | 277     | 480     | US commercial                              |
| 3-phase 3-wire DELTA| —       | 415     | Industrial motor loads (no neutral)        |

Then on each panel set the `Distribution System` parameter to the matching
type. Without this, the panel's circuit list stays empty no matter how many
fixtures you wire.

### 5.4b Voltage-drop budget and demand-factor assumptions

Two project-wide settings determine whether your cables and panels will be
under- or over-sized. Set them before placing equipment so STING's auto-drop
sizes correctly first time.

**Voltage-drop budget** (BS 7671 Appendix 4 — write into Project Information):

| Circuit type     | Max Vd from origin | Why                                      |
|------------------|--------------------|------------------------------------------|
| Lighting         | 3 %                | Lower drop = stable colour temperature   |
| Power / general  | 5 %                | Tolerated swing on appliances            |
| Total end-to-end | 8 %                | Absolute ceiling per BS 7671             |

**Demand (diversity) factors** — set per panel via the `Demand Factor`
parameter. Typical UK office:

| Load type        | Factor | Why                                            |
|------------------|--------|------------------------------------------------|
| Office lighting  | 1.0    | All on during occupancy                        |
| Office power     | 0.6    | Sockets rarely all loaded simultaneously       |
| Server room      | 1.0    | 24/7 base load                                 |
| Comfort cooling  | 0.9    | Steady once at setpoint                        |
| Lifts            | 0.5    | Start currents spread across phases            |
| Catering kitchen | 0.6–0.7| BS 7671 Appendix 16                            |

The panel schedule's *Total Estimated Demand* equals
`Σ(connected load × demand factor)` and drives upstream cable sizing.

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

### 7.6 Lighting Grid — the math that drives the layout

The **Lumen Method** is the textbook calc for interior lighting. STING's
Lighting Grid command applies it automatically per room:

```
N = (E × A) / (Φ × MF × UF)
```

| Symbol | Meaning                              | Where it comes from                        |
|--------|--------------------------------------|--------------------------------------------|
| N      | Number of luminaires                 | The output (rounded up to grid)            |
| E      | Required lux on the working plane    | Room *function* → CIBSE LG7 lookup         |
| A      | Room area (m²)                       | Room boundary                              |
| Φ      | Lumens per luminaire                 | Family parameter `LMP_LUMENS`              |
| MF     | Maintenance Factor (0.7–0.85)        | Project setting (`MAINTENANCE_FACTOR`)     |
| UF     | Utilisation Factor                   | Manufacturer table on K + reflectances     |

**Room Cavity Ratio** drives UF:

```
K = (L × W) / [Hm × (L + W)]
```

where `L`, `W` are room length & width and `Hm` is mounting height *above
the working plane* (working plane = 0.85 m AFFL for desks, 0 m for
warehouses, 1.0 m for benches).

STING reads UF tables from `LIGHTING_LUMINAIRE_DATA.json` or from the
luminaire family's `UF_TABLE` parameter. If neither exists it falls back to
0.5 — a safe, slightly pessimistic default.

**CIBSE LG7 target lux** (excerpt — STING auto-fills from room *function*):

| Room function   | E (lux) | Notes                                  |
|-----------------|---------|----------------------------------------|
| Open office     | 500     | Task plane                             |
| Cellular office | 500     | Task plane                             |
| Meeting room    | 500     | Plus dimming for AV                    |
| Corridor        | 100     | 150 in care/medical                    |
| Stair           | 150     | 200 at landings                        |
| WC              | 200     | Wall-mounted vanity preferred          |
| Plant room      | 200     | 300 at controls                        |
| Reception       | 300     | Plus accent feature lighting           |
| Retail floor    | 750     | Plus 1500 lx accent                    |
| Operating theatre| 1000   | + 100 000 lx focal task lighting       |
| Warehouse aisle | 200     | Vertical plane                         |
| Classroom       | 300–500 | 500 on board                           |

### 7.7 Lighting Grid settings that change the layout

Eight knobs in the dialog — tune them in this order for predictable results:

| Setting                    | Default               | Effect when changed                                    |
|----------------------------|-----------------------|--------------------------------------------------------|
| Target lux (E)             | from room function    | Doubles → twice the fixtures                           |
| Working plane height       | 0.85 m (BS EN 12464)  | 0 m for warehouses, 1.0 m for benches                  |
| Mounting height (Hm)       | ceiling – WP          | Lower → more fixtures (worse UF)                       |
| MF (maintenance)           | 0.8                   | 0.85 in clean offices, 0.7 in dusty/factory            |
| UF override                | auto from K + reflectances | Override only if family lacks tables               |
| Spacing-to-Mounting Height ratio (SHR) | 1.5:1     | 1:1 = uniform but 50 % more fixtures; 2:1 = patchy     |
| Pattern                    | Grid                  | Staggered for entrances, Perimeter for retail          |
| Edge offset                | half-spacing          | Set to wall thickness for narrow corridors             |

> **Rule of thumb for "perfect"**: target uniformity Uo ≥ 0.6 (min/avg lux)
> and UGR ≤ 19 for offices, ≤ 25 for warehouses. Tighten SHR to 1:1 if Uo
> falls short. Re-run after each change.

### 7.8 Smart-place rule JSON — anatomy of one rule

`STING_PLACEMENT_RULES.json` is the single source of truth for rule-driven
placement. Each rule looks like this:

```json
{
  "id": "office-twin-socket-perimeter",
  "discipline": "E",
  "category": "ElectricalFixtures",
  "family": "Twin Socket - Switched",
  "trigger": {
    "spaceFunction": ["Office", "Open Office", "Meeting"],
    "minArea_m2": 6,
    "phase": ["NEW", "EXISTING"]
  },
  "anchor": "PerimeterWall",
  "spacing": { "type": "linear", "interval_mm": 3000 },
  "offsets": {
    "fromCorner_mm": 600,
    "mountingHeight_mm": 450
  },
  "side": "Inside",
  "pattern": "AlongPerimeter",
  "score": {
    "weights": {
      "distanceToCorner": 1.0,
      "distanceToOtherSocket": 0.5,
      "distanceToDoor": 2.0,
      "distanceToWindow": 0.3,
      "wallAlignment": 1.5
    },
    "minScore": 0.4
  },
  "exclusions": [
    "Within 600 mm of door swing",
    "Behind wall-fixed furniture",
    "On glazed walls",
    "Within 300 mm of corner"
  ],
  "priority": 50
}
```

**Anchor types** the engine understands:

| Anchor          | What it means                                                |
|-----------------|--------------------------------------------------------------|
| `PerimeterWall` | Along the inside face of the room's perimeter walls           |
| `ColumnGrid`    | At the intersections of the structural grid                  |
| `CeilingGrid`   | At the centres of suspended-ceiling tiles (600/1200 modules) |
| `RoomCenter`    | Single fixture at the geometric centre                        |
| `EquidistantArray` | Even spacing across the floor (lighting fallback)         |
| `ScopeBox`      | At the bounds of a named scope box                           |
| `LevelDatum`    | At fixed offsets from a level line (risers, panels)          |

### 7.9 Family authoring — what a fixture needs to be auto-place-friendly

Six parameters added in v4 MVP let the engine know *where* a fixture should
sit *relative to its host*. Missing parameters → engine falls back to the
family's insertion point (which is often the wrong place):

| Parameter            | Type   | Purpose                                        |
|----------------------|--------|------------------------------------------------|
| `PLACE_ANCHOR`       | text   | "Wall", "Ceiling", "Floor", "Furniture"        |
| `PLACE_OFFSET_X_MM`  | length | Horizontal offset from anchor                  |
| `PLACE_OFFSET_Y_MM`  | length | Vertical offset (above floor / below ceiling)  |
| `PLACE_SIDE`         | text   | "Inside", "Outside", "Either"                  |
| `PLACE_PRIORITY`     | int    | Higher wins when two rules collide             |
| `PLACE_GROUP`        | text   | Siblings that should align (e.g. row of pendants) |

Two more authoring rules are non-negotiable for accurate auto-routing:

1. **Connector position must equal the actual electrical termination.**
   If the connector point sits 50 mm inside the body, every conduit drop
   routes 50 mm short and the model looks correct but the BOQ is wrong.
2. **Lookup-table lumens / wattage for lighting families.** Place
   `LMP_LUMENS`, `LMP_WATTAGE`, `LMP_CCT_K`, `LMP_CRI` as type parameters so
   the Lighting Grid math has data to read.

### 7.10 Project overrides for placement rules

Drop `<project>/_BIM_COORD/placement_rules.json` next to the model.
Same schema. Project rules **win by `id`** — to override the corporate
"office-twin-socket-perimeter" rule, declare a project rule with the same
id and STING merges the project's fields on top of the corporate baseline.

### 7.11 Tuning the scorer for "perfect" auto-layouts

The scorer ranks every candidate position. When two layouts both *look*
fine, the higher-scored one wins. Tune the weights to bias toward your
office's house style:

| Bias for                     | Increase weight on                       |
|------------------------------|------------------------------------------|
| Even spacing                 | `distanceToOtherSocket` (0.5 → 1.0)      |
| Alignment with column grid   | `wallAlignment`, `gridAlignment`         |
| Avoiding doors               | `distanceToDoor` (2.0 → 4.0)             |
| Hiding behind furniture lines| `furnitureAlignment`                     |
| Symmetry                     | `centerlineAlignment`                    |

Save tuned weights as a project rule override. Set `minScore` higher (0.4 →
0.6) to *prevent* ugly fallback placements — at the cost of some elements
being skipped (engine flags them in the result panel).

### 7.12 Emergency lighting auto-place (BS 5266-1)

Smart-place ships a `BS5266` rule pack that produces compliant emergency
lighting layouts:

- ≤ 2 m of every change of escape-route direction.
- ≤ 2 m of each final exit door.
- ≤ 2 m of each first-aid post / fire-fighting equipment.
- ≤ 2 m of each toilet > 8 m².
- Maximum 12 m apart along a straight escape route.
- 1 lux minimum on the escape-route centerline (open areas: 0.5 lux).
- Anti-panic uplift: 0.5 lux floor coverage in lifts/risers.

The pack runs *alongside* the lumen-method grid and inserts a parallel
emergency-luminaire layer; emergency fixtures don't count toward the
general-lighting target lux. Emergency luminaires are tagged with
`PROD = EMG` and are picked up automatically by the
`elec-fire-alarm-A1-1to100` and `elec-lighting-A1-1to100` drawing types.

### 7.13 UK mounting-height reference

Defaults baked into STING's families (override per project under
`<project>/_BIM_COORD/mounting_heights.json`):

| Element                       | Height AFFL (mm) | Standard            |
|-------------------------------|------------------|---------------------|
| 13 A socket (general)         | 450              | BS 8300 / Part M    |
| 13 A socket (kitchen worktop) | 1050             | BS 7671             |
| Light switch                  | 1200             | BS 8300             |
| Fire-alarm break-glass call point | 1400         | BS 5839-1           |
| Fire-alarm sounder/beacon     | 2400 (high-level)| BS 5839-1           |
| Smoke detector (ceiling)      | 0 (host)         | BS 5839-1           |
| Heat detector (ceiling)       | 0 (host)         | BS 5839-1           |
| Emergency luminaire (ceiling) | 0 (host)         | BS 5266-1           |
| Data outlet (with socket)     | 450              | spec / BCO          |
| Thermostat                    | 1500             | spec                |
| Door access reader            | 1000             | spec / Part M       |
| Disabled-toilet alarm pull    | 100 (low)        | BS 8300 — accessible|
| Disabled-toilet ceiling pull cord | 0 (ceiling)  | BS 8300             |

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

### 8.5 Cable sizing — the BS 7671 method

A cable size is **acceptable** only when *all four* tests pass simultaneously:

| Test                       | Inequality                  | Meaning                                                |
|----------------------------|-----------------------------|--------------------------------------------------------|
| 1. Carrying capacity       | `Iz ≥ In`                   | Cable's safe current ≥ breaker rating                  |
| 2. Protection coordination | `In ≥ Ib`                   | Breaker rating ≥ design current                        |
| 3. Voltage drop            | `Vd ≤ budget`               | End-to-end drop within Appendix 4 limits               |
| 4. Fault disconnection     | `Zs × Ia ≤ U₀`              | Loop impedance × trip current ≤ supply voltage         |

Where:
- `Ib` = design current (load current after diversity)
- `In` = nominal breaker rating (next standard size up from `Ib`)
- `Iz` = cable's effective current-carrying capacity *after* derating
- `Vd` = voltage drop end-to-end
- `Zs` = earth-fault loop impedance
- `Ia` = current that causes the breaker to trip in 0.4 s (final) / 5 s (distribution)
- `U₀` = nominal phase-earth voltage (230 V in UK)

`Iz` is *derated* from the tabulated value:

```
Iz = It × Cg × Ca × Ci × Cs
```

| Factor | Meaning                          | Typical                          |
|--------|----------------------------------|----------------------------------|
| Cg     | Grouping (BS 7671 Table 4C1)     | 0.80 for 4 circuits in tray      |
| Ca     | Ambient temperature (Table 4B1)  | 0.94 at 35 °C                    |
| Ci     | Thermal insulation (Table 52.2)  | 0.50 in completely-insulated wall|
| Cs     | Soil resistivity (buried cables) | 0.85 in clay                     |

STING's auto-drop reads `Ib` from the circuit's connected load × demand
factor, picks the next-standard `In`, looks up `It` from BS 7671 Table 4D5
(twin-and-earth) / 4E1 (singles in conduit) / 4F1A (multicore in tray),
applies the derating chain, and selects the smallest size that passes all
four tests. Where the cable size is forced larger by voltage drop, the
result panel reports *"upsized for Vd from 2.5 mm² to 4.0 mm²"*.

### 8.6 Voltage-drop calculation

Per circuit:

```
Vd = (mV/A/m × L × Ib) / 1000          (single phase)
Vd = (mV/A/m × L × Ib × √3) / 1000     (three phase, no neutral)
```

`mV/A/m` comes from BS 7671 Table 4D5 (column 3 single-phase, column 4
three-phase). `L` is the **route length**, not the straight-line distance —
STING reads it from the conduit/tray segment lengths it dropped in §8.1.

End-to-end `Vd_total` is the sum across the supply chain:

```
Vd_total = Vd_main_to_SDB + Vd_SDB_to_FDB + Vd_FDB_to_outlet
```

STING accumulates this through the panel hierarchy. If `Vd_total > budget`,
the validator flags the offending circuit and suggests upsizing the *first*
section that produces enough headroom (usually the main feeder, not the
final circuit).

### 8.7 Fault level and breaker discrimination

Two checks every electrical engineer must do, and STING automates both:

**Breaker breaking capacity.** At the panel terminals the *prospective
fault current* `Ipf = U / Z_loop`. The breaker's **kA rating** must exceed
`Ipf`. Common values:

| Location               | Typical Ipf | Breaker kA needed |
|------------------------|-------------|-------------------|
| Domestic CU            | 6 kA        | 6 kA              |
| Small-commercial DB    | 10 kA       | 10 kA             |
| LV main panel          | 25–50 kA    | 25 kA / 50 kA     |
| Industrial main switchgear | 50–100 kA | 65 kA / 100 kA   |

**Discrimination (selectivity).** An upstream breaker should NOT trip
before the downstream one. Achieved by:

- Current ratio `In_up / In_down ≥ 1.6`
- Trip-curve compatibility (upstream Type C / D, downstream Type B / C)
- Time delay on upstream MCCBs (S-curve)

The Spec validator flags both violations.

### 8.8 Earthing and bonding

Every metal enclosure (panels, conduits, trays, equipment cases) must be
tied back to the **Main Earth Terminal (MET)** so a fault current trips the
breaker before touching anyone.

**CPC (Circuit Protective Conductor) sizes** per BS 7671 Table 54.7
(adiabatic equation), applied automatically to STING's `CPC_SZ_MM`:

| Phase conductor (mm²) | CPC (mm²)        |
|-----------------------|------------------|
| ≤ 16                  | = phase          |
| 25 – 35               | 16               |
| ≥ 50                  | phase ÷ 2        |

**Main protective bonding** (Reg 411.3.1.2) — gas, water, oil, structural
steel, lightning protection — sized per Table 54.8 (commonly 10 mm² for
TN-S/TN-C-S installs).

**Supplementary bonding** in special locations (BS 7671 Section 701/702/
704) — bathrooms, swimming pools, kitchens — 4 mm² minimum between exposed
and extraneous metalwork. STING's smart-place pack `BS7671-Bonding`
auto-inserts bonding straps when a Room's *function* matches a special
location.

### 8.9 Cable schedule export

`DOCS → Doc Automation → Cable Schedule` (or `BIM → Excel Link → Export
Schedules`) produces one row per circuit:

| Column            | Source                                                   |
|-------------------|----------------------------------------------------------|
| Cable reference   | TAG (`E-BLD1-Z01-L02-LTG-LTG-LFL-0042`)                  |
| From              | Panel name + circuit number                              |
| To                | Fixture description + tag                                |
| Cable type        | XLPE / PVC / MICC / FP200 / SWA                          |
| Cores × size      | mm² (e.g. `3C × 2.5` or `4C × 16 + E`)                   |
| Length            | Sum of conduit/tray segment lengths (route, not straight)|
| Ib / In / Iz      | Computed                                                 |
| Vd %              | Computed                                                 |
| Zs                | Computed                                                 |
| Origin            | Substation / origin point                                |
| Cable code        | Manufacturer reference                                   |
| Notes             | Fire rating (Cca / B2ca), LSZH, armour, drum number     |

This is the QS handover document, the contractor's pricing schedule, and
the as-built record all in one. Treat it as a deliverable, not a by-product.

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

### 11.6 The DrawingType POCO — every field that matters

A drawing type is a JSON bundle. Here are the fields you'll actually tune
to get a perfect auto-layout, in roughly the order you'll meet them:

| Field                | What it controls                                  | Tuning advice                                            |
|----------------------|---------------------------------------------------|----------------------------------------------------------|
| `id`                 | Stable identifier (e.g. `elec-lighting-A1-1to100`)| Reusable across projects — pick once and stick           |
| `name`               | Human label                                       | Browser display                                          |
| `purpose`            | Plan / RCP / Section / Schedule / Spool / Coord   | Drives the routing dispatch                              |
| `discipline`         | `Electrical`, `Mechanical`, `*` wildcard          | Use `*` only for cross-disc legends                       |
| `phase`              | `Construction` / `As-Built` / `Demolition` / `*`  | Routes a refurb model to the right type set              |
| `paperSize`          | A0 / A1 / A2 / A3                                 | A1 for layouts, A3 for details, A2 for risers            |
| `titleBlockFamily`   | Specific `.rfa` family name                       | **Always declare** — fallback picks "first available"    |
| `orientation`        | Landscape / Portrait                              | Landscape is standard, Portrait for risers               |
| `scale`              | 1:50, 1:100, 1:200, 1:500                         | 1:100 layouts, 1:50 plant rooms, 1:200 site, 1:500 context |
| `detailLevel`        | Coarse / Medium / Fine                            | Fine for fab + plant rooms, Medium for layouts           |
| `viewTemplateName`   | Existing Revit template by name                    | Or use a *managed* pack (§11.9)                          |
| `viewportTypeName`   | Title-on-sheet style                               | Always set — default Revit titles are ugly               |
| `sheetNumberPattern` | `"E-{lvl}-{seq:D3}"`                              | See §11.12 — never type sheet numbers                    |
| `sheetNamePattern`   | `"Lighting Plan — {lvl}"`                          | Matches your office numbering                            |
| `crop`               | Strategy + margin                                  | See §11.13 — biggest lever for tightness                 |
| `sectionMarker`      | Family + mark prefix + bubble style + farClipMm    | Section / elevation / callout symbol set                 |
| `viewStylePackId`    | Reference to a pack                                | See §11.8                                                |
| `tokenProfile`       | Tag depth + style preset + segment mask + colour   | Phase 135 — controls auto-tag appearance                 |
| `titleBlockParams`   | Map of param→value template                        | See §11.12 — auto-fills the title block                  |
| `annotation`         | AutoDim, AutoTag, dim style, per-cat tag families  | See §11.11                                               |
| `print`              | Colour scheme, line-weight scale, halftone links   | 0.85 line scale for layouts, 1.0 for fab                  |
| `extends`            | Parent profile id                                  | Inherit from a base, override what differs               |
| `slots[]`            | Normalised viewport positions                      | See §11.7 — paper-size-independent                        |
| `origin`             | `corporate` (checksum-locked) / `project`          | Set automatically — don't touch manually                  |

### 11.7 Slots — viewport positions in normalised coordinates

A slot describes *where on the sheet* a viewport sits, in **0..1 over the
drawable zone** (the area the title block leaves for content — *not* paper
edges). Same definition, different paper sizes, different physical spots:

```json
"slots": [
  { "id": "main",     "x": 0.0,  "y": 0.0,  "w": 0.7,  "h": 1.0 },
  { "id": "key-plan", "x": 0.75, "y": 0.7,  "w": 0.25, "h": 0.25, "scaleOverride": 500 },
  { "id": "legend",   "x": 0.75, "y": 0.35, "w": 0.25, "h": 0.30 },
  { "id": "notes",    "x": 0.75, "y": 0.0,  "w": 0.25, "h": 0.30 }
]
```

| Slot field          | Purpose                                                |
|---------------------|--------------------------------------------------------|
| `id`                | Identifier, used by the slot resolver                  |
| `x`, `y`, `w`, `h`  | 0..1 over drawable zone (origin top-left)              |
| `scaleOverride`     | Override profile scale on this slot only (key plans)   |
| `detailLevelOverride`| Coarse / Medium / Fine                                |
| `viewTemplateOverride`| Different template on this slot (e.g. uncoloured key plan) |
| `viewportTypeOverride`| Different viewport title                             |

**Layouts that work for electrical**:

- *Lighting plan*: `main` 70 % left + `key-plan` top-right + `legend` middle-right + `notes` bottom-right.
- *Power plan*: same as lighting (consistency aids reviewers).
- *Riser*: single full-bleed slot, portrait A2.
- *Panel schedule*: single slot (Revit panel-schedule view fills the sheet).
- *Fire-alarm plan*: 60 % main + 25 % key-plan + 15 % zone-list table.

### 11.8 View Style Packs — the shared visual layer

A *pack* factors graphic settings out of DrawingTypes so 80+ drawing types
share ~11 packs. Electrical-relevant ones:

| Pack                         | Use for                                             |
|------------------------------|-----------------------------------------------------|
| `corp-base`                  | Root pack — every other extends from this           |
| `corp-standard-plan`         | Power / lighting / fire-alarm layouts               |
| `corp-coordination`          | Services + clash visualisation (3D coord views)     |
| `corp-presentation-rich`     | Client-facing 3D / axonometric                      |
| `corp-presentation-mono`     | Mono client elevations                              |
| `corp-fabrication-shop`      | Fab packages (containment spools, panel internals)  |

Each pack defines `vg` (visibility graphics), `filters`, `detailLevel`,
`discipline` (View Discipline — Electrical, Mechanical, Coord), `visualStyle`,
`phaseFilter`, `phaseName`, `annotationCrop`, `farClipMm`, `viewRange`, and
`underlay`. Packs `extend` parent packs (loop-detected) — child fields
override parent fields.

**Tuning a pack for electrical layouts**:

- Set `discipline = Electrical` so Revit dims non-electrical items.
- Set `detailLevel = Medium`.
- Add filters: power circuits (project line colour by phase), fire-alarm
  circuits (red), comms (purple), emergency lighting (green), regular
  lighting (default).
- Set `farClipMm = 0` for ceiling-projected views.
- Halftone the architectural / structural categories so electrical reads
  as foreground.

### 11.9 Managed view templates (the new way) vs external (the old way)

Each pack carries a `templateMode`:

| Mode      | What happens                                                                |
|-----------|-----------------------------------------------------------------------------|
| `managed` | STING auto-generates and maintains a Revit template named `STING:<pack-id>:<ViewType>` from the pack JSON. Idempotent — absent → create; present + drift → re-apply. |
| `external`| Pack references a hand-built template by name. You maintain it manually.    |

**Managed mode is the recommended default.** Edit the JSON, run
`DrawingTypes_RegeneratePackTemplates`, every view stays in sync. Two
shared parameters mark managed templates for drift detection:
`STING_PACK_ID_TXT`, `STING_PACK_CHECKSUM_TXT`.

Migration commands: `ConvertPackToManaged`, `DetachFromManaged`,
`RegeneratePackTemplates`.

> **Caveat**: `displayOptions` (shadows, sketchy lines, ambient shadows)
> are flagged warnings — Revit has no public API for these.

### 11.10 The 199-filter Corporate Library

`STING_AEC_FILTERS.json` ships 199 corporate-baseline filters. The pack
`corp-coordination` references 21 of them, `corp-standard-plan` references
19. Electrical-relevant filters out of the box:

| Group           | Filters                                                          |
|-----------------|------------------------------------------------------------------|
| Power circuits  | By phase (L1 / L2 / L3), by voltage band, by panel               |
| Lighting        | By luminaire type (LED / fluorescent / emergency / decorative)   |
| Fire alarm      | By zone, by device type (detector / sounder / call point)        |
| Containment     | By route (cable basket / conduit / trunking / bus-bar)           |
| Voltage band    | LV (≤ 1000 V), ELV (≤ 50 V), SELV (battery / data)               |
| Status          | Existing / New / Demolished / Temporary (for refurb projects)    |
| Suitability     | S0 / S1 / S2 / S3 / S4 (CDE state)                              |

Filters are *lazy-created* on demand by `ViewStylePackApplier` — you don't
have to pre-mint them. A field-by-field merge applies your pack's
`StyleFilterRule` first, registry defaults next, Revit defaults last.

### 11.11 Annotation Rule Packs — auto-dim and auto-tag per profile

The `annotation` block tells STING what to *do* on the view after the
template is applied:

```json
"annotation": {
  "autoDim": true,
  "dimStrategy": "ChainOnGrid",
  "autoTag": true,
  "tagFamilies": {
    "ElectricalFixtures":  "STING - Electrical Tag",
    "LightingFixtures":    "STING - Lighting Tag",
    "FireAlarmDevices":    "STING - Fire Alarm Tag",
    "ConduitFittings":     "STING - Conduit Tag",
    "CableTrayFittings":   "STING - Tray Tag",
    "ElectricalEquipment": "STING - Panel Tag"
  },
  "denseUntilScale": 50
}
```

| Field             | Effect                                                              |
|-------------------|---------------------------------------------------------------------|
| `autoDim`         | Auto-place dimensions per `dimStrategy`                              |
| `dimStrategy`     | `ChainOnGrid`, `Bay`, `EdgeToCentreline`, `RoomDiagonal`, `None`    |
| `autoTag`         | Auto-place IndependentTags using the smart-placement engine          |
| `tagFamilies`     | Per-category tag family override                                     |
| `denseUntilScale` | At scales finer (e.g. 1:50) than this, tag everything; coarser, only major equipment |

> **#1 cause of unreadable plans**: tagging every socket at 1:200. Honour
> `denseUntilScale` — at 1:200 you only need DBs and major equipment.

### 11.12 Token substitution — sheet numbers and title-block bindings

Sheet numbers / names use string templates with **token substitution**.
Available tokens:

| Token         | Source                                            | Example       |
|---------------|---------------------------------------------------|---------------|
| `{disc}`      | Single-letter discipline                          | `E`           |
| `{discipline}`| Full discipline name                              | `Electrical`  |
| `{lvl}`       | Level code                                        | `L02`         |
| `{sys}`       | Sanitised system code                             | `LTG`         |
| `{spool}`     | Spool number (fab)                                | `SP-014`      |
| `{mark}`      | Section / elevation / callout mark                | `A`           |
| `{seq}`       | Zero-padded 4-digit sequence (default)            | `0042`        |
| `{seq:D2}`    | Sequence with width 2                             | `42`          |
| `{seq:D3}`    | Sequence with width 3                             | `042`         |

Title-block parameters bind **declaratively** via `titleBlockParams`. Two
substitution kinds:

| Substitution form         | Source                                                |
|---------------------------|-------------------------------------------------------|
| `${PRJ_ORG_xxx}`          | `ProjectInformation` parameter named `PRJ_ORG_xxx`    |
| `{disc}` / `{lvl}` / `{seq:Dn}` etc. | Caller-supplied token dictionary           |

Example for a UK office's electrical lighting type:

```json
"titleBlockParams": {
  "Project Number":    "${PRJ_ORG_PROJECT_CODE}",
  "Project Name":      "${PRJ_ORG_PROJECT_NAME}",
  "Client":            "${PRJ_ORG_CLIENT_NAME}",
  "Originator":        "${PRJ_ORG_ORIGINATOR_CODE}",
  "Suitability":       "${PRJ_ORG_PHASE}",
  "Sheet Number":      "{disc}-{lvl}-LTG-{seq:D3}",
  "Sheet Name":        "Lighting Plan — {lvl}",
  "Discipline":        "Electrical",
  "Drawing Type":      "Lighting Layout",
  "Revision":          "${PRJ_ORG_REV}",
  "Drawn By":          "${PRJ_ORG_DRAWN_BY}",
  "Checked By":        "${PRJ_ORG_CHECKED_BY}"
}
```

Unknown tokens pass through literally. String / Integer / Double storage
types are auto-handled. Numeric parameters that fail to parse warn, then
write the default.

### 11.13 Scope-box auto-binding — the naming convention

This is the closest STING gets to magic. Name a scope box:

```
STING::<drawing-type-id>::<level-code?>::<tag?>
```

Examples:

```
STING::elec-lighting-A1-1to100::L02::west
STING::elec-power-A1-1to100::L02::east
STING::elec-fire-alarm-A1-1to100::GF
STING::elec-riser-A2-1to100::ALL::main
```

Run `DrawingTypes_FromScopeBoxes`. STING:

1. Parses the name → `drawingTypeId`, `levelCode`, `tag`.
2. Creates a view of the right `ViewType` (FloorPlan / Section / 3D etc.).
3. Applies the profile (template, scale, crop, style pack).
4. Crops to the scope box.
5. Creates the sheet (sheet number = pattern with substituted tokens).
6. Places the viewport in slot `main` (or as configured).
7. Stamps the title block via `titleBlockParams`.
8. Adds the new sheet to the drawing register.

Idempotent — re-run finds existing stamped views (by
`STING_DRAWING_TYPE_ID_TXT`) and updates them rather than duplicating.

> **The most efficient electrical workflow** is: get the architect's scope
> boxes, rename them with the STING:: convention for every electrical
> drawing you need (one per level per type), run
> `DrawingTypes_FromScopeBoxes` once, walk away with a complete sheet set.

### 11.14 Crop strategies — the "tightness" lever

`crop` chooses *what* the view shows. Five strategies:

| Strategy           | Behaviour                                                         | Use for                              |
|--------------------|-------------------------------------------------------------------|--------------------------------------|
| `ScopeBox`         | Crop to a named scope box                                          | All discipline plans                 |
| `ScopeBoxOrBbox`   | Scope box if found, else element bounding box + margin            | Auto-generated from selection        |
| `TightBbox`        | Bounding box of all elements + margin                              | Detail callouts, fab spools          |
| `RoomBoundary`     | Crop to a single room outline (falls back to TightBbox if no rooms)| Single-room electrical sheets        |
| `None`             | Do not crop                                                        | Riser elevations, full-floor plans   |

Margin is set in mm (e.g. `"margin_mm": 50`). The difference between a
tight crop and a sloppy one is usually one number.

### 11.15 Section markers and view-range

`sectionMarker` controls section / elevation / callout cosmetics:

```json
"sectionMarker": {
  "family":     "STING - Section Marker A1",
  "markPrefix": "S",
  "bubbleStyle":"Filled",
  "farClipMm":  3000
}
```

For electrical risers and panel-internal sections, set `farClipMm` short
(1–3 m) to prevent picking up containment from adjacent zones.

### 11.16 Browser Organizer + drift kinds

`DrawingTypes_BrowserOrganize` creates `'STING - by Drawing Type'`
organisations for views *and* sheets, keyed off `STING_DRAWING_TYPE_ID_TXT`.
Worth its weight in gold once you exceed 50 sheets.

`DrawingDriftDetector` reports five drift kinds:

| Kind                   | What it means                                              | Fix                          |
|------------------------|------------------------------------------------------------|------------------------------|
| `SCALE`                | View scale ≠ profile scale                                 | SyncStyles                   |
| `DETAIL`               | Detail level ≠ profile detailLevel                         | SyncStyles                   |
| `TEMPLATE`             | View template detached / replaced                          | SyncStyles                   |
| `MANAGED_TEMPLATE`     | Pack JSON updated, template still on previous checksum     | SyncStyles                   |
| `TOKEN_PROFILE_DRIFT`  | TokenProfile changed, but tags not re-tagged with new style | RetagStale → SyncStyles      |

Run before every issue. Locked views (`STING_STYLE_LOCKED_BOOL = 1`) are
skipped — set this on hand-crafted hero views you don't want syncing to touch.

### 11.17 Profile inheritance — `extends`

Author one parent, customise children:

```json
{
  "id": "elec-lighting-emergency-A1-1to100",
  "extends": "elec-lighting-A1-1to100",
  "annotation": {
    "tagFamilies": {
      "LightingFixtures": "STING - Emergency Tag (Green)"
    }
  },
  "viewStylePackId": "corp-emergency-lighting"
}
```

Child fields fill nulls in the parent and override declared parents
field-by-field. Loop-detected. Same mechanism applies to View Style Packs.

### 11.18 Conditional routing rules

A routing rule maps `(discipline, phase, docType, level, projectCode) →
drawingTypeId`. Five **regex** predicates (all set predicates must match,
logical AND):

```json
{
  "disciplineMatches":   "E|LV|FP",
  "phaseMatches":        "AsBuilt|Construction",
  "docTypeMatches":      "LIGHTING|POWER",
  "levelMatches":        "L0[1-3]",
  "projectCodeMatches":  "^EDC-",
  "drawingTypeId":       "elec-lighting-A1-1to100"
}
```

First-match-wins. Project rules are **prepended** to the corporate baseline
when the override JSON loads, so project rules always have first crack.

### 11.19 Riser diagram — electrical-specific setup

`elec-riser-A2-1to100` is portrait A2, scale 1:100, view template
`STING - Electrical Riser`. To populate it:

1. Create one **Section View** oriented vertically through the riser shaft.
2. Set the section's *view range* to the full building height
   (`Top = Top of building`, `Bottom = Lowest level`).
3. Apply the profile (`DrawingTypes_FromScopeBoxes` if a riser scope box
   exists, else `Sheet Manager → Create From Template → elec-riser-A2-1to100`).
4. STING:
   - Crops to the scope box.
   - Tags every panel and major piece of switchgear it crosses.
   - Adds level-line annotations at each storey.
   - Stamps panel feeder cables with cable size and length.
   - Places the diagram on the sheet via the riser slot layout.

### 11.20 The full apply pipeline — 10 steps

`DrawingTypePresentation.Apply(doc, view, dt)` runs in this exact order
(every step try/catch-wrapped; warnings collect but the run continues):

1. **Lock check** — `STING_STYLE_LOCKED_BOOL = 1` → skip view.
2. **Stamp** drawing-type id (`STING_DRAWING_TYPE_ID_TXT`).
3. **Scale** — `view.Scale = dt.Scale`.
4. **Detail level** — `view.DetailLevel = dt.DetailLevel`.
5. **View template** — apply by name (or run managed-pack syncer).
6. **Crop strategy** — `DrawingCropApplier` runs the chosen crop.
7. **View style pack** — resolve + `ViewStylePackApplier.Apply`
   (filters, VG, line/text/dim styles, tag-family map).
7.5 **Token profile** — `TokenProfileApplier` so auto-tags inherit the
    profile's tag depth + segment mask + colour scheme.
8. **Annotation pass** — `AnnotationRunner` (auto-dim + auto-tag using
   `tagFamilies` from the rule pack).

For **sheet creation** an extra pair runs after the sheet is minted:

9. **Sheet stamp** — `DrawingTypeStamper.Stamp(sheet, dt.Id)`.
10. **Title-block param binding** — `TitleBlockParamApplier.Apply`.

### 11.21 Tuning a profile for "perfect" auto-layouts

What separates a *good* drawing-type from a *perfect* one:

- **Always set `viewportTypeName`** — default Revit viewport titles look
  unfinished.
- **Set `crop.margin_mm` deliberately** — 50 mm is a sensible default; 25
  mm if your title block is generous; 75 mm if you have lots of leader
  notes outside the view.
- **Set `print.lineWeightScale = 0.85`** for layouts (pdf print legibility),
  `1.0` for fab.
- **Honour `denseUntilScale`** — at 1:200, only DBs + major plant; at 1:50,
  tag everything.
- **Always declare every `titleBlockParams` entry** the title block has — a
  half-bound title block is worse than a fully-manual one.
- **Use `extends`** — author one parent profile per discipline/purpose
  (`elec-lighting-A1-1to100`), and have child profiles for variants
  (`-emergency`, `-fire-alarm-overlay`, `-power-coord`).
- **Author scope boxes once, run `FromScopeBoxes` forever.**
- **Run `SyncStyles` before every drop** — drift is silent and cumulative.
- **Lock hero views** (`STING_STYLE_LOCKED_BOOL = 1`) to protect manual
  composition from automated sync.

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

## 12a. STING Electrical Panel — sub-system map

The dedicated **STING Electrical Panel** hosts the discipline's calculation
kernels. Every sub-system below is reached from a button on that panel and
backed by 54 command files (~10,676 lines) under `Commands/Electrical/`
plus the support engines under `Core/Electrical/`, `Core/Calc/` and
`Core/SLD/`.

| Sub-system            | Headline commands                                          | Standards           |
|-----------------------|------------------------------------------------------------|---------------------|
| **Cable Sizing**      | `CableSizerCommand` — picks the smallest size that passes the 4-test rule | BS 7671 / IEC 60364 |
| **Voltage Drop**      | `VoltageDropCommand`, `VoltageDropScheduleCommand`, `VoltageDropFlagCommand` | BS 7671 App 4       |
| **Feeder Sizing**     | `FeederSizerCommand` — feeder cables with diversity        | BS 7671 / NEC       |
| **Fault Current**     | `FaultCurrentCommand`, `FaultCurrentScheduleCommand`, `AicRatingCommand` | IEC 60909        |
| **Arc Flash**         | `ArcFlashCommand`, `ArcFlashLabelSheetCommand`, `ArcFlashScheduleCommand` | IEEE 1584 / NFPA 70E |
| **Busbar**            | `BusbarModelingCommand` + `BusbarSizerEngine`              | IEC 60947           |
| **Conduit Routing**   | `ConduitAutoRouteCommand`, `ConduitConsolidatorCommand` (A* + ACO + tray merge) | BS 7671 §522 |
| **Cable Schedule**    | `CableScheduleBuilderCommand` (cable + conduit + box BOMs) | ISO 19650            |
| **Circuit Wizard**    | `CircuitWizardCommand` + WPF dialog                        | —                   |
| **Selective Coord**   | `SelectiveCoordCommand` + TCC database                     | NEC 240.87           |
| **Tray Fill**         | `ShowTrayFillCommand` + `TrayFillCalculator`               | NEC 392 / BS EN 61537 |
| **Conduit Fill**      | `ConduitFillValidateCommand` + `ConduitFillSolver`         | BS 7671 / NEC App C |
| **Phase Balance**     | `PhaseBalanceCommand`                                      | BS 7671 §312        |
| **Demand Factor**     | `DemandFactorReportCommand`                                | NEC 220 / BS 7671 App 16 |
| **Lighting**          | `LightingPowerDensityCommand`, `EmergencyLightingAuditCommand`, `LpdColorCommand`, `EmergencyLightingMarkCommand` | ASHRAE 90.1 / Part L / BS 5266 |
| **Photometrics**      | `AssignPhotometricCommand`, `PhotometricLibraryCommand`, `PhotometricDesignReviewCommand`, `DialuxRoundTripCommand`, `PhotometricPreflightCommand` | IES LM-83 / EN 13032 |
| **SLD**               | `SLD_Generate`, `SLD_ExportDXF`, `SLD_Riser` + IUpdater live sync | IEC 60617 |
| **Standards**         | `ElectricalStandardsValidatorCommand`                      | BS 7671 / NEC       |
| **External Exports**  | `DIALuxExportCommand`, `EtapExportCommand`, `EasyPowerExportCommand` | IFC 4 / CSV |
| **Lightning (LPS)**   | 18 LPS commands — `LpsComplianceCheckCommand` to `LpsRollingSphere3DCommand` | BS EN 62305 |
| **IFC Results**       | `IfcResultsImportCommand`, `MultiEngineAggregatorCommand`  | IFC 4 + Pset_StingLighting |

> **The sub-systems aren't optional add-ons.** Each one is a kernel that
> reads from the same STING parameter pack. Output from the **Cable Sizer**
> (cable size, derated `Iz`) becomes input for the **Voltage Drop**
> calculator; output from the **Fault Current** engine becomes input for
> **Arc Flash**; output from **Demand Factor** becomes the design current
> the cable sizer needs. Run them in this order and the chain coheres.

---

## 12b. Cross-discipline integration — what the other panels give you

A real electrical design lives downstream of architecture, plumbing
and (especially) HVAC. The STING HVAC Panel and STING Plumbing Panel
each push data into the model that your electrical schedules then
consume. Here's the contract.

### 12b.1 HVAC equipment as electrical loads

When the HVAC team runs **Hvac_BlockLoad** + **Hvac_SelectIdus**
(detailed in `STING_HVAC_LAYMANS_GUIDE.md`), every AHU / FCU / VRF
indoor unit / chiller / pump / cooling tower / heat pump in the
project gets a sized capacity stamped as a Revit parameter. That
capacity drives the electrical load on your panels:

| HVAC parameter           | What it carries                            | Where electrical reads it           |
|--------------------------|--------------------------------------------|-------------------------------------|
| `HVC_PEAK_SENS_W`        | Per-space peak sensible cooling load (W)   | Drives FCU connected-load lookup    |
| `HVC_FLOW_LS`            | Design supply airflow (L/s)                | Drives fan kW estimation            |
| `HVC_SELECTED_IDU_ID_TXT`| Catalogue id of the chosen IDU             | Resolves vendor kW from the catalogue|
| `HVC_LOAD_SOURCE_TXT`    | Provenance: BlockLoad / gbXML / TRACE      | Confidence flag on cable sizing     |

In the EQPT and SYS tabs of the HVAC panel, every row shows the device's
nameplate `Apparent Power kVA`, `Voltage`, `Number of Poles` and
`Distribution System`. Wire those to your panels exactly as you would a
manually-placed luminaire — `Modify → Power → pick the panel`.

When the HVAC team re-runs **Hvac_PropagateLoads** after a layout change
(very common — they nudge a duct, the load shifts, the AHU resizes), your
panel schedule on the affected panel **changes**. Two ways to find out:

1. **Real-time auto-tagger.** Subscribe `StingAutoTagger` (CREATE → Auto
   Tagger Toggle); when an HVAC fixture changes geometry, you get a
   notification + the panel schedule updates on the next tag run.
2. **Run the connectivity validator weekly.** If a chiller's connected
   load grew from 75 kW to 95 kW and your feeder is still sized for
   75 kW, the Spec validator flags it red.

### 12b.2 Plumbing equipment as electrical loads

Pumps (booster sets, recirculation pumps, sump pumps), TMVs with
electronic actuators, immersion heaters in calorifiers, UV-water
treatment, and rainwater-harvesting pump sets all draw electrical power.
The STING Plumbing Panel writes:

| Plumbing parameter       | What it carries                            |
|--------------------------|--------------------------------------------|
| `PLM_RECIRC_FLOW_LS`     | DHW recirc flow (sizes the recirc pump)    |
| `PLM_EXP_VESSEL_L`       | Expansion vessel size (informs cabinet sizing) |
| `PLM_TMV_SET_TEMP_C`     | TMV setpoint (controls input)              |

For every pump / heater family the plumbing team places, **CREATE →
Tag & Combine** stamps a `PROD = PMP` or `PROD = WHT` tag. Filter your
electrical schedule by `ASS_PROD_COD_TXT in (PMP, WHT, ACT)` to capture
plumbing-side loads in one query.

### 12b.3 Coordinated cable + duct + pipe routing

`ConduitAutoRouteCommand` (electrical) and `Plumb_AutoRoute` (plumbing)
share the same A* router with a voxel-grid clash check. When both run on
the same level they negotiate via the `Core/Routing/` voxel grid — your
conduit doesn't drop into a 200 mm soil stack. If the plumbing team
hasn't routed yet, run the electrical auto-route, then re-run after
their routing pass; the second pass adds your conduits at a different
elevation band.

### 12b.4 Penetration coordination

Cable trays, conduits and busbar trunking all penetrate fire-rated
walls. The penetration engine (Phase 184 + 185) detects each crossing
and inserts a sleeve / firestop family with the correct UL system.

Workflow: place containment → **TAGS → Routing → Penetrations Detect
And Place** → the engine walks every electrical containment element,
finds the wall/floor it crosses, picks a sleeve from
`STING_FAMILY_SWAP_REGISTRY.json`, stamps `PEN_CERTIFICATION_TXT` with
the UL system reference. The penetration coverage validator flags any
crossing that the auto-pass missed.

---

## 12c. Recent commands worth knowing about (post-v1.0 additions)

These shipped in the last few phases. They aren't strictly *new*
concepts but they remove pain points first-timers hit constantly.

| Command                            | Tab       | What it does                                              |
|------------------------------------|-----------|-----------------------------------------------------------|
| `FamilyConformanceCheck`            | CREATE    | Audits a folder of `.rfa` against the STING contract (4 placement params + tag style matrix + Ring 1/2 position types). Use BEFORE bulk-stamping a vendor library. 100-point score → PASS ≥ 85, WARN 70-84, BLOCK < 70. |
| `Hvac_PublishToServer`              | HVAC RPRT | Bundles HVAC panel grids and pushes them to Planscape Server `/hvac/*` endpoints — your electrical dashboard then reads the canonical loads from the same source. |
| `Hvac_ImportGbxmlLoads`             | HVAC LOADS| Reads a TRACE / HAP / IES gbXML and overwrites STING block-load stamps with the simulator's numbers. Your cable sizer then sizes against the authoritative loads. |
| `Hvac_GenerateCxChecklist`          | HVAC FAB  | Emits a CSV under `_BIM_COORD/cx/` with per-class commissioning tasks. Electrical equipment (panels / switchgear / luminaires / fire alarm) gets matching rows you witness alongside HVAC. |
| `Symbols_BuildSeeds`                | TAGS      | Builds all 16 seed families from JSON. Includes `STING_SEED_ElectricalEquipment` (MDB / DB / SDB / MCB / MCCB / ACB / RCD), `STING_SEED_LightingFixture` (5 variants), `STING_SEED_ElectricalFixture` (4 variants), `STING_SEED_FireAlarmDevice` (5 variants), `STING_SEED_JunctionBox` (4 variants), `STING_SEED_CommunicationDevice` (5 variants). |
| `Symbols_SwapToManufacturer`        | TAGS      | Swaps a seed family for a manufacturer family via `STING_FAMILY_SWAP_REGISTRY.json`; stamps `PEN_CERTIFICATION_TXT` with the UL system. |
| `Symbols_DriftDetect`               | TAGS      | Detects symbols whose geometry / parameters have drifted from the JSON spec — catches the "manufacturer family lost a connector" failure mode. |
| `DesignOptions_Audit`                | DOCS      | Read-only audit of cost/carbon implications per design option. Use when the architect provides "option A vs option B" — STING reports the electrical containment delta. |
| `DesignOptions_ClashView`            | DOCS      | Creates a view isolating the primary option only so your clash detection doesn't false-flag option-B containment against option-A architecture. |
| `Hvac_EnvelopeStaleToggle`           | HVAC LOADS| Subscribes the envelope-stale IUpdater. After it's on, any wall / window geometry change marks affected Spaces with `HVC_LOAD_STALE_BOOL = 1`. Run `Hvac_BlockLoad` to re-stamp and the flag clears. Coordinate with your panel schedule re-run. |

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
| Not creating a Distribution System before placing panels                | Revit silently refuses to circuit fixtures                    | Panel audit + dock-panel status bar warning              |
| Forgetting to set `Demand Factor` on each panel                         | Panel schedule shows 100 % connected as design current — oversized cable everywhere | Spec validator (overspec warning)              |
| Hand-typing sheet numbers                                              | Inconsistency, gaps, duplicates                                | Use `sheetNumberPattern` + `titleBlockParams`            |
| Not honouring `denseUntilScale`                                        | Tag soup at 1:200 — unreadable plans                           | `Tag Overlap Analysis` reports collisions per scale      |
| Editing a managed view template by hand                                | SyncStyles wipes it on next run                                | Set the pack to `external` mode if you really must edit  |
| Naming scope boxes without the `STING::` prefix                        | `FromScopeBoxes` skips them                                    | Run `Sheet Audit` — surfaces unbound scope boxes         |
| Authoring tag families without `LMP_LUMENS` / `PLACE_ANCHOR`           | Lighting Grid math fails / smart-place defaults to insertion point | `Family Audit` flags missing parameters             |
| Mixing `managed` and `external` packs in one project                   | Drift detection inconsistent                                   | Pick one mode per pack and stick                          |
| Drawing risers in 2D                                                   | Section view auto-tagging finds nothing                        | Use a real Section View per §11.19                       |
| Forgetting to apply derating (Cg, Ca, Ci) when grouping cables in tray | Cables overheat under fault — fire risk                        | Spec validator Cg-aware sizing                           |
| Not validating discrimination on the upstream/downstream pair          | Whole panel trips on a single circuit fault                    | Spec validator                                            |

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
| Cg / Ca / Ci | Cable derating for grouping / ambient / insulation (BS 7671)        |
| Ib / In / Iz | Design / nominal-breaker / cable-effective current                  |
| Vd      | Voltage drop                                                             |
| Zs      | Earth-fault loop impedance (BS 7671)                                     |
| MET     | Main Earth Terminal                                                      |
| CPC     | Circuit Protective Conductor (the earth wire)                            |
| MF / UF / SHR | Maintenance / Utilisation / Spacing-to-mounting-height ratio        |
| K       | Room Cavity Ratio (lighting calc)                                        |
| Uo / UGR| Lighting Uniformity / Unified Glare Rating (CIBSE / EN 12464)            |
| LG7     | CIBSE Lighting Guide 7 (offices)                                         |
| LSZH    | Low-Smoke Zero-Halogen cable jacketing                                   |
| MICC    | Mineral Insulated Copper Clad cable (fire survival)                      |
| FP200   | Fire-rated cable (typical fire-alarm)                                    |
| TN-S / TN-C-S / TT | Earthing system types (UK supplies)                              |
| RCBO    | Residual Current Breaker with Overload (combined RCD + MCB)              |
| MCCB    | Moulded-Case Circuit Breaker (large feeders, often with S-curve)         |
| DALI    | Digital Addressable Lighting Interface (control protocol)                |
| CCT     | Correlated Colour Temperature (Kelvin)                                   |
| CRI     | Colour Rendering Index (0–100, ≥ 80 for offices)                         |
| AFFL    | Above Finished Floor Level                                               |
| DT      | Drawing Type — STING profile id (e.g. `elec-lighting-A1-1to100`)         |
| VSP     | View Style Pack — STING shared visual layer                              |
| Slot    | Normalised viewport position on a sheet (0..1 over drawable zone)        |
| Drift   | Difference between a view's current state and its profile expectation     |
| Scope box | Revit object that bounds views — STING uses naming convention to bind to DTs |

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

*Document version 1.1 — May 2026. Maintained alongside `CLAUDE.md`.
Companions in this series: `STING_HVAC_LAYMANS_GUIDE.md`,
`STING_PLUMBING_LAYMANS_GUIDE.md`, `HEALTHCARE_WORKFLOW_LAYMANS_GUIDE.md`,
`SLD_SYMBOLS_LAYMANS_GUIDE.md`. For deeper dives see
`docs/MEP_SYMBOL_GUIDE.md`, `docs/AEC_FILTER_LIBRARY.md` and the
CIBSE / BS 7671 references.

**Changelog:**
- v1.1 — Added §4 cross-panel discovery (Electrical / HVAC / Plumbing
  panels), §12a sub-system map for the dedicated Electrical Panel,
  §12b cross-discipline integration (HVAC + plumbing equipment as
  electrical loads, coordinated routing, penetration coordination),
  §12c recent commands (FamilyConformanceCheck, Symbols_BuildSeeds /
  SwapToManufacturer / DriftDetect, DesignOptions_Audit / ClashView,
  Hvac_PublishToServer, Hvac_ImportGbxmlLoads, Hvac_GenerateCxChecklist,
  Hvac_EnvelopeStaleToggle).
- v1.0 — Initial release covering placement, tagging, routing, panel
  schedules, validation, drawings, fabrication.*
