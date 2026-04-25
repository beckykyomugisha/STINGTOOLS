# STING Drawing Type Manager — Complete Layman Guide

> **Audience:** Revit users, BIM coordinators, project managers and discipline leads who want to understand *how STING produces a quality drawing automatically* without having to set 40 view properties by hand on every sheet. No coding, no registry surgery — everything lives in two JSON files, an editor dialog and a small set of conventions.
>
> **Plain-language goal:** by the end of this guide you will be able to (a) name any drawing your project must produce, (b) tell STING which corporate recipe to use for it, and (c) hit one button and watch the engine create the sheet, drop the right viewports, apply the right look, dimension the grids, tag the rooms, stamp the title block, and post the deliverable to the CDE.
>
> **Prerequisites:** this guide assumes you have already read [`TITLE_BLOCK_CREATION_GUIDE.md`](TITLE_BLOCK_CREATION_GUIDE.md) — title blocks are *one input* to the drawing-type machine. Title blocks live in `.rfa` files; drawing types live in JSON.

---

## Table of Contents

1.  [Why the Drawing Type Manager exists](#1-why-it-exists)
2.  [The mental model — three concentric layers](#2-the-mental-model)
3.  [The 40 corporate drawing types at a glance](#3-the-40-corporate-drawing-types)
4.  [The 11 visual style packs](#4-the-11-visual-style-packs)
5.  [How a sheet gets produced — the engine pipeline](#5-the-engine-pipeline)
6.  [Editor walkthrough — Drawing Types tab](#6-editor-drawing-types-tab)
7.  [Editor walkthrough — View Style Packs tab](#7-editor-view-style-packs-tab)
8.  [Automation procedures — five worked examples](#8-automation-procedures)
9.  [Per-category annotation rules — what the engine auto-does](#9-annotation-rules)
10. [Per-category TAG7 paragraph depth](#10-tag7-depth)
11. [Conditional routing — when the same input picks different recipes](#11-conditional-routing)
12. [Project-scoped overrides — keep corporate clean](#12-project-overrides)
13. [Validation, drift detection and sync](#13-validation-drift-sync)
14. [Daily workflow cheat sheet](#14-daily-cheat-sheet)
15. [Troubleshooting](#15-troubleshooting)
16. [File map](#16-file-map)

---

## 1. Why it exists

A modern AEC project produces hundreds of drawings — site plans, RCPs, sections, elevations, MEP coordination plans, fabrication spools, presentation 3Ds, RFI sketches, authority submissions. Without a system, every drawing is a hand-crafted snowflake: a sheet here, a view template there, a manually adjusted scope box, a discipline colour scheme that drifts across projects.

STING's Drawing Type Manager replaces that chaos with **a single corporate catalogue of recipes**. Each recipe answers every question the engine needs to produce a drawing:

| Question | Answer field on the recipe |
|---|---|
| What paper size? | `paperSize` (A0/A1/A2/A3) |
| Which title block family? | `titleBlockFamily` |
| What scale? | `scale` (1:N integer) |
| Which view template applies? | `viewTemplateName` |
| Which graphic-style pack governs the look? | `viewStylePackId` (links into `STING_VIEW_STYLE_PACKS.json`) |
| Where do viewports land on the sheet? | `slots[]` (normalised 0..1 over the drawable zone) |
| What sheet number / name format? | `sheetNumberPattern` / `sheetNamePattern` |
| Where does the view crop? | `crop` (`ScopeBox` / `ScopeBoxOrBbox` / `TightBbox` / `RoomBoundary` / `None`) |
| Which section / elevation marker family? | `sectionMarker.family` |
| What gets auto-dimensioned and auto-tagged? | `annotation.rules[]` |
| Which tag family per category? Which TAG7 depth? | `annotation.tagFamilies` + `annotation.tagDepths` |
| Which title-block cells auto-fill? | `titleBlockParams` (with `${PRJ_ORG_…}` and `{disc}/{lvl}/…` tokens) |
| Which routing rule fires when? | `routing[]` table (first-match-wins) |

> **Plain-language version:** instead of training every drafter to remember "for an MEP coord A1 plan use scale 1:50, view template `STING - MEP Coordination`, scope-box-or-bbox crop, dimension grids, tag equipment, suit code S2, sheet number `M-{lvl}-{seq:D3}`…" you train them once to **pick a recipe by name** and the engine does the rest.

---

## 2. The mental model

Three layers, top to bottom:

```
              ┌──────────────────────────────────────────────┐
              │  ROUTING TABLE                               │
              │  (discipline, phase, docType, level, …)      │
              │  → drawingTypeId                             │
              └──────────────────────────────────────────────┘
                              │
                              ▼
              ┌──────────────────────────────────────────────┐
              │  DRAWING TYPE                                │
              │  paper, title block, scale, slots[],         │
              │  crop, section marker, annotation rules[],   │
              │  numbering, viewStylePackId →                │
              └──────────────────────────────────────────────┘
                              │
                              ▼
              ┌──────────────────────────────────────────────┐
              │  VIEW STYLE PACK                             │
              │  appearance (line wt, text, dim, hatch),     │
              │  filterRules[], vgOverrides[]                │
              │  + extends chain (corp-base ◀ corp-plan ◀ …) │
              └──────────────────────────────────────────────┘
```

Layer 1 — **Routing**: when the user asks for a "MEP Coordination plan at level 02 in the design phase," a first-match-wins rule table converts that 4-tuple into a drawing-type id like `mep-coord-A1-1to50`.

Layer 2 — **Drawing type**: the recipe answers every layout question. It points to layer 3 by id.

Layer 3 — **View style pack**: the *visual* styling — line weights, text styles, filter rules, per-category VG overrides. Every pack inherits from `corp-base` (or another pack) so a small office baseline cascades into 40 drawing types without copy-paste.

> **Layman tip:** think of it like a restaurant. Routing = the head waiter ("table for 4 in the design phase, MEP coord, please"). Drawing type = the menu item ("MEP Coord A1 1:50"). View style pack = the chef's plating standard ("MEP coordination = blue ducts, green pipes, amber electrics, halftoned walls"). One waiter, one menu, one chef — every plate looks the same.

---

## 3. The 40 corporate drawing types

Every recipe shipped in `StingTools/Data/STING_DRAWING_TYPES.json`. Use the `id` to refer to a recipe; that string is the contract between the catalogue, the routing table, the title-block `TB_DRAWING_TYPE_ID_TXT` field and the engine.

### 3.1 Architectural (12 recipes)

| id | Paper | Scale | Purpose |
|---|---|---|---|
| `arch-plan-A1-1to100` | A1 | 1:100 | Architectural plan (default) |
| `arch-rcp-A1-1to100` | A1 | 1:100 | Reflected ceiling plan |
| `arch-section-A1-1to50` | A1 | 1:50 | Building section |
| `arch-elev-A1-1to100` | A1 | 1:100 | Building elevation |
| `arch-detail-A3-1to20` | A3 | 1:20 | Construction detail |
| `arch-site-A1-1to500` | A1 | 1:500 | Site / context plan |
| `arch-roof-A1-1to100` | A1 | 1:100 | Roof plan |
| `arch-floor-finishes-A1-1to100` | A1 | 1:100 | Floor finishes plan |
| `arch-fire-strategy-A1-1to100` | A1 | 1:100 | Fire strategy plan |
| `arch-accessibility-A1-1to100` | A1 | 1:100 | Part M / BS 8300 accessibility |
| `arch-interior-elev-A1-1to50` | A1 | 1:50 | Interior elevation |
| `arch-window-schedule-A2` | A2 | – | Window schedule |

### 3.2 Structural (4 recipes)

| id | Paper | Scale | Purpose |
|---|---|---|---|
| `struct-plan-A1-1to100` | A1 | 1:100 | Structural plan |
| `struct-section-A1-1to50` | A1 | 1:50 | Structural section |
| `struct-foundation-A1-1to100` | A1 | 1:100 | Foundation plan |
| `struct-rebar-detail-A3-1to20` | A3 | 1:20 | Rebar detail |

### 3.3 MEP (4 recipes)

| id | Paper | Scale | Purpose |
|---|---|---|---|
| `mep-plan-A1-1to100` | A1 | 1:100 | MEP plan |
| `mep-coord-A1-1to50` | A1 | 1:50 | MEP coordination plan |
| `mep-hvac-duct-A1-1to100` | A1 | 1:100 | HVAC ductwork plan |
| `mep-plantroom-A1-1to50` | A1 | 1:50 | Plant room layout |

### 3.4 Electrical (4 recipes)

| id | Paper | Scale | Purpose |
|---|---|---|---|
| `elec-riser-A2-1to100` | A2 | 1:100 | Electrical riser |
| `elec-power-A1-1to100` | A1 | 1:100 | Power layout |
| `elec-lighting-A1-1to100` | A1 | 1:100 | Lighting layout |
| `elec-fire-alarm-A1-1to100` | A1 | 1:100 | Fire alarm |

### 3.5 Public Health / Plumbing (1 recipe)

| id | Paper | Scale | Purpose |
|---|---|---|---|
| `plumb-drainage-A1-1to100` | A1 | 1:100 | Drainage layout |

### 3.6 Fabrication / spool (2 recipes)

| id | Paper | Scale | Purpose |
|---|---|---|---|
| `pipe-spool-A1-1to50` | A1 | 1:50 | Pipe spool drawing |
| `duct-spool-A1-1to50` | A1 | 1:50 | Duct spool drawing |

### 3.7 Coordination / handover (3 recipes)

| id | Paper | Scale | Purpose |
|---|---|---|---|
| `coord-clash-A1-1to50` | A1 | 1:50 | Clash report sheet |
| `fm-asset-location-A1-1to100` | A1 | 1:100 | FM asset location |
| `handover-A1` | A1 | – | Handover sheet |

### 3.8 Schedules / legend (3 recipes)

| id | Paper | Purpose |
|---|---|---|
| `door-schedule-A2` | A2 | Door schedule |
| `arch-window-schedule-A2` | A2 | Window schedule (also listed in §3.1) |
| `legend-A2` | A2 | Generic legend |

### 3.9 Presentation pack (5 recipes)

| id | Paper | Purpose |
|---|---|---|
| `pres-3d-axon-A1` | A1 | 3D axonometric + caption |
| `pres-perspective-A1` | A1 | Full-bleed perspective |
| `pres-exterior-elev-A1` | A1 | Exterior elevation w/ material callouts |
| `pres-render-board-A1` | A1 | 4-up render board |
| `pres-context-site-A1` | A1 | Aerial + legend + caption |

### 3.10 Clarification / RFI pack (3 recipes)

| id | Paper | Purpose |
|---|---|---|
| `clar-markup-A1` | A1 | Mark-up + query log + revision strip |
| `clar-rfi-A3` | A3 | Single-issue A3 RFI sketch |
| `clar-design-intent-A1` | A1 | Plan + 3D + narrative + materials |

> **Layman summary:** 40 names, 4 paper sizes, 12 disciplines, every common drawing your office produces — already covered. Add new recipes per project as needed; corporate names should not change without a kit-version bump (see §13).

---

## 4. The 11 visual style packs

Every drawing type points to a visual style pack via `viewStylePackId`. Packs live in `StingTools/Data/STING_VIEW_STYLE_PACKS.json` and inherit from each other in a tree:

```
corp-base
├── corp-standard-plan
│   └── corp-standard-rcp
├── corp-standard-section
├── corp-standard-elevation
├── corp-standard-detail
├── corp-clarification
├── corp-coordination
│   └── corp-fabrication-shop
└── corp-presentation-rich
    └── corp-presentation-mono
```

Children only need to record fields that **differ** from the parent. The applier walks the chain at runtime and merges in.

### 4.1 Pack purpose & line weight headlines

| id | Extends | Purpose | Headline overrides |
|---|---|---|---|
| `corp-base` | – | House baseline | Line wt 1.0, text 2.5 mm, dim Linear, ISO 13567 mono palette, 5 universal filters (Existing/Demolished/New/Temporary/Out-of-Scope), grids/levels visible at wt 3, ref planes hidden |
| `corp-standard-plan` | `corp-base` | 1:100 architectural plan | Walls cut wt 5; doors/windows wt 2; floors halftone; ceilings hidden; rooms transparent fill 80; structural columns cut wt 5; pipes/ducts hidden |
| `corp-standard-rcp` | `corp-standard-plan` | Reflected ceiling plan | Walls halftone; floors hidden; ceilings on at wt 3; lighting wt 2; air terminals blue wt 2; mech equip on |
| `corp-standard-section` | `corp-base` | 1:50 building section | Walls cut wt 5; floors cut wt 4; struct cols cut wt 5; insulation halftone; rooms/furniture hidden |
| `corp-standard-elevation` | `corp-base` | 1:100 elevation | Walls wt 3; struct cols wt 4; topo halftone; curtain panels & mullions wt 2 |
| `corp-standard-detail` | `corp-base` | 1:20 detail | Text 2.0 mm; walls cut wt 6; struct framing cut wt 5; rebar red wt 2; insulation halftone |
| `corp-clarification` | `corp-base` | RFI / mark-up | RFI Query filter red wt 3; Design Intent filter blue wt 2; generic annotations red wt 2 |
| `corp-coordination` | `corp-base` | MEP coord | Ducts blue wt 3; pipes green wt 3; electrics amber wt 3; mech-equip purple wt 3; fire alarm red wt 3; struct halftone purple |
| `corp-fabrication-shop` | `corp-coordination` | Spool drawing | Text 2.0mm Shop; dim Ordinate; pipes wt 5, ducts wt 5; walls + struct framing hidden |
| `corp-presentation-rich` | `corp-standard-plan` | Internal IDR/TDR | Text 3.0mm Presentation; walls deep slate cut wt 6; floors light grey; rooms desaturated; topo green halftone; grids/dims hidden |
| `corp-presentation-mono` | `corp-presentation-rich` | Client greyscale | All colours collapse to greyscale (walls black, floors light grey, furniture mid grey) |

### 4.2 Filter rules included in every pack (via corp-base)

The five filter rules below ship in `corp-base` and cascade to every child:

| Filter | Visibility | Halftone | Projection colour | Projection wt | Notes |
|---|---|---|---|---|---|
| Existing - Halftone | Yes | Yes | Mid-grey #808080 | 1 | Auto-greys "Existing" phase |
| Demolished - Red | Yes | Yes | Red #C00000 | 1 (30 % transparency) | "Demolished" phase |
| New Construction | Yes | No | Black | 4/5 | "New" phase, bold |
| Temporary Works | Yes | Yes | Orange #FF6600 | 1 | Temporary scope |
| Out of Scope | Yes | Yes | Light grey #AAAAAA | 1 (50 % transparency) | NTS / outside contract |

Adding more filter rules per pack is a one-line JSON edit (or one row in the editor's filter-rules grid).

### 4.3 Why the inheritance tree matters

When the BIM coordinator wants to bump every drawing's text height by 0.5 mm, they edit **one field in `corp-base`** — `appearance.textStyleName` — and every child pack inherits the change unless it explicitly overrides text style.

This is the single biggest reason to resist project-by-project tweaks: every tweak you make in `corp-base` benefits 40 drawing types and ~11 packs. Every tweak you make in a leaf pack benefits one.

> **Layman summary:** packs are *plating standards*. Edit corp-base for "everything looks better"; edit a leaf pack only when one drawing has its own visual identity (presentation, clarification, fabrication shop).

---

## 5. The engine pipeline

When a sheet is generated by ANY entry point (Fabrication ▸ Generate Fab Package, Sheet Manager ▸ Create From Template, Doc Automation ▸ Batch Sections / Elevations / Sheets), the same 10-step pipeline runs. Knowing the order matters when something looks wrong — you can skip directly to the failed step instead of reading every log line.

```
 1. Routing            (RoutingTable.Resolve)              → drawingTypeId
 2. Fetch recipe       (DrawingDispatcher.Resolve)         → DrawingType profile
 3. Pre-flight         (DrawingTypeValidator.Validate)     → list of warnings/errors
 4. Create / find sheet (ViewSheet.Create + tb selection)  → ViewSheet instance
 5. Lock check         (DrawingTypeStamper.IsLocked)       → skip if STING_STYLE_LOCKED_BOOL == 1
 6. Stamp DT id        (DrawingTypeStamper.Stamp)          → STING_DRAWING_TYPE_ID_TXT
 7. Apply view props   (DrawingTypePresentation.Apply)
       a. scale  ←  DrawingType.Scale
       b. detail ←  DrawingType.DetailLevel
       c. view template ← DrawingType.ViewTemplateName
       d. crop strategy ← DrawingCropApplier (ScopeBox / Bbox / Room / None)
       e. style pack   ← ViewStylePackApplier (extends-chain merged)
       f. annotation pass ← AnnotationRunner (Rules + TagFamilies + TagDepths)
 8. Place viewports    (slots[]  — normalised positions)
 9. Title-block fill   (TitleBlockParamApplier)
       ${PRJ_ORG_…} substitution + {disc}/{lvl}/{seq:Dn}/{mark} tokens
10. Stamp sheet number (SubstituteTokens(SheetNumberPattern))
```

Steps 1–3 happen on the API thread before any document mutation. Step 4 begins a Revit `Transaction`. Steps 5–10 run inside that transaction; each step is `try/catch` wrapped so a single annotation failure does not abort the whole sheet creation. Warnings collect into an `ApplyResult.Warnings` list and surface in the result dialog.

> **Layman version:** the engine is a 10-step assembly line. If a sheet looks wrong, ask which step misfired:
> - Wrong title block → step 4
> - Wrong colours / line weights → step 7e (style pack)
> - Wrong dim / tag pattern → step 7f (annotation pass)
> - Sheet number missing / mis-formatted → step 10
> - Title-block cell empty → step 9 (or `ProjectInformation` field actually empty)

---

## 6. Editor — Drawing Types tab

Open via dock-panel **DOCS ▸ Drawing Types ▸ Edit Types** or hit the *Edit Types* alias in the dock toolbar. Three columns:

```
┌─────────────┬───────────────────────────────────────────────────┐
│ Search      │ Identity, Sheet, Views, Numbering, Crop, Section, │
│ [+ New]     │ Annotation rule pack, Slots                       │
│ [Clone]     │ ────────────────────────────────────────────────  │
│ [Delete]    │ Validation strip                                   │
│             │                                                    │
│ List:       │ ────────────────────────────────────────────────  │
│ Drawing     │                                                    │
│ types       │                                                    │
│ ordered by  │                                                    │
│ Discipline  │                                                    │
│ → Purpose   │                                                    │
│ → Id        │                                                    │
└─────────────┴───────────────────────────────────────────────────┘
                Save / Close (footer pinned, never clipped)
```

### 6.1 Identity card

| Field | Picker | Layman tip |
|---|---|---|
| Id | text | Stable identifier — never change once shipped to a project. Convention: `<discipline>-<purpose>-<paper>-<scale>` |
| Name | text | Human-readable, e.g. "Mech Coord A1 1:50" |
| Description | text | Two-line synopsis |
| Purpose | combo | Plan / RCP / Section / Elevation / Detail / Schedule / Spool / Coordination / Legend / 3D / Cover / Startup / Render / Submission / Clarification / ClientReview / DesignReview |
| Discipline | combo | ISO 19650 single-letter code: A/B/C/D/E/F/G/H/I/K/L/M/P/Q/S/T/W/X/Y/Z, or `*` for any |
| Phase / RIBA stage | combo | RIBA 0..7 or `*` |
| Origin | read-only | `corporate` (locked) or `project` (editable) |

### 6.2 Sheet card

| Field | Picker source | Result |
|---|---|---|
| Paper size | ISO 216 | A0..A4 |
| Title block family | live `OST_TitleBlocks` symbols | Resolves a `FamilySymbol` at sheet creation |
| Orientation | combo | Landscape / Portrait |

### 6.3 Views card

| Field | Picker source |
|---|---|
| Scale | integer (1:N) |
| Detail level | Coarse / Medium / Fine |
| View template name | live `View.IsTemplate=true` ∪ STING corporate templates |
| Viewport type name | live `OST_Viewports` element types |

### 6.4 Numbering card

| Field | Tokens supported |
|---|---|
| Sheet number pattern | `{prj}` `{orig}` `{vol}` `{lvl}` `{role}` `{spool}` `{disc}` `{discipline}` `{sys}` `{mark}` `{seq}` `{seq:D2}..{seq:D4}` |
| Sheet name pattern | Same token set |

The combo is pre-populated with five common ISO 19650 patterns; users can paste their own.

### 6.5 Crop card

| Kind | What it does |
|---|---|
| `ScopeBox` | Use the named scope box, error if missing |
| `ScopeBoxOrBbox` | Scope box if present, else tight bounding box of model elements + margin |
| `TightBbox` | Bounding box of model elements + margin |
| `RoomBoundary` | Union of room boundaries + margin (plans only; falls back to TightBbox if no rooms) |
| `None` | Leave the view's default crop alone |

The `Margin (mm)` field controls the offset around the union.

### 6.6 Section / elevation marker card

Bind a section/elevation/callout marker family from the live `OST_SectionHeads / OST_ElevationMarks / OST_CalloutHeads` lists. Add a mark prefix (`S` / `EL` / `D`) and far-clip distance (default 3000 mm, raise for atrium / large-volume sections).

### 6.7 Annotation rule pack card

This is the heart of the automation contract. Three sub-grids:

1. **Automation rules grid** — one row per *(Category, Rule type)* pair. Six columns: ✓ Enabled · Category · Rule type · Tag family · Depth · ×. See §9 for the full rule-type vocabulary.
2. **Dimension strategy / style / Dense-until-scale** — three single-row controls.
3. **Tag families grid** — Category → Family → Depth (the per-category TAG7 paragraph depth, see §10).

> **Layman tip:** the automation rules grid is the *only* place to enable auto-dim grids, auto-tag rooms, auto-tag equipment, etc. The legacy 9 checkboxes (`AutoDimGrids`, `AutoTagRooms`, …) were retired and folded into Rules entries on first edit; if you open an old project and see the migration happen, it's harmless and idempotent.

### 6.8 Slots card

Add as many slots as needed — each is a normalised rectangle on the drawable zone:

| Column | Meaning |
|---|---|
| Label | "Main Plan", "Key Plan", "Notes", … |
| ViewType | FloorPlan / CeilingPlan / Section / Elevation / ThreeD / Detail / DraftingView / Legend / Schedule / AreaPlan / EngineeringPlan / Walkthrough / Rendering / Plan |
| X / Y / W / H | 0..1 normalised coordinates over the *drawable zone* (not the paper) |
| Scale | Optional per-slot scale override |

The header and every row share the same column widths so cells stay aligned at any dialog size.

### 6.9 Save semantics

The footer **Save** button writes only project-origin entries to `<project>/_BIM_COORD/drawing_types.json` — the corporate baseline on disk is never mutated. If you edit a corporate entry, its origin flips to `project` automatically (SHA-256 drift detection).

---

## 7. Editor — View Style Packs tab

Same dialog, second tab. Mirrors the Drawing Types layout:

```
┌─────────────┬───────────────────────────────────────────────────┐
│ Search      │ Identity (id / name / extends parent / origin)    │
│ [+ New]     │ Appearance (line wt scale, text, dim, hatch,      │
│ [Clone]     │             scale hint, colour scheme, view tpl)  │
│ [Delete]    │ Filter rules grid (project-filter overrides)      │
│             │ VG overrides grid (per-category overrides)        │
│ List of     │ Tag families map (category → family)              │
│ packs       │                                                    │
│ with        │ Validation strip                                   │
│ extends     │                                                    │
│ chain       │                                                    │
└─────────────┴───────────────────────────────────────────────────┘
```

### 7.1 Identity card

| Field | Notes |
|---|---|
| id / name / description | Same conventions as Drawing Types tab |
| Extends (parent pack id) | Combo of every other pack in the registry. Empty = root |
| Origin | `corporate` (corporate baseline) or `project` (editable) |

### 7.2 Appearance card

| Field | Picker source |
|---|---|
| Line-weight scale | `0.8` (lighter), `1.0` (default), `1.5` (bolder fabrication) |
| Text style name | live `TextNoteType` ∪ STING corporate text styles ("STING - 2.0mm", "2.5mm", "3.0mm Presentation", "2.0mm Shop", "3.5mm Large Format") |
| Dimension style name | live `DimensionType` ∪ STING corporate dim styles ("STING - Linear", "Ordinate", "Chain") |
| Hatch palette | "ISO 13567 monochrome" / "ISO 13567 colour" / "AIA NCS" / "BS 1192 mono" / "Project custom" |
| View template name | live `View.IsTemplate=true` ∪ 18 STING corporate templates |
| Detail level | Coarse / Medium / Fine |
| Scale hint | "1:5", "1:10", "1:20", "1:25", "1:50", "1:100", "1:200", "1:500" |
| Colour scheme | Monochrome / Discipline / Pastel / RAG / Spectral / Warm / Cool / High Contrast / PresentationRich / ClarificationRed |

### 7.3 Filter rules grid

One row per filter override. Columns:

| Column | Picker source | Notes |
|---|---|---|
| Filter name | `ParameterFilterElement` ∪ 20 common STING filters | Filter must already exist in the project |
| Visible | check | Visibility toggle |
| Halftone | check | Halftone toggle |
| Proj-Col | hex `#RRGGBB` | Projection line colour |
| Proj-Wt | int | Projection line weight (1..16) |
| Cut-Col | hex `#RRGGBB` | Cut line colour |
| Cut-Wt | int | Cut line weight |
| Trans% | int | Surface transparency 0..100 |

### 7.4 VG overrides grid

One row per category override. Columns identical to filter rules but the first column is **Category** (combo of `KnownRevitCategories ∪ doc.Settings.Categories.AllowsBoundParameters`).

> **Layman tip:** the Category combo is *editable* — type `OST_Walls` or `<Room Separation>` (a subcategory key) if you need to override something not in the dropdown. The applier resolves built-in enum, display name *and* `<bracket>` subcategories.

### 7.5 Save semantics

Save writes only project-origin packs to `<project>/_BIM_COORD/view_style_packs.json`. Editing a corporate pack flips its origin to `project` via the SHA-256 drift detector — your edits land in the project file, the corporate baseline on disk stays pristine.

---

## 8. Automation procedures — five worked examples

Five canonical workflows that produce a quality drawing automatically. Run these end-to-end before claiming the manager is "set up" in your office.

### 8.1 MEP coordination plan, A1 @ 1:50

Pre-conditions:
- Project has a scope box named `MEP-COORD-L02` covering level 02.
- View template `STING - MEP Coordination` is loaded.
- Title-block family `STING_TB_TECHNICAL_A1` is loaded with `TB_DRAWING_TYPE_ID_TXT == "mep-coord-A1-1to50"`.

Procedure:
1. **Dock panel ▸ DOCS ▸ Doc Automation ▸ Batch Sheets**.
2. Pick "Mech Coord A1 1:50" from the picker (the picker now lists Drawing Type profiles alongside built-in templates).
3. Select level 02 + scope box `MEP-COORD-L02` from the dialog.
4. Click **OK**.

What the engine does (10-step pipeline from §5):
- Resolves recipe `mep-coord-A1-1to50`.
- Validates pre-flight (scope box exists, view template loaded, title-block family loaded).
- Creates a new ViewSheet with title block `STING_TB_TECHNICAL_A1`.
- Applies `STING - MEP Coordination` view template + `corp-coordination` style pack (ducts blue, pipes green, electrics amber, mech equip purple, struct halftoned).
- Crops to scope box `MEP-COORD-L02` + 150 mm margin.
- Annotation rules fire: auto-dim grids, auto-dim levels, auto-tag rooms, auto-tag mechanical equipment, auto-tag air terminals, auto-annotate space numbers, auto-annotate flow arrows on ducts, auto-annotate slope on pipes (12 rules in total — see §9.3).
- Slot 0: Main coord plan, Slot 1: Key plan, Slot 2: Notes legend.
- Title-block fields auto-fill: client, project code, sheet number `M-02-001`, suit code `S2`, revision `P01`.

Time: ~8 seconds per sheet. Manual equivalent: ~25 minutes per sheet.

### 8.2 Pipe spool fabrication package

Pre-conditions:
- Project has assemblies grouped by `ASS_SPOOL_NR_TXT` (run **Fabrication ▸ Generate Fab Package** earlier in the workflow if not yet done).
- `STING_TB_ASSEMBLY_PIPE` title-block family loaded.

Procedure:
1. **Dock panel ▸ TAGS ▸ Fabrication ▸ Generate Fab Package**.
2. Pick discipline `Pipe`, select assemblies (or "All").
3. Click **OK**.

Engine actions:
- For each assembly, `ShopDrawingComposer.ResolveTitleBlock` reads `DISCIPLINE = Pipe` → picks `STING_TB_ASSEMBLY_PIPE`.
- Drawing-type id `pipe-spool-A1-1to50` resolves; style pack `corp-fabrication-shop` applies (line wt 1.5×, ordinate dimensions, walls hidden).
- Slot JSON inside the title-block reads 5 slots: PLAN, ISO, ELEV0, ELEV90, 3D — the composer drops one viewport per slot.
- Annotation rules: auto-tag pipe fittings, auto-tag duct fittings, auto-annotate slope, auto-annotate flow arrow, auto-tag pipe accessories (6 rules).
- BOM strip auto-fills from `AssyParams` (`ASS_SPOOL_NR_TXT`, `ASS_WEIGHT_KG`, `ASS_WELD_COUNT_NR`, …).
- ISO 6412 axonometric symbols lazy-load from `Families/ISO6412/`.

Output: one A1 sheet per spool, ready for the workshop floor, BOM tied live to the assembly.

### 8.3 Architectural plan — full level 01 publish

Procedure:
1. **DOCS ▸ Drawing Types ▸ From Scope Boxes**.
2. The engine scans every scope box named `STING::<drawing-type-id>::<level>::<tag?>`.
3. For each match, it generates a sheet against the matching recipe.
4. Idempotent — re-running the command on a project that already has the stamped views just re-applies the profile (does not duplicate views).

Naming convention to remember:
```
STING::arch-plan-A1-1to100::L01
STING::arch-plan-A1-1to100::L02
STING::arch-plan-A1-1to100::L03
STING::mep-coord-A1-1to50::L01::east
STING::mep-coord-A1-1to50::L01::west
```

> **Layman tip:** name your scope boxes correctly once, and the rest of the project's drawing-production effort collapses to a button.

### 8.4 Clarification (RFI) sketch

Procedure:
1. **DOCS ▸ Drawing Types ▸ Edit Types**.
2. Find `clar-rfi-A3` in the list. Confirm:
   - Title block: `STING_TB_CLARIFICATION_A3`.
   - View style pack: `corp-clarification` (red callouts, blue design-intent overlay).
   - Annotation rule: `Generic Annotations → AutoTag` (so RFI comment bubbles get tagged).
3. Open a working view of the issue area, enable the `RFI Query` filter on a Detail Group of the affected element.
4. **DOCS ▸ Doc Automation ▸ Batch Sections** ▸ pick `clar-rfi-A3` recipe.
5. Engine creates an A3 sheet with one detail callout, the design-intent overlay, the query log table and a revision strip.

Output: a single, consistent RFI sheet format that every drafter produces identically.

### 8.5 Client presentation 3D + key plan

Procedure:
1. Set up a 3D view named `Pres-Axon-East` and a key plan named `Pres-Keyplan-Site`.
2. Open the recipe `pres-3d-axon-A1` in the editor; confirm slots[0] points to ThreeD (full bleed) and slot[1] points to Plan (key plan, top-right).
3. **DOCS ▸ Sheet Manager ▸ Create From Template** ▸ pick `pres-3d-axon-A1`.
4. Engine creates an A1 sheet, drops the 3D + key plan into the slots, applies `corp-presentation-rich` (deep slate walls, light-grey floors, rooms transparent fill, no grids/dims).
5. Title block stamped with the 9-field title-block param map (`PRJ_ORG_CLIENT_NAME`, "Suitability: S3", caption from a designated parameter).

Output: a magazine-ready presentation sheet — same layout every time the studio produces a TDR.

---

## 9. Annotation rules — what the engine auto-does

The annotation rule pack carries a list of `AutoAnnotationRule` entries. Each rule is a `(Category, Rule type)` pair plus three optional fields (Enabled, Tag family override, Depth override). The engine fires the rule against the current view at step 7f of the pipeline.

### 9.1 The 21 rule-type vocabulary

| Rule type | What it does |
|---|---|
| `AutoTag` | Place a default tag against every visible instance of the category |
| `AutoDim` | Run an automatic dimension chain |
| `AutoDimOrdinate` | Same as AutoDim but Ordinate strategy |
| `AutoDimChain` | Continuous chain dimension |
| `AutoTagWithLeader` | AutoTag + force leader on every tag |
| `AutoTagHideIfEmpty` | AutoTag but skip if the tag value resolves to empty |
| `AutoTagTypeMark` | Tag using `Type Mark` parameter rather than instance Mark |
| `AutoTagRoomName` | Tag rooms with name only |
| `AutoTagRoomNumber` | Tag rooms with number only |
| `AutoTagDoorNumber` | Door schedule mark |
| `AutoTagWindowMark` | Window schedule mark |
| `AutoTagEquipmentTag` | Equipment tag (mech/elec/plumb) |
| `AutoTagGridBubble` | Place grid bubble at every visible grid intersection |
| `AutoDimWallLength` | Linear dimension along every wall |
| `AutoDimColumnGrid` | Dimension column-to-column grid spacing |
| `AutoDimOpenings` | Dimension door / window openings |
| `AutoDimElevation` | Dimension floor / ceiling elevations |
| `AutoAnnotateSlope` | Add slope annotation (pipes, drainage, ramps, roofs) |
| `AutoAnnotateFlowArrow` | Add flow direction arrow (ducts, pipes) |
| `AutoAnnotateSpaceNumber` | Number every space (HVAC zones) |
| `AutoAnnotateAreaBoundary` | Tag the area boundary line |

### 9.2 Default rule packs by discipline

The rule packs ship in `STING_DRAWING_TYPES.json`. Every drawing type carries a populated `annotation.rules[]` array:

**Architectural (15 rules — same for arch-plan / arch-rcp / arch-section / arch-elev / arch-detail / arch-interior-elev):**
```
Grids → AutoDim                Levels → AutoDim
Rooms → AutoTag                Doors → AutoTag
Windows → AutoTag              Stairs → AutoTag
Railings → AutoTag             Furniture → AutoTag
Casework → AutoTag             Areas → AutoTag
Walls → AutoDimWallLength
Doors → AutoDimOpenings        Windows → AutoDimOpenings
Rooms → AutoTagRoomName        Rooms → AutoTagRoomNumber
```

**MEP (12 rules — mep-plan / mep-coord / mep-hvac-duct / mep-plantroom / coord-clash):**
```
Grids → AutoDim                Levels → AutoDim
Rooms → AutoTag
Mechanical Equipment → AutoTag Air Terminals → AutoTag
Duct Fittings → AutoTag        Pipe Fittings → AutoTag
Electrical Equipment → AutoTag Lighting Fixtures → AutoTag
Spaces → AutoAnnotateSpaceNumber
Ducts → AutoAnnotateFlowArrow
Pipes → AutoAnnotateSlope
```

**Structural (7 rules — struct-plan / struct-section / struct-foundation):**
```
Grids → AutoDim                Levels → AutoDim
Structural Columns → AutoTag   Structural Framing → AutoTag
Structural Foundations → AutoTag
Structural Rebar → AutoTag
Structural Columns → AutoDimColumnGrid
```

**Spool (6 rules — pipe-spool / duct-spool):**
```
Pipe Fittings → AutoTag        Duct Fittings → AutoTag
Pipes → AutoAnnotateSlope      Ducts → AutoAnnotateFlowArrow
Pipe Accessories → AutoTag     Duct Accessories → AutoTag
```

**Electrical / Plumbing / Specialty packs**: 6–8 rules each, see the JSON for exact contents.

**Schedule / Legend / Presentation types**: empty rule packs (no auto-annotation).

**Clarification types**: 1 rule (Generic Annotations → AutoTag).

### 9.3 How to add a new rule

1. Editor ▸ Drawing Types ▸ pick the recipe.
2. Annotation card ▸ click **+ Add rule**.
3. Pick Category from the combo (merged live + 50 known taggable categories).
4. Pick Rule type from the combo (the 21 vocabulary).
5. Optionally pick a Tag family override (live tag families ∪ `Iso19650Vocabulary.CommonTagFamilies`).
6. Optionally pick a Depth override (1..10, blank = inherit).
7. Save.

The new rule fires on the next sheet generated against this recipe.

### 9.4 Disabling a rule without deleting it

Untick the **✓ Enabled** column. The engine skips it but the row remains visible — useful when iterating on a project where one rule is too noisy.

---

## 10. Per-category TAG7 paragraph depth

TAG7 is the rich descriptive tag (see CLAUDE.md "TAG7 — Rich Descriptive Narrative" section). It has 6 sub-sections (A–F) and a `paragraph depth` knob 1..10:

| Depth | What surfaces in the tag |
|---|---|
| 1 | Identity only — name + number |
| 2 | Identity + system/function |
| 3 | + spatial context |
| 4 | + lifecycle status |
| 5 | + technical specs (default for fabrication) |
| 6 | + classification codes |
| 7 | + cost references |
| 8 | + maintenance schedule pointers |
| 9 | + handover annexes |
| 10 | Full audit — every TAG7 sub-section visible |

### 10.1 Why per-category

A spool drawing wants `Pipes` at depth 5 (specs for the welder) but `Walls` at depth 1 (just an outline label). A client presentation wants `Rooms` at depth 1 (just the room name) and everything else hidden. Rather than ship 40 view-templates that differ only on depth, the rule pack carries a **per-category override map** in `annotation.tagDepths`.

### 10.2 Editing depths

The Tag families grid in the editor has three columns: **Category · Family · Depth**. Each row writes both the family name *and* the depth into the same recipe, keyed by category. Depth is a 1..10 combo (uneditable — only whole numbers).

If a category is in `tagFamilies` but not in `tagDepths`, it falls back to the recipe's `denseUntilScale` rule, then to depth 2 (the office baseline).

### 10.3 Where the depth lands

The annotation pass writes depth into the tag-family instance parameter `TAG_PARAGRAPH_DEPTH_INT`. The tag family's visibility formulas hide TAG7 sub-sections C..F whenever `TAG_PARAGRAPH_DEPTH_INT < (n)`. No code change needed when you tweak depth — the family's visibility logic does the rest.

---

## 11. Conditional routing

The routing table converts a `(discipline, phase, docType, level, projectCode)` tuple into a recipe id. Each rule has up to 5 optional regex predicates; **all set predicates must match** (logical AND). First-match-wins ordering.

### 11.1 Routing schema

```jsonc
{
  "discipline":           "M",                // exact match
  "phase":                "*",                // exact match (wildcard)
  "docType":              "Plan",             // exact match
  "drawingTypeId":        "mep-coord-A1-1to50",

  // Optional regex predicates (Phase 113 Week 6):
  "disciplineMatches":    "^[ME]$",           // ECMAScript regex
  "phaseMatches":         "^(C|H)$",
  "docTypeMatches":       "^(Plan|Coord)$",
  "levelMatches":         "^B\\d+",           // any basement
  "projectCodeMatches":   "^STG-2026-\\d{4}$" // narrow to STING projects
}
```

### 11.2 Worked example — basement plans get a different recipe

```jsonc
{ "discipline": "M", "phase": "*", "docType": "Plan",
  "levelMatches": "^B\\d+",
  "drawingTypeId": "mep-plantroom-A1-1to50" },

{ "discipline": "M", "phase": "*", "docType": "Plan",
  "drawingTypeId": "mep-plan-A1-1to100" }
```

The first rule fires for any basement level (`B1`, `B2`, …) and routes mech plans to the plantroom recipe. The second rule catches everything else.

### 11.3 Worked example — presentation phase routes to client recipes

```jsonc
{ "discipline": "A", "phase": "PRESENTATION", "docType": "Plan",
  "drawingTypeId": "pres-context-site-A1" },

{ "discipline": "A", "phase": "*", "docType": "Plan",
  "drawingTypeId": "arch-plan-A1-1to100" }
```

Same discipline (`A`), same docType (`Plan`), but different phase → different recipe. The same Revit document can host both production and presentation drawings without the user having to think about which template to apply.

### 11.4 Order matters

Place the *more specific* rule first. The dispatcher evaluates top-to-bottom and stops at the first match. If you accidentally place a wildcard rule above a specific one, the specific one is never reached.

---

## 12. Project-scoped overrides — keep corporate clean

Two override files per project, both under `<project>/_BIM_COORD/`:

| File | Overrides |
|---|---|
| `drawing_types.json` | Drawing types + routing rules (project entries win on `id`; project routing rules are **prepended** so they fire first) |
| `view_style_packs.json` | View style packs (project entries win on `id`) |

### 12.1 What lands in the project file

When you click Save in the editor, the dialog writes ONLY the entries marked `origin: project` to the project file. Corporate entries on disk (in `StingTools/Data/`) are **never mutated** by Save.

If you edit a corporate recipe in the dialog, the SHA-256 drift detector fires on the next reload and flips the recipe's origin to `project`. From that point on it lives in the project file and shadows the corporate baseline for this project only.

### 12.2 Worked example — bump arch-plan's title block

A particular project wants `arch-plan-A1-1to100` to use a custom client-branded title block:

1. Editor ▸ Drawing Types ▸ pick `arch-plan-A1-1to100`.
2. Sheet card ▸ Title block family ▸ pick `STING_TB_CLIENT_CUSTOM_A1`.
3. Save.

Result:
- Project file `<project>/_BIM_COORD/drawing_types.json` now contains a copy of `arch-plan-A1-1to100` with the new title block, marked `origin: "project"`.
- Corporate file untouched.
- Other projects in the office continue to use the corporate title block.

### 12.3 Promoting a project tweak to the corporate baseline

When a project tweak proves useful office-wide:

1. Open the project's `_BIM_COORD/drawing_types.json`.
2. Copy the modified entry.
3. Paste into the corporate `StingTools/Data/STING_DRAWING_TYPES.json`, replacing the original.
4. Bump the kit version (see §13).
5. Re-validate every drawing type that depends on the changed entry.

> **Layman tip:** treat the corporate JSON like a library catalogue. Project edits are personal annotations; promoting a tweak to corporate is the equivalent of getting a book added to the library shelf.

---

## 13. Validation, drift detection and sync

Three integrity mechanisms keep the catalogue trustworthy.

### 13.1 Pre-flight validator (`DrawingTypeValidator`)

Run via dock-panel **DOCS ▸ Drawing Types ▸ Inspect**. The validator checks every recipe for:

| Check | Severity |
|---|---|
| `titleBlockFamily` resolves to a loaded `OST_TitleBlocks` symbol | Warning |
| `viewTemplateName` resolves to a loaded `View.IsTemplate=true` | Warning |
| `viewportTypeName` resolves to a loaded viewport type | Warning |
| `sectionMarker.family` resolves to a loaded marker family | Warning |
| Slots: every rectangle inside 0..1, no overlaps | Error |
| Annotation `tagFamilies` values resolve to loaded families | Warning |
| `viewStylePackId` resolves to a known pack | Error |
| Routing rules: every `drawingTypeId` matches an existing recipe | Error |

Output: a `StingResultPanel` with one row per recipe and counts. Errors block downstream commands; warnings allow run-with-warnings (engine falls back, reports loss).

### 13.2 Drift detection (SHA-256 corporate lock)

On every load, the registry computes a SHA-256 hash of the corporate JSON file and compares it to the stored `checksum` field on each entry. If the hash changed (someone edited the file outside the editor), the entry's origin flips to `project` automatically. The status bar surfaces the count.

This means:
- *Authorised* edits go through the editor → land in the project file → corporate file untouched.
- *Out-of-band* edits flip origin to `project` → still applied, but the editor highlights the drift.

### 13.3 Sync styles (`DrawingTypes ▸ Sync Styles`)

A common scenario: a recipe was edited weeks ago, but views/sheets stamped before the edit still carry the old style. **Sync Styles** scans every stamped view (i.e. has `STING_DRAWING_TYPE_ID_TXT` set), detects scale / detail / template drift against the current recipe, shows a first-10 preview and on confirmation re-runs the full presentation pipeline against each drifted view (annotation off, to avoid re-dimensioning).

Skips any view with `STING_STYLE_LOCKED_BOOL == 1` — useful if you have manual tweaks you want to preserve.

### 13.4 Reload (`DrawingTypes ▸ Reload`)

Forces a registry cache flush. Use after editing the corporate JSON on disk by hand (rare) or after pulling new recipes from a teammate.

---

## 14. Daily cheat sheet

The four-click drawing production cycle:

```
                          ┌───────────────────────────────┐
                          │  1. Pick drawing type recipe  │
                          │  2. Run Batch Sheets / Sections│
                          │  3. Inspect result strip       │
                          │  4. Issue Deliverable          │
                          └───────────────────────────────┘
```

| Step | Where | What happens |
|---|---|---|
| 1 | Editor list / picker | Recipe id, view template, style pack, slots, annotations all pre-decided |
| 2 | DOCS ▸ Sheet Manager / Doc Automation / Fabrication | 10-step engine pipeline runs |
| 3 | Result panel | Errors red, warnings amber, OKs green; auto-opens after run |
| 4 | BIM ▸ Issue Deliverable | Title-block fields refresh, rev mints, QR stamps, PDF exports, CDE posts |

### 14.1 Set-up (one-off per office)

1. Confirm `StingTools/Data/STING_DRAWING_TYPES.json` is on disk where the plugin can find it.
2. Confirm `StingTools/Data/STING_VIEW_STYLE_PACKS.json` is on disk.
3. Author the title-block kit per [`TITLE_BLOCK_CREATION_GUIDE.md`](TITLE_BLOCK_CREATION_GUIDE.md). Each title block carries `TB_DRAWING_TYPE_ID_TXT` matching a recipe.
4. Run **DOCS ▸ Drawing Types ▸ Inspect** in a freshly-opened project. Resolve every error.

### 14.2 Set-up (per project)

1. Open project, ensure `_BIM_COORD/` folder exists (the engine creates it on first save).
2. Run **DOCS ▸ Drawing Types ▸ Reload**.
3. Optionally edit a recipe via the editor — your edit lands in `_BIM_COORD/drawing_types.json`.
4. Run **DOCS ▸ Drawing Types ▸ From Scope Boxes** if you have scope boxes named per the convention.

### 14.3 During production

- Keep the dialog open — it's modeless once docked.
- Use **+ Clone** to spin up a project-scoped variant of a corporate recipe.
- Use **Inspect** before every IFC issue — a 30-second sanity check.

---

## 15. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| "Drawing type not found" when running From Scope Boxes | Scope-box name uses unknown id | Confirm the id segment matches a recipe in `STING_DRAWING_TYPES.json` exactly (case-sensitive) |
| Sheet generated but title block is wrong | Recipe `titleBlockFamily` blank or doesn't resolve | Open the recipe ▸ Sheet card ▸ pick a real loaded title-block family |
| Sheet generated but everything looks like default Revit | Style pack didn't apply — usually means the view template hides everything the pack would override | Check view template hierarchy; remember the template applies BEFORE the pack and the pack only writes overrides for fields not pinned by the template |
| Auto-tag rules don't fire | Rules grid empty, or rules disabled, or category combo holds a mismatched name | Open Annotation card; confirm rules exist; confirm Category strings match Revit category display names exactly |
| Same view re-stamped on every run | `STING_STYLE_LOCKED_BOOL` not set after manual tweaks | Set to 1 to pin the view; SyncStyles will skip it |
| Routing picks the wrong recipe | Wildcard rule placed above a specific rule | Re-order so specific rules come first |
| Editor is slow to open on large project | Live picker queries can be expensive on 1000+ family-loaded projects | Cache once on Init; reopen the dialog with the same project to use the warm cache |
| Project override file gets out of date when corporate updates | Project entry's checksum doesn't drift if you didn't open the editor | Open the editor; click Save; SaveToProjectOverride re-emits the file with the latest `Routing` table |
| Validator reports "viewStylePackId references missing pack" | Pack id typo or pack not yet shipped | Check `STING_VIEW_STYLE_PACKS.json`; run `DrawingTypes ▸ Reload` |

---

## 16. File map

| File | Purpose |
|---|---|
| `StingTools/Core/Drawing/DrawingType.cs` | POCO models — DrawingType, AnnotationRulePack (Rules, TagDepths), AutoAnnotationRule, IsoNaming, DrawingSlot, DrawingCropStrategy, SectionMarkerSpec, PrintOverride, DrawingRoutingRule, DrawingTypeLibrary |
| `StingTools/Core/Drawing/DrawingTypeRegistry.cs` | Loader — corporate + project merge, SHA-256 drift detection, MigrateFromLegacy invocation |
| `StingTools/Core/Drawing/DrawingDispatcher.cs` | Routing resolver — `(discipline, phase, docType, level)` → drawingTypeId |
| `StingTools/Core/Drawing/DrawingTypePresentation.cs` | The 8-step apply pipeline (lock check → stamp → scale → detail → template → crop → style pack → annotation) |
| `StingTools/Core/Drawing/DrawingCropApplier.cs` | Implements the 5 crop strategies |
| `StingTools/Core/Drawing/ViewStylePack.cs` + `ViewStylePackRegistry.cs` + `ViewStylePackApplier.cs` | View style pack POCO, loader (extends-chain resolution), applier (writes `OverrideGraphicSettings`) |
| `StingTools/Core/Drawing/DrawingTypeStamper.cs` | Stamps `STING_DRAWING_TYPE_ID_TXT` + `STING_STYLE_LOCKED_BOOL` on views/sheets |
| `StingTools/Core/Drawing/TitleBlockParamApplier.cs` | Resolves `${PRJ_ORG_…}` and `{disc}/{lvl}/{seq:Dn}/{mark}` tokens into title-block instance parameters |
| `StingTools/Core/Drawing/DrawingDriftDetector.cs` | Scale / detail / template drift scanner |
| `StingTools/Core/Drawing/DrawingTypeValidator.cs` | Pre-flight checks |
| `StingTools/Core/Drawing/Iso19650Vocabulary.cs` | Closed-list dropdown sources (DisciplineCodes, DocTypes, SuitabilityCodes, RibaStages, …) |
| `StingTools/UI/DrawingTypeEditorDialog.cs` | The editor dialog (this guide's main subject) |
| `StingTools/UI/ProjectAssetPicker.cs` | Live readers over `Document.Settings.Categories`, `OST_TitleBlocks`, view templates, viewport types, scope boxes, dimension styles, text styles, parameter filters, tag families, levels, phases, worksets |
| `StingTools/Commands/Drawing/DrawingTypesInspectCommand.cs` | Inspect command |
| `StingTools/Commands/Drawing/DrawingTypesReloadCommand.cs` | Reload command |
| `StingTools/Commands/Drawing/DrawingSyncStylesCommand.cs` | Sync Styles command |
| `StingTools/Commands/Drawing/DrawingBrowserOrganizerCommand.cs` | Browser Organizer (groups views/sheets by drawing type id) |
| `StingTools/Commands/Drawing/GenerateFromScopeBoxesCommand.cs` | From Scope Boxes command |
| `StingTools/Data/STING_DRAWING_TYPES.json` | 40 corporate recipes + routing |
| `StingTools/Data/STING_VIEW_STYLE_PACKS.json` | 11 corporate view style packs |
| `<project>/_BIM_COORD/drawing_types.json` | Per-project drawing-type overrides |
| `<project>/_BIM_COORD/view_style_packs.json` | Per-project view-style-pack overrides |
| `docs/guides/DRAWING_TYPE_MANAGER_GUIDE.md` | This file |
| `docs/guides/TITLE_BLOCK_CREATION_GUIDE.md` | Title-block kit author's guide (companion document) |

---

> **Final layman summary:**
> The Drawing Type Manager replaces the per-drafter, per-project, per-day cognitive load of "which template, which scale, which scope box, which tag family, which sheet number format" with a 40-entry catalogue + 11-pack visual library + a routing table. Train the team to **pick a recipe by name**; the engine handles the other 40 decisions per drawing. Quality drawings are produced automatically because the recipe is the contract — every output is identical to the contract, by construction.
