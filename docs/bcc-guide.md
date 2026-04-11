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

