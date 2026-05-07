# STING Electrical — Layman's Guide

**For:** the BIM modeller, junior architect, or graduate who's been told "model the electrical for this project" and isn't an electrical engineer.

**This guide gets you to a credible first-pass design.** It does *not* make you an electrical engineer. Every project needs a chartered/licensed electrical engineer to sign off the final design, certify the installation, and take legal responsibility. STING does the calculations and produces the documents — the engineer reviews, adjusts, and signs.

If you're nervous about that boundary, good. Read the **"When to stop and call the engineer"** boxes throughout this guide.

---

## 1. The 60-second mental model

Electricity in a building works like water in plumbing:

| Plumbing | Electricity | What STING calls it |
|---|---|---|
| Mains water pipe entering the building | Utility supply | **Service entrance** / Ze |
| Stopcock + meter | Main switch + meter | **Main DB** (Distribution Board) |
| Branch pipes to bathrooms / kitchen | Sub-panels feeding zones | **Sub-DB** / panelboard |
| Each tap or appliance valve | Each light, socket, AC unit | **Final circuit** |
| Pipe diameter (15mm / 22mm / 28mm) | Cable size (1.5 / 2.5 / 6 / 10 mm²) | **CSA** (cross-sectional area) |
| Pressure at the tap | Voltage available at the load | **Voltage drop** |
| Pipe bursting if pressure too high | Cable melting if fault current too high | **Fault current** + **AIC** |
| Stop-tap that closes if a pipe leaks | Breaker/RCD that opens when something's wrong | **OCPD** (Over-Current Protective Device) |

If you remember this analogy, 80% of the electrical jargon makes sense.

---

## 2. The big picture: what you're actually producing

By the end of a STING-driven design you will have **eight things** that go into the design pack:

1. **Panel schedules** — one per DB, listing every circuit (like a tap list per bathroom)
2. **Single-line diagram (SLD)** — a tree showing utility → main → sub-DBs → loads
3. **Cable schedule (pull list)** — what cable runs where, what length, on which drum
4. **Equipment schedule** — every transformer, panel, UPS, generator with nameplate data
5. **Compliance audit** — proves every circuit is safe (BS 7671 / NEC)
6. **Loop calc sheets** — per-circuit working-out for the engineer to sign
7. **Initial Verification Certificate** — the legal document that gets handed over
8. **Lighting design** — fixture layout that hits the lux target per room

Plus optional extras: fault current schedule, arc-flash labels, carbon report, demand factors, etc.

You don't need to design any of this from scratch. STING walks the Revit model and computes everything — your job is to **load good inputs** and **review the outputs sensibly**.

---

## 3. Open the panel

Click **STING Tools → Electrical Center** in Revit's ribbon, or run the `ElectricalHub` command. A 7-tab dockable panel opens on the right:

| Tab | What it does | When you use it |
|---|---|---|
| **PNLS** (Panels) | Creates and audits panel schedules | Step 4 of the workflow |
| **CIRCTS** (Circuits) | Edits, balances, renumbers individual circuits | Step 5 |
| **CALCS** | Voltage drop, fault current, breaker sizing, BS 7671 audit, load + demand | Step 6 (the heart of compliance) |
| **SLD** | Single-line diagram + riser diagram | Step 7 |
| **CABLE** | Cable sizing + conduit fill | Step 8 |
| **LITE** (Lighting) | Lux estimates, LPD, emergency lighting, controls | Step 9 |
| **RPRT** (Reports) | Excel/PDF/COBie exports + pull list + equipment schedule + carbon | Step 10 |

Don't worry about the tab order — you'll use them iteratively, not top-to-bottom.

---

## 4. Before you press anything: load good inputs

Garbage in, garbage out. STING reads the Revit model. If the model is empty, every audit returns "no data". The minimum it needs:

### 4.1 Equipment placed in the model

- **Panels** (`OST_ElectricalEquipment` family instances) — at minimum: one main DB and one or more sub-DBs. Place them with the *Electrical → Equipment* tool. Set **Voltage** (e.g. 230 V or 400 V) and **Number of poles** (1 or 3) on each.
- **Loads** — lights, sockets, AC units, lifts, etc. Each can be a Lighting Fixture, Receptacle, Equipment, or Mechanical Equipment family. Each carries a kW or VA rating in its parameters.

### 4.2 Circuits wired up

A "circuit" is a path from a panel slot through a cable to a group of loads. In Revit:

1. Select your loads (e.g. all the office lights).
2. Click **Electrical → Power → Power** (creates a power circuit).
3. Right-click → **Edit Circuit** → pick a panel, assign a slot.
4. Run the circuit through space (use **Edit → Run Wire** or just leave it logical for now).

Do this until every load is on a circuit. STING can audit panels with no circuits, but every interesting calculation depends on circuits being wired.

### 4.3 The four shared parameters that matter most

STING reads custom shared parameters off circuits / panels. The four you cannot skip:

| Parameter | Where | What | Sensible default |
|---|---|---|---|
| `ELC_FEEDER_CSA_MM2` | Each circuit | Cable cross-section in mm² | Set after running the cable sizer |
| `ELC_CPC_SZ_MM` | Each circuit | Earth conductor (CPC) size in mm² | Same as feeder unless your engineer says otherwise |
| `ELC_CBL_INSULATION` | Each circuit | "PVC" / "XLPE" / "LSOH" | "PVC" for 230 V residential, "XLPE" for industrial / 400 V |
| `ELC_BREAKER_TYPE` | Each circuit | "MCB_B", "MCB_C", "MCB_D", "MCCB" | "MCB_C" is the safest default for general use |

You don't have to fill these by hand for every circuit. Most STING calculations write back the recommended value. But you do need the parameters to **exist** in the project — run **STING → Setup → Load Shared Parameters** once when you start the project.

> **Stop point #1:** if you can't get the panels and circuits wired, stop. Ask the electrical engineer to set up the SLD topology in a clean model, then resume from step 5.

---

## 5. The natural workflow (10 steps)

Do these in order. You will iterate (go back and re-run after a change), but the first pass should follow this order.

### Step 1 — Set the project basics

**On the BS 7671 expander on the CALCS tab:**
- Pick the **Earthing system**. For UK projects this is almost always `TN-C-S` (PME). Ask the engineer if you're unsure — getting this wrong invalidates every Zs calculation.

**On the LITE tab:**
- Pick the **LPD standard** (ASHRAE 90.1 for the US, Part L for the UK, custom for elsewhere).

**On the PNLS tab:**
- Make sure each panel has a **voltage** (230 V or 400 V) and a **busbar rating** (the panel's max current — typically 100/200/400/630 A).

### Step 2 — Mint panel schedules

**PNLS → Batch Schedules.** This creates a Revit Panel Schedule View for every panel that doesn't have one yet. Open one — it shows every circuit on that panel with its load, breaker rating, and slot.

If a panel says "no schedule template found," your project's Revit template is missing the standard Panel Schedule template. Ask the engineer to load `STING - 3-Phase DB` or similar.

> **Stop point #2:** if the panel schedules look mostly empty, your circuits aren't connected to panels. Go back to step 4.2.

### Step 3 — Fill spares + spaces

**PNLS → Fill Spares → All Schedules.** Empty panel slots get auto-filled with "SPARE" entries. This is cosmetic but matters for the construction electrician — empty slots are confusing.

### Step 4 — Run the load summary

**CALCS → Load Summary.** STING totals every circuit's connected load per panel, per system, per level. Open the resulting `.xlsx` and sanity-check:

- Does the main DB demand look like a sensible number of kVA for the building? (Rough rule: 80-150 W/m² for offices, 30-50 W/m² for warehouses.)
- Is anything missing? "0 kW for the kitchen" probably means the kitchen equipment isn't on circuits yet.

### Step 5 — Run voltage drop

**CALCS → Voltage Drop.** STING computes the voltage drop on every circuit using BS 7671 Appendix 4 / NEC Chapter 9. Pass = green, fail = amber/red.

The rule of thumb (BS 7671): **3% drop max for lighting, 5% max for everything else, end-to-end**.

If circuits fail:
- **Auto-Upsize Failing** suggests the next standard cable size up — review the recommendations and click Apply.
- Don't manually fudge the cable size unless you know what you're doing.

### Step 6 — Run fault current + AIC

**CALCS → Calculate Fault Levels.** STING walks the SLD top-down: utility kA → main DB → sub-DBs, attenuated by feeder cable impedance per IEC 60909.

Then **Stamp to Panels** writes the computed fault level onto each panel's `ELC_PNL_SHORT_CIRCUIT_RATING_KA` parameter. This is critical for the next step.

### Step 7 — Run the BS 7671 compliance audit (the most important step)

**CALCS → BS 7671 Compliance → Run BS 7671 Audit.** This is the single command that verifies your design meets the regulations. It checks three things per circuit:

1. **Earth fault loop impedance Zs** — if a fault happens, will the breaker trip fast enough? (Table 41.1: 0.4 s for sockets, 5 s for distribution.)
2. **Adiabatic check** — will the cable survive the fault until the breaker trips? (Pass = the cable is thick enough; fail = pick a thicker cable.)
3. **RCD recommendation** — does the circuit need a 30 mA / 100 mA / 300 mA RCD by regulation? (Sockets in dwellings, bathrooms, outdoors all need 30 mA.)

The output is a colour-coded Excel:
- **Green** — circuit is fully compliant.
- **Yellow (PASS_VIA_RCD)** — Zs check fails but adding an RCD makes it compliant.
- **Red (FAIL)** — circuit is not compliant. Cable too thin, breaker too slow, or CPC undersized.

> **Stop point #3 (the big one):** if any circuit is red, you cannot proceed. Show the audit Excel to the electrical engineer. Don't try to "fix" red rows by guessing at cable sizes — the engineer needs to decide whether to upsize the conductor, change the breaker type (B → C → D), or add an RCD.

After the engineer approves the strategy, re-run the audit and confirm everything is green/yellow before continuing.

### Step 8 — Generate the loop calc sheets

**CALCS → BS 7671 Compliance → Loop Calc Sheet.** One A4 page per circuit showing the full working-out: Ze + R1 + R2 = Zs, Zs vs Zs_max, k·S vs I·t, RCD recommendation, with a designer/checker signature block at the bottom.

These are the engineer's working papers. They go in the design pack.

### Step 9 — Run lighting

**LITE → Quick Lux (CIBSE LG10).** This is a coarse first-pass: tells you if each room is roughly hitting its lux target with the fixtures placed. The output suggests "+3 fixtures" or "−2 fixtures" per room.

Use this to iterate the fixture layout *before* you spend a day round-tripping to DIALux.

When the layout looks reasonable:
- **LITE → Control Zones.** Auto-derives lighting control zones per Part L 2021 / ASHRAE 90.1 (perimeter daylight band, occupancy sensors for offices/WCs, scene control for meeting rooms).
- **LITE → LPD.** Verifies lighting power density is below the standard's W/m² limit. Failures need either fewer fixtures, lower-wattage LEDs, or controls.
- **LITE → Emergency Audit.** Lists every emergency luminaire and warns where coverage gaps exist.

For the formal photometric report, round-trip through DIALux:
- **PHOTO → Export DIALux** writes IFC for the engineer / lighting designer to open in DIALux evo.
- They do the proper calculation, save IFC results, send back.
- **PHOTO → IFC Import** reads the results back and writes the lux/UGR/uniformity onto each room.
- **PHOTO → Design Review** scores every room against BS EN 12464-1 + flags rooms with stale results > 14 days old.

### Step 10 — Generate the design pack

**RPRT tab:**

- **PDF Report** — every panel schedule as PDF, ready to drop on a sheet.
- **Cable Pull List** — Excel with every cable's from / to / length / drum number.
- **Equipment Schedule** — Excel with every transformer/panel/UPS/generator nameplate.
- **Demand Factors** — Excel with diversity-corrected loads per panel.
- **Carbon Rollup** — annual kWh + scope-2 carbon + embodied A1-A3 + 60-year LCA.

**SLD tab:**
- **Generate** creates the single-line diagram as a Revit drafting view.
- **Export** exports it to PDF or DWG.

**CALCS → BS 7671 → Certificate** generates the BS 7671 Initial Verification Certificate (Appendix 6) with cover sheet, schedule of inspections, circuit details, and schedule of test results — all the columns the inspecting electrician fills in during commissioning.

---

## 6. The four scary buttons (and what they actually do)

These come up in conversations and you need to know what they mean:

### "Arc Flash"
A measurement of how much energy gets released if a short circuit happens at a panel — relevant for the electrician's PPE (the suit they wear when working on live equipment). STING computes it per IEEE 1584-2018 and stamps a label.

You do *not* need to interpret arc-flash numbers. The number gets printed on a sticker that goes on the panel; the electrician uses it to pick the right gloves and visor. **Just run it and let the engineer review.**

- **Elec → Arc Flash Calc** — runs the calculation
- **Elec → Boundary Circles** — draws the safe-distance circle on the plan view (visual check that nothing dangerous is too close to the panel)

### "Selective Coordination"
Checks that if a fault happens downstream, only the *nearest* breaker trips — not every breaker upstream. (You don't want a single short to take out the whole building.)

- **Elec → Selective Coord** — runs the check
- **Elec → TCC Curve Plot** — generates SVG plots showing two breakers' trip curves overlaid, for the engineer to verify by eye

### "Fault Current Schedule"
A summary table of fault levels at every panel. The engineer uses it to verify that every breaker's interrupting rating (AIC) is high enough.

### "Working Clearance"
**Bs7671 → Working Clearance** — checks that you've left 1 m in front and 750 mm at the sides of every panel, free of walls, columns, ducts, doors, etc. If the audit fails, you've put the panel somewhere the electrician physically can't service it.

---

## 7. The 10 most important glossary items

| Term | Meaning |
|---|---|
| **Circuit** | One cable serving one or more loads, protected by one breaker. |
| **Panel / DB / Panelboard** | The metal box with all the breakers in it. |
| **Feeder** | The cable from one panel to another panel. |
| **Final circuit** | The cable from a panel to the actual loads (lights, sockets, AC). |
| **CSA (mm²)** | Cable size. Bigger = more current, less voltage drop. 2.5 mm² for sockets, 1.5 mm² for lights, 6-95+ mm² for feeders. |
| **OCPD** | Over-Current Protective Device — the breaker, fuse, or RCD. |
| **MCB Type B/C/D** | Breaker speed: B trips fast (resistive), C medium (mixed), D slow (motors). C is the safe default. |
| **RCD / RCBO** | Earth-leakage breaker. 30 mA = personal protection (people), 100/300 mA = fire prevention. |
| **Zs** | Earth fault loop impedance. Lower = breaker trips faster on a fault = safer. |
| **Voltage drop %** | How much voltage you lose along the cable. 3% lighting, 5% other (BS 7671). |

---

## 8. Common mistakes (and how to spot them)

| You did | What goes wrong | How to spot it |
|---|---|---|
| Forgot to set panel voltage | Voltage drop calc returns garbage | All circuits show 100% drop or 0% drop |
| Forgot to wire a load to a circuit | Load summary misses it | Total demand looks too low |
| Wrong earthing system | Zs check passes wrongly | TT systems show very low Zs (impossible) |
| Cable inserted but `ELC_FEEDER_CSA_MM2` not set | Voltage drop / fault current / Zs all blank | Audit Excel has empty mm² columns |
| One circuit has 50 loads on it | Voltage drop fails dramatically | "Auto-Upsize Failing" suggests 95 mm² for a lighting circuit |
| Panel placed in a 600 mm corridor | Working clearance fails | Audit lists "Walls: ..." as an intrusion |
| LED fixtures with no `Initial Intensity` | Quick Lux underestimates by 100× | Every room shows 5 lx instead of 500 lx |

---

## 9. The recommended workflows (one-click batches)

Don't run individual commands — let STING chain them. From any tab, **Workflow → Run Preset** and pick one of:

| Preset | When to run | What it does |
|---|---|---|
| **ElectricalQA** | Daily during design | Audit panels → load summary → balance phases → preview breakers → VD → compliance audit → Excel |
| **ElectricalDesignReview** | Before each design issue | Photometric round-trip + LPD + emergency audit + VD re-flag + audit |
| **ElectricalSubmission** | Pre-issue gate | The full 19-step pipeline: panels → loads → fault → AIC → feeders → VD → SLD → arc-flash → all reports |
| **ElectricalPostFitOut** | After commissioning | Re-import as-built photometric IFC, refresh circuits, recompute fault, mint handover schedules |

Run the right preset, walk away for 5-10 minutes, come back and review the consolidated output. **This is the most efficient way to use STING** — the per-button workflow is for fixing a specific issue.

---

## 10. When to stop and call the engineer

You are *not* qualified to make these decisions, no matter what STING tells you:

- ❗ **Any red row in the BS 7671 audit.** The engineer decides: upsize cable, change breaker type, or add RCD.
- ❗ **Any selective-coordination failure.** Wrong choice can cascade-trip the whole building.
- ❗ **Any fault current exceeding 25 kA at any panel.** Likely needs current-limiting devices.
- ❗ **Earthing-system selection.** Especially for industrial, healthcare, or rural sites.
- ❗ **Generator / UPS sizing.** Step-loading and harmonic content are specialist territory.
- ❗ **Lightning protection (BS EN 62305).** STING doesn't do this — needs a dedicated tool (DEHN/Furse/Erico).
- ❗ **Anything described as "MV" (Medium Voltage) or above 1 kV.** Different rules entirely.
- ❗ **Final certificate sign-off.** Only a chartered engineer can put their name on the BS 7671 Initial Verification Certificate.

If in doubt, don't sign anything. Generate the audit, generate the loop calc sheets, hand them to the engineer, and let them review.

---

## 11. The minimum viable first day

If you've never used STING before and need to produce *something* by tomorrow:

1. Open the project. Check it has panels and circuits.
2. **STING → Setup → Load Shared Parameters** (once, takes 30 seconds).
3. **PNLS → Batch Schedules.**
4. **CALCS → Load Summary.**
5. **CALCS → Voltage Drop.**
6. **CALCS → BS 7671 → Run BS 7671 Audit.**
7. Open the four Excel files that came out.
8. Email them to the engineer with a one-line message: *"First-pass STING run. Please review red rows."*

That's a defensible starting point. Everything else is iteration.

---

## 12. Where the output files go

Everything STING writes goes to `<project_folder>/_BIM_COORD/electrical/`:

- `STING_BS7671_Compliance_*.xlsx` — the audit
- `STING_BS7671_LoopCalcSheets_*.xlsx` — per-circuit working
- `STING_BS7671_Certificate_*.xlsx` — the Initial Verification Certificate
- `STING_CablePullList_*.xlsx` — for the install team
- `STING_EquipmentSchedule_*.xlsx` — nameplate roll-up
- `STING_LoadDemand_*.xlsx` — diversity / spare capacity / harmonics / PFC
- `STING_QuickLux_*.xlsx` — first-pass lighting
- `STING_LightingControlZones_*.xlsx` — control schedule
- `STING_WorkingClearance_*.xlsx` — clearance audit
- `STING_ElectricalCarbon_*.xlsx` — carbon rollup
- `tcc/*.svg` — TCC coordination plots
- `ElecPDF/*.pdf` — panel schedules as PDF
- `SLD_Export/*.pdf` — SLD as PDF

These survive Revit closing. Version-control them with the project files.

---

## 13. Closing note

STING does the math. It writes the documents. It catches the obvious mistakes. It does *not* replace the electrical engineer's judgement, and it cannot certify a design.

What it gives you, as a non-engineer, is the ability to produce a defensible first-pass design, hand it over with the working clearly shown, and have a productive conversation with the engineer about what to change. That's a much better starting point than a blank Revit model and a deadline.

When in doubt: **run the audit. Look at the red rows. Send them to the engineer.** That single loop, repeated until everything is green, is how a building gets electrified safely.
