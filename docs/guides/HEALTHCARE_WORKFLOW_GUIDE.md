# STING Healthcare Workflow Guide

A practical, step-by-step guide for designing and documenting healthcare facilities in Autodesk Revit using the STING Healthcare Pack. Written for healthcare building services engineers, architects, and project BIM managers who are new to STING.

---

## How to use this guide

This guide is organised as a journey through a healthcare project — from initial setup through clinical zoning, service design, compliance validation, Room Data Sheet production, and commissioning. Each chapter builds on the one before it.

- **Bold text** marks a named STING command, button, or field.
- `Monospace text` marks a parameter name or code value.
- Stuck-points are boxed and tell you what to do when something goes wrong.
- Tables summarise reference material so you can quickly look up codes, parameters, and standards.

You do not need to read every chapter. A mechanical engineer designing medical gas systems can jump straight to Chapter 2. A mental health architect can skip to Chapter 4. The Quick Reference at the end of this guide lists every command alphabetically.

---

## Who this guide is for

This guide is written for professionals who know healthcare design but are new to STING. You do not need prior STING experience. You do need:

- A basic understanding of Autodesk Revit (placing rooms, elements, and families).
- Familiarity with the type of building you are working on (hospital, clinic, mental health unit, imaging centre, etc.).
- Access to a Revit project with the STING plugin loaded.

If you have never used STING before, read the standard STING Tagging Workflow Guide first to understand how the core tagging pipeline works. The Healthcare Pack layers clinical compliance on top of that foundation.

---

## The Big Picture

The Healthcare Pack does not replace STING's core tools. It adds a clinical compliance layer on top of them. Think of it as a specialist overlay that activates when you tell STING you are working on a healthcare facility.

Here is what the Healthcare Pack adds:

- **5 new parameter groups** capturing clinical room attributes, medical gas systems, radiation protection, clinical equipment classification, and anti-ligature requirements.
- **16 healthcare validators** that check your model against clinical standards before you issue drawings.
- **22 healthcare-specific drawing types** for Room Data Sheets, medical gas schematics, pressure regime plans, radiation shielding plans, and more.
- **8 commissioning workflow presets** that guide you through MGPS verification, pressure regime auditing, Room Data Sheet issue, and annual inspections.
- **80 healthcare-specific filters** for viewing your model by pressure regime, medical gas type, essential power branch, infection control class, radiation zone, and more.

All of STING's core tools — auto-tagging, sheet management, BIM coordination, COBie export, document issue — still apply. The healthcare layer activates on top.

### Project journey at a glance

```
1. Project Setup
   Set facility type (FULL / ACUTE / COMMUNITY / DENTAL / IMAGING-ONLY)
   Load healthcare shared parameters
        |
        v
2. Clinical Zoning
   Place rooms in Revit
   Set CLN_* parameters (room class, pressure regime, infection class)
   Run RdsCompletenessValidator to check mandatory fields
        |
        v
3. Discipline Design
   Medical gas systems (Chapter 2 — MGS_* parameters, MGPS Network Builder)
   Water safety (Chapter 4 — PLM_TMV_* parameters, HTM 04-01)
   Essential electrical (Chapter 5 — ELC_EES_BRANCH_TXT, EES branch audit)
   Anti-ligature (Chapter 6 — LIG_* parameters, mental health)
   Radiation shielding (Chapter 7 — RAD_* parameters)
   Clinical equipment (Chapter 8 — CEQ_* parameters)
        |
        v
4. Healthcare Validation
   Run RunAllHealthcareValidators
   Fix red (error) findings first, then amber (warnings)
   Get Authorising Engineer sign-offs
        |
        v
5. Room Data Sheets
   Fix any remaining RdsCompletenessValidator gaps
   Run BatchIssueRoomDataSheetsCommand
   Review generated .docx files
        |
        v
6. Commissioning
   Run WORKFLOW_HealthcareCommissioning
   Complete manual steps (pressure tests, gas tests, sign-offs)
   Export commissioning records to Planscape Server
        |
        v
7. Issue
   Export COBie healthcare package
   Issue drawings via Document Manager
   Archive commissioning records
   (See DOCUMENT_MANAGER_GUIDE.md)
```

---

## Prerequisites

### What the Healthcare Pack requires

Before you can use the Healthcare Pack, three things must be in place:

1. **Standard STING shared parameters must already be loaded.** The Healthcare Pack extends the standard parameter set — it does not replace it. If you have not already run **Load Shared Params** (STING panel > Tags tab > **Load Params**), do that first.

2. **Healthcare parameter groups must be loaded.** The five new healthcare groups (`CLN_CLINICAL`, `MGS_SYSTEMS`, `RAD_PROTECTION`, `CEQ_CLINICAL`, `LIG_BEHAVIOURAL`) are loaded in the same operation as the standard parameters. When you run **Load Shared Params** on a project with the Healthcare Pack installed, all five groups bind automatically.

3. **The facility profile must be set.** STING needs to know what type of healthcare facility you are designing so it can activate the right validators and drawing types.

> **Stuck?** If healthcare parameters do not appear in the Properties panel after loading, check that you are running a version of STING with the Healthcare Pack installed. The Healthcare Pack is identified by the presence of `CLN_ROOM_CLASS_TXT` in the STING shared parameter file. If it is missing, contact your STING administrator.

### Setting the facility profile

The facility profile is a project-level parameter stored in `PRJ_ORG_HEALTH_PACK_PROFILE_TXT`. It gates which validators, drawing types, and COBie presets are activated. A community clinic should not be required to pass LINAC vault shielding checks — the profile system prevents that.

**To set the facility profile:**

1. STING panel > BIM tab > **Project Setup** (or open Revit's Manage tab > Project Information).
2. Find the parameter `PRJ_ORG_HEALTH_PACK_PROFILE_TXT`.
3. Type one of the values from the table below.
4. Click OK.

| Profile value | Facility type | Plain English | Validators activated |
|---|---|---|---|
| `FULL` | Full acute hospital | A complete hospital including theatres, ICU, maternity, imaging, pharmacy, and mental health. Everything is activated. | All 16 healthcare validators |
| `ACUTE` | Acute hospital (no mental health) | A general hospital without a dedicated mental health unit. Anti-ligature validator is suppressed. | 15 validators (excludes AntiLigature) |
| `COMMUNITY` | Community health centre / GP surgery | A primary care facility. No theatres, no LINAC, no HBO. Simpler pressure regime and water safety checks only. | 6 validators (Pressure, Water, EES, RDS, Adjacency, Acoustic basics) |
| `DENTAL` | Dental practice or hospital dental department | Activates the dental compressed air / vacuum MGPS sub-system and HTM 01-05 decontamination flow. | 8 validators |
| `IMAGING-ONLY` | Standalone imaging centre (MRI, CT, X-ray, PET) | No wards, no theatres. Radiation, MRI zoning, structural loads, and RTLS validators. | 5 validators (Radiation, AdvancedRad, Structural, RDS, RTLS) |
| `MENTAL-HEALTH` | Mental health unit or psychiatric hospital | Dedicated anti-ligature pack, observation line-of-sight validator, and behavioural-health room types. | 10 validators (includes AntiLigature, behavioural room classes) |

> **Stuck?** If you set the profile but healthcare validators still do not run, the `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` parameter may not be bound. Open Revit's **Manage** tab > **Project Information** and confirm the parameter appears there. If it does not, re-run **Load Shared Params**.

### Healthcare parameter groups

The five healthcare parameter groups carry the clinical information that drives validators, Room Data Sheets, and COBie exports.

| Group prefix | Group name | What it covers | Number of parameters |
|---|---|---|---|
| `CLN_*` | CLN_CLINICAL | Clinical room attributes: room classification, pressure regime, infection control class, MRI zone, anti-ligature, acoustic requirements, waste class, BMS sensor references, FHIR location link | 44 parameters |
| `MGS_*` | MGS_SYSTEMS | Medical gas pipeline systems: gas type, nominal pressure, design flow rate, zone valve box reference, area alarm panel reference, brazing compliance, verification records | 13 parameters |
| `RAD_*` | RAD_PROTECTION | Radiation shielding: lead-equivalent thickness, barrier type, workload, use factor, occupancy factor, design goal, Qualified Expert sign-off | 8 parameters |
| `CEQ_*` | CEQ_CLINICAL | Clinical equipment classification: GMDN code (global medical device nomenclature), Spaulding infection tier, decontamination method, SFG20 maintenance reference | 7 parameters |
| `LIG_*` | LIG_BEHAVIOURAL | Anti-ligature and behavioural health: product rating, self-closing, pressure-release threshold, observation line-of-sight | 5 parameters |

In addition, the Healthcare Pack extends several existing parameter groups:

| Existing group | Healthcare extensions added |
|---|---|
| `ELC_*` (Electrical) | EES branch classification, ATS transfer time, IPS flag, cardiac-protected outlet, generator runtime target |
| `PLM_*` (Plumbing) | TMV type, dead-leg distance, augmented care flag, point-of-use filter, sentinel flag, flushing log reference, dialysis RO loop |
| `HVC_*` (HVAC) | HEPA filter requirement, HEPA grade, last DOP test date, outside air ACH component |
| `PRJ_ORG_*` (Project Information) | Facility type, bed count, OR count, ICU bed count, Authorising Engineers for ventilation/medical gas/water/electrical, Qualified Expert for radiation, regional HTM variant |

---

## Chapter 1 — Clinical Zoning and Space Types

### Why healthcare spaces are classified differently

In an office building, every room is broadly similar — it needs heating, cooling, lighting, and power. In a hospital, every room is fundamentally different. An operating theatre, an isolation room, an ICU bay, and a ward corridor each have different ventilation regimes, different pressure relationships to adjacent spaces, different surface finish requirements, different services connections, and different access restrictions.

STING captures this through the `CLN_ROOM_CLASS_TXT` parameter on Room elements. When you set the room class on a Revit Room, STING knows which validators to run, which default parameter values to suggest, and which standards to cross-check against.

Think of it this way: the room class is the clinical contract for that space. Setting `OR-ULTRA` on a room tells STING (and everyone reading the model) that this space must deliver laminar flow at ≥ 300 ACH, maintain +25 Pa relative to the scrub corridor, and have HEPA H14 terminal filtration. Without the room class set, STING cannot validate any of that.

### CLN_* parameters for rooms

These are the most important clinical parameters on a Room element. Set them before running any validators.

| Parameter | What it stores | Example values | Why it matters |
|---|---|---|---|
| `CLN_ROOM_CLASS_TXT` | The clinical room type classification (see the full list below) | `OR-ULTRA`, `AIIR`, `ICU`, `WARD-INPT` | Gates which validators apply and what default values are expected |
| `CLN_PRESS_REGIME_TXT` | The ventilation pressure regime for this room | `POS` (positive), `NEG` (negative), `NEUTRAL` | The pressure regime validator checks that adjacent rooms form a valid cascade |
| `CLN_PRESS_DELTA_DESIGN_PA_NR` | The design pressure difference in Pascals between this room and adjacent corridors | `25` for an OR, `15` for an AIIR anteroom | Validators flag rooms where the design delta is below the clinical minimum |
| `CLN_INFECT_CLASS_TXT` | The infection control risk class for this room | `STD` (standard), `AIIR` (Airborne Infection Isolation), `PE` (Protective Environment), `CLASS-N`, `CLASS-P` | AIIR rooms need negative pressure; PE rooms need positive pressure |
| `CLN_ANTERM_LINKED_ID_TXT` | The Revit Element ID of the anteroom that serves as an airlock for this room | The numeric element ID | Tells the pressure validator which anteroom to check |
| `CLN_MRI_ZONE_INT` | The MRI zone (1 to 4) for rooms in or adjacent to an MRI suite | `3` for the scanner room itself | RTLS anchors inside Zone 3 and 4 raise blocking errors |
| `CLN_RF_SHIELD_BOOL` | Whether this room has a radiofrequency Faraday cage (MRI) or lead shielding | `Yes` / `No` | Prevents RTLS anchors using BLE/Wi-Fi/UWB from being placed in shielded rooms |
| `CLN_LIGATURE_RES_BOOL` | Whether anti-ligature design requirements apply in this room | `Yes` / `No` | Activates the anti-ligature validator for this room |
| `CLN_LIG_RISK_LVL_TXT` | The ligature risk level | `LOW`, `MED`, `HIGH`, `VERY-HIGH` | Determines the minimum product rating required for fixtures |
| `CLN_HANDWASH_COUNT_INT` | The number of clinical handwash basins required in this room | `2` for an ICU bay | The RDS completeness validator checks this against HBN minimum counts |
| `CLN_WASTE_CLASS_TXT` | The dominant waste stream for this room | `CLINICAL`, `CYTOTOXIC`, `RADIOACTIVE`, `PHARMACEUTICAL`, `SHARPS`, `DOMESTIC` | Used by the waste flow validator to check segregation routes |

### Setting up room classifications

**Step 1.** Place rooms in the Revit model using the standard Revit Architecture tab > **Room** command. Give each room a meaningful name (for example "OR 1", "ICU Bay 4", "AIIR Room 12").

**Step 2.** Select one or more rooms. In the Properties panel, scroll to the `CLN_CLINICAL` group. You will see the healthcare parameters listed there.

**Step 3.** Fill in at minimum: `CLN_ROOM_CLASS_TXT`, `CLN_PRESS_REGIME_TXT`, and `CLN_INFECT_CLASS_TXT`. These three are mandatory for most validators.

**Step 4.** For rooms in batches of the same type (for example, all general ward rooms), use **BulkParamWrite** to set the same values across multiple rooms at once:
- STING panel > SELECT tab > **Bulk Param Write** button.
- Select the rooms you want to update.
- Choose the parameter name and value.
- Click Apply.

**Step 5.** Run **RDS Completeness Check** (STING panel > Healthcare > **RDS Completeness Check**) to see which mandatory fields are still missing.

> **Stuck?** If the `CLN_CLINICAL` group does not appear in the Properties panel after loading parameters, the Room category may not be bound to those parameters. Re-run **Load Shared Params** and confirm that Rooms are included in the category bindings.

### The clinical room type classification system

STING defines a comprehensive set of room class codes. The most important ones are listed here. The full list is stored in the `CLN_ROOM_CLASS_TXT` parameter definition.

| Room class code | Full name | Typical standards | Key design requirements |
|---|---|---|---|
| `OR-ULTRA` | Ultra-clean operating theatre | HTM 03-01, ASHRAE 170 | ≥ 300 ACH laminar flow, HEPA H14, +25 Pa, 19–24 °C, NR 40 |
| `OR-CONV` | Conventional operating theatre | HTM 03-01 | ≥ 20 ACH, +25 Pa, NR 40 |
| `OR-HYBRID` | Hybrid operating / imaging room | FGI Guidelines | ≥ 70 m², ceiling zone clearance for fluoroscopy, booms, and lights |
| `CATHLAB` | Cardiac catheterisation laboratory | HBN 12, NEC 517 | Lead curtain at table, lead-lined walls, IPS mandatory |
| `ICU` | Intensive care unit bay | HBN 09-02 | 6 ACH, neutral pressure, cardiac-protected outlets, bedhead pendant |
| `HDU` | High dependency unit bay | HBN 09-02 | Similar to ICU with slightly relaxed standards |
| `NICU` | Neonatal intensive care unit | HBN 09-03, FGI 2026 | ≥ 14 m² single room, 6 ACH, NR 30 |
| `AIIR` | Airborne Infection Isolation Room | CDC, HBN 04-01 | 12 ACH (new build), HEPA H14, –15 Pa to anteroom, –30 Pa to corridor, exhaust to outside |
| `PE-PROT` | Protective Environment room | CDC | 12 ACH, HEPA H14, +12 Pa to anteroom, monitored delta-P |
| `ANTERM` | Anteroom / airlock | HTM 03-01, CDC | Pressure buffer between AIIR/PE and corridor |
| `WARD-INPT` | In-patient ward room | HBN 04-01 | Standard ACH, natural or mechanical ventilation |
| `IMG-MRI` | MRI scanner room | HBN 06, IEC 60601-2-33 | RF Faraday cage, 5-Gauss geometry, Z1–Z4 zoning, no ferrous within Zone 3 |
| `IMG-CT` | CT scanner room | NCRP 147 | Primary and secondary shielding, QE sign-off mandatory |
| `IMG-XRY` | Plain X-ray room | NCRP 147 | Lead-lined walls, controlled area designation, IRR17 |
| `IMG-LIN` | Linear accelerator vault | NCRP 151 | Maze geometry, neutron shielding ≥ 10 MV, primary + secondary barriers |
| `IMG-PET` | PET scanner room | NCRP 151 | 511 keV two-photon shielding — significantly more than diagnostic X-ray |
| `PH-CSP-797` | Pharmacy compounding sterile area (USP 797) | USP <797> | ISO 5 primary engineering control in ISO 7 buffer, +5 Pa to anteroom |
| `PH-CSP-800` | Pharmacy hazardous drug handling area (USP 800) | USP <800> | Negative pressure C-PEC, –2.5 Pa buffer to anteroom, isolated exhaust |
| `PSY-BED` | Mental health in-patient bedroom | HBN 03-01 | Anti-ligature rated fittings, nurse call, minimum camera coverage |
| `SECL` | Seclusion room | HBN 03-01, FGI Part 2 | Anti-ligature throughout, 100% observation line-of-sight, soft furnishings |
| `HSDU-P` | HSDU packing/sterile storage room | HBN 13 | +10 Pa to corridor, separated from wash side |
| `HSDU-W` | HSDU wash/decontamination room | HBN 13 | –10 Pa to corridor, separate exhaust |
| `DECON-D` | Dirty decontamination utility | HBN 00-04 | Negative pressure, soiled linen / equipment entry point |
| `DECON-C` | Clean decontamination utility | HBN 00-04 | Positive relative to dirty |
| `MORT` | Mortuary | HBN 16 | 6 ACH, remote location, refrigeration capacity |
| `DIAL` | Dialysis station | HBN 07-02 | RO-loop tap, 1.5 m clearance per chair, 24-hour RO water |
| `ENDOSCOPY` | GI endoscopy suite | HTM 01-06, HBN 12 | Decon dirty-clean-sterile flow, RFID reader points, –10 Pa |
| `BRACHYTHERAPY` | Brachytherapy vault (sealed source radiotherapy) | NCRP 151, IRR17 | Interlocked door, dose-rate monitor mandatory, afterloading source vault |
| `TELEHEAL` | Telehealth consultation room | NHS Digital guidance | Camera mount, ≥ 500 lux face illuminance, ≥ 4 Mbps uplink, acoustic SENSITIVE class |

---

## Chapter 2 — Medical Gas Pipeline System (MGPS)

### What MGPS is

A Medical Gas Pipeline System (MGPS) is the network of copper pipework permanently installed in a hospital to deliver gases and vacuum to patient care areas. Unlike natural gas in an ordinary building — which is used for heating and cooking — medical gases are therapeutic agents delivered directly to patients. Oxygen is delivered to bedside pendants for ventilated patients. Medical vacuum removes secretions. Nitrous oxide and medical air support anaesthesia.

MGPS failure can be fatal. For this reason, MGPS is governed by strict standards (HTM 02-01 in the UK, NFPA 99 in the US), must be designed to defined pressure and flow specifications, and must pass a documented 12-step verification before it can be used clinically.

STING helps you design, document, and verify MGPS against these standards through three engines:

- **MgasNetwork** — traces the pipe network and builds a zone graph showing how every outlet connects back to the plant room.
- **MgasFlowSolver** — calculates the realistic simultaneous flow demand at each point in the network, applying diversity factors from NFPA 99.
- **MgasVerificationLog** — records the 12-step NFPA 99 verification against the model.

> **Cross-reference:** For MGPS piping modelling (placing pipes, sizing, routing), see **PLUMBING_WORKFLOW_GUIDE.md**. This chapter covers the healthcare-specific validation and documentation layer.

### The gas types in STING MGPS

| Gas code | Full name | Typical pressure | Pipe colour (BS EN 737) | Standards reference |
|---|---|---|---|---|
| `O2` | Medical oxygen | 400 kPa | White | HTM 02-01 Part A; NFPA 99 Ch.5 |
| `MA4` | Medical compressed air (400 kPa) | 400 kPa | Black + white quadrant | HTM 02-01; ISO 7396-1 |
| `MA7` | Surgical compressed air (700 kPa) | 700 kPa | Black + white quadrant | HTM 02-01 |
| `N2O` | Nitrous oxide (laughing gas — anaesthetic) | 400 kPa | Blue | HTM 02-01 Part A |
| `N2` | Surgical nitrogen (tool gas) | 700 kPa | Black | HTM 02-01 |
| `CO2` | Carbon dioxide (laparoscopic insufflation) | 400–700 kPa | Grey | HTM 02-01 |
| `HE` | Helium / heliox (respiratory therapy) | 400 kPa | Brown | Specialist — HTM 02-01 App |
| `VAC` | Medical vacuum | –400 to –600 mbar | Yellow | HTM 02-01; NFPA 99 |
| `AGS` | Anaesthetic gas scavenging (removes waste anaesthetic from theatres) | Passive or active sub-atmospheric | Lilac | HTM 02-01; ISO 7396-2 |
| `DENT` | Dental compressed air / dental vacuum | Varies | Black / Yellow | HTM 01-05 (UK dental) |

### MGS_* parameters for pipes and terminal units

Set these parameters on every MGPS pipe, terminal unit (the outlet at the bedside), zone valve box, and alarm panel in your model.

| Parameter | What it stores | Example value | Why it matters |
|---|---|---|---|
| `MGS_GAS_TYPE_TXT` | The gas type for this element | `O2`, `VAC`, `MA4` | STING uses this to colour-code elements and trace the network |
| `MGS_NOM_PRESS_KPA_NR` | Nominal operating pressure in kilopascals | `400` for oxygen | Flow solver uses this to check pressure drop |
| `MGS_DESIGN_FLOW_LPM_NR` | Design flow rate in litres per minute (free air) | `10` for a standard oxygen outlet | Flow solver sums these at each zone valve box |
| `MGS_DIVERSITY_PCT_NR` | The diversity factor for this outlet | `70` (meaning only 70% of outlets are expected to run simultaneously) | NFPA 99 diversity factors reduce the total calculated demand |
| `MGS_TU_BS5682_BOOL` | Does this terminal unit comply with BS 5682 (indexed probe pattern)? | `Yes` / `No` | The MGPS validator checks for BS 5682 compliance on all UK terminal units |
| `MGS_TU_INDEXED_BOOL` | Does this terminal unit have gas-specific NIST indexing (US standard)? | `Yes` / `No` | Cross-connection prevention — indexed probes can only accept the correct gas |
| `MGS_ZVB_REF_TXT` | The identifier of the zone valve box that controls this outlet | `ZVB-OR-1-O2` | Allows STING to group outlets by zone for flow calculations |
| `MGS_AAP_REF_TXT` | The identifier of the area alarm panel for this zone | `AAP-OR-1` | Allows STING to check that every zone valve box has a corresponding alarm |
| `MGS_PIPE_BRAZED_BOOL` | Was this pipe joint brazed under inert-gas purge (required by HTM 02-01)? | `Yes` / `No` | The MGPS validator checks brazing compliance on all copper pipe joints |
| `MGS_VERIFY_DT` | Date of NFPA 99 §5.1.12 verification | `2026-09-15` | Records when the system was formally verified |
| `MGS_VERIFY_BY_TXT` | Name of the ASSE 6030 certified verifier | `John Smith ASSE 6030` | Mandatory — STING will not mark verification as passed without a name |
| `MGS_VERIFY_PASS_BOOL` | Did the latest verification pass? | `Yes` / `No` | The commissioning workflow reads this to determine if the MGPS is ready for clinical use |
| `MGS_VERIFY_CERT_REF_TXT` | Reference number of the verification certificate | `CERT-2026-OR1-O2` | Document management link for the certificate |

### The MGPS Network Builder (MgasNetwork)

The MgasNetwork engine is STING's way of understanding how your MGPS is connected. Think of it as building a family tree: the plant room (compressors and cylinders) is the ancestor, and every bedside outlet is a descendant. STING traces the pipe connections to build this tree automatically — but only if the pipes are properly connected in Revit.

**Step 1.** Ensure all MGPS pipes and fittings are placed and connected in Revit. Every pipe must be joined to the next (no gaps), and every terminal unit must be connected to a pipe. Use Revit's **Pipe** tool under Systems > Plumbing & Piping, and set the system type to the correct MGPS system (`MGAS-O2`, `MGAS-VAC`, etc.).

**Step 2.** Set `MGS_GAS_TYPE_TXT` on all terminal units, zone valve boxes, and alarm panels.

**Step 3.** In the STING panel, go to the **BIM** tab and click **MGPS Network Audit** (or run `MgasNetworkAuditCommand`). STING will trace all `MGAS-*` systems and report:
- How many terminal units were found per gas type.
- Which terminal units could not be connected back to a plant room (orphaned outlets — these must be fixed before commissioning).
- Which zone valve boxes serve more than one alarm panel (this is not permitted under HTM 02-01).

**Step 4.** Review the network report. Fix any orphaned outlets by checking pipe connections in Revit. Re-run the audit until it reports zero orphaned outlets.

> **Stuck?** If STING reports "No MGAS systems found", check that the Revit pipe system types are named with the `MGAS-` prefix (e.g. `MGAS-O2`, `MGAS-MA4`). STING recognises systems only with that prefix. Open Revit's Manage > MEP Settings > Mechanical Settings > Pipe Settings to check system names.

### MGPS Flow Verification (MgasFlowSolver)

NFPA 99 §5.1.13 requires that medical gas systems be designed to deliver adequate flow with diversity. Diversity means that not every outlet in a hospital runs at full flow simultaneously — an ICU at 3 a.m. with 8 occupied beds does not draw the same demand as that same ICU with all 20 beds occupied and running maximum ventilation support.

The MgasFlowSolver applies published diversity factors to calculate the realistic simultaneous demand at every node in the network. A node is any junction, zone valve box, or major branch point.

**Step 1.** With the MGPS network graph built (see above), click **MGPS Flow Solver** in the STING panel BIM tab.

**Step 2.** The solver reads `MGS_DESIGN_FLOW_LPM_NR` from every terminal unit and `MGS_DIVERSITY_PCT_NR` where set. Where diversity is not set, STING applies the NFPA 99 Table 5.1.13 defaults.

**Step 3.** The solver calculates the diversified flow at each zone valve box, sums through to the branch header, and reports:
- **Green nodes**: flow capacity adequate, pressure drop within the 10% limit.
- **Red nodes**: undersized — the calculated flow exceeds the pipe capacity or the pressure drop is too high. The pipe needs to be upsized.
- **Amber nodes**: marginal — within specification but close to the limit. Flag for the Authorising Engineer to review.

**Step 4.** Resize any red-flagged pipes in Revit and re-run the solver.

> **Stuck?** If the flow solver gives negative flow values at some nodes, the network graph was not built before running the solver. Run **MGPS Network Audit** first, confirm zero orphaned outlets, then run the flow solver again.

### The 12-step NFPA 99 verification log

NFPA 99 §5.1.12 requires that every medical gas pipeline system be formally verified before first clinical use. The verification covers 12 specific tests, each of which must be witnessed, recorded, and signed by an ASSE 6030 certified verifier.

STING's `MgasVerificationLog` creates a structured record for each zone, persisted to `<project>/_BIM_COORD/healthcare/mgas_verifications/` as a JSON file per zone per date.

The 12 verification steps, in order, are:

| Step | What is tested | Pass criterion |
|---|---|---|
| 1 | Cross-connection test | No gas is present at any outlet of another gas species on the same system |
| 2 | Concentration test | Gas purity meets pharmacopoeial standard (e.g. oxygen ≥ 99.5%) |
| 3 | Particulate contamination | Particulate count at outlets meets ISO 8573-1 requirements |
| 4 | Pressure test (operational) | System maintains nominal pressure under full diversified load |
| 5 | Indexing / probe fit test | Terminal units accept only the correct indexed probe |
| 6 | Flow test | Each outlet delivers at least the minimum flow at nominal pressure |
| 7 | Labelling verification | All outlets, zone valve boxes, and alarm panels correctly labelled and colour-coded |
| 8 | Zone valve box test | Closing each ZVB isolates only the correct zone and no other |
| 9 | Area alarm panel test | Each alarm panel alerts correctly when pressure falls or rises outside limits |
| 10 | Master alarm panel test | Master alarm receives and displays all area alarm signals |
| 11 | Patient room alarm test | Visual and audible alarms at nurse call panels trigger correctly on pressure loss |
| 12 | Final documentation review | All test records, certificates, and as-built drawings are present and consistent |

**To record a verification step:**

1. STING panel > BIM tab > **MGPS Verify** (or run `MgasVerifyCommand`).
2. Select the zone to be verified from the dropdown.
3. For each step, click the step row and mark it Pass, Fail, or N/A.
4. Enter the verifier's name in `MGS_VERIFY_BY_TXT` and their ASSE 6030 certificate number.
5. Click **Save Log**. STING writes the verification record to `_BIM_COORD/healthcare/mgas_verifications/`.

**Where the log is saved:** `<project>/_BIM_COORD/healthcare/mgas_verifications/YYYYMMDD_zone-N.json` (one file per zone per verification date).

> **Important:** STING cannot mark a zone as verified without a named verifier (`MGS_VERIFY_BY_TXT` must be populated). This is by design — the standard requires a human signature.

### MGPS Workflow Preset: WORKFLOW_MgasVerification

The `WORKFLOW_MgasVerification.json` preset chains all MGPS verification steps into a managed sequence.

**To run the workflow:**
1. STING panel > BIM tab > **Workflow Presets** dropdown > select **MgasVerification**.
2. Review the step list in the dialog. Each step is shown with its automation level (Automated / Manual).
3. Click **Run**.

| Workflow step | What STING does | What you do |
|---|---|---|
| Pre-purge check | Verifies `MGS_PIPE_BRAZED_BOOL` is set on all joints | Confirm on-site purge records |
| Cross-connection test | Opens verification log, records Step 1 | Test each outlet with probe tools; mark Pass/Fail |
| Particulate test | Opens log, records Step 3 | Submit sample to laboratory; enter result when available |
| Purity test | Opens log, records Step 2 | Submit gas sample; enter purity percentage |
| Flow test | Runs MgasFlowSolver; records Step 6 | Confirm on-site flow measurements match model |
| Flow verification | Checks solver results against 10% pressure-drop limit | Review and sign |
| Write verification log | Saves complete log to _BIM_COORD; sets `MGS_VERIFY_PASS_BOOL` | Sign the log in the STING dialog |

**Sign-off:** The workflow pauses at the final step for the Authorising Engineer (Medical Gas) to enter their name and confirm. STING will not write `MGS_VERIFY_PASS_BOOL = Yes` without this confirmation.

---

## Chapter 3 — Pressure Regimes and Ventilation

### What pressure regimes mean in a hospital

Imagine air pressure as water in a plumbing system. Positive pressure pushes water out through every opening — it creates a bubble that keeps contaminants outside. Negative pressure creates a vacuum that pulls air in through every opening — it acts as a trap that keeps infectious material from escaping.

In a hospital, these principles protect patients and staff:

- **Positive pressure** (a "clean bubble"): used in operating theatres, HEPA-filtered rooms, and protective environment rooms. Air flows outward through every gap, preventing corridor dust, spores, and bacteria from entering. An immunocompromised bone-marrow transplant patient needs this protection.

- **Negative pressure** (a "containment zone"): used in airborne infection isolation rooms (AIIRs), mortuary post-mortem suites, and decontamination dirty utilities. Air flows inward through every gap, preventing infectious aerosols from escaping to the corridor. A patient with active tuberculosis or measles must be nursed in a negative pressure room.

- **Neutral pressure**: the corridor itself is typically neutral. It acts as a buffer between positive and negative spaces. A scrub corridor outside an operating theatre is neutral relative to the theatre (positive) and the wider hospital corridor.

The sequence of pressures from one side of a department to the other is called the **pressure cascade**. The cascade must be monotonic (always going in one direction). A positive operating theatre flowing to a neutral scrub corridor flowing to a neutral clean corridor is a valid cascade. An AIIR flowing from negative directly to a positive PE room without an anteroom in between is a dangerous design that STING's pressure validator will flag.

### The pressure regime values in STING

Set `CLN_PRESS_REGIME_TXT` on every Room element with one of these values:

| Value | Direction | Typical rooms | Why |
|---|---|---|---|
| `POS` | Positive (clean bubble) | Operating theatres, HEPA PE rooms, pharmacy clean rooms (USP 797), HSDU sterile pack room | Keeps contaminants out |
| `NEG` | Negative (containment) | AIIRs, mortuary post-mortem rooms, dirty decon utilities, PET hot labs | Keeps infectious material in |
| `NEUTRAL` | Neutral (buffer) | Wards, corridors, clean utilities, scrub corridors | Pressure buffer |
| `POS-ISO` | Positive isolation (enhanced positive) | Bone marrow transplant rooms, NICU single rooms | Enhanced protection for very immunocompromised patients |
| `NEG-ISO` | Negative isolation (enhanced negative) | Biosafety level 3 labs, BSL4 labs | Enhanced containment for dangerous pathogens |

### Running the Pressure Regime Audit

The Pressure Regime Audit checks that adjacent rooms form valid pressure cascades. It reads `CLN_PRESS_REGIME_TXT` on all rooms and uses the model's room boundaries (walls and doors) to identify which rooms are adjacent.

**Step 1.** Ensure `CLN_PRESS_REGIME_TXT` is set on all clinical rooms. Use **BulkParamWrite** for efficiency on large ward layouts.

**Step 2.** STING panel > Healthcare > **Pressure Regime Audit** (or run `PressureRegimeValidator` via `RunAllHealthcareValidators`).

**Step 3.** The validator produces a report showing:
- **Red findings (Errors)**: adjacent rooms with incompatible pressure regimes (for example, a positive AIIR anteroom adjacent directly to a negative AIIR — the anteroom should be negative relative to the corridor but positive relative to the AIIR, creating a cascade).
- **Amber findings (Warnings)**: pressure cascade arrows that are internally inconsistent, or AIIR rooms whose exhaust does not discharge to outside (it must not recirculate).
- **Green**: valid cascade.

**Step 4.** Fix pressure regime conflicts by:
- Adding an anteroom between incompatible spaces (most common fix).
- Correcting `CLN_PRESS_REGIME_TXT` values where the design intent was entered incorrectly.
- Setting `CLN_ANTERM_LINKED_ID_TXT` on AIIR and PE rooms to point to their paired anteroom element ID.

**Step 5.** Re-run the validator until zero red findings remain.

### WORKFLOW_PressureRegimeAudit

The `WORKFLOW_PressureRegimeAudit.json` preset chains the full pressure audit sequence:

| Step | STING action | Your action |
|---|---|---|
| Build room graph | STING traces room adjacencies via doors and corridors | Confirm room connections are correct in the model |
| Resolve cascades | STING checks monotonic cascade logic | Review report for cascade inversions |
| HVAC verification | STING cross-checks `CLN_PRESS_REGIME_TXT` against `HVC_AIR_CHANGES_PER_HR` for minimum ACH | Confirm ACH values are correctly entered |
| Produce drift report | STING generates a findings report | Sign off and issue via Document Manager |
| Push to mobile | STING pushes live pressure data to the Planscape mobile app (if IoT bridge configured) | On-site team can read live delta-P on the mobile app |

---

## Chapter 4 — Water Safety (HTM 04-01 and Legionella Control)

### Why water safety is a clinical governance issue in hospitals

Legionnaires' disease — a severe form of pneumonia caused by the bacterium Legionella pneumophila — kills approximately 10% of those infected. Legionella thrives in water systems where water is stored between 25 °C and 45 °C, where stagnant water creates "warm corners", and where pipe systems accumulate scale, rust, or biofilm that the bacteria can feed on.

For immunocompromised hospital patients — transplant recipients, ICU patients, cancer patients undergoing chemotherapy, NICU neonates — the risk is far higher than for healthy adults. A Legionella outbreak in an ICU can be catastrophic.

HTM 04-01 (Safe Water in Healthcare Premises, Parts A, B, and C) is the UK standard governing hospital water systems. It sets out the design limits, maintenance requirements, and monitoring regime. The key requirement you will interact with as a designer is the **dead-leg limit**: no part of a hot or cold water pipe serving a high-risk outlet should be more than 1 metre from the nearest flowing point.

Think of a dead leg as a cul-de-sac in a road network. Water sits in a cul-de-sac, goes stale, and harbours bacteria. The solution is to eliminate dead ends by extending the circuit or removing redundant pipe runs.

### HTM 04-01 water safety: key concepts in STING

STING tracks water safety through extensions to the existing `PLM_*` (Plumbing) parameter group. These parameters bind to plumbing fixtures (sinks, basins, showers, TMV units, outlets) and to pipe elements.

| Parameter | Where it is set | What it records |
|---|---|---|
| `PLM_TMV_TYPE_TXT` | On TMV fitting families | The type of thermostatic mixing valve: `TMV2` (domestic standard), `TMV3` (healthcare standard), `NONE` (no TMV) |
| `PLM_DEAD_LEG_M_NR` | On outlet fixtures | The estimated dead-leg distance in metres from the outlet back to the nearest flowing supply main |
| `PLM_AUGMENTED_CARE_BOOL` | On outlet fixtures | Is this outlet in an augmented-care area (ICU, NICU, transplant, haematology, burns)? `Yes` / `No` |
| `PLM_SENTINEL_BOOL` | On specific outlet fixtures | Is this a designated sentinel sampling point for monthly Legionella temperature measurement? `Yes` / `No` |
| `PLM_POU_FILTER_BOOL` | On outlet fixtures in augmented care | Is a point-of-use sterile water filter fitted? `Yes` / `No` |
| `PLM_POU_FILTER_CHANGED_DT` | On outlet fixtures with POU filter | Date the point-of-use filter was last replaced (POU filters must be changed every 3–6 months) |
| `PLM_FLUSH_LOG_REF_TXT` | On sentinel outlets | Reference to the flush log record (weekly flushing of low-use outlets prevents stagnation) |
| `PLM_DIALYSIS_RO_LOOP_BOOL` | On outlets in dialysis areas | Is this outlet connected to a reverse-osmosis loop (required for dialysis)? `Yes` / `No` |
| `PLM_DHW_TEMP_SET_C_NR` | On hot-water storage and calorifier elements | The set-point temperature of the DHW system in this zone. Must be ≥ 60 °C in storage and ≥ 50 °C at sentinel outlets |
| `PLM_CW_TEMP_MAX_C_NR` | On cold-water storage and pipe runs | The maximum cold-water temperature in this zone. Must be ≤ 20 °C at outlet |

### Setting up water safety parameters

**Step 1.** Identify your augmented care areas. These are the rooms where patients are most vulnerable:

| Room type | Augmented care status |
|---|---|
| ICU bay (`ICU`) | Always augmented care |
| HDU bay (`HDU`) | Always augmented care |
| NICU room (`NICU`) | Always augmented care |
| Burns unit (`BURNS`) | Always augmented care |
| Haematology / bone marrow transplant room | Always augmented care |
| General ward room (`WARD-INPT`) | Not augmented care unless specified by clinician |
| Day surgery recovery | Not augmented care |

**Step 2.** Select all plumbing fixtures in augmented care rooms. Use STING panel > SELECT tab > **Select by Discipline** (set to `P` for plumbing). Then filter further by room using **Select by Level** if needed.

**Step 3.** Use **BulkParamWrite** to set `PLM_AUGMENTED_CARE_BOOL = Yes` on all selected fixtures.

**Step 4.** Set `PLM_TMV_TYPE_TXT = TMV3` on all fixtures in augmented care areas. TMV3 is the higher-performance standard — it holds outlet temperature more tightly (±2 °C vs ±5 °C for TMV2) and has an anti-scald fail-safe that stops hot water flow if cold water fails.

**Step 5.** Designate sentinel points. Sentinel points are specific outlets used for regular Legionella temperature monitoring. HTM 04-01 Part C requires at minimum:
- One sentinel on the first outlet on each rising main.
- One sentinel on the last outlet on each branch (the "end of line" outlet — the most vulnerable to stagnation).

Select these outlet families and set `PLM_SENTINEL_BOOL = Yes`.

**Step 6.** For dialysis areas, set `PLM_DIALYSIS_RO_LOOP_BOOL = Yes` on all outlets in `DIAL` rooms.

> **Stuck?** If `PLM_TMV_TYPE_TXT` does not appear in the Properties panel for a plumbing fixture, the Healthcare Pack extension to the PLM group may not be loaded. Re-run **Load Shared Params**.

### Running the Water Safety Audit

**Step 1.** STING panel > Healthcare > **Water Safety Audit** (or `WaterSafetyAuditCommand`).

**Step 2.** The validator checks:

| Check | Pass criterion | Fail action |
|---|---|---|
| Augmented care outlets have TMV3 | `PLM_TMV_TYPE_TXT = TMV3` on all `PLM_AUGMENTED_CARE_BOOL = Yes` fixtures | Error: update TMV specification on the fixture. Replace TMV2 with TMV3. |
| Dead legs ≤ 1 m in augmented care | `PLM_DEAD_LEG_M_NR ≤ 1.0` on all augmented care sentinel outlets | Error: redesign the pipework to eliminate dead ends longer than 1 m. |
| DHW temperature at storage ≥ 60 °C | `PLM_DHW_TEMP_SET_C_NR ≥ 60` on calorifiers and storage vessels | Warning: set calorifier temperatures to at least 60 °C. |
| DHW temperature at sentinel outlet ≥ 50 °C | Temperature measurement ≥ 50 °C (entered during annual audit workflow) | Warning: this indicates heat loss in the distribution system. Check pipe insulation and calorifier set-point. |
| Cold water temperature ≤ 20 °C | `PLM_CW_TEMP_MAX_C_NR ≤ 20` on cold water pipe runs in warm plant spaces | Warning: check pipe routes near heat sources. Add cold pipe insulation. |
| Dialysis outlets on RO loop | `PLM_DIALYSIS_RO_LOOP_BOOL = Yes` on all outlets in DIAL rooms | Error: non-RO water cannot be used for dialysis. |
| Point-of-use filters present | `PLM_POU_FILTER_BOOL = Yes` on augmented care outlets in highest-risk rooms (NICU, transplant, burns) | Warning: POU filters reduce infection risk significantly in the highest-risk areas. |
| Sentinel points designated | At least one `PLM_SENTINEL_BOOL = Yes` outlet per floor per branch | Warning: without sentinel points, annual monitoring cannot be structured correctly. |

**Step 3.** Review the report. Fix all errors first, then review warnings with the Authorising Engineer (Water).

**Step 4.** Document findings. The Water Safety Audit produces a structured report that feeds into the Water Safety Group meeting minutes and the Legionella risk assessment.

### The annual HTM 04-01 water safety workflow

Run this annually as part of the estates maintenance programme. See the full walkthrough in **Chapter 8** under `WORKFLOW_HTM-04-01-Annual`.

> **Cross-reference:** For the underlying plumbing pipe modelling, system types, and routing design, see **PLUMBING_WORKFLOW_GUIDE.md**. This chapter covers the healthcare-specific compliance layer only.

---

## Chapter 5 — Essential Electrical Services (EES)

### What the Essential Electrical System is

A hospital cannot tolerate a power failure. Patients on ventilators, in the middle of surgery, or receiving intravenous infusions will die if power is lost for more than a few seconds. To ensure continuity of power, hospitals maintain a **Essential Electrical System (EES)** — a category of circuits that:

1. Are connected through an **Automatic Transfer Switch (ATS)** that switches from mains to generator power within 10 seconds of a mains failure.
2. Are physically separate from ordinary mains circuits, so a fault on the ordinary side does not affect the EES.
3. Are powered by **on-site generators** sized to supply all essential loads for at least 24 hours without refuelling.

The Essential Electrical System is divided into three branches:

| Branch code | Full name | What it powers | Transfer time |
|---|---|---|---|
| `LIFE-SAFETY` (LS) | Life Safety Branch | Exit lighting, emergency lighting, fire alarm, nurse call, public address, alarm systems | ≤ 10 seconds |
| `CRITICAL` (CR) | Critical Branch | Patient-care areas, OR luminaires, ICU monitoring equipment, patient-care receptacles | ≤ 10 seconds |
| `EQUIPMENT` (EQ) | Equipment Branch | HVAC for isolation rooms, sterilisers, water heaters, fixed equipment | ≤ 10–30 seconds (varies) |

In the UK, the equivalent framework is the Essential Services classification under HTM 06-01, with similar branch divisions.

In addition, two specialist circuits apply in surgical and critical care areas:

| Circuit code | Full name | Where required |
|---|---|---|
| `IPS-OR` | Isolated Power System — Operating Room | Every anaesthetising location (OR, catheter lab, certain imaging rooms) |
| `IT-CARD` | IT-type isolated system for cardiac-protected areas | ICU, CCU, NICU — any room where cardiac microshock risk must be eliminated |

### ELC_* parameters for EES classification

These parameters are set on electrical outlet families, circuit panels, and ATS units.

| Parameter | Where it is set | What it records |
|---|---|---|
| `ELC_EES_BRANCH_TXT` | On electrical outlets, circuit panels, ATS units | The EES branch classification: `LIFE-SAFETY`, `CRITICAL`, `EQUIPMENT`, `NORMAL` (not essential) |
| `ELC_ATS_TRANSFER_TIME_S_NR` | On ATS units | The measured or design transfer time in seconds. Must be ≤ 10 s for LS and CR branches |
| `ELC_IPS_BOOL` | On distribution panels and outlets in ORs and wet procedure rooms | Is this circuit on an Isolated Power System? `Yes` / `No` |
| `ELC_CARDIAC_PROT_BOOL` | On receptacles in ICU and NICU | Is this outlet in a cardiac-protected (IT-CARD) circuit? `Yes` / `No` |
| `ELC_GENERATOR_RUNTIME_HRS_NR` | On generator sets | Declared runtime on full tank of fuel in hours. Must be ≥ 24 h under NFPA 110 |
| `ELC_UPS_BATTERY_REPLACE_DT` | On UPS units | The date when the UPS battery was last replaced |

### Setting up EES classification

**Step 1.** Select all patient-care receptacles (duplex outlets, socket outlets) in patient rooms, ICU bays, operating theatres, catheter labs, and treatment rooms. Use STING panel > SELECT > **Select by Category** > Electrical Equipment.

**Step 2.** Use **BulkParamWrite** to set `ELC_EES_BRANCH_TXT` on these outlets:
- OR outlets: `CRITICAL`
- ICU outlets: `CRITICAL`
- AIIR outlets: `CRITICAL`
- General ward outlets: `CRITICAL` (patient-care area)
- Corridor exit lighting: `LIFE-SAFETY`
- Nurse call panels: `LIFE-SAFETY`
- Fire alarm devices: `LIFE-SAFETY`

**Step 3.** Set `ELC_IPS_BOOL = Yes` on all panels and outlets in `OR-ULTRA`, `OR-CONV`, `CATHLAB`, and other anaesthetising locations.

**Step 4.** Set `ELC_CARDIAC_PROT_BOOL = Yes` on all receptacles in `ICU`, `CCU`, and `NICU` rooms.

**Step 5.** Set `ELC_ATS_TRANSFER_TIME_S_NR` on all ATS units. Design value: ≤ 10 s (measured during commissioning).

### Running the EES Branch Audit

**Step 1.** STING panel > Healthcare > **EES Branch Audit** (or `EesBranchAuditCommand`).

**Step 2.** The validator checks:

| Check | Pass criterion | Fail action |
|---|---|---|
| All patient-care receptacles classified | `ELC_EES_BRANCH_TXT` set (not empty) on all outlets in clinical rooms | Error: classify all unclassified outlets. Use BulkParamWrite by room type. |
| OR circuits on CRITICAL branch | All outlets in `OR-*` rooms: `ELC_EES_BRANCH_TXT = CRITICAL` | Error: update OR circuit classification. |
| IPS declared in ORs | `ELC_IPS_BOOL = Yes` on at least one panel in each `OR-*` room | Error: add IPS panel family to OR. Set `ELC_IPS_BOOL = Yes`. |
| ATS transfer time ≤ 10 s | `ELC_ATS_TRANSFER_TIME_S_NR ≤ 10` on all LIFE-SAFETY and CRITICAL branch ATSs | Warning (pre-commissioning) / Error (after commissioning): replace slow ATS or adjust settings. |
| Cardiac-protected circuits in ICU | `ELC_CARDIAC_PROT_BOOL = Yes` on ICU and NICU outlets | Error: update ICU outlet classification. |
| Generator runtime ≥ 24 h | `ELC_GENERATOR_RUNTIME_HRS_NR ≥ 24` on all standby generators | Warning: size or fuel tank is insufficient for NFPA 110 compliance. |

**Step 3.** Fix all errors. Submit warnings for Authorising Engineer (Electrical) review.

> **Cross-reference:** For the full electrical system design workflow including circuit modelling, riser diagrams, and panel schedules, see **ELECTRICAL_WORKFLOW_GUIDE.md**. This chapter covers only the healthcare EES compliance layer.

---

## Chapter 6 — Anti-Ligature Design (Mental Health)

### What anti-ligature means

A ligature point is any object that a person could attach a rope, cord, or other material to for the purpose of self-harm. In mental health facilities, every item in patient areas must be designed, selected, and installed to eliminate ligature risks. This includes:

- Door handles (rounded, no projections above 25 mm when forced downward).
- Coat hooks (designed to fold under load below a set threshold).
- Curtain rails (continuous tension rail that releases along its full length; no end brackets).
- Shower heads (anti-ligature shower rose flush-mounted; no riser bar).
- Light fittings (enclosed, flush-mounted, no exposed brackets).
- Towel rails (anti-ligature type that collapses under load).
- Exposed pipework (lagged and boxing where possible; anti-ligature boxing).
- Bedsteads (specific anti-ligature bed designs only).
- Observation panels (flush-mounted, no frame projections).

This is not about aesthetics — it is a life-safety requirement governed by HBN 03-01 (UK) and FGI Guidelines Part 2 (US).

### When this applies

The anti-ligature validator is activated when:
- `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` is set to `MENTAL-HEALTH`, `FULL`, or `ACUTE` (where the project includes a mental health unit).
- `CLN_LIGATURE_RES_BOOL = Yes` is set on one or more rooms.

The parameters that record anti-ligature compliance are on the **fitting or fixture element**, not on the room. For example, the anti-ligature rating is set on the door handle family, the coat hook family, or the shower unit family — not on the room itself.

| Parameter | Location | What it records |
|---|---|---|
| `LIG_PRODUCT_RATING_TXT` | On the fixture/fitting element | The anti-ligature product rating (e.g. `BS 9263 LR1`, `LR2`, `LR3`, `LR4`) |
| `LIG_SELF_CLOSE_BOOL` | On door and closure elements | Whether the fitting self-closes |
| `LIG_PRESSURE_REL_BOOL` | On hooks, rails, and handles | Whether the fitting releases under load |
| `LIG_FORCE_LIMIT_KG_NR` | On hooks, rails, and handles | The load in kilograms at which the fitting releases |
| `LIG_AREA_OBS_LOS_TXT` | On room or zone elements | The observation line-of-sight code for the area |

### Running the Anti-Ligature Audit

**Step 1.** Ensure all rooms requiring anti-ligature design have `CLN_LIGATURE_RES_BOOL = Yes` and `CLN_LIG_RISK_LVL_TXT` set.

**Step 2.** Ensure all fittings in those rooms have `LIG_PRODUCT_RATING_TXT` set. Use **AutoTag** and **BulkParamWrite** to populate these from family type parameters where the family is already rated.

**Step 3.** STING panel > Healthcare > **Anti-Ligature Audit** (or run `AntiLigatureValidator` via `RunAllHealthcareValidators`).

**Step 4.** The validator reports:
- **Red (Errors)**: fittings in anti-ligature rooms with no `LIG_PRODUCT_RATING_TXT` — these must be rated before handover.
- **Amber (Warnings)**: fittings where `LIG_PRODUCT_RATING_TXT` is present but `LIG_PRESSURE_REL_BOOL` is not set.
- **Observation gaps**: rooms where the observation line-of-sight does not provide full room coverage.

**Step 5.** For each non-compliant fitting:
- Select the element in Revit.
- Set `LIG_PRODUCT_RATING_TXT` to the product's rated value (from the manufacturer's certificate).
- Set `LIG_PRESSURE_REL_BOOL = Yes` and `LIG_FORCE_LIMIT_KG_NR` if the fitting is load-release type.

**Step 6.** Re-run the audit until zero red findings remain.

> **Stuck?** If the validator reports "No anti-ligature rooms found" but you have set `CLN_LIGATURE_RES_BOOL = Yes` on rooms, check that the `LIG_BEHAVIOURAL` parameter group has been loaded. Re-run **Load Shared Params** and confirm the group appears in the parameter browser.

### WORKFLOW_AntiLigatureAudit

The `WORKFLOW_AntiLigatureAudit.json` preset:

| Step | STING action | Your action |
|---|---|---|
| Build psy-room set | STING identifies all rooms with `CLN_LIGATURE_RES_BOOL = Yes` | Confirm room list is complete |
| Load fittings | STING collects all family instances in those rooms | Confirm family placement is complete |
| Check product rating | Validator checks `LIG_PRODUCT_RATING_TXT` on every fitting | Review red findings; update missing ratings |
| Check observation LOS | Validator checks sightline coverage | Architect confirms sightlines with room layout |
| Produce risk register | STING generates an anti-ligature risk register document | Sign and issue as part of commissioning pack |

---

## Chapter 7 — Radiation Protection (Imaging and Nuclear Medicine)

### When radiation shielding applies

Ionising radiation is used for diagnosis (X-rays, CT, PET, nuclear medicine scans) and treatment (radiotherapy). The biological hazard from ionising radiation requires that rooms housing radiation sources be shielded so that the dose to staff and members of the public in adjacent spaces remains within regulatory limits.

In the UK, the Ionising Radiations Regulations 2017 (IRR17) designates spaces near radiation sources as **Controlled Areas** (where occupational workers may receive doses up to 6 mSv/year) or **Supervised Areas** (lower dose, general precautions). In the US, 10 CFR 20 governs.

STING implements the NCRP 147 calculation method for diagnostic radiology shielding. This is the most widely used method internationally, covering X-ray rooms, CT suites, fluoroscopy rooms, and cath labs.

**Important caveat before you use the calculator:** STING's radiation calculator is a planning tool. It provides a draft shielding design for review by a Qualified Expert (QE) — a medical or health physicist with formal qualifications in radiation protection. Under IRR17 (UK) and 10 CFR 20 (US), the final shielding design must be reviewed and approved by a QE. STING flags any radiation room where `RAD_QE_NAME_TXT` is empty and will not mark shielding as adequate until a QE has signed off.

### RAD_* parameters

Set these parameters on the **walls, doors, and windows** that form the boundary of a radiation-controlled room. These are element-level parameters, not room-level.

| Parameter | What it records | Example value |
|---|---|---|
| `RAD_LEAD_MM_NR` | The lead-equivalent thickness in this element (mm Pb) | `2.0` for 2 mm lead |
| `RAD_BARRIER_TYPE_TXT` | Whether this barrier is primary, secondary, or scatter | `PRIMARY`, `SECONDARY`, `SCATTER`, `LEAKAGE` |
| `RAD_WORKLOAD_MAWK_NR` | Workload in milliampere-minutes per week (the total X-ray exposure for this room per week) | `600` for a moderate-use chest room |
| `RAD_USE_FACTOR_NR` | The fraction of time the X-ray beam points at this wall (U factor) | `0.25` for a side wall, `1.0` for the primary wall behind the table |
| `RAD_OCC_FACTOR_NR` | The fraction of time the adjacent space is occupied (T factor) | `1.0` for an office, `0.05` for a car park |
| `RAD_DOSE_DESIGN_GOAL_TXT` | Whether the adjacent space is in a controlled or uncontrolled area | `CONTROLLED` (6 mSv/year) or `UNCONTROLLED` (0.3 mSv/year) |
| `RAD_QE_NAME_TXT` | The name of the Qualified Expert who reviewed and approved this shielding design | `Dr Jane Smith, MIPEM` |
| `RAD_QE_DT` | The date of the Qualified Expert's sign-off | `2026-09-20` |

Also set on the Room element:

| Parameter | What it records |
|---|---|
| `CLN_RAD_CONTROLLED_BOOL` | Is this room designated as an IRR17 Controlled Area? |
| `CLN_MRI_ZONE_INT` | For MRI suites: which zone (1 to 4) is this room in? |
| `CLN_RF_SHIELD_BOOL` | Does this room have RF shielding (MRI Faraday cage)? |

### The NCRP 147 shielding calculator

**How NCRP 147 works (in plain English):**

The NCRP 147 method calculates the thickness of shielding material needed to reduce radiation to the design dose limit in the adjacent space. It uses four inputs:

1. **Workload (W)**: how much X-ray exposure happens per week, measured in milliampere-minutes per week. A busy chest X-ray room might have W = 1,000 mA·min/week. A CT scanner runs much higher workloads than plain film.

2. **Use factor (U)**: what fraction of the time does the beam point at this particular wall? For the wall directly behind the table (the primary wall), U = 1.0. For a side wall, U = 0.25. For the floor, U = 1.0 for rooms with an X-ray table pointing downward.

3. **Occupancy factor (T)**: what fraction of the time is the adjacent space occupied? An office is T = 1.0 (occupied all day). A toilet is T = 0.05. An unoccupied storage room is T = 0.025.

4. **Design dose goal**: the maximum acceptable annual dose in the adjacent space. For uncontrolled areas (corridors, offices, public spaces) this is typically 0.3 mSv/year (the "1 mSv/year public dose limit" divided by the occupancy-adjusted fraction).

The calculator takes these four inputs and outputs the required thickness in mm of lead (Pb-equivalent). Other materials (concrete, barium plaster, gypsum board) are then converted using published transmission data.

**Running the calculation:**

**Step 1.** Select the wall, door, or window element you want to calculate shielding for.

**Step 2.** STING panel > Healthcare > **Rad Calc** (or run `RadCalcChestRoomCommand` for a plain X-ray room, `RadCalcCtRoomCommand` for a CT suite).

**Step 3.** The calculator dialog opens. Fill in the fields:
- **Source type**: select the X-ray tube energy (70 kVp, 100 kVp, 125 kVp, 150 kVp, or 200 kVp for CT).
- **Workload W (mA·min/week)**: enter the weekly workload. Your radiologist or equipment manufacturer can provide this. Typical values: chest room 1,000; fluoroscopy room 3,000; CT 40,000.
- **Use factor U**: enter the fraction appropriate for this wall. Side wall = 0.25; primary wall = 1.0; floor = 1.0; ceiling = 0.05.
- **Occupancy factor T**: enter the fraction for the adjacent space. Office, consultation room = 1.0; corridor = 0.2; outside area = 0.05.
- **Design dose goal**: select Controlled (6 mSv/year) or Uncontrolled (0.3 mSv/year).

**Step 4.** Click **Calculate**. STING outputs:
- Required mm Pb (lead-equivalent).
- Approximate concrete thickness equivalent.
- A note about barium plaster (specialist — refer to manufacturer data).

**Step 5.** STING writes the required mm Pb to `RAD_LEAD_MM_NR` on the selected element as a design note. The actual construction detail must be confirmed by the QE.

**Step 6.** Send the calculations to your Qualified Expert for review. They will confirm or revise the values and complete `RAD_QE_NAME_TXT` and `RAD_QE_DT`.

> **Stuck?** If the calculator gives no output, check that `RAD_BARRIER_TYPE_TXT` is set on the element and that a source type is selected. The calculator requires both.

### The mandatory QE sign-off

STING's `RadShieldValidator` will flag any radiation room where:
- `RAD_QE_NAME_TXT` is empty on any barrier element around the room.
- `RAD_LEAD_MM_NR` on any barrier is less than the calculated required value (i.e. the provided shielding is insufficient for the declared W·U·T inputs).

**These are blocking errors** — the commissioning workflow will not complete until the QE has signed off all radiation rooms. This is deliberate and mirrors the legal requirement under IRR17 and 10 CFR 20.

### Advanced radiation shielding (PET, nuclear medicine, brachytherapy)

For PET scanners, nuclear medicine (SPECT/gamma camera), and brachytherapy (sealed source radiotherapy), the shielding requirements are significantly different from diagnostic X-ray. PET uses 511 keV photons from positron annihilation — much higher energy than diagnostic X-ray, requiring far more concrete per equivalent Pb value. NCRP 151 governs these advanced modalities.

STING's `AdvancedRadShieldValidator` checks rooms of class `PET-HOT-LAB`, `NM-SCAN`, and `BRACHYTHERAPY`. It warns any `RAD_LEAD_MM_NR` value on a PET room that appears to have been calculated using NCRP 147 (diagnostic X-ray curves) rather than NCRP 151 (511 keV curves).

All advanced radiation shielding outputs require QE sign-off regardless of source type.

---

## Chapter 8 — Clinical Equipment (CEQ)

### What clinical equipment management covers

A large acute hospital can contain 15,000 to 40,000 pieces of clinical equipment — from MRI scanners weighing 15 tonnes to individual ECG electrodes. Each item needs to be tracked through its lifecycle: procured, installed, commissioned, maintained, calibrated, decontaminated, and eventually replaced. In the BIM model, every piece of clinical equipment should be placed as a Revit family with its services connections (power, water, gas) modelled.

STING tracks clinical equipment through a combination of the standard `ASS_*` (asset) parameters and the new `CEQ_*` (clinical equipment) parameters added by the Healthcare Pack.

### CEQ_* parameters for clinical equipment

| Parameter | What it records | Example value |
|---|---|---|
| `CEQ_CATEGORY_TXT` | High-level equipment category | `IMAGING`, `PENDANT`, `BEDHEAD`, `PHARMACY`, `DIAL`, `MORT`, `MOBIL` |
| `CEQ_GMDN_CODE_TXT` | Global Medical Device Nomenclature code — a worldwide standard identifier for medical device types | `35977` for an anaesthetic machine |
| `CEQ_UMDNS_CODE_TXT` | ECRI UMDNS code — alternative international identifier for medical devices | `11-977` |
| `CEQ_SFG20_REF_TXT` | SFG20 healthcare maintenance schedule identifier — links this equipment to its PPM schedule | `SFG20-HC-ANA-01` |
| `CEQ_CLINICAL_BOOL` | Does this item count as a clinical asset for the asset register? | `Yes` / `No` |
| `CEQ_INFECT_TIER_TXT` | Spaulding infection risk tier — determines the level of decontamination required | `CRITICAL` (must be sterilised), `SEMI-CRITICAL` (high-level disinfection), `NON-CRITICAL` (low-level disinfection) |
| `CEQ_DECON_METHOD_TXT` | The required decontamination method for this equipment | `AUTOCLAVE`, `AER-ENDO`, `WIPE-DOWN`, `SINGLE-USE` |

In addition, the standard `ASS_*` parameters are used for:
- `ASS_MANUFACTURER_TXT` — equipment manufacturer name.
- `ASS_MODEL_NR_TXT` — model number.
- `ASS_SERIAL_NR_TXT` — serial number (after commissioning).
- `ASS_BARCODE_TXT` — asset barcode or QR code reference.
- `ASS_MAINTENANCE_FREQUENCY_MONTHS` — PPM interval in months.

> **Cross-reference:** For placing clinical equipment families, see **MEP_FOUNDATION_GUIDE.md, Chapter 3 (Placement Centre)**. This chapter covers the healthcare-specific parameter layer.

### The Spaulding classification

The Spaulding classification is a system used in hospitals worldwide to determine how intensively a piece of equipment must be decontaminated between patient uses. It was developed by Dr Earle Spaulding in 1968 and remains the foundation of healthcare decontamination policy globally.

| Spaulding tier | Set as `CEQ_INFECT_TIER_TXT` | What it means | Decontamination required |
|---|---|---|---|
| Critical | `CRITICAL` | Enters sterile tissue or the vascular system (surgical instruments, catheters) | Sterilisation (autoclave or EO gas) |
| Semi-critical | `SEMI-CRITICAL` | Contacts mucous membranes or non-intact skin (endoscopes, laryngoscopes, respiratory equipment) | High-level disinfection (AER or chemical soak) |
| Non-critical | `NON-CRITICAL` | Contacts intact skin only (blood pressure cuffs, ECG leads, stethoscopes) | Low-level disinfection (wipe down) |

### The 50 COBie clinical equipment types

The Healthcare Pack adds 50 clinical equipment types to STING's COBie export (`COBIE_TYPE_MAP.csv`). These types link each equipment category to its COBie classification, Uniclass 2015 code, and SFG20 maintenance schedule reference. The COBie export includes these in the TYPE worksheet for FM handover.

Examples:

| COBie type | Equipment | Uniclass 2015 | SFG20 ref |
|---|---|---|---|
| `Medical Gas Terminal Unit` | Bedside gas outlet | Pr_40_50_16 | SFG20-HC-MG-TU |
| `Anaesthetic Machine` | Anaesthesia workstation | Pr_70_65_97 | SFG20-HC-ANA-01 |
| `MRI System` | MRI scanner | Pr_70_65_61_18 | SFG20-HC-RAD-MRI |
| `Autoclave` | Steriliser | Pr_70_50_02 | SFG20-HC-SSD-AUTO |
| `Dialysis Machine` | Haemodialysis unit | Pr_70_65_26 | SFG20-HC-DIA-01 |
| `Ceiling Pendant — ICU` | Patient monitoring pendant | Pr_40_50_16 | SFG20-HC-PEN-ICU |

---

## Chapter 9 — Room Data Sheets (RDS)

### What an RDS is

A Room Data Sheet is a single document — typically one to four pages — that records everything about one clinical room type. It is the primary coordination document between architect, engineer, specialist designer, and clinical client. The RDS records:

- Room dimensions and orientation.
- Required ventilation regime, air changes, and temperatures.
- Services connections (medical gases, specialist electrical outlets, water points).
- Equipment list (all items that must be provided in the room).
- Finish specifications (floor, walls, ceiling, coving, doors).
- Environmental requirements (noise rating, lighting levels, colour rendering).
- Access and safety requirements (access restriction, anti-ligature, observation).
- Sign-off fields for architect, engineer, and clinician.

A large acute hospital might have 150 distinct room types, each with its own RDS. Managing these manually in Word documents is error-prone and slow. STING generates them automatically from the BIM model parameters.

### What STING's RDS engine produces

STING's `RdsRenderer` reads the clinical parameters from every Room element in the model and renders a populated `.docx` file for each room type using the `health_rds.docx` template. The generated files are placed in `<project>/_BIM_COORD/generated/` with names like `YYYYMMDD_RDS_OR-ULTRA_1.docx`.

The template uses the MiniWord token system. Tokens look like `{{room.number}}` in the template and are replaced with the actual values from the model. The key tokens include:

| Token | Parameter source |
|---|---|
| `{{room.number}}` | Revit room number |
| `{{room.name}}` | Revit room name |
| `{{room.area}}` | `ASS_ROOM_AREA_SQ_M` |
| `{{room.health_class}}` | `CLN_ROOM_CLASS_TXT` |
| `{{room.hbn_ref}}` | `CLN_HBN_REF_TXT` |
| `{{room.press.regime}}` | `CLN_PRESS_REGIME_TXT` |
| `{{room.press.delta_pa}}` | `CLN_PRESS_DELTA_DESIGN_PA_NR` |
| `{{room.ach.req}}` | `HVC_AIR_CHANGES_PER_HR` |
| `{{room.temp.design_c}}` | `PER_ENVIRONMENTAL_TEMP_DESIGN_C` |
| `{{room.noise.nr}}` | `CLN_NOISE_NR_NR` |
| `{{room.lighting.lux}}` | `LTG_DESIGN_ILLUMINANCE_LUX` |
| `{{room.fire_comp}}` | `FLS_COMPARTMENT_ID_TXT` |
| `{{room.lig.risk}}` | `CLN_LIG_RISK_LVL_TXT` |
| Equipment list | Loop over `CEQ_*` elements in the room |
| Services connections | Loop over `MGS_*` and `ELC_*` elements in the room |
| Finishes | `BLE_ROOM_FINISH_FLOOR_TXT`, `BLE_ROOM_FINISH_WALL_TXT`, `BLE_ROOM_FINISH_CEILING_TXT` |
| Sign-offs | `signoff.architect`, `signoff.clinician`, `signoff.date` |

> **Note:** The `health_rds.docx` template must be authored separately — STING provides an authoring guide showing how to build the Word template with the correct token syntax. Once the template exists in `<project>/_BIM_COORD/templates/`, STING renders it automatically.

### The mandatory RDS parameters

The `RdsCompletenessValidator` checks that every Room classified as clinical has all mandatory fields filled before RDS generation is attempted. A room with missing mandatory fields will block its own RDS generation.

| Parameter | Why mandatory | What to fill in |
|---|---|---|
| `CLN_ROOM_CLASS_TXT` | Cannot generate RDS without knowing the room type | Set the appropriate class code (e.g. `OR-CONV`, `AIIR`, `WARD-INPT`) |
| `CLN_HBN_REF_TXT` | Clinical client needs the standards reference | Enter the HBN room reference (e.g. `HBN 04-01 Section 3.2`) |
| `CLN_PRESS_REGIME_TXT` | Ventilation engineer needs the pressure regime | `POS`, `NEG`, or `NEUTRAL` |
| `HVC_AIR_CHANGES_PER_HR` | Mechanical engineer needs the ACH requirement | Enter the required ACH (e.g. `20` for an OR, `12` for an AIIR) |
| `PER_ENVIRONMENTAL_TEMP_DESIGN_C` | Mechanical engineer needs the design temperature | Enter the design temperature in Celsius |
| `ASS_ROOM_AREA_SQ_M` | Area is fundamental to all clinical standards | Populated automatically from the Revit Room area if Room is placed; or enter manually |
| `BLE_ROOM_FINISH_FLOOR_TXT` | Infection control requires a defined floor finish specification | Enter the finish specification (e.g. `Vinyl sheet — welded seam — coved`) |

### Generating Room Data Sheets step by step

**Step 1.** Ensure all rooms have `CLN_*` parameters filled. The fastest way is to use **BulkParamWrite** by room type.

**Step 2.** Run **RDS Completeness Check**:
- STING panel > Healthcare > **RDS Completeness Check** button.
- The validator lists every mandatory field that is missing, grouped by room.
- Red rows are blocking errors. Amber rows are warnings that will generate incomplete RDS documents.

**Step 3.** Fix all red findings. Re-run the completeness check until it shows zero red rows.

**Step 4.** Run **Batch Issue RDS**:
- STING panel > Healthcare > **Batch Issue Room Data Sheets** button (or run `BatchIssueRoomDataSheetsCommand`).
- A dialog appears showing how many rooms will be processed and how many room types are represented.
- Click **Generate**.
- STING renders one `.docx` per room type and saves them to `_BIM_COORD/generated/`.

**Step 5.** Open the generated documents and review. Check that all token values have populated correctly. Empty fields indicate missing parameters — return to Step 2.

**Step 6.** Issue the RDS documents via the Document Manager. See **DOCUMENT_MANAGER_GUIDE.md** for the full document issue workflow. Use document type `Room Data Sheet (E17)` and suitability code `S2` (suitable for information) for initial issue, advancing to `S4` (suitable for construction) after clinical sign-off.

> **Stuck?** If RDS generation fails with a "template not found" error, the `health_rds.docx` template has not been placed in the `_BIM_COORD/templates/` folder. Either author the template (see the authoring guide) or contact your BIM manager who should have placed it as part of project setup.

### WORKFLOW_RdsIssue

The `WORKFLOW_RdsIssue.json` preset automates the RDS generation and issue sequence:

| Step | STING action | Your action |
|---|---|---|
| Refresh tag tokens | AutoTag updates `CLN_*` tokens from current model | Review any changed values |
| Recompute room areas | STING recalculates `ASS_ROOM_AREA_SQ_M` from Room boundaries | Confirm room boundaries are correct |
| Fetch ADB cross-refs | STING populates `CLN_ADB_CODE_TXT` from the ADB room-type table | Review ADB codes for accuracy |
| Render RDS docs | `BatchIssueRoomDataSheetsCommand` runs | Review generated .docx files |
| Audit and sign-off | Pauses for clinical review | Clinician reviews each RDS and approves |
| Issue | Documents issued via Document Manager | Issue to suitability code S2 or S4 |

---

## Chapter 10 — Healthcare Commissioning Workflows

### The 8 built-in healthcare workflow presets

STING ships eight workflow presets for healthcare commissioning. Each one chains a sequence of validation, documentation, and sign-off steps. They are run from the STING panel > BIM tab > **Workflow Presets** dropdown.

| Workflow name | What it covers | Who runs it | Standards reference |
|---|---|---|---|
| `WORKFLOW_HealthcareCommissioning` | The full commissioning sequence: RDS → adjacency → pressure → MGPS → EES → water safety → anti-ligature → COBie → handover | BIM Manager or Project Engineer | HBN 00, HTM series |
| `WORKFLOW_MgasVerification` | MGPS 12-step verification record per zone | Authorising Engineer (Medical Gas) with ASSE 6030 verifier | NFPA 99 §5.1.12, HTM 02-01 Part A |
| `WORKFLOW_PressureRegimeAudit` | Ventilation pressure regime cascade audit | Authorising Engineer (Ventilation) | HTM 03-01, ASHRAE 170 |
| `WORKFLOW_RdsIssue` | Room Data Sheet generation and issue | BIM Manager + Clinical Sign-off | HBN 00-01, ADB |
| `WORKFLOW_HTM-04-01-Annual` | Annual water safety inspection (sentinel flush logs, TMV3 checks, augmented care review) | Authorising Engineer (Water) | HTM 04-01 Parts A/B/C |
| `WORKFLOW_AntiLigatureAudit` | Mental health anti-ligature assessment and risk register | Mental Health Architect + Clinician | HBN 03-01, FGI Part 2 |
| `WORKFLOW_NFPA110-GeneratorTest` | Emergency power generator weekly exercise test log | Facilities / Estates | NFPA 110, NFPA 99 §6.4.2 |
| `WORKFLOW_HTM-01-06-EndoReprocess` | Endoscope decontamination traceability chain audit | Decontamination Lead + SHTM variant if Scotland | HTM 01-06 Parts A/B/C |

### Running a commissioning workflow step by step

**Step 1.** In the STING panel, navigate to the **BIM** tab. Click the **Workflow Presets** dropdown.

**Step 2.** Select the workflow you want to run. A dialog opens showing:
- The name and purpose of the workflow.
- A numbered list of every step, with each step's automation level (Automated, Semi-automated, Manual).
- The estimated time to complete (where STING can calculate it from previous runs).

**Step 3.** Review the step list. If a step is marked Manual, you must complete it on-site or with physical test equipment — STING cannot automate a pressure gauge reading.

**Step 4.** Click **Run**. STING executes the automated steps immediately. When it reaches a manual step, it pauses and shows you:
- What you need to do (for example: "Measure the differential pressure at the AIIR sensor and enter the reading below").
- Where to find the relevant element in the model (STING can zoom to it).
- The expected value range (from the design parameters).

**Step 5.** Complete each manual step and enter the result. Click **Continue** to proceed to the next step.

**Step 6.** When the workflow completes, STING saves a `WorkflowRunRecord` to `_BIM_COORD/workflow_state.json` with the date, step results, operator names, and pass/fail status. This is your commissioning audit trail.

### The WORKFLOW_HealthcareCommissioning preset — full walkthrough

This is the master commissioning workflow that chains all other checks. It is typically run at RIBA Stage 5 (Construction) leading into Stage 6 (Handover).

| Step | What STING does automatically | What you must do |
|---|---|---|
| 1. RDS completeness gate | Runs `RdsCompletenessValidator`. Blocks if any clinical room has missing mandatory parameters. | Fix any red findings from the completeness report before proceeding. |
| 2. Adjacency audit | Runs `AdjacencyValidator`. Checks that OR is adjacent to HSDU, ED is adjacent to Imaging, Mortuary is remote, Pharmacy is central. | Review the adjacency report. Flag any non-compliant adjacencies to the clinical team and architect. |
| 3. Pressure regime audit | Runs `PressureRegimeValidator`. Checks all pressure cascades. | Review the cascade report. Fix any inverted cascades or missing anterooms. |
| 4. MGPS audit | Runs `MgasFlowValidator`. Checks flow adequacy, pressure drop, cross-connection protection. | Review flow solver results. Upsize any undersized pipes in Revit. |
| 5. EES audit | Runs `EesBranchValidator`. Checks that all patient-care receptacles are on LIFE-SAFETY or CRITICAL branch, ATS time ≤ 10 s. | Review EES branch report. Update `ELC_EES_BRANCH_TXT` on any incorrectly classified circuits. |
| 6. Water safety audit | Runs `WaterSafetyValidator`. Checks TMV types, dead-leg distances, augmented care status, sentinel designation. | Review water safety report. Flag any dead legs > 1 m for remediation. |
| 7. Anti-ligature audit | Runs `AntiLigatureValidator` (only if facility profile includes mental health). | Review anti-ligature report. Update `LIG_PRODUCT_RATING_TXT` on any non-rated fittings. |
| 8. RDS generation | Runs `BatchIssueRoomDataSheetsCommand`. Generates all RDS documents. | Review all generated RDS documents. Obtain clinical sign-off. |
| 9. COBie export | Runs the Hospital-NHS COBie preset. Generates the full COBie handover spreadsheet. | Review the COBie output and pass to the FM client. |
| 10. Handover manual | Runs `HandoverManualCommand`. Assembles the FM/O&M handover package. | Review and issue the handover package via Document Manager. |

### WORKFLOW_HTM-04-01-Annual — full walkthrough

The annual water safety workflow implements the HTM 04-01 Part C schedule of inspections. It is run once per year (and additionally whenever a high-risk Legionella finding is made). Who runs it: the Authorising Engineer (Water) or a trained Water Safety Group member.

**Before running this workflow:** confirm that `PLM_SENTINEL_BOOL = Yes` is set on all sentinel sampling points. These are typically one outlet per floor on the rising main, and one on each branch. If they are not designated, the workflow will flag "No sentinel points found" and you will not be able to complete Step 3.

| Step | Automation | What STING does | What you must do |
|---|---|---|---|
| 1. System map refresh | Automated | STING traces all `PLM_*` plumbing systems and rebuilds the pipe network graph | Confirm all new pipework added since last audit is modelled |
| 2. Augmented care check | Automated | WaterSafetyValidator checks `PLM_AUGMENTED_CARE_BOOL = Yes` on all ICU, NICU, transplant, haematology rooms | Confirm augmented care rooms are correctly flagged |
| 3. Sentinel flush logs | Semi-automated | STING opens a log dialog per sentinel point; reads last-logged flush date from `PLM_FLUSH_LOG_REF_TXT` | Confirm each sentinel point has been flushed this week (or that the flush is logged in the linked system). Enter pass/fail |
| 4. TMV3 survey | Semi-automated | WaterSafetyValidator lists all `PLM_TMV_TYPE_TXT` values. Flags any augmented-care outlet without `TMV3` | Check each flagged outlet on-site. Replace TMV2 with TMV3 where required |
| 5. Temperature checks | Manual | STING opens a temperature entry dialog per sentinel point | Measure hot-water temperature at each sentinel outlet. Enter readings. STING flags any outside 50–60 °C |
| 6. Dead-leg survey | Automated | STING calculates estimated pipe length to each outlet from the network graph. Flags outlets where dead-leg > 1 m | Review flagged outlets. Arrange remediation of any dead legs > 1 m in augmented care |
| 7. RO loop check (dialysis only) | Automated | STING checks that all `DIAL` rooms connect only to `PLM_DIALYSIS_RO_LOOP_BOOL = Yes` outlets | Confirm dialysis stations are on RO loop |
| 8. Point-of-use filter check | Automated | STING checks `PLM_POU_FILTER_BOOL = Yes` on all augmented care outlets | Confirm filters are present on all flagged outlets. Enter last-changed date |
| 9. Generate water safety log | Automated | STING generates the annual water safety report with all readings, pass/fail status, and outstanding actions | Sign the report (Authorising Engineer sign-off field) |
| 10. Issue and archive | Automated | STING issues the report via Document Manager as a `D15 Progress Report` with suitability `S4` | Distribute to the Water Safety Group and maintain on the estates file |

### WORKFLOW_PressureRegimeAudit — full walkthrough

| Step | Automation | What STING does | What you must do |
|---|---|---|---|
| 1. Build room adjacency graph | Automated | STING identifies all adjacent room pairs by wall and door connections | Confirm room placements are complete and room boundaries close correctly |
| 2. Cascade validation | Automated | PressureRegimeValidator checks monotonic pressure direction in each department | Review red findings. Add missing anterooms where pressure transitions are invalid |
| 3. AIIR exhaust check | Automated | STING checks that all NEG rooms have exhaust connected to an `HVC_EXHAUST_TO_OUTSIDE_BOOL = Yes` duct | Confirm exhaust routes are modelled correctly. Do not recirculate AIIR exhaust |
| 4. ACH cross-check | Automated | STING compares `HVC_AIR_CHANGES_PER_HR` against HTM 03-01 minimums for each room class | Review rooms below the minimum ACH. Update AHU schedules |
| 5. HEPA certification | Semi-automated | STING checks `HVC_HEPA_GRADE_TXT` on terminal filter units in OR and PE rooms | Confirm HEPA grade is H13 or H14. Enter DOP test dates where available |
| 6. Produce drift report | Automated | STING generates the pressure regime findings report as a PDF | Sign and issue via Document Manager as a `Pressure Regime Audit (E04)` |
| 7. Live push (if IoT bridge) | Semi-automated | STING pushes current pressure cascade data to the Planscape mobile app | On-site team can verify live differential pressure readings against design values on mobile |

### WORKFLOW_AntiLigatureAudit — full walkthrough

| Step | Automation | What STING does | What you must do |
|---|---|---|---|
| 1. Identify anti-ligature rooms | Automated | STING collects all rooms with `CLN_LIGATURE_RES_BOOL = Yes` | Confirm all mental health bedrooms, en-suites, seclusion rooms, day lounges, and corridors are flagged |
| 2. Load fittings | Automated | STING collects all family instances (doors, handles, hooks, rails, pendants, light fittings, pipes) in the flagged rooms | Confirm that families have been placed to represent all physical fixtures |
| 3. Check product ratings | Automated | AntiLigatureValidator checks `LIG_PRODUCT_RATING_TXT` on every fixture | Review unrated fixtures. Contact manufacturers for certificate data. Enter ratings |
| 4. Check load-release | Automated | STING checks `LIG_PRESSURE_REL_BOOL` and `LIG_FORCE_LIMIT_KG_NR` on hooks, rails, and towel bars | Confirm that load-release fittings are specified. Enter force limits from manufacturer data sheets |
| 5. Observation line-of-sight | Semi-automated | STING checks `LIG_AREA_OBS_LOS_TXT` per room and models sightlines from nurse station positions | Architect confirms sightline geometry in model. Mark any observation-blind corners |
| 6. Anti-barricade check | Automated | STING checks that en-suite doors in `PSY-BED` and `SECL` rooms are flagged `LIG_SELF_CLOSE_BOOL = Yes` and have inward/outward release hardware | Confirm door hardware specification. Update `LIG_PRODUCT_RATING_TXT` accordingly |
| 7. Exposed services check | Automated | STING checks for any unboxed pipe runs, conduit runs, or cable routes in anti-ligature rooms | Engineer reviews flagged elements. Add boxing families or anti-ligature lagging |
| 8. Risk register | Automated | STING generates the anti-ligature risk register document (one row per room, one column per ligature risk category) | Risk register reviewed and signed by mental health clinician and architect |
| 9. Issue | Automated | Document issued via Document Manager as `Anti-Ligature Assessment (E18)` | Issue to clinical client for sign-off |

### WORKFLOW_NFPA110-GeneratorTest — full walkthrough

This workflow is used by estates and facilities teams to log the mandatory generator exercise tests required by NFPA 110 and NFPA 99 §6.4.2. It is run weekly (brief 30-minute load test) and monthly (full ATS transfer test under load).

| Step | Automation | What STING does | What you must do |
|---|---|---|---|
| 1. Identify EES assets | Automated | STING collects all generators, ATS units, and UPS systems tagged with `ELC_EES_BRANCH_TXT = LIFE-SAFETY` | Confirm all EES plant is tagged |
| 2. Weekly test log entry | Manual | STING opens the generator test log dialog | Run the generator under a test load. Enter start time, stop time, voltage, frequency, and load percentage. STING flags any test older than 35 days as overdue |
| 3. ATS transfer test | Manual | STING opens the ATS test dialog | Simulate mains failure. Record transfer time. Enter measured transfer time in seconds. STING flags any ATS with transfer time > 10 s |
| 4. UPS battery check | Automated | STING reads `ELC_UPS_BATTERY_REPLACE_DT` on all UPS units. Flags any overdue | Enter battery replacement dates. Raise purchase orders for overdue batteries |
| 5. N+1 redundancy check | Automated | EesResilienceValidator checks that critical Level 1 equipment has N+1 redundancy | Review findings. Note any single-points-of-failure for the Authorising Engineer (Electrical) |
| 6. Generate test log | Automated | STING generates the NFPA 110 test log record | Sign and file. Upload to estates management system |

### WORKFLOW_HTM-01-06-EndoReprocess — full walkthrough

This workflow is used in endoscopy decontamination units to verify RFID traceability compliance against HTM 01-06 Parts A, B, and C. In Scotland, SHTM 01-06 applies.

| Step | Automation | What STING does | What you must do |
|---|---|---|---|
| 1. Identify endoscopy rooms | Automated | STING collects all rooms with `CLN_ROOM_CLASS_TXT = ENDOSCOPY` | Confirm all decontamination rooms (soak, AER, drying cabinet, sterile storage) are modelled |
| 2. RFID reader position check | Automated | EndoscopeTraceValidator checks that each ENDOSCOPY room has four RFID reader families placed: soak position, AER position, drying position, and storage position | Place RFID reader families at all four positions in the decontamination flow sequence |
| 3. Dirty-clean flow check | Automated | STING checks that the dirty-clean-sterile flow is non-crossing: soiled scopes enter from the dirty side; decontaminated scopes exit to the sterile side; no crossover | Review AdjacencyValidator findings for endoscopy rooms. Confirm dirty entry and clean exit are on separate sides of the decon room |
| 4. Scope asset check | Automated | STING checks that all endoscope assets have `ASS_BARCODE_TXT`, `CEQ_GMDN_CODE_TXT`, and `CEQ_SFG20_REF_TXT` populated | Update missing GMDN codes and maintenance references on each scope asset |
| 5. Cycle count check | Automated | STING reads `ASS_MAINTENANCE_FREQUENCY_MONTHS` and flags scopes with cycle count exceeding manufacturer limits | Replace scopes that have exceeded their operational cycle life |
| 6. AER validation | Semi-automated | STING checks that AER units have `CEQ_DECON_METHOD_TXT = AER-ENDO` and last validation date is within 12 months | Confirm AER validation certificates are in the document register. Enter last validation date |
| 7. Generate traceability audit | Automated | STING generates the HTM 01-06 traceability audit report | Decontamination Lead reviews and signs the report |
| 8. Issue | Automated | Document issued via Document Manager | Issue to clinical team and infection control |

> **Stuck?** If the workflow reports "No endoscopy rooms found" even though you have rooms named "Endoscopy", check that `CLN_ROOM_CLASS_TXT` is set to `ENDOSCOPY` on the rooms (not just a text name). STING looks at the parameter value, not the room name.

### WORKFLOW_MgasVerification — detailed step reference

For the full 12-step NFPA 99 verification, see **Chapter 2** of this guide. The workflow preset chains those steps in this order:

| Step | Automation | Notes |
|---|---|---|
| Pre-purge check | Semi-automated | `MGS_PIPE_BRAZED_BOOL` must be set on all joints |
| Cross-connection test | Manual | Probe every outlet with the opposite gas — must accept no gas |
| Particulate test | Manual (lab result) | Submit gas samples; enter ISO 8573-1 particle count results |
| Purity test | Manual (lab result) | Submit oxygen sample to pharmacopoeial standard; enter purity % |
| Pressure test | Semi-automated | Enter on-site gauge readings; STING compares to design pressure |
| Indexing check | Manual | Confirm indexed probes reject wrong gas type |
| Flow test | Semi-automated | MgasFlowSolver runs; enter on-site measured flows |
| Labelling check | Manual | Walk each zone; confirm all labels match BIM model |
| ZVB isolation test | Manual | Close each ZVB; confirm correct zone isolates |
| Area alarm test | Manual | Drop pressure below limit; confirm AAP alarm fires |
| Master alarm test | Manual | Confirm master alarm panel receives all area alarms |
| Final documentation | Semi-automated | STING assembles log; verifier signs; certificate reference entered |

---

## Chapter 11 — The 16 Healthcare Validators

### What validators do

A validator is a set of automated checks that STING runs against your model. It reads parameters, measures distances, checks connections, and compares values against standards. Think of it as a knowledgeable colleague who reads every element in your model and tells you what is missing or wrong before the drawings leave your office.

Validators run on demand. They do not slow down your Revit session when you are working — they only check when you ask them to.

**To run all healthcare validators at once:**
- STING panel > Healthcare > **Run All Healthcare Validators** button (or `RunAllHealthcareValidatorsCommand`).
- The combined report shows findings from all active validators, sorted by severity.
- Alternatively, each validator has its own dedicated command for targeted checking (listed below).

### Complete validator reference table

| Validator | Dedicated command | What it checks | Standards | Profiles that activate it | Severity |
|---|---|---|---|---|---|
| **PressureRegimeValidator** | `PressureRegimeAuditCommand` | Adjacent room pressure cascades are monotonic; AIIR exhaust goes to outside; PE supply uses HEPA H14; OR meets minimum ACH; linked anterooms form valid pressure buffers | HTM 03-01, ASHRAE 170, CDC | All profiles with clinical rooms | Errors for invalid cascades; Warnings for marginal values |
| **MgasFlowValidator** | `MgasNetworkAuditCommand` | Diversified flow adequate at each zone valve box; pressure drop ≤ 10% of source pressure; gas-type indexing present on all terminal units; each ZVB serves only one alarm panel; brazing flag set on all copper joints | HTM 02-01, NFPA 99 Ch.5, ISO 7396-1 | FULL, ACUTE, COMMUNITY (if MGPS present) | Errors for cross-connection risk; Warnings for undersized pipes |
| **EesBranchValidator** | `EesBranchAuditCommand` | Every patient-care receptacle on LIFE-SAFETY or CRITICAL branch; ATS transfer time ≤ 10 s for LS/CR; OR and ICU circuits correctly classified; cardiac-protected outlets in IT-CARD circuit only | NFPA 99 Ch.6, NEC 517, HTM 06-01 | FULL, ACUTE | Errors for uncategorised patient-care circuits; Warnings for ATS time > 10 s |
| **WaterSafetyValidator** | `WaterSafetyAuditCommand` | HTM 04-01 dead-leg ≤ 1 m for sentinel outlets; TMV3 on all augmented care outlets; DHW flush temperatures 50–60 °C; no blind branches; dialysis stations only on RO-loop | HTM 04-01 Parts A/B/C, ASHRAE 188, BS 8558 | FULL, ACUTE, COMMUNITY | Errors for dead legs > 1 m in augmented care; Warnings for missing TMV designation |
| **RadShieldValidator** | `RadShieldAuditCommand` | NCRP 147 W·U·T → required mm Pb ≤ provided; QE sign-off present; doors interlocked with beam-on; controlled-area designation correct; MRI 5-Gauss fence geometry present | NCRP 147, IRR17, 10 CFR 20 | FULL, ACUTE, IMAGING-ONLY | Errors for insufficient shielding or missing QE; Warnings for marginal values |
| **AdjacencyValidator** | `AdjacencyAuditCommand` | HBN-derived adjacency requirements: ED ≤ 3 doors from Imaging; OR ↔ HSDU in same compartment; Mortuary remote; Pharmacy central; clean/dirty flow non-crossing | HBN 00-01, HBN 04-01, HBN 13, FGI Guidelines | FULL, ACUTE | Warnings (not blocking) — adjacency non-conformance reported for clinical review |
| **AntiLigatureValidator** | `AntiLigatureAuditCommand` | Every fitting in `PSY-*` rooms has `LIG_PRODUCT_RATING_TXT`; observation line-of-sight intact; en-suite door is anti-barricade type; no exposed pipes in bedrooms or seclusion rooms | HBN 03-01, FGI Part 2, BS 9263 | MENTAL-HEALTH, FULL (if mental health wing) | Errors for unrated fittings; Warnings for marginal observation coverage |
| **RdsCompletenessValidator** | `RdsCompletenessCheckCommand` | All ADB/HBN-required parameters populated on clinical rooms; equipment groups accounted for; room finishes match clinical class; HBN reference present | HBN 00-01, ADB, HTM series | All profiles | Errors for missing mandatory fields; Warnings for incomplete optional fields |
| **AcousticValidator** | `AcousticAuditCommand` | HTM 08-01 NR target by room class met; FGI/HIPAA STC threshold met; RT60 target vs `CLN_RT60_TARGET_S_NR`; NC curve vs `HVC_NC_CURVE_INT`; sound masking system present when STC < 45 | HTM 08-01, FGI Guidelines, HIPAA | FULL, ACUTE, COMMUNITY, MENTAL-HEALTH | Warnings — acoustic non-conformance reported for review |
| **StructuralLoadValidator** | `StructuralLoadAuditCommand` | Declared floor load ≥ equipment weight from reference table; vibration criterion specified for imaging rooms; structural sign-off date present before handover | BS EN 1991, manufacturer specs, HTM series | FULL, ACUTE, IMAGING-ONLY | Errors for missing structural sign-off at handover; Warnings for under-declared loads |
| **AdvancedRadShieldValidator** | `AdvancedRadCalcCommand` | PET 511 keV geometry; SPECT/gamma camera scatter shielding; brachytherapy afterloading vault dose-rate; all using energy-correct transmission factors (not NCRP 147 kV curves) | NCRP 151, IRR17 | FULL, ACUTE, IMAGING-ONLY (if PET/NM/brachytherapy present) | Errors for insufficient 511 keV shielding; Warnings if NCRP 147 curves used for PET room |
| **IoTStalenessValidator** | `IoTStalenessAuditCommand` | BMS last-seen timestamp > 30 min triggers sensor-fault warning; TMV temperature outside 40–45 °C band triggers scald/Legionella warning; cleanroom cert overdue; radiation area monitor above alert threshold | HTM 04-01, USP <797>, ASHRAE 135 | All profiles (only when IoT bridge configured) | Warnings for stale sensors; Errors for safety-critical alerts |
| **EndoscopeTraceValidator** | `EndoscopeTraceAuditCommand` | Each ENDOSCOPY room has 4 RFID reader positions modelled (soak, AER, drying, storage); each scope asset has ID, AER reference, and cycle count; decon dirty-clean-sterile flow non-crossing | HTM 01-06 Parts A/B/C, SHTM 01-06 (Scotland) | FULL, ACUTE, COMMUNITY (if endoscopy present) | Errors for missing RFID positions; Warnings for scope cycle count exceeding manufacturer limit |
| **EesResilienceValidator** | `EesResilienceDashboardCommand` | NFPA 110 weekly generator test log not older than 35 days; monthly ATS transfer test logged; UPS battery replacement date not overdue; N+1 redundancy check on Level 1 EES equipment | NFPA 110, NFPA 99 §6.4.2 | FULL, ACUTE | Errors for overdue generator tests; Warnings for approaching UPS replace date |
| **RtlsCoverageValidator** | Runs as part of MGPS/Healthcare suite | RTLS anchor coverage adequate per zone; no BLE/Wi-Fi/UWB anchors in RF-shielded rooms; no active wireless in MRI Zone ≥ 3 (blocking error); anchor technology matches room shielding type | Vendor RTLS design guides | FULL, ACUTE (if RTLS specified) | Blocking errors for wireless transmitters in MRI Zone 3/4; Warnings for coverage gaps |
| **WasteFlowValidator** | `WasteFlowAuditCommand` | `CLN_WASTE_CLASS_TXT` populated per HBN; waste routing from clinical collection points to segregation store does not cross patient or clean supply flow; radioactive waste rooms have `CLN_RAD_CONTROLLED_BOOL = Yes` | HBN 07-01, HTM 07-01, IRR17 | FULL, ACUTE | Warnings for routing conflicts; Errors for radioactive waste in uncontrolled areas |

### Running all validators and reading the combined report

When you run **Run All Healthcare Validators**, STING presents a unified report with four sections:

1. **Blocking Errors** (red): findings that must be resolved before commissioning can proceed. These map to mandatory sign-offs in the commissioning workflow.
2. **Errors** (orange): findings that must be resolved before issue — they indicate non-compliant design.
3. **Warnings** (amber): findings that should be reviewed but are not automatically blocking — they may represent design decisions that differ from the standard recommendation, which need a clinical or engineering justification.
4. **Informational** (blue): notes that may affect downstream processes (for example, a missing ADB code that will cause an incomplete RDS).

**Priority order for fixing findings:**
1. Fix Blocking Errors first (RTLS in MRI Zone 3/4, radiation shielding below required, AIIR exhaust not to outside).
2. Fix Errors second (uncategorised EES circuits, dead legs > 1 m, unrated anti-ligature fittings, missing QE sign-off).
3. Review Warnings with the relevant Authorising Engineer (adjacency, acoustic shortfalls, structural margin).
4. Action Informational items as part of completeness checking (missing ADB codes, missing HBN references).

---

## Chapter 12 — Healthcare Drawing Types

### The 22 healthcare-specific drawing types

These drawing types are defined in STING's drawing type registry (`STING_DRAWING_TYPES.json`) and are produced using the Drawing Production System. For the full workflow to produce these drawings, see **DRAWING_PRODUCTION_SYSTEM_GUIDE.md**.

| Drawing type ID | Paper / scale | Purpose | When to produce |
|---|---|---|---|
| `health-rds-A2` | A2 / No scale | Room Data Sheet — one per clinical room type | RIBA Stage 3 onwards; updated at each stage |
| `health-eqp-pln-A1-1to50` | A1 / 1:50 | Clinical equipment plan with furniture, fixtures, and equipment callouts | RIBA Stage 4 (Technical Design) |
| `health-mep-coord-A1-1to50` | A1 / 1:50 | MEP coordination plan with shielding overlay | RIBA Stage 4; updated at Stage 5 |
| `health-medgas-pln-A1-1to100` | A1 / 1:100 | MGPS layout plan showing pipe routes, ZVBs, alarm panels | RIBA Stage 4 |
| `health-medgas-schem-A1` | A1 / Schematic | MGPS schematic: zone valves, manifolds, alarms, plant room | RIBA Stage 3 (for approval); updated at Stage 4 |
| `health-pressure-pln-A1-1to100` | A1 / 1:100 | Pressure regime plan with pressure cascade arrows | RIBA Stage 3; updated at each revision |
| `health-ess-power-riser-A1` | A1 / Schematic | Essential power single-line diagram (LS/CR/EQ branches, ATS, generator) | RIBA Stage 4 |
| `health-ips-pln-A1-1to50` | A1 / 1:50 | Isolated power systems layout for OR/ICU | RIBA Stage 4 |
| `health-decon-flow-A1-1to50` | A1 / 1:50 | Decontamination dirty-clean-sterile flow diagram | RIBA Stage 4; mandatory for HSDU, Endoscopy |
| `health-mortuary-pln-A2-1to50` | A2 / 1:50 | Mortuary and post-mortem suite plan | RIBA Stage 4 |
| `health-fire-comp-A1-1to100` | A1 / 1:100 | Fire compartmentation and PHE refuge plan | RIBA Stage 4; mandatory for fire strategy |
| `health-rad-shield-A1-1to50` | A1 / 1:50 | Radiation shielding plan with mm Pb callouts per barrier element | RIBA Stage 4; updated after QE review |
| `health-mri-zoning-A1-1to50` | A1 / 1:50 | MRI suite zone 1–4 zoning plan with 5-Gauss line | RIBA Stage 3 (for magnet purchase approval) |
| `health-ligature-pln-A1-1to50` | A1 / 1:50 | Anti-ligature plan with risk levels by area | RIBA Stage 4; mandatory for mental health projects |
| `health-bedhead-elev-A2-1to20` | A2 / 1:20 | Bedhead trunking elevations (services, pendants, call points) | RIBA Stage 4 |
| `health-or-ceiling-A2-1to20` | A2 / 1:20 | Operating theatre ceiling reflected plan with pendant boom positions | RIBA Stage 4 |
| `health-acoustic-pln-A1-1to100` | A1 / 1:100 | Acoustic zoning plan: NR targets per room, STC of separating walls | RIBA Stage 4 |
| `health-struct-loads-A1-1to50` | A1 / 1:50 | Structural loading plan: floor live-load zones, point-load callouts, vibration isolation pads | RIBA Stage 4 |
| `health-infection-ctrl-A1-1to100` | A1 / 1:100 | Infection control classification plan: rooms coloured by class, coving zones, pass-throughs | RIBA Stage 3 onwards |
| `health-waste-flow-A1-1to100` | A1 / 1:100 | Waste flow diagram: segregation by waste class, chute and collection routes | RIBA Stage 4 |
| `health-rtls-coverage-A1-1to100` | A1 / 1:100 | RTLS anchor coverage plan: beacon locations, technology type, coverage radii, RF dead zones | RIBA Stage 4 (if RTLS specified) |
| `health-nm-schem-A1` | A1 / Schematic | Nuclear medicine/PET schematic: dose calibrator, hot lab, decay storage, exhaust routes | RIBA Stage 4 (nuclear medicine only) |

### Healthcare ViewStylePacks

Six ViewStylePacks are included in the Healthcare Pack. Each one applies a consistent set of graphic overrides to make the relevant clinical information immediately readable.

| Pack ID | What it shows | Key visual settings |
|---|---|---|
| `corp-healthcare-clinical` | Standard clinical plan | Clinical equipment in saturated colour, non-clinical content lighter, wet-room floors hatched |
| `corp-healthcare-shielding` | Radiation shielding | Lead-equivalent walls drawn thick; X-ray hazard hatch pattern; MRI 5-Gauss line as red dashed circle; controlled-area halo |
| `corp-healthcare-pressure` | Pressure regime | Negative-pressure rooms blue tint, positive-pressure rooms red tint, neutral rooms grey; pressure cascade arrows as detail components |
| `corp-healthcare-fire` | Fire compartmentation | Compartments in primary colours; PHE refuge zones halftoned; smoke-rated lines visually distinct from fire-rated |
| `corp-healthcare-acoustic` | Acoustic zoning | Room boundaries tinted by acoustic class (sensitive / medium / not-sensitive); STC callout annotation visible at coarse detail |
| `corp-healthcare-structural` | Structural loads | Floor-load zone polygons in graduated fill intensity; point-load symbols prominent; vibration isolation elements hatched |

> **Cross-reference:** For the full drawing production workflow — creating views, applying drawing types, placing sheets, and issuing — see **DRAWING_PRODUCTION_SYSTEM_GUIDE.md**.

> **Cross-reference:** For document issue and revision management — see **DOCUMENT_MANAGER_GUIDE.md**.

---

## Chapter 13 — Standards References

Understanding which standard governs which aspect of healthcare design helps you respond correctly when a validator raises a finding. The table below lists every standard referenced by the Healthcare Pack.

| Standard | Full title | What it governs | STING validators that reference it |
|---|---|---|---|
| **HTM 02-01 Parts A/B** | Medical Gas Pipeline Systems | Design, installation, testing, and maintenance of medical gas systems | MgasFlowValidator |
| **HTM 03-01 Parts A/B** | Specialist Ventilation for Healthcare Premises | ACH, pressure regimes, HEPA filtration, temperature and humidity ranges | PressureRegimeValidator |
| **HTM 04-01 Parts A/B/C** | Safe Water in Healthcare Premises | Legionella risk management, dead-leg limits, TMV requirements, augmented care | WaterSafetyValidator |
| **HTM 05-01/05-02** | Fire Safety / Firecode Functional Provisions | Fire compartmentation, PHE refuges, smoke control strategy | AdjacencyValidator (partial) |
| **HTM 06-01** | Electrical Services Supply and Distribution | Essential electrical services branches, IPS, isolated power systems | EesBranchValidator |
| **HTM 01-06 Parts A/B/C** | Decontamination of Flexible Endoscopes | Endoscope reprocessing flow, RFID traceability, AER performance | EndoscopeTraceValidator |
| **HTM 08-01** | Acoustics in Healthcare Buildings | NR targets by room type, sound insulation, speech privacy | AcousticValidator |
| **HBN 00-01** | General Design Guidance for Healthcare Buildings | Departmental adjacency, circulation, room-size standards | AdjacencyValidator, RdsCompletenessValidator |
| **HBN 03-01** | Adult Acute Mental Health and Psychiatric Intensive Care | Anti-ligature design, observation standards, seclusion room design | AntiLigatureValidator |
| **HBN 04-01** | Adult In-Patient Facilities | Bed-head services, AIIR requirements, isolation room design | PressureRegimeValidator, RdsCompletenessValidator |
| **HBN 09-02** | Critical Care | ICU bay design, monitoring services, pendant design | RdsCompletenessValidator |
| **HBN 13** | Sterile Services / HSDU | Dirty-clean-sterile flow, washer-disinfector and autoclave clearances | AdjacencyValidator |
| **HBN 16** | Mortuary and Post-mortem | Refrigeration capacity, downflow ventilation, remote location | AdjacencyValidator |
| **NFPA 99 (Health Care Facilities Code)** | Medical gas systems, essential electrical services, isolated power systems, anaesthetising locations | MgasFlowValidator, EesBranchValidator |
| **NFPA 110 (Emergency and Standby Power)** | Generator systems, ATS performance, weekly testing requirements | EesResilienceValidator |
| **NCRP 147** | Structural Shielding Design for Medical X-Ray Imaging Facilities | Diagnostic X-ray shielding calculation (up to 200 kVp) | RadShieldValidator |
| **NCRP 151** | Radiation Shielding Design for Megavoltage X-Ray and Gamma-Ray Radiotherapy Facilities | LINAC vault shielding, neutron shielding, PET/NM advanced shielding | AdvancedRadShieldValidator |
| **ANSI/ASHRAE/ASHE 170** | Ventilation of Health Care Facilities | ACH requirements, pressure differentials, HEPA filter specifications for all clinical space types | PressureRegimeValidator |
| **IRR17 (Ionising Radiations Regulations 2017, UK)** | Designation of controlled and supervised areas, dose limits, Qualified Expert requirements | RadShieldValidator |
| **10 CFR 20 (US)** | Standards for Protection Against Radiation (NRC) | US dose limits, controlled area designation | RadShieldValidator (US projects) |
| **USP <797>** | Pharmaceutical Compounding — Sterile Preparations | Pharmacy clean room pressure cascade, ISO classification, recertification cycle | PressureRegimeValidator (pharmacy rooms), IoTStalenessValidator |
| **USP <800>** | Handling Hazardous Drugs in Healthcare Settings | Negative-pressure hazardous drug handling areas, isolated exhaust | PressureRegimeValidator (pharmacy rooms) |
| **NEC 517 (National Electrical Code, Article 517, US)** | Health-care occupancies electrical requirements | EES branch classification, IPS, cardiac-protected circuits | EesBranchValidator (US projects) |
| **ISO 7396-1** | Medical gas pipeline systems | Terminal unit specifications, alarm system design | MgasFlowValidator |
| **ISO 14644-1/3/4** | Cleanrooms and associated controlled environments | Cleanroom classification, recovery testing, HEPA validation | IoTStalenessValidator (USP rooms) |
| **BS 5682** | Terminal units for medical gas pipeline systems | Indexed probe design, cross-connection prevention | MgasFlowValidator |
| **BS 8300** | Design of an Accessible and Inclusive Built Environment | Accessibility requirements in healthcare | AdjacencyValidator (partial) |
| **BS 9263** | Specification for anti-ligature products | Product rating LR1–LR4 | AntiLigatureValidator |
| **ASSE 6030/6040** | Medical gas pipeline system verifier and maintenance credentials | Verifier qualifications for NFPA 99 verification log | MgasFlowValidator (sign-off check) |
| **IEC 60601-1** | Medical electrical equipment — General safety requirements | Electrical safety clearances, IT-cardiac protection | EesBranchValidator |
| **HBN 21** | Maternity Facilities | LDR rooms, NICU design, midwife-led birth centre | RdsCompletenessValidator |

---

## Troubleshooting

| Problem | Likely cause | Fix |
|---|---|---|
| Healthcare validators do not run when I click Run All Healthcare Validators | `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` is empty | Open Manage > Project Information > set `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` to one of: `FULL`, `ACUTE`, `COMMUNITY`, `DENTAL`, `IMAGING-ONLY`, `MENTAL-HEALTH` |
| MGPS flow solver gives negative flow values at some nodes | Network graph was not built before running the flow solver | Run **MGPS Network Audit** first. Confirm zero orphaned outlets. Then run the flow solver. |
| RDS generation fails with "template not found" error | `health_rds.docx` is not in `_BIM_COORD/templates/` | Author the RDS template using the provided authoring guide and place it in that folder, or ask your BIM manager. |
| Radiation calculator gives no output | `RAD_BARRIER_TYPE_TXT` is missing on the selected element, or no source type is selected in the calculator dialog | Set `RAD_BARRIER_TYPE_TXT` (`PRIMARY`, `SECONDARY`, or `SCATTER`) on the wall, door, or window element. Select a source type in the calculator. |
| Anti-ligature validator reports "No anti-ligature rooms found" | `CLN_LIGATURE_RES_BOOL` is not set on any rooms, or the `LIG_BEHAVIOURAL` parameter group has not been loaded | Run **Load Shared Params** to ensure the LIG_BEHAVIOURAL group is bound. Then set `CLN_LIGATURE_RES_BOOL = Yes` on the relevant rooms. |
| Pressure regime audit finds no rooms | Rooms are not placed in the model, or `CLN_PRESS_REGIME_TXT` is not filled on any room | Confirm Room elements are placed (not just room bounding boxes). Fill `CLN_PRESS_REGIME_TXT` on each room. |
| RDS Completeness Check shows red for every room | CLN_* parameters are not loaded as shared parameters | Re-run **Load Shared Params**. Confirm that the CLN_CLINICAL group (group 28) appears in the parameter browser. |
| MGPS Network Audit reports orphaned terminal units | Pipes are not connected to the terminal units, or system types do not use the `MGAS-` prefix | Check pipe connections in Revit (select a pipe and verify its system connectivity in Properties). Check that system types are named `MGAS-O2`, `MGAS-VAC`, etc. |
| EES branch validator flags all receptacles | `ELC_EES_BRANCH_TXT` is not loaded, or the Healthcare Pack is not installed | Run **Load Shared Params**. Check that `ELC_EES_BRANCH_TXT` appears in the Electrical group. |
| Water safety validator cannot find sentinel outlets | `PLM_SENTINEL_BOOL` is not set on any plumbing fixtures | Select the sentinel temperature measurement points (typically one per floor, one per branch) and set `PLM_SENTINEL_BOOL = Yes`. |
| MRI zone validator reports blocking error for RTLS anchor | A BLE, Wi-Fi, or UWB RTLS anchor is placed inside MRI Zone 3 or 4 | Move the anchor outside Zone 3, or change its technology to IR (infrared). Active wireless transmitters cannot be placed within the 5-Gauss line. This is a life-safety requirement. |
| Commissioning workflow pauses and will not continue | A mandatory sign-off field is empty | Read the pause message. The workflow tells you which parameter is missing. Enter the required name or date. |
| COBie export does not include clinical equipment | `CEQ_CLINICAL_BOOL` is not set to `Yes` on clinical equipment elements | Select all clinical equipment families. Set `CEQ_CLINICAL_BOOL = Yes` using **BulkParamWrite**. |
| Acoustic validator flags all ward rooms | `CLN_NOISE_NR_NR` is not set, or the NR target CSV has not loaded | Ensure the `HEALTHCARE_ACOUSTIC_NR_TARGETS.csv` data file is in the STING data folder. Set `CLN_NOISE_NR_NR` on rooms where you want to override the default HTM 08-01 target. |

---

## Quick Reference

### All healthcare commands

| Button label | STING panel tab | Command class | What it does | When to use |
|---|---|---|---|---|
| Load Shared Params | Tags | `LoadSharedParamsCommand` | Binds all STING shared parameters including healthcare groups | First step on any new project |
| MGPS Network Audit | BIM / Healthcare | `MgasNetworkAuditCommand` | Traces MGAS-* systems and reports network topology | Before running MGPS flow solver or verification |
| MGPS Flow Solver | BIM / Healthcare | `MgasFlowSolver` (via Audit) | Calculates diversified flow and checks pressure drop | After network audit; before commissioning |
| MGPS Generate Schematic | BIM / Healthcare | `MgasGenerateSchematicCommand` | Generates MGPS schematic drawing from model | RIBA Stage 4 |
| MGPS Verify | BIM / Healthcare | `MgasVerifyCommand` | Records NFPA 99 §5.1.12 12-step verification | On-site commissioning |
| Pressure Regime Audit | Healthcare | `PressureRegimeAuditCommand` | Checks pressure cascade validity | RIBA Stage 4; before commissioning |
| EES Branch Audit | Healthcare | `EesBranchAuditCommand` | Checks essential power branch classification | Before commissioning |
| Water Safety Audit | Healthcare | `WaterSafetyAuditCommand` | Checks dead legs, TMV, augmented care status | Before commissioning; annually (HTM 04-01) |
| Rad Shield Audit | Healthcare | `RadShieldAuditCommand` | Checks shielding against NCRP 147 calculation | After QE review; before commissioning |
| Rad Calc — Chest Room | Healthcare / Radiation | `RadCalcChestRoomCommand` | NCRP 147 calculator for plain X-ray rooms | Design stage |
| Rad Calc — CT Room | Healthcare / Radiation | `RadCalcCtRoomCommand` | NCRP 147 calculator for CT suites | Design stage |
| Rad Calc — Advanced | Healthcare / Radiation | `AdvancedRadCalcCommand` | NCRP 151 calculator for PET/NM/brachytherapy | Design stage (advanced imaging only) |
| MRI Zone Audit | Healthcare / Radiation | `MriZoneAuditCommand` | Checks MRI zone geometry and 5-Gauss line | Design stage and before commissioning |
| Adjacency Audit | Healthcare | `AdjacencyAuditCommand` | Checks HBN adjacency requirements | RIBA Stage 3 and 4 |
| Anti-Ligature Audit | Healthcare | `AntiLigatureAuditCommand` | Checks anti-ligature product ratings in mental health rooms | Design and pre-commissioning |
| RDS Completeness Check | Healthcare | `RdsCompletenessCheckCommand` | Lists missing mandatory parameters for Room Data Sheets | Before RDS generation |
| Batch Issue RDS | Healthcare | `BatchIssueRoomDataSheetsCommand` | Generates .docx Room Data Sheets for all clinical rooms | RIBA Stage 4 onwards |
| Acoustic Audit | Healthcare | `AcousticAuditCommand` | Checks NR, STC, and RT60 targets | RIBA Stage 4 |
| Structural Load Audit | Healthcare | `StructuralLoadAuditCommand` | Checks floor loads for heavy medical equipment | RIBA Stage 4; before handover |
| RTLS Coverage Audit | Healthcare | `RtlsCoverageValidator` (via Run All) | Checks RTLS anchor coverage and RF dead zones | Design and commissioning |
| Endoscope Trace Audit | Healthcare | `EndoscopeTraceAuditCommand` | Checks HTM 01-06 RFID traceability chain | Design and commissioning |
| EES Resilience Dashboard | Healthcare | `EesResilienceDashboardCommand` | Reports NFPA 110 generator test log age, UPS dates | Ongoing facilities management |
| Run All Healthcare Validators | Healthcare | `RunAllHealthcareValidatorsCommand` | Runs all active healthcare validators in sequence | Before any significant drawing issue |
| Workflow Presets | BIM | `WorkflowPresetCommand` | Opens workflow preset runner | Commissioning and annual audits |

### Healthcare parameter groups — key parameters

**CLN_CLINICAL (Group 28 — binds to Rooms)**

| Parameter | Type | Key values |
|---|---|---|
| `CLN_ROOM_CLASS_TXT` | Text | `OR-ULTRA`, `AIIR`, `ICU`, `WARD-INPT`, `IMG-MRI`, `PSY-BED`, etc. |
| `CLN_PRESS_REGIME_TXT` | Text | `POS`, `NEG`, `NEUTRAL`, `POS-ISO`, `NEG-ISO` |
| `CLN_PRESS_DELTA_DESIGN_PA_NR` | Number | 8–25 Pa typical |
| `CLN_INFECT_CLASS_TXT` | Text | `STD`, `AIIR`, `PE`, `CLASS-N`, `CLASS-P` |
| `CLN_LIGATURE_RES_BOOL` | Yes/No | Activates anti-ligature validator |
| `CLN_MRI_ZONE_INT` | Integer | 1, 2, 3, or 4 |
| `CLN_HANDWASH_COUNT_INT` | Integer | HBN minimum |
| `CLN_WASTE_CLASS_TXT` | Text | `CLINICAL`, `CYTOTOXIC`, `RADIOACTIVE`, etc. |

**MGS_SYSTEMS (Group 29 — binds to Pipes, Mechanical Equipment)**

| Parameter | Type | Key values |
|---|---|---|
| `MGS_GAS_TYPE_TXT` | Text | `O2`, `MA4`, `MA7`, `VAC`, `N2O`, `AGS`, `N2`, `CO2`, `HE`, `DENT` |
| `MGS_NOM_PRESS_KPA_NR` | Number | 400 or 700 kPa typical |
| `MGS_DESIGN_FLOW_LPM_NR` | Number | Litres per minute (free air) |
| `MGS_VERIFY_PASS_BOOL` | Yes/No | Must be Yes before clinical use |
| `MGS_VERIFY_BY_TXT` | Text | ASSE 6030 certified verifier name |

**RAD_PROTECTION (Group 30 — binds to Walls, Doors, Windows)**

| Parameter | Type | Key values |
|---|---|---|
| `RAD_LEAD_MM_NR` | Number | Required mm Pb |
| `RAD_BARRIER_TYPE_TXT` | Text | `PRIMARY`, `SECONDARY`, `SCATTER`, `LEAKAGE` |
| `RAD_QE_NAME_TXT` | Text | Qualified Expert name — mandatory |
| `RAD_QE_DT` | Text (date) | QE sign-off date |

**CEQ_CLINICAL (Group 31 — binds to Mechanical Equipment, Specialty Equipment)**

| Parameter | Type | Key values |
|---|---|---|
| `CEQ_CATEGORY_TXT` | Text | `IMAGING`, `PENDANT`, `BEDHEAD`, `PHARMACY`, `DIAL` |
| `CEQ_INFECT_TIER_TXT` | Text | `CRITICAL`, `SEMI-CRITICAL`, `NON-CRITICAL` |
| `CEQ_CLINICAL_BOOL` | Yes/No | Must be Yes for COBie clinical asset register |

**LIG_BEHAVIOURAL (Group 32 — binds to Fixtures and Fittings)**

| Parameter | Type | Key values |
|---|---|---|
| `LIG_PRODUCT_RATING_TXT` | Text | `BS 9263 LR1`, `LR2`, `LR3`, `LR4` |
| `LIG_PRESSURE_REL_BOOL` | Yes/No | Whether fitting releases under load |
| `LIG_FORCE_LIMIT_KG_NR` | Number | Release load in kilograms |

### Facility profile comparison

| Validator | FULL | ACUTE | COMMUNITY | DENTAL | IMAGING-ONLY | MENTAL-HEALTH |
|---|---|---|---|---|---|---|
| PressureRegimeValidator | Yes | Yes | Yes | Partial | No | Yes |
| MgasFlowValidator | Yes | Yes | Yes | Yes (dental gas) | No | Yes |
| EesBranchValidator | Yes | Yes | No | No | Partial | Yes |
| WaterSafetyValidator | Yes | Yes | Yes | Yes | No | Yes |
| RadShieldValidator | Yes | Yes | No | No | Yes | No |
| AdjacencyValidator | Yes | Yes | No | No | No | Yes |
| AntiLigatureValidator | Yes | Partial | No | No | No | Yes |
| RdsCompletenessValidator | Yes | Yes | Yes | Yes | Yes | Yes |
| AcousticValidator | Yes | Yes | Partial | No | No | Yes |
| StructuralLoadValidator | Yes | Yes | No | No | Yes | No |
| AdvancedRadShieldValidator | Yes | Yes | No | No | Yes | No |
| IoTStalenessValidator | Yes | Yes | Partial | No | Partial | Partial |
| EndoscopeTraceValidator | Yes | Yes | Partial | No | No | No |
| EesResilienceValidator | Yes | Yes | No | No | No | No |
| RtlsCoverageValidator | Yes | Yes | No | No | Partial | Yes |
| WasteFlowValidator | Yes | Yes | Partial | No | No | Yes |

### Glossary of clinical and regulatory terms

| Term | Plain English explanation |
|---|---|
| ACH | Air Changes per Hour — the number of times the entire air volume of a room is replaced in one hour. An operating theatre needs 20 or more; a ward needs 6 or more. |
| AER | Automated Endoscope Reprocessor — the machine that washes and disinfects flexible endoscopes. |
| AIIR | Airborne Infection Isolation Room — a room kept at negative pressure to prevent infectious aerosols (from tuberculosis, measles, chickenpox, COVID-19) from escaping to the corridor. |
| ATS | Automatic Transfer Switch — the electrical device that switches from mains power to generator power when the mains fails. NFPA 99 requires this to happen within 10 seconds for life-safety circuits. |
| Augmented care | A term from HTM 04-01 for wards where patients are immunocompromised or have open wounds (ICU, transplant, NICU). These areas require stricter water safety controls including TMV3 and point-of-use filters. |
| ASSE 6030/6040 | American Society of Sanitary Engineers qualifications for medical gas pipeline system verifiers (6030) and maintenance personnel (6040). |
| Dead leg | A section of water pipework that is sealed at one end, like a cul-de-sac. Water in a dead leg stagnates and can harbour Legionella bacteria. HTM 04-01 limits dead legs to 1 m maximum in sentinel circuits and augmented care areas. |
| EES | Essential Electrical System — the category of electrical circuits in a hospital that must continue to function even when mains power fails. Divided into Life Safety (LS), Critical (CR), and Equipment (EQ) branches. |
| HEPA | High Efficiency Particulate Air — a type of filter that removes at least 99.97% of particles ≥ 0.3 µm. Grade H13 or H14 is required in operating theatres and protective environment rooms. |
| IPS | Isolated Power System — an electrical system where the supply conductors are isolated from earth, used in wet locations (OR, ICU, wet procedure rooms) to prevent dangerous earth-fault currents. |
| IRR17 | Ionising Radiations Regulations 2017 (UK) — the primary legislation governing radiation protection in the UK. Requires Radiation Protection Advisers and Qualified Experts to oversee shielding design. |
| Legionella | A bacterium (Legionella pneumophila) that grows in warm water (25–50 °C) and causes Legionnaires' disease, a serious form of pneumonia. Hospital water systems must be managed to prevent Legionella growth. |
| Ligature point | Any fixture or fitting in a mental health ward to which a patient could attach a cord or rope. Anti-ligature design eliminates all such points. |
| MGPS | Medical Gas Pipeline System — the permanent pipework system that delivers medical gases and vacuum to clinical areas. |
| MRI Zone | MRI suites are divided into zones 1 to 4 based on proximity to the magnet. Zone 4 is the magnet room itself. No ferrous metal and no active wireless transmitters are permitted in Zones 3 or 4. |
| NCRP 147/151 | National Council on Radiation Protection and Measurements reports 147 (diagnostic X-ray shielding) and 151 (radiotherapy and advanced imaging shielding). These are the most widely used shielding calculation methods internationally. |
| NFPA 99 | National Fire Protection Association standard for health care facilities (US). Covers medical gas systems, essential electrical systems, isolated power systems, and emergency management. |
| Pb | Chemical symbol for lead — the most common shielding material for X-ray rooms. Shielding is expressed in millimetres of lead equivalent (mm Pb). |
| PE room | Protective Environment room — a room kept at positive pressure with HEPA filtration to protect immunocompromised patients (bone marrow transplant, haematology) from airborne organisms. |
| PPM | Planned Preventive Maintenance — scheduled maintenance carried out at regular intervals to prevent equipment failure. SFG20 is the standard PPM schedule library used in the UK. |
| Pressure cascade | The sequence of pressure relationships from one side of a department to the other. A valid cascade is one where pressures change monotonically (always in one direction). |
| QE | Qualified Expert — in UK radiation protection, a medical or health physicist who has formal qualifications to advise on and approve radiation shielding designs. Required by IRR17. |
| RDS | Room Data Sheet — a single document that captures all design requirements for one clinical room type. |
| Spaulding classification | A system that classifies medical devices into Critical, Semi-critical, and Non-critical categories based on their infection risk, determining the required level of decontamination. |
| SFG20 | Standard Form of Guidance for the Specification of Planned Preventive Maintenance for Building Engineering Services — the PPM schedule library used in UK healthcare facilities management. |
| TMV | Thermostatic Mixing Valve — a valve that blends hot and cold water to produce tempered water at a safe temperature (typically 38–43 °C for handwash, 41–46 °C for showering). TMV2 is a domestic standard; TMV3 is the higher-performance healthcare standard required in augmented care areas. |
| ZVB | Zone Valve Box — the assembly of shut-off valves that can isolate one zone of the medical gas pipeline system without affecting other zones. |

---

## Cross-references

This guide covers the healthcare-specific layer of STING. For the underlying systems that healthcare design is built on, see:

- **MEP_FOUNDATION_GUIDE.md** — for MEP symbol creation (Chapter 1), family authoring (Chapter 2), and the Placement Centre for placing clinical equipment families (Chapter 3).
- **ELECTRICAL_WORKFLOW_GUIDE.md** — for electrical systems design including nurse call systems, fire alarm systems, emergency lighting, and security systems. This guide covers only the healthcare EES/IPS classification layer on top.
- **PLUMBING_WORKFLOW_GUIDE.md** — for plumbing and piping systems including MGPS pipe routing, water systems, and drainage. This guide covers the healthcare-specific validation layer (MGPS verification, water safety audit).
- **DRAWING_PRODUCTION_SYSTEM_GUIDE.md** — for the full drawing production workflow: creating views, applying drawing types, running ViewStylePacks, managing sheets, and producing the drawing register.
- **DOCUMENT_MANAGER_GUIDE.md** — for document issue, revision management, transmittals, COBie export, and the ISO 19650 document lifecycle. Room Data Sheets and commissioning records are issued through this system.
