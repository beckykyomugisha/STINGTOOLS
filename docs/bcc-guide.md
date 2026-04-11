# BIM Coordination Center (BCC) — Beginner's Guide

> **Audience:** Revit users new to BIM coordination.
> **Plugin:** StingTools for Autodesk Revit 2025/2026/2027.

---

## 1. What is BIM Coordination?

BIM coordination is the process of keeping a building model accurate, complete, and ready for handover. In traditional construction, coordinators check paper drawings for clashes and missing information. In BIM (Building Information Modelling), the coordinator works inside a 3D Revit model.

Your job as a BIM coordinator is to make sure every element in the model — every duct, wall, column, and light fitting — has correct data attached to it. That data follows the **ISO 19650** standard, which is the international rulebook for how building information should be organised, named, and shared.

StingTools automates most of this work. The **BIM Coordination Center (BCC)** is a single dialog window with 13 tabs that gives you a real-time dashboard of your model's health. Instead of running dozens of separate commands, you open the BCC, see what needs attention, and fix problems with one click.

The BCC covers the full BIM coordinator lifecycle:

- **Monitor** — tag compliance, warnings, stale elements
- **Fix** — auto-resolve common issues, retag moved elements
- **Report** — export to CSV, Excel, HTML, COBie
- **Collaborate** — raise issues, track meetings, manage transmittals
- **Deliver** — COBie handover, BEP generation, revision control

---

## 2. Key Terms Glossary

| Term | What it means |
|------|---------------|
| **BIM** | Building Information Modelling — a 3D model enriched with data about every element (cost, material, manufacturer, etc.) |
| **ISO 19650** | The international standard for managing information over the lifecycle of a built asset using BIM |
| **CDE** | Common Data Environment — a shared digital space where project files move through stages: WIP → SHARED → PUBLISHED → ARCHIVE |
| **COBie** | Construction Operations Building Information Exchange — a spreadsheet format (Excel) for handing building data to facilities managers |
| **BCF** | BIM Collaboration Format — an XML file format for sharing issues/clashes between BIM tools (Navisworks, Solibri, BIMcollab) |
| **RFI** | Request for Information — a formal question to the design team when something is unclear |
| **NCR** | Non-Conformance Report — a record that something does not meet the required standard |
| **SI** | Site Instruction — a directive issued on-site to change or clarify work |
| **RAG** | Red / Amber / Green — a traffic-light system showing status at a glance (Red = bad, Amber = needs attention, Green = good) |
| **Transmittal** | A formal record of documents sent between parties, with a unique reference number |
| **Tag** | An 8-segment code assigned to every element, e.g. `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003` |
| **Stale element** | An element whose tag is outdated because it has been moved, resized, or changed since it was last tagged |

---

## 2a. Complete Abbreviations and Acronyms

This reference covers every abbreviation you will encounter in the BCC and StingTools documentation.

### Standards and Frameworks

| Abbreviation | Full Term | Context |
|-------------|-----------|---------|
| **ISO 19650** | International Standard for information management using BIM | The rulebook that governs how building data is created, named, shared, and archived across a project's lifecycle |
| **BIM** | Building Information Modelling | A process that uses 3D models enriched with data — not just geometry but cost, material, maintenance, and performance attributes |
| **BEP** | BIM Execution Plan | The project's BIM playbook: who does what, when, to what level of detail, and how information is exchanged — StingTools generates 22 sector-specific BEP presets |
| **EIR** | Exchange Information Requirements | The client's wish-list: what data they need, at what RIBA stage, in what format — the BEP responds to the EIR |
| **OIR** | Organisational Information Requirements | The client organisation's high-level data needs (e.g. "we need asset data for CAFM integration") |
| **PIR** | Project Information Requirements | OIR translated into project-specific deliverables (e.g. "COBie for every MEP asset by DD3") |
| **AIR** | Asset Information Requirements | Data needed for ongoing operations and maintenance after handover |
| **MIDP** | Master Information Delivery Plan | The overall timetable for who produces what information, by when, at what suitability |
| **TIDP** | Task Information Delivery Plan | Each discipline's detailed plan that feeds into the MIDP |
| **PAS 1192** | Publicly Available Specification 1192 | The predecessor to ISO 19650, still referenced in legacy UK contracts |
| **BS 1192** | British Standard 1192 | Naming conventions and CDE folder structures — now superseded by ISO 19650 but still widely used for file naming |
| **LOD** | Level of Detail | How geometrically detailed a model element needs to be (LOD 100 = conceptual box, LOD 400 = fabrication-ready) |
| **LOI** | Level of Information | How much non-geometric data (parameters, specifications) an element carries |
| **LOA** | Level of Accuracy | How precisely geometry matches real-world as-built dimensions |
| **RIBA** | Royal Institute of British Architects | Their Plan of Work defines 8 project stages (0–7) that govern when information is due |
| **CDM 2015** | Construction Design and Management Regulations | UK H&S legislation requiring hazard data in BIM models |
| **CIBSE** | Chartered Institution of Building Services Engineers | Publishes guides for MEP system design — StingTools uses CIBSE velocity limits and TM40 function codes |
| **COBie** | Construction Operations Building Information Exchange | A 17-worksheet spreadsheet format for handing asset data to facilities managers — StingTools exports with 22 project-type presets |
| **IFC** | Industry Foundation Classes | A vendor-neutral file format (ISO 16739) for exchanging BIM data between Revit, ArchiCAD, Tekla, etc. |
| **BCF** | BIM Collaboration Format | An XML format for sharing clash/issue data between tools — StingTools exports BCF 2.1 with camera viewpoints |
| **SFG20** | Standard for Good Practice in Maintenance | Defines maintenance task schedules — referenced in COBie Job worksheet |
| **BS 8210** | Guide to Facilities Maintenance Management | UK standard for planned preventive maintenance — referenced in COBie spare parts data |
| **Uniclass** | Unified Classification for the Construction Industry | A UK classification system with codes for systems (Ss), products (Pr), and spaces — used in STING SYS and FUNC codes |

### CDE and Document Management

| Abbreviation | Full Term | Context |
|-------------|-----------|---------|
| **CDE** | Common Data Environment | The shared digital filing system — all project information lives here, moving through states (WIP → SHARED → PUBLISHED → ARCHIVE) |
| **WIP** | Work In Progress | CDE state: file is being developed by the originator — not visible to other parties |
| **S0** | Suitability Code: Work In Progress | Document is incomplete and not ready for coordination |
| **S1** | Suitability Code: Fit for Coordination | Ready for checking against other disciplines |
| **S2** | Suitability Code: Fit for Information | For reference only — not for design decisions |
| **S3** | Suitability Code: Fit for Review and Comment | Issued for formal review with tracked comments |
| **S4** | Suitability Code: Fit for Stage Approval | Design freeze — submitted for client or stage sign-off |
| **S5** | Suitability Code: Fit for Manufacturing/Procurement | Approved for procurement, fabrication, or ordering |
| **S6** | Suitability Code: Fit for PIM Authorization | Authorised for the Project Information Model |
| **S7** | Suitability Code: Fit for AIM Authorization | Authorised for the Asset Information Model (handover to FM) |
| **CR** | Code: Correspondence | Document type code for letters, emails, and formal communications |
| **AB** | Code: As-Built | Indicates a document reflects the constructed (not designed) state |
| **DD1** | Data Drop 1 — Concept (RIBA Stage 2) | First milestone: ≥30% tag compliance required |
| **DD2** | Data Drop 2 — Design (RIBA Stage 3) | Second milestone: ≥60% tag compliance, COBie Type and System sheets required |
| **DD3** | Data Drop 3 — Construction (RIBA Stage 4–5) | Third milestone: ≥85% tag compliance, full COBie export, O&M data |
| **DD4** | Data Drop 4 — Handover (RIBA Stage 6) | Final milestone: ≥95% tag compliance, complete COBie, digital twin ready |
| **TX** | Transmittal | A formal document exchange record with unique ID (TX-0001), tracking who sent what to whom |
| **DR** | Drawing (2D) | Document type code for traditional 2D drawings and plans |
| **M3** | 3D Model | Document type code for a three-dimensional Revit/IFC model |
| **CM** | Combined Model (Federated) | Document type code for a federated model merging multiple discipline models |
| **BQ** | Bill of Quantities | Document type code for priced quantities of materials and labour |
| **CA** | Calculations | Document type code for engineering calculations and analysis reports |
| **IE** | Information Exchange (COBie) | Document type code specifically for COBie data exchange files |
| **MI** | Minutes / Action Notes | Document type code for meeting records and follow-up actions |
| **HS** | Health and Safety | Document type code for CDM documentation, risk assessments, and H&S plans |

### Issue Types

| Abbreviation | Full Term | Context |
|-------------|-----------|---------|
| **NCR** | Non-Conformance Report | Something does not meet the BEP, EIR, or agreed standard — **default priority: HIGH** |
| **RFI** | Request for Information | A formal question requiring a documented answer — **default priority: MEDIUM** |
| **RFA** | Request for Approval | Formal submission requiring sign-off from client or lead designer |
| **TQ** | Technical Query | Technical question needing a specialist response |
| **SI** | Site Instruction | On-site directive to change or clarify construction work |
| **EWN** | Early Warning Notice | NEC-contractual alert: a risk that may affect time, cost, or quality |
| **CE** | Compensation Event | NEC contract: a change event with cost or programme implications |
| **VO** | Variation Order | JCT/FIDIC contract: formal instruction to change the scope of works |
| **AI** | Architect's Instruction | Formal instruction issued by the lead designer |
| **PMI** | Project Manager's Instruction | Instruction from the project manager directing action |
| **CVI** | Confirmation of Verbal Instruction | Written confirmation of a previously spoken instruction |
| **SLA** | Service Level Agreement | Time thresholds for issue resolution: CRITICAL = 4 hours, HIGH = 24 hours, MEDIUM = 7 days, LOW = 14 days |

### Discipline Codes

| Abbreviation | Full Term | Context |
|-------------|-----------|---------|
| **M** | Mechanical | HVAC, heating, cooling, ventilation — covers ductwork, air handling, heat exchangers |
| **E** | Electrical | Power distribution, lighting, fire alarm, data/comms |
| **P** | Plumbing | Domestic water, sanitary drainage, rainwater, gas |
| **A** | Architectural | Walls, doors, windows, ceilings, finishes |
| **S** | Structural | Columns, beams, slabs, foundations, steelwork |
| **FP** | Fire Protection | Sprinklers, fire suppression, fire-rated barriers |
| **LV** | Low Voltage | Data, security, comms, access control systems |
| **G** | General | Elements that don't fit a specific discipline |

### Status and Tracking

| Abbreviation | Full Term | Context |
|-------------|-----------|---------|
| **RAG** | Red / Amber / Green | Traffic-light status: RED <50% compliance, AMBER 50–80%, GREEN ≥80% |
| **KPI** | Key Performance Indicator | Measurable metric — the BCC overview shows 5 KPIs: elements, compliance %, warnings, issues, containers |
| **SEQ** | Sequence Number | The 4-digit serial number at the end of a tag (e.g. `0003`) — auto-incremented per discipline/system group |
| **EVM** | Earned Value Management | 4D/5D metric: CPI (Cost Performance Index) and SPI (Schedule Performance Index) measure project efficiency |
| **CPI** | Cost Performance Index | EVM metric: CPI > 1.0 = under budget, CPI < 1.0 = over budget |
| **SPI** | Schedule Performance Index | EVM metric: SPI > 1.0 = ahead of schedule, SPI < 1.0 = behind schedule |

### MEP System and Equipment Codes

| Abbreviation | Full Term | Context |
|-------------|-----------|---------|
| **HVAC** | Heating, Ventilation and Air Conditioning | System code for ductwork, AHUs, FCUs, VAVs — the largest MEP system group |
| **HWS** | Hot Water Supply | System code for domestic hot water distribution |
| **DHW** | Domestic Hot Water | System code for heated potable water (boilers, calorifiers, cylinders) |
| **DCW** | Domestic Cold Water | System code for potable cold water supply from mains |
| **SAN** | Sanitary | System code for waste drainage — toilets, basins, showers |
| **RWD** | Rainwater Drainage | System code for roof drainage, gutters, and downpipes |
| **GAS** | Gas Supply | System code for natural gas distribution |
| **FP** | Fire Protection (system) | System code for sprinkler and suppression systems |
| **LV** | Low Voltage (system) | System code for data, telecoms, security, AV systems |
| **FLS** | Fire Life Safety | System code for fire detection, alarm panels, emergency lighting |
| **COM** | Communications | System code for structured cabling and telecoms |
| **ICT** | Information and Communications Technology | System code for data networks, server rooms, Wi-Fi |
| **AHU** | Air Handling Unit | Product code: large HVAC unit with fans, filters, heating/cooling coils |
| **FCU** | Fan Coil Unit | Product code: small terminal HVAC unit serving a single zone |
| **VAV** | Variable Air Volume | Product code: duct terminal that varies airflow to a zone |
| **DB** | Distribution Board | Product code: electrical panel distributing power to circuits |
| **WC** | Water Closet (Toilet) | Product code: sanitary fitting for plumbing discipline |
| **WHB** | Wash Hand Basin | Product code: sanitary basin fitting |
| **SPR** | Sprinkler Head | Product code: fire suppression discharge point |


## 3. Opening the BCC

1. In Revit, look at the **STING Tools dockable panel** on the right side of your screen.
2. Go to the **BIM** tab.
3. Click the **"Coordination Center"** button (blue styling, near the top of the tab).
4. The BCC dialog opens as a modal window — it stays in front of Revit until you close it.

**Tip:** The BCC remembers which tab you were on last. When you close and reopen it, you return to the same tab.

**Keyboard shortcuts inside the BCC:**

| Key | Action |
|-----|--------|
| **Escape** | Close the dialog (or clear an inline panel) |
| **F5** | Refresh the current tab with fresh data |
| **Ctrl+E** | Export data (context-dependent) |
| **D1–D9** | Jump to tabs 1–9 directly (only when not typing in a text box) |

---

## 4. Overview Tab

The **Overview** tab is your morning dashboard. It shows the big picture of your model's health in one glance.

### KPI Cards (top row)

Five coloured cards across the top:

| Card | What it shows | Colour logic |
|------|---------------|-------------|
| **Total Elements** | Count of all taggable elements in the model | Blue (informational) |
| **Tag Compliance** | Percentage of elements with complete 8-segment tags | Green ≥80%, Amber 50–79%, Red <50% |
| **Warnings** | Total Revit warnings in the model | Green ≤10, Amber 11–50, Red >50 |
| **Open Issues** | Number of unresolved RFIs, NCRs, clashes | Green = 0, Amber 1–5, Red >5 |
| **Containers** | Percentage of tagged elements with all discipline containers populated | Same RAG thresholds as Tag Compliance |

**Hover** over any card to see a detailed breakdown (e.g., tagged vs untagged vs stale counts).

### Discipline Compliance Table

Below the KPI cards, a table shows compliance per discipline:

| Column | Meaning |
|--------|---------|
| Discipline | M (Mechanical), E (Electrical), P (Plumbing), A (Architectural), S (Structural), etc. |
| Total | Number of elements in that discipline |
| Tagged | How many have complete tags |
| Compliance % | Tagged ÷ Total, with RAG colouring |

**Double-click** a discipline row to select all elements of that discipline in the Revit model view.

### Action Required Panel

A yellow panel listing urgent items sorted by priority:

- Stale elements that need re-tagging
- Overdue issues past their SLA deadline
- Critical warnings
- Untagged elements
- Placeholder tokens (GEN, XX, ZZ, 0000)

Each item is clickable — clicking it runs the fix command immediately.

### Quick Actions Toolbar

Five buttons for the most common coordinator tasks:

| Button | What it does |
|--------|-------------|
| Run Daily QA | Executes the Daily QA workflow (retag stale → validate → audit) |
| Repeat Last Workflow | Re-runs whatever workflow you ran most recently |
| Full Compliance Dashboard | Shows detailed compliance report with per-discipline breakdown |
| Document Center | Opens the Document Management Center for CDE file management |
| New Meeting | Creates a new coordination meeting record |

### Compliance Forecast

If you have run workflows before, the BCC calculates a compliance trend from the last 5 runs and projects your compliance 3 cycles ahead, showing whether you are trending up, down, or stable.

---

## 5. Model Health Tab

The **Model Health** tab is your diagnostic centre. It answers: "Is my model in good shape?"

### Health Checks

A list of checks runs automatically when you open this tab. Each check shows:

- A **score** (e.g., 8/10)
- A **pass/fail icon** (green tick or red cross)
- A **Fix button** for failing checks

| Check | What it measures | Fix action |
|-------|-----------------|------------|
| Warnings | Count and severity of Revit warnings | Auto-Fix Warnings |
| Tag Completeness | % of elements with complete 8-segment tags | Tag New Only |
| Stale Elements | Elements moved/changed since last tag | Retag Stale |
| Parameters | Whether STING shared parameters are bound | Load Shared Params |
| Containers | Whether discipline containers are populated | Combine Parameters |
| Formulas | Whether 199 formulas have been evaluated | Evaluate Formulas |

### Action Panels

Click a failing check's **Fix** button to open an inline panel with:
- A description of the problem
- An element count showing how many elements are affected
- A one-click fix button

### Bottom Toolbar

| Button | Action |
|--------|--------|
| Refresh Health | Recalculate all health metrics |
| 45-Point Validation | Run the comprehensive template validation (data files, parameters, formulas, schedules, cross-references) |

---

## 6. Warnings Tab

Revit generates warnings when something is wrong with the model — overlapping walls, duplicate marks, unconnected pipes. The **Warnings** tab classifies every warning by category and severity so you can fix the worst ones first.

### Warning Tree

Warnings are displayed in a **TreeView** grouped by category (Geometric, Spatial, MEP, Structural, Annotation, Data, Compliance, etc.). Expand a category node to see individual warning descriptions, each showing how many elements are affected.

- **Double-click** a warning node to select the affected elements and zoom to them in a 3D section box view.
- **Right-click** for a context menu: Zoom to 3D, Select Elements, Suppress Warning.

### Warning Severity Levels

| Severity | Meaning | SLA (time to fix) |
|----------|---------|-------------------|
| CRITICAL | Blocks deliverables (COBie, handover) | 4 hours |
| HIGH | Affects data quality | 24 hours |
| MEDIUM | Should be resolved before next milestone | 1 week |
| LOW | Cosmetic or informational | 2 weeks |

### Warning Actions

| Button | What it does |
|--------|-------------|
| Auto-Fix Warnings | Automatically resolves: duplicate instances, room separation overlaps, duplicate marks, wall join issues |
| Create Issues from Warnings | Converts critical/high warnings into NCR/SI issue records for tracking |
| Export Warnings | Exports all warnings to CSV for BIM 360 / Aconex upload |
| Save Baseline | Saves current warning count as a baseline for trend comparison |
| Suppress Warnings | Hides specific warning types from the dashboard (persisted to config) |
| Compliance Mapping | Maps warnings to ISO 19650 / CIBSE / BS 7671 standard requirements |

### Health Score

A weighted score from 0–100 displayed at the top:
- Critical warnings: −20 points each
- High: −5 each
- Medium: −2 each
- Low: −1 each

---

## 7. Issues Tab

The **Issues** tab is your issue tracker — like a simplified Jira inside Revit. Every RFI, clash, snagging item, and non-conformance lives here.

### Issue Types (20 types)

Issues are colour-coded by type. Common types include:

| Type | Code | When to use |
|------|------|-------------|
| Request for Information | RFI | When design information is unclear |
| Non-Conformance Report | NCR | When something does not meet the standard |
| Site Instruction | SI | When issuing a directive on-site |
| Clash | CLASH | When two elements occupy the same space |
| Snagging | SNAG | Defects found during inspection |
| Design Change | DCR | When the design is formally changed |
| Health & Safety | HSE | Safety-related observations |

### Issue DataGrid

A full data table shows all issues with columns:

| Column | Description |
|--------|-------------|
| ID | Unique identifier (e.g., RFI-0001, NCR-0012) |
| Title | Short description |
| Type | Issue type code |
| Priority | CRITICAL, HIGH, MEDIUM, LOW |
| Status | OPEN, IN PROGRESS, CLOSED |
| Assignee | Person responsible |
| Created | Date raised |
| Age | Days since creation |

- **Overdue issues** are highlighted in red based on SLA thresholds (CRITICAL = 4 hours, HIGH = 24 hours).
- **Double-click** a row to select linked elements in the model and zoom to them.
- **Right-click** for context menu: Zoom to 3D Section Box, Select Elements, Update Status.

### Issue Actions

| Button | What it does |
|--------|-------------|
| Raise Issue | Create a new issue linked to selected Revit elements |
| Update Issue | Change status, priority, or assignee |
| BCF Export | Export issues as BCF 2.1 XML for Navisworks / Solibri / BIMcollab |
| BCF Import | Import issues from external clash detection tools |
| From Warnings | Auto-create NCR/SI issues from critical/high warnings |
| Bulk Close | Close multiple resolved issues at once |

### SLA Gate

At the top of the tab, a gate indicator shows PASS or FAIL:
- **PASS** — No critical or overdue issues
- **FAIL** — Critical or overdue issues require resolution before handover

---

## 8. Revisions Tab

The **Revisions** tab tracks changes to the model over time, following ISO 19650 revision procedures.

### Revision DataGrid

| Column | Description |
|--------|-------------|
| ID | Revision sequence number |
| Name | ISO 19650 formatted name |
| Date | Date created |
| Description | What changed |
| Clouds | Number of revision clouds |
| Status | Current/superseded |

- **Double-click** a revision to see a detailed dashboard with per-element changes.
- **Right-click** for context menu: Zoom to 3D, View Details, Compare.

### Revision Actions

| Button | What it does |
|--------|-------------|
| Create Revision | Create a new revision with ISO 19650 naming. Includes a compliance gate — if tag compliance is below 80%, you are warned before proceeding. |
| Take Snapshot | Capture the current model state (tag %, containers %, warnings, stale count) for later comparison |
| Compare Revisions | Select two snapshots and see exactly which tokens changed on which elements |
| Auto Rev Clouds | Automatically generate revision clouds around elements that changed since the last revision |
| Revision Schedule | View/create Revit revision schedules |

### Why Snapshots Matter

A snapshot records every element's tag values at a point in time. When you compare two snapshots, you get a CSV showing:
- **ADDED** — New elements tagged since the last snapshot
- **CHANGED** — Elements whose tokens changed (with old and new values)
- **REMOVED** — Elements deleted since the last snapshot

This is essential for ISO 19650 audit trails.

---

## 9. Workflows Tab

The **Workflows** tab lets you chain multiple commands into a single automated sequence. Instead of running 8 separate operations one by one, you press one button and the BCC runs them all in order.

### What is a Workflow?

A workflow is a list of steps. Each step is a STING command (like "Retag Stale" or "Validate Tags"). The BCC runs them top to bottom, tracks timing, and reports results.

### KPI Cards

| Card | What it shows |
|------|---------------|
| **Total Runs** | How many workflows have been executed in this project |
| **Last Run** | Name and time of the most recent workflow |
| **Compliance Δ** | Change in compliance percentage from the last workflow run |
| **History** | Number of recorded workflow executions |

### Quick Workflow Buttons

The tab shows buttons for the most commonly used presets. Click one to run the entire sequence:

| Preset | What it does | When to use |
|--------|-------------|-------------|
| **Daily QA Sync** | Retag stale → validate → audit → completeness dashboard | Every morning |
| **Morning Health Check** | Stale fix → warnings → tag new → validate → template audit | Start of day |
| **Project Kickoff** | Full 26-step project setup from blank template | New projects |
| **Handover Readiness** | Stale fix → tag → validate → COBie → BEP → revision | Before handover |
| **COBie Readiness** | Retag → resolve → containers → validate → COBie export | Before COBie submission |
| **End of Day Sync** | Retag → validate → baseline → registers → revision | End of each day |
| **Drawing Issue** | Templates → naming → fix warnings → print PDF → register | Before issuing drawings |
| **Clash Coordination** | Detect clashes → export BCF → create issues → assign | Before coordination meetings |
| **Healthcare NHS** | Medical gas → infection zones → HTM compliance | NHS projects |
| **Data Centre** | Power distribution → cooling → cable tray → Uptime Institute | Data centre projects |

### Workflow History DataGrid

A table below shows previous workflow runs:

| Column | Meaning |
|--------|---------|
| Time | When the workflow ran |
| Preset | Which workflow preset was used |
| Steps | How many steps in the workflow |
| Pass/Fail/Skip | How many steps succeeded, failed, or were skipped |
| Duration | Total time taken |
| Before/After | Compliance percentage before and after the run |

### Conditional Steps

Some workflows have **smart conditions**. For example:
- "Skip this step if compliance is already above 90%"
- "Only run this step if there are stale elements"
- "Skip if no warnings exist"

This means the workflow adapts to your model's current state rather than blindly running every step.

---

## 10. QA Dashboard Tab

The **QA Dashboard** tab is your quality assurance centre. It shows what is wrong with the data in your model, not the geometry.

### KPI Cards

| Card | What it shows | Why it matters |
|------|---------------|---------------|
| **Placeholders** | Elements with generic codes like GEN, XX, ZZ, 0000 | These are not real codes — they need replacing with actual values |
| **Anomalies** | Elements with detected data inconsistencies | E.g., DISC=M (Mechanical) but SYS=LV (Low Voltage) — a cross-discipline mismatch |
| **Stale** | Elements that have moved or changed since they were last tagged | The tag data is out of date |
| **Validation Errors** | Elements failing ISO 19650 code validation | Invalid codes that will be rejected at handover |

### Token Coverage Matrix

A table showing how many elements have each of the 8 tag tokens filled in:

| Token | Filled | Empty | Placeholder |
|-------|--------|-------|-------------|
| DISC | 4,521 | 12 | 0 |
| LOC | 4,200 | 321 | 12 |
| ZONE | 3,890 | 543 | 100 |
| ... | ... | ... | ... |

This tells you exactly which tokens need the most work.

### Cross-System Integrity

Shows correlations between data problems:
- Stale elements that also have warnings
- Warning elements that also have open issues
- Issues linked to elements with incomplete tags

### Actions

| Button | What it does |
|--------|-------------|
| Auto-Fix Anomalies | Automatically resolve cross-discipline mismatches |
| Resolve All Issues | One-click ISO 19650 compliance resolution |
| Schema Validate | Validate parameter data against the material schema |

---

## 11. 4D/5D Scheduling Tab

The **4D/5D** tab links your BIM model to construction time (4D) and cost (5D).

### What is 4D and 5D?

- **4D** = 3D model + time. Each element gets a construction phase and date, so you can simulate the build sequence.
- **5D** = 4D + cost. Each element also gets a cost, so you can track budgets.

### KPI Cards

| Card | What it shows |
|------|---------------|
| **Total Tasks** | Number of scheduled construction tasks |
| **Est. Cost** | Total estimated project cost |
| **Milestones** | Number of defined milestones (e.g., "structural frame complete") |
| **Earned Value %** | How much of the planned work has been completed (EVM) |

### Earned Value Management (EVM)

Two key metrics are shown:

| Metric | Formula | Meaning |
|--------|---------|---------|
| **CPI** (Cost Performance Index) | Earned Value ÷ Actual Cost | >1.0 = under budget, <1.0 = over budget |
| **SPI** (Schedule Performance Index) | Earned Value ÷ Planned Value | >1.0 = ahead of schedule, <1.0 = behind |

### Cost Breakdown

A mini bar chart shows cost by construction phase (e.g., Substructure, Frame, Envelope, MEP, Fit-out). Each bar shows progress as a percentage.

### Actions

| Button | What it does |
|--------|-------------|
| AutoSchedule4D | Auto-assign construction phases to elements by trade sequence |
| AutoCost5D | Auto-assign cost rates to elements from the cost database |
| ViewTimeline | Show a Gantt-style timeline of construction phases |
| CostReport | Generate a 5D cost summary report |
| CashFlow | Show cash flow forecast over project duration |
| ExportSchedule | Export 4D schedule to CSV for MS Project |

---

## 12. Deliverables Tab

The **Deliverables** tab tracks documents and data packages that must be submitted at each project stage. In ISO 19650, these are called **data drops** (DD1–DD4).

### KPI Cards

| Card | What it shows | Colour logic |
|------|---------------|-------------|
| **Total** | All tracked deliverables | Blue (informational) |
| **Pending** | Deliverables not yet started | Amber if >0, Green if 0 |
| **Submitted** | Deliverables sent for review | Teal |
| **Approved** | Deliverables accepted | Green |
| **Overdue** | Deliverables past their due date | Red if >0, Green if 0 |

### Deliverables DataGrid

An editable table listing each deliverable:

| Column | Description |
|--------|-------------|
| Name | Deliverable title (e.g., "COBie Component Sheet") |
| Data Drop | Which milestone it belongs to (DD1/DD2/DD3/DD4) |
| Status | Pending / In Progress / Submitted / Approved / Rejected |
| Due Date | Deadline |
| Assignee | Person responsible |

You can edit cells directly in the grid to update status or assignee.

### Transmittal Section

Below the grid, a transmittal panel lets you create formal records of document submissions. Each transmittal gets a unique **TX-NNNN** reference number and records:
- Which documents were sent
- Who sent them and to whom
- The date and CDE status

---

## 13. Meetings Tab

The **Meetings** tab manages BIM coordination meetings. It has 4 sub-tabs:

### Sub-tab 1: Meetings List

Shows upcoming and past meetings in a DataGrid:

| Column | Description |
|--------|-------------|
| Date | Meeting date |
| Type | BIM Coordination / Design Review / Client Review / Clash Resolution / Handover |
| Title | Meeting title |
| Status | Scheduled / Completed / Cancelled |
| Actions | Number of action items from this meeting |

### Sub-tab 2: Action Items

A full DataGrid of all action items across all meetings:

| Column | Description |
|--------|-------------|
| ID | Unique reference (ACT-001, ACT-002, …) |
| Description | What needs to be done |
| Assignee | Who is responsible |
| Due Date | Deadline |
| Priority | CRITICAL / HIGH / MEDIUM / LOW |
| Status | OPEN / IN PROGRESS / CLOSED |

**Overdue** items are highlighted in red. You can bulk-close selected items.

### Sub-tab 3: Minutes Editor

A text editor for recording meeting minutes. Minutes are saved as timestamped `.txt` files alongside the project.

### Sub-tab 4: Automation

Six cross-system automation rules that link meetings to other BCC systems:

| Rule | What it does |
|------|-------------|
| Overdue Action → Issue Escalation | Auto-creates NCR issues from overdue actions |
| Open Issues → Next Meeting Agenda | Populates the next agenda from open issues |
| Compliance Gate → Transmittal | Auto-creates transmittal when compliance hits 80% |
| Meeting Closure → Follow-Up | Auto-schedules follow-up meeting with open actions |
| SLA Violation → Escalation | Auto-escalates issue priority when SLA is breached |
| Stale Elements → Auto-Retag | Auto-retags elements that have moved |

---

## 14. Project Members Tab

The **Project Members** tab shows who is on the project team and what they can access. It has 3 sub-tabs:

### Sub-tab 1: Member Directory

Lists all team members with their name, role code, discipline, and contact.

### Sub-tab 2: Permission Groups

Shows the 14 ISO 19650 role definitions:

| Code | Role | CDE Write Access | Can Approve | Can Issue |
|------|------|-------------------|-------------|-----------|
| A | Architect | WIP, SHARED | Yes | Yes |
| M | MEP Engineer | WIP, SHARED | No | No |
| E | Electrical Engineer | WIP, SHARED | No | No |
| S | Structural Engineer | WIP, SHARED | No | No |
| C | BIM Coordinator | WIP, SHARED | No | Yes |
| K | BIM Manager | WIP, SHARED, PUBLISHED | Yes | Yes |
| I | Information Manager | All | Yes | Yes |
| Q | QA/QS | SHARED | No | No |

### Sub-tab 3: CDE Access Matrix

A table showing which roles can read, write, or approve documents in each CDE folder (WIP, SHARED, PUBLISHED, ARCHIVE, MODELS, DRAWINGS, etc.).

---

## 15. Platform Tab

The **Platform** tab connects your Revit model to external cloud platforms.

### Supported Platforms

| Platform | What it is |
|----------|-----------|
| **ACC** | Autodesk Construction Cloud (BIM 360) |
| **SharePoint** | Microsoft SharePoint / Teams document library |
| **Procore** | Construction project management platform |
| **Aconex** | Oracle Aconex document management |
| **Trimble Connect** | Trimble's cloud collaboration |
| **Bentley iTwin** | Bentley's digital twin platform |
| **Viewpoint 4P** | Viewpoint For Projects document control |
| **BCF Server** | BIM Collaboration Format server for clash management |

Click a platform tile to see its detail panel with connection settings, sync status, and action buttons.

### BCF Section

At the bottom, a dedicated BCF panel allows you to:

| Button | What it does |
|--------|-------------|
| BCF Export | Export issues as BCF 2.1 XML with 3D viewpoints |
| BCF Import | Import clash results from Navisworks / Solibri / BIMcollab |

BCF files are the standard way to share clash and issue data between different BIM tools.

---

## 16. Coord Log Tab

The **Coord Log** tab is a chronological record of every significant action taken in the BCC.

### What Gets Logged

Every time someone runs a workflow, fixes warnings, raises an issue, creates a revision, or exports a deliverable, an entry is added to the log.

### Columns

| Column | Description |
|--------|-------------|
| Timestamp | When the action happened |
| User | Who performed it |
| Action | What was done (e.g., "AutoFixWarnings", "CreateRevision") |
| Detail | Additional context (e.g., "Fixed 12 duplicate instances") |
| Impact | HIGH / MEDIUM / LOW |

### Filtering

- **Search box** — type to filter by action name, detail text, or user
- **Category dropdown** — filter by action category
- **Impact dropdown** — show only HIGH, MEDIUM, or LOW impact entries

### Actions

| Button | What it does |
|--------|-------------|
| Export Log | Export the full log to CSV for audit purposes |
| Clear Log | Clear the log (requires confirmation) |


---

## Appendix A — Daily Checklist

Use this checklist every day to keep your model healthy. The BCC automates most of these steps, but this list helps you verify nothing is missed.

### Morning (Start of Day)

1. **Open your Revit model** — StingTools runs a morning briefing automatically on first interaction
2. **Check the Overview tab** — look at the 5 KPI cards (Elements, Compliance %, Warnings, Issues, Containers)
3. **Review Action Required panel** — click any orange items to fix them
4. **Run Morning Health Check** — click "Run Morning Check" in the Workflows tab, or use the Overview quick action button
5. **Check for stale elements** — if the stale count is >0, click "Retag Stale" to update moved/changed elements
6. **Review SLA violations** — if any issues are overdue, update or escalate them in the Issues tab

### Midday (Coordination Check)

1. **Refresh the Overview** — press F5 to reload data
2. **Check new warnings** — switch to Warnings tab, expand new categories
3. **Run auto-fix** — click "Auto-Fix" for quick wins (duplicate instances, room separation overlaps)
4. **Review any new issues** — check the Issues tab for items raised by team members
5. **Update action items** — mark completed meeting actions in the Meetings tab

### End of Day

1. **Run "End of Day" workflow** — Workflows tab → "End of Day Sync"
2. **Save warning baseline** — Warnings tab → "Save Baseline" to track progress
3. **Create revision if needed** — Revisions tab → "Create Revision" for significant changes
4. **Export registers** — if submitting data, export Tag Register and Sheet Register from Deliverables tab
5. **Check compliance trend** — Overview tab → verify the trend arrow is stable or improving

---

## Appendix B — Common Problems and Solutions

| Problem | Cause | Solution |
|---------|-------|----------|
| Compliance stuck at 0% | Shared parameters not loaded | Run "Load Parameters" from the TEMP tab or Master Setup |
| Tags show as "GEN-XX-ZZ-XX-GEN-GEN-GEN-0000" | Elements have placeholder tokens only | Run "Resolve All Issues" from the QA Dashboard or "Full Auto-Populate" from CREATE tab |
| Containers show 0% | Combine Parameters never run | Run "Combine Parameters" from CREATE tab — this writes TAG1 to all 53 discipline containers |
| Warnings count is very high (500+) | Normal for new models | Run "Auto-Fix Warnings" — it resolves duplicate instances, overlapping room separation lines, and duplicate marks automatically |
| Issues tab is empty | No issues raised yet | This is normal — issues are created when you click "Raise Issue" or when auto-creation triggers |
| Revisions tab shows no data | No revision snapshots taken | Click "Take Snapshot" to capture the current model state |
| 4D/5D tab shows zeros | No schedule or cost data loaded | Import cost rates from `cost_rates_5d.csv` via "Import Cost Rates" in the BIM tab |
| Workflows fail mid-way | A required command cannot resolve | Check the error message — it usually names the failing step. Ensure data files exist in the Data folder |
| Morning briefing does not appear | Model is already healthy (no alerts) | The briefing only shows when there are issues to report — a silent opening means good health |
| Model Health score is RED | Multiple checks failing | Click "Fix" next to each failing check — the button runs the specific repair command |
| Elements not tagging | Category not in the 22 supported categories | Check if your element category is in the taggable list (see CLAUDE.md "22 tagged categories") |
| Stale count keeps increasing | Auto-tagger stale marker detecting geometry changes | Run "Retag Stale" to clear the backlog, then it will only track new changes |
| COBie export blocked | Compliance below 60% gate | Improve tag compliance above 60% or override the gate when prompted |
| CDE transition blocked | Compliance below required threshold | WIP→SHARED requires 70%, SHARED→PUBLISHED requires 90%. Improve tags or override |

---

## Appendix C — Keyboard Shortcuts

All shortcuts work when the BIM Coordination Center dialog is open.

| Shortcut | Action |
|----------|--------|
| **Escape** | Clear any open inline panel (closes detail views without closing the dialog) |
| **F5** | Refresh the current tab with fresh data |
| **Ctrl+E** | Export current report to CSV/HTML |
| **Ctrl+Q** | Jump to QA Dashboard tab |
| **Ctrl+Shift+S** | Jump to 4D/5D Scheduling tab |
| **Ctrl+D** | Jump to Deliverables tab |
| **Ctrl+L** | Jump to Coord Log tab |
| **Ctrl+T** | Jump to Project Members tab |
| **Ctrl+M** | Jump to Meetings tab |
| **1–9** | Jump to tab by number (1=Overview, 2=Model Health, 3=Warnings, 4=Issues, 5=Revisions, 6=Platform, 7=Workflows, 8=QA, 9=4D/5D). Only works when a text box is not focused |

### Mouse Interactions

| Action | Where | What happens |
|--------|-------|-------------|
| **Double-click** a discipline row | Overview tab | Selects all elements of that discipline in the model |
| **Double-click** a warning node | Warnings tab | Zooms to affected elements in a 3D section box view |
| **Double-click** an issue row | Issues tab | Zooms to linked elements in a 3D section box view |
| **Right-click** a warning/issue | Warnings or Issues tab | Context menu: Zoom to 3D, Select Elements, Update Status |
| **Hover** over a KPI card | Any tab | Shows drill-down tooltip with detailed breakdown |
| **Click "Fix"** on a health check | Model Health tab | Runs the specific repair command for that check |

---

## Appendix D — Action Tag Reference

Every button in the BCC dispatches an **action tag** — a short code that maps to a StingTools command. This table lists all action tags available in the BCC.

### Overview & Health

| Action Tag | Description |
|------------|-------------|
| RunDailyQA | Run Daily QA workflow: retag stale → validate → audit → dashboard |
| RunMorningCheck | Morning health check: warnings → tags → templates → issues → revisions |
| RetagStale | Find elements with stale tags (moved/changed) and re-derive their tags |
| TagNewOnly | Tag only new/untagged elements — skips already-tagged elements |
| RefreshHealth | Refresh model health metrics (warnings, tags, stale elements) |
| ExportHealth | Export model health report to CSV/HTML |
| RunFullCheck | Run 45-point template validation check (data files, parameters, formulas) |
| FullComplianceDashboard | Full project compliance report with per-discipline breakdown |
| RepeatLastWorkflow | Re-run the last workflow preset that was executed |
| ExportReport | Export current model health and compliance report to CSV or HTML |
| SelectAllTaggable | Select all taggable elements in the active view for batch operations |
| CombineParameters | Write tag values to all 53 discipline-specific container parameters |

### Warnings

| Action Tag | Description |
|------------|-------------|
| AutoFixWarnings | Auto-fix: duplicate instances, room separation overlaps, duplicate marks |
| CreateIssuesFromWarnings | Create NCR/SI issues from critical/high severity warnings |
| ExportWarnings | Export all classified warnings to CSV for BIM360/Aconex |
| SaveBaseline | Save current warning count as baseline for trend tracking |
| SaveExtendedBaseline | Save warning types + counts for regression analysis |
| SelectWarningElements | Select elements associated with a specific warning type |
| SuppressWarnings | Suppress warning types from dashboard (persisted to config) |
| WarningsCompliance | Map warnings to ISO 19650 / CIBSE / BS 7671 requirements |

### Issues & Revisions

| Action Tag | Description |
|------------|-------------|
| RaiseIssue | Raise RFI/Clash/NCR/Snagging issue with element linking + BCF |
| UpdateIssue | Update issue status, priority, assignee, or close issues |
| BCFExport | Export issues as BCF 2.1 XML for Navisworks/Solibri/BIMcollab |
| BCFImport | Import BCF issues from external clash detection tools |
| CreateTransmittal | Create ISO 19650 document transmittal record |
| CreateRevision | Create new revision with ISO 19650 naming and compliance gate |
| AutoRevisionCloud | Auto-generate revision clouds for changed elements |
| TakeSnapshot | Capture model compliance snapshot for trend tracking |
| RevisionCompare | Compare tag values between revision snapshots |

### Platform & Data Exchange

| Action Tag | Description |
|------------|-------------|
| PlatformSync | Bidirectional sync with CDE platform (delta detection) |
| CDEPackage | Package files into ISO 19650 CDE folder structure |
| CDEStatus | Set CDE status (WIP → SHARED → PUBLISHED → ARCHIVE) |
| ValidateDocNaming | Validate document naming against ISO 19650 convention |
| ExportToExcel | Export element data to Excel (30+ columns with tags, identity, spatial) |
| ImportFromExcel | Import data from Excel with validation and change tracking |
| ExcelRoundTrip | One-click export → edit → import Excel data exchange |
| COBieExport | Export COBie V2.4 (17 worksheets) for FM handover |
| ExportCOBie | Export COBie V2.4 FM handover data (17 worksheets, XLSX) |
| IFCExport | Export model as IFC with STING property mapping |
| ACCPublish | Package for Autodesk Construction Cloud / BIM 360 |
| SharePointExport | Export to corporate SharePoint / Microsoft Teams |

### QA & Validation

| Action Tag | Description |
|------------|-------------|
| ValidateTags | Validate tag completeness and ISO 19650 compliance |
| PreTagAudit | Dry-run audit: predict tags, collisions, ISO violations before tagging |
| AnomalyAutoFix | Auto-fix tag anomalies (DISC/SYS/FUNC/PROD/TAG7/stale) |
| ResolveAllIssues | One-click ISO 19650 compliance resolution (batched, 500 elements) |
| StageComplianceGate | RIBA stage-gated compliance check with data drop requirements |

### Meetings

| Action Tag | Description |
|------------|-------------|
| NewMeeting | Create new meeting (BIM Coordination, Design Review, Client Review, Handover, Clash Resolution) |
| AddActionItem | Create action item with assignee, due date, and priority (ACT-NNNN ID) |
| AutoAgenda | Auto-generate agenda from open issues, pending transmittals, recent revisions |
| LogMinutes | Record timestamped meeting minutes |
| MeetingTemplates | Browse 5 meeting templates |
| MeetingHistory | View past meetings with minutes and action items |
| OpenActions | View outstanding action items grouped by overdue/assignee |
| ExportMinutes | Export minutes to timestamped text file |
| SendReminder | Generate email reminder for outstanding action items |
| EscalateActions | Auto-create NCR issues from overdue meeting actions |

### Permissions & Documents

| Action Tag | Description |
|------------|-------------|
| EditUserRole | Change your active ISO 19650 role (determines CDE access and approval rights) |
| SavePermissions | Save permission matrix to project_config.json |
| CreateFolders | Create ISO 19650 CDE folder structure |
| ExportPermissionMatrix | Export role-based permission matrix to CSV |
| AddDocument | Register new deliverable in document register |
| DocumentRegister | View/manage document register entries |
| DocumentManager | Open Document Management Center |

---

## Appendix E — Real-World Workflow Examples

These 5 scenarios show the BCC solving actual coordination problems. Each one includes the situation, the step-by-step solution, and the measurable outcome.

---

### Example 1: Monday Morning Model Health Check

**Situation:** You arrive at 8:30 AM on a Monday. The MEP subcontractor worked over the weekend, adding 400+ ductwork elements. You need to know the model's state before the 10:00 AM design team meeting.

**Solution — 4 clicks, 12 minutes:**

1. **Open Revit → Open the project file**
   - StingTools runs a morning briefing automatically — a dialog shows overnight compliance changes, stale element count, and any SLA violations.

2. **Open BCC** (BIM tab → Coordination Center)
   - The **Overview tab** loads instantly. You see 5 KPI cards:
     - Total Elements: **14,280**
     - Tag Compliance: **76%** (AMBER — was 82% GREEN on Friday)
     - Warnings: **47** (up from 31)
     - Open Issues: **3** (1 overdue)
     - Container Compliance: **71%**

3. **Click "Run Morning Check" in the Quick Actions toolbar**
   - This triggers the `MorningHealthCheck` preset (10 steps):
     - Retag 400 stale elements → auto-fix 12 warnings → tag 400 new elements → validate → template check → model health → issues review → revision check → compliance dashboard
   - **Progress bar shows each step** — total run time: ~8 minutes on 14K elements

4. **Check the result**
   - Tag Compliance: **76% → 91%** (GREEN)
   - Warnings: **47 → 29** (12 auto-fixed, 6 suppressed nuisance warnings)
   - Stale elements: **412 → 0**
   - The BCC stays open — you can drill into any tab for detail

**Result:** In **12 minutes** you went from a weekend-disrupted model to **91% GREEN compliance**, ready for the 10 AM meeting. Without StingTools, manually tagging 400 elements and checking warnings would take **3–4 hours**.

---

### Example 2: Clash Coordination Meeting Prep

**Situation:** You have a fortnightly clash coordination meeting at 2:00 PM. The structural engineer raised 8 RFIs last week, and Navisworks found 23 clashes between MEP and structure. You need an agenda, the latest compliance state, and BCF files for discussion.

**Solution — 6 clicks, 18 minutes:**

1. **Open BCC → Meetings tab**
   - Click **"Auto Agenda"** — StingTools generates an agenda from:
     - 8 open RFIs (grouped by discipline)
     - 23 CLASH issues (from BCF import)
     - 2 pending transmittals
     - Current compliance: **88%** (up from 82% last meeting)
     - 4 overdue action items from the last meeting

2. **Switch to Issues tab**
   - Filter by **Status: Open** — see all 31 active issues
   - Double-click the highest-priority CLASH issue → BCC zooms to the clash location in a 3D section box view
   - Right-click → **"Zoom to 3D Section Box"** on 3 more clashes to screenshot for the meeting slides

3. **Click "BCF Export"** in the Issues action bar
   - Exports all 23 CLASH issues as BCF 2.1 XML with camera viewpoints
   - File saved next to the project: `ProjectName_BCF_2026-04-11.bcf`

4. **Switch to Model Health tab → Click "Export Health"**
   - Generates an HTML report with per-discipline compliance, warning summary, and KPI cards
   - Share the `.html` file with attendees — **no Revit licence needed** to view it

5. **During the meeting — log minutes directly in BCC**
   - Meetings tab → **"Log Minutes"** → type decisions and notes
   - **"Add Action Item"** → assign follow-ups with due dates

6. **After the meeting — Click "Export Minutes"**
   - Saves timestamped `.txt` file with all minutes and new action items

**Result:** Meeting prep completed in **18 minutes** instead of the usual **2 hours** of manual screenshot-taking, spreadsheet-updating, and email-chasing. The BCF export means the structural engineer can **see exact clash locations** in their own BIM tool.

---

### Example 3: RIBA Stage 3 → Stage 4 Gate

**Situation:** The client has a Stage 3 design freeze deadline on Friday. To pass the stage gate, the BEP requires ≥85% tag compliance, complete COBie Type and System sheets, and zero CRITICAL warnings. Your model is at 78%.

**Solution — 3 sessions over 3 days, ~90 minutes total:**

**Day 1 (Wednesday) — Assess the gap:**

1. **Open BCC → Click "Stage Gate" in Overview Quick Actions**
   - The `StageComplianceGate` command auto-detects RIBA Stage 3 → DD2 requirements:
     - Tag compliance: **78%** — needs ≥85% (gap: **840 elements**)
     - Container compliance: **72%** — needs ≥70% (PASS ✓)
     - CRITICAL warnings: **4** — needs 0 (FAIL ✗)
     - COBie Type sheet: **62 of 85 types populated** (FAIL ✗)

2. **Run the `HandoverReadiness` workflow preset**
   - Retags stale elements → full batch tag → validate → COBie export (preview mode)
   - Tag compliance jumps: **78% → 86%** after batch tagging 840 previously untagged elements
   - COBie preview shows 23 type records still missing manufacturer data

3. **Switch to Warnings tab → Click "Auto Fix"**
   - 3 of 4 CRITICAL warnings auto-fixed (duplicate instances, overlapping room separations)
   - 1 remaining CRITICAL: "Host has been deleted" — needs manual fix (element reference lost)

**Day 2 (Thursday) — Fix remaining gaps:**

4. **Manually fix the 1 CRITICAL warning** (delete orphaned annotation referencing deleted host)

5. **Open BCC → QA Dashboard tab**
   - Token coverage matrix shows: DISC 100%, SYS 98%, FUNC 94%, PROD 91%
   - 23 elements have placeholder PROD codes (GEN/XX) — switch to Issues tab, raise a bulk DATA issue

6. **Run `COBieReadiness` workflow**
   - Validates ISO codes → writes containers → exports COBie preview
   - COBie Type sheet: **85 of 85 populated** (PASS ✓ after manufacturer data entry)

**Day 3 (Friday) — Final check and submit:**

7. **Open BCC → Click "Stage Gate" again**
   - Tag compliance: **92%** (GREEN ✓)
   - Container compliance: **89%** (GREEN ✓)
   - CRITICAL warnings: **0** (GREEN ✓)
   - COBie Type: **complete** (GREEN ✓)
   - **Verdict: STAGE 3 GATE PASSED**

8. **Click "Create Revision"** → CDE status → SHARED (S4: Fit for Stage Approval)

**Result:** Stage gate passed in **~90 minutes of actual work** spread over 3 days. Compliance rose from **78% to 92%**. The COBie export was complete on first submission — **no client rejection and resubmission cycle**. Manual equivalent: 2–3 full days of a senior BIM coordinator's time.

---

### Example 4: Emergency Snagging Issue on Site

**Situation:** It's Thursday afternoon. The site manager calls: "There's a sprinkler head clashing with a cable tray at Level 3, grid intersection C-7. We need a formal response before the ceiling goes up tomorrow morning."

**Solution — 5 clicks, 8 minutes:**

1. **Open BCC → Issues tab → Click "Raise Issue"**
   - Issue Type: **CLASH** (hard clash, cross-discipline)
   - Priority: **CRITICAL** (SLA = 4 hours)
   - Title: "Sprinkler head / cable tray clash at L03 C-7"
   - Select the 2 affected elements in the Revit model → they're auto-linked to the issue

2. **The issue is auto-assigned** to the MEP lead (based on DISC=FP discipline detection from the selected elements)

3. **Click "BCF Export"** — export the single issue as BCF with a 3D section box viewpoint centred on grid C-7
   - Email the `.bcf` file to the MEP subcontractor for immediate review

4. **Switch to Meetings tab → Click "Add Action Item"**
   - Description: "Resolve sprinkler/cable tray clash at L03 C-7"
   - Assignee: MEP Lead
   - Due: Tomorrow 8:00 AM
   - Priority: CRITICAL

5. **The MEP subcontractor resolves the clash**, updates the model, and you **re-run AutoTag on the affected area**
   - Come back to BCC → Issues tab → right-click the issue → **"Update Status" → CLOSED**
   - StingTools auto-links the resolution to the latest revision snapshot

**Result:** Issue raised, assigned, exported, and tracked in **8 minutes**. The 4-hour SLA was met. The full audit trail (issue → BCF → action item → resolution → revision) satisfies **ISO 19650-2 Section 5.6** requirements. Without BCC, the coordinator would spend **45+ minutes** writing emails, attaching screenshots, and manually logging the issue in a spreadsheet.

---

### Example 5: COBie Handover to Facilities Management

**Situation:** The project is at RIBA Stage 6 (Handover). The FM team needs a complete COBie V2.4 workbook covering 2,400 MEP assets across 6 floors. The BEP requires DD4 compliance (≥95% tag completeness).

**Solution — 2 sessions, ~45 minutes total:**

**Session 1 — Validate readiness:**

1. **Open BCC → Click "Data Drop Readiness" in Overview**
   - Auto-detects DD4 milestone:
     - Tag compliance: **93%** — needs ≥95% (gap: **168 elements**)
     - Container compliance: **91%** — needs ≥95%
     - Placeholders (GEN/XX/ZZ): **42 elements** — needs 0
     - Stale elements: **7** — needs 0

2. **Run `COBieReadiness` workflow (7 steps)**
   - Retag 7 stale → resolve 42 placeholders → validate → write containers → schema validate → COBie preview → tag register
   - Tag compliance: **93% → 97%** (GREEN ✓)
   - Placeholders: **42 → 3** (3 elements in unmapped custom categories — raise DATA issue for manual resolution)

3. **Fix the 3 remaining placeholders manually** (assign correct PROD codes via dockable panel TOKEN section)

**Session 2 — Export and deliver:**

4. **Open BCC → 4D/5D tab → Click "COBie Export"**
   - StingTools runs a **pre-export compliance gate**: tag ≥95% ✓, containers ≥95% ✓, 0 CRITICAL warnings ✓, 0 stale ✓
   - A **pre-export container staleness check** samples 200 elements — all containers current
   - COBie V2.4 export generates **17 worksheets**:
     - Instruction (metadata), Facility, Floor, Space, Zone, Type, Component (2,400 rows), System, Assembly, Connection, Spare, Resource, Job, Document, Coordinate, Attribute, Impact
   - File saved: `ProjectName_COBie_2026-04-11.xlsx` (2.8 MB)

5. **Verify the export**
   - Open the Excel file — check Component sheet has 2,400 rows with SerialNumber, BarCode, InstallationDate populated
   - Check Type sheet — all 85 equipment types have Manufacturer, ModelNumber, WarrantyDuration
   - Check System sheet — 14 MEP systems grouped by actual SYS token distribution

6. **Create final transmittal**
   - BCC → Click "Create Transmittal" → recipient: FM Team → attach the COBie file
   - CDE Status → PUBLISHED → ARCHIVE (S7: Fit for AIM Authorization)
   - BCC → Click "Create Revision" — final handover revision with compliance snapshot

**Result:** Complete **17-worksheet COBie V2.4 handover** delivered in **45 minutes** with **97% tag compliance**. The FM team receives a validated dataset that imports directly into their CAFM system — **no manual data re-entry**. Manual COBie population from scratch typically takes **2–3 weeks** for a project of this size.

