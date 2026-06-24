# STING MEP Systems — A Layman's Guide
*Why it exists · What it is · How to use it*

> Audience: BIM coordinators and MEP engineers who want consistent, standards‑aligned
> MEP systems, drawings, and classification without the manual setup. No coding required —
> you press buttons and edit one data file.

---

## Contents
1. [Why this exists](#1-why-this-exists)
2. [What it is — the ideas in plain English](#2-what-it-is--the-ideas-in-plain-english)
3. [How to use it — step by step](#3-how-to-use-it--step-by-step)
4. [The one‑click way](#4-the-one-click-way--validation-workflow)
5. [How to customise it](#5-how-to-customise-it)
6. [Worked example — blank model → issued sheet set](#6-worked-example--blank-model--issued-sheet-set)
7. [Glossary](#7-glossary)
8. [Troubleshooting](#8-troubleshooting)
9. [Reference — every button](#9-reference--every-button)

---

## 1. Why this exists

In Revit, an **MEP system** is a connected network of services — the supply‑air ductwork
fed from an air handling unit, a chilled‑water flow‑and‑return loop, a soil‑and‑waste
drainage stack, an electrical circuit, and so on. Revit *can* track these, but on a real
project the setup is slow, manual, and inconsistent:

- Someone creates each **system type** by hand — its name, colour, line style, abbreviation,
  classification — and every engineer does it slightly differently.
- The actual pipe/duct networks get **generic auto‑names** like "Mechanical Supply Air 3" —
  meaningless on a drawing.
- Making a coordination drawing **colour‑code by system** means hand‑building view filters
  every time.
- **Classification** (Uniclass/CSI for the spec and bill of quantities) and **keynotes** are
  filled in separately, by hand, often forgotten.

The result: inconsistent drawings between disciplines and projects, BOQs that don't reconcile,
and hours of repetitive setup.

**STING MEP Systems automates that whole chain from one data file.** You press a few buttons
(or run one workflow) and every project comes out the same way — standards‑aligned colours,
names, tags, classification, and coordinated drawings.

---

## 2. What it is — the ideas in plain English

### The two layers: "type" vs "instance"
This trips everyone up, so an analogy:

- A **system type** is a *recipe*: "Supply Air = blue, abbreviation `SA`, classification
  SupplyAir." There is one recipe per kind of system.
- A **system instance** is the *actual dish*: the specific run of supply ductwork on Level 2,
  named `SA‑01`, that follows the recipe.

STING builds the recipes first (types), then the actual systems (instances), then makes them
look right on drawings.

### One source of truth
Everything — colours, abbreviations, classification codes, line styles — comes from **one file**:
`Data/STING_MEP_SYSTEM_TYPES.json`. Change the file, and every project that runs STING gets the
change. No recompiling, no hunting through Revit dialogs. A project can override it locally
without touching the corporate file.

### What you get out of the box (23 systems)
Supply / return / extract / fresh / smoke air · LTHW / MTHW / CHW / condenser **flow + return** ·
domestic cold / hot water + circulation · soil · vent · rainwater · sprinkler wet / dry · gas ·
refrigerant — each with a sensible colour (per CIBSE / BS 1192 conventions), a short code, and a
Uniclass reference.

### The chain (how it all connects)
```
   one JSON file
        │
        ▼
 (1) system TYPES  ──►  (2) system INSTANCES  ──►  (3) COLOURED drawings
 (recipes, colours)      (named, tagged runs)        (view filters + sheets)
        │                       │                          │
        └───────────► one consistent colour + name + tag everywhere ◄────────┘
                                │
                                ▼
                    (4) classification + keynotes  (Uniclass / CSI → BOQ + tags)
```

---

## 3. How to use it — step by step

Everything lives in the **STING HVAC panel → `SYS` tab → "STING MEP SYSTEMS"** section (scroll
to the bottom of the tab). Buttons are grouped to follow the chain above; you normally run them
top to bottom.

### Step 1 — "Build types"  *(the big one)*
- **Does:** reads `STING_MEP_SYSTEM_TYPES.json` and creates/updates all 23 duct & pipe **system
  types** — each with colour, line weight, dash pattern, abbreviation, and classification.
- **Why:** this is the "batch‑create all MEP systems with properly configured properties" step.
  One click instead of an hour of manual setup.
- **Re‑run safe:** never duplicates; existing types are left untouched (use **Restyle** to force
  the corporate colours back).

### Step 2 — "Build systems"
- **Does:** walks the model's connector graph, finds each connected duct/pipe network, assigns
  the right STING type, names it meaningfully (`CHWF‑01`, `SA‑02`…), and stamps the STING tag
  tokens (DISC / SYS / FUNC) on every member.
- **Why:** turns Revit's anonymous networks into properly named, typed, taggable systems.
- **Tip:** select elements first to limit scope; otherwise it does the whole project.
  **"Force build"** additionally *creates* systems for orphan networks that have a valid source
  (AHU/pump) — use only when the network is clean.

### Step 3 — "Gen filters"
- **Does:** generates one view filter per system from the same colours and saves them into the
  project for reuse.
- **Why:** this is what lets drawings colour by system, and it distinguishes CHW vs LTHW vs CW
  (which classification alone can't).

### Step 4 — "Colour view"
- **Does:** colours the **currently open view** by system and applies the MEP coordination
  drawing template.
- **Why:** instant coordinated MEP plan — supply air blue, return green, CHW navy, LTHW orange —
  with no manual filter setup.

### Step 5 — "Per‑level + sheets"
- **Does:** creates one coordinated plan **per level × discipline** (M/E/P) and drops each on its
  own sheet, numbered and titled automatically.
- **Why:** turns a model into a sheet set in one click. Re‑run‑safe — it won't make duplicates.

### Electrical — "Build circuits" / "Auto‑group circuits"
- **Build circuits:** names + tags every existing circuit (`<Panel>‑<Circuit>`) so circuits
  behave like duct/pipe systems.
- **Auto‑group circuits:** a **first‑pass** that groups loose, un‑circuited devices onto their
  nearest panel. It does **no load balancing** — it's a starting point an engineer reviews in the
  panel schedules (the result panel says "⚠ REVIEW REQUIRED").

### Classification & keynotes
- **Set standard:** pick the classification scheme — **Uniclass 2015** (default; what KUT uses),
  CSI MasterFormat, OmniClass, or Native. This drives BOQ / COBie / handover grouping.
- **Assign keynotes:** fills each element's keynote from its classification; then `KeynoteSync`
  builds the keynote table — so CSI codes show on tags and reconcile to the spec.

---

## 4. The one‑click way — validation workflow

**STING → Run Workflow → "MEP Systems Validate"** runs the whole chain (types → systems →
filters → colour → circuits → views) in one pass, showing each step's result. Ideal for a first
run on a model, or to validate a build.

---

## 5. How to customise it

You almost never edit code — you edit **`STING_MEP_SYSTEM_TYPES.json`**:
- **Change a colour** → edit `lineColor: [r,g,b]`.
- **Add a system** → copy a block; change `name`, `abbreviation`, `classification`, colour.
- **Per‑project tweak** without touching the corporate file → drop a `mep_system_types.json` in
  the project's `_BIM_COORD` folder (it merges over the baseline; project wins).

Then press **Reload** (or restart Revit) and **Build types** again.

> **Rule:** abbreviations must be alphanumeric with **no internal dash** (`CHWF`, not `CHW-F`) —
> the system naming uses `-` as the separator, and the registry warns you if you break this.

---

## 6. Worked example — blank model → issued sheet set

1. Model your MEP (ducts from an AHU to diffusers; a CHW flow/return loop; some sockets + a panel).
2. **Build types** → 23 system types appear in *Manage → MEP Settings*.
3. **Build systems** → ducts/pipes get typed + named (`SA‑01`, `CHWF‑01`…) + tag tokens.
4. **Gen filters** → "STING – Sys: …" filters land in the project.
5. Open a Level 1 plan → **Colour view** → supply blue, return green, CHW navy, LTHW orange.
6. **Build circuits** (and **Auto‑group circuits** for loose devices) → circuits named + tagged.
7. **Per‑level + sheets** → one M/E/P coordinated plan per level, each on its own sheet.
8. **Set standard → Uniclass** then **Assign keynotes** → spec/BOQ classification + keynotes on tags.

Or just run **"MEP Systems Validate"** to do 2–8 in one pass.

---

## 7. Glossary
- **Classification** — the Revit `MEPSystemClassification` (SupplyAir, SupplyHydronic…) that drives
  behaviour and the colour filters.
- **Abbreviation** — the short code (`CHWF`) used to name systems and match filters.
- **SYS / FUNC tokens** — the ISO 19650 tag pieces (HVAC / CLG…) that flow into tags and schedules.
- **Uniclass / CSI** — external classification standards for spec & BOQ; you choose which leads.
- **Filter** — the view rule that paints a system its colour on a drawing.

---

## 8. Troubleshooting
- **Colours generic (all ducts blue)?** Run **Gen filters** then **Colour view** — or you applied
  the drawing template without the system filters.
- **CHW and LTHW the same colour?** You're on an old build — both are distinct now.
- **No buttons in the SYS tab?** You're running a stale DLL — rebuild from `main` and **fully**
  restart Revit (make sure `Revit.exe` is gone from Task Manager first).
- **Force‑build made nothing?** Orphan networks must have a valid source equipment + consistent
  flow direction; otherwise they're reported, not forced.

---

## 9. Reference — every button

| Group | Button | Command tag | What it does |
|---|---|---|---|
| System types (A) | Build types | `MEP_BuildSystemTypes` | Create/update the 23 system types (idempotent) |
| | Restyle | `MEP_RestyleSystemTypes` | Re‑apply baseline colours to existing types |
| | Inspect | `MEP_InspectSystemTypes` | Read‑only: present vs to‑build |
| | Reload | `MEP_ReloadSystemTypes` | Re‑read the JSON after an edit |
| Instances (B) | Build systems | `MEP_BuildSystems` | Type / name / stamp existing systems |
| | Force build | `MEP_BuildSystemsForce` | Also create orphan systems (review after) |
| Coordination (C/D) | Gen filters | `MEP_GenerateSystemFilters` | Generate + persist the system colour filters |
| | Colour view | `MEP_ApplyMepCoordination` | Colour the active view + apply the MEP template |
| | Inspect | `MEP_InspectMepCoordination` | Dry run of the system → filter resolution |
| Views & circuits (E/F) | Build circuits | `MEP_BuildCircuits` | Name/tag circuits; create from selection |
| | Auto‑group circuits | `MEP_AutoGroupCircuits` | First‑pass group loose devices to nearest panel |
| | Produce views | `MEP_ProduceMepViews` | Duplicate the active plan per discipline |
| | Per‑level + sheets | `MEP_ProduceMepViewsByLevel` | One plan per level × discipline, on sheets |
| Classification | Set standard | `Classification_SetStandard` | Choose Uniclass / CSI / OmniClass / Native |
| | Assign keynotes | `Keynote_Assign` | Fill keynotes from CSI / SYS tokens |

**Data files** (`StingTools/Data/`): `STING_MEP_SYSTEM_TYPES.json` (the recipes — *edit this*) ·
`STING_AEC_FILTERS.json` (filters) · `STING_VIEW_STYLE_PACKS.json` (`corp-coordination` pack) ·
`WORKFLOW_MEPSystemsValidate.json` (one‑click validation).

**Engines** (`StingTools/Core/Mep/`): `MepSystemTypeMaterializer`, `MepSystemInstanceBuilder`,
`MepCoordinationEngine`, `MepSystemFilterGenerator`, `MepViewProducer`, `MepLevelViewProducer`,
`MepCircuitBuilder` · classification in `StingTools/Core/Classification/`.
