# STING Template Manager — User Guide

The **Template Manager** is STING's single control surface for setting up and
maintaining a Revit project's graphic standards: shared parameters, view
filters, worksets, view templates, object/line/text/dimension/fill styles,
VG overrides, schedules, family parameters, and the automation that ties
them together.

Everything is reachable from one window — the **Template Manager Dashboard
(v2)** — which adds a live readiness scan, dependency awareness, previews,
and an in-window result log on top of the individual commands.

---

## 1. Opening the dashboard

In the STING dock panel, open the **VIEW / Template Mgr** area and click the
green **★ Dashboard** button (tooltip: *"Unified Template Manager Dashboard"*).

> The dashboard works best with a project document open. It still opens with
> no document, but every **Run** button is disabled until a document is
> active (read-only browsing of the catalogue still works).

The dashboard stays open between operations: after each run, control returns
to the dashboard so you can chain operations without re-launching it. Click
**Close** when finished.

---

## 2. Anatomy of the window

```
┌─────────────────────────────────────────────────────────────────────┐
│ HEADER   document name + 5 readiness "lights" (RAG + done/total)      │
├───────────────┬───────────────────────────────────────────────────────┤
│ NAV (left)    │ DETAIL (right)                                        │
│  search box   │   op title + flags (Destructive / Read-only)          │
│  ▸ SETUP      │   description                                         │
│  ▸ TEMPLATES  │   readiness badge for this op                         │
│  ▸ STYLES     │   "Requires" box (dependencies + their status)        │
│  ▸ SCHEDULES  │   preview grid (when the op supports it)              │
│  ▸ AUTOMATION │   last result for this op                             │
│  ▸ CORPORATE  │   ▶ Run                                               │
│  ▸ CROSS-ENG  │                                                       │
├───────────────┴───────────────────────────────────────────────────────┤
│ FOOTER / LOG  collapsible history of every operation this session     │
└─────────────────────────────────────────────────────────────────────┘
```

### Header — readiness lights
Five lights summarise how "set up" the project is. They are recomputed each
time the dashboard opens (and after each run):

| Light | What it measures |
|---|---|
| **Shared Parameters** | STING shared parameters bound to project categories |
| **STING Filters** | The 28 multi-category view filters present |
| **Worksets** | ISO 19650 worksets present (greys out / notes "Not workshared" on non-workshared models) |
| **View Templates** | The 23 STING discipline view templates present |
| **Styles & Patterns** | Fill / line / object / text / dimension styles present |

Each light shows a **done / total** count and a **RAG** colour.

### Left nav — operation tree
Operations are grouped into seven sections (see §6). Each row shows:
- a **RAG dot** (red/amber/green) for that operation's readiness, and
- a **(done / total)** badge where a count is meaningful (e.g. `28/28` filters).

A **search box** at the top filters the whole tree live across titles,
descriptions, and command tags. Press **Ctrl+F** to jump to it.

### Right detail pane
Selecting an operation shows its title, flag pills, description, its own
readiness badge, a **Requires** box listing prerequisite operations (each
with its own RAG dot + count), an optional **preview grid**, the **last
result** from this session, and the **▶ Run** button.

### Footer — log pane
A collapsible log shows the running history of operations executed this
session (sourced from the internal `OperationResultBus`), so you can see
what ran, in what order, and the outcome — without chasing TaskDialogs.

---

## 3. Reading status: RAG + badges

The same RAG convention is used for the header lights, the nav dots, and the
dependency rows:

| Colour | Meaning |
|---|---|
| 🔴 **Red** | Not present yet (done = 0) |
| 🟠 **Amber** | Partially present (0 < done < total) |
| 🟢 **Green** | Complete (done = total) |

A green tree means the project already carries STING's full standard for that
area; amber/red tells you exactly what's missing before you run anything.

---

## 4. Operation flags

Each operation can carry one or more flags, shown as pills in the detail pane:

| Flag | Pill | Meaning |
|---|---|---|
| **★ Highlighted** | green Run button | The "do everything" entry points — wizards and Master Setup |
| **Destructive** | red | Modifies/overwrites existing graphics. Capture a snapshot first (see §8) |
| **Read-only** | green | No transaction — safe to run repeatedly (audits, validators, browsers) |
| **Requires** | dependency box | Will warn **"⚠ Run dependencies first"** until prerequisites are green |

**Dependency chain** (enforced by the *Requires* box):

```
CreateParameters ──▶ CreateFilters ──▶ ViewTemplates ──▶ AutoAssignTemplates
                            └──────────────┴──▶ ApplyFilters
```

You can still force-run an operation with unmet dependencies, but the warning
tells you the result will be incomplete (e.g. templates with no filters to
apply).

---

## 5. The Run flow

1. Select an operation in the nav tree.
2. Review its description, readiness badge, dependencies, and preview.
3. Click **▶ Run** (the button shows the selected count when a preview is
   present, e.g. `▶ Run (12)`).
4. For **Destructive** operations, confirm the warning — and consider running
   **Capture state snapshot** first.
5. The result renders inline in the detail pane and is appended to the log.
6. The readiness lights and badges refresh automatically.
7. Pick the next operation — the window stays open.

---

## 6. Operation catalogue

### SETUP — project foundations
| Operation | What it does |
|---|---|
| **Create Shared Parameters** | 2-pass bind of STING shared parameters to project categories (from `MR_PARAMETERS.txt`) |
| **Create Filters** | 28 multi-category view filters (Mechanical, Electrical, Plumbing, …) — *requires Shared Parameters* |
| **Create Worksets** | 35 ISO 19650-compliant worksets |
| **Create Line Patterns** | 10 ISO 128-2:2020 line patterns |
| **Create Phases** | Reports phase status *(read-only)* |
| **★ Master Setup** | Runs all setup steps in sequence (20 steps) |

### TEMPLATES — view templates & health
| Operation | What it does |
|---|---|
| **Create View Templates** | 23 STING discipline view templates with VG — *requires Filters* |
| **Auto-Assign Templates** | 5-layer intelligent matching (name → level → phase → scope → type) — *requires View Templates* |
| **Clone Template** | Deep clone with VG, filters, and overrides |
| **Apply Filters to Templates** | Apply STING filters to all STING templates — *requires Filters + View Templates* |
| **Sync VG Overrides** | Re-apply VG overrides to restore discipline colours *(destructive)* |
| **Auto-Fix Templates** | One-click template health repair *(destructive)* |
| **Batch VG Reset** | Reset VG settings across multiple views *(destructive)* |
| **Template Audit** | Deep compliance audit with scoring *(read-only)* |
| **Template Diff** | Compare VG settings between two templates *(read-only)* |
| **Compliance Score** | Weighted 10-point score per view *(read-only)* |

### STYLES — annotation & graphics standards
| Operation | What it does |
|---|---|
| **Fill Patterns** | 12 ISO 128-2:2020 fill patterns |
| **Line Styles** | 16 ISO line styles (from CSV, with hard-coded fallback) |
| **Object Styles** | 40+ ISO category line weights / colours |
| **Text Styles** | 12 ISO 3098 text-note types |
| **Dimension Styles** | 7 ISO dimension types |
| **VG Overrides** | 6-layer VG override intelligence |

### SCHEDULES & DATA
| Operation | What it does |
|---|---|
| **Create Template Schedules** | Standard schedule templates from CSV |
| **Material Schedules** | Material takeoff schedules (8 categories) |
| **Cable Trays** | Cable-tray types from `MEP_MATERIALS.csv` |
| **Conduits** | Conduit types from `MEP_MATERIALS.csv` |
| **Batch Family Parameters** | Add shared parameters to loaded families |
| **Family Parameter Processor** | Batch `.rfa` parameter processing on disk |

### AUTOMATION — wizards, validation & governance
| Operation | What it does |
|---|---|
| **★ Template Setup Wizard** | 15-step complete automation pipeline |
| **★ Project Setup Wizard** | 7-page comprehensive project wizard |
| **Validate Template** | 45 validation checks *(read-only)* |
| **Dynamic Bindings** | Load bindings from `BINDING_COVERAGE_MATRIX.csv` |
| **Schema Validate** | Verify CSV columns match `MATERIAL_SCHEMA.json` *(read-only)* |
| **Template VG Audit** | Visual-graphics override analysis *(read-only)* |
| **Template drift scan** | SHA-256 diff vs last stamped state — surfaces user-edited STING templates *(read-only)* |
| **Stamp template checksums** | Refresh the drift baseline on every STING template |
| **Capture state snapshot** | Write a `state.json` snapshot before a destructive op *(read-only)* |
| **Verify audit-log chain** | Recompute the SHA-256 chain on the template audit log; reports tampering *(read-only)* |

### CORPORATE LIBRARY — shared standards
| Operation | What it does |
|---|---|
| **Pull from corporate library** | Copy `*.json` overlays from the configured library into `_BIM_COORD/` |
| **Push to corporate library** | Push project `_BIM_COORD/*.json` back with a timestamped backup *(destructive)* |
| **Configure library** | Set the corporate library path + channel *(read-only)* |

### CROSS-ENGINE — browse related catalogues *(read-only)*
| Operation | What it does |
|---|---|
| **AEC Filter Library (289)** | Browse + lazy-create from the corporate filter library |
| **Drawing Types catalogue (90)** | Browse the drawing-type catalogue |
| **View Style Packs (22)** | Browse the view-style packs (managed + external) |

---

## 7. The intelligence under the hood

### 5-layer template auto-assignment
**Auto-Assign Templates** evaluates each unassigned view against five layers,
first match wins:
1. **Name pattern** — keywords in the view name (e.g. "Mechanical" → STING - Mechanical Plan)
2. **Level-aware override** — level keywords (e.g. "Plant Room" → Mechanical Plan)
3. **Phase-aware mapping** — phase name (e.g. "Existing" → As-Built Plan)
4. **Scope-box inference** — falls back to the scope-box name
5. **View-type default** — per-ViewType fallback (FloorPlan → Architectural, etc.)

### 10-point compliance scoring
**Compliance Score** rates each view on a weighted 10-point scale:
HasTemplate · IsStingTemplate · HasFilters · FilterOverrides · DetailLevel ·
CorrectDiscipline · PhaseCorrect · VGConsistent · NoOrphans · ScaleAppropriate.

### Drift detection
STING templates are checksummed (`STING_TEMPLATE_CHECKSUM_TXT`).
**Template drift scan** diffs the live state against the last stamp and lists
templates a user has hand-edited; **Stamp template checksums** resets the
baseline once you accept the current state as canonical.

---

## 8. Safety: snapshots, destructive ops & the audit log

- **Before any destructive op** (Sync VG Overrides, Auto-Fix Templates, Batch
  VG Reset, Push to corporate library), run **Capture state snapshot** — it
  writes `_BIM_COORD/snapshots/<timestamp>/state.json` so you can see (and
  manually restore) the pre-change state.
- Every state-changing operation appends to a tamper-evident **audit log**;
  **Verify audit-log chain** recomputes its SHA-256 chain and flags any
  tampering.
- **Read-only** operations never open a transaction and are always safe to
  re-run.

---

## 9. Recommended workflows

### A. Brand-new project (fastest path)
1. **★ Master Setup** (or **★ Template Setup Wizard** for a guided 15 steps).
2. **Auto-Assign Templates** to bind every view to the right template.
3. **Compliance Score** to confirm the project is green.

### B. Inherited / messy project (clean-up)
1. **Template Audit** + **Compliance Score** — see what's wrong.
2. **Template drift scan** — find hand-edited STING templates.
3. **Capture state snapshot**.
4. **Auto-Fix Templates** / **Sync VG Overrides** to repair.
5. **Stamp template checksums** to set a fresh drift baseline.

### C. Roll out / receive corporate standards
1. **Configure library** (set path + channel).
2. **Pull from corporate library** to bring overlays into `_BIM_COORD/`.
3. Run the relevant SETUP / STYLES operations.
4. **Push to corporate library** when you've improved the standard (snapshot first).

### D. Family parameters
1. **Batch Family Parameters** for families already loaded in the model.
2. **Family Parameter Processor** for batch `.rfa` files on disk.

---

## 10. Data files the Template Manager reads

| File | Used by |
|---|---|
| `MR_PARAMETERS.txt` | Create Shared Parameters |
| `BINDING_COVERAGE_MATRIX.csv` | Dynamic Bindings |
| `MATERIAL_SCHEMA.json` | Schema Validate |
| `MEP_MATERIALS.csv` | Cable Trays, Conduits |
| `_BIM_COORD/*.json` | Corporate Library pull/push, snapshots, audit log |

(Data files ship in the `data/` folder beside the DLL; project-scoped
overlays live under the project's `_BIM_COORD/` folder.)

---

## 11. Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Every **Run** button is greyed out | No active project document — open a model first |
| **"⚠ Run dependencies first"** | A prerequisite isn't green yet — run the items in the *Requires* box |
| Worksets light greyed / "Not workshared" | The model isn't workshared; enable worksharing if you need STING worksets |
| A light stays amber after running | The op partially succeeded — open the log pane / result panel for the detail |
| Drift scan flags templates you didn't change | Someone edited a STING template by hand — review, then re-stamp checksums |
| You still see the **old tabbed dashboard** | The plugin DLL needs a rebuild + redeploy; the dashboard button is wired to the v2 layout in code |

---

## 12. Quick reference

- **Open:** dock panel → VIEW / Template Mgr → **★ Dashboard**
- **Search ops:** Ctrl+F
- **Fastest setup:** ★ Master Setup → Auto-Assign Templates → Compliance Score
- **Before destructive ops:** Capture state snapshot
- **Health check:** Template Audit · Compliance Score · Template drift scan
- **Read-only = safe to re-run; Destructive = snapshot first**
