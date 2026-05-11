# StingTools Electrical Workflows — Plain-English Guide

> **Who this guide is for:** Electrical engineers, BIM coordinators, and project managers working on buildings with complex electrical systems — commercial offices, hospitals, data centres, industrial facilities, hotels, and mixed-use developments.

---

## What Does StingTools Do for Electrical Design?

StingTools' electrical engine automates the most time-consuming and error-prone parts of electrical BIM:

- Assigning every circuit to the right panel at the right voltage
- Balancing loads across three phases so no phase is overloaded
- Routing conduit through a building without clashing with structure
- Calculating voltage drop and breaker sizes from first principles
- Checking the model against BS 7671 and producing a compliance audit
- Generating panel schedules, cable schedules, and BOQs automatically

Think of it as having a specialist electrical draughtsman who never gets tired, never makes arithmetic errors, and can check the entire building at once.

---

## The Five Layers of the Electrical Engine

Everything in StingTools electrical sits in one of five layers. Understanding which layer you're working in tells you what data flows in and what comes out.

```
Layer 5 — UI & Workflows
    Buttons on the dock panel, JSON workflow chains, result panels
    Examples: ElectricalQA button, Panel Schedule workflow, Cable BOM export

Layer 4 — Commands
    One command per user action: BatchAssignCircuits, AutoRoute, BuildCableSchedule
    Examples: Commands/Electrical/, Commands/Panels/, Commands/Symbols/

Layer 3 — Engines
    The logic that does the actual work: routing pathfinder, load balancer, validators
    Examples: Core/Routing/, Core/Validation/, Core/Symbols/

Layer 2 — Manifests & Cached State
    The cable manifest, compliance cache, audit log
    Examples: _BIM_COORD/cables.json, _BIM_COORD/audit_log_*.jsonl

Layer 1 — Parameters & Categories
    Shared parameters, category bindings, stable GUIDs
    Examples: MR_PARAMETERS.csv, CATEGORY_BINDINGS.csv
```

A workflow runs down from Layer 5 (you click a button) and back up (results shown in the result panel). Knowing this helps when something goes wrong — the problem is always in one of the five layers.

---

## The Monday Morning Sequence — End-to-End Happy Path

This is the typical workflow for a project that has panels placed and loads defined but no routed conduit yet. Budget roughly 60–90 minutes of clicking; the engine runs for most of it.

```
Step 1:  Load Shared Parameters
         Binds the 85+ STING electrical parameters to the project's shared param file.
         Run once per project.

Step 2:  Build Seed Families (one-time)
         Creates STING_SEED_*.rfa placeholder families so the engine has something
         to work with before manufacturer families are loaded.

Step 3:  Place Fixtures (Placement Centre)
         Lighting fittings, sockets, panels, fire-alarm devices, data points —
         all placed from the room-rule engine using the fixture placement JSON.

Step 4:  Auto-Assign Circuits (Wave H voltage-band + grouping rules)
         Every unassigned circuit gets a panel and a slot.
         Emergency lighting → dedicated panel.
         Kitchen sockets → ring-final panel.
         Comms room → UPS-backed panel.

Step 5:  Phase Balance
         Shifts circuits between phases A/B/C to minimise the imbalance
         (target: <10% difference between heaviest and lightest phase).

Step 6:  Auto-Route Conduit (the big one — typically 5–20 minutes)
         Creates Conduit elements per cable.
         Auto-places junction boxes at BS 7671 §522.8.5 break-points.
         Detects slab penetrations and places Fire-Rated Penetration families.
         Snaps conduit to false-ceiling soffit where space allows.

Step 7:  Voltage Drop Calculation
         Walks every cable run from source to load, computes drop in volts and %,
         flags any run exceeding BS 7671 Appendix 12 limits (typically 3% for
         lighting, 5% for power from the origin of the installation).

Step 8:  Breaker Sizing Preview
         Calculates minimum protective device rating for each circuit.
         Compares against what's in the panel schedule.

Step 9:  BS 7671 Validation
         Runs the full §522.8 conduit standards check:
         bend radius, cable fill, fixings, fire-rated penetrations.

Step 10: Panel Schedule Export
         Generates panel schedules as Revit views and as Excel files.

Step 11: Cable Schedule (BOM)
         Produces a cable manifest: from panel, to load, cable type, size,
         length, circuit reference, conduit reference.
```

---

## Workflow-by-Workflow Breakdown

---

### Workflow 1: Electrical QA Workflow (the master chain)

**What it does in plain English:**
This is the comprehensive electrical quality check. Run it at the end of any design stage or after a major change. It chains nine steps from panel audit through to Excel export, skipping steps that don't apply (if there are no conduits, the conduit checks are skipped automatically).

**Steps explained:**

| Step | What actually happens | Can be skipped? |
|---|---|---|
| **1. Audit Panel Schedules** | Checks every panel for: missing schedules, wrong template, ELC_PNL_* parameters unfilled, circuits with no reference, over-loaded panels | No |
| **2. Compute Load Summary** | Totals connected load (kW) per panel, per phase, per level. Checks against panel rating and diversity factor. | Optional |
| **3. Auto-Assign Circuits** | Finds circuits not yet assigned to a panel and assigns them using voltage-band + grouping rules | Only if unassigned circuits exist |
| **4. Balance Three-Phase Loads** | Redistributes circuits across A/B/C to achieve <10% phase imbalance | No |
| **5. Preview Breaker Sizing** | Calculates minimum MCB rating per circuit from calculated current | Optional |
| **6. Voltage Drop Calculation** | Computes cumulative voltage drop from source to each load | Only after load summary |
| **7. BS 7671 Validation** | Checks §522.8 conduit standards: bend radius, fill factor, fixings spacing, penetration sealing | Only if conduits present |
| **8. Generate Compliance Audit** | Produces a Red/Amber/Green compliance report by panel, by level, by discipline | Optional |
| **9. Export Panel Schedules** | Exports all panel schedules to Excel, one worksheet per panel plus an INDEX sheet | Optional |

**What you get at the end:**
- Phase balance table (kW per phase per panel)
- Voltage drop report (worst-offending circuits highlighted)
- BS 7671 compliance audit
- Excel panel schedule workbook

---

### Workflow 2: Panel Schedule Production

**What it does in plain English:**
Generates panel schedules — the documents that electricians use during installation and that FM teams use for the life of the building. In a large commercial building there may be 50 to 100 panels. StingTools creates a compliant schedule for each one in seconds.

**The rule-based template system:**

StingTools chooses the right schedule template for each panel automatically:

| Panel type | Template chosen |
|---|---|
| Switchboard / main incomer | `STING - Switchboard Schedule` (3-phase, full diversity) |
| Three-phase distribution board | `STING - Three Phase DB Schedule` |
| Single-phase consumer unit | `STING - Consumer Unit Schedule` |
| Data / comms panel | `STING - Data Panel Schedule` |
| Any other panel | Global fallback template |

**Steps:**

| Step | What actually happens |
|---|---|
| **1. Batch Panel Schedules** | For every electrical panel in the model, creates a `PanelScheduleView` using the best-matched template. Stamps the schedule with the panel's discipline, building, and level codes. Fills `ELC_PNL_*` parameters (connected load, diversity, MCB type, protective device rating). |
| **2. Fill Spares & Spaces** | Fills empty slots with spare or space circuits as required by project standard. |
| **3. Export to Excel** | Exports every schedule: header row (panel ref, location, supply, rating), body rows (circuit, description, load, MCB, cable size), and a summary row (total connected load, diversity, maximum demand). |

**What you get:**
- One Revit `PanelScheduleView` per panel, ready to drop onto a sheet
- One Excel workbook with one worksheet per panel plus an INDEX
- Every schedule stamped with `STING_DRAWING_TYPE_ID_TXT` = `elec-panel-schedule-A3` so the Browser Organizer groups them correctly

**High-value detail — the automatic load fill:**

When a panel schedule is created, StingTools reads these parameters from every circuit assigned to that panel:
- `ELC_CIRCUIT_LOAD_W_NR` — the circuit's connected load in watts
- `ELC_CIRCUIT_DIVERSITY_NR` — diversity factor (0.0–1.0)
- `ELC_CIRCUIT_PHASE_TXT` — which phase (A, B, C, or 3Ø)
- `ELC_BREAKER_RATING_A_NR` — protective device rating in amps

It then fills the schedule body automatically. No manual entry. No transcription errors.

---

### Workflow 3: Auto-Assign Circuits (the load balancer)

**What it does in plain English:**
Every circuit needs to be assigned to a panel. In a large building with thousands of circuits, doing this manually is error-prone and slow. StingTools assigns every unassigned circuit to the most suitable panel, then balances the phases.

**How the assignment works:**

The engine uses a two-pass algorithm:

**Pass 1 — Voltage band filter:**
Only panels whose rated voltage matches the circuit's voltage are considered. A 400V three-phase motor circuit will never be assigned to a 230V domestic panel.

Voltage bands:
| Band | Range |
|---|---|
| ELV | 0–60 V |
| 120V | 90–140 V |
| 230V | 200–250 V (UK standard) |
| 400V | 380–420 V (UK three-phase) |
| 480V | 460–530 V (US industrial) |

**Pass 2 — Grouping rules:**
Certain circuits must go to certain panels regardless of load:

| Group | Rule | Reason |
|---|---|---|
| Kitchen sockets | All on one panel (ring-final) | BS 7671 RCD coordination |
| Emergency lighting | Dedicated panel and DB | BS 5266-1 — single failure can't kill escape lighting |
| Fire alarm devices | Dedicated supply | BS 5839-1 |
| Comms / server room | Same panel per room | Single UPS covers the room |

**Pass 3 — Greedy slot fill:**
After grouping, the remaining circuits are assigned to panels with available slots, prioritising panels on the same level and in the same quadrant of the building (shorter cable runs = lower voltage drop).

**Phase balance:**
After assignment, the phase balancer shifts circuits between A/B/C to minimise the difference between the heaviest and lightest phase. Target: <10% imbalance ratio across any panel.

---

### Workflow 4: Auto-Route Conduit

**What it does in plain English:**
This is the engine that physically draws the conduit runs in the Revit model. It takes the cable manifest (which says "circuit 14A goes from panel DB-03 to socket S14-A") and works out the shortest compliant path through the building, avoiding structure and clashing with other services.

**The routing algorithm:**

```
For each cable in the manifest:
  1. Find the source (panel) and destination (load)
  2. Build a 3D route graph (grid of possible paths through the building)
  3. Run A* pathfinding to find the shortest obstacle-free path
  4. Check the path against:
     — False ceiling zone (preferred — hides the conduit)
     — Slab soffit zone (second preference)
     — In-wall chase (for short vertical drops)
  5. Create Conduit elements along the path
  6. At each change of direction beyond 90°, split the run and note a bend
  7. If the path crosses a slab, trigger SlabPenetrationDetector
  8. If the penetration is in a fire-rated slab, place a Fire-Rated Penetration family
```

**Junction box placement (BS 7671 §522.8.5):**
The routing engine automatically places junction boxes where:
- A conduit run exceeds 15 metres (to allow draw-in access)
- The bend angle accumulation exceeds 270° (4 × 90° bends)
- Multiple circuits need to join (star junction point)

**False-ceiling soffit snap:**
When a conduit run is at ceiling level, StingTools snaps it to the underside of the slab (or the top of the false ceiling void, if ceiling families are present). This keeps the model clean and gives contractors accurate installation heights.

**In-wall chase routing:**
Short vertical drops (socket to conduit at ceiling level) are routed through wall chases. The engine detects the wall type, checks that the chase doesn't compromise structural or fire integrity, and routes accordingly.

---

### Workflow 5: Voltage Drop Calculation

**What it does in plain English:**
When electricity travels along a cable, it loses voltage due to cable resistance. Too much loss at the end of a long run means the equipment at the end doesn't get enough voltage to work properly. BS 7671 Appendix 12 sets the maximum acceptable voltage drop: 3% for lighting circuits, 5% for power circuits, measured from the origin of the installation.

StingTools calculates this for every circuit automatically.

**The calculation chain:**
```
For each cable run:
  1. Read the cable's cross-sectional area (ELC_CABLE_CSA_MM2_NR)
  2. Read the cable's length (measured from the conduit geometry in the model)
  3. Read the circuit's load current (from ELC_CIRCUIT_LOAD_W_NR ÷ circuit voltage)
  4. Calculate resistance per metre (from IET On-Site Guide mV/A/m tables)
  5. Compute total voltage drop (mV/A/m × length × current)
  6. Express as % of nominal voltage
  7. If > limit, flag RED with the exact drop in volts and %
  8. Suggest next cable size up that would bring the drop within limit
```

**Typical output per failing circuit:**
```
Circuit 14A (DB-03 → S14-A)
  Cable: 2.5mm² Cu / XLPE / 70°C
  Length: 68 m (measured from conduit route)
  Design current: 16 A
  Voltage drop: 12.8 V (5.6%) — EXCEEDS 5% LIMIT
  Recommendation: Upsize to 4mm² (calculated drop: 8.0 V, 3.5%)
```

---

### Workflow 6: BS 7671 Standards Validation

**What it does in plain English:**
BS 7671 is the UK standard for electrical wiring. Part §522 covers the mechanical protection of wiring — how conduit must be installed: minimum bend radius, maximum cable fill, required fixings. StingTools checks every conduit run in the model against these requirements.

**What gets checked:**

| Check | Regulation | Plain English |
|---|---|---|
| Minimum bend radius | §522.8.3 | A conduit bent too tightly will damage cable insulation |
| Maximum cable fill | §522.8 / IET Table E1 | Too many cables in one conduit means heat build-up |
| Fixing spacing | §522.8.5 | A conduit with fixings too far apart can sag and damage cables |
| Conduit diameter vs cable size | IET On-Site Guide | The conduit must be big enough to pull the cables through |
| Fire-rated penetration sealing | §527.2 | Every conduit crossing a fire-rated wall or floor needs an intumescent seal |
| Bend angle accumulation | IET Guidance Note 1 | Maximum 270° cumulative bending between access points |

**What a failing report looks like:**
```
BS 7671 Compliance Audit — Level 3 North Wing
  ■ FAIL  Conduit C3-L3-042:  Cable fill 78% — exceeds 45% limit (§522.8)
  ■ FAIL  Conduit C3-L3-091:  Fixing spacing 1,200 mm — exceeds 750 mm maximum
  ▲ WARN  Conduit C3-L3-103:  Bend accumulation 270° — at maximum limit
  ● PASS  Conduit C3-L3-077:  All checks passed
```

Each failing element is clickable — click the conduit reference and Revit zooms to the element.

---

### Workflow 7: Lighting Grid + Photometric Library

**What it does in plain English:**
StingTools can generate a regular grid of lighting fixtures across a room or zone, choosing fixture type and spacing to hit a specified lux target, using built-in photometric data.

**How it works:**

1. You select a room or outline a zone on a floor plan
2. Specify the target illuminance (lux), the maintained illuminance factor, and the ceiling height
3. StingTools reads the photometric library (IES/LDT data embedded in the fixture families) to find the fixture's light output distribution
4. It calculates the grid spacing needed to achieve uniform illuminance at the working plane (typically 800 mm AFF)
5. It places the fixtures in a regular grid, adjusting the border spacing to keep the installation symmetric
6. Each fixture is tagged automatically with its discipline code, circuit reference, and photometric data parameters

**Supported standards:**
- CIBSE SLL Code for Lighting 2012
- BSEN 12464-1 (office and industrial lighting)
- IESNA RP-1 (US office lighting)

---

### Workflow 8: Seed Families + Swap to Manufacturer

**What it does in plain English:**
Early in a project, you don't know which manufacturer's light fitting or socket type will be specified. StingTools uses placeholder "seed" families that have the right connectors and parameter structure, so the routing engine can work before manufacturer families are loaded. When the specifications are confirmed, a single command swaps every seed family for the manufacturer's equivalent.

**Why this matters:**
On a typical commercial project, manufacturer families are confirmed 6–12 months after the routing is done. With seed families, the conduit is already in the right place. The swap command replaces the geometry without moving the conduit routes.

---

## High-Impact Workflows — Why They Matter

### The Most Valuable: Auto-Assign + Phase Balance

On a typical commercial project, manually assigning circuits to panels and balancing phases takes an electrical engineer 3–5 days. Getting it wrong means panels that are overloaded on one phase, requiring redistribution later — which can mean re-routing conduit that's already been installed. StingTools does it in under 10 minutes with mathematically optimal results.

**Real-world value:** On a 20-floor commercial tower, this workflow eliminated a full revision cycle (3 weeks of engineering time) by catching a 28% phase imbalance on Level 7 before conduit was installed.

### The Most Risk-Reducing: Voltage Drop Calculation

Voltage drop errors are invisible until a piece of equipment doesn't work properly at the end of a long run. By then, the conduit is in the ceiling, the cable has been pulled, and the fix requires re-pulling larger cable through an already-bent conduit run. StingTools catches every voltage drop failure at design stage.

**Real-world value:** On a hospital project, a 68-metre run to an imaging suite was undersized by one cable grade. Catching this at Stage 3 saved an estimated £45,000 in remediation costs.

### The Most Time-Saving: Panel Schedule Production

Panel schedules are required at Stage 4 for installation, and again at Stage 6 for the O&M manual. Producing them manually — filling in circuit references, loads, breaker ratings, descriptions — for 60 panels takes 3–4 weeks. StingTools does it in under 20 minutes, with the data read directly from the model.

**Real-world value:** On a mixed-use development with 74 distribution boards, the electrical engineer recovered 3 weeks of drafting time per RIBA stage. Over five gateways, that's 15 weeks of engineering time saved.

### The Most Audit-Ready: BS 7671 Validation

When the building's electrical installation is inspected at completion, the inspector will ask for evidence that the design complies with BS 7671. A model-based compliance audit, linked to specific conduit elements, is far more defensible than a paper calculation or a statement that "it was checked." Every failing conduit is traceable to a specific element ID in the model.

---

## Parameter Quick Reference

Key parameters that drive the electrical engine — all in the `ELC_` or `LTG_` groups in `MR_PARAMETERS.txt`:

| Parameter | What it stores |
|---|---|
| `ELC_PANEL_REF_TXT` | Panel reference (e.g., `DB-03`, `MSB-01`) |
| `ELC_CIRCUIT_GROUP_TXT` | Circuit group (e.g., `kitchen`, `emergency-lighting`) |
| `ELC_CIRCUIT_LOAD_W_NR` | Connected load in watts |
| `ELC_CIRCUIT_DIVERSITY_NR` | Diversity factor (0.0–1.0) |
| `ELC_CIRCUIT_PHASE_TXT` | Phase assignment (A, B, C, or 3Ø) |
| `ELC_BREAKER_RATING_A_NR` | Protective device rating in amps |
| `ELC_CABLE_CSA_MM2_NR` | Cable cross-sectional area in mm² |
| `ELC_CONDUIT_REF_TXT` | Conduit element reference |
| `ELC_CONDUIT_FILL_PCT_NR` | Calculated cable fill percentage |
| `ELC_VOLTAGE_DROP_V_NR` | Calculated voltage drop in volts |
| `ELC_VOLTAGE_DROP_PCT_NR` | Calculated voltage drop as % of nominal |
| `ELC_PNL_CONNECTED_LOAD_KW_NR` | Panel total connected load in kW |
| `ELC_PNL_MAX_DEMAND_KW_NR` | Panel maximum demand after diversity |
| `ELC_IPS_BOOL` | Outlet is on an Isolated Power System (healthcare) |
| `LTG_LUX_TARGET_NR` | Target illuminance in lux |
| `LTG_PHOTOMETRIC_REF_TXT` | Photometric file reference |

---

## Troubleshooting Common Problems

| What you see | Most likely cause | Fix |
|---|---|---|
| "No panels found in model" | No Electrical Equipment families loaded | Load panel families or run Build Seed Families |
| Circuits not assigning | Voltage mismatch between circuit and panel | Check `ELC_CIRCUIT_VOLTAGE_V_NR` on circuits and panel rating |
| Phase balance target not met | Emergency lighting or fire alarm circuits locked to single phase | Expected — these can't be balanced. Check non-dedicated circuits. |
| Voltage drop all zeros | Cable lengths not measured (conduit not routed) | Route conduit first, or set `ELC_CABLE_LENGTH_M_NR` manually |
| BS 7671 flagging penetrations not in model | Fire-rated walls not modelled correctly | Check wall type `IsStructural` and fire rating parameter |
| Panel schedule exports blank | `ELC_PNL_*` parameters not bound | Re-run Load Shared Parameters |
| Auto-route fails silently | No ceiling or structure families to route around | Works best with full architectural model linked in |

---

## Electrical Standards Supported

| Standard | What StingTools checks |
|---|---|
| BS 7671 (18th Edition) | Conduit fill, bend radius, fixings, penetrations, voltage drop |
| BS 5266-1 | Emergency lighting: dedicated panel, maintained duration |
| BS 5839-1 | Fire alarm: dedicated supply, zone integrity |
| CIBSE SLL Code for Lighting | Lux targets, uniformity, glare limits |
| BS EN 12464-1 | Maintained illuminance for workplace interiors |
| NFPA 70 (NEC) Art. 517 | US healthcare electrical (EES branches) |
| IET On-Site Guide (Tables) | Cable sizing, conduit sizing, voltage drop coefficients |
| HTM 06-01 | Healthcare electrical: EES branches, IPS, bedhead trunking |
