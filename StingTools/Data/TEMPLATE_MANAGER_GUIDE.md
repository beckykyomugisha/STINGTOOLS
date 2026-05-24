# STING Template Manager — Layman's User Guide

**Audience:** any Revit user who has the STING plugin installed. No coding required. No BIM-jargon assumed beyond "view template" and "shared parameter".

**Scope:** every screen, every button, every keyboard shortcut, every file the Template Manager writes, with plain-English explanations of *why* it exists and *when* to use it.

---

## 1. What the Template Manager is for

A Revit project needs a lot of housekeeping before it's actually usable: shared parameters bound, filters created, worksets set up, line patterns, fill patterns, view templates, dimension styles, schedules, and so on. Doing all of that by hand is hours of click-work — and easy to forget steps.

The **Template Manager** is a one-window control room for that housekeeping. It can:

- Tell you at a glance what's already set up and what isn't (the **readiness lights**).
- Show you exactly what will be created *before* you click Run (the **preview grid**).
- Recommend the next thing you should do based on the current state (the **suggestion strip**).
- Run multi-step pipelines for you (the **recipes** — including the famous **Master Setup**).
- Roll back safely if something breaks (**snapshots**).
- Catch drift when someone hand-edited a STING template (**drift scan**).
- Lock down templates so they can't be accidentally overwritten (**Lock toggle**).
- Pull/push corporate baselines so every project starts from the same library.

It also keeps a **tamper-evident audit log** (SHA-256 chain) for every action taken — useful for QA defence and client handovers.

---

## 2. How to open it

Three ways:

1. **Ribbon:** click the **STING Tools** tab → look for **Template Manager** in the Setup panel.
2. **STING Dock Panel:** click **STING Panel** on the ribbon → switch to the **TEMP** tab → click the **Template Manager** button.
3. **Keyboard:** any time the STING dock panel is open, you can hover the Template Manager button and middle-click for context help, or just left-click to open.

You can have a Revit project open or not. If you don't, the dashboard still opens in inspection mode but Run buttons are disabled.

---

## 3. The dashboard at a glance

When it opens you'll see this layout:

```
┌──────────────────────────────────────────────────────────────────────────┐
│  Template Manager                                  [↻ Refresh]  [Theme]  │  ← Header
├────────────┬─────────────────────────────────────────┬───────────────────┤
│            │  ● Params  ● Filters  ● Worksets  …    │  Session log       │
│   NAV      ├─────────────────────────────────────────┤                   │
│ (sidebar)  │  ★ Suggestions                          │  14:22  ✓ Filters │
│            │  ● Create STING view filters …          │  14:21  ⚠ Drift   │
│            │                                         │  14:20  ✓ Worksets│
│  Search…   │  ── Create View Templates ──            │                   │
│            │  23 STING discipline view templates     │                   │
│ ▼ SETUP    │                                         │                   │
│   ○ Params │  Requires: Filters ●                    │                   │
│   ● Filters│                                         │                   │
│   ○ Worksts│  ┌───────────────────────────────────┐  │                   │
│ ▼ TEMPLATES│  │☑│Name           │Discipline│Exists│  │                   │
│   ○ ViewT. │  │☑│STING - Mech.  │   M      │  ✗   │  │                   │
│   …        │  │☐│STING - Elec.  │   E      │  ✓   │  │                   │
│ ▼ STYLES   │  └───────────────────────────────────┘  │                   │
│ ▼ AUTO     │  22 of 23 selected                       │                   │
│ ▼ CORPORATE│                                         │                   │
│ ▼ CROSS-…  │  [▶ Run (22)]                           │                   │
└────────────┴─────────────────────────────────────────┴───────────────────┘
 Status: Last run: Create Filters · 8.2s · 28 created                        ← Footer
```

Three columns, separated by **draggable splitter bars** so you can resize as needed:

- **Left:** Navigation sidebar (list of operations, search box).
- **Centre:** Detail pane (lights + suggestions + the currently-selected op).
- **Right:** Session log (every operation you've run this session).

You can collapse the right log pane to zero width if you don't need it — drag its splitter all the way right.

---

## 4. The header bar

Across the top of the window:

| Element | What it does |
|---|---|
| **Title** "Template Manager" | Just identifies the window. |
| **Document name** | The Revit project you're working in. Says "(no document)" if nothing is open. |
| **↻ Refresh (F5)** button | Recomputes the readiness lights, the per-op badges, and the suggestion strip. Use after big changes outside the dashboard. |
| **Theme** button | Cycles through the four corporate themes (Cool / Warm / Light / Corporate). Pure cosmetic — your preference. |

**Keyboard shortcut:** **F5** anywhere in the dashboard triggers Refresh.

---

## 5. The readiness lights (5-light strip)

The strip just below the header shows five **traffic-light tiles**. Each is a quick health-check answer to a project setup question.

| Light | What it counts | When you should worry |
|---|---|---|
| **Shared Parameters** | How many of STING's shared parameters are bound on this project | RED until you've run **Create Shared Parameters** at least once |
| **STING Filters** | Filters whose name starts with "STING - " | RED if 0; AMBER if some are missing |
| **Worksets** | STING-prefixed worksets — only counts when the project is workshared | Says "Not workshared" if your project doesn't use worksharing |
| **View Templates** | View templates whose name starts with "STING - " | RED before first **Create View Templates** run |
| **Styles & Patterns** | Combined line + fill pattern count | RED on a brand-new project |

Each tile shows:
- A **coloured dot** (Green = complete, Amber = partial, Red = empty, Grey = unknown).
- The **label** (e.g. "STING Filters").
- The **count** ("28/28 · 100%").

This strip refreshes every time you run an operation or hit F5.

---

## 6. The suggestion strip (★ Suggestions)

A small panel just above the selected operation. The dashboard reads your readiness lights + drift + project info (Healthcare flag, worksharing state) and recommends the top 3 things you should probably do next.

Each suggestion has:

- A **coloured dot** — Red dot = critical (most ops will fail without it), Amber = important, Green = informational.
- A **title** like "Bind STING shared parameters".
- A **detail line** explaining why.
- An optional **→ Open** button that takes you straight to that operation's detail pane.

**Examples of suggestions you'll see in practice:**

| Trigger | Suggestion |
|---|---|
| Project has no STING parameters bound | "Bind STING shared parameters" (Critical) |
| You have templates but only 5 filters | "Create STING view filters" (Warning) |
| 47 views have no template assigned | "Auto-assign templates to views" (Info) |
| Healthcare profile detected | "Apply Healthcare profile" (Info) |
| Templates have drifted from baseline | "Sync VG overrides" (Warning) |
| Project not workshared | "Enable worksharing for worksets" (Info) |

If you don't see any suggestions, your project is in good shape.

---

## 7. The navigation sidebar

The left column lists every operation, grouped into 7 categories. Each operation row shows:

```
●  Operation Title                    28/28
↑    ↑                                  ↑
status RAG dot                          done/total counter
```

The **RAG dot** tells you state at a glance:
- 🟢 **Green** — fully done (e.g. all 28 filters present)
- 🟡 **Amber** — partially done (e.g. 12/28)
- 🔴 **Red** — not done (0/something)
- ⚫ **Grey** — not applicable or status unknown

**Search box** at the top: type any word and the tree filters to matching operations across all groups. **Ctrl+F** jumps focus there.

**The 7 groups:**

1. **SETUP** — foundation steps (parameters, filters, worksets, line patterns, phases, Master Setup)
2. **TEMPLATES** — view template management
3. **STYLES** — ISO standard style creation
4. **SCHEDULES & DATA** — schedule + family parameter tools
5. **AUTOMATION** — wizards, audits, and governance ops
6. **CORPORATE LIBRARY** — pull/push the firm's baseline
7. **CROSS-ENGINE** — browse the wider STING catalogues (AEC Filters, Drawing Types, View Style Packs)

The order matters: SETUP first, AUTOMATION last. If you click them top-to-bottom you'll set up your project in the right sequence.

**★ Highlighted items** (Master Setup, Template Setup Wizard, Project Setup Wizard) are marked in green and bold — they're the all-in-one shortcuts.

---

## 8. The detail pane (centre)

When you click any operation in the sidebar, the centre pane fills with this:

```
[Suggestion strip]               ← shown when there are recommendations
[Op title + Pills]               ← "Destructive" / "Read-only" badges
[Description]                    ← what the op does in plain English
[Current state badge]            ← e.g. "● 12/28 — STING Filters partial"
[Requires block]                 ← prerequisite ops with dependency dots
[Preview grid]                   ← the checkbox table (see §10)
[Last result panel]              ← inline output from the last run (see §11)
[Action bar with ▶ Run button]   ← see §12
```

### The pills

- **🔴 Destructive** — this operation modifies/overwrites things in the project. Snapshot before running if you're not sure.
- **🟢 Read-only** — purely informational, can't break anything. Safe to run any time.

### The "Requires" block

If the op needs other ops to have been run first, you'll see a panel like:

```
Requires
●  Create Filters          (28/28)
●  Create View Templates   (23/23)
```

When all dependency dots are green you're good. When any are red, the **Run** button shows the warning "⚠ Run dependencies first" and you can either:
- Click the dependency in the sidebar and run it first, or
- Run **Master Setup** which orders everything correctly.

---

## 9. The operation catalogue — what each button does

This is the full inventory. Group-by-group.

### 9.1 SETUP group

#### Create Shared Parameters

**What it does:** Binds STING's catalogue of shared parameters (over 2,500 of them — tag tokens, BIM metadata, cost fields, healthcare params, etc.) to the project. Two-pass binding from `MR_PARAMETERS.txt`.

**When to run it:** First thing on any new project. Most other operations will skip work or fail without these parameters bound.

**Safe to re-run?** Yes — only adds new bindings, never removes anything.

**Watch out for:** Adds many parameters. Use a Revit Project Standards filter if you only want a subset.

#### Create Filters

**What it does:** Creates 28 multi-category view filters that group elements by discipline (Mechanical / Electrical / Plumbing / etc.), by status (Existing / Demolition / New Work), and for QA (Untagged / Stale Elements / Tag Style Drift).

**When to run it:** Right after Create Shared Parameters. Filters reference parameters, so the parameters must exist first.

**Safe to re-run?** Yes — skips existing filters.

**Look at the preview grid first** — you'll see all 28 filter names with their Exists column ticked for ones already in the project. Use the discipline picker to create only a subset.

#### Create Worksets

**What it does:** Creates 35 ISO 19650-compliant worksets covering every common discipline split + status worksets (Existing, Demolition, Temporary) + reference + annotation + coordination worksets.

**When to run it:** Only works if your project is workshared. Otherwise the preview shows "Project is not workshared — enable worksharing first."

**Safe to re-run?** Yes — skips existing worksets.

**The 35 worksets** include groupings like:
- STING - 00 Shared Levels & Grids
- STING - 01 Architecture - Walls / Floors / Roofs / Ceilings / Doors+Windows / Stairs / Interior
- STING - 02 Structure - Columns / Framing / Foundation / Rebar
- STING - 03 Mech / 04 Elec / 05 Plumb / 06 Fire / 07 LV
- STING - 90 Existing / 91 Demolition / 92 Temporary
- STING - 95 References / 96 Annotation / 99 Coordination

#### Create Line Patterns

**What it does:** Creates 10 ISO 128-2:2020 line patterns (Dashed / Dotted / Dash Dot / Dash Dot Dot / Long Dash / Center / Hidden / Phase Boundary / Fire Compartment / Setout).

**When to run it:** Before any style operation that references these patterns by name (Line Styles, View Templates, Drawing Types).

**Safe to re-run?** Yes.

#### Create Phases

**What it does:** Read-only inspection. Lists every Revit phase in the project with a row per phase.

**When to run it:** When you want to quickly check what phases are defined without leaving the dashboard.

**Safe to re-run?** Always — it doesn't modify anything.

#### ★ Master Setup

**What it does:** Runs all the SETUP + STYLES + TEMPLATES ops in the correct sequence. Roughly 20 steps. Includes:
- Load shared parameters
- Create BLE / MEP materials
- Create wall/floor/ceiling/roof/duct/pipe types
- Create schedules
- Create filters / worksets / line patterns
- Create view templates
- Apply filters to templates
- Configure VG overrides
- (When Healthcare profile detected) loads the Healthcare overlay too

**When to run it:** Once, on a brand-new project. After you've decided which template profile to use (default vs Healthcare).

**Safe to re-run?** Yes — every step uses skip-if-exists semantics. But it takes a few minutes.

**Cancellation:** Press Escape between steps to stop. The dashboard offers "Keep partial results or rollback" when an interruption is detected.

---

### 9.2 TEMPLATES group

#### Create View Templates

**What it does:** Creates 23 STING discipline view templates (Architectural Plan, Mechanical Plan, Coordination 3D, Healthcare Clinical, etc.) with proper detail level + scale + filter bindings + VG overrides.

**When to run it:** After Create Filters. Templates reference filters by name, so filters must exist first.

**Safe to re-run?** Yes — skips existing templates.

**The 23 templates** cover: Architectural Plan / Ceiling RCP / Working Section / Working Elevation / Coordination 3D / Mechanical Plan / Electrical Plan / Lighting RCP / Plumbing Plan / Structural Plan / Fire Protection Plan / Low Voltage Plan / MEP Coordination / Combined Services / Demolition Plan / As-Built Plan / Detail Section / Presentation Section / Presentation 3D / Presentation Elevation / Area Plan / Engineering Plan / Handover Plan.

#### Auto-Assign Templates

**What it does:** Walks every view in the project that doesn't have a template assigned, and uses a 5-layer matching algorithm to guess which STING template to apply:

1. **Name pattern** — view name contains "Mechanical" → STING - Mechanical Plan
2. **Level keyword** — level called "Basement" → STING - Structural Plan
3. **Phase keyword** — view phase "Demolition" → STING - Demolition Plan
4. **Scope-box discipline** — scope-box name contains "MEP-E" → STING - Electrical Plan
5. **View-type default** — any plain plan view → STING - Architectural Plan

**When to run it:** After Create View Templates. The preview grid shows you every unassigned view + what template Auto-Assign *would* set, so you can review before committing.

**Safe to re-run?** Yes — only touches views that don't already have a template (unless you override that in the Action column).

#### Clone Template

**What it does:** Deep-clones a STING template — copies all filter bindings, VG overrides, and per-category settings into a new template you can then customise. Optionally re-configures VG for a different discipline.

**When to run it:** When you need a project-specific variant of a standard template (e.g. "STING - Mechanical Plan - Phase 2").

**Safe to re-run?** Each run creates one new template.

#### Apply Filters to Templates

**What it does:** Walks every STING view template and attaches any STING filters that aren't already on it. Doesn't change templates that already have all filters.

**When to run it:** After you've added new filters mid-project, to back-populate them onto existing templates.

**Safe to re-run?** Yes — idempotent (only adds missing filters).

#### Sync VG Overrides 🔴 **Destructive**

**What it does:** Re-applies the corporate VG override recipes onto every STING template. This restores discipline colours / line weights / patterns after someone has hand-edited a template and broken the standard look.

**When to run it:** When the Drift Scan tells you there's drift, or after the corporate library bumps to a new version.

**Safe to re-run?** Yes, but it WILL overwrite manual VG edits. Use **Snapshot Capture** first if you're not sure.

**Locked templates skipped:** Templates with `STING_TEMPLATE_LOCKED_BOOL = 1` are skipped automatically (see §15 — Lockdown).

#### Auto-Fix Templates 🔴 **Destructive**

**What it does:** One-click health repair: missing filters re-applied, wrong detail level reset, broken filter references removed, scale put back into the standard range.

**When to run it:** When Template Audit reports many small issues you'd rather not fix one at a time.

**Safe to re-run?** Yes, but it changes the model. Snapshot first.

#### Batch VG Reset 🔴 **Destructive**

**What it does:** Resets VG settings across multiple views back to their template defaults. Useful after manual VG mess.

**When to run it:** When you have a batch of views where someone overrode VG by hand and you want them all back to template.

**Safe to re-run?** Yes — but you're throwing away view-level VG customisations. Snapshot first.

#### Template Audit 🟢 **Read-only**

**What it does:** Deep inspection report. Counts STING templates / project templates / orphaned filters / views without templates / discipline distribution. Samples compliance score across views. Lists templates missing filters and views without templates.

**When to run it:** Any time. Great as a project health check before a deliverable.

**Safe to re-run?** Always — read-only.

#### Template Diff 🟢 **Read-only**

**What it does:** Compares VG overrides of two STING view templates side-by-side. Shows filter-by-filter differences in visibility, colours, line weights, halftone, transparency, fill patterns.

**When to run it:** When two templates should look the same but don't, and you need to find where they diverge.

**Safe to re-run?** Always — read-only.

#### Compliance Score 🟢 **Read-only**

**What it does:** Scores every scorable view in the project against 10 weighted criteria (HasTemplate / IsStingTemplate / HasFilters / FilterOverrides / DetailLevel / CorrectDiscipline / PhaseCorrect / VGConsistent / NoOrphans / ScaleAppropriate). Reports per-view + project average + Green/Amber/Red distribution.

**Different weight profiles** apply depending on the project type — Healthcare projects get heavier weighting on filter overrides + discipline match; presentation views get more weight on visual quality, etc. The profile is picked automatically by reading `PRJ_ORG_HEALTH_PACK_PROFILE_TXT` or `PRJ_TEMPLATE_PROFILE_TXT` on Project Info.

**When to run it:** Before client review. Before issuing a CDE drop.

**Safe to re-run?** Always — read-only. Per-view scores are cached for 30 seconds.

---

### 9.3 STYLES group

All six STYLES ops follow the same pattern: a preview grid lists every style that would be created, with an Exists column ticked for any already in the project. You can deselect rows, filter by discipline, hide existing entries, then click Run to create the rest.

#### Fill Patterns

12 ISO 128-2:2020 fill patterns (Crosshatch / Diagonal Up / Diagonal Down / Diagonal Cross / Horizontal / Vertical) plus model patterns for materials (Brick / Tile / Insulation / Sand / Earth / Concrete).

**Safe to re-run?** Yes — skips existing.

#### Line Styles

16 ISO line styles for disciplines (Mechanical / Electrical / Plumbing / Architectural / Structural / Fire / LV) plus status styles (Existing / Demolition / New Work / Temporary) plus reference styles (Centerline / Hidden / Boundary / Fire Boundary / Setout).

**Source badge:** "CSV" when read from project CSV overlay, "hardcoded" when using built-in defaults. Surfaced in the Source column so you know which path is feeding the data.

#### Object Styles

40+ ISO category line weights + colours. Applies discipline-coloured edges to Walls, Doors, Windows, Ducts, Pipes, Conduits, Cable Trays, Electrical Equipment, Plumbing Fixtures, Sprinklers, Fire Alarm Devices, etc.

#### Text Styles

12 ISO 3098 / BS 8541 text note types (Title Large / Medium / Small / Body / Annotation / Note / Tag Text / Room Name / Room Number / Sheet Title / Sheet Number / Key Note) in sizes from 1.5mm to 5.0mm.

#### Dimension Styles

7 dimension types (Linear mm / Linear m / Angular / Ordinate / String / Detail / Structural).

#### VG Overrides

The "intelligence layer". Applies 6 layers of VG overrides:
1. Discipline colour coding (10 colours)
2. QA highlighting (red = missing, orange = incomplete)
3. Status styling (halftone existing, crosshatch demolished)
4. Phase-aware overrides
5. Workset visibility
6. CSV-driven VG schemes

The preview shows the discipline schemes (Mechanical / Electrical / Plumbing / Architectural / Structural / Fire); pick which to apply.

---

### 9.4 SCHEDULES & DATA group

#### Create Template Schedules

Standard schedule templates from `MR_SCHEDULES.csv`. Creates the schedules with proper sorting / grouping / filtering / totals + field remapping for renamed parameters.

#### Material Schedules

8 material takeoff schedules (one per discipline category). Quick takeoff for cost / carbon / quantity rollups.

#### Cable Trays

Creates cable tray types from `MEP_MATERIALS.csv`. Sizes + materials + finishes.

#### Conduits

Same as Cable Trays but for conduits.

#### Batch Family Parameters

Adds STING shared parameters to *loaded family documents* in batch. Useful for projects with many loaded families that need parameter binding.

#### Family Parameter Processor

Batch processor for `.rfa` files on disk. Opens each family, injects shared parameters, saves, closes. Used for vendor family libraries before they're loaded.

---

### 9.5 AUTOMATION group

#### ★ Template Setup Wizard

15-step complete automation pipeline. Smaller than Master Setup — skips materials, focuses on templates + styles + filters.

#### ★ Project Setup Wizard

7-page comprehensive WPF wizard. Asks you about project type, disciplines, healthcare flag, presentation needs, and corporate baselines — then runs a tailored pipeline.

#### Validate Template 🟢 **Read-only**

45 validation checks per template. Reports template integrity issues that the audit might miss.

#### Dynamic Bindings

Loads parameter bindings from `BINDING_COVERAGE_MATRIX.csv` — useful when adding bindings to many categories at once.

#### Schema Validate 🟢 **Read-only**

Validates CSV columns in your project's data files against `MATERIAL_SCHEMA.json`. Catches column-rename / column-drop drift before it causes silent failures elsewhere.

#### Template VG Audit 🟢 **Read-only**

Visual Graphics override analysis. Reports per-template VG override counts + identifies templates with non-standard VG settings.

#### Drift Scan 🟢 **Read-only** *(Template Manager v2)*

Walks every STING template and computes a SHA-256 checksum of its current VG state. Compares to the last stamped checksum and reports any drift. Three kinds of drift:

| Kind | Meaning |
|---|---|
| **Missing** | Template has never been stamped — no baseline to compare against |
| **FilterOverride** | The template's VG overrides have changed since the last stamp |
| **Orphan** | The template references a filter that no longer exists in the project |

Run this regularly (weekly is a good cadence) to catch unauthorised template edits.

#### Drift Stamp *(Template Manager v2)*

Writes the current checksum onto every STING template (stored in `STING_TEMPLATE_CHECKSUM_TXT`). Use after Sync VG Overrides or after intentionally editing templates so the next Drift Scan compares against the new baseline.

#### Snapshot Capture *(Template Manager v2)*

Manually captures a state snapshot (templates + their VG state + filters + filter category bindings) into `<project>/_BIM_COORD/snapshots/<timestamp>/state.json`. Use before any destructive op as belt-and-braces beside Revit's own undo.

The dashboard lists previous snapshots so you can browse them, and they form the basis for future "restore from snapshot" commands.

#### Audit Verify 🟢 **Read-only** *(Template Manager v2)*

Recomputes the SHA-256 hash chain of the latest audit log file. Reports either "Audit log chain verified" (green) or "Audit log chain BROKEN — possible tampering" (red). Use for QA / client defence to prove no one has edited the log file after the fact.

---

### 9.6 CORPORATE LIBRARY group *(Template Manager v2)*

The Corporate Library is the firm-wide baseline of template assignment rules, weight profiles, filter definitions, and other JSON files. Stored on a network share, a local seed folder, or wherever the firm wants. Once configured, every project can pull the latest baseline + push project-specific tweaks back to be promoted to the corporate standard.

#### Pull from corporate library

Copies every `*.json` file from the configured library path into the project's `_BIM_COORD/` folder. Acts as an additive overlay — your project-specific JSON edits get merged on top of corporate defaults at runtime.

**When to run it:** When the corporate library publishes a new version + you want this project to pick up the changes. Or on day one of a new project.

#### Push to corporate library 🔴 **Destructive** *(to the library, not your project)*

Pushes the project's `_BIM_COORD/*.json` files back to the corporate library path. Before overwriting, it creates a timestamped backup in `<library>/_backups/<ts>/` so the previous corporate version isn't lost.

**When to run it:** When this project has produced a template tweak the rest of the firm should pick up. **Make sure you have permission** to publish to the library before clicking.

#### Configure library 🟢 **Read-only**

Shows the current library configuration:
- **Path** — where the library lives (or "(none)" if unset).
- **Channel** — stable / beta / dev.
- **Stamped version** — what version this project is on (`PRJ_CORPORATE_LIBRARY_VERSION_TXT`).
- **Last synced** — when this project last pulled.

To **set** the library path, edit `%APPDATA%/STING/corporate_library.json` (the global config) or set `PRJ_CORPORATE_LIBRARY_PATH_TXT` on Project Information for a per-project override.

---

### 9.7 CROSS-ENGINE group *(Template Manager v2)*

Three browsers that surface other STING engines as first-class data inside the Template Manager. Read-only — you can browse but not run from here. Each opens a preview grid you can filter, sort, and inspect.

#### AEC Filter Library (289) 🟢 **Read-only**

Browses the AEC Filter Library — 289 corporate-baseline `ParameterFilterElement` definitions covering: 47 Arch / 33 HVAC / 31 Struct / 30 Fire / 27 Elec / 18 Plumb / 11 FM/COBie / 8 ISO 19650 / 8 Coord/LOD / 5 VT / 5 QA / + 24 Healthcare additions / + 47 misc.

Each row shows the filter name, category (the filter's primary tag), discipline, exists-in-project flag, and origin (corporate vs project-overlay).

**Why it matters:** the legacy `Create Filters` op only creates 28 hardcoded filters. The AEC Filter Library is the proper home for the rest. Long-term, `Create Filters` will migrate to use this registry.

#### Drawing Types catalogue (90) 🟢 **Read-only**

Browses the Drawing Type catalogue — 90 corporate drawing types (architectural plan A1 1:100 / mechanical plan A1 1:100 / healthcare clinical A1 1:50 / pipe spool A1 1:50 / etc.).

Each row shows the Drawing Type id, name, paper size, scale, discipline, purpose, and origin. Used by the Drawing Template Manager subsystem.

#### View Style Packs (22) 🟢 **Read-only**

Browses the View Style Packs — 22 corporate VG style packs that share VG / filter / text / dim style definitions across many Drawing Types. Each pack has a `templateMode` (managed = STING owns it / external = user owns it).

---

## 10. The preview grid (every op that supports preview)

Most operations show a checkbox `DataGrid` after their description. This is the cleanest place to control exactly what the op will do.

### Columns

| Column | Editable? | Meaning |
|---|---|---|
| **☑** (checkbox) | Yes | Tick to include this row in the next Run. Untick to skip. |
| **Name** | No | What's about to be created/applied (e.g. "STING - Mechanical Plan"). |
| **Discipline** | No | The discipline tag (M / E / P / A / S / FP / LV / G / QA / *). |
| **Category** | No | The type/family/category this row applies to. |
| **Exists** | No | True if this item is already in the project — clicking Run will skip these rows (unless you change the Action). |
| **Action** | Yes (dropdown) | Per-row override: Skip / Create / Overwrite / Merge / Rename. |
| **Source** | No | Where the row came from: `hardcoded` / `CSV` / `corp-library` / `project-overlay` / `drift` / `snapshot` / `config`. |

### Toolbar (above the grid)

| Button | What it does |
|---|---|
| **Select All** | Ticks every row's checkbox. |
| **Select None** | Unticks every row. |
| **Hide Existing** / **Show All** | Toggles whether already-existing rows are visible. |
| **Discipline ▼** dropdown | Only ticks rows matching the chosen discipline (or "All"). Visible only when the op has multiple disciplines. |

The toolbar also shows live counts like **"22 of 28 selected · 22 new, 6 existing"** so you know at all times what Run will actually do.

---

## 11. The result panel (after a Run)

After clicking Run, an inline result panel appears just above the Run button. It replaces the old TaskDialog popups so you can see all results without leaving the dashboard.

The panel has a coloured header (Green = Success, Amber = Warning, Red = Error, Blue = Info), a one-line headline like **"Created 28 fill patterns · Skipped 0 · Total defined 12"**, and then one or more **sections**, each with:

- A title (e.g. "Drift entries", "Pulled files", "By Discipline").
- **Metric tiles** — pill-shaped values (e.g. "Path: \\\\corp-share\\sting\\v3.4").
- An **inline DataGrid** showing per-row outcomes (Name / Status / Discipline / Detail). For drift scans the grid shows each drifted template; for batch operations it shows each created/skipped/failed item.

The result also gets logged to:
1. The right-hand **session log** (shown for the rest of the session).
2. The **audit log file** at `<project>/_BIM_COORD/template_audit_log_<month>.jsonl` (permanent, SHA-256-chained).
3. The **Planscape Server** (if you're logged in) — visible in the server's web dashboard for cross-project rollup.

---

## 12. The Run button

At the bottom-right of every operation pane. Three things to notice:

1. **The label** shows the number of selected items: `▶  Run (22)`. If the operation doesn't have a preview, it just says `▶  Run`.
2. **The colour** is green for highlighted (★) ops, accent colour for everything else.
3. **The state** — greyed out when:
   - There's no document open, or
   - The op's dependencies aren't met (you'll see a "⚠ Run dependencies first" warning beside the button).

After you click Run, the dialog closes momentarily to dispatch the command through Revit's external-event mechanism, then re-opens with the result panel populated. This is normal — it's how Revit's API requires modal commands to run.

---

## 13. The session log (right pane)

The right column shows every operation you've run in the current session, newest first. Each row has:

- A coloured dot matching the result severity.
- The local timestamp + operation label.
- The headline from the result.

**Click any row** to jump back to that operation's detail pane with the result re-rendered. Useful for reviewing what an audit said 10 minutes ago without re-running it.

The session log shows roughly the last 50 results. To collapse the log pane, drag its splitter all the way to the right.

---

## 14. The footer status bar

| Field | What it shows |
|---|---|
| Left side | Status text (e.g. "Selected: Auto-Assign Templates", "Readiness refreshed.", "Theme: Corporate") |
| Right side | Last-run summary (operation name + duration + counts) |
| **Close** button | Closes the dashboard. Same as Escape. |

---

## 15. Lockdown — protecting templates from destructive ops

Every STING view template has a hidden boolean parameter called `STING_TEMPLATE_LOCKED_BOOL`. When it's set to **1 (true)**:

- **Sync VG Overrides** skips this template
- **Auto-Fix Templates** skips this template
- **Batch VG Reset** skips this template

To **lock** a template, just set the parameter to 1 in the template's properties. The simplest path is via a parameter-editing window — or any of the bulk parameter set commands STING ships.

To **unlock**, set it back to 0.

You'll typically lock:
- Templates you've heavily customised for a specific deliverable.
- Templates that have to match a client's standard exactly.
- Templates used by automated batch-printing pipelines where any unintended change would break the output.

---

## 16. Snapshots — undo for the things Revit can't undo

The Snapshot Capture op writes a JSON file with everything the Template Manager cares about:

- Every view template's name + scale + detail level + filter list + per-category VG overrides
- Every filter's id + name + category bindings

These land in `<project>/_BIM_COORD/snapshots/<yyyyMMdd_HHmmss>/state.json`.

**Strongly recommended:** click **Snapshot Capture** *before* running any destructive op. It takes about 1 second and gives you a paper trail of what the project looked like before you changed anything.

Snapshots are surfaced in the Snapshot Capture operation's preview grid — you can browse the list, see when each was taken, and (in future versions) restore from one.

---

## 17. Drift detection — catching unauthorised edits

The Template Manager stamps a SHA-256 checksum onto every STING template (in the `STING_TEMPLATE_CHECKSUM_TXT` parameter) when you run **Drift Stamp**. The next **Drift Scan** recomputes the checksum and reports any mismatch.

**Why it matters:** in big teams, someone will inevitably hand-edit a STING template. That breaks the standard look. Drift Scan finds those edits before the next audit / client review surfaces them embarrassingly.

**Workflow:**
1. Run **Drift Stamp** after the project's templates are in their "good" state (typically right after Master Setup, or after Sync VG Overrides).
2. Run **Drift Scan** weekly (or on demand) to catch edits.
3. When drift is found, you can either:
   - **Accept** the new state — run **Drift Stamp** again to make it the new baseline.
   - **Reject** — run **Sync VG Overrides** to restore the standard, then **Drift Stamp** again.

---

## 18. The audit log

Every operation publishes one entry to a JSONL file at `<project>/_BIM_COORD/template_audit_log_<yyyy>_<MM>.jsonl`. Each entry has:

- UTC timestamp + local user name
- Operation tag + label
- Severity + headline
- Duration in milliseconds
- Document path + title
- Created / skipped / failed counts
- The previous entry's SHA-256 hash + this entry's SHA-256 hash

Each entry's hash is computed over (previous-hash + entry-body), so the log forms a **tamper-evident chain**. If someone edits a past entry, every subsequent entry's hash becomes invalid.

**Audit Verify** (under AUTOMATION) recomputes the entire chain and reports either "verified" or "BROKEN — possible tampering". For QA defence / client handover you can include the audit log file as proof of what was done when, and run Audit Verify in front of the client to demonstrate integrity.

---

## 19. Recipes (the JSON in `_BIM_COORD/recipes/`)

A **recipe** is a named sequence of operations stored as a JSON file. The Template Manager ships 5 built-in recipes:

| Recipe | What it does |
|---|---|
| **Setup (lite)** | Parameters → Filters → View Templates (3 steps) |
| **★ Master Setup** | The full ~20-step pipeline |
| **Style refresh** | Re-apply all STING styles without touching templates |
| **Audit (read-only)** | Run every audit / inspection op; safe on locked projects |
| **Healthcare project setup** | Master Setup with Healthcare profile auto-selected |

You can also create **project-specific recipes** by dropping a JSON file into `<project>/_BIM_COORD/recipes/`. Each step references an op tag from the operation catalogue. Example recipe JSON:

```json
{
  "id": "my-project-bootstrap",
  "name": "My Project Bootstrap",
  "description": "Standard bootstrap for type-X projects",
  "steps": [
    { "opTag": "CreateParameters", "stopOnFailure": true },
    { "opTag": "CreateFilters" },
    { "opTag": "ViewTemplates" },
    { "opTag": "CreateFillPatterns", "skipIfDone": true },
    { "opTag": "ApplyFilters" }
  ]
}
```

Each step supports:
- `skipIfDone` — skip if the op's RAG dot is already Green
- `stopOnFailure` — abort the recipe on failure (else continue)
- `options` — bag of key/value strings passed through to the op

---

## 20. Keyboard shortcuts

| Key | Action |
|---|---|
| **F5** | Refresh readiness lights + per-op badges + suggestions |
| **Esc** | Close the dashboard |
| **Ctrl+F** | Focus the search box in the nav sidebar |

---

## 21. Files the Template Manager writes

Everything lives under either the active project's `_BIM_COORD/` folder or `%APPDATA%/STING/`.

| Path | What it is | When written |
|---|---|---|
| `<project>/_BIM_COORD/template_assignment_rules.json` | Project overlay of assignment rules (overrides corporate baseline by rule id) | Hand-edited |
| `<project>/_BIM_COORD/template_audit_log_<yyyy>_<MM>.jsonl` | SHA-256-chained audit log, one file per month | Every op |
| `<project>/_BIM_COORD/snapshots/<ts>/state.json` | State snapshot before destructive op | Snapshot Capture |
| `<project>/_BIM_COORD/recipes/*.json` | Project-authored recipes | Hand-edited |
| `<project>/_BIM_COORD/aec_filters.json` | Project overlay for AEC filter library | Hand-edited |
| `%APPDATA%/STING/corporate_library.json` | Global config: library path + channel | Configure Library |

You can safely delete any of these — the Template Manager re-creates / starts fresh on the next run.

---

## 22. Shared parameters the Template Manager creates

Five Template Manager v2 parameters land in `MR_PARAMETERS.txt` (auto-bound on first Create Shared Parameters run):

| Parameter | Group | Type | Used by |
|---|---|---|---|
| `STING_TEMPLATE_CHECKSUM_TXT` | TPL_TRACKING | Text | Drift Detector — baseline checksum |
| `STING_TEMPLATE_LOCKED_BOOL` | TPL_TRACKING | Yes/No | Lockdown — set to 1 to protect a template |
| `PRJ_CORPORATE_LIBRARY_PATH_TXT` | PRJ_INFORMATION | Text | Per-project override of library path |
| `PRJ_CORPORATE_LIBRARY_VERSION_TXT` | PRJ_INFORMATION | Text | Last pulled library version |
| `PRJ_TEMPLATE_PROFILE_TXT` | PRJ_INFORMATION | Text | Compliance weight profile (default / working / coordination / presentation / handover / healthcare) |

You'll only ever touch the Boolean (Lockdown) and possibly the project profile string. The rest are auto-stamped.

---

## 23. A typical day-one workflow

For a brand-new Revit project, the canonical sequence is:

1. Open the project.
2. Open the Template Manager.
3. Look at the readiness lights — they'll all be Red.
4. Look at the Suggestion strip — top suggestion will likely be **Bind STING shared parameters**.
5. Click **★ Master Setup** in the SETUP group.
6. Confirm the action.
7. Watch the progress; results render inline.
8. (Optional) Click **Auto-Assign Templates** to bind templates to existing views.
9. (Optional) Click **Drift Stamp** so the dashboard has a baseline to compare against later.
10. Close.

Total time: 2-5 minutes depending on project size.

---

## 24. A typical "weekly health check" workflow

For an ongoing project where you want to catch problems early:

1. Open the Template Manager.
2. Run **Drift Scan** — catches unauthorised template edits.
3. Run **Template Audit** — overall health report.
4. Run **Compliance Score** — per-view weighted scoring.
5. Run **Schema Validate** — catches CSV column drift.
6. If any showed issues, decide whether to **Auto-Fix Templates** (one-click) or address them individually.
7. Optionally **Snapshot Capture** before applying any fixes.
8. Run **Audit Verify** at the end to confirm the audit log hasn't been tampered with.

---

## 25. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| All readiness lights red | Brand-new project, never touched by STING | Run Master Setup |
| Run button greyed out | No document open, or dependencies unmet | Open a doc; or run dependencies first (see "Requires" block) |
| Worksets light says "Not workshared" | Project isn't workshared | Enable worksharing in Revit's collaboration settings |
| Drift Scan returns "Missing" for every template | Templates have never been stamped | Run Drift Stamp once |
| Audit Verify says "BROKEN" | Audit log file has been hand-edited | Investigate; restore from backup if possible |
| "Filter not in document" warning | A template references a filter that doesn't exist | Run **Create Filters** or **Apply Filters to Templates** |
| Preview grid is empty for some op | Op doesn't yet have a preview provider (a few don't) | Use Run directly — result still renders inline |
| Suggestion strip empty | Project is in good shape | Nothing to do! |
| Server publish says "skipped" in the log | Not logged into Planscape Server, or server route unavailable on this build | Optional — purely a server-side dashboard feature |
| Theme button cycles but nothing changes | Some dialogs cache brushes | Close + re-open the dashboard |

---

## 26. Glossary

| Term | Meaning |
|---|---|
| **Op** | A single Template Manager operation (e.g. Create Filters). |
| **RAG** | Red / Amber / Green status indicator. |
| **Drift** | A STING template that has been edited away from its corporate baseline. |
| **Profile** | A named set of weights for compliance scoring (default / working / coordination / presentation / handover / healthcare). |
| **Recipe** | A user-defined sequence of ops, stored as JSON. |
| **Snapshot** | A JSON file capturing template + filter state at a point in time. |
| **Corporate library** | A network share (or local folder) containing the firm's baseline JSON files. |
| **Audit log** | The SHA-256-chained JSONL file at `_BIM_COORD/template_audit_log_<month>.jsonl`. |
| **Readiness light** | One of the five tiles at the top of the detail pane showing project setup completeness. |
| **Preview grid** | The checkbox DataGrid that lets you choose what an op will do before Run. |
| **Action column** | The per-row dropdown (Skip / Create / Overwrite / Merge / Rename) in the preview grid. |
| **Lockdown** | The `STING_TEMPLATE_LOCKED_BOOL` flag that protects a template from destructive ops. |
| **VG** | Visibility/Graphics — Revit's per-category override system. |
| **CDE** | Common Data Environment (ISO 19650). |

---

*End of guide. For per-operation source code, see `Core/TemplateManager/` and `Temp/TemplateManagerCommands.cs`. For the design narrative, see [`docs/CHANGELOG.md`](../../docs/CHANGELOG.md) under "Completed (Template Manager v2 — Phases 1-20)".*
