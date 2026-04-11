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

