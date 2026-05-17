# STING Title Block Creation & Management — Complete Layman Guide

> **Audience:** Revit users, BIM coordinators and project managers who have never authored a title block from scratch. No prior knowledge of shared parameters, nested families or the STING engine is assumed.
>
> **Plain-language goal:** by the end of this guide you will own a *family of title blocks* that auto-fill from project data, route the right viewports into the right slots, and present correctly for **fabrication, technical review, client review, construction issue, tender, as-built, authority submission and marketing.**
>
> **Repository scope:** `StingTools/` (Revit plugin) and `Families/AssemblyTitleBlocks/` (the `.rfa` author-source folder).
>
> **Companion guide:** read [`DRAWING_TYPE_MANAGER_GUIDE.md`](DRAWING_TYPE_MANAGER_GUIDE.md) for the catalogue of 40 drawing-type recipes + 11 view-style packs that *consume* the title blocks you author here. Title blocks are the **inputs**; drawing types are the **recipes**; the engine joins them at sheet-creation time.

> ### What changed in the latest workflow (Phase 113 + UX improvements, Apr 2026)
>
> 1.  **Drawing Type Editor dialog** is now the primary surface for binding a title block to a recipe. Open via dock-panel **DOCS ▸ Drawing Types ▸ Edit Types**. Two tabs: *Drawing Types* and *View Style Packs* — each with a list on the left, a form on the right, footer pinned (Save/Close never clipped on resize).
> 2.  **View Style Packs registry** (`STING_VIEW_STYLE_PACKS.json`) — the corporate visual library. 11 packs (corp-base + 10 children) with full extends-chain inheritance. Edit a single field in `corp-base` and every drawing type that descends from it picks up the change.
> 3.  **Per-category Annotation rules grid** replaces the legacy 9 boolean flags (`AutoDimGrids`, `AutoTagRooms`, …). Add as many `(Category, Rule type)` rows as you need — 21 rule types covering AutoTag / AutoDim / AutoAnnotateSlope / AutoAnnotateFlowArrow / AutoTagGridBubble / etc. Old projects auto-migrate at load time.
> 4.  **Per-category TAG7 paragraph depth** (`tagDepths` field on the rule pack) — set tag depth per category so a spool drawing tags Pipes at depth 5 (full specs) but Walls at depth 1 (outline label).
> 5.  **Live combo dropdowns everywhere** — title-block family, view template, viewport type, scope-box name, section-marker family, dimension style, text style, parameter filter, category, tag family — all sourced from `ProjectAssetPicker` against the active project (merged with STING corporate defaults).
> 6.  **Title-block params declarative binding** — `titleBlockParams` field on the recipe maps title-block instance parameter names to value templates with `${PRJ_ORG_…}` and `{disc}/{lvl}/{seq:Dn}` substitution. Editor card surfaces this as a row-per-param grid.
> 7.  **Slots grid** column widths now align between header and rows pixel-for-pixel; footer Save/Close never clip on narrow windows.
>
> If you used STING before April 2026 and your project still has the legacy 9 boolean fields in `_BIM_COORD/drawing_types.json`, opening the editor and clicking Save once auto-migrates the project file to the new Rules schema (idempotent).

---

## Table of Contents

1.  [What a title block actually is (and why STING treats it as a database row)](#1-what-a-title-block-actually-is)
2.  [The STING title-block stack at a glance](#2-the-sting-title-block-stack-at-a-glance)
3.  [Before you start — checklist](#3-before-you-start--checklist)
4.  [Stage 1 — Plan your title-block family tree](#4-stage-1--plan-your-title-block-family-tree)
5.  [Stage 2 — Build the cover page](#5-stage-2--build-the-cover-page)
6.  [Stage 3 — Build the start-up (project information) page](#6-stage-3--build-the-start-up-project-information-page)
7.  [Stage 4 — Build the fabrication title block](#7-stage-4--build-the-fabrication-title-block)
8.  [Stage 5 — Build the technical-presentation title block](#8-stage-5--build-the-technical-presentation-title-block)
9.  [Stage 6 — Build the client-presentation title block](#9-stage-6--build-the-client-presentation-title-block)
10. [Stage 7 — Build the construction / tender / as-built / submission title blocks](#10-stage-7--build-the-construction--tender--as-built--submission-title-blocks)
11. [Stage 8 — How section annotations help automation](#11-stage-8--how-section-annotations-help-automation)
12. [Stage 9 — Wire up the corporate base styles (`corp-base`)](#12-stage-9--wire-up-the-corporate-base-styles-corp-base)
13. [Stage 10 — Validate, lock, version and ship](#13-stage-10--validate-lock-version-and-ship)
14. [Stage 11 — Day-to-day management](#14-stage-11--day-to-day-management)
15. [Reference — every TB_ shared parameter explained](#15-reference--every-tb_-shared-parameter-explained)
16. [Reference — every PRJ_TB_ shared parameter explained](#16-reference--every-prj_tb_-shared-parameter-explained)
17. [Reference — required nested families](#17-reference--required-nested-families)
18. [Troubleshooting](#18-troubleshooting)
19. [File map](#19-file-map)
20. [Bind a title block to a recipe via the Drawing Type Editor (latest workflow)](#20-bind-a-title-block-to-a-recipe-via-the-drawing-type-editor-latest-workflow)

---

## 1. What a title block actually is

In Revit, a **title block** is a special family category (`OST_TitleBlocks`) that hosts the printable border, the project info strip, the revision table, the company logo and any auto-fill labels. Every Revit sheet must reference exactly one title-block family **type**. The family lives in `.rfa` form, the type lives inside the project, the instance lives on a sheet.

In STING, a title block is more than a printable border — it is a **self-describing database row** that the engine can read at runtime to answer:

| Question the engine asks | Parameter that answers it |
|---|---|
| Where on the sheet may I drop viewports? | `TB_DRAWZONE_X_MM`, `TB_DRAWZONE_Y_MM`, `TB_DRAWZONE_W_MM`, `TB_DRAWZONE_H_MM` |
| Which rectangles must I never overlap? | `TB_RESERVED_REGIONS_JSON_TXT` |
| What slots already exist for fabrication? | `TB_VIEWPORT_SLOTS_JSON_TXT` |
| Which drawing-type catalogue entry does this title block back? | `TB_DRAWING_TYPE_ID_TXT` |
| Should I show the company strip? The discipline colour band? The QR code? | `TB_SHOW_*_BOOL` family of toggles |
| Which nested families (north arrow, scale bar, key plan, grid bubble, section marker, elevation marker, callout, rev cloud) are pre-loaded? | `TB_NESTED_*_FAMILY_TXT` family of pointers |
| Is this title block valid? When was it last validated? | `TB_LAST_VALIDATED_DT_TXT`, `TB_TEMPLATE_VERSION_TXT` |

Because the title block answers all those questions itself, the STING placement engine, the validator, the QR-code stamper and the auto-rotate-north-arrow tool **never need a hard-coded fallback** — they read the family.

> **Plain-language version:** the title block is the *map* of the sheet. The engine reads the map; you draw the map once.

---

## 2. The STING title-block stack at a glance

```
            ┌──────────────────────────────────────────────────────────────┐
            │  Project (.rvt)                                              │
            │   ├── Project Information                                    │
            │   │   └── PRJ_TB_*  (37 shared params — once, edit anytime)  │
            │   │                                                          │
            │   ├── Title Block Family Type "STING - A1 - Fabrication"     │
            │   │   ├── TB_DRAWZONE_*           (drawable zone)            │
            │   │   ├── TB_RESERVED_REGIONS_JSON_TXT                       │
            │   │   ├── TB_VIEWPORT_SLOTS_JSON_TXT                         │
            │   │   ├── TB_NESTED_*_FAMILY_TXT  (nested family names)      │
            │   │   ├── TB_SHOW_*_BOOL          (visibility toggles)       │
            │   │   └── TB_AUTHORITY_CODE_TXT   (KCCA/ERA/NEMA/blank)      │
            │   │                                                          │
            │   └── ViewSheet                                              │
            │       └── instance of "STING - A1 - Fabrication"             │
            └──────────────────────────────────────────────────────────────┘
                                     ▲
                                     │ reads at runtime
                                     │
            ┌──────────────────────────────────────────────────────────────┐
            │  STING Engine                                                │
            │   ├── ShopDrawingComposer  (S5.6)        — fabrication route │
            │   ├── DrawingDispatcher    (Phase 113)   — JSON catalogue    │
            │   ├── PlacementCentre      (Phase 127)   — auto-place views  │
            │   ├── DocAutomationExt    (Batch sheets) — bulk creation     │
            │   └── Title Block Validator              — pre-flight checks │
            └──────────────────────────────────────────────────────────────┘
                                     ▲
                                     │ catalogue-driven
                                     │
            ┌──────────────────────────────────────────────────────────────┐
            │  Data files (StingTools/Data/)                               │
            │   ├── STING_DRAWING_TYPES.json   — 15 drawing-type recipes   │
            │   ├── STING_VIEW_STYLE_PACKS.json — 9 visual style packs     │
            │   ├── TITLE_BLOCK.csv            — per-discipline defaults   │
            │   └── STING_PARAMS_V4.txt        — shared-param fragment     │
            └──────────────────────────────────────────────────────────────┘
```

Three layers, no magic:
1. The **family** describes itself.
2. The **engine** reads the family.
3. The **catalogue** decides which family to use for which drawing.

---

## 3. Before you start — checklist

Tick every box before opening the Family Editor. Each item exists because skipping it breaks something downstream.

| ✓ | Check | Why |
|---|---|---|
| ☐ | Revit 2025 / 2026 / 2027 installed | STING targets `net8.0-windows` against these three releases |
| ☐ | STING plugin built and loaded (`StingTools.dll` + `StingTools.addin` in `…/Addins/2025/`) | The validator command lives in the plugin |
| ☐ | `MR_PARAMETERS.txt` is set as the active Shared Parameter file (`Manage > Shared Parameters > Browse`) | All `TB_*` and `PRJ_TB_*` GUIDs resolve from this file |
| ☐ | `STING_DRAWING_TYPES.json` reachable in `StingTools/Data/` | The engine reads it for routing |
| ☐ | Eight nested families authored or stub-loaded — north arrow, scale bar, key plan, grid bubble, section marker, elevation marker, callout tag, rev-cloud tag | The title block embeds them by family name; missing ones become validator warnings |
| ☐ | A test project (`.rvt`) with 1 sheet, 1 plan view and 1 section | You will need it to dry-run the auto-placement engine |
| ☐ | Company logo as `.png` 300 dpi (transparent background, max 200 mm × 60 mm) | Embedded in cover and start-up pages |
| ☐ | Branch checked out: `claude/title-block-guide-Kdthv` | All edits land on the agreed feature branch |

> **Layman tip:** if you can answer "yes" to *"can I open Revit, see STING in the ribbon, and pick a title block type from the Type Selector"* — you are 90 % of the way through the prereqs.

---

## 4. Stage 1 — Plan your title-block family tree

You will not author a single title block — you will author **one family per *purpose × paper size*** combination, then ship them as a *kit*. A kit is reusable, swappable per sheet, and validated as a whole.

### 4.1 The recommended kit (15 title blocks, layman-detailed)

The kit ships as 15 `.rfa` families covering every deliverable surface a typical AEC office produces. Each block binds via `TB_DRAWING_TYPE_ID_TXT` to one or more recipes in `STING_DRAWING_TYPES.json` and is plated by a view style pack in `STING_VIEW_STYLE_PACKS.json` (see [`DRAWING_TYPE_MANAGER_GUIDE.md`](DRAWING_TYPE_MANAGER_GUIDE.md) §3 and §4 for the full catalogue).

| # | File name | Purpose | Paper | Authority | Bound recipes (examples) | Style pack | Audience |
|---|---|---|---|---|---|---|---|
| 1 | `STING_TB_COVER_A3.rfa` | Project cover page | A3 portrait | – | Cover purpose recipes | corp-base | Front of any deliverable bundle |
| 2 | `STING_TB_STARTUP_A3.rfa` | Start-up / project info page | A3 portrait | – | Startup purpose recipes | corp-base | Page 2 of any deliverable bundle |
| 3 | `STING_TB_ASSEMBLY_PIPE.rfa` | Pipe spool fabrication | A1 landscape | – | `pipe-spool-A1-1to50` | corp-fabrication-shop | Workshop floor |
| 4 | `STING_TB_ASSEMBLY_DUCT.rfa` | Duct spool fabrication | A1 landscape | – | `duct-spool-A1-1to50` | corp-fabrication-shop | Workshop floor |
| 5 | `STING_TB_ASSEMBLY_COND.rfa` | Conduit / electrical fabrication | A1 landscape | – | (project-scoped recipe) | corp-fabrication-shop | Workshop floor |
| 6 | `STING_TB_ASSEMBLY_HANGER.rfa` | Hanger-only fabrication | A2 landscape | – | (project-scoped recipe) | corp-fabrication-shop | Workshop floor |
| 7 | `STING_TB_TECHNICAL_A1.rfa` | Technical / coordination presentation | A1 landscape | – | `mep-coord-A1-1to50`, `mep-plan-A1-1to100`, `mep-plantroom-A1-1to50`, `mep-hvac-duct-A1-1to100`, `coord-clash-A1-1to50` | corp-coordination | Internal IDR / TDR meetings |
| 8 | `STING_TB_CLIENT_A1.rfa` | Client presentation | A1 landscape | – | `pres-3d-axon-A1`, `pres-perspective-A1`, `pres-context-site-A1`, `pres-render-board-A1`, `pres-exterior-elev-A1` | corp-presentation-rich / corp-presentation-mono | Client steering committee |
| 9 | `STING_TB_IFC_A1.rfa` | Issued For Construction | A1 landscape | – | `arch-plan-A1-1to100`, `mep-plan-A1-1to100`, `struct-plan-A1-1to100` (phase=Construction routing) | corp-standard-plan / corp-coordination | Site team |
| 10 | `STING_TB_IFT_A1.rfa` | Issued For Tender | A1 landscape | – | Same recipe set as IFC, phase=Tender routing | corp-standard-plan | Tender pack |
| 11 | `STING_TB_AS_BUILT_A1.rfa` | As-built / record | A1 landscape | – | All production recipes, phase=As-Built routing | corp-standard-plan (existing un-halftoned) | Client handover |
| 12 | `STING_TB_SUBMISSION_KCCA.rfa` | Authority — KCCA (Kampala City Council) | A1 landscape | KCCA | All production recipes with `authorityCode=KCCA` predicate | corp-standard-plan | Permit submission |
| 13 | `STING_TB_SUBMISSION_ERA.rfa` | Authority — ERA (Electricity Regulatory Authority) | A1 landscape | ERA | `elec-power-A1-1to100`, `elec-lighting-A1-1to100`, `elec-fire-alarm-A1-1to100` | corp-standard-plan | Permit submission |
| 14 | `STING_TB_SUBMISSION_NEMA.rfa` | Authority — NEMA (Environment) | A1 landscape | NEMA | `arch-fire-strategy-A1-1to100`, `arch-site-A1-1to500`, `plumb-drainage-A1-1to100` | corp-standard-plan | Permit submission |
| 15 | `STING_TB_MARKETING_A2.rfa` | Marketing / render | A2 landscape | – | (marketing-render-A2 — project-scoped) | corp-presentation-rich | Brochure, website |

#### 4.1.1 Layman-friendly description of every kit member

1. **Cover page (`STING_TB_COVER_A3`)** — the front sheet of any deliverable bundle. Big logo, project name banner, deliverable code, revision number. *No drawable zone* (so the engine refuses to drop viewports on it). Auto-fills from `PRJ_NAME_TXT`, `PRJ_NUMBER_TXT`, `PRJ_TB_DELIVERABLE_STATUS_TXT`, `PRJ_TB_REVISION_NR_TXT`. QR code links back to the CDE record.
2. **Start-up page (`STING_TB_STARTUP_A3`)** — page 2. "About this deliverable" paragraph, project team table, sheet-index placeholder, revision-history placeholder. The DocAutomation `BuildStartupPage` command fills the placeholder rectangles with live `DrawingRegisterSchedule` and `RevisionSchedule` views at issue time.
3. **Pipe spool (`STING_TB_ASSEMBLY_PIPE`)** — workshop drawing for one spool of pipe. 5 viewport slots (PLAN / ISO / ELEV0 / ELEV90 / 3D) plus a 200 mm BOM strip on the right. BOM auto-fills from `AssyParams` (spool#, weight, weld count, bolt count, fitting count, test pressure, fab status, BOM rev, QC inspector). Bound to `pipe-spool-A1-1to50`.
4. **Duct spool (`STING_TB_ASSEMBLY_DUCT`)** — same shape as pipe spool but discipline = DUCT. Bound to `duct-spool-A1-1to50`. Engine swaps the discipline-tagged grid bubble (`STING_GRID_HVAC`) and the title-block colour band (HVAC blue).
5. **Conduit / electrical fabrication (`STING_TB_ASSEMBLY_COND`)** — same shape, discipline = COND. Tagged for cable runs, conduit fittings and electrical equipment.
6. **Hanger-only fabrication (`STING_TB_ASSEMBLY_HANGER`)** — A2 (smaller) because hanger packages are simpler. Used for drawing lone support / hanger / bracket assemblies.
7. **Technical presentation (`STING_TB_TECHNICAL_A1`)** — internal IDR / TDR. Discipline colour band on the right edge (Mech blue / Elec yellow / Plumb green / Arch grey / Struct red), key plan top-left, north arrow top-right (auto-rotates to True North), revision history bottom-left, project info bottom-right. Hosts MEP coord, mech plan, plant rooms, clash sheets.
8. **Client presentation (`STING_TB_CLIENT_A1`)** — softer typography (Arial 18 pt minimum), client logo top, no grids/levels/sections visible, big breathing room. Hosts presentation 3Ds, perspectives, context-site, render boards, exterior elevations. Style pack flips between `corp-presentation-rich` (full colour) and `corp-presentation-mono` (greyscale) per deliverable.
9. **Issued For Construction (`STING_TB_IFC_A1`)** — solid red 25 mm "ISSUED FOR CONSTRUCTION" banner across the top. Revision table on, QR code links to RFI portal. Validator refuses to ship if `PRJ_TB_DESIGN_STAGE_TXT != "C"`.
10. **Issued For Tender (`STING_TB_IFT_A1`)** — diagonal "ISSUED FOR TENDER" watermark at 8 % opacity. Required seals from Lead Architect / Structural Engineer / MEP Engineer. CDE state pinned to `S3`.
11. **As-built (`STING_TB_AS_BUILT_A1`)** — green "AS-BUILT — RECORD" status banner. Existing-phase elements show un-halftoned (the record IS the existing fabric). Frozen rev table.
12. **KCCA submission (`STING_TB_SUBMISSION_KCCA`)** — authority-specific. Required `PRJ_PLOT_NUMBER`, `PRJ_LRV_NUMBER`, `PRJ_PHYSICAL_ADDRESS` non-empty. KCCA-tagged grid bubble. Revision pattern `P\d{2}`. Form version baked in (`KCCA-2024-Rev3`).
13. **ERA submission (`STING_TB_SUBMISSION_ERA`)** — Electricity Regulatory Authority. ERA-tagged grid bubble, revision pattern `R\d{2}`. Required seals: Electrical Engineer.
14. **NEMA submission (`STING_TB_SUBMISSION_NEMA`)** — Environment authority. Required impact-assessment cross-reference. Used for fire-strategy, site, drainage submissions.
15. **Marketing render (`STING_TB_MARKETING_A2`)** — full-bleed render, only the company strip at the bottom. No grids / no annotations / no revision table. Used for brochures, web, social media.

> **Why so many?** Because each row above maps to a different *visual style pack* (`STING_VIEW_STYLE_PACKS.json`) and a different *drawing-type recipe* (`STING_DRAWING_TYPES.json`). One title block per recipe means the engine never has to guess.

### 4.2 Naming convention

`STING_TB_{PURPOSE}_{PAPER}` — purpose is **uppercase**, paper is **A0 / A1 / A2 / A3**. Authority blocks use `STING_TB_SUBMISSION_{AUTHORITY}` and **inherit** A1 landscape unless told otherwise.

### 4.3 Versioning

Every title block carries `TB_TEMPLATE_VERSION_TXT` — semver ish (`1.0.0`, `1.0.1`, …). Bump:
- **Major** when you change the drawable zone or the slot JSON (engine-visible).
- **Minor** when you change visibility toggles or nested families.
- **Patch** when you fix typography or tidy graphics.

The validator refuses to ship a deliverable if any title block on any sheet has `TB_TEMPLATE_VERSION_TXT` more than one major behind the kit baseline.

---

## 5. Stage 2 — Build the cover page

The cover page is **page 1** of every deliverable. Its job is to communicate *which project, which deliverable, who issued it, and which version*.

### 5.1 Why a separate family

Q: *"Can't the cover page just be a regular sheet with a big logo?"*
A: Yes — but then every project has to re-build the layout, the font sizes drift, and the auto-fill labels break when someone forgets to copy them. By making the cover page a dedicated title-block family **with no drawable zone** the engine knows it is unfit for viewports, refuses to drop one there, and the deliverable orchestrator routes it as `Cover` instead of `Plan`.

### 5.2 Author it — step by step

1. **File ▸ New ▸ Family ▸ Title Block ▸ A3 metric.rft**.
2. Set the family category Sheet Size to **A3 (297 × 420 mm portrait)** via Family Types.
3. **Insert ▸ Image** the `LOGO.png` at top-centre. Size = 80 mm wide × 24 mm tall, centred horizontally on the 297 mm width, with a 30 mm top margin. Lock its position with two EQ dimensions.
4. **Manage ▸ Shared Parameters**, point at `MR_PARAMETERS.txt`.
5. **Create ▸ Label**. Drop a label and bind to **`PRJ_NAME_TXT`**. Font: Arial Narrow, 24 pt, bold, centred. This is the *project name banner*.
6. Drop a second label bound to `PRJ_NUMBER_TXT` directly under it. Font: Arial Narrow, 14 pt.
7. Drop a third label bound to **`PRJ_TB_DELIVERABLE_STATUS_TXT`** (S2 / S3 / S4 / WIP / SHARED / PUBLISHED). Font: 14 pt, bold, all caps. This is the **CDE state stamp**.
8. Drop a fourth label bound to `PRJ_TB_REVISION_NR_TXT` and `PRJ_TB_REVISION_DATE_TXT` separated by a `—`. Font: 12 pt.
9. Drop a fifth label bound to `PRJ_TB_CLIENT_NAME_TXT`, then on the line below `PRJ_TB_CLIENT_ADDRESS_TXT`. Font: 12 pt.
10. **Insert ▸ Image** company strip footer. Size = 297 × 16 mm, anchored to the bottom of the sheet.
11. **Family Types** — add the *family parameters*:

    ```
    TB_PAPER_SIZE_TXT          = "A3"
    TB_ORIENTATION_TXT         = "Portrait"
    TB_PURPOSE_TXT             = "Cover"
    TB_TEMPLATE_VERSION_TXT    = "1.0.0"
    TB_DRAWZONE_W_MM           = 0      ← critical: zero means "no viewports allowed"
    TB_DRAWZONE_H_MM           = 0
    TB_MAX_VIEWPORTS_INT       = 0
    TB_SHOW_COMPANY_STRIP_BOOL = 1
    TB_SHOW_KEY_PLAN_BOOL      = 0
    TB_SHOW_NORTH_ARROW_BOOL   = 0
    TB_SHOW_SCALEBAR_BOOL      = 0
    TB_SHOW_REV_TABLE_BOOL     = 0
    TB_SHOW_QR_CODE_BOOL       = 1     ← QR links back to CDE record
    ```

12. **Save As** ▸ `STING_TB_COVER_A3.rfa` into `Families/AssemblyTitleBlocks/` (or your project's title-block library).

### 5.3 What auto-fills at issue time

When the deliverable orchestrator (Phase 112 template engine) renders a deliverable bundle and reaches the cover page, it:

1. Reads `ProjectInformation` parameters once.
2. Mints / increments the deliverable revision via `DocumentIdentityGenerator.Next()`.
3. Writes the freshly-minted values back to all `PRJ_TB_*` cover-page labels.
4. Bakes a QR code into `TB_QR_PAYLOAD_TXT` containing the deliverable id + CDE permalink.

Result: the cover page **never** has stale info, because every issue rewrites it.

---

## 6. Stage 3 — Build the start-up (project information) page

The start-up page is **page 2** of the deliverable bundle. It tells the recipient *what they are about to read* — scope, contributors, sheet index, revision history.

### 6.1 Why a dedicated start-up page

Tender packs, IFC bundles, and authority submissions all need a "this is what's inside" summary. A start-up page that auto-renders the sheet index from the project beats hand-typed contents pages every time.

### 6.2 Anatomy

```
┌────────────────────────────────────────────────────────────────────┐
│  ▌ STING — PROJECT START-UP            ◯ Logo top right           │
│                                                                    │
│  [Project name banner]                                             │
│  [Project number]              [Deliverable code & revision]       │
│  ─────────────────────────────────────────────────────────────     │
│  ABOUT THIS DELIVERABLE                                            │
│  PRJ_TB_ISSUE_SUMMARY_TXT (multi-line label, 8 lines)              │
│  ─────────────────────────────────────────────────────────────     │
│  PROJECT TEAM                                                      │
│  Client    : PRJ_TB_CLIENT_NAME_TXT                                │
│  Architect : PRJ_TB_CONSULTANT_NAME_TXT                            │
│  Structural: PRJ_TB_STRUCTURAL_CONSULTANTS_NAME_TXT                │
│  MEP       : PRJ_TB_MEP_CONSULTANTS_NAME_TXT                       │
│  Contractor: PRJ_TB_CONTRACTOR_NAME_TXT                            │
│  ─────────────────────────────────────────────────────────────     │
│  SHEET INDEX                                                       │
│  [Embedded "Drawing Register" schedule placeholder]                │
│  ─────────────────────────────────────────────────────────────     │
│  REVISION HISTORY                                                  │
│  [Embedded "Revision Schedule" placeholder]                        │
│  ─────────────────────────────────────────────────────────────     │
│  ✎ Drawn:    PRJ_TB_DRAWN_BY_TXT     PRJ_TB_DATE_DRAWN_TXT         │
│  ✎ Checked:  PRJ_TB_CHECKED_BY_TXT   PRJ_TB_DATE_CHECKED_TXT       │
│  ✎ Approved: PRJ_TB_APVD_BY_TXT      PRJ_TB_DATE_APVD_TXT          │
└────────────────────────────────────────────────────────────────────┘
```

### 6.3 Author it — step by step

1. Duplicate `STING_TB_COVER_A3.rfa` ▸ Save As `STING_TB_STARTUP_A3.rfa`.
2. Strip the cover-page banner styling — replace with a row of section headings (`ABOUT THIS DELIVERABLE`, `PROJECT TEAM`, `SHEET INDEX`, `REVISION HISTORY`).
3. Drop labels for every `PRJ_TB_*` field listed in the anatomy box. Keep them on a 5 mm grid for alignment.
4. **Critical**: the *Sheet Index* and *Revision History* areas are **placeholders, not embedded schedules.** Why? Because Revit cannot embed a schedule inside a title-block family. Instead:
   - Mark the rectangle with two reference planes named `SHEET_INDEX_AREA` and `REVISION_HISTORY_AREA`.
   - The DocAutomation `BuildStartupPage` command places live Drawing Register and Revision Schedule **schedules** into those rectangles when the start-up sheet is generated in the project.
5. Family Types — set:

    ```
    TB_PURPOSE_TXT             = "Startup"
    TB_DRAWZONE_W_MM           = 280   ← schedules need a drawable zone
    TB_DRAWZONE_H_MM           = 200
    TB_DRAWZONE_X_MM           = 8
    TB_DRAWZONE_Y_MM           = 100
    TB_MAX_VIEWPORTS_INT       = 0     ← schedules only, no viewports
    TB_RESERVED_REGIONS_JSON_TXT = '[
        {"name":"sheet_index","x":8,"y":180,"w":280,"h":80,"kind":"schedule"},
        {"name":"rev_history","x":8,"y":100,"w":280,"h":70,"kind":"schedule"}
    ]'
    TB_SHOW_KEY_PLAN_BOOL      = 0
    TB_SHOW_QR_CODE_BOOL       = 1
    ```

6. Save into the kit folder.

### 6.4 What auto-fills at issue time

The Phase 112 template engine, when it sees `TB_PURPOSE_TXT == "Startup"`:

1. Builds a `DrawingRegisterSchedule` in the project (or refreshes the existing one).
2. Builds a `RevisionSchedule` in the project (or refreshes the existing one).
3. Drops both into the reserved rectangles via `Viewport.Create`.
4. Writes `PRJ_TB_TOTAL_NO_SHEETS_TXT` so the cover-page count stays in sync.

---

## 7. Stage 4 — Build the fabrication title block

Fabrication title blocks (`STING_TB_ASSEMBLY_PIPE/DUCT/COND/HANGER`) are the **workshop-floor** drawings. Read in poor light, possibly oily, almost always at a workbench. So: thick lines, no halftone, BOM strip on the right, weld-count and bolt-count visible in the strip.

### 7.1 Why an A1 landscape

A1 (594 × 841 mm) gives 5 viewport slots (PLAN, ISO, ELEV0, ELEV90, 3D) plus a 200 mm right-hand strip for the Bill of Materials. A2 is too tight; A0 is wasteful and harder to fold.

### 7.2 Author it — step by step

1. **File ▸ New ▸ Family ▸ Title Block ▸ A1 metric.rft**.
2. Set Family Types Sheet Size to **A1 (594 × 841 mm landscape)**.
3. Insert the company strip footer, 841 × 16 mm.
4. Draw a vertical line at x = 641 mm — splitting the sheet into a **641 mm drawable zone** and a **200 mm BOM strip**.
5. Inside the BOM strip, drop labels for the assembly metadata. **Bind to the shared parameters in `StingTools.Core.Fabrication.AssyParams`:**

    | Label | Bound parameter | Note |
    |---|---|---|
    | Spool # | `ASS_SPOOL_NR_TXT` | Auto-populated by `ShopDrawingComposer` |
    | Weight | `ASS_WEIGHT_KG` | Number / Mass |
    | Test pressure | `ASS_TEST_PRESSURE_BAR` | Number / Pressure |
    | Fab location | `ASS_FAB_LOC_TXT` | WORKSHOP / SITE |
    | Fab seq # | `ASS_FAB_SEQ_NR` | Integer |
    | Fab status | `ASS_FAB_STATUS_TXT` | DRAFT / IFC / IFA / IFR |
    | BOM rev | `ASS_BOM_REV_TXT` | P01 / P02 / C01 |
    | QC inspector | `ASS_QC_INSPECTOR_TXT` | – |
    | Welds | `ASS_WELD_COUNT_NR` | – |
    | Bolts | `ASS_BOLT_COUNT_NR` | – |
    | Flanges | `ASS_FLANGE_COUNT_NR` | – |
    | Fittings | `ASS_FITTING_COUNT_NR` | – |
    | Length | `ASS_LENGTH_TOTAL_MM` | – |
    | Insulation | `ASS_INSULATION_AREA_M2` | – |
    | Supports | `ASS_SUPPORT_COUNT_NR` | – |
    | Spool ref | `ASS_SPOOL_DRAWING_REF_TXT` | – |
    | Discipline | `DISCIPLINE` | Pipe / Duct / Electrical / Hanger |

6. Inside the 641 mm drawable zone, place **5 detail-item markers** (a cheap way to lock the slot positions). The marker is a tiny detail item named `STING_DRAWZONE_SLOT_{N}`. Drop one per slot:

    | Slot | Position (norm 0..1) | Use |
    |---|---|---|
    | PLAN  | (0.00, 0.50, 0.65, 0.50) | top-left half |
    | ISO   | (0.65, 0.50, 0.35, 0.50) | top-right square |
    | ELEV0 | (0.00, 0.00, 0.32, 0.50) | bottom-left |
    | ELEV90| (0.32, 0.00, 0.33, 0.50) | bottom-middle |
    | 3D    | (0.65, 0.00, 0.35, 0.50) | bottom-right |

7. Encode the slot table in `TB_VIEWPORT_SLOTS_JSON_TXT`:

    ```
    [
      {"label":"PLAN",  "normX":0.00,"normY":0.50,"normW":0.65,"normH":0.50,"viewType":"FloorPlan",     "scale":25},
      {"label":"ISO",   "normX":0.65,"normY":0.50,"normW":0.35,"normH":0.50,"viewType":"ThreeD",        "scale":25},
      {"label":"ELEV0", "normX":0.00,"normY":0.00,"normW":0.32,"normH":0.50,"viewType":"Elevation",     "scale":25},
      {"label":"ELEV90","normX":0.32,"normY":0.00,"normW":0.33,"normH":0.50,"viewType":"Elevation",     "scale":25},
      {"label":"3D",    "normX":0.65,"normY":0.00,"normW":0.35,"normH":0.50,"viewType":"ThreeD",        "scale":25}
    ]
    ```

8. Family Types — set:

    ```
    TB_PURPOSE_TXT             = "Spool"
    TB_PAPER_SIZE_TXT          = "A1"
    TB_ORIENTATION_TXT         = "Landscape"
    TB_DISCIPLINE_LIST_TXT     = "PIPE"        ← per family, change to DUCT / COND / HANGER
    TB_DRAWZONE_X_MM           = 0
    TB_DRAWZONE_Y_MM           = 16            ← above company strip
    TB_DRAWZONE_W_MM           = 641
    TB_DRAWZONE_H_MM           = 559
    TB_MAX_VIEWPORTS_INT       = 5
    TB_DRAWING_TYPE_ID_TXT     = "pipe-spool-A1-1to50"     ← for PIPE family
    TB_SHOW_COMPANY_STRIP_BOOL = 1
    TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL = 1
    TB_SHOW_KEY_PLAN_BOOL      = 0
    TB_SHOW_NORTH_ARROW_BOOL   = 0
    TB_SHOW_SCALEBAR_BOOL      = 1
    TB_SHOW_QR_CODE_BOOL       = 1
    TB_NESTED_GRID_BUBBLE_FAMILY_TXT     = "STING_GRID_M"
    TB_NESTED_SECTION_MARKER_FAMILY_TXT  = "STING_SECTION_M"
    TB_NESTED_ELEVATION_MARKER_FAMILY_TXT= "STING_ELEVATION_M"
    TB_NESTED_CALLOUT_TAG_FAMILY_TXT     = "STING_CALLOUT_M"
    TB_NESTED_REV_CLOUD_TAG_FAMILY_TXT   = "STING_REVCLOUD_STD"
    TB_NESTED_SCALEBAR_FAMILY_TXT        = "STING_SCALEBAR_METRIC"
    ```

9. Save as `STING_TB_ASSEMBLY_PIPE.rfa`. Repeat for DUCT (change discipline to DUCT, drawing type id to `duct-spool-A1-1to50`, nested grid to `STING_GRID_HVAC`), COND, HANGER.

### 7.3 What auto-fills at fabrication

When the user clicks **Fabrication ▸ Generate Fab Package** in the dock panel:

1. `GenerateFabPackageCommand` opens a `TransactionGroup`.
2. `AssemblyGrouper` groups elements by spool number.
3. `AssemblyBuilder` creates an `AssemblyInstance`.
4. `ShopDrawingComposer.ResolveTitleBlock` reads `DISCIPLINE` from the assembly to pick `STING_TB_ASSEMBLY_PIPE` (or DUCT, COND, HANGER).
5. A new ViewSheet is created, the title block is dropped at (0, 0).
6. The composer reads `TB_VIEWPORT_SLOTS_JSON_TXT` and drops PLAN / ISO / ELEV0 / ELEV90 / 3D viewports into the named slots.
7. The composer writes assembly metadata (`ASS_SPOOL_NR_TXT`, `ASS_WEIGHT_KG`, `ASS_WELD_COUNT_NR`, …) into the title-block instance — the BOM strip auto-fills.
8. `IsoSymbolPlacer` lazy-loads ISO 6412 symbols from `Families/ISO6412/` into the 3D slot.

Result: zero manual layout, zero typing, every spool sheet identical, BOM always reflects the live assembly.

---

## 8. Stage 5 — Build the technical-presentation title block

Technical presentation is for the **internal coordination meeting** — the IDR (interim design review), TDR (technical design review) and the BIM 360 issue board. Audience: discipline leads and the BIM coordinator. Style pack: `technical-presentation`.

### 8.1 What it must show

| Strip | Content |
|---|---|
| Top | Project name, deliverable status banner (e.g. `S2 — SHARED`) |
| Right | **Discipline colour band** (Mech blue, Elec yellow, Plumb green, Arch grey, Struct red) — driven by `TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL` |
| Bottom-right | Standard project info strip (drawn by, checked by, approved by, scale, paper size, sheet number/total) |
| Bottom-left | Revision history table (max 8 rows; older revs auto-archive to the Revision Schedule on the start-up page) |
| Top-right | **North arrow** (auto-rotates to True North if `TB_NORTH_ARROW_AUTO_ROTATE_BOOL = 1`) |
| Top-left | **Key plan** with the current drawing area highlighted |

### 8.2 Author it — step by step

1. **File ▸ New ▸ Family ▸ Title Block ▸ A1 metric.rft**.
2. Reserve a **15 mm right-edge band** for the discipline colour strip. Draw a filled region the full height of the sheet, parameter-driven by `M_DISC_COLOR` family parameter (a colour family parameter that is overridden per type). Wrap the filled region in a visibility parameter linked to `TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL` so it can be hidden per sheet.
3. Lay out the project info strip at the bottom-right, 220 × 60 mm.
4. Lay out the revision history table at the bottom-left, 220 × 80 mm. 8 rows × 4 cols (Rev / Description / Date / By).
5. Place the north arrow in a 30 × 30 mm reserved circle at the top-right. The nested family is `STING_NORTH_ARROW_STD`; the rotation parameter is wired to a *True North* angle.
6. Place the key plan in a 60 × 40 mm reserved rectangle at the top-left. The nested family is `STING_KEYPLAN_BUILDING`.
7. Drop a single hidden detail item in the centre of the *drawable zone* — this is the **drawable-zone marker**. Its bounding box is what the engine reads to know "this is where viewports go". Mark it with `STING_DRAWZONE` and store its mark string in `TB_DRAWZONE_MARKER_ID_TXT`.
8. Family Types — set:

    ```
    TB_PURPOSE_TXT             = "Plan"
    TB_PAPER_SIZE_TXT          = "A1"
    TB_ORIENTATION_TXT         = "Landscape"
    TB_DISCIPLINE_LIST_TXT     = "M;E;P;A;S;FP;LV"   ← multi-discipline allowed
    TB_DRAWZONE_X_MM           = 5
    TB_DRAWZONE_Y_MM           = 80
    TB_DRAWZONE_W_MM           = 740                 ← 841 − 15 (disc band) − 60 (key+north) − 5 margin
    TB_DRAWZONE_H_MM           = 470                 ← 594 − 80 (info) − 30 (north) − 14 margin
    TB_DRAWZONE_MARKER_ID_TXT  = "STING_DRAWZONE"
    TB_DRAWING_TYPE_ID_TXT     = "mep-coord-A1-1to50"
    TB_MAX_VIEWPORTS_INT       = 4                    ← typical 4-up coord layout
    TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL = 1
    TB_SHOW_KEY_PLAN_BOOL      = 1
    TB_SHOW_NORTH_ARROW_BOOL   = 1
    TB_SHOW_SCALEBAR_BOOL      = 1
    TB_SHOW_REV_TABLE_BOOL     = 1
    TB_SHOW_COMPANY_STRIP_BOOL = 1
    TB_SHOW_QR_CODE_BOOL       = 1
    TB_NORTH_ARROW_AUTO_ROTATE_BOOL = 1
    ```

9. Save as `STING_TB_TECHNICAL_A1.rfa`.

### 8.3 What auto-fills at presentation time

The DocAutomation `BatchSheetsCommand` (Phase II of Drawing Template Manager) wires this title block into a sheet bundle:

1. The `DrawingDispatcher` resolves drawing-type `mep-coord-A1-1to50` from `STING_DRAWING_TYPES.json`.
2. The dispatcher picks `STING_TB_TECHNICAL_A1.rfa` because the drawing-type `titleBlockFamily` field points at it.
3. The view template `STING - Technical Presentation` (from `STING_VIEW_STYLE_PACKS.json`) is applied to every viewport.
4. The discipline colour strip auto-renders the per-sheet colour by reading the lead discipline from the resolved drawing-type recipe.
5. The QR code stamps the issue's CDE permalink.

---

## 9. Stage 6 — Build the client-presentation title block

Client presentation is for the **client steering committee, marketing brochure or planning department**. Audience: people who do not read drawings every day. Style pack: `client-presentation`.

### 9.1 Design intent — soft, readable, branded

| Goal | Mechanism |
|---|---|
| No grids, no levels, no section markers visible inside the drawable zone | The view template (`STING - Client Presentation`) hides them |
| Pastel walls, full-colour furniture | View template `colorScheme: "Pastel"` |
| Soft shadows | View template `shadowsOff: false`, `ambientShadows: true` |
| Large, readable typography | All labels in Arial, **18 pt minimum** |
| Lots of breathing room | 30 mm margin on all sides; the drawable zone is small (≈ 60 % of the sheet) |
| Strong client branding | Top 80 mm reserved for client logo + tagline; STING company strip at bottom only 8 mm tall |
| No technical clutter (no CDE state, no revision table, no QR code unless requested) | `TB_SHOW_REV_TABLE_BOOL = 0`, `TB_SHOW_QR_CODE_BOOL = 0`, deliverable status hidden |

### 9.2 Author it — step by step

1. **File ▸ New ▸ Family ▸ Title Block ▸ A1 metric.rft**.
2. Top 80 mm: drop a 200 × 60 mm placeholder for client logo (image control, image source = `PRJ_TB_LOGO_PATH_TXT`). Drop a 400 × 40 mm tagline label bound to a new project-info field `PRJ_TB_CLIENT_TAGLINE_TXT` (add to `MR_PARAMETERS.txt` if missing).
3. Bottom 8 mm: STING company strip, low-key.
4. The full middle is the drawable zone. **No** revision table, **no** north arrow (the view template hides annotation), **no** scale bar (we present in 1 : 100 to 1 : 200 ranges; an exact scale bar is not what the audience needs — a *room legend* is).
5. Family Types — set:

    ```
    TB_PURPOSE_TXT             = "Plan"
    TB_PAPER_SIZE_TXT          = "A1"
    TB_ORIENTATION_TXT         = "Landscape"
    TB_DISCIPLINE_LIST_TXT     = "ARCH"
    TB_DRAWZONE_X_MM           = 30
    TB_DRAWZONE_Y_MM           = 8
    TB_DRAWZONE_W_MM           = 781
    TB_DRAWZONE_H_MM           = 486
    TB_MAX_VIEWPORTS_INT       = 1               ← ONE big rendered viewport
    TB_DRAWING_TYPE_ID_TXT     = "client-render-A1-1to100"
    TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL = 0
    TB_SHOW_KEY_PLAN_BOOL      = 0
    TB_SHOW_NORTH_ARROW_BOOL   = 0
    TB_SHOW_SCALEBAR_BOOL      = 0
    TB_SHOW_REV_TABLE_BOOL     = 0
    TB_SHOW_COMPANY_STRIP_BOOL = 1
    TB_SHOW_QR_CODE_BOOL       = 0
    ```

6. Save as `STING_TB_CLIENT_A1.rfa`.

### 9.3 Why one viewport

Because the audience cannot decode 4-up coordination layouts. One large rendered or photo-realistic plan/elevation/3D, *one* idea per sheet, plain language labels. This is opinionated — but it is what works.

### 9.4 What auto-fills at issue time

Same as the technical block: dispatcher picks the family by drawing-type id, view template applies the *Client Presentation* style pack, the engine drops a single big viewport.

---

## 10. Stage 7 — Build the construction / tender / as-built / submission title blocks

Four siblings, all derived from the technical-presentation block but with different toggles, banners and required fields. The differences are summarised below — author each as a *duplicate* of `STING_TB_TECHNICAL_A1.rfa`, change the noted values, save under the new name.

### 10.1 IFC — Issued For Construction (`STING_TB_IFC_A1.rfa`)

| Change | Value | Why |
|---|---|---|
| Drawing-type id | `construction-issue-A1-1to50` | Routes to `construction-issue` style pack |
| Status banner | "ISSUED FOR CONSTRUCTION" — solid red bar 25 mm tall across the top | Site can see the status without reading the strip |
| `TB_SHOW_REV_TABLE_BOOL` | 1 | Site needs the rev history |
| `TB_SHOW_QR_CODE_BOOL` | 1 (links to RFI portal) | Site can scan to ask an RFI |
| Required project params | `PRJ_TB_DESIGN_STAGE_TXT == "C"` (Construction) | Validator refuses if stage is still "DE" |

### 10.2 IFT — Issued For Tender (`STING_TB_IFT_A1.rfa`)

| Change | Value | Why |
|---|---|---|
| Drawing-type id | `tender-issue-A1-1to100` | Routes to `tender-issue` style pack |
| Watermark | `"ISSUED FOR TENDER"` 8 % opacity diagonal across drawable zone | Prevents the tender drawing being mistaken for a construction-issue drawing |
| `PRJ_TB_DELIVERABLE_STATUS_TXT` | `"S3"` | Tender CDE state |
| Required seals | `["LEAD ARCHITECT","STRUCTURAL ENGINEER","MEP ENGINEER"]` | Validator checks signatures |

### 10.3 As-Built (`STING_TB_AS_BUILT_A1.rfa`)

| Change | Value | Why |
|---|---|---|
| Drawing-type id | `as-built-A1-1to100` | Routes to `as-built` style pack |
| Status banner | "AS-BUILT — RECORD" green | Distinguishes record drawings |
| `TB_SHOW_REV_TABLE_BOOL` | 1 (frozen — final rev only) | Historical clarity |
| Watermark | none (record is record) | – |
| Existing-phase un-halftoned | View template handles it | New / demolished is gone — only existing (built) survives |

### 10.4 Authority submission (`STING_TB_SUBMISSION_KCCA/ERA/NEMA.rfa`)

Submission blocks already exist as `.params.txt` stubs in `Families/AssemblyTitleBlocks/`. Author them with the *additional* requirements:

| Change | Value | Why |
|---|---|---|
| `TB_AUTHORITY_CODE_TXT` | `"KCCA"` / `"ERA"` / `"NEMA"` | The validator runs authority-specific checks |
| `TB_REQUIRED_PRJ_PARAMS_TXT` | `"PRJ_PLOT_NUMBER;PRJ_LRV_NUMBER;PRJ_PHYSICAL_ADDRESS"` | Submission rejected if any are blank |
| `TB_REQUIRED_SEALS_JSON_TXT` | `[{"role":"LEAD ARCHITECT","x":600,"y":40,"w":120,"h":50}, …]` | Validator checks the seal area is populated with an image |
| `TB_AUTHORITY_FORM_VERSION_TXT` | e.g. `"KCCA-2024-Rev3"` | Lets you ship multiple authority versions in parallel |
| `TB_REQUIRED_REV_FORMAT_TXT` | `"P\d{2}"` (KCCA) / `"R\d{2}"` (ERA) | Each authority requires a different revision format |
| `TB_NESTED_GRID_BUBBLE_FAMILY_TXT` | `STING_GRID_KCCA` (or ERA / NEMA equivalents) | Some authorities require their own bubble style |

> **Layman tip:** if the validator fails on submission, it tells you exactly which `TB_REQUIRED_*` field is unsatisfied. Treat the validator as your gate-keeper, not your enemy.

### 10.5 Marketing render (`STING_TB_MARKETING_A2.rfa`)

A2 landscape (420 × 594 mm). Purpose: brochure, web, social.

| Setting | Value |
|---|---|
| `TB_PURPOSE_TXT` | `"Render"` |
| `TB_DRAWING_TYPE_ID_TXT` | `"marketing-render-A2"` |
| `TB_DRAWZONE_*` | full sheet minus 12 mm bottom strip |
| `TB_MAX_VIEWPORTS_INT` | 1 |
| `TB_SHOW_*_BOOL` | All zero except `COMPANY_STRIP_BOOL = 1` |
| Watermark | none |

The view template hides every annotation, leaves shadows on, and increases image quality. The output is one full-bleed render.

---

## 11. Stage 8 — How section annotations help automation

Section, elevation, callout and revision-cloud annotations look like graphics on a sheet — but in STING they are **wayfinding for the engine**. The engine cannot reliably guess where to drop a section bubble; you tell it once, in the title block, and the engine reuses that knowledge forever.

### 11.1 Why pre-load section annotations into the title block

Three reasons:

1. **Family resolution.** The Revit `Section.Create` API requires a `FamilySymbol` for the marker. If `STING_SECTION_M` is not in the project, the call fails. Pre-loading the family inside the title block guarantees that *every project that drops the title block also has the section marker available* — even on a freshly-spawned blank project.
2. **Discipline-correct symbology.** Mech wants the section bubble in blue with an `M-` prefix; Elec wants gold with `E-`; Plumb wants green with `P-`. By pre-loading the *discipline-tagged* nested family (`M_GridBubble`, `E_SectionMarker`, …) and storing the family name in `TB_NESTED_SECTION_MARKER_FAMILY_TXT`, the engine reads the right marker straight from the title block at runtime.
3. **Drawable-zone safety.** Section annotations placed by the engine carry their own bounding box, but *callout markers* and *revision clouds* are bigger than they look. The validator computes the *occupied area* of a sheet by summing the bounding boxes of all reserved nested-family annotations. Pre-loading them lets the validator catch overflow during pre-flight, not at issue time.

### 11.2 The seven nested-family pointers

| `TB_*` parameter | Default family value | Used by |
|---|---|---|
| `TB_NESTED_NORTH_ARROW_FAMILY_TXT` | `STING_NORTH_ARROW_STD` | Auto-rotate at sheet creation |
| `TB_NESTED_SCALEBAR_FAMILY_TXT` | `STING_SCALEBAR_METRIC` | Embedded scale bar that auto-snaps to the slot's scale |
| `TB_NESTED_KEYPLAN_FAMILY_TXT` | `STING_KEYPLAN_BUILDING` | Drawing-area highlight |
| `TB_NESTED_GRID_BUBBLE_FAMILY_TXT` | `STING_GRID_M` (mech), `STING_GRID_HVAC`, `STING_GRID_KCCA`, … | Discipline-tagged grid bubbles |
| `TB_NESTED_SECTION_MARKER_FAMILY_TXT` | `STING_SECTION_M` / `_E` / `_P` / … | Section bubble used by `BatchSections` |
| `TB_NESTED_ELEVATION_MARKER_FAMILY_TXT` | `STING_ELEVATION_M` / `_E` / `_P` / … | Elevation marker used by `BatchElevations` |
| `TB_NESTED_CALLOUT_TAG_FAMILY_TXT` | `STING_CALLOUT_M` / … | Callout tag used by `CreateCallout` |
| `TB_NESTED_REV_CLOUD_TAG_FAMILY_TXT` | `STING_REVCLOUD_STD` | Revision-cloud tag with rev letter |

### 11.3 Engine pseudocode

```csharp
// inside ShopDrawingComposer / BatchSectionsCommand
FamilySymbol marker = Resolve(
    titleBlock.LookupParameter("TB_NESTED_SECTION_MARKER_FAMILY_TXT").AsString(),
    fallbackFamilyName: "STING_SECTION_DEFAULT");

ViewSection section = ViewSection.CreateSection(doc, viewFamilyTypeId, sectionBox);
section.SetSectionMarkerSymbol(marker);     // discipline-correct, no guessing
```

### 11.4 Multi-discipline coordination title block

When the title block hosts multiple disciplines (e.g. an MEP coord sheet), prefix the nested family value with the discipline letter, comma-separated:

```
TB_NESTED_GRID_BUBBLE_FAMILY_TXT = "M_GridBubble, E_GridBubble, P_GridBubble"
```

The engine inspects the *current view's* discipline first, then picks the matching prefix. This keeps the title block universal while still rendering discipline-specific symbology.

### 11.5 Pinning nested family versions

When the title block is updated, the nested family versions are pinned to the title block's release. **`TB_TEMPLATE_VERSION_TXT` doubles as your annotation-kit version stamp** — this is the simplest, most auditable governance mechanism.

---

## 12. Stage 9 — Wire up the corporate base styles (`corp-base`)

`corp-base` is the **default visual pack** — every drawing falls back to it when no other rule matches. It lives in `StingTools/Data/STING_VIEW_STYLE_PACKS.json` (shipped alongside this guide). Treat it as your *house style*: edit it once, every project inherits it.

### 12.1 What `corp-base` defines

| Field | Default | Why |
|---|---|---|
| `viewTemplate` | `STING - Corporate Base` | Pinned view template — every "default-look" view uses it |
| `detailLevel` | `Medium` | Balance of speed and clarity |
| `scaleHint` | `1:100` | Office baseline; per-drawing-type rules can override |
| `colorScheme` | `Monochrome` | Black-on-white is the most printable, photocopy-safe palette |
| `lineWeightScale` | `1.0` | Revit base; bumped to 1.5 for fabrication, 0.8 for client |
| `halftoneLinks` | `true` | Linked Revit / DWG files are halftoned to keep them out of the way |
| `thinLines` | `false` | Thin-lines mode is for screen review, not print |
| `fillPatternForeground` | `Solid fill` | Avoids hatching the underlay |
| Annotation overrides | Grids / Levels un-halftoned at LW 1; Sections at LW 2; Reference Planes hidden | Clean look |
| Model overrides | Walls at LW 4; Doors / Windows at LW 2; Floors halftoned; Ceilings hidden in plan | Industry default for floor plans |
| Filter | `Existing - Halftone` (Phase Created == Existing) | Auto-greys the legacy fabric |

### 12.2 The full populated pack

The full populated default pack is shipped as `StingTools/Data/STING_VIEW_STYLE_PACKS.json`. Open the file in any text editor and you will find nine packs:

| id | When the dispatcher routes to it |
|---|---|
| `corp-base` | Catch-all default |
| `fabrication-shop` | Any drawing with `purpose=Spool` |
| `technical-presentation` | MEP / coordination plans |
| `client-presentation` | Anything with `purpose=client-review` |
| `construction-issue` | IFC drawings |
| `tender-issue` | IFT drawings |
| `as-built` | Handover record drawings |
| `authority-submission` | KCCA / ERA / NEMA submissions |
| `marketing-render` | Brochure / web renders |

### 12.3 How to edit `corp-base` for *your* office

1. Open `StingTools/Data/STING_VIEW_STYLE_PACKS.json`.
2. Find the pack with `"id": "corp-base"`.
3. Edit `viewTemplate`, `detailLevel`, `scaleHint`, `colorScheme`, `lineWeightScale`, `halftoneLinks` to match your office house style.
4. Adjust `modelCategoryOverrides` and `annotationCategoryOverrides` — the defaults follow ISO 19650 + RIBA Plan of Work conventions but can be replaced wholesale.
5. Run **Drawing Types ▸ Reload** in Revit to flush the cached registry.
6. Open one sheet that uses the default route and confirm the look. Iterate.

### 12.4 Project-scoped overrides

Per-project tweaks belong in `<project>/_BIM_COORD/view_style_packs.json` — same shape as the corporate file. The registry layers project entries on top of the corporate baseline (project entries win by id). This keeps the corporate file as the single source of truth while giving each project a safe place to override.

### 12.5 SHA-256 lock & drift detection

The corporate file is checksum-locked. If anyone edits it on disk, its `origin` flips from `corporate` to `project` automatically. The validator surfaces this drift on the dock-panel status bar — so you always know whether you are looking at the *shipped* corp-base or a *modified* one.

---

## 13. Stage 10 — Validate, lock, version and ship

Before any title block leaves the kit folder, it must pass the validator. The validator is the contract between *the family author* and *the engine*.

### 13.1 The five validator checks

1. **Drawable zone bbox** — `TB_DRAWZONE_*` parameters resolve to a non-zero bounded rectangle that fits inside the paper size (or are explicitly zero for `Cover` purpose).
2. **Reserved regions** — every entry in `TB_RESERVED_REGIONS_JSON_TXT` is well-formed JSON and inside the drawable zone.
3. **Viewport slots** — every entry in `TB_VIEWPORT_SLOTS_JSON_TXT` resolves to a non-overlapping rectangle inside the drawable zone, and the count is `≤ TB_MAX_VIEWPORTS_INT`.
4. **Nested families present** — every `TB_NESTED_*_FAMILY_TXT` either resolves to a loaded family in the host project or has a documented fallback (`StingLog.Warn`).
5. **Authority requirements** — if `TB_AUTHORITY_CODE_TXT` is set, every key in `TB_REQUIRED_PRJ_PARAMS_TXT` is non-empty in `ProjectInformation`, every seal in `TB_REQUIRED_SEALS_JSON_TXT` is populated, and the revision pattern in `TB_REQUIRED_REV_FORMAT_TXT` matches the current project rev.

### 13.2 Run the validator

In the dock panel: **DOCS ▸ Sheet Manager ▸ ISO Compliance**. The command runs the title-block validator first, then the wider ISO 19650 sheet-name check. Output: a `StingResultPanel` summary with one table row per family and an Errors / Warnings count.

> **Layman tip:** treat warnings as errors before issue. Warnings are the engine telling you "I will fall back, but I will lose information."

### 13.3 Lock the family

Once it passes, set:

```
TB_LAST_VALIDATED_DT_TXT = "2026-04-25"   ← today's date
TB_TEMPLATE_VERSION_TXT  = "1.0.0"
```

Save, close, drop into the project's title-block library.

### 13.4 Ship the kit

A "kit" is a folder containing every `.rfa` listed in section 4.1, plus a `manifest.json`:

```
{
  "kitName": "STING Corp v1.0",
  "kitVersion": "1.0.0",
  "validatedOn": "2026-04-25",
  "members": [
    {"file": "STING_TB_COVER_A3.rfa",        "purpose": "Cover",     "version": "1.0.0"},
    {"file": "STING_TB_STARTUP_A3.rfa",      "purpose": "Startup",   "version": "1.0.0"},
    {"file": "STING_TB_ASSEMBLY_PIPE.rfa",   "purpose": "Spool",     "version": "1.0.0"},
    {"file": "STING_TB_ASSEMBLY_DUCT.rfa",   "purpose": "Spool",     "version": "1.0.0"},
    {"file": "STING_TB_ASSEMBLY_COND.rfa",   "purpose": "Spool",     "version": "1.0.0"},
    {"file": "STING_TB_ASSEMBLY_HANGER.rfa", "purpose": "Spool",     "version": "1.0.0"},
    {"file": "STING_TB_TECHNICAL_A1.rfa",    "purpose": "Plan",      "version": "1.0.0"},
    {"file": "STING_TB_CLIENT_A1.rfa",       "purpose": "Plan",      "version": "1.0.0"},
    {"file": "STING_TB_IFC_A1.rfa",          "purpose": "Plan",      "version": "1.0.0"},
    {"file": "STING_TB_IFT_A1.rfa",          "purpose": "Plan",      "version": "1.0.0"},
    {"file": "STING_TB_AS_BUILT_A1.rfa",     "purpose": "Plan",      "version": "1.0.0"},
    {"file": "STING_TB_SUBMISSION_KCCA.rfa", "purpose": "Submission","version": "1.0.0"},
    {"file": "STING_TB_SUBMISSION_ERA.rfa",  "purpose": "Submission","version": "1.0.0"},
    {"file": "STING_TB_SUBMISSION_NEMA.rfa", "purpose": "Submission","version": "1.0.0"},
    {"file": "STING_TB_MARKETING_A2.rfa",    "purpose": "Render",    "version": "1.0.0"}
  ]
}
```

Drop the folder into your office BIM library / `\\fileserver\BIM\Templates\TitleBlocks\STING_Corp_v1.0\`. Every project loads from there.

---

## 14. Stage 11 — Day-to-day management

Title blocks are not one-and-done — they are a **living kit**. Manage them the way you manage source code.

### 14.1 Routine tasks

| Task | Frequency | How |
|---|---|---|
| Project info refresh | Every issue | DocAutomation `RefreshTitleBlocks` reads `ProjectInformation` and re-writes every `PRJ_TB_*` instance parameter |
| Revision bump | Every issue | The deliverable orchestrator increments `PRJ_TB_REVISION_NR_TXT` and adds a row to the `RevisionSchedule` |
| QR re-stamp | Every issue | `TB_QR_PAYLOAD_TXT` rebuilt from the new deliverable id |
| Validate before issue | Every issue | DOCS ▸ Sheet Manager ▸ ISO Compliance |
| Sync corp file | Monthly | The BIM coordinator reviews `STING_VIEW_STYLE_PACKS.json` drift; merges project tweaks back into corporate baseline if appropriate |
| Bump kit version | Quarterly | When a graphic / typographic / engine-visible change accumulates, bump kit semver and re-validate every member |

### 14.2 Routine pitfalls

| Pitfall | Symptom | Fix |
|---|---|---|
| Edited a label but didn't bump version | Old projects render the new look unexpectedly | Bump `TB_TEMPLATE_VERSION_TXT`; old projects pin to old version via family upgrade |
| Forgot to load nested family before issue | Validator warns "missing nested family" | Open the title block, `Insert ▸ Load Family`, save |
| Drawable zone too small for the slot JSON | Validator errors "slot N exceeds drawable zone" | Either widen the drawable zone or shrink the slot |
| Authority submission fails because seal area is empty | Validator errors "required seal not populated" | Insert the seal image into the reserved rectangle, save the project |
| `TB_NORTH_ARROW_AUTO_ROTATE_BOOL = 1` but the arrow does not rotate | The nested arrow family has its own rotation parameter overriding the title block | Open the nested family, unlock the rotation parameter, save |

### 14.3 The "I just want to issue a sheet" cheat sheet

1. Pick the right title block from the type selector (e.g. `STING - A1 - Technical Presentation`).
2. Drop viewports into the slots — or run `DOCS ▸ Sheet Manager ▸ Auto Layout`.
3. Run `DOCS ▸ Sheet Manager ▸ ISO Compliance` and fix any errors.
4. Run `BIM ▸ Issue Deliverable` — engine refreshes `PRJ_TB_*`, mints rev, stamps QR, exports PDF, posts to CDE.

That's the whole loop. Once the title block is authored and the kit is shipped, day-to-day issue is **four clicks**.

---

## 15. Reference — every `TB_` shared parameter explained

The 37 family-level title-block parameters in `GROUP 26 TBL_TITLEBLOCK` (defined in `StingTools/Data/MR_PARAMETERS.txt`).

| # | Parameter | Type | Why it exists |
|---|---|---|---|
| 1 | `TB_DRAWZONE_X_MM` | NUMBER | Drawable zone origin X from sheet origin |
| 2 | `TB_DRAWZONE_Y_MM` | NUMBER | Drawable zone origin Y from sheet origin |
| 3 | `TB_DRAWZONE_W_MM` | NUMBER | Drawable zone width |
| 4 | `TB_DRAWZONE_H_MM` | NUMBER | Drawable zone height |
| 5 | `TB_DRAWZONE_MARKER_ID_TXT` | TEXT | Mark of nested detail item that geometrically defines the drawable zone (engine reads bbox at runtime) |
| 6 | `TB_RESERVED_REGIONS_JSON_TXT` | TEXT | JSON array of rectangles the engine must not overlap (`{name,x,y,w,h,kind}`) |
| 7 | `TB_VIEWPORT_SLOTS_JSON_TXT` | TEXT | JSON array of viewport slots (`{label,normX,normY,normW,normH,viewType,scale}`) |
| 8 | `TB_DRAWING_TYPE_ID_TXT` | TEXT | Back-reference to `STING_DRAWING_TYPES.json` id |
| 9 | `TB_PAPER_SIZE_TXT` | TEXT | A0 / A1 / A2 / A3 |
| 10 | `TB_ORIENTATION_TXT` | TEXT | Portrait / Landscape |
| 11 | `TB_PURPOSE_TXT` | TEXT | Cover / Startup / Plan / Spool / Render / Submission |
| 12 | `TB_DISCIPLINE_LIST_TXT` | TEXT | Semicolon-separated discipline codes the family is approved for |
| 13 | `TB_AUTHORITY_CODE_TXT` | TEXT | KCCA / ERA / NEMA — blank for non-submission |
| 14 | `TB_MAX_VIEWPORTS_INT` | INTEGER | Capacity hint used by overflow logic |
| 15 | `TB_TEMPLATE_VERSION_TXT` | TEXT | Semver — major.minor.patch |
| 16 | `TB_LAST_VALIDATED_DT_TXT` | TEXT | ISO date of last validator pass |
| 17 | `TB_SHOW_NORTH_ARROW_BOOL` | YESNO | Visibility toggle |
| 18 | `TB_SHOW_SCALEBAR_BOOL` | YESNO | Visibility toggle |
| 19 | `TB_SHOW_KEY_PLAN_BOOL` | YESNO | Visibility toggle |
| 20 | `TB_SHOW_REV_TABLE_BOOL` | YESNO | Visibility toggle |
| 21 | `TB_SHOW_QR_CODE_BOOL` | YESNO | Visibility toggle |
| 22 | `TB_SHOW_COMPANY_STRIP_BOOL` | YESNO | Visibility toggle |
| 23 | `TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL` | YESNO | Visibility toggle |
| 24 | `TB_NORTH_ARROW_AUTO_ROTATE_BOOL` | YESNO | Engine rotates north arrow to True North |
| 25 | `TB_QR_PAYLOAD_TXT` | TEXT | Payload baked into QR (deliverable id + CDE permalink) |
| 26 | `TB_REQUIRED_SEALS_JSON_TXT` | TEXT | Submission seals expected (role + bbox) |
| 27 | `TB_REQUIRED_REV_FORMAT_TXT` | TEXT | Authority rev pattern e.g. `P\d{2}` |
| 28 | `TB_AUTHORITY_FORM_VERSION_TXT` | TEXT | Submission form version e.g. `KCCA-2024-Rev3` |
| 29 | `TB_REQUIRED_PRJ_PARAMS_TXT` | TEXT | CSV of project params that must be non-empty for submission |
| 30 | `TB_NESTED_NORTH_ARROW_FAMILY_TXT` | TEXT | Family name of nested north arrow |
| 31 | `TB_NESTED_SCALEBAR_FAMILY_TXT` | TEXT | Family name of nested scale bar |
| 32 | `TB_NESTED_KEYPLAN_FAMILY_TXT` | TEXT | Family name of nested key plan |
| 33 | `TB_NESTED_GRID_BUBBLE_FAMILY_TXT` | TEXT | Family name of nested grid bubble |
| 34 | `TB_NESTED_SECTION_MARKER_FAMILY_TXT` | TEXT | Family name of nested section marker |
| 35 | `TB_NESTED_ELEVATION_MARKER_FAMILY_TXT` | TEXT | Family name of nested elevation marker |
| 36 | `TB_NESTED_CALLOUT_TAG_FAMILY_TXT` | TEXT | Family name of nested callout tag |
| 37 | `TB_NESTED_REV_CLOUD_TAG_FAMILY_TXT` | TEXT | Family name of nested rev-cloud tag |

---

## 16. Reference — every `PRJ_TB_` shared parameter explained

The **project-level** title-block parameters bound to `ProjectInformation`. Edit once per project — every title block instance reads from these.

### 16.1 Identity

| Parameter | Use |
|---|---|
| `PRJ_TB_VARIANT_TXT` | Variant code, e.g. `A1-R` (revised), `A1-O` (original) |
| `PRJ_TB_SCHEMA_VERSION_TXT` | Schema version baseline (`1.0`) |
| `PRJ_TB_LOGO_PATH_TXT` | Absolute or project-relative path to client logo image |

### 16.2 Project parties

| Parameter | Use |
|---|---|
| `PRJ_TB_CLIENT_NAME_TXT` | Client legal entity |
| `PRJ_TB_CLIENT_ADDRESS_TXT` | Client postal address |
| `PRJ_TB_CONSULTANT_NAME_TXT` | Lead consultant (architect) |
| `PRJ_TB_CONSULTANT_ADDRESS_TXT` | Consultant postal address |
| `PRJ_TB_MEP_CONSULTANTS_NAME_TXT` | MEP sub-consultant |
| `PRJ_TB_STRUCTURAL_CONSULTANTS_NAME_TXT` | Structural sub-consultant |
| `PRJ_TB_CONTRACTOR_NAME_TXT` | Main contractor |
| `PRJ_TB_CONTRACTOR_ADDRESS_TXT` | Contractor postal address |

### 16.3 Stage and discipline

| Parameter | Use |
|---|---|
| `PRJ_TB_DESIGN_STAGE_TXT` | RIBA / ISO 19650 stage code (`DE`, `C`, `H`, …) |
| `PRJ_TB_DISCIPLINE_TXT` | Discipline label (e.g. "Mechanical") |

### 16.4 Approvals

| Parameter | Use |
|---|---|
| `PRJ_TB_DRAWN_BY_TXT` | Drafter initials |
| `PRJ_TB_DATE_DRAWN_TXT` | Date drawn |
| `PRJ_TB_CHECKED_BY_TXT` | Checker initials |
| `PRJ_TB_DATE_CHECKED_TXT` | Date checked |
| `PRJ_TB_APVD_BY_TXT` | Approver initials |
| `PRJ_TB_DATE_APVD_TXT` | Date approved |

### 16.5 Revision

| Parameter | Use |
|---|---|
| `PRJ_TB_REVISION_NR_TXT` | Current revision number (e.g. `P01`, `C02`) |
| `PRJ_TB_REVISION_DATE_TXT` | Date of current revision |
| `PRJ_TB_REVISION_DESCRIPTION_TXT` | Description of current revision |

### 16.6 Sheet identity

| Parameter | Use |
|---|---|
| `PRJ_TB_SHEET_NR_TXT` | Sheet number on this sheet |
| `PRJ_TB_TOTAL_NO_SHEETS_TXT` | Total sheets in deliverable (auto-filled by start-up page generator) |
| `PRJ_TB_PAPER_SZ_TXT` | Paper size (A0 / A1 / A2 / A3) |

### 16.7 STING extensions

| Parameter | Use |
|---|---|
| `PRJ_TB_LAST_SYNC_TXT` | Last sync timestamp (server) |
| `PRJ_TB_LAST_SYNC_BY_TXT` | Last sync user (server) |
| `PRJ_TB_LOCK_BOOL` | Per-project lock — when `1`, rev bumps require admin |
| `PRJ_TB_SHOW_KEYPLAN_BOOL` | Project-wide override for key-plan visibility |
| `PRJ_TB_SHOW_SCALEBAR_BOOL` | Project-wide override for scale-bar visibility |
| `PRJ_TB_SHOW_NORTHARROW_BOOL` | Project-wide override for north-arrow visibility |
| `PRJ_TB_SHOW_DISCBAND_BOOL` | Project-wide override for discipline-band visibility |
| `PRJ_TB_SCALE_OVERRIDE_TXT` | If non-empty, overrides the scale shown in the strip |
| `PRJ_TB_ISSUE_SUMMARY_TXT` | Multi-line summary printed on the start-up page |

### 16.8 Deliverable / CDE

| Parameter | Use |
|---|---|
| `PRJ_TB_DELIVERABLE_DATADROP_TXT` | Data-drop number (DD1, DD2, …) |
| `PRJ_TB_DELIVERABLE_STATUS_TXT` | CDE state (S0/S1/S2/S3/S4/A1/B1/…) |
| `PRJ_TB_DELIVERABLE_DUE_TXT` | Deliverable due date |
| `PRJ_TB_DELIVERABLE_CDE_TXT` | CDE container name |
| `PRJ_TB_LAST_TRANSMITTAL_TXT` | Last transmittal id |
| `PRJ_TB_LAST_TRANSMITTAL_DATE_TXT` | Last transmittal date |
| `PRJ_TB_NOTES_LEGEND_REF_TXT` | Reference to the project's notes / legend sheet |

---

## 17. Reference — required nested families

Eight nested families. Each is a **separate `.rfa`** loaded into the title block, then referenced by family-name string in the matching `TB_NESTED_*_FAMILY_TXT` parameter.

| Family file | Default name | Why nested into title block |
|---|---|---|
| `STING_NORTH_ARROW_STD.rfa` | `STING_NORTH_ARROW_STD` | Auto-rotates to True North |
| `STING_SCALEBAR_METRIC.rfa` | `STING_SCALEBAR_METRIC` | Embedded scale bar that auto-snaps to viewport scale |
| `STING_KEYPLAN_BUILDING.rfa` | `STING_KEYPLAN_BUILDING` | Building footprint with current drawing area highlighted |
| `STING_GRID_M.rfa` (and `_E`, `_P`, `_A`, `_S`, `_HVAC`, `_KCCA`) | `STING_GRID_M` etc. | Discipline-tagged grid bubble |
| `STING_SECTION_M.rfa` (and `_E`, `_P`, …) | `STING_SECTION_M` | Discipline-tagged section marker |
| `STING_ELEVATION_M.rfa` (and discipline siblings) | `STING_ELEVATION_M` | Discipline-tagged elevation marker |
| `STING_CALLOUT_M.rfa` (and discipline siblings) | `STING_CALLOUT_M` | Discipline-tagged callout tag |
| `STING_REVCLOUD_STD.rfa` | `STING_REVCLOUD_STD` | Revision cloud + rev-letter tag |

Drop the eight families into `Families/Annotations/` (or your project's annotation library). The validator finds them by family name; no GUID lookup is needed.

---

## 18. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Validator says "drawable zone marker not found" | `TB_DRAWZONE_MARKER_ID_TXT` references a Mark value that doesn't exist on any nested detail item | Add a detail item with that mark inside the title block |
| Title block placed but viewports overlap reserved areas | `TB_RESERVED_REGIONS_JSON_TXT` is malformed JSON, so the engine ignored it | Validate the JSON in any online linter; commas / quotes are common culprits |
| Validator reports "slot 3 exceeds drawable zone" | Slot rectangle (norm coords) extends past the drawable-zone boundary | Either widen the drawable zone or shrink the slot |
| North arrow does not auto-rotate | Either `TB_NORTH_ARROW_AUTO_ROTATE_BOOL = 0` *or* the nested arrow family overrides its own rotation | Set the toggle, then open the nested family and unlock the rotation parameter |
| Discipline colour band invisible | `TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL` set to 0, *or* the project-wide override `PRJ_TB_SHOW_DISCBAND_BOOL` set to 0 | Set both to 1 |
| QR code stamps wrong URL | `TB_QR_PAYLOAD_TXT` was hand-edited and didn't match the deliverable id | Don't hand-edit; let `IssueDeliverable` rebuild it |
| Authority submission fails on missing seal | The reserved seal rectangle is empty | Insert the seal image into the rectangle, save the project |
| `corp-base` view template not applied | Project-scoped override missing or `STING_VIEW_STYLE_PACKS.json` invalid | Run **Drawing Types ▸ Inspect**; the diagnostic lists every routing rule and validation issue |
| Sheet number does not auto-increment between issues | `PRJ_TB_LOCK_BOOL = 1` (project lock active) | Unlock the project (admin only) |
| Old projects render the new title-block look | The `.rfa` was overwritten without bumping `TB_TEMPLATE_VERSION_TXT` | Bump the version, save again, and rely on Revit's family-version pinning to keep old projects on the old version |

---

## 19. File map

| File | Purpose |
|---|---|
| `StingTools/Data/MR_PARAMETERS.txt` (GROUP 26) | 37 `TB_*` shared params, all with stable UUIDv5 GUIDs in Planscape namespace |
| `StingTools/Data/TITLE_BLOCK.csv` | Per-discipline default values for `PRJ_TB_*` |
| `StingTools/Data/STING_DRAWING_TYPES.json` | **40** drawing-type recipes + routing (latest, Apr 2026) |
| `StingTools/Data/STING_VIEW_STYLE_PACKS.json` | **11** visual-style packs incl. `corp-base` (full extends chain, comprehensive vgOverrides) |
| `Families/AssemblyTitleBlocks/*.params.txt` | Author-source spec for the fabrication / submission families |
| `Families/AssemblyTitleBlocks/*.rfa` | Authored title-block families (the kit, 15 members) |
| `<project>/_BIM_COORD/drawing_types.json` | Per-project override for drawing types + routing |
| `<project>/_BIM_COORD/view_style_packs.json` | Per-project override for visual style |
| `StingTools/Core/Drawing/DrawingType.cs` | POCO models — DrawingType, AnnotationRulePack (Rules + TagDepths), AutoAnnotationRule |
| `StingTools/Core/Drawing/DrawingTypeRegistry.cs` | Loader + SHA-256 corp-lock + project-merge + MigrateFromLegacy invocation |
| `StingTools/Core/Drawing/DrawingTypePresentation.cs` | The 8-step apply pipeline used by every sheet-creating command |
| `StingTools/Core/Drawing/ViewStylePack.cs` + `ViewStylePackRegistry.cs` + `ViewStylePackApplier.cs` | View-style-pack POCO + loader + applier |
| `StingTools/Core/Drawing/TitleBlockParamApplier.cs` | Resolves `${PRJ_ORG_…}` and `{disc}/{lvl}/{seq:Dn}` tokens into title-block instance parameters |
| `StingTools/Core/Drawing/DrawingTypeValidator.cs` | Pre-flight checks for missing nested families and required params |
| `StingTools/Core/Fabrication/ShopDrawingComposer.cs` | Reads `TB_VIEWPORT_SLOTS_JSON_TXT` for fabrication route |
| `StingTools/UI/DrawingTypeEditorDialog.cs` | The editor dialog (two tabs: Drawing Types + View Style Packs) |
| `StingTools/UI/ProjectAssetPicker.cs` | Live readers — title-block families, view templates, viewport types, scope boxes, section markers, dimension styles, text styles, parameter filters, tag families, levels, phases, worksets, **categories**, **taggable categories** |
| `docs/guides/DRAWING_TYPE_MANAGER_GUIDE.md` | **Companion guide** — Drawing Type Manager catalogue + automation procedures |
| `docs/guides/TITLE_BLOCK_CREATION_GUIDE.md` | This file |

---

> **Final layman summary:**
> A title block in STING is a self-describing database row. You author it once per *purpose × paper size*, you let the engine read it, and you ship the kit as a folder of `.rfa` files plus a `manifest.json`. The corporate `STING_VIEW_STYLE_PACKS.json` defines the *house style*; per-project overrides go in `_BIM_COORD/view_style_packs.json`. The validator is your gate-keeper. Day-to-day issue is four clicks because every other piece of state is wired to the family.

---

## 20. Bind a title block to a recipe via the Drawing Type Editor (latest workflow)

Title-block authoring (Stages 1–10 above) gets you a `.rfa` family in the kit. **Binding** is the act of telling the engine "for `arch-plan-A1-1to100`, use `STING_TB_TECHNICAL_A1`." Two ways:

### 20.1 Through the editor dialog (recommended)

1. Dock panel ▸ **DOCS ▸ Drawing Types ▸ Edit Types**.
2. The editor opens. Drawing Types tab is selected.
3. Type the recipe id in the search box (e.g. `arch-plan-A1-1to100`). Pick the entry on the left.
4. Right-side form: **Sheet** card ▸ **Title block family** combo. The dropdown lists every `OST_TitleBlocks` family loaded in the active project, sorted alphabetically.
5. Pick `STING_TB_TECHNICAL_A1`.
6. Footer ▸ **Save**. The dialog writes only project-origin entries to `<project>/_BIM_COORD/drawing_types.json`; the corporate baseline on disk stays pristine.

If the dropdown does not list your title-block family, it isn't loaded in the active project. Open the family from the kit folder via **Insert ▸ Load Family**, then re-open the editor (the picker re-queries on each open).

### 20.2 Through the JSON file (advanced)

Edit `<project>/_BIM_COORD/drawing_types.json`:

```jsonc
{
  "id": "arch-plan-A1-1to100",
  "origin": "project",
  "titleBlockFamily": "STING_TB_TECHNICAL_A1",
  "...": "..."
}
```

Save. Run dock panel ▸ **DOCS ▸ Drawing Types ▸ Reload** to flush the registry cache.

### 20.3 The `titleBlockParams` declarative bind

The latest workflow exposes a `titleBlockParams` field on each recipe — a map of title-block instance parameter name → value template. The engine writes these onto the title-block instance at sheet-creation time via `TitleBlockParamApplier`.

Example (from `arch-plan-A1-1to100`):

```jsonc
"titleBlockParams": {
  "Client Name":      "${PRJ_ORG_CLIENT_NAME}",
  "Project Code":     "${PRJ_ORG_PROJECT_CODE}",
  "Originator":       "${PRJ_ORG_ORIGINATOR_CODE}",
  "Company Name":     "${PRJ_ORG_COMPANY_NAME}",
  "Company Address":  "${PRJ_ORG_COMPANY_ADDRESS}",
  "Appointing Party": "${PRJ_ORG_APPOINTING_PARTY}",
  "Discipline":       "Architectural",
  "Suitability":      "S2",
  "Sheet Status":     "WIP",
  "Revision":         "P01",
  "Sheet Number":     "A-{lvl}-{seq:D3}"
}
```

Substitution kinds:

| Token | Resolved by |
|---|---|
| `${PRJ_ORG_xxx}` | Reads `ProjectInformation` parameter `PRJ_ORG_xxx` (e.g. `PRJ_ORG_CLIENT_NAME`) |
| `{disc}` / `{discipline}` | ISO discipline single-letter / full name from the resolved recipe |
| `{lvl}` | Caller-supplied level code from the level-aware overload of the dispatcher |
| `{sys}` | Caller-supplied system code (sanitised) |
| `{spool}` | `AssyParams.SPOOL_NR_TXT` for fabrication path |
| `{mark}` | Section / elevation / detail mark |
| `{seq}` / `{seq:D2}` / `{seq:D3}` / `{seq:D4}` | Zero-padded sequence number |

Edit the map in the editor's **Title-block parameter binding** card (row-per-param grid; rename-key-preserves-value).

> **Layman tip:** the title block stops needing hand-typed labels for `Client Name` / `Project Code` / etc. — those cells become parameter labels in the family and the engine fills them automatically per sheet. Author once, populate forever.

### 20.4 One picture in your head

```
        ┌─────────────────────────────────────────────────┐
        │ STING_TB_TECHNICAL_A1.rfa                       │
        │ (a family — Stages 1–10 of this guide)          │
        └─────────────────────────────────────────────────┘
                          │
                          │ TB_DRAWING_TYPE_ID_TXT
                          ▼
        ┌─────────────────────────────────────────────────┐
        │ Drawing Type "mep-coord-A1-1to50"                │
        │ titleBlockFamily: STING_TB_TECHNICAL_A1          │
        │ viewStylePackId:  corp-coordination              │
        │ slots[]:          4-up coord layout              │
        │ annotation.rules: 12 auto rules                  │
        │ titleBlockParams: 11 declarative bindings        │
        └─────────────────────────────────────────────────┘
                          │
                          │ resolved by routing
                          ▼
        ┌─────────────────────────────────────────────────┐
        │ Sheet generated — title block placed,            │
        │ slots filled, view template + style pack applied,│
        │ rules fired, params written, sheet number stamped│
        └─────────────────────────────────────────────────┘
```

For a deeper dive into the drawing-type catalogue, the 11 view style packs, the 21-vocabulary auto-annotation rules and 5 worked automation procedures, read the companion guide [`DRAWING_TYPE_MANAGER_GUIDE.md`](DRAWING_TYPE_MANAGER_GUIDE.md).
