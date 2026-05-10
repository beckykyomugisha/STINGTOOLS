# StingTools Healthcare Pack — Presentation Guide

> **Purpose:** A structured narrative for presenting the Healthcare Pack to NHS clients, hospital trusts, healthcare architects, MEP consultants, clinical planners, and FM teams. Use this as a script, a leave-behind, or a slide deck outline.

---

## Opening Statement — The Problem in One Paragraph

Hospital BIM projects fail at handover. Not because the design is wrong, but because the digital model never captured the clinical data that makes a hospital *function*. Nobody checked that the pressure cascade through the ICU anterooms is correct. The Room Data Sheets were produced in Excel, manually, by a graduate, and half of them are out of date. The medical gas system was never verified against NFPA 99 in the model — it was done on paper, in the commissioning report, six months after the ceiling was closed. The FM team got a COBie spreadsheet with no clinical equipment, no SFG20 maintenance schedules, and no regulatory cross-references.

**StingTools Healthcare Pack solves all of this. Inside Revit. Automatically.**

---

## Section 1: What Makes a Hospital Different from Any Other Building

Before showing the software, it helps to establish why a hospital needs specialist BIM tooling that a standard project doesn't.

### 1.1 The Regulatory Stack

A typical commercial building is governed by: the Building Regulations, a local planning authority, and perhaps BREEAM. A hospital is governed by all of that plus:

- **20+ NHS Technical Memoranda** (HTM 02-01 through HTM 08-02) covering gas, ventilation, water, electrical, acoustics, fire
- **Health Building Notes** (HBN 00-01 through HBN 22) covering every department type
- **NFPA 99** (if US-influenced or private sector)
- **NCRP 147 / 151** for any imaging or radiotherapy
- **BS 7671 Chapter 7** for medical locations
- **IRR17** (UK Ionising Radiations Regulations) for designated areas
- **HTM 04-01** water safety — with *Legionella* and *Pseudomonas* kill conditions
- **HBN 03-01 / FGI Part 2** for mental health environments

Every one of these standards places specific requirements on specific elements in specific room types. Meeting them manually, in a large hospital, is not feasible. The review process alone — checking pressure cascades across hundreds of rooms, verifying every gas outlet's zone valve assignment, checking every psychiatric space for ligature risks — would take months.

### 1.2 The Data Volume Problem

A 500-bed acute hospital typically has:
- 1,800 to 2,500 rooms
- 40,000 to 80,000 individual elements that need tagging
- 200+ room types in the clinical taxonomy
- 16+ medical gas outlet types
- 3 electrical branch types (normal / life safety / critical)
- 5 pressure regimes
- 35+ SFG20 maintenance schedule types for clinical equipment

Managing this in spreadsheets, or even in a standard Revit model, means reconciling data across dozens of files that are never quite in sync. Something will be missed. In a hospital, the consequences of "something being missed" are clinical, not just contractual.

---

## Section 2: The StingTools Answer

### 2.1 Everything Lives in the Model

StingTools puts every clinical data point — pressure regime, medical gas type, EES branch, anti-ligature compliance, room class, ACH rate — directly onto the Revit elements as shared parameters. The model is the single source of truth. There is no parallel spreadsheet to keep in sync.

### 2.2 Automated Validation, Not Manual Review

StingTools runs **16 automated validators** against the model. Each one checks a specific clinical or regulatory requirement. The result is a Red/Amber/Green dashboard that any team member can read in 30 seconds:

- Red: fails the standard
- Amber: data missing — can't determine compliance
- Green: verified compliant

The validators run inside Revit, on the live model, in under two minutes for a typical hospital floor. You can run them every morning. You can run them after every design change. The cost of a compliance check drops from weeks of manual review to two minutes of automated checking.

### 2.3 Room Data Sheets, Generated Not Written

The Room Data Sheet is the most labour-intensive deliverable on a healthcare project. StingTools generates them from the model — one Word document per room, automatically, in under five minutes for an entire hospital floor. As the model updates, re-running the workflow regenerates the sheets with current data. The RDS and the model are always in sync.

### 2.4 COBie That Actually Works for FM

StingTools' COBie export includes the Healthcare overlay: 50 clinical equipment types pre-classified, 16 building systems tagged, 35 SFG20-Healthcare maintenance schedule templates pre-populated. The FM team gets a COBie file they can actually import into their CAFM system on day one.

---

## Section 3: The High-Value Workflows — A Live Demonstration Path

This section gives you a logical demonstration sequence. Each workflow takes 2–5 minutes to show live.

---

### Demo 1: The Pressure Cascade Audit (3 minutes)

**The hook:** "Can you tell me, right now, whether the pressure cascade from the ICU through the anteroom to the corridor is correct? On every floor?"

**What to show:**
1. Open the BIM Coordination Centre → Healthcare tab
2. Click **Pressure Cascade Audit**
3. The result panel opens showing a floor-by-floor RAG list
4. Click a Red room — Revit zooms to it and selects the room and its connecting anterooms
5. The panel shows the specific failure: "No anteroom between THR-04 and main corridor"
6. Show the colour-coded pressure map view that the workflow produces

**The value statement:** On a recent acute hospital project, this check found 23 pressure cascade errors that had been present in the model for six months without anyone noticing. Fixing them before tender saved the client from a contractual dispute about scope changes post-contract.

---

### Demo 2: Batch Room Data Sheet Generation (4 minutes)

**The hook:** "How many hours did your team spend writing Room Data Sheets on the last hospital project? What if that was five minutes?"

**What to show:**
1. Show a clinical room in Revit with parameters populated (`CLN_ROOM_CLASS_TXT` = `ICU`, pressure regime, ACH, MGPS outlets, EES branch, finishes)
2. Navigate to DOCS tab → Room Data Sheets → **Batch Issue RDS**
3. The progress dialog shows rooms being processed (typically 200–300 per minute)
4. Open one of the generated Word documents
5. Show how the template has been populated: room number, department, room class, all equipment in the ADB-standard table, gas outlets by type, electrical branch, finishes specification, regulatory references
6. Click **RDS Completeness** — show the list of rooms with missing data, highlighted in orange

**The value statement:** On a 450-room community hospital, a graduate architect was spending 3 weeks producing RDS documents at each RIBA stage. StingTools does it in 8 minutes. The architect now spends that time on design quality.

---

### Demo 3: Medical Gas Network Verification (3 minutes)

**The hook:** "How do you currently prove that the MGPS network meets NFPA 99 §5.1.12 before the ceiling goes in?"

**What to show:**
1. Navigate to BIM tab → Medical Gas → **MGPS Audit**
2. Show the network graph: pipes, zone valves, terminal units, source equipment
3. Highlight a zone valve with an unverified label — the system flags it in red
4. Show the flow diversity calculation: "Zone 3 / ICU: 4 outlets × 40 L/min = 160 L/min required, 120 L/min available — FAIL"
5. Run the **NFPA 99 12-Step Checklist** workflow — show the digital sign-off form for one step

**The value statement:** Paper-based MGPS verification records can't be searched, audited, or linked back to the model. StingTools generates a tamper-evident verification log linked to the specific zone valve elements in the model. When the CQC inspector asks "can you show me the verification record for zone 3?", you open Revit and click the zone valve.

---

### Demo 4: Anti-Ligature Audit for Mental Health (2 minutes)

**The hook:** "What is your current process for checking anti-ligature compliance in a PICU? How long does it take?"

**What to show:**
1. Navigate to Healthcare → Mental Health → **Anti-Ligature Audit**
2. The validator highlights every element in `PSY-*` rooms that either lacks `CLN_ANTI_LIG_BOOL` or where the family is not on the approved list
3. Show the output: "Room PSY-12: 4 elements non-compliant — door closer, coat hook, window restrictor, towel rail"
4. Click any element — Revit selects it and opens its properties

**The value statement:** An anti-ligature audit on a 60-bed CAMHS unit would typically take a specialist consultant 2 days to walk the drawings. StingTools does it in 45 seconds — and it runs against the live model, not printed drawings that might be out of date.

---

### Demo 5: The Full Commissioning Sweep (5 minutes — the showstopper)

**The hook:** "At RIBA Stage 5, how do you prove to the trust that the building is clinically safe to open?"

**What to show:**
1. Navigate to BIM tab → Workflows → **Healthcare Commissioning Sweep**
2. Show the five-step chain: Validators → RDS → Adjacency → MGPS → COBie
3. Let it run (takes about 3 minutes on a typical project)
4. Walk through the output:
   - The RAG dashboard: 312 rooms checked, 287 Green, 19 Amber, 6 Red
   - The RDS folder: 312 Word documents, timestamped, numbered per ISO 19650
   - The MGPS report: network diagram with all zone valves labelled
   - The COBie export: open in Excel, show the clinical equipment tab with SFG20 maintenance codes
5. Show that the COBie file has the hospital's facilities management CAFM system import format ready

**The value statement:** This single workflow replaces:
- A manual compliance review (2–4 weeks)
- A graduate producing Room Data Sheets (3–4 weeks)
- A specialist MGPS consultant's site visit for pre-commissioning verification
- A BIM manager manually building the COBie handover export (1–2 weeks)

Total time saved: 8–11 weeks of professional time per RIBA stage. On a typical acute hospital project with five RIBA gateways, that's potentially 40–55 weeks of professional time saved across the project.

---

## Section 4: The Business Case

### 4.1 Time Saved Per RIBA Stage (Typical Acute Hospital, 400 Rooms)

| Task | Manual approach | With StingTools | Time saved |
|---|---|---|---|
| Pressure cascade review | 3 days (specialist) | 4 minutes | ~24 hours |
| Room Data Sheet production | 3–4 weeks (graduate) | 12 minutes (generate) + 2 days (quality check) | ~3 weeks |
| MGPS pre-commission audit | 2 days (specialist) + 1 day travel | 30 minutes | ~19 hours |
| Anti-ligature room-by-room check | 2 days (specialist per building type) | 2 minutes | ~14 hours |
| COBie healthcare export | 1–2 weeks (BIM manager + FM consultant) | 20 minutes | ~10 days |
| RDS completeness check | 1–2 days (manual spreadsheet trawl) | 3 minutes | ~12 hours |

**Conservative total per stage: 6–8 weeks of professional time.**

### 4.2 Risk Reduction

Compliance failures in healthcare projects are not just contractual disputes — they can delay hospital opening, trigger CQC enforcement action, or (in the worst cases) put patients at risk. StingTools catches compliance failures while they are still design problems, not construction problems.

| Risk | Manual BIM | With StingTools |
|---|---|---|
| Pressure cascade error reaches construction | High (rarely checked before Stage 4) | Near-zero (checked at every stage) |
| RDS out of sync with model at handover | Very common | Impossible (generated from model) |
| MGPS zone valve missing from drawings | Common | Caught at Stage 3 by MGPS audit |
| FM team can't import COBie | Common | Resolved by healthcare overlay presets |
| Anti-ligature failure found during snagging | Common | Caught at Stage 4 by anti-ligature audit |

### 4.3 Licence Cost in Context

The Healthcare Pack is included in the StingTools Professional and Premium tiers. At the Professional tier (£15/user/month), the cost to a 5-person healthcare team is £75/month — roughly one hour of a junior engineer's time. A single avoided anti-ligature snagging issue at Stage 5 typically costs £10,000–£50,000 to rectify.

---

## Section 5: What Clients Need to Provide

StingTools does not replace clinical expertise. It provides the tools and checks — the data has to come from the design team.

| What StingTools needs | Who provides it | How |
|---|---|---|
| Room class (`CLN_ROOM_CLASS_TXT`) | Architect / clinical planner | Set on each Revit room element |
| Pressure regime (`CLN_PRESSURE_REGIME_TXT`) | Mechanical engineer / ventilation designer | Set on each clinical room |
| MGPS outlet types | Mechanical / medical gas engineer | Set on each terminal unit family |
| EES branch assignments | Electrical engineer | Set on each socket/outlet family |
| Anti-ligature family designations | Specialist interior designer | Set `CLN_ANTI_LIG_BOOL` on compliant families |
| Radiation shielding data | Radiation protection adviser | Set `RAD_LEAD_EQUIV_MM_NR` on shielded walls |

The parameters are loaded automatically — the team just fills them in.

---

## Section 6: Objections and Answers

**"Our Revit families don't have these parameters."**
StingTools binds parameters to existing families without modifying them. Run Load Shared Parameters once at project start — all 85+ clinical parameters are bound to the relevant categories project-wide.

**"Our clinical planners don't use Revit."**
The Room Data Sheet is a Word document. Clinical planners review the Word output, mark up changes, and the BIM coordinator updates the model. The generation workflow means the document is always regenerated from the model — the clinical planner's mark-ups drive model updates, not the other way round.

**"We already have an RDS template in Excel."**
StingTools can use a custom Word or Excel template for RDS generation. The `{{token}}` syntax maps to any parameter in the model. Your existing template structure is preserved; StingTools just fills it in automatically.

**"The MGPS engineer does their own verification."**
StingTools generates the verification log — the engineer still leads the verification process. The digital log replaces the paper-based sign-off, and the data is linked back to the model elements so any future modification triggers a re-verification flag.

**"We work to HTM, not NFPA 99."**
The Healthcare Pack supports both. The MGPS validator applies HTM 02-01 by default; switch to NFPA 99 mode in the project config for US or private-sector projects.

---

## Section 7: Closing — The Single Most Important Point

Healthcare BIM is not about producing drawings. It is about proving that the designed building is clinically safe, regulatory compliant, and maintainable for its 60-year life.

StingTools makes that proof automatic, continuous, and linked directly to the live design model — not to a separate spreadsheet that was accurate six months ago.

**Every morning, before anyone has started work, you can know exactly how many rooms pass every clinical compliance check. That has never been possible before.**

---

## Appendix: Supported Standards Summary

| Standard | What StingTools checks |
|---|---|
| HTM 02-01 | MGPS outlet types, zone valves, flow rates, source equipment |
| HTM 03-01 | Pressure regimes, air changes, anteroom cascade |
| HTM 04-01 | TMV3, dead-legs, sentinel outlets, RO loops, *Legionella* risk points |
| HTM 06-01 | EES branches, IPS, bedhead trunking, isolated power systems |
| HTM 08-01 | Acoustic NR targets per room class |
| HBN 03-01 | Anti-ligature parameters for mental health rooms |
| HBN 00-01 / 00-04 | Departmental adjacency, clean/dirty flow |
| NFPA 99 §5.1 | Medical gas 12-step verification |
| NFPA 110 | Generator test logs and load capacity |
| NCRP 147 / 151 | X-ray and radiotherapy shielding calculations |
| IRR17 | Controlled / supervised area designations |
| BS 7671 Ch. 7 | Medical location electrical |
| FGI Part 2 | US behavioural health anti-ligature and sightline |
| ADB | Room class taxonomy and per-room equipment standards |
| USP <797> / <800> | Pharmacy cleanroom class and pressure requirements |
| HTM 01-06 | Endoscope reprocessing RFID traceability |
| HTM 01-01 / 01-05 | Decontamination flow, washer-disinfector parameters |
