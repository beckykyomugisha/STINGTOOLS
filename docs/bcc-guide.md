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

