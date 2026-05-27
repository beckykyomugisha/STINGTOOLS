# StingTools Healthcare Workflows — Plain-English Guide

> **Who this guide is for:** BIM coordinators, project architects, mechanical and electrical engineers, facilities managers, and clinical planners who need to get value from StingTools' Healthcare Pack without reading a stack of NHS technical memoranda first.

---

## What Is the Healthcare Pack?

StingTools is a Revit plugin that automates BIM coordination for construction projects. The **Healthcare Pack** is a specialist layer built on top — it adds the rules, checks, drawings, and workflows that hospitals, clinics, mental health units, imaging centres, and care homes need but that a standard BIM tool never knows about.

Think of it this way: a regular Revit project knows that a room has walls, a floor, and a door. The Healthcare Pack teaches Revit that an operating theatre also needs a **positive-pressure cascade** through an anteroom, a **medical gas manifold** with specific zone valve positions, **ultra-clean ventilation** at 25 air changes per hour, and lead-lined walls if imaging equipment is nearby — and it checks all of this automatically.

---

## The Big Picture: How Healthcare Workflows Fit Together

Every Healthcare workflow follows the same five-stage lifecycle:

```
1. SET UP          →  Load parameters, choose facility profile
2. POPULATE DATA   →  Fill room data sheets, tag all clinical assets
3. VALIDATE        →  Run automated compliance checks
4. ISSUE DRAWINGS  →  Produce Room Data Sheets, schematics, schedules
5. HAND OVER       →  COBie export, maintenance records, sign-offs
```

You don't have to do all five stages at once. Most teams run Stage 3 (Validate) every morning during design, Stage 4 (Issue Drawings) at each RIBA gateway, and Stage 5 (Hand Over) at practical completion.

---

## Facility Profiles — Choosing the Right Mode

Before running any Healthcare workflow, StingTools asks which type of facility you're designing. This **profile** controls which validators fire and which drawing types are produced:

| Profile | What it covers | Validators active |
|---|---|---|
| **FULL** | Large acute hospital | All 16 validators |
| **ACUTE** | General district hospital | 14 validators (all except USP pharmacy) |
| **COMMUNITY** | Health centre, GP surgery | 7 validators (supply, drainage, water, adjacency, fire) |
| **DENTAL** | Dental practice or clinic | 4 validators (gas, water, drainage, RDS completeness) |
| **IMAGING-ONLY** | Stand-alone diagnostic centre, PET/CT, MRI | 4 validators (radiation, structural, acoustic, RTLS) |
| **MENTAL-HEALTH** | CAMHS, PICU, adult acute mental health | 5 validators (anti-ligature, adjacency, water, acoustic, fire) |

**How to set it:** Go to BIM tab → Project Setup → Healthcare Profile, or open `_BIM_COORD/healthcare_config.json` and set `"facilityType"` to one of the values above.

---

## Core Concepts You Need to Know

### Room Classes
StingTools assigns every room a **clinical room class** from the NHS Activity DataBase (ADB). This is the single most important piece of data in a healthcare project. It drives everything else — which validators run, which equipment gets placed, which pressure regime applies, which tag family is used.

Common room classes:

| Code | Room type |
|---|---|
| `THR` | Operating Theatre (ultra-clean) |
| `ICU` | Intensive Care Unit bed bay |
| `AIR` | Airborne Infection Isolation Room |
| `AIIR` | Airborne Infection Isolation Room (enhanced) |
| `PE` | Protective Environment (immunocompromised patients) |
| `ENS` | Ensuite patient bedroom |
| `CLN` | Clean utility room |
| `DTY` | Dirty utility room |
| `SLU` | Sluice room |
| `PSY` | Mental health / psychiatric room |
| `LAB` | Laboratory (wet or dry) |
| `RAD` | Radiology / imaging room |
| `PHM` | Pharmacy cleanroom |

Set the room class in the `CLN_ROOM_CLASS_TXT` parameter on each Room element.

### Pressure Regimes
Healthcare buildings live and breathe air pressure differentials. Positive pressure rooms push air out (protecting sterile patients from contamination). Negative pressure rooms pull air in (protecting the corridor from infectious patients). StingTools checks that every room is at the right pressure **and** that the pressure cascade through connecting anterooms is in the right direction.

| Regime | Pascal relative to corridor | Used for |
|---|---|---|
| `++` | +20 Pa | Ultra-clean theatre (from corridor via 2 anterooms) |
| `+` | +8 to +12 Pa | PE rooms, clean rooms, pharmacy |
| `0` | Neutral | General ward, offices |
| `-` | −8 to −12 Pa | Isolation rooms, dirty utility |
| `--` | −20 Pa | AIIR, TB isolation |

### Medical Gas Zones
The MGPS (Medical Gas Pipeline System) is divided into zone valves. StingTools tracks which zone each outlet belongs to, verifies NFPA 99 / HTM 02-01 flow rates, and checks that every zone valve is accessible and labelled.

### Essential Electrical Service (EES) Branches
Hospital electrical systems have three branches:
- **Normal** — standard power that goes off in a blackout
- **Life Safety** — restores within 10 seconds (exit lighting, fire alarms)
- **Critical** — restores within 10 seconds (ventilators, monitors, theatre lights)

StingTools checks that every outlet in a clinical space is on the correct branch.

---

## Workflow-by-Workflow Breakdown

---

### Workflow 1: Healthcare Commissioning Sweep

**What it does in plain English:**
This is your "does everything check out before the certificate" workflow. Run it at RIBA Stage 5 (Construction) and again at practical completion. It chains five steps: runs all validators → issues all Room Data Sheets → checks adjacency and patient flow → audits the medical gas network → produces the COBie handover pack.

**When to run it:**
- End of each design stage (abbreviated version)
- Pre-handover (full version)
- After any major design change

**Steps explained:**

| Step | What actually happens |
|---|---|
| **1. Run All Healthcare Validators** | All 16 checks fire against the model. Each room gets a Red/Amber/Green status. The result panel shows a summary with click-through to failing rooms. |
| **2. Batch Issue Room Data Sheets** | For every room with a clinical room class, StingTools renders a Word document listing all equipment, services, finishes, and regulatory parameters. One .docx per room, numbered automatically. |
| **3. Adjacency + Flow Audit** | Checks that clean and dirty flows don't cross. Patients and clean supplies should never share a corridor with used equipment, laundry, or waste — the model must prove this. |
| **4. MGPS Network Audit** | Verifies medical gas pipe sizes, zone valve counts, flow diversity, terminal unit labels, alarm panel coverage, and NFPA 99 §5.1.12 / HTM 02-01 compliance. |
| **5. COBie Export with Healthcare Overlay** | Produces a full COBie spreadsheet with 50 clinical equipment types, 16 systems, and 35 SFG20-Healthcare maintenance schedules pre-populated. |

**What you get at the end:**
- RAG dashboard (pass/fail per room)
- One Word RDS per clinical room
- MGPS compliance report
- COBie .xlsx ready for the FM team

---

### Workflow 2: Pressure Cascade Audit (HTM 03-01)

**What it does in plain English:**
Checks that every room is at the right air pressure and that the pressure waterfall flows in the right direction through any anterooms connecting it to the corridor.

**The problem this solves:**
An operating theatre that opens directly onto a corridor — with no anteroom in between — fails HTM 03-01 no matter how good the ventilation is. This workflow finds every room where the pressure cascade path is broken, mislabelled, or simply missing.

**Steps:**

| Step | What actually happens |
|---|---|
| **1. Pressure Regime Validator** | Reads `CLN_PRESSURE_REGIME_TXT` on every clinical room. Checks that each room's target Pa differential matches its room class. Checks that connecting anterooms form a valid cascade chain. |
| **2. Adjacency Validator** | Confirms that anterooms physically sit between the high-pressure room and the corridor, and that no high-pressure room shares a door directly with a negative-pressure room without an anteroom in between. |

**Output:** Per-room pressure map, list of broken cascade chains with room names and numbers, colour-coded floor plan view.

**Common failures and what they mean:**

| Failure message | What it means in plain English |
|---|---|
| "No anteroom between theatre and corridor" | The theatre door opens straight into the main corridor. Add an anteroom. |
| "Pressure direction reversed: clean to dirty" | The clean zone is blowing air toward the dirty zone. Recheck the AHU supply/extract balance. |
| "Anteroom pressure undefined" | The anteroom room class hasn't been set — the validator can't check the cascade. |
| "Dead-end anteroom" | The anteroom has no second door connecting it to the protected room. |

---

### Workflow 3: Room Data Sheet Issue Sweep (RDS Issue)

**What it does in plain English:**
Room Data Sheets are the documents that tell contractors, facilities managers, and clinical staff exactly what is in every room: every socket, every gas outlet, every tap, every light fitting, every piece of fixed equipment, every finish, every regulatory reference. In a 500-bed hospital there may be 2,000 of them. StingTools generates them automatically.

**Steps:**

| Step | What actually happens |
|---|---|
| **1. Refresh All Tokens** | Re-derives spatial data (room name, number, department, level) and writes it to every element in the room. Ensures the RDS has current data. |
| **2. Batch Render Room Data Sheets** | Opens the `healthcare_rds.docx` Word template, substitutes every `{{token}}` with live model data, and saves one file per room to `_BIM_COORD/generated/`. |
| **3. RDS Completeness Validator** | Checks that every mandatory RDS field is populated. Mandatory fields include: room class, pressure regime, ACH rate, MGPS outlets (if clinical), EES branch, fire compartment, and finishes specification. Any room with gaps goes Red. |

**What you get at the end:**
- One Word document per room in `_BIM_COORD/generated/YYYYMMDD_RDS_<room-number>.docx`
- A completeness report showing which fields are empty on which rooms

**Why this matters:**
On a hospital project, incomplete RDS documents at Stage 4 can delay contractor tender by weeks. Running this workflow catches missing data in minutes.

---

### Workflow 4: Annual HTM 04-01 Water Safety Review

**What it does in plain English:**
Water in hospitals can kill — Legionella, Pseudomonas aeruginosa, and scalding are the main risks. HTM 04-01 is the NHS standard that controls this. This workflow checks every water point in the model against its requirements.

**What it checks:**

| Check | Standard | What a failure means |
|---|---|---|
| TMV3 thermostatic mixing valves on all augmented-care outlets | HTM 04-01 Part B | A tap in a clinical area doesn't have a blending valve — scalding risk |
| Dead-leg length ≤ 2 litres | HTM 04-01 Part A | A branch pipe too long to flush out — Legionella trap |
| Sentinel outlets for temperature monitoring | HTM 04-01 Appendix 2 | No designated test points — can't prove the system is safe |
| Reverse-osmosis loop integrity | HTM 04-01 / renal unit addendum | The RO loop for dialysis or decon has a gap |

**Output:** Per-tap report, colour-coded plan showing safe/at-risk water points, updated Room Data Sheets with current water safety data.

**When to run:** Annually as the name says, and also after any plumbing modification.

---

### Workflow 5: MGPS NFPA 99 Verification

**What it does in plain English:**
Before a medical gas system can be commissioned, it must pass a 12-step verification procedure set out in NFPA 99 §5.1.12 (US) or HTM 02-01 (UK). This workflow walks the verifier through each step and captures the signed record.

**Steps:**

| Step | What actually happens |
|---|---|
| **1. Pre-flight Network Audit** | Checks that all pipes are modelled, all zone valves are placed and labelled, all terminal unit families are loaded and tagged. Any missing element stops the verification from proceeding. |
| **2. 12-Step Checklist** | Presents a digital checklist: purge and test, cross-connection test, pressure test, alarm test, labelling verification, flow rate measurement, zone valve test, emergency shutoff test, terminal unit test, source equipment test, signal test, final documentation. Each step records the verifier's name, date, and pass/fail. |

**Output:** A signed verification log saved to `_BIM_COORD/healthcare/mgas_verifications/<date>_MGPS_Verification.jsonl`.

---

### Workflow 6: Anti-Ligature Audit (HBN 03-01 / FGI Part 2)

**What it does in plain English:**
In mental health units, everything that could be used as an anchor point for self-harm must be ligature-resistant. This includes door handles, towel rails, coat hooks, window fittings, pipe clips, light fittings, and even toilet flush buttons. StingTools checks every element in every `PSY-*` room against the anti-ligature rule set.

**Steps:**

| Step | What actually happens |
|---|---|
| **1. Anti-Ligature Validator** | Checks `CLN_ANTI_LIG_BOOL` on every element in psychiatric rooms. Elements without the parameter set, or with a non-compliant family, are flagged. |
| **2. Behavioural Health Audit** | Applies the FGI Part 2 overlay for US projects: checks sightlines from nursing stations, door swing direction, observation window positions, en-suite hinging, and anchor-point clearances. |

**Output:** Room-by-room anti-ligature compliance report, list of non-compliant elements with their family names and element IDs (so you can select them directly in Revit).

---

### Workflow 7: HTM 01-06 Endoscope Reprocessing

**What it does in plain English:**
Every flexible endoscope must be tracked from patient to decontamination unit to storage to reuse. HTM 01-06 mandates RFID traceability. This workflow checks that every Endoscope Washer-Disinfector unit in the model is correctly tagged, zoned, and connected to the RFID system.

**Steps:**

| Step | What actually happens |
|---|---|
| **1. EWD Equipment Scan** | Finds every Washer-Disinfector family in the model, checks it has the mandatory `CLN_EWD_SERIAL_TXT`, `CLN_EWD_TYPE_TXT`, and `CLN_RFID_ASSET_TXT` parameters. |
| **2. Flow Compliance** | Checks decontamination room layout: dirty entry, clean exit, no cross-contamination path. |
| **3. RFID Coverage** | Verifies RFID reader positions cover all storage cabinets and EWD units. |

---

### Workflow 8: NFPA 110 Generator Test Log

**What it does in plain English:**
Emergency generators must be tested monthly under load. This workflow records each test, checks the generator's rated output against the connected load, and flags generators where the connected load has grown beyond capacity.

**What it checks:**
- Generator KVA rating vs current connected load
- Last test date (fail if >30 days for critical, >12 months for standby)
- Test duration (must be ≥30 minutes under ≥30% load per NFPA 110)
- Transfer switch test result

---

## The 16 Healthcare Validators — What Each One Checks

| # | Validator | Standard | Plain English |
|---|---|---|---|
| 1 | **Pressure Regime** | HTM 03-01 | Every room has the right Pa differential and cascade direction |
| 2 | **MGPS Flow** | NFPA 99 / HTM 02-01 | Gas flow rates and diversity factors are correct |
| 3 | **EES Branch** | NFPA 99 / HTM 06-01 | Every outlet is on the right electrical branch |
| 4 | **Water Safety** | HTM 04-01 | TMV3, dead-legs, sentinel outlets, RO loops |
| 5 | **Radiation Shielding** | NCRP 147 / IRR17 | Lead thickness is sufficient for the X-ray workload |
| 6 | **Adjacency** | HBN 00-01 / 00-04 | Clean/dirty flows don't cross; departments are correctly adjacent |
| 7 | **Anti-Ligature** | HBN 03-01 / FGI Pt 2 | No anchor points in psychiatric rooms |
| 8 | **RDS Completeness** | ADB | Every mandatory Room Data Sheet field is filled |
| 9 | **IoT Staleness** | Internal | Live sensor data is no more than 24 hours old |
| 10 | **Structural Loads** | BS EN 1991 | Heavy equipment (MRI, CT, robot) doesn't exceed floor capacity |
| 11 | **Acoustic** | HTM 08-01 / BS 8233 | Noise Rating targets met in clinical spaces |
| 12 | **Advanced Radiation** | NCRP 151 | LINAC vault shielding (radiotherapy) |
| 13 | **Endoscope RFID** | HTM 01-06 | EWD traceability chain complete |
| 14 | **EES Resilience** | NFPA 110 | Generator capacity not exceeded |
| 15 | **RTLS Coverage** | Internal / JCI | Real-Time Location System covers all clinical areas |
| 16 | **Waste Flow** | HTM 07-01 | Clinical waste, domestic waste, and soiled linen flows are segregated |

---

## Healthcare Drawing Types Produced Automatically

StingTools generates 22 dedicated healthcare drawing types. You don't design these from scratch — you set the room class, pressure regime, and equipment parameters, then run the batch draw command.

| Drawing type | What it shows |
|---|---|
| Room Data Sheet (A2) | Full per-room equipment, services, finishes schedule |
| Equipment Plan (A1 1:50) | All fixed clinical equipment with clearances |
| MGPS Schematic (A1 1:50) | Medical gas pipework, zone valves, terminal units |
| Pressure Regime Overlay | Colour-coded pressure cascade on the floor plan |
| EES Riser Diagram (A2) | Essential electrical supply branches |
| IPS Isolated Power System (A2) | Isolated power system for wet locations |
| Decontamination Flow (A2) | Clean/dirty/sterile flow arrows |
| Mortuary Layout | Cold store, PM suite, viewing room arrangement |
| Fire Compartment Strategy (A1 1:100) | Fire-rated walls, smoke strategy, escape routes |
| Radiation Shielding Plan | Lead lining extents, controlled/supervised areas |
| MRI Zone Plan | Zone I–IV overlay with 5G and 0.5 mT lines |
| Anti-Ligature Schedule | Per-room anti-ligature element register |
| Bedhead Trunking (A2) | HTM 06-01 bedhead trunking arrangement |
| OR RCP (A1 1:50) | Operating theatre reflected ceiling plan (ultra-clean) |
| Water Safety Plan (A1 1:100) | TMV3, sentinel outlet, RO loop positions |
| Acoustic Zoning Plan | NR target zones |
| Structural Loads Plan | Heavy equipment loadings |
| RTLS Coverage Plan | Tag reader positions and signal zones |
| Waste Segregation Plan | Clinical, domestic, soiled linen routes |
| Nuclear Medicine Plan | Hot lab, scanner room, decay store |
| Bedhead/Pendant Schedule (A2) | HTM-compliant bedhead equipment list per bay |
| Water-Safety Schedule | Per-tap TMV3, dead-leg, sentinel, sentinel-flush data |

---

## Parameter Quick Reference

Key parameters that drive Healthcare Pack logic — all live in the `CLN_CLINICAL` or `MGS_SYSTEMS` groups in `MR_PARAMETERS.txt`:

| Parameter | What it stores |
|---|---|
| `CLN_ROOM_CLASS_TXT` | Room class code (THR, ICU, AIR, PSY, etc.) |
| `CLN_PRESSURE_REGIME_TXT` | Target pressure regime (++, +, 0, -, --) |
| `CLN_ACH_TXT` | Required air changes per hour |
| `CLN_ANTI_LIG_BOOL` | Element is anti-ligature compliant |
| `CLN_DEPT_TXT` | Clinical department name |
| `CLN_CLEAN_DIRTY_FLOW_TXT` | Patient/clean/dirty corridor assignment |
| `MGS_OUTLET_TYPE_TXT` | Gas type at terminal unit (O2, N2O, Air, VAC, CO2, AGSS) |
| `MGS_ZONE_VALVE_REF_TXT` | Zone valve identifier |
| `MGS_VERIFY_DATE_DT` | Date of last NFPA 99 / HTM verification |
| `RAD_LEAD_EQUIV_MM_NR` | Lead equivalence in mm at primary beam |
| `RAD_ROOM_CLASS_TXT` | Radiation room class (CONTROLLED, SUPERVISED, UNCLASSIFIED) |

---

## Troubleshooting Common Problems

| What you see | Most likely cause | Fix |
|---|---|---|
| "Room class not set on 47 rooms" | Rooms created without the parameter loaded | Run Load Shared Parameters, then set `CLN_ROOM_CLASS_TXT` on each room |
| Pressure validator fires on non-clinical rooms | Filter not applied | Set `CLN_ROOM_CLASS_TXT` = `GEN` on offices and plant rooms |
| RDS renders with `{{CLN_ACH_TXT}}` un-replaced | Parameter is empty | Set the ACH target on the room element |
| MGPS validator passes but gas outlets missing | Families not tagged with `MGS_OUTLET_TYPE_TXT` | Run AutoTag on MGPS families or set manually |
| Anti-ligature audit finds zero PSY rooms | Room class not set | Set `CLN_ROOM_CLASS_TXT` = `PSY` on psychiatric rooms |
| Generator test workflow won't start | No generator family with `ELC_GEN_KVA_NR` | Load the STING seed generator family or set the param on existing families |

---

## Getting Started Checklist

For a new healthcare project, do these in order:

- [ ] Set facility profile in Project Setup
- [ ] Run Load Shared Parameters (binds all healthcare params)
- [ ] Set `CLN_ROOM_CLASS_TXT` on every room
- [ ] Set `CLN_PRESSURE_REGIME_TXT` on clinical rooms
- [ ] Load MGPS terminal unit families (or run Build Seed Families)
- [ ] Set `MGS_OUTLET_TYPE_TXT` on all gas outlets
- [ ] Run Pressure Cascade Audit to find any cascade breaks
- [ ] Run RDS Sweep to check data completeness
- [ ] Run Full Healthcare Commissioning Sweep at each RIBA gateway
