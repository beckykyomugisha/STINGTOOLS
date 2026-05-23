# Healthcare Pack — Plain-English Guide for Hospital Projects

> **Audience:** clinicians, estates managers, BIM coordinators, project
> managers, healthcare planners, and anyone who needs to use STINGTOOLS
> or the Planscape mobile app on a hospital, mental-health unit, clinic,
> dental practice, or other healthcare facility.
> **Plugin:** StingTools for Autodesk Revit 2025 / 2026 / 2027.
> **Mobile:** Planscape app (iOS + Android, Expo SDK 52).
> **Server:** Planscape (ASP.NET Core 8, runs in a Docker stack).
>
> No prior Revit knowledge is assumed. Every term is explained. Every
> button has a "why" attached.

---

## Quick map — where to find what

| You want to… | Look in |
|---|---|
| See what STINGTOOLS does for healthcare | §1 *What is the Healthcare Pack?* |
| Understand a code or acronym | §2 *Glossary* |
| Open the dock panel for the first time | §4 *Day-1 setup* |
| Find a single tool | §5 *Desktop UI tour* |
| Use the phone app on-site | §6 *Mobile app tour* |
| Audit one specific thing (pressure, gas, water…) | §7 *The 16 validators in plain English* |
| Look up a code reference (HTM 03-01, NFPA 99, etc.) | §8 *Standards in plain English* |
| Run a typical workflow end-to-end | §9 *Common workflows* |
| Troubleshoot | §10 *Troubleshooting & FAQ* |
| See who has to sign what | §11 *Sign-offs and certifications* |

---

## 1. What is the Healthcare Pack?

A hospital is the most heavily-regulated building you can design. A
single floor of an acute hospital can carry **medical-gas pipework**
(for oxygen, vacuum, anaesthetic gases), **clean-air zones** (operating
theatres need 20+ air changes per hour, ultra-clean theatres 300+),
**radiation shielding** (CT, MRI, X-ray, LINAC), **infection-control
rooms** (negative-pressure isolation, positive-pressure protective
environments), **emergency-power branches** (life-safety, critical,
equipment), **water-safety regimes** (TMV3 mixing valves, dialysis RO
loops, sentinel temperature points), **fire compartments** (with PHE —
progressive horizontal evacuation — refuges), **anti-ligature fittings**
(in mental-health units), and **clinical equipment** (pendants,
bedheads, scrub troughs, infusion pumps, imaging modalities).

Each of those domains has a separate set of rules — usually at least
one British (HTM / HBN), one American (FGI / NFPA / ASHRAE / USP), one
international (ISO / iHFG), and one trade body (SFG20, BS EN). A
single-bed AIIR (Airborne Infection Isolation Room) might be governed
by **eleven different documents** at the same time.

The **Healthcare Pack** is a content layer on top of STINGTOOLS that:

1. **Knows** all those standards. It carries the rule numbers and the
   threshold values inside it.
2. **Tags** every clinical element correctly (the room, the door, the
   medical gas terminal unit, the X-ray barrier, the fitting).
3. **Validates** the model against the standards. It tells you in plain
   English: *"AIIR room 4-12 is set to positive pressure but HTM 03-01
   requires negative."*
4. **Generates** the documents and reports auditors expect: Room Data
   Sheets, MGPS verification certificates, COBie spreadsheets,
   commissioning files.
5. **Talks to the mobile app** so a commissioning engineer on-site can
   walk through the NFPA 99 §5.1.12 12-step gas verification, log it,
   and sync it back to the model.

You don't have to use all of it. There are six **profiles** —
`FULL` / `ACUTE` / `COMMUNITY` / `DENTAL` / `IMAGING-ONLY` /
`MENTAL-HEALTH` — that switch on only the rules relevant to your
facility type, so a community dental practice doesn't get told to fix
its LINAC vault shielding (because it has no LINAC).

---

## 2. Glossary — every term, every acronym

Think of this as the Rosetta Stone for healthcare BIM. Tabs run
alphabetically.

### General BIM

| Term | What it means in plain English |
|---|---|
| **BIM** | Building Information Modelling — a 3D model with data attached to every element (manufacturer, cost, fire rating, etc.). |
| **CDE** | Common Data Environment — a shared digital folder system where project files move through stages: Work-In-Progress → Shared → Published → Archive. |
| **COBie** | Construction Operations Building Information Exchange — an Excel spreadsheet format used to hand a building's data over to the facilities manager at the end of a project. Healthcare projects use a richer "COBie healthcare overlay" that carries 50 + clinical-equipment types and 35 + maintenance schedules. |
| **Element** | A single thing in the Revit model — a wall, a door, a room, a duct, a medical-gas terminal unit, etc. |
| **Family** | A reusable Revit definition. *Family of doors* = the door type with all its sizes; placing one in the model creates a family *instance*. |
| **ISO 19650** | The international standard for managing BIM information over a building's whole lifecycle. The reason every element has a coded tag like `H-BLD1-Z02-L03-HVAC-SUP-AHU-0042`. |
| **Parameter** | A field of data on an element — `FIRE_RATING`, `CLN_PRESS_REGIME_TXT`, `MGS_GAS_TYPE_TXT`. |
| **RAG** | Red / Amber / Green — traffic-light status. Red = problem, Amber = needs attention, Green = OK. |
| **RFA** | Revit Family file — `.rfa` extension. The file that defines a reusable item like a door type or a medical-gas terminal unit. |
| **Tag** | A short label that appears in views (drawings) showing an element's coded ID. STING tags use 8 segments: `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ`. |

### Healthcare-specific abbreviations

| Term | Stands for | What it means |
|---|---|---|
| **ACH** | Air Changes per Hour | How many times the air in a room is fully replaced every hour. Operating theatres need 20 +; ultra-clean theatres 300 +. |
| **ADB** | Activity DataBase | NHS database of "what should be in this kind of room". STING references ADB room codes so you don't reinvent the wheel. |
| **AE** | Authorising Engineer | A named person formally responsible for one engineering domain (Ventilation, Medical Gas, Water, Electrical) on a healthcare project. |
| **AED** | Automated External Defibrillator | The wall cabinet you've seen in airports — STING tags them so they're countable. |
| **AGS** | Anaesthetic Gas Scavenging | Pipework that removes waste anaesthetic gases from operating theatres. |
| **AIIR** | Airborne Infection Isolation Room | A room kept at *negative pressure* to stop airborne infections leaving — used for TB, COVID, etc. |
| **ASHRAE** | American Society of Heating, Refrigerating and Air-Conditioning Engineers | American HVAC standards body. *ASHRAE 170* sets ventilation rules for healthcare. |
| **ASSE** | American Society of Sanitary Engineering | The body that certifies medical-gas verifiers (qualification *ASSE 6030*). |
| **ATS** | Automatic Transfer Switch | The relay that flips electrical supply from mains to generator when the power fails. NFPA 99 says it must do so in ≤ 10 seconds for life-safety circuits. |
| **BACnet** | Building Automation and Control Networks | A protocol used by Building Management Systems (BMS) to read pressure / temperature sensors. STING can read live values from BACnet. |
| **BMS** | Building Management System | The control system that runs HVAC, lighting, etc. STING does **read-only** BMS reads — never writes. |
| **BS** | British Standard | E.g. *BS 5682* = standard for medical-gas terminal units. |
| **C-PEC** | Containment Primary Engineering Control | A Class II Biosafety Cabinet or CACI used to compound hazardous drugs in pharmacy (USP <800>). |
| **C-SEC** | Containment Secondary Engineering Control | The room that *contains* a C-PEC. Must be at negative pressure to the anteroom. |
| **CDC** | Centers for Disease Control (USA) | Issues guidance on isolation-room ventilation and infection control. |
| **CFU** | Colony Forming Units | A measure of bacterial contamination in air. HTM 03-01: ≤ 10 CFU/m³ in operating theatres. |
| **CIBSE** | Chartered Institution of Building Services Engineers | UK building-services standards body. Sister to ASHRAE. |
| **COBie** | (see above — the healthcare overlay adds 50 + clinical-equipment types). |
| **EES** | Essential Electrical System | The backup-powered electrical network in a hospital. NFPA 99 splits it into three branches: **Life Safety** (egress / alarms), **Critical** (patient care), **Equipment** (HVAC and elevators). |
| **EHR** | Electronic Health Record | The patient database (Epic, Cerner, etc.). Outside scope of BIM but referenced by clinical IT. |
| **FGI** | Facility Guidelines Institute | American counterpart to HBN — issues the **Guidelines for Design and Construction of Hospitals**. The 2026 edition becomes the *Facility Code* (legally enforceable). |
| **GMDN** | Global Medical Device Nomenclature | A coded classification for medical devices. STING tags clinical equipment with GMDN codes. |
| **HBN** | Health Building Note | NHS document giving design guidance for a *type of facility* (HBN 04-01 = adult inpatient, HBN 09-02 = critical care, HBN 11-01 = primary care, etc.). |
| **HBO** | Hyperbaric Oxygen | A pressurised oxygen chamber for treating dive injuries, carbon-monoxide poisoning, etc. Has special fire-safety rules (NFPA 99 Ch. 14). |
| **HEPA** | High-Efficiency Particulate Air | A grade of filter. **H13** = 99.95 % efficient at 0.3 µm; **H14** = 99.995 %. Used in operating theatres and pharmacy clean-rooms. |
| **HSDU** | Hospital Sterile Services Department | Where surgical instruments are washed, packed, and sterilised between operations. Has a strict dirty → clean → sterile flow. |
| **HTM** | Health Technical Memorandum | NHS document giving guidance on a *building service* (HTM 02-01 = medical gas, HTM 03-01 = ventilation, HTM 04-01 = water, etc.). |
| **iHFG** | International Health Facility Guidelines | Australian-led standard used in many international healthcare projects. |
| **IPS** | Isolated Power System | A medical-grade ungrounded electrical supply used in wet locations like operating theatres so a single fault doesn't cause shock. NEC 517.19. |
| **IRR17** | Ionising Radiations Regulations 2017 (UK) | UK law on radiation safety. *Controlled* and *supervised* areas, dose limits, Qualified Expert sign-off. |
| **MGPS / MGS** | Medical Gas Pipeline System | The piped delivery of oxygen, medical air, surgical air, nitrous oxide, vacuum, etc. throughout a hospital. |
| **MRI** | Magnetic Resonance Imaging | Imaging modality with a very strong magnetic field. Has 4 safety **zones** (Z1 – outside, Z4 – the magnet room itself). |
| **NCRP** | National Council on Radiation Protection (USA) | Issues shielding standards. *NCRP 147* = X-ray facilities, *NCRP 151* = megavoltage radiotherapy. |
| **NFPA** | National Fire Protection Association (USA) | American standards body. *NFPA 99* is the Healthcare Facilities Code (medical gas, electrical, hyperbaric, etc.). *NFPA 110* covers standby power. |
| **NICU** | Neonatal Intensive Care Unit | Critical-care unit for newborns. HBN 09-03. |
| **OPC-UA** | Open Platform Communications — Unified Architecture | Industrial protocol used for MRI quench monitors, advanced BMS. STING can subscribe to OPC-UA events. |
| **PE** | Protective Environment | A *positive-pressure* isolation room used to protect immunosuppressed patients (e.g. bone-marrow transplant). The reverse of an AIIR. |
| **PHE** | Progressive Horizontal Evacuation | Hospital evacuation strategy: move patients sideways into the next fire compartment (a *refuge*) instead of down the stairs, which is impossible for non-ambulant patients. |
| **PPM** | Planned Preventative Maintenance | Routine maintenance done on a schedule (monthly, annually). SFG20 publishes PPM schedules. |
| **QE** | Qualified Expert | A medical / health physicist who signs off radiation shielding designs. STING does not certify shielding — it produces drafts for the QE. |
| **RDS** | Room Data Sheet | A one-page A2 document listing every requirement for a single room: equipment, services, finishes, environmental envelope. STING's RDS engine generates these from the model. |
| **RTLS** | Real-Time Location System | Tags + readers that track equipment / staff / patients live. Uses BLE, UWB, Wi-Fi, IR or RFID. |
| **SFG20** | The standard for building maintenance specifications | A subscription library of maintenance schedules used across UK FM. STING references the schedule IDs. |
| **TMV** | Thermostatic Mixing Valve | A valve that blends hot and cold water to a safe temperature so patients don't get scalded. **TMV3** = the medical-grade variant for augmented-care (e.g. burns units, hospices). |
| **USP** | United States Pharmacopoeia | Issues *USP <797>* (sterile compounding) and *USP <800>* (hazardous drugs). Defines the cleanroom pressure cascade for hospital pharmacies. |
| **VIE** | Vacuum Insulated Evaporator | A bulk liquid-oxygen tank that supplies the hospital's medical-gas pipeline. |
| **W·U·T** | Workload × Use factor × Occupancy factor | The three inputs to NCRP 147 shielding calculations. |
| **ZVB** | Zone Valve Box | A wall box containing the shut-off valves for medical gases serving one clinical zone. Required by HTM 02-01. |

### Tag codes used by STING

| Code | What it means |
|---|---|
| **DISC** | Discipline letter. `H` = Healthcare, `MG` = Medical Gas, `RP` = Radiation Protection (the three new ones), plus existing `M / E / P / A / S / FP / LV / G`. |
| **LOC** | Location code. `BLD1`, `BLD2`, `EXT`. |
| **ZONE** | Zone code. `Z01–Z04`. |
| **LVL** | Level code. `L01`, `GF`, `B1`, `RF`. |
| **SYS** | System code. For healthcare: `MGS-O2 / MGS-MA4 / MGS-MA7 / MGS-N2O / MGS-VAC / MGS-AGS / EES-LS / EES-CR / EES-EQ / IPS-WET / IT-CARD / WAT-DCW-TMV`, etc. |
| **FUNC** | Function code. `LIFE-SAF / CRIT / EQP-BR / IPS / IT-CP / DECON-DTY / DECON-CLN / SCRUB / MORT`. |
| **PROD** | Product code. `BHP` (bedhead pendant), `CP-OR` (OR ceiling pendant), `ZVB` (zone valve box), `AAP` (area alarm panel), `MAP` (master alarm panel), `TU-O2` (oxygen terminal unit), `MRI`, `LIN`, etc. |
| **SEQ** | A 4-digit serial number making the tag unique. |

A typical tag for an oxygen terminal unit on the second-floor ICU of
building 1 might be: `MG-BLD1-Z02-L02-MGS-O2-SUP-TU-O2-0007`.

### Clinical room classes

When you set `CLN_ROOM_CLASS_TXT` on a Room, you pick from this list.
The validators then know what rules apply.

| Class | Meaning |
|---|---|
| `OR-ULTRA` | Ultra-clean operating theatre (orthopaedic / neuro). 300 ACH, HEPA H14, +25 Pa. |
| `OR-CONV` | Conventional operating theatre. 20 ACH, +25 Pa. |
| `OR-HYBRID` | Hybrid theatre — surgery + imaging. ≥ 70 m² (FGI). |
| `CATHLAB` | Cardiac catheterisation lab. |
| `IR` | Interventional radiology. |
| `ICU / HDU` | Intensive Care Unit / High Dependency Unit. |
| `NICU / MAT-LDR` | Neonatal ICU / Maternity Labour-Delivery-Recovery. |
| `AIIR / PE-PROT` | Negative-pressure isolation / positive-pressure protective env. |
| `ANTERM` | Anteroom — buffer between an isolation room and the corridor. |
| `EXAM / TREAT / CONS` | Exam, treatment, consultation rooms. |
| `IMG-CT / IMG-MRI / IMG-XRY / IMG-LIN / IMG-PET` | Imaging modalities. |
| `PH-CSP-797 / PH-CSP-800` | USP <797> sterile / <800> hazardous compounding. |
| `LAB-WET / LAB-DRY / BSL2 / BSL3` | Laboratories. |
| `MORT / POST` | Mortuary / post-mortem. |
| `DECON-D / DECON-C / HSDU-W / HSDU-P` | HSDU dirty / clean / wash / pack. |
| `DIAL` | Dialysis station. |
| `PSY-BED / PSY-OBS / SECL` | Mental-health bed / observation / seclusion. |
| `BARI` | Bariatric room. |
| `HBO` | Hyperbaric oxygen chamber suite. |

---

## 3. The lifecycle — where the Healthcare Pack fits

UK projects follow **RIBA stages 0 – 7**, US projects follow **AIA
phases SD / DD / CD / CA / FM**. The Healthcare Pack works across all
stages but its centre of gravity is from late design through commissioning
to handover.

| Stage | What you do | What the Pack does for you |
|---|---|---|
| **0 / 1 — Strategic brief** | Decide bed numbers, OR count, facility type | Set `PRJ_ORG_HEALTH_FACILITY_TYPE_TXT` (Acute / Community / Mental / Day) and `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` (FULL / ACUTE / COMMUNITY / DENTAL / IMAGING-ONLY / MENTAL-HEALTH) — switches on the right validators |
| **2 / 3 — Concept + spatial** | Department adjacencies, gross room areas | **AdjacencyValidator** flags HBN-violating layouts (mortuary near OPD, OR not adjacent to HSDU) |
| **4 — Technical design** | Detailed MEP, shielding, gases | All 16 validators; **Drawing Type Manager** auto-generates the right plan / RCP / shielding sheet from each view |
| **5 — Construction** | Mobile app on-site | Anti-ligature audits, water-flush logs, MGPS verification walkthroughs; pressure-cascade checks once BMS is alive |
| **6 — Handover** | Commissioning + COBie | **HealthcareCommissioning** workflow chains the validator sweep + RDS rendering + COBie export with the healthcare overlay |
| **7 — In use / FM** | Recurring audits | NFPA 110 generator-test log freshness, HTM 04-01 annual water audit, anti-ligature audits |

---

## 4. Day-1 setup

You've just opened the Revit model for the first time. Here's the
five-minute kick-off.

### 4.1  Tell STING this is a healthcare project

1. Open **Project Information** (Manage tab → Project Information).
2. Find `PRJ_ORG_HEALTH_FACILITY_TYPE_TXT`. Type one of:
   `Acute / Mental / Community / Rehab / Day / FM`.
3. Find `PRJ_ORG_HEALTH_PACK_PROFILE_TXT`. Type one of
   `FULL / ACUTE / COMMUNITY / DENTAL / IMAGING-ONLY / MENTAL-HEALTH`.
4. Find `PRJ_ORG_HEALTH_CODE_BASE_TXT`. Type `HBN/HTM` (UK) or `FGI`
   (US) or `iHFG` (international).

**Why?** Many of the Pack's behaviours are gated on these stamps. The
BIM Coordination Centre's **HEALTHCARE** tab only appears once
`PRJ_ORG_HEALTH_FACILITY_TYPE_TXT` is non-empty. The validator chain
returns zero findings on a non-healthcare project (so you can have one
firm-wide Revit setup that serves offices and hospitals without paying
the validator cost on the office work).

### 4.2  Run **Master Setup**

1. Open the dock panel (View → User Interface → STING Tools).
2. Go to the **TEMP** tab → **Setup** → **Master Setup**.
3. Click. Wait. It loads ~ 2,800 shared parameters (including all the
   healthcare ones), 815 building-element materials, 464 MEP materials,
   168 schedules, 28 filters, 35 worksets, 23 view templates, 10 line
   patterns, 6 phases. About 2 minutes on a fast machine.

**Why?** The Pack adds 5 new parameter groups (`CLN_CLINICAL`,
`MGS_SYSTEMS`, `RAD_PROTECTION`, `CEQ_CLINICAL`, `LIG_BEHAVIOURAL`) and
~ 100 new shared parameters. Without them, the Rooms in the model have
no `CLN_ROOM_CLASS_TXT` field for you to fill in.

### 4.3  Tag every clinical room

For each clinical room:

1. Select the Room.
2. In Properties → look for `CLN_ROOM_CLASS_TXT`.
3. Type the right class code (see §2 *Clinical room classes*).
4. Fill in the rest as needed: `CLN_PRESS_REGIME_TXT`,
   `CLN_INFECT_CLASS_TXT`, `CLN_HBN_REF_TXT`, etc.

Or use the **Auto-Tag** command on the dock panel's **CREATE** tab to
infer codes from the room name (e.g. a room called *"OR 4"* will be
auto-classified as `OR-CONV`).

### 4.4  Verify

Open the dock panel **HEALTHCARE** tab. Click **Run All Healthcare**.
You should see a count of findings. Errors and warnings come out
sorted with errors first.

---

## 5. Desktop UI tour — the dock panel

After commit `4620b579` the dock panel has a **HEALTHCARE** tab (10th
tab, after `EXLINK`). It's organised into 5 sections.

### 5.1  Validation Chain (17 buttons)

| Button | What it checks | Why |
|---|---|---|
| **Run All Healthcare** | Every validator below, gated by your pack profile | The one-shot daily check |
| **Pressure Regime** | Each clinical room has the right ± Pa and air changes per hour | Patients in AIIR rooms must be in negative pressure or staff and other patients can be infected |
| **MGPS Audit** | Walks the medical-gas network: counts terminal units, zone valve boxes, alarm panels per gas | Catches missing alarms and unconnected pipework before the gas company tests it |
| **MGPS Verify** | Walks you through the 12-step NFPA 99 §5.1.12 verification with PASS/FAIL per step | Required by code before the hospital can use the gas |
| **EES Branches** | Every patient-care receptacle is on Life-Safety or Critical branch; ATS time ≤ 10 s | If an outlet powering a ventilator is on the wrong branch, the ventilator stops when mains fail |
| **Water Safety** | TMV3 fitted on augmented-care outlets, sentinel dead-leg ≤ 1 m, RO-loop integrity | Prevents legionella + scalding + dialysis water contamination |
| **Rad Shield** | Required vs provided mm Pb on every barrier element | Stops X-ray scatter dosing the public |
| **Adv Rad** | Flags PET / SPECT / brachytherapy rooms designed with Pb-only (need concrete too because 511 keV is much harder than diagnostic X-ray) | Diagnostic-X-ray Pb thickness is 10× too low for PET — easy mistake |
| **Adjacency / Flow** | HBN adjacency targets + door-graph BFS for clean / dirty crossing | Linen / waste flows must not enter a sterile zone — required by HBN 13 |
| **Anti-Ligature** | Every fitting in a ligature-resistant room has a `LIG_PRODUCT_RATING_TXT` | Mental-health units need every door handle, tap, curtain track to be anti-ligature |
| **Structural Loads** | MRI / CT / LINAC rooms have ≥ required floor load (kN/m²) and right vibration band | A 5,500 kg MRI dropped through a slab is not a recoverable mistake |
| **Acoustic** | NR / RT60 vs HTM 08-01 target per room class | NICU < NR 35 (babies are sensitive), counselling rooms have privacy |
| **Endoscope Trace** | Every endoscope decon room has ≥ 4 RFID readers (soak / AER / drying / storage) | HTM 01-06 traceability for reprocessed scopes |
| **EES Resilience** | Generator + ATS test logs not older than 35 days; UPS battery < 4 years | NFPA 110 weekly generator exercise; UPS dies silently after 4 years |
| **RTLS Coverage** | RTLS readers placed inside MRI Z3/Z4 (forbidden — wireless transmitter) or in RF-shielded rooms (signal can't get out) | Bad placement = infant tag goes silent in the magnet room — false sense of security |
| **Waste Flow** | Each room's waste class (HC1 General – HC6 Radioactive) is set; radioactive waste rooms are IRR17-controlled | Per HTM 07-01 |
| **IoT Devices** | Inventory of every BMS / smart-TMV / RTLS / pressure-sensor device by protocol | The estate manager has to know what's on the network |
| **IoT Staleness** | Devices not seen in > 30 minutes | Catches dead sensors |

### 5.2  Room Data Sheets (3 buttons)

| Button | Why |
|---|---|
| **Issue RDS (one room)** | Pick a Room, get a one-page A2 .docx with all its services, equipment, finishes, environmental envelope. Hand to the clinical user for sign-off. |
| **Batch RDS (all)** | Same for every clinical room — typically 200–800 docs on a hospital, written to `<project>/_BIM_COORD/healthcare/rds/`. |
| **RDS Completeness** | Audits which rooms are missing the 14 mandatory RDS parameters (occupancy, ACH, temperature, RH, finishes, etc.). |

### 5.3  Radiation calculators (4 buttons)

These are **drafts for the Qualified Expert** — STING never certifies
radiation shielding. They're worked examples that help the QE check
their own arithmetic.

| Button | What it does |
|---|---|
| **Chest Room Calc** | NCRP 147 example for a chest-radiography room (uncontrolled area, 125 kVp, W = 50 mA·min/wk, U = 1, T = 1, d = 2 m) |
| **CT Room Calc** | NCRP 147 secondary-barrier example (controlled area, 150 kVp, W = 600, U = 0.25, T = 0.5, d = 3 m) |
| **LINAC Vault** | NCRP 151 first-pass calc for a 10 MV linear accelerator vault (concrete TVL = 450 mm) |
| **MRI Zoning** | Audits each MRI suite for Z1–Z4 designation + Faraday-cage flag |

### 5.4  Specialist audits (8 buttons)

These are quick dedicated checks for one clinical context.

| Button | Standard |
|---|---|
| **Hybrid OR / Cath** | FGI / VA hybrid-OR area minimums (≥ 70 m²) |
| **Pharmacy USP** | USP <797> + <800> pressure cascade + ACH ≥ 30 |
| **Behavioural** | FGI Pt 2 / HBN 03-01 anti-ligature + risk + privacy |
| **Mortuary** | HBN 16 capacity (0.5 % of beds, min 4 fridge bays) |
| **Maternity / NICU** | HBN 21 / 09-03 room areas + NICU NR ≤ 35 |
| **HSDU** | HBN 13 wash / pack / sterile compartment polarity |
| **Dialysis** | HBN 07-02 RO-loop integrity |
| **Hyperbaric / IVF** | NFPA 99 Ch. 14 + USP cytotoxic + IVF |

### 5.5  Workflow Presets (2 buttons)

| Button | Why |
|---|---|
| **List Workflows** | Shows every workflow (the 8 healthcare ones plus all the existing ones). You can pick one and run its chained steps. |
| **Workflow Trend** | A 100-run history of how long each workflow took, what compliance % it left the model at — useful for regression tracking. |

The 8 healthcare workflows:

| Workflow | What it chains |
|---|---|
| `HealthcareCommissioning` | Run all validators → Batch RDS → Adjacency audit → MGPS audit → COBie export |
| `MgasVerification` | MGPS audit → 12-step verification with sign-off |
| `PressureRegimeAudit` | Pressure validator + adjacency check (anteroom links) |
| `RdsIssue` | FullAutoPopulate → Batch RDS → RDS completeness check |
| `HTM-04-01-Annual` | Water-safety validator → re-issue RDS for every clinical room |
| `AntiLigatureAudit` | Anti-lig validator + behavioural-health audit |
| `NFPA110-GeneratorTest` | EES resilience log freshness check |
| `HTM-01-06-EndoReprocess` | Endoscope RFID-chain audit |

---

## 6. Mobile app tour

The mobile app sits in the *Planscape* React-Native app. From the
**Dashboard** tab tap the **🏥 Healthcare** quick-action card. Five
sub-screens.

### 6.1  Healthcare Dashboard

A list of 8 RAG cards (Pressure / MGPS / EES / Water / Rad / Anti-Lig /
RDS / Adjacency). Pulls from `/api/projects/{id}/healthcare/dashboard`
on the Planscape Server. Each tappable card deep-links to the worker
screen.

### 6.2  MGPS Verification (NFPA 99 §5.1.12)

The 12-step walkthrough. PASS/FAIL each step. Save Verification submits
to `/api/projects/{id}/healthcare/mgas-verification`. The verifier's
name (whoever logged in) and the device location are recorded.

**Why a phone?** The verifier walks the gas plant + every zone-valve
box. They need a single-hand UI; a laptop is impractical in plant
rooms.

### 6.3  Pressure (live)

When the desktop plugin's BACnet bridge is running, this shows live
Δp per room. Designed to be open on a tablet during commissioning so
you can stand in the AIIR room and watch the cascade.

### 6.4  Water Flushing (HTM 04-01 sentinel log)

Outlet ID + temperature + duration. Local list of recent entries.
On a real flush week the FM team logs ~ 200 outlets per day.

### 6.5  Anti-Ligature audit

Per-fitting checklist with photo and GPS. PASS / FAIL plus notes.
For mental-health units the standard is to audit every fitting in
every patient bedroom every 6 months.

### 6.6  Room Data Sheet viewer

Type a Room ID, get a read-only summary of that room's RDS pulled from
`/api/projects/{id}/healthcare/rds/{roomBimId}`. Useful for handover —
the clinical user can see exactly what was specified for their room.

---

## 7. The 16 validators in plain English

Each validator returns findings of three severities:

- **Error** — a code violation. Must be fixed.
- **Warning** — a probable problem. Almost always must be fixed.
- **Info** — note for the coordinator's diary.

### 7.1  PressureRegimeValidator (HTM 03-01 / ASHRAE 170)

Reads each room's `CLN_ROOM_CLASS_TXT`, looks up the design rule
(positive / negative / neutral, ACH, Δp), compares to the actual
`CLN_PRESS_REGIME_TXT` and `HVC_AIR_CHANGES_PER_HR` you've authored.

Catches:

- AIIR rooms set to positive pressure (would spread infection).
- OR rooms with < 20 ACH (HTM minimum).
- AIIR / PE rooms without a paired anteroom.

### 7.2  MgasFlowValidator (HTM 02-01 / NFPA 99)

Walks every Pipe / Pipe Fitting / Plumbing Fixture / Mechanical
Equipment with a `MGS_GAS_TYPE_TXT` set.

Catches:

- Nominal pressure drift > 10 % from the gas's spec (MA7 should be
  700 kPa, not 400).
- BS 5682 terminal units missing the indexing flag.
- Pipework with no inert-gas brazing flag.
- Verification records that failed.

### 7.3  EesBranchValidator (NFPA 99 §6.4 / NEC 517)

For every electrical fixture / equipment in a patient-care room: which
branch is it on, what's the ATS time, is IPS required.

Catches:

- A bedside outlet on Normal branch in an ICU bay.
- ATS time > 10 s on a Critical branch.
- Wet-location rooms (OR, ICU, cath lab) without IPS flag.

### 7.4  WaterSafetyValidator (HTM 04-01)

Plumbing fixtures + pipework + dialysis stations.

Catches:

- Sentinel point with a dead-leg > 1 m.
- Augmented-care outlet without point-of-use filter (Pseudomonas
  risk — HTM 04-01 Pt C).
- TMV outlet outside 38–41 °C.
- Dialysis station not flagged as RO-loop member.

### 7.5  RadShieldValidator (NCRP 147)

Every wall / door / window with a `RAD_BARRIER_TYPE_TXT`. Computes the
required mm Pb from W·U·T at 2 m default (real distance is filled by
the QE workflow command).

Catches:

- Provided mm Pb < required.
- Primary barriers with no QE sign-off (`RAD_QE_NAME_TXT` empty).

**Caveat:** the Archer α/β/γ constants are *approximate digitisations*
of NCRP 147 tables. Real designs use the published values via the QE.

### 7.6  AdvancedRadShieldValidator (PET / SPECT / brachytherapy)

The 511 keV photons from PET are about 4 × harder than diagnostic
X-ray. Pb thickness derived from NCRP 147 (kV) curves under-shields
PET rooms.

Catches:

- PET / NM / brachytherapy rooms with a `RAD_LEAD_MM_NR` value but no
  QE sign-off (energy correction missing).

### 7.7  AdjacencyValidator (HBN-derived)

Compares room-pair distances to the HBN target matrix.

Catches:

- ED not adjacent to imaging.
- OR not adjacent to HSDU.
- Mortuary too close to OPD or wards.

### 7.8  AntiLigatureValidator (HBN 03-01 / FGI Pt 2)

Every door / sink / light / data device inside a `CLN_LIGATURE_RES_BOOL`
room.

Catches:

- Fitting without a `LIG_PRODUCT_RATING_TXT` (BS 9263 LR1–LR4).
- Risk level not set on a ligature-resistant room.

### 7.9  RdsCompletenessValidator

The 14 parameters every clinical room must have populated before its
RDS can be issued: number, name, area, occupancy, ACH, temp, RH,
finishes (floor, ceiling, wall), illuminance, noise, fire compartment.

### 7.10  IoTStalenessValidator

Devices in `IoTDeviceRegistry` whose `LastSeenUtc` is older than 30
minutes. Currently inert (the BACnet/OPC-UA transports are stubs) but
will fire once a real transport is plugged in.

### 7.11  StructuralLoadValidator

Imaging room floor loads + vibration limits per modality.

| Room class | Min kN/m² | Vibration limit (µm/s) |
|---|---|---|
| `IMG-MRI` | 15 | 8 (VC-C) |
| `IMG-CT` | 12 | 25 (VC-B) |
| `IMG-LIN` | 30 | 50 |

### 7.12  AcousticValidator (HTM 08-01)

NR rating + RT60 per room class. NICU NR ≤ 35, exam rooms STC ≥ 45,
private consultation rooms STC ≥ 50.

### 7.13  EndoscopeTraceValidator (HTM 01-06)

Endoscopy decon rooms need ≥ 4 RFID reader positions (soak sink,
washer-disinfector inlet, drying cabinet, sterile storage) so a scope
can be tracked from contaminated arrival to sterile use.

### 7.14  EesResilienceValidator (NFPA 110)

Generator test log + ATS test log + UPS replacement-date freshness.

| Field | Max age |
|---|---|
| `ELC_GEN_TEST_LOG_REF_TXT` | 35 days |
| `ELC_ATS_TEST_LOG_REF_TXT` | 35 days |
| `ELC_UPS_REPLACE_DT` | 4 years |

### 7.15  RtlsCoverageValidator

RF physics check.

Catches:

- BLE / Wi-Fi / UWB anchor inside a room with `CLN_RF_SHIELD_BOOL = 1`
  (Faraday cage — signal can't escape).
- Any wireless anchor inside MRI Z3/Z4 (forbidden — strong magnet).

### 7.16  WasteFlowValidator (HTM 07-01)

Each room's `CLN_WASTE_CLASS_TXT` is one of HC1-General / HC2-Offensive
/ HC3-Infectious / HC4-Anatomical / HC5-Cytotoxic / HC6-Radioactive.
Radioactive waste rooms must be IRR17-controlled.

---

## 8. Standards in plain English

| Standard | Plain English |
|---|---|
| **HTM 00** | NHS umbrella document — "best practice for healthcare buildings". Refers out to the per-domain HTMs. |
| **HTM 02-01** | Medical gas pipeline systems. The Bible for MGPS in the UK — pipe materials (copper Cu-Y / Cu-X), brazing, terminal units, alarms, zone valve boxes, verification. |
| **HTM 03-01** | Specialist ventilation. Ultra-clean theatres ≥ 300 ACH, conventional ≥ 20, AIIR negative pressure with HEPA on exhaust, PE positive with HEPA on supply. |
| **HTM 04-01** | Safe water. Three parts: A (design), B (operational), C (Pseudomonas in augmented care). Dead-legs, TMV3, sentinel temperature monitoring, dialysis water, flushing regimes. |
| **HTM 05-01 / 05-02** | Firecode. Compartmentation, PHE refuges, smoke management. |
| **HTM 06-01** | Electrical services. UK equivalent to NFPA 99 + NFPA 110 — generator + ATS + UPS, life-safety / critical / equipment branches. |
| **HTM 07-01** | Healthcare waste. The HC1–HC6 classification. |
| **HTM 08-01** | Acoustics. NR ratings, RT60, STC for speech privacy. |
| **HTM 01-06** | Endoscope reprocessing. RFID traceability chain. |
| **HBN catalogue** | Per-facility design notes (HBN 04-01 inpatient, HBN 09-02 ICU, HBN 09-03 NICU, HBN 11-01 community, HBN 13 HSDU, HBN 16 mortuary, HBN 21 maternity, HBN 03-01 mental health). |
| **FGI Guidelines** | American counterpart. Editions 2014 / 2018 / 2022 / Facility Code 2026 (legally enforceable). |
| **NFPA 99** | Healthcare Facilities Code. Ch. 5 medical gas, Ch. 6 essential electrical, Ch. 14 hyperbaric. |
| **NFPA 110** | Standby power systems — generator test cadence + Type 10 runtime requirements. |
| **NEC 517** | National Electrical Code Article 517 — healthcare facilities (US). Cardiac-protected outlets, IPS, equipotential grounding. |
| **NCRP 147** | Structural shielding for medical X-ray. The W·U·T calculation. |
| **NCRP 151** | Megavoltage radiotherapy shielding. Adds primary + secondary + neutron components. |
| **ASHRAE 170** | American ventilation table (Table 7.1 — minimum ACH + pressure relation per space). Embedded in FGI 2026. |
| **ISO 14644** | Cleanroom particle classification. ISO 5 = pharmacy PEC, ISO 7 = buffer / anteroom. |
| **USP <797>** | Sterile compounding. Positive-pressure cascade buffer → anteroom → corridor. |
| **USP <800>** | Hazardous drugs (cytotoxic). Negative-pressure C-PEC inside C-SEC, externally exhausted. |
| **BS 5682** | UK standard for medical-gas terminal units — gas-specific NIST indexing. |
| **BS EN 1822** | HEPA filter classification — H13 / H14 / U15 / U16. |
| **BS 9263** | Anti-ligature product rating LR1–LR4. |
| **iHFG** | International Health Facility Guidelines (TAHPI Australia). Used on overseas projects. |
| **SFG20** | UK building maintenance schedule library. Subscription-based; STING ships the schedule IDs only. |
| **IRR17** | UK Ionising Radiations Regulations 2017. Defines "controlled" and "supervised" areas. |

---

## 9. Common workflows

### 9.1  "I'm starting a new hospital project"

1. **Day 1 setup** (§4): facility type + profile + code base.
2. **Master Setup** loads every parameter.
3. Tag clinical rooms by class.
4. **Run All Healthcare** to see your starting position.
5. Let designers fill in the per-domain parameters as they design.
6. Re-run validators weekly. Trend curves stored automatically.
7. Pre-handover, run **HealthcareCommissioning** workflow. It does
   everything for you in one click.

### 9.2  "I need to issue Room Data Sheets for sign-off"

1. Make sure every clinical room has `CLN_ROOM_CLASS_TXT` and the 13
   other mandatory parameters.
2. Run **RDS Completeness** — fix the gaps it lists.
3. Run **Batch RDS**. Output lands in
   `<project>/_BIM_COORD/healthcare/rds/<roomNum>_RDS.docx`.
4. Email each clinical user the docs for their rooms.

### 9.3  "Commissioning team is arriving Monday — what do I prep?"

The day before:

1. **MGPS Audit** to confirm every alarm + ZVB + TU is modelled.
2. **EES Branches** to confirm every receptacle is on the right circuit.
3. **Adjacency / Flow** to confirm physical zoning is right.

Day-of:

1. Verifier opens the Planscape app, taps **MGPS Verification**, walks
   through the 12 steps. Result auto-syncs to the server.
2. Estate manager taps **Pressure (live)** — sees real-time Δp on a
   tablet.
3. Water-safety lead taps **Water Flushing** — logs every sentinel.

### 9.4  "Mental-health unit anti-ligature inspection"

1. Mark every room of class `PSY-BED`, `PSY-OBS`, `SECL` with
   `CLN_LIGATURE_RES_BOOL = Yes`.
2. Run **Anti-Ligature** validator. Fix every fitting it flags.
3. On-site: open **Anti-Ligature audit** in Planscape app, walk every
   bedroom, log PASS/FAIL per fitting with a photo.
4. Server emails the unit manager a summary at end of day.

### 9.5  "I need a COBie deliverable for the FM team"

1. Go to BCC → DELIVERABLES → COBie Export.
2. Choose preset `HEALTHCARE_NHS` (or `HEALTHCARE_PRIVATE`).
3. Click. Output: `<project>/STING_Exports/COBie/<project>.xlsx` with
   19 worksheets including the healthcare overlay (50 + clinical
   equipment types, 35 + SFG20 maintenance schedules, 26 + spare-part
   templates).

---

## 10. Troubleshooting & FAQ

### "I clicked Run All Healthcare and got 0 findings"

Either:

1. `PRJ_ORG_HEALTH_FACILITY_TYPE_TXT` is empty — set it to
   `Acute / Mental / Community / Rehab / Day / FM`. The validator chain
   refuses to run on non-healthcare projects (zero-cost gate).
2. No room has `CLN_ROOM_CLASS_TXT` populated — every validator reads
   that field first.
3. The pack profile is set to `IMAGING-ONLY` or similar and the
   validators relevant to your project are filtered out.

### "I see HEALTHCARE in the dock panel but not in the BCC"

The BCC's HEALTHCARE tab is gated on `PRJ_ORG_HEALTH_FACILITY_TYPE_TXT`.
Set that param. Reopen the BCC.

### "MGPS verify said FAIL on step 6 — what now?"

Step 6 is gas-specific NIST/DISS indexing. The terminal unit accepts
the wrong gas. Fix the manifold before commissioning continues. Log
the failure in the verification record so an audit trail exists.

### "RDS render failed — 'resource missing'"

The `healthcare_rds.docx` template ships as a README placeholder.
Author the .docx in Word, drop it next to the other template
sources, rebuild. The token contract is in
`docs/HEALTHCARE_PACK_DESIGN.md` §11 H-8.

### "BACnet readback is empty — why no live pressure data?"

The `BacnetReadback` and `OpcUaReadback` classes ship as stubs.
Wire a real BACnet stack (yabe / CAS-BACnet / similar) behind the same
interface. The validators tolerate a stub — they just report devices
as "stale" until the transport fills in.

### "I'm working on a community clinic — most of these audits don't apply"

Set `PRJ_ORG_HEALTH_PACK_PROFILE_TXT = COMMUNITY`. The gate filters out
LINAC, hyperbaric, BSL4, advanced-imaging, structural-loads, and
endoscope checks. You'll see a smaller list of Healthcare buttons run.

### "The mobile app shows '—' for project name"

(Fixed in commit `0f7dbfed`.) Pull the latest mobile build.

### "Mortuary capacity validator says I need 4 fridge bays but my project only has 2"

`PRJ_ORG_HEALTH_BEDS_INT` × 0.005 with a minimum of 4 (HBN 16 baseline).
Either author more bays in the model or override the minimum in your
project standards if your AHJ accepts it.

### "I want to add a new clinical room class"

Edit `Data/HEALTHCARE_PACK_PROFILES.json` (project-side override goes
to `<project>/_BIM_COORD/healthcare/`). The pack profile JSON is
hot-reloaded on the next validator run thanks to the mtime cache
introduced in commit `2438438c`.

---

## 11. Sign-offs and certifications

The Healthcare Pack **never automatically certifies anything**. It
produces drafts for named experts to sign off. The mandatory roles:

| Domain | Sign-off param | Who signs |
|---|---|---|
| Medical gas | `MGS_VERIFY_BY_TXT` + `MGS_VERIFY_PASS_BOOL` | ASSE 6030 verifier |
| Radiation | `RAD_QE_NAME_TXT` + `RAD_QE_DT` | Qualified Expert (medical / health physicist) |
| Structural (imaging) | `CLN_STRUCT_SIGN_OFF_TXT` + `CLN_STRUCT_SIGN_OFF_DT` | Structural Engineer |
| RDS | `signoff.clinician` (in template) | Clinical user / nurse-in-charge |
| Anti-ligature | per-audit row | Mental health lead |
| MGPS verification | per-step + `MGS_VERIFY_CERT_REF_TXT` | NFPA 99 §5.1.12 verifier |
| Project-wide | `PRJ_ORG_HEALTH_AE_VENT_TXT` / `_AE_MGAS_TXT` / `_AE_WATER_TXT` / `_AE_ELEC_TXT` | Authorising Engineers (one per domain) |

The validators report the sign-off as missing until the field is
populated. Don't dismiss those warnings — they're how the AHJ checks
that humans were in the loop.

---

## 12. Where to go from here

| Want to… | Read |
|---|---|
| See the design rationale | `docs/HEALTHCARE_PACK_DESIGN.md` |
| See the IoT architecture | `docs/HEALTHCARE_PACK_DESIGN.md` §19 |
| See open follow-ups | `docs/ROADMAP.md` items HC-01..HC-25 |
| Author the RDS template | `StingTools/Docs/_template_sources/healthcare_rds_README.md` |
| Wire a real BACnet transport | `StingTools/Core/Twin/TwinReadback.cs` |
| Add a new validator | `StingTools/Core/Validation/Healthcare/HealthcareValidatorBase.cs` |
| Get help | Open an issue with label `healthcare` on the repo |

---

## Appendix A — every healthcare command tag

For workflow authors. Every tag here is wired through both
`WorkflowEngine.ResolveCommand` and `StingCommandHandler.Execute`.

| Tag | Command class |
|---|---|
| `Healthcare_RunAllValidators` | runs every validator through the profile gate |
| `Healthcare_PressureAudit` | PressureRegimeValidator |
| `Healthcare_MgasAudit` | MgasNetworkAuditCommand |
| `Healthcare_MgasVerify` | MgasVerifyCommand (12-step walkthrough) |
| `Healthcare_EesBranch` | EesBranchValidator |
| `Healthcare_WaterSafety` | WaterSafetyValidator |
| `Healthcare_RadShield` | RadShieldValidator |
| `Healthcare_AdvancedRadShield` | AdvancedRadShieldValidator |
| `Healthcare_RdsCompleteness` | RdsCompletenessValidator |
| `Healthcare_IoTStaleness` | IoTStalenessValidator |
| `Healthcare_StructuralLoad` | StructuralLoadValidator |
| `Healthcare_Acoustic` | AcousticValidator |
| `Healthcare_EndoscopeTrace` | EndoscopeTraceValidator |
| `Healthcare_EesResilience` | EesResilienceValidator |
| `Healthcare_RtlsCoverage` | RtlsCoverageValidator |
| `Healthcare_WasteFlow` | WasteFlowValidator |
| `Healthcare_IssueRDS` | IssueRoomDataSheetCommand |
| `Healthcare_BatchRDS` | BatchIssueRoomDataSheetsCommand |
| `Healthcare_AdjacencyAudit` | AdjacencyAuditCommand |
| `Healthcare_RadCalcChest` | NCRP 147 chest-room worked example |
| `Healthcare_RadCalcCt` | NCRP 147 CT worked example |
| `Healthcare_RadCalcLinac` | NCRP 151 LINAC vault first-pass |
| `Healthcare_MriZoneAudit` | MriZoneAuditCommand |
| `Healthcare_IoTRegistry` | IoTRegistryCommand |
| `Healthcare_AntiLigature` | AntiLigatureAuditCommand |
| `Healthcare_HybridOr` | HybridOrCheckCommand |
| `Healthcare_PharmacyUsp` | PharmacyUspAuditCommand |
| `Healthcare_BehaviouralHealth` | BehaviouralHealthAuditCommand |
| `Healthcare_Mortuary` | MortuaryAuditCommand |
| `Healthcare_MaternityNicu` | MaternityNicuAuditCommand |
| `Healthcare_Hsdu` | HsduAuditCommand |
| `Healthcare_Dialysis` | DialysisAuditCommand |
| `Healthcare_Hbo` | HboAuditCommand |

---

## Appendix B — the parameter prefixes

| Prefix | Domain | Examples |
|---|---|---|
| `CLN_` | Clinical room metadata | `CLN_ROOM_CLASS_TXT`, `CLN_PRESS_REGIME_TXT`, `CLN_INFECT_CLASS_TXT`, `CLN_MRI_ZONE_INT` |
| `MGS_` | Medical gas | `MGS_GAS_TYPE_TXT`, `MGS_NOM_PRESS_KPA_NR`, `MGS_ZVB_REF_TXT`, `MGS_VERIFY_BY_TXT` |
| `RAD_` | Radiation | `RAD_LEAD_MM_NR`, `RAD_BARRIER_TYPE_TXT`, `RAD_QE_NAME_TXT` |
| `CEQ_` | Clinical equipment | `CEQ_GMDN_CODE_TXT`, `CEQ_INFECT_TIER_TXT` (Spaulding) |
| `LIG_` | Anti-ligature | `LIG_PRODUCT_RATING_TXT`, `LIG_FORCE_LIMIT_KG_NR` |
| `ELC_EES_` / `ELC_IPS_` / `ELC_IT_CARDIAC_` | EES extensions | `ELC_EES_BRANCH_TXT`, `ELC_ATS_TIME_S_NR`, `ELC_IPS_BOOL` |
| `HVC_HEPA_` / `HVC_ACH_OUTSIDE_` | HVAC extensions | `HVC_HEPA_GRADE_TXT`, `HVC_ACH_OUTSIDE_NR` |
| `PLM_TMV_` / `PLM_DEAD_LEG_` / `PLM_AUG_CARE_` / `PLM_RO_LOOP_` | Water safety | `PLM_TMV_TYPE_TXT`, `PLM_DEAD_LEG_M_NR`, `PLM_AUG_CARE_BOOL` |
| `FLS_COMPARTMENT_` | Fire compartment ID | `FLS_COMPARTMENT_ID_TXT` |
| `MNT_CAL_` | Calibration dates | `MNT_CAL_LAST_DT`, `MNT_CAL_NEXT_DT` |
| `PRJ_ORG_HEALTH_` | Project info | `PRJ_ORG_HEALTH_FACILITY_TYPE_TXT`, `PRJ_ORG_HEALTH_PACK_PROFILE_TXT`, `PRJ_ORG_HEALTH_AE_VENT_TXT` |

A mnemonic: **CLN** = clinical, **MGS** = medical gas, **RAD** =
radiation, **CEQ** = clinical equipment, **LIG** = ligature.
Everything else extends an existing prefix (`ELC_` electrical,
`HVC_` HVAC, `PLM_` plumbing, `FLS_` fire, `MNT_` maintenance).

---

> **Last refreshed:** with PR #202 head `58de3252`. The Healthcare Pack
> covers 30 phases (H-1..H-30) implemented across ~30 commits on branch
> `claude/research-hospital-design-0Uxbi`. See `docs/CHANGELOG.md` for
> the per-phase summary and `docs/ROADMAP.md` items HC-01..HC-25 for
> open follow-ups.
