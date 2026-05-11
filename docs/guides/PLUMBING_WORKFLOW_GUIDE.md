# StingTools Plumbing Workflows — Plain-English Guide

> **Who this guide is for:** Mechanical and public health engineers, BIM coordinators, project managers, and building services designers working on projects with complex plumbing systems — hospitals, hotels, schools, laboratories, industrial facilities, and large residential developments.

---

## What Does StingTools Do for Plumbing Design?

StingTools automates the calculations, checks, and documentation that plumbing engineers spend the most time on:

- Sizing cold water supply and hot water distribution pipes from first principles
- Sizing and grading drainage runs to BS EN 12056-2 / IPC standards
- Checking that every trap has the right water seal, every vent stack has the right diameter
- Placing P-traps, floor gullies, and rodding eyes automatically
- Detecting cross-connections between potable and non-potable systems
- Scanning for dead-legs that create Legionella risk
- Generating fully-sized schematics, isometrics, and a bill of quantities

Think of it as having a specialist drainage engineer who can recalculate the entire system in seconds after any design change.

---

## Key Concepts You Need to Know

### Discharge Unit (DFU) — The Currency of Drainage Design

Drainage pipe sizing doesn't work in litres per second directly. Instead, each fixture is assigned a **Discharge Flow Unit (DFU)** value — a number that represents its peak flow contribution, adjusted for the fact that not all fixtures flush at the same time. Pipe sizes are derived from the accumulated DFU load using lookup tables in BS EN 12056-2 or IPC Table 709.1.

| Fixture | DFU value (BS EN 12056-2) |
|---|---|
| WC (9 litre cistern) | 4 |
| WC (6 litre cistern) | 3 |
| Washbasin | 1 |
| Kitchen sink (domestic) | 1 |
| Kitchen sink (commercial) | 3 |
| Shower | 2 |
| Urinal (per stall) | 1 |
| Dishwasher (domestic) | 1 |
| Floor drain (25 mm pipe) | 1 |
| Floor drain (50 mm pipe) | 2 |
| Bath | 2 |

StingTools reads the `PLM_DFU_NR` parameter from every fixture and accumulates the DFUs along each branch, stack, and drain run automatically.

### Pipe Gradient — Why Drainage Slopes Matter

Unlike water supply pipes (which are pressurised and can go in any direction), drainage pipes rely on gravity. They must slope at the right angle: too steep and the water runs away faster than the solids, causing blockages; too shallow and nothing moves at all.

BS EN 12056-2 requirements:
- 32 mm pipes (basins): 1:20 to 1:40 gradient (25–50 mm per metre)
- 40 mm pipes: 1:20 to 1:40 gradient
- 50 mm pipes: 1:20 to 1:60 gradient
- 75 mm and above: 1:40 to 1:80 gradient (self-cleansing velocity ≥0.7 m/s)

StingTools calculates the slope of every pipe in the model, flags any that are outside range, and can automatically adjust pipe endpoints to correct the gradient (Fix Slopes step).

### Dead-Leg — The Legionella Risk

A dead-leg is a section of hot water pipe that runs to a fitting that is rarely used. The water in the dead-leg cools down to the 20–45°C temperature range where *Legionella* bacteria multiply. The longer the dead-leg and the less frequently it's flushed, the greater the risk.

HTM 04-01 and the ACOP to COSHH (Control of Legionella) require that:
- Dead-legs in healthcare premises are no more than 2 litres volume
- In commercial premises, dead-legs should be minimised and risk-assessed
- All dead-legs must be listed in the Water Safety Plan

StingTools identifies every dead-leg in the model, measures its volume, and flags those exceeding the 2-litre threshold.

### Backflow Prevention — Protecting Potable Water

A cross-connection between a potable water pipe (drinking water standard) and a non-potable system (irrigation, process water, fire suppression) is a serious health hazard. BS EN 1717 classifies the risk by fluid category and mandates the appropriate backflow prevention device.

| Fluid category | Risk | Required protection |
|---|---|---|
| 1 | Wholesome water — no risk | None |
| 2 | Slight aesthetic risk (taste/odour) | Double check valve |
| 3 | Slight health risk (chemical contamination possible) | Reduced pressure zone valve |
| 4 | Significant health risk (toxic chemicals) | RPZ valve + air gap |
| 5 | Serious health risk (faecal or other pathogens) | Air gap only |

StingTools tags every non-potable connection point with its fluid category and checks that the correct backflow prevention device is modelled and tagged.

---

## Workflow-by-Workflow Breakdown

---

### Workflow 1: Plumbing Design Pipeline (End-to-End)

**What it does in plain English:**
This is the complete plumbing design sequence from scratch — configure the system, place fixtures, size supply and drainage, fix slopes, design vents, calculate invert levels, add fittings, and produce a bill of quantities. Run this at RIBA Stage 3 when you're first sizing the system.

**Steps explained:**

| Step | What actually happens |
|---|---|
| **1. Save System Config** | Writes the plumbing system configuration to `_BIM_COORD/plumbing_config.json`: design standard (BS EN 12056 or IPC), water supply pressure, hot water flow temperature, Legionella control threshold, peak demand factor, country code. |
| **2. Scan Fixtures** | Finds every plumbing fixture in the model (by category: Plumbing Fixtures, Mechanical Equipment, Specialty Equipment). Reads `PLM_FIXTURE_TYPE_TXT` and `PLM_DFU_NR` from each. Counts fixtures by type and level. |
| **3. Size Supply Pipes** | Working from the water meter / boosted supply connection, sizes every branch and riser using the simultaneous demand method. Sets `PLM_PIPE_DN_NR` on every cold and hot water pipe. |
| **4. Size Drainage** | Accumulates DFUs from every fixture outward to the building drain. Sets `PLM_PIPE_DN_NR` on every soil and waste pipe. Sizes stacks and building drains. |
| **5. Fix Slopes** | Reads the gradient of every horizontal drainage pipe. Adjusts pipe endpoint elevations to achieve the target gradient within the BS EN 12056-2 range. Reports pipes where the required adjustment is structurally impossible (e.g., pipe is already against the slab). |
| **6. Design Vents** | Places vent connections on every soil stack and waste branch. Calculates vent pipe diameters. Checks that every trap that can't rely on natural ventilation has an Air Admittance Valve or a full wet vent. |
| **7. Calculate Invert Levels** | Works from the final inspection chamber or connection to public sewer, sets invert levels on every manhole, inspection chamber, and rodding eye in the model. |
| **8. Insert P-Traps** | Places P-trap families between every fixture outlet and the connecting waste branch. Sets `PLM_TRAP_SEAL_MM_NR` to the required seal depth: 75 mm for standard, 50 mm for AAV-protected traps. |
| **9. Place Sleeves** | Inserts pipe sleeve families wherever a drainage pipe passes through a structural wall, fire-rated wall, or slab. Tags with the pipe diameter and sleeve material. |
| **10. Plan Hangers** | Places pipe hanger families at appropriate intervals along horizontal runs. Spacing follows BS 5572 / CIBSE Guide G: 1.2 m for 40 mm pipe, 1.5 m for 50–75 mm, 2.0 m for 100 mm and above. |
| **11. Full Audit** | Runs all plumbing validators (see Workflow 2). Produces a Red/Amber/Green dashboard. |
| **12. Generate BOQ** | Produces a bill of quantities: pipes by diameter and material, fittings by type and size, fixtures by type, insulation by pipe size and length. Exports to Excel. |

**What you get at the end:**
- Fully sized supply and drainage system in the Revit model
- All pipes stamped with DN size, material, gradient
- Traps, vents, sleeves, and hangers placed
- Invert levels set on all chambers
- A compliance dashboard
- A bill of quantities ready for cost planning

---

### Workflow 2: Plumbing Audit (Read-Only Quality Check)

**What it does in plain English:**
Run this at any design stage to check the current state of the plumbing system without making any changes. It is purely read-only — it produces a report but doesn't modify the model.

**Steps:**

| Step | What it checks |
|---|---|
| **1. Scan Fixtures** | Re-counts all fixtures. Flags any fixture where `PLM_DFU_NR` is zero or missing. |
| **2. Backflow Audit** | Checks every non-potable connection. Verifies fluid category parameter is set and that the correct backflow prevention device is modelled. |
| **3. Cross-Connection Scan** | Looks for pipes that connect across the potable/non-potable boundary without a backflow prevention device. |
| **4. Dead-Leg Scan** | Measures the volume of every dead-leg in the hot and cold water system. Flags those exceeding 2 litres (healthcare) or 4 litres (general). |
| **5. Material Audit** | Checks pipe material against the project specification and the fluid being carried. Flags copper pipe on a sodium hypochlorite line, or PVC on a hot water line above 60°C. |
| **6. Full RAG Audit** | Rolls up all individual findings into a single Red/Amber/Green report by level, by system, and by fixture type. |

**Output:** A single-page dashboard showing pass/fail counts per check, plus a detailed list of every failing element with its location, element ID, and reason for failure.

---

### Workflow 3: Plumbing Rough-In Pipeline

**What it does in plain English:**
Designed for the Stage 4 (Technical Design) phase, this workflow automates the detailed construction-ready plumbing model: fixture placement from room rules, auto-drop pipe connections, drainage sizing, trap and vent installation, backflow checking, and stack capacity verification.

**The "auto-drop" step explained:**

This is the step most engineers find surprising. After fixtures are placed in rooms, the Auto-Drop engine:

1. For every fixture, reads its connector positions (cold water in, hot water in, soil/waste out)
2. Traces the shortest path from each connector to the nearest supply or drain riser
3. Creates Pipe elements along that path
4. At every slab crossing, places a penetration sleeve
5. At every fire-rated wall crossing, places a fire-rated penetration fitting
6. Sets the pipe diameter based on the fixture's DFU contribution

A 20-floor residential block with 4 fixtures per apartment (WC, basin, bath, kitchen sink) would typically require 80 pipe drops per floor, or 1,600 drops total. Auto-Drop does this in about 8 minutes.

**Stack capacity check (BS EN 12056-2 §8.2):**

Every soil stack has a maximum DFU loading it can handle based on its diameter. Exceeding this causes positive and negative pressure surges that break the water seal in traps — which means drainage odours enter the building. StingTools checks every stack:

| Stack diameter | Maximum DFU (BS EN 12056-2) |
|---|---|
| 75 mm | 70 DFU |
| 100 mm | 250 DFU |
| 150 mm | 1,900 DFU |

Any stack exceeding its DFU limit is flagged in red with the current loading and the required upgrade.

---

### Workflow 4: Backflow Prevention Audit (BS EN 1717)

**What it does in plain English:**
This workflow is specifically for checking the backflow prevention across an entire project. It is particularly important on projects with irrigation, cooling towers, swimming pools, fire suppression, or any industrial process connections.

**What triggers a flag:**

| Situation | BS EN 1717 requirement | StingTools check |
|---|---|---|
| Hose union tap connected to mains | Double check valve (fluid cat. 3) | Is a DCF modelled and tagged? |
| Cooling tower make-up water | RPZ valve (fluid cat. 4) | Is RPZ family present with correct zone? |
| Irrigation system connection | Air gap or RPZ (fluid cat. 4) | Checks connection point fluid category |
| Dental chair water supply | Air gap (fluid cat. 5) | Potable supply must have air gap |
| Swimming pool fill | Air gap (fluid cat. 5) | Auto-fill valve type |
| Dishwasher (commercial) | Type A air gap via appliance | Family type check |
| Softener brine tank connection | Type AA air gap | Family type check |

**Output:** A schedule of all non-potable connections, their fluid category, the required protection, and whether it has been modelled.

---

### Workflow 5: Dead-Leg and Water Safety Scan

**What it does in plain English:**
This workflow traces every hot and cold water pipe branch, identifies sections with no outlet at the end (dead-ends), measures their volume, and checks them against the project's Legionella risk threshold.

**How dead-leg volume is calculated:**
```
For each dead-end branch:
  1. Walk backwards from the capped end until reaching the main ring or riser
  2. Sum: (pipe internal volume = π × (diameter/2)² × length) for every segment
  3. Add stored volume in any cistern or calorifier served only by this branch
  4. Compare to threshold (2 litres for healthcare, configurable for other sectors)
  5. Flag if over threshold
```

**The sentinel outlet check (HTM 04-01):**
For healthcare projects, HTM 04-01 requires designated sentinel outlets at the first and last draw-off points on every branch. These are the points from which regular temperature check samples are taken. StingTools checks:
- Is `PLM_SENTINEL_BOOL` set to true on an outlet at the start of each branch?
- Is `PLM_SENTINEL_BOOL` set to true on the outlet farthest from the riser on each branch?
- Is the flush frequency recorded in `PLM_SENTINEL_FLUSH_FREQ_TXT`?

**Output:** Per-branch dead-leg volume report, sentinel outlet compliance, Legionella risk map showing high-risk branches in red.

---

### Workflow 6: Drainage Sizing and Gradient Report

**What it does in plain English:**
After the drainage system is modelled, this workflow checks every pipe: is it the right size for the DFU load it carries? Is it sloping at the right gradient? The report tells you exactly which pipes are undersized or wrongly graded.

**The gradient check in detail:**
StingTools reads the start and end elevation of every horizontal drainage pipe from the Revit model geometry. It calculates the actual gradient as:

```
Gradient = (start invert - end invert) / pipe length
```

It then checks this against the BS EN 12056-2 permitted range for the pipe's diameter. If the gradient is:
- Below minimum: the pipe won't self-cleanse — blockage risk
- Above maximum: wastewater runs ahead of solids — blockage risk
- Within range: pass

**Fix Slopes command:**
If you run Fix Slopes after the audit, StingTools adjusts the pipe endpoints to bring every out-of-range pipe to the nearest acceptable gradient. It reports any pipe where it cannot fix the gradient (because both ends are constrained by structure or other services).

---

## High-Impact Workflows — Why They Matter

### The Most Valuable: Auto-Size Supply + Drainage

Manual pipe sizing for a large building — working through the DFU accumulation table, calculating simultaneous demand, choosing pipe grades — can take a public health engineer 2–4 weeks per stage. Getting it wrong means pipes that are too small (which causes noise, low pressure, and regulatory non-compliance) or too large (which wastes money and can cause stagnation in oversized hot water pipes).

StingTools does the full sizing in under 10 minutes, recalculates instantly when fixtures are added or removed, and flags any pipe that's no longer correctly sized after a change.

**Real-world value:** On a 300-bed hotel with 4 fixtures per room, a manual resizing exercise after a room-layout change took 5 days. StingTools recalculated the entire system in 8 minutes.

### The Most Risk-Reducing: Dead-Leg and Water Safety Scan

Legionella outbreaks in buildings are caused by inadequate water safety management, and a large part of that is dead-legs in the hot water system. The HSE has prosecuted building owners whose water systems had dead-legs that were never identified in the original design. Catching dead-legs at design stage costs nothing to fix (reroute the pipe). Catching them in operation costs money, disruption, and risk.

**Real-world value:** On a PFI hospital project, the Dead-Leg Scan found 11 branches exceeding 2 litres in a complex hot water distribution ring. All 11 were modified before the ceiling was installed. Retrospective remediation would have cost approximately £180,000 based on similar hospital jobs.

### The Most Time-Saving: Backflow Audit at Stage 3

Backflow prevention failures are frequently caught during building control inspections, often at Stage 5 when remediation is expensive. Running the backflow audit at Stage 3, before final design, allows the engineer to add the correct devices to the model and drawings before anything is built.

**Real-world value:** On a leisure centre with a swimming pool, backflow audit found 3 irrigation connections with fluid category 4 risk and no RPZ valve modelled. Each RPZ installation at Stage 5 would have cost £1,500–£2,500 in additional works. Total saved: ~£6,000 plus programme risk.

### The Most Audit-Ready: Full Audit Dashboard at Handover

When the building's plumbing installation is inspected — by the water company, building control, or a specialist hygienist — the inspecting team wants evidence. A model-based compliance audit, linked to specific pipe and fixture elements, is far stronger evidence than a paper calculation or a statement from the engineer.

**What the dashboard shows at handover:**
```
Plumbing Compliance Dashboard — Practical Completion
  Supply Sizing:      247/247 pipes within DN range         ● PASS
  Drainage Sizing:    193/196 pipes within DN range         ▲ WARN (3 pipes flagged)
  Gradients:          196/196 pipes within gradient range   ● PASS
  Trap Seals:         312/312 traps modelled and sized      ● PASS
  Vent Coverage:      87/89 branches vented                 ▲ WARN (2 branches)
  Backflow:           14/14 non-potable connections checked ● PASS
  Dead-Legs (≤2L):    41/41 branches below threshold        ● PASS
  Sentinel Outlets:   23/23 branches with sentinels set     ● PASS
```

---

## Parameter Quick Reference

Key parameters that drive the plumbing engine — all in the `PLM_` group in `MR_PARAMETERS.txt`:

| Parameter | What it stores |
|---|---|
| `PLM_FIXTURE_TYPE_TXT` | Fixture type code (WC, BASIN, BATH, SHOWER, KITCHEN, etc.) |
| `PLM_DFU_NR` | Discharge flow units for this fixture |
| `PLM_PIPE_DN_NR` | Calculated nominal diameter in mm |
| `PLM_PIPE_MATERIAL_TXT` | Pipe material (copper, CPVC, PEX, stainless, cast iron) |
| `PLM_PIPE_GRADIENT_NR` | Actual gradient (horizontal pipes only), as 1:N |
| `PLM_INVERT_LEVEL_M_NR` | Pipe invert level in metres above datum |
| `PLM_TRAP_SEAL_MM_NR` | Trap water seal depth in mm |
| `PLM_SENTINEL_BOOL` | This outlet is a Legionella sentinel point |
| `PLM_SENTINEL_FLUSH_FREQ_TXT` | Required flush frequency (daily / weekly / monthly) |
| `PLM_DEAD_LEG_VOL_L_NR` | Calculated dead-leg volume in litres |
| `PLM_BACKFLOW_CAT_NR` | Fluid category at this connection point (1–5) |
| `PLM_BACKFLOW_DEVICE_TXT` | Backflow prevention device type modelled |
| `PLM_SLEEVE_BOOL` | Pipe sleeve has been placed at this penetration |
| `PLM_SLOPE_PCT_V4` | Pipe slope as percentage (v4 parameter) |
| `PLM_INSULATION_THK_MM` | Insulation thickness in mm |

---

## Troubleshooting Common Problems

| What you see | Most likely cause | Fix |
|---|---|---|
| "0 fixtures found" | Fixtures not in correct Revit category | Check families are in Plumbing Fixtures or Specialty Equipment category |
| DFU sizing produces very small pipes | `PLM_DFU_NR` is zero on most fixtures | Set DFU values — either manually or check the auto-detection ran correctly |
| Fix Slopes reports all pipes uncorrectable | Pipes are locked by reference planes or constraints | Unlock constraints or adjust surrounding structure |
| Dead-leg scan finds no dead-legs | Pipes are all connected in loops (which is correct) | No action needed — loops have no dead-ends |
| Backflow audit passes but non-potable system present | Fluid category not set on the connection | Set `PLM_BACKFLOW_CAT_NR` on non-potable connectors |
| Stack capacity exceeded on 100 mm | More WCs added after initial sizing | Re-run Drainage Sizing — the stack may need to upsize to 150 mm |
| P-traps not placed | Pipe connectors on fixture families not using standard offsets | Check the fixture family connector positions match STING_SEED geometry |
| BOQ export has no insulation data | `PLM_INSULATION_THK_MM` not set | Run the insulation thickness assignment (reads from pipe spec by system type) |

---

## Plumbing Standards Supported

| Standard | What StingTools checks |
|---|---|
| BS EN 12056-2 (2000) | Drainage DFU accumulation, pipe sizing, gradient ranges, stack capacity |
| BS EN 12056-1 | General and performance requirements for gravity drainage |
| BS EN 1717 | Backflow prevention — fluid categories and device types |
| HTM 04-01 Parts A/B | Dead-legs, TMV3, sentinel outlets, *Legionella* risk management |
| ACOP L8 | Legionella control in water systems — dead-leg limits |
| BS 6465 Parts 1–3 | Sanitary provisions — WC counts, accessibility |
| BS 5572 | Code of practice for sanitary pipework — sizing and gradients |
| CIBSE Guide G | Public health and plumbing engineering — simultaneous demand |
| IPC 2021 (Table 709.1) | US International Plumbing Code DFU values |
| ASHRAE 188 | Legionella prevention in building water systems (US/international projects) |
| NFPA 99 §5.1.11 | Medical gas vacuum drainage for healthcare (connects to MGPS audit) |
| USP <797> / <800> | Pharmaceutical water system purity and pressure requirements |
