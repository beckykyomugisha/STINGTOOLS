# STING Drawing Production System — Complete Guide

> **Who this guide is for:** anyone who has opened Revit at least once and wants to understand how STING automates drawing production — from the blank-sheet title block all the way to a stamped, scaled, annotated, ISO-compliant drawing sheet. No coding required.
>
> **What you will be able to do after reading this:** set up a title block, configure a drawing type recipe, produce a batch of coordinated sheets at the press of one button, and understand what to do when something looks wrong.
>
> **This guide consolidates:** `SLOT_TAXONOMY.md`, `TITLE_BLOCK_CREATION_GUIDE.md`, `DRAWING_TYPE_MANAGER_GUIDE.md`, `STING_MANAGED_TEMPLATES_DESIGN.md`, and `DRAWING_TEMPLATE_GUIDE.md`. All five documents remain available for historical reference, but this guide is the current, single source of truth.

---

## How to use this guide

Read the sections in order the first time. The system has four stages that build on each other — skipping one makes the next stage confusing. Once you understand the whole picture, each stage chapter works as a standalone reference.

If you are in a hurry:
- **Just want to produce sheets now** using existing corporate settings: go to Stage 3, section "Quick start: your first profile-driven sheet."
- **Need to make your own title block**: read Stage 2 in full.
- **Something looks wrong on a produced sheet**: go to the "Troubleshooting" callouts inside each stage, and the Quick Reference at the end.

---

## The Big Picture (read this first)

Imagine you run a busy kitchen. Every morning your head chef makes four decisions, once, and writes them on the whiteboard:

1. **What the plate looks like** (size, shape, where the garnish goes) — this is the **title block**.
2. **What slots on the plate are available** (main dish area, side dish spot, sauce cup corner) — these are the **slot taxonomy**.
3. **The recipe card for each dish** (what goes in it, how long it cooks, which plating standard it follows) — this is the **drawing type**.
4. **The kitchen's plating standard** (line weights, colours, which sauce goes with which dish) — this is the **view style pack**.

Once the whiteboard is written, every cook in the kitchen can produce that dish identically — without guessing, without arguing, without phoning the chef. You change one line on the whiteboard and every dish updates.

STING's drawing production system works exactly like that kitchen:

| Kitchen analogy | STING equivalent |
|---|---|
| The plate | Title block family (`.rfa`) |
| Slots on the plate | Slot taxonomy (purposeTag vocabulary) |
| Recipe card | Drawing Type (JSON record in `STING_DRAWING_TYPES.json`) |
| Plating standard | View Style Pack (JSON record in `STING_VIEW_STYLE_PACKS.json`) |
| The head chef writes the whiteboard once | BIM coordinator sets up the catalogue once |
| Every cook follows the whiteboard | Every STING batch command reads the catalogue |
| Change the whiteboard, every dish updates | Edit a recipe or pack, Sync Styles re-applies to all sheets |

**The four-decision chain, as one sentence:** STING reads the *slot vocabulary* on your *title block*, picks the right *drawing type recipe*, applies the *view style pack*, and produces the sheet — you watch.

---

## Stage 1 — Slot Taxonomy: Understanding What Spaces Exist

### What is a slot?

A slot is a named rectangle on a sheet. Think of a sheet of paper as a picture frame. Inside the frame there is the big glass area where you hang the picture — that is the **primary** slot. But there is also a small plaque at the bottom for the caption, a corner for the gallery label, maybe a mat area around the picture. Each of those areas is a slot.

In STING, every slot has two key properties:
- A **category** that controls how it is drawn in previews (solid outline, dashed, dotted).
- A **purposeTag** that tells the auto-placer *what kind of view goes here*.

When you build a title block (Stage 2) you declare which slots exist. When STING places viewports (Stage 3), it reads those slot declarations and routes the right views to the right rectangles automatically.

> **Stuck?** If you wonder "why can't I just drag viewports manually?" — you can! But STING's auto-placer routes 50 views to 50 sheets in the time it takes you to drag one. Slots are what make that automation reliable.

### The four slot types

| Category | What it means in plain English | Visual in preview | When to use it |
|---|---|---|---|
| `primary` | The main drawing area — where the plan, section, 3D or spool isometric goes | Solid outline, 5% colour fill | For the biggest rectangle on the sheet where the actual view lives |
| `auxiliary` | A supporting content area — notes panel, key plan, legend, revision table | Dashed outline, 10% colour fill | For anything that frames or explains the main view |
| `symbol` | A tiny graphic element — north arrow, scale bar, discipline chip, QR code | Dotted outline, no fill | For small families (less than 40 × 40 mm) |
| `overlay` | A transparent layer on top of another slot — RFI markup over a plan, caption over a render | Dot-dash outline, transparent | For review clouds, mark-ups, or captions that sit on top of the main view |

### Purpose tags — the vocabulary the auto-placer speaks

A purposeTag is a short label like `main-plan` or `key-plan`. The auto-placer reads it to decide "which view should I put here?" Think of purpose tags as job descriptions posted on the wall of each slot: "WANTED: one main plan view" or "WANTED: one key plan showing the building at small scale."

#### Primary tags (main drawing content)

| Purpose tag | Colour code | Plain-English description | When you see this tag on a sheet |
|---|---|---|---|
| `main-plan` | Deep blue | Full-size main drawing area — plan, 3D, section | Standard plan, RCP, section, elevation sheets |
| `main-plan-half-left` | Light blue | Left half of the sheet | When two views share the sheet side by side |
| `main-plan-half-right` | Light blue | Right half of the sheet | As above, right side |
| `quad-top-left` | Light blue | Top-left quadrant (4-up layout) | Coordination sheets with 4 views |
| `quad-top-right` | Light blue | Top-right quadrant | As above |
| `quad-bottom-left` | Light blue | Bottom-left quadrant | As above |
| `quad-bottom-right` | Light blue | Bottom-right quadrant | As above |
| `fabrication-isometric` | Magenta | Isometric view area on a spool sheet | Pipe/duct/conduit fabrication sheets |
| `rfi-sketch` | Cyan | Primary sketch area on a clarification sheet | RFI and mark-up sheets |
| `presentation-render` | Yellow | Full-bleed render (no margins) | Client render boards |
| `presentation-perspective` | Amber | Architectural perspective area | Client presentation sheets |

#### Auxiliary tags (supporting content)

| Purpose tag | Colour code | Plain-English description | Toggle parameter |
|---|---|---|---|
| `key-plan` | Green | Small thumbnail showing where this area sits in the building | `TB_SHOW_KEY_PLAN_BOOL` |
| `aerial-key` | Light green | Aerial view showing site context | — |
| `notes` | Slate | General notes panel | — |
| `discipline-legend` | Orange | Symbol legend for one discipline | — |
| `material-legend` | Light orange | Material colours / hatching key | — |
| `fire-legend` | Red | Fire compartmentation legend | — |
| `accessibility-legend` | Purple | Part M / wayfinding legend | — |
| `falls-legend` | Blue | Drainage falls key | — |
| `lighting-legend` | Amber | Luminaire schedule | — |
| `schedule` | Deep purple | Generic Revit schedule view | — |
| `bom` | Deep purple | Bill of Materials (alias of `schedule`) | — |
| `cut-list` | Dark pink | Lengths and cut summary for fabrication | — |
| `revision-history` | Red | Revit revision schedule table | `TB_SHOW_REV_TABLE_BOOL` |
| `caption` | Brown | Drawing title caption (presentation sheets) | — |
| `recipient-to` | Slate | Transmittal "To" address block | — |
| `recipient-from` | Slate | Transmittal "From" address block | — |
| `regulator-stamp` | Brown | Authority seal placeholder | — |
| `discipline-band` | Orange | Discipline-coloured banner strip | `TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL` |

#### Symbol tags (tiny graphics)

| Purpose tag | Colour code | Plain-English description | Toggle parameter |
|---|---|---|---|
| `north-arrow` | Teal | North arrow nested family | `TB_SHOW_NORTH_ARROW_BOOL` |
| `scale-bar` | Teal | Scale bar nested family | `TB_SHOW_SCALEBAR_BOOL` |
| `qr-code` | Dark grey | QR code post-export stamp | `TB_SHOW_QR_CODE_BOOL` |

#### Overlay and specialty tags

| Purpose tag | Colour code | Plain-English description |
|---|---|---|
| `spool-refs` | Dark pink | Cross-spool reference list overlaid on a fabrication sheet |
| `markup-plan` | Cyan | Review-cloud overlay on a base plan |
| `rfi-query` | Deep orange | RFI question text legend |

### Why you must understand slots before building a title block

Here is the key insight: **a slot is a promise**. When you put a slot labelled `key-plan` in your title block, you are promising STING: "I have reserved this rectangle for a key plan view." When the auto-placer runs, it looks for that promise, and if it finds it, it fills the rectangle with the right view — automatically.

If you do not declare a slot, the auto-placer cannot fill it. If you declare a slot with the wrong purposeTag, the auto-placer puts the wrong view there. Getting the slot vocabulary right once means every sheet the engine produces is correctly laid out.

---

## Stage 2 — Title Block Creation: Building the Frame

### What is a title block family and why does it matter?

In Revit, every sheet must have a title block — the printable border, the company logo strip, the project information panel, the sheet number, the revision table. The title block is a special Revit family (file extension `.rfa`) that lives inside your project and appears on every sheet that uses it.

Think of the title block as the **frame on a picture**. The frame itself does not change — it is always the same size, same colour, same style. What changes is the picture inside it (the views you place). STING treats the title block as more than decoration: it is a **map of the sheet** that the engine reads at runtime to know where it can place viewports, which cells to fill with project data, and which features to show or hide.

### BIM vs Non-BIM families — what is the difference and which do you need?

STING ships title block families in two variants:

| Variant | File name pattern | What it does |
|---|---|---|
| BIM family | `STING_TB_<SIZE>_BIM_v<MAJOR>.<MINOR>.rfa` | Auto-fills labels from shared parameters (`PRJ_ORG_*`, `TB_*`). The engine reads slot JSON from `TB_VIEWPORT_SLOTS_JSON_TXT` and fills title-block cells from Project Information automatically. |
| Non-BIM family | `STING_TB_<SIZE>_NONBIM_v<MAJOR>.<MINOR>.rfa` | Simpler layout; the engineer types project info by hand. No slot JSON. Engine still places viewports in the drawable zone but does not route by purposeTag. |

**Which one do you need?** For any project where STING will auto-produce sheets, always use the BIM family. The Non-BIM variant exists for legacy situations where hand-typed info is acceptable. On a new project, start with BIM.

Every sheet stamped by a BIM title block carries the parameter `STING_SHEET_BIM_MODE_TXT` = `"BIM"`. Audit commands flag mismatches (e.g., someone swapped a BIM family for a Non-BIM one on an existing sheet).

### The 20-stage title block workflow

This is the complete recipe for authoring one title block family from scratch. Follow every stage in order; each stage exists because skipping it breaks something downstream.

**Before you start — checklist:**

| Step | Action | Why |
|---|---|---|
| 1 | Revit 2025, 2026, or 2027 installed | STING targets these three releases only |
| 2 | STING plugin loaded (`StingTools.dll` + `StingTools.addin` in the Revit Addins folder) | Validator and placement commands live in the plugin |
| 3 | `MR_PARAMETERS.txt` set as the active Shared Parameter file in Revit (Manage > Shared Parameters > Browse) | All `TB_*` and `PRJ_TB_*` GUIDs come from this file |
| 4 | `STING_DRAWING_TYPES.json` reachable in `StingTools/Data/` | The engine reads it for routing |
| 5 | A test Revit project with 1 sheet, 1 plan view, 1 section | For dry-running the auto-placement engine |
| 6 | Company logo as `.png` at 300 dpi, transparent background, max 200 × 60 mm | Embedded in cover and project-information pages |

> **Stuck?** If you can answer "yes" to "can I open Revit, see STING in the ribbon, and pick a title block type from the Type Selector?" you are 90% through the pre-requisites.

**The 20 stages:**

**Stage 1 — Plan your title block family tree.**
You are not authoring one title block — you are authoring one family per *purpose × paper size* combination, then shipping them as a kit. A typical corporate kit has 15 families covering every deliverable surface the office produces (cover page, project info page, fabrication sheets for each discipline, technical presentation, client presentation, issued-for-construction, issued-for-tender, as-built, authority submissions, marketing).

Before opening the Family Editor, draw a table on paper: rows are purposes (Cover / Startup / Spool / Technical / Client / IFC / IFT / AsBuilt / Submission / Marketing), columns are paper sizes (A3 / A2 / A1 / A0). Mark every cell your office uses. That table is your build list.

**Stage 2 — Choose orientation and strip convention.**
STING uses a "bottom strip, both orientations" convention. The company information, sheet number, revision number, drawn-by/checked-by signatures, and scale all live in a horizontal strip across the bottom of the sheet. For landscape sheets the main drawable area sits above this strip. For A3/A4 portrait sheets, the same strip convention applies. The right-edge discipline colour band (for technical-presentation sheets) is an *additional* strip, not a change to the bottom strip.

**Stage 3 — Open the Revit Family Editor for the correct template.**
Go to `File > New > Family > Title Block`. Pick the metric template matching your paper size (`A0 metric.rft`, `A1 metric.rft`, etc.). Never start from a non-metric template if your project uses millimetres.

**Stage 4 — Set sheet size in Family Types.**
Inside the Family Editor, open `Create > Family Types`. Set the Sheet Size to the correct ISO paper size. For landscape A1: 594 × 841 mm. For portrait A3: 297 × 420 mm.

**Stage 5 — Draw the border and bottom strip.**
Draw the outer border as a thin line (0.25 mm). Draw a horizontal fill region at the bottom, 110 mm tall for A1, 90 mm for A3. This is the company information strip. Lock it to the bottom of the sheet with a dimension and a `EQ` constraint.

**Stage 6 — Insert the company logo.**
`Insert > Image`. Browse to your `LOGO.png`. Size it to fit the left side of the strip (approximately 80 mm wide). Lock its position with two dimensions — one from the left edge, one from the bottom of the strip.

**Stage 7 — Load shared parameters from `MR_PARAMETERS.txt`.**
`Manage > Shared Parameters`. Click `Browse` and navigate to `MR_PARAMETERS.txt`. You will now bind labels to the parameters in this file. Every `PRJ_TB_*` and `TB_*` parameter your title block needs lives here.

**Stage 8 — Drop the project-information labels.**
`Create > Label`. Place a label and click `Add Parameter` in the label dialog. Bind it to the shared parameter for project name (`PRJ_NAME_TXT`). Repeat for: project number, client name, CDE status, revision number, revision date, drawn-by, checked-by, approved-by, scale, sheet number, sheet name, sheet total. Use Arial Narrow or your corporate font. Set sizes: project name 14 pt bold, most other fields 8-10 pt.

**Stage 9 — Declare the drawable zone.**
This is the most important step for STING automation. The drawable zone is the rectangle inside the title block where viewports may be placed. Store its position and size in four Family Type parameters:

| Parameter | Meaning |
|---|---|
| `TB_DRAWZONE_X_MM` | Distance from left edge of sheet to left edge of drawable zone |
| `TB_DRAWZONE_Y_MM` | Distance from bottom edge of sheet to bottom edge of drawable zone |
| `TB_DRAWZONE_W_MM` | Width of drawable zone in millimetres |
| `TB_DRAWZONE_H_MM` | Height of drawable zone in millimetres |

For a cover page or startup page where no viewports are allowed, set `TB_DRAWZONE_W_MM = 0` and `TB_DRAWZONE_H_MM = 0`. The engine reads these values to refuse viewport placement on covers.

**Stage 10 — Declare the slot JSON.**
For BIM title blocks, encode the slot table in the `TB_VIEWPORT_SLOTS_JSON_TXT` family parameter. Each slot has: a label, a normalised position (0..1 over the drawable zone), a purposeTag, a view type hint, and an optional scale hint.

Example for a fabrication sheet with 5 slots:
```json
[
  {"label":"PLAN",   "normX":0.00,"normY":0.50,"normW":0.65,"normH":0.50,
   "purposeTag":"fabrication-isometric","viewType":"FloorPlan","scale":25},
  {"label":"ISO",    "normX":0.65,"normY":0.50,"normW":0.35,"normH":0.50,
   "purposeTag":"fabrication-isometric","viewType":"ThreeD","scale":25},
  {"label":"ELEV0",  "normX":0.00,"normY":0.00,"normW":0.32,"normH":0.50,
   "purposeTag":"main-plan","viewType":"Elevation","scale":25},
  {"label":"ELEV90", "normX":0.32,"normY":0.00,"normW":0.33,"normH":0.50,
   "purposeTag":"main-plan","viewType":"Elevation","scale":25},
  {"label":"3D",     "normX":0.65,"normY":0.00,"normW":0.35,"normH":0.50,
   "purposeTag":"fabrication-isometric","viewType":"ThreeD","scale":25}
]
```

The `normX/Y/W/H` values are fractions of the drawable zone (not the whole paper). So `normX=0.65` means "65% of the way across the drawable zone from the left."

**Stage 11 — Add the visibility toggle parameters.**
STING controls which features appear on each sheet using a family of `TB_SHOW_*_BOOL` parameters. Add these to your family as Family Type parameters (Integer, 0 or 1):

| Parameter | Controls |
|---|---|
| `TB_SHOW_COMPANY_STRIP_BOOL` | The company logo and info strip |
| `TB_SHOW_KEY_PLAN_BOOL` | The key plan slot |
| `TB_SHOW_NORTH_ARROW_BOOL` | The north arrow symbol |
| `TB_SHOW_SCALEBAR_BOOL` | The scale bar symbol |
| `TB_SHOW_REV_TABLE_BOOL` | The revision history table |
| `TB_SHOW_QR_CODE_BOOL` | The QR code stamp |
| `TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL` | The discipline colour band on the right edge |

Set each to `1` (visible) or `0` (hidden) in Family Types. These defaults can be overridden per sheet instance.

**Stage 12 — Nest the symbol families.**
The north arrow, scale bar, key plan thumbnail, grid bubble, section marker, elevation marker, callout tag, and revision cloud tag are all separate nested families that live inside your title block family. `Insert > Load Family` and browse to the STING family library for each. Once loaded, place them in the appropriate slot positions.

Store the nested family names in the corresponding `TB_NESTED_*_FAMILY_TXT` parameters so the engine knows which family name to expect:

| Parameter | Nested family |
|---|---|
| `TB_NESTED_NORTH_ARROW_FAMILY_TXT` | e.g. `STING_NORTH_ARROW_STD` |
| `TB_NESTED_SCALEBAR_FAMILY_TXT` | e.g. `STING_SCALEBAR_METRIC` |
| `TB_NESTED_GRID_BUBBLE_FAMILY_TXT` | e.g. `STING_GRID_M` (discipline-specific) |
| `TB_NESTED_SECTION_MARKER_FAMILY_TXT` | e.g. `STING_SECTION_M` |
| `TB_NESTED_ELEVATION_MARKER_FAMILY_TXT` | e.g. `STING_ELEVATION_M` |
| `TB_NESTED_CALLOUT_TAG_FAMILY_TXT` | e.g. `STING_CALLOUT_M` |
| `TB_NESTED_REV_CLOUD_TAG_FAMILY_TXT` | e.g. `STING_REVCLOUD_STD` |

**Stage 13 — Add the purpose and identity parameters.**
These parameters tell the engine what this title block is for:

| Parameter | Example values | What it means |
|---|---|---|
| `TB_PURPOSE_TXT` | `Cover` / `Startup` / `Spool` / `Plan` | The role of this title block in the deliverable stack |
| `TB_PAPER_SIZE_TXT` | `A1` / `A2` / `A3` | ISO paper size |
| `TB_ORIENTATION_TXT` | `Landscape` / `Portrait` | Sheet orientation |
| `TB_DISCIPLINE_LIST_TXT` | `M;E;P;A;S;FP;LV` or `PIPE` | Which disciplines may use this title block (semicolon-separated) |
| `TB_DRAWING_TYPE_ID_TXT` | `pipe-spool-A1-1to50` | The drawing type recipe this title block is bound to |
| `TB_TEMPLATE_VERSION_TXT` | `1.0.0` | Version number — bump when the engine-visible structure changes |
| `TB_MAX_VIEWPORTS_INT` | `5` for spool, `0` for cover | How many viewports this sheet may hold |

**Stage 14 — Add the reserved regions JSON (optional).**
For startup pages and any sheet where specific rectangles must not be overlapped by viewports, encode them in `TB_RESERVED_REGIONS_JSON_TXT`:
```json
[
  {"name":"sheet_index","x":8,"y":180,"w":280,"h":80,"kind":"schedule"},
  {"name":"rev_history","x":8,"y":100,"w":280,"h":70,"kind":"schedule"}
]
```
The engine reads this list and keeps those areas free when placing viewports.

**Stage 15 — Draw the revision history table.**
The revision table is a simple grid inside the company strip. Typical columns: Rev / Description / Date / By. 8 rows is enough for most deliverables (older revisions archive to the start-up page's revision schedule). Bind the row fields to `TB_REV_1_CODE_TXT`, `TB_REV_1_DATE_TXT`, etc. from `MR_PARAMETERS.txt`.

Wrap the whole table in a visibility parameter linked to `TB_SHOW_REV_TABLE_BOOL`.

**Stage 16 — Add the BIM mode marker parameter.**
Add `STING_SHEET_BIM_MODE_TXT` as a family parameter with default value `"BIM"`. This is what audit commands check to confirm the right family type is loaded on each sheet.

**Stage 17 — Add the authority-code parameter (for submission families only).**
For authority submission families (KCCA, ERA, NEMA), add `TB_AUTHORITY_CODE_TXT` with the authority name as the default. Also ensure the required project-information parameters for that authority are listed in `TB_REQUIRED_PRJ_PARAMS_JSON_TXT` — the validator reads this list and fails the pre-flight check if any are empty.

**Stage 18 — Set up the last-validated parameter.**
Add `TB_LAST_VALIDATED_DT_TXT` as a family parameter (Text, blank default). The validator writes today's date here when it passes. Any title block with a date more than one major version behind the corporate baseline shows a warning.

**Stage 19 — Save and name the family file.**
File name convention: `STING_TB_{PURPOSE}_{PAPER}.rfa`

Examples:
- `STING_TB_COVER_A3.rfa`
- `STING_TB_ASSEMBLY_PIPE.rfa`
- `STING_TB_TECHNICAL_A1.rfa`
- `STING_TB_IFC_A1.rfa`
- `STING_TB_SUBMISSION_KCCA.rfa`

Save into `Families/AssemblyTitleBlocks/` (your corporate family library folder).

**Stage 20 — Test the title block in a live project.**
Load the family into your test project: `Insert > Load Family`. Create a new sheet and select the new title block from the Type Selector. Then:
1. Run `DOCS > Drawing Types > Inspect` — confirm the validator recognises the new family and reports no critical errors.
2. Run `TitleBlock_AutoPlaceViewports` — drag a plan view onto the sheet and confirm it lands in the `main-plan` slot, not somewhere random.
3. Run `DOCS > Drawing Types > Sync Styles` — confirm the style pack applies without warnings.
4. Check every label auto-filled from Project Information.

> **Stuck?** If a label shows `???` instead of project data, the shared parameter GUID in your family does not match the GUID in `MR_PARAMETERS.txt`. Re-bind the label: click it, click the label button in Properties, remove the old parameter and re-add it from `MR_PARAMETERS.txt`.

### Naming conventions — the exact naming pattern and why it matters

The naming pattern `STING_TB_{PURPOSE}_{PAPER}` is a contract between three systems:

1. The **title block family** declares `TB_DRAWING_TYPE_ID_TXT` pointing to a recipe.
2. The **drawing type recipe** declares `titleBlockFamily` pointing to this family name.
3. The **engine** resolves the family by name from the project's loaded title-block types.

If any of the three names diverge, the engine falls back to the first available title block and logs a warning. Keep names consistent across all three systems. Never rename a family after it has been used in a project without updating both the JSON recipe and re-loading the family.

### Embedding slots in your title block — the link between Stage 1 and Stage 2

The connection between Stage 1 (the slot vocabulary) and Stage 2 (the title block) happens in `TB_VIEWPORT_SLOTS_JSON_TXT`. Each slot you declare in that JSON string must use a `purposeTag` from the Stage 1 vocabulary. The engine reads this JSON at runtime; it does not look inside the `.rfa` geometry. So:

- Getting the purposeTag right matters. `main-plan` routes plan views; `key-plan` routes key plans. Mistyping the tag means the auto-placer skips that slot.
- The normalised coordinates (0..1 over the drawable zone) let the layout work across paper sizes. If you later change the drawable zone dimensions in `TB_DRAWZONE_*_MM`, the slot positions remain correct because they are fractions, not absolute millimetres.

### Common mistakes and how to avoid them

| Mistake | Symptom | Fix |
|---|---|---|
| Not setting `TB_DRAWZONE_W_MM` | Engine cannot find the drawable zone; viewports land at sheet origin | Set all four `TB_DRAWZONE_*_MM` parameters. Set to 0 for cover pages. |
| Wrong purposeTag in slot JSON | Auto-placer skips the slot; you see "No slot found for view" in the log | Check the purposeTag against the Stage 1 vocabulary table |
| Missing nested family | North arrow / scale bar does not appear | Load the nested family into the title block family, or clear `TB_NESTED_*_FAMILY_TXT` if you are not using it |
| Version not bumped after structural change | Validator accepts stale title blocks | Bump `TB_TEMPLATE_VERSION_TXT` any time drawable zone or slot JSON changes |
| Corporate family renamed in a project | Engine falls back to first available title block | Restore the original file name or update the drawing type recipe's `titleBlockFamily` field |

### Testing your title block before you use it

Run these three tests before deploying a new title block family to your project team:

1. **Validation test**: `DOCS > Drawing Types > Inspect` — all title block entries in the catalogue should show green, not red.
2. **Auto-placement test**: create a new sheet with the title block, then run `TitleBlock_AutoPlaceViewports` with a plan view selected. The plan should land in the `main-plan` slot, not at (0,0).
3. **Fill test**: run `DOCS > Drawing Types > Sync Styles` on a view already stamped with this title block's recipe. Confirm all 10 pipeline steps complete without errors in the result dialog.

---

## Stage 3 — Drawing Type Manager: Wiring the Recipe

### What is a Drawing Type?

A Drawing Type is a recipe card for one kind of drawing. Imagine a recipe for pasta: it tells you what ingredients you need (sheet size, title block), how long to cook it (scale, detail level), what steps to follow (view template, crop strategy, annotation rules), and what the plate should look like (view style pack). Once the recipe is written, anyone can make the same pasta — every time, identically.

A Drawing Type answers every question the engine needs to produce one drawing:

| Question the engine asks | Field in the recipe |
|---|---|
| What kind of drawing is this? | `purpose` (Plan, RCP, Section, Elevation, Detail, Schedule, Spool, Coordination, Legend, 3D) |
| Which discipline? | `discipline` (A = Architectural, S = Structural, M = Mechanical, E = Electrical, P = Plumbing, etc.) |
| What paper size? | `paperSize` (A0, A1, A2, A3) |
| Which title block family? | `titleBlockFamily` |
| What scale? | `scale` (1:N integer) |
| Which view template? | `viewTemplateName` |
| How is the view cropped? | `crop` strategy |
| Where do viewports land? | `slots[]` (normalised 0..1 rectangles) |
| What gets auto-tagged and auto-dimensioned? | `annotation.rules[]` |
| How does it look (line weights, colours)? | `viewStylePackId` (links to a View Style Pack) |
| What is the sheet number format? | `sheetNumberPattern` |
| What text goes in the title block cells? | `titleBlockParams` (declarative cell-fill map) |

STING ships **40 ready-made drawing types** covering every common deliverable. You rarely start from scratch — you clone the closest match and adjust.

### The three concentric layers

The system has three layers that nest inside each other like Russian dolls:

```
ROUTING TABLE
  → picks the right drawing type based on (discipline, phase, docType)
    ↓
DRAWING TYPE
  → answers every layout and content question
  → points to a View Style Pack
    ↓
VIEW STYLE PACK
  → answers every visual-appearance question
  → inherits from a parent pack via the extends chain
```

Think of it as a **restaurant**:
- The **routing table** is the head waiter: "Table for 4, MEP coordination, please" → tells the kitchen which dish to cook.
- The **drawing type** is the menu item: "MEP Coord A1 1:50" — the dish, with all its ingredients and cooking instructions.
- The **view style pack** is the plating standard: "MEP coord plates have blue ducts, green pipes, amber electrics, halftoned walls."

One waiter, one menu, one kitchen — every plate looks the same.

### The 40 corporate drawing types — what they are and when to use each

#### Architectural (12 types)

| ID | Paper | Scale | When you use it |
|---|---|---|---|
| `arch-plan-A1-1to100` | A1 | 1:100 | Standard architectural floor plan — the most common sheet in any building project |
| `arch-rcp-A1-1to100` | A1 | 1:100 | Reflected ceiling plan — showing what you see looking up at the ceiling |
| `arch-section-A1-1to50` | A1 | 1:50 | Building section cut — shows the inside of the building from top to bottom |
| `arch-elev-A1-1to100` | A1 | 1:100 | Exterior elevation — the front, back, or side face of the building |
| `arch-detail-A3-1to20` | A3 | 1:20 | Construction detail — a zoomed-in drawing of a specific junction or connection |
| `arch-site-A1-1to500` | A1 | 1:500 | Site or context plan — the building shown in relation to the surrounding plot and streets |
| `arch-roof-A1-1to100` | A1 | 1:100 | Roof plan — looking down at the roof from above |
| `arch-floor-finishes-A1-1to100` | A1 | 1:100 | Floor finishes layout — which tiles, carpets, or materials go where |
| `arch-fire-strategy-A1-1to100` | A1 | 1:100 | Fire strategy plan — showing fire compartments, escape routes, sprinkler zones |
| `arch-accessibility-A1-1to100` | A1 | 1:100 | Accessibility plan — Part M / BS 8300 wheelchair routes, ramps, turning circles |
| `arch-interior-elev-A1-1to50` | A1 | 1:50 | Interior elevation — the finished surface of an interior wall (bathrooms, kitchens, lobbies) |
| `arch-window-schedule-A2` | A2 | — | Window schedule — a table listing every window type, size, and specification |

#### Structural (4 types)

| ID | Paper | Scale | When you use it |
|---|---|---|---|
| `struct-plan-A1-1to100` | A1 | 1:100 | Structural layout plan — showing columns, beams, slabs with their sizes and grid references |
| `struct-section-A1-1to50` | A1 | 1:50 | Structural section — detailed cross-section through a structural frame or floor build-up |
| `struct-foundation-A1-1to100` | A1 | 1:100 | Foundation plan — pad footings, strip footings, pile caps and their sizes |
| `struct-rebar-detail-A3-1to20` | A3 | 1:20 | Rebar detail — reinforcement bar arrangement for concrete elements |

#### MEP — Mechanical, Electrical, Plumbing (4 types)

| ID | Paper | Scale | When you use it |
|---|---|---|---|
| `mep-plan-A1-1to100` | A1 | 1:100 | General MEP services plan — all disciplines overlaid, used for early-stage coordination |
| `mep-coord-A1-1to50` | A1 | 1:50 | MEP coordination plan — detailed clash resolution drawing for IDR/TDR meetings |
| `mep-hvac-duct-A1-1to100` | A1 | 1:100 | HVAC ductwork plan — duct sizes, routes, air terminal locations |
| `mep-plantroom-A1-1to50` | A1 | 1:50 | Plant room layout — major mechanical equipment in a plant room at 1:50 |

#### Electrical (4 types)

| ID | Paper | Scale | When you use it |
|---|---|---|---|
| `elec-riser-A2-1to100` | A2 | 1:100 | Electrical riser diagram — vertical distribution of electrical systems floor by floor |
| `elec-power-A1-1to100` | A1 | 1:100 | Power layout plan — socket outlets, distribution boards, cable routes |
| `elec-lighting-A1-1to100` | A1 | 1:100 | Lighting layout plan — luminaire positions, circuit references, switch locations |
| `elec-fire-alarm-A1-1to100` | A1 | 1:100 | Fire alarm layout — detector, call point, and sounder positions |

#### Public Health / Plumbing (1 type)

| ID | Paper | Scale | When you use it |
|---|---|---|---|
| `plumb-drainage-A1-1to100` | A1 | 1:100 | Drainage layout plan — soil, waste, and rainwater pipe routes |

#### Fabrication / Spool (2 types)

| ID | Paper | Scale | When you use it |
|---|---|---|---|
| `pipe-spool-A1-1to50` | A1 | 1:50 | Pipe spool fabrication sheet — plan + isometric + elevations + BOM strip for one pre-fabricated pipe assembly |
| `duct-spool-A1-1to50` | A1 | 1:50 | Duct spool fabrication sheet — same format for pre-fabricated ductwork |

#### Coordination and Handover (3 types)

| ID | Paper | Scale | When you use it |
|---|---|---|---|
| `coord-clash-A1-1to50` | A1 | 1:50 | Clash report sheet — shows a specific clash location with views from multiple disciplines |
| `fm-asset-location-A1-1to100` | A1 | 1:100 | FM asset location plan — for the facilities management team, showing equipment positions for maintenance |
| `handover-A1` | A1 | — | Handover sheet — general purpose handover / O&M documentation sheet |

#### Schedules and Legends (3 types)

| ID | Paper | When you use it |
|---|---|---|
| `door-schedule-A2` | A2 | Door schedule — listing every door by mark number with its type, size, and hardware |
| `legend-A2` | A2 | General legend sheet — symbol keys, colour codes, general notes |

#### Presentation Pack — Client-Facing (5 types)

These types print in full colour with lighter line weights and no grid/dimension clutter. Route to them by setting the project phase to `PRESENTATION`.

| ID | Paper | When you use it |
|---|---|---|
| `pres-3d-axon-A1` | A1 | 3D axonometric overview with a key plan and caption block — for client steering committee meetings |
| `pres-perspective-A1` | A1 | Full-bleed architectural perspective — for brochures, planning presentations |
| `pres-exterior-elev-A1` | A1 | Exterior elevation with material callouts — for client reviews of the building facade |
| `pres-render-board-A1` | A1 | Four-up render board — four rendered images with captions for a design presentation |
| `pres-context-site-A1` | A1 | Aerial context plan with legend and caption — for planning authority submissions |

#### Clarification / RFI Pack (3 types)

| ID | Paper | When you use it |
|---|---|---|
| `clar-markup-A1` | A1 | Mark-up sheet with query log and revision strip — for issuing a formal comment on another party's drawing |
| `clar-rfi-A3` | A3 | Single-issue A3 RFI sketch — a self-contained one-page request for information |
| `clar-design-intent-A1` | A1 | Design intent sheet — plan + 3D + narrative + materials strip for explaining a design decision |

### Every field in a Drawing Type explained

Each drawing type is a JSON record. Here is every field in plain English:

| JSON field | Plain English | Example |
|---|---|---|
| `id` | The permanent name of this recipe. Never change it after it is used in a project. | `"mep-coord-A1-1to50"` |
| `name` | Human-readable label that appears in the editor picker. | `"MEP Coord A1 1:50"` |
| `description` | Two-sentence description of when to use this type. | `"MEP coordination plan at 1:50..."` |
| `purpose` | The category of drawing this produces. | `"Coordination"` |
| `discipline` | Which discipline owns this drawing (or `*` for any). | `"M"` for Mechanical |
| `phase` | Which RIBA stage or project phase this applies to (or `*` for any). | `"*"` |
| `paperSize` | ISO paper size. | `"A1"` |
| `titleBlockFamily` | The exact name of the title block family to load (must match the `.rfa` file name without the extension). | `"STING_TB_TECHNICAL_A1"` |
| `orientation` | Sheet orientation. | `"Landscape"` |
| `scale` | View scale as a 1:N integer. The engine sets `view.Scale` to this value. | `50` (meaning 1:50) |
| `detailLevel` | How much geometric detail to show. | `"Fine"` / `"Medium"` / `"Coarse"` |
| `viewTemplateName` | The name of a Revit view template to apply. Can be overridden by managed mode (see Stage 4). | `"STING - MEP Coordination"` |
| `viewportTypeName` | Which Revit viewport type to use (controls whether a viewport title strip is shown). | `"With Line"` / `"No Title"` |
| `viewStylePackId` | Which visual style pack governs this drawing's appearance. | `"corp-coordination"` |
| `sheetNumberPattern` | The formula for the sheet number. Tokens like `{disc}`, `{lvl}`, `{seq:D3}` are substituted at sheet-creation time. | `"M-{lvl}-{seq:D3}"` |
| `sheetNamePattern` | The formula for the sheet name. | `"{discipline} Coord L{lvl}"` |
| `crop` | How the view's crop box is computed. | `"ScopeBoxOrBbox"` |
| `cropMarginMm` | The breathing room around the cropped content. | `150` |
| `sectionMarker.family` | Name of the section/elevation/callout marker family. | `"STING_SECTION_M"` |
| `sectionMarker.markPrefix` | The letter prefix before the section number. | `"S"` |
| `sectionMarker.farClipMm` | How far behind the section cut the view extends. | `3000` |
| `slots[]` | List of viewport slots on the sheet (see "Slots" below). | — |
| `annotation.rules[]` | List of auto-annotation actions (see "Annotation Rules" below). | — |
| `annotation.tagFamilies` | Which tag family to use per Revit category. | `{"Mechanical Equipment": "STING_EQP_TAG"}` |
| `annotation.tagDepths` | How much information to show in the TAG7 rich tag per category (1 = name only, 10 = everything). | `{"Pipes": 5}` |
| `annotation.denseUntilScale` | If the drawing is at a scale coarser than this, skip per-element annotation (prevents clutter on overview sheets). | `100` |
| `annotation.dimensionStrategy` | How dimensions are placed. | `"Linear"` / `"Ordinate"` / `"Chain"` |
| `titleBlockParams` | A map of title-block cell names to value formulas. The engine writes each entry into the title block instance at sheet-creation time. | `{"Client Name": "${PRJ_ORG_CLIENT_NAME}"}` |
| `origin` | Whether this record came from the corporate catalogue or was overridden per-project. Set automatically; do not edit. | `"corporate"` / `"project"` |
| `extends` | If this type inherits from another type, put the parent's `id` here. The engine merges parent fields first, child fields override. | `"arch-plan-A1-1to100"` |

### How routing works — how STING picks the right type automatically

The routing table is a list of rules. Each rule is a simple "if this, then that" statement:

```
If discipline = "M" AND phase = "*" AND docType = "PLAN"
→ use drawing type "mep-plan-A1-1to100"
```

The engine walks the routing table from top to bottom and uses the **first rule that matches**. This is called "first-match-wins."

**Where the routing table lives:** at the bottom of `StingTools/Data/STING_DRAWING_TYPES.json`. It is a JSON array called `"routing"`.

**How project-specific routing works:** Project-level routing rules (from `<project>/_BIM_COORD/drawing_types.json`) are **prepended** to the corporate rules. That means your project rules always win over the corporate defaults. You can add a rule for "basement levels use 1:50 instead of 1:100" without touching the corporate catalogue.

**Conditional rules (Week 6 capability):** A routing rule can carry extra filters called "predicates." A rule fires only if ALL predicates match:

| Predicate | What it matches | Example |
|---|---|---|
| `disciplineMatches` | Regex against discipline code | `"^M$"` matches only Mechanical |
| `phaseMatches` | Regex against phase name | `"PRESENTATION"` |
| `docTypeMatches` | Regex against document type | `"SPOOL"` |
| `levelMatches` | Regex against level code | `"^B\\d+"` matches basement levels |
| `projectCodeMatches` | Regex against project code | `"^PLNS"` |

A typical use: "MEP plans on basement levels always use 1:50 instead of 1:100." Write one routing rule with `levelMatches: "^B\\d+"` and `docType: "PLAN"` → point it at a custom `mep-basement-plan-A1-1to50` recipe.

> **Stuck?** If the wrong drawing type is being applied, go to `DOCS > Drawing Types > Inspect`. Look at the routing table dump. Find the rule that covers your `(discipline, phase, docType)` combination. Is your project rule missing? Is it misspelled? The Inspect output will show you which rule matched.

### Token substitution — {disc}, {lvl}, {sys}, {spool} explained with examples

Tokens are placeholders in a pattern string. When the engine creates a sheet, it replaces every token with the real value for that sheet. Think of tokens like mail-merge fields in a letter template.

**Example:** Pattern `M-{lvl}-{seq:D3}` for a Mechanical plan on Level 02 (the third sheet produced):
→ Result: `M-L02-003`

**Full token reference:**

| Token | Replaced with | Source |
|---|---|---|
| `{project}` | Project code | Project Information parameter `PRJ_ORG_PROJECT_CODE` |
| `{originator}` | Originator code | Project Information parameter `PRJ_ORG_ORIGINATOR_CODE` |
| `{vol}` | Volume / system group | `DrawingType.IsoNaming.Volume` |
| `{type}` | Document type code | `DrawingType.IsoNaming.Type` (DR = drawing, SH = schedule, SP = spool, etc.) |
| `{role}` | Discipline role code | `DrawingType.IsoNaming.Role` (A, S, M, E, P, FP) |
| `{suit}` | Suitability code | `DrawingType.IsoNaming.Suitability` (S0–S7, A1–A5) |
| `{rev}` | Revision code | `DrawingType.IsoNaming.Revision` (P01, C01, etc.) |
| `{disc}` | Discipline letter | Derived from the view's category distribution |
| `{discipline}` | Full discipline name | Derived (e.g. `Pipe`, `Electrical`) |
| `{sys}` | System code | From the element or spool (e.g. `HWS`, `SAN`) |
| `{lvl}` | Level code | Derived from the view's level (e.g. `L02`, `GF`, `B1`) |
| `{spool}` | Spool number | From the fabrication assembly (`ASS_SPOOL_NR_TXT`) |
| `{mark}` | Section or detail mark | From the view's Mark property |
| `{seq}` | 4-digit zero-padded counter | Auto-incremented per (drawing type, level) pair |
| `{seq:D2}` | 2-digit counter | As above with 2-digit padding |
| `{seq:D3}` | 3-digit counter | As above with 3-digit padding |
| `{seq:D4}` | 4-digit counter | As above with 4-digit padding |

**In `titleBlockParams` only:** `${PRJ_ORG_PROJECT_CODE}` style references (using dollar-brace syntax) read directly from any Project Information parameter.

> **Stuck?** If your sheet number shows `--L02-DR-A-003-S2-P01` with two hyphens at the start, the `{project}` or `{originator}` token resolved to an empty string. Go to Project Information and fill `PRJ_ORG_PROJECT_CODE` and `PRJ_ORG_ORIGINATOR_CODE`.

### Project-scoped overrides — how to customise without breaking the corporate baseline

This is a critical concept: **you never edit the corporate drawing types or style packs directly.** Instead:

1. The corporate baseline lives in `StingTools/Data/STING_DRAWING_TYPES.json` — this file is locked (the engine watches its SHA-256 checksum).
2. When you edit a corporate entry in the editor dialog and click Save, the engine copies it to `<project>/_BIM_COORD/drawing_types.json` with `origin: "project"`. The corporate file on disk is untouched.
3. On the next document open, the engine loads the corporate file first, then layers the project file on top. Project entries win by `id`.

Why this matters: when the BIM manager updates the corporate drawing types next month, your project-scoped overrides survive. You do not have to re-apply your changes. The corporate update only affects entries you have NOT overridden per-project.

**The SHA-256 drift detection** is the mechanism that notices when a corporate entry has been modified. If the checksum of an entry in the loaded JSON does not match the checksum of the same entry in the on-disk corporate file, the entry's `origin` flips to `"project"` automatically. You can see this happen the first time you edit a corporate entry in the editor.

### The Drawing Type Editor dialog — every button and card explained

Open the editor from the STING dock panel: **DOCS tab > Drawing Types section > Edit Types.**

The editor has two tabs: **Drawing Types** (left tab) and **View Style Packs** (right tab). Each tab has the same three-column layout:

- **Left column:** search box, New/Clone/Delete buttons, list of types/packs ordered by discipline and purpose.
- **Right column:** a scrollable form with multiple "cards" (expandable sections).
- **Footer:** Save and Close buttons, always visible (never clipped by window resize).

#### Drawing Types tab — card by card:

**Identity card**

| Field | What to fill in |
|---|---|
| Id | Stable identifier — fill once and never change. Convention: `<discipline>-<purpose>-<paper>-<scale>` |
| Name | Human-readable label for the picker list |
| Description | Two lines explaining when to use this type |
| Purpose | Pick from the dropdown: Plan / RCP / Section / Elevation / Detail / Schedule / Spool / Coordination / Legend / 3D / Cover / Startup / Render / Submission / Clarification / ClientReview / DesignReview |
| Discipline | ISO 19650 single-letter code or `*` for any |
| Phase | RIBA stage number (0–7) or `*` |
| Origin | Read-only — shows `corporate` or `project`. You cannot edit this. |

**Sheet card**

| Field | What to fill in |
|---|---|
| Paper size | A0 / A1 / A2 / A3 / A4 |
| Title block family | Pick from the dropdown — populated from title block families loaded in the active project |
| Orientation | Landscape / Portrait |

**Views card**

| Field | What to fill in |
|---|---|
| Scale | The 1:N scale (type the number N, e.g. `50` for 1:50) |
| Detail level | Coarse / Medium / Fine |
| View template name | Pick from the dropdown (live list of templates in the project, merged with STING corporate templates) |
| Viewport type name | Pick from the dropdown (controls whether viewport title strip is shown) |
| View style pack id | Pick which visual style pack to use (see Stage 5 for the full pack list) |

**Numbering card**

| Field | Supported tokens |
|---|---|
| Sheet number pattern | `{project}`, `{originator}`, `{vol}`, `{lvl}`, `{type}`, `{role}`, `{seq}`, `{seq:D2}`, `{seq:D3}`, `{seq:D4}`, `{disc}`, `{discipline}`, `{sys}`, `{mark}`, `{spool}`, `{suit}`, `{rev}` |
| Sheet name pattern | Same token set |

The dropdown is pre-populated with five common ISO 19650 patterns. You can also type your own.

**Crop card**

| Crop kind | What happens |
|---|---|
| `ScopeBox` | Uses the named scope box; shows an error if the scope box is missing |
| `ScopeBoxOrBbox` | Uses the scope box if one exists; otherwise computes a tight bounding box of model elements with your margin |
| `TightBbox` | Always uses the bounding box of model elements, plus your margin |
| `RoomBoundary` | Uses the union of room boundaries (plan views only; falls back to TightBbox if no rooms are placed) |
| `None` | Leaves the view's crop box exactly as it is |

Margin field: the extra breathing room in millimetres added around the crop boundary.

**Section/Elevation marker card**

Bind a section marker, elevation marker, or callout marker family from the live list. Set:
- Family name (dropdown of `OST_SectionHeads / OST_ElevationMarks / OST_CalloutHeads` element types)
- Mark prefix (the letter before the number, e.g. `S` for sections, `EL` for elevations, `D` for details)
- Far clip distance: how far behind the cut plane the section view extends. 3000 mm is the default; raise it for large-volume sections like atria.

**Annotation rule pack card**

This is the most powerful card — it tells the engine what to auto-tag and auto-dimension.

Three sub-sections:

1. **Automation rules grid** — one row per action. Columns: Enabled tick, Category (dropdown), Rule type (dropdown of 21 types — see table below), Tag family override, Depth, Delete. Click `+ Add rule` to add a row.

2. **Dimension settings** — Strategy (Linear / Ordinate / Chain), dimension style name (dropdown), Dense-until-scale threshold.

3. **Tag families grid** — one row per category override. Columns: Category, tag family name, TAG7 paragraph depth (1–10).

**The 21 annotation rule types:**

| Rule type | Plain English |
|---|---|
| `AutoTag` | Place a default tag on every visible element in this category |
| `AutoDim` | Run an automatic dimension chain |
| `AutoDimOrdinate` | Ordinate-style automatic dimension |
| `AutoDimChain` | Continuous chain dimension |
| `AutoTagWithLeader` | AutoTag but force a leader line on every tag |
| `AutoTagHideIfEmpty` | AutoTag but skip elements where the tag value would be blank |
| `AutoTagTypeMark` | Tag using the Type Mark parameter (not instance mark) |
| `AutoTagRoomName` | Tag rooms showing name only |
| `AutoTagRoomNumber` | Tag rooms showing number only |
| `AutoTagDoorNumber` | Door schedule mark tag |
| `AutoTagWindowMark` | Window schedule mark tag |
| `AutoTagEquipmentTag` | Equipment tag (mechanical / electrical / plumbing equipment) |
| `AutoTagGridBubble` | Place grid bubbles at every visible grid intersection |
| `AutoDimWallLength` | Linear dimension along every wall |
| `AutoDimColumnGrid` | Column-to-column grid spacing dimensions |
| `AutoDimOpenings` | Dimension door and window openings |
| `AutoDimElevation` | Floor and ceiling elevation dimensions |
| `AutoAnnotateSlope` | Add slope annotation on pipes, drainage, ramps, or roofs |
| `AutoAnnotateFlowArrow` | Add flow direction arrows on ducts and pipes |
| `AutoAnnotateSpaceNumber` | Number every space (HVAC zone numbering) |
| `AutoAnnotateAreaBoundary` | Tag area boundary lines |

**Slots card**

Each slot is a named rectangle on the sheet's drawable zone. Add as many as needed.

| Column | What to fill in |
|---|---|
| Label | A descriptive name, e.g. "Main Plan", "Key Plan", "Notes" |
| ViewType | Which type of Revit view goes in this slot (FloorPlan / CeilingPlan / Section / Elevation / ThreeD / Detail / DraftingView / Legend / Schedule) |
| X | Left edge of slot, as a fraction of drawable zone width (0.0 = far left, 1.0 = far right) |
| Y | Bottom edge of slot, as a fraction of drawable zone height (0.0 = bottom, 1.0 = top) |
| W | Width as a fraction of drawable zone width |
| H | Height as a fraction of drawable zone height |
| Scale | Optional per-slot scale override (overrides the drawing type's main scale) |

**Title-block parameter binding card**

One row per title-block cell. Columns: cell name (the parameter name on the title-block instance), value template (which tokens to write).

Example row: `| Client Name | ${PRJ_ORG_CLIENT_NAME} |`
This writes the project's client name from Project Information into the title block's `Client Name` parameter when the sheet is created.

**Save semantics:** Clicking Save writes only entries with `origin: "project"` to `<project>/_BIM_COORD/drawing_types.json`. The corporate file on disk is never changed. If you edit a corporate entry and click Save, the entry's origin flips to `"project"` automatically and your edits land in the project file.

### Scope-box auto-binding — the STING:: naming trick

This is the fastest way to produce a coordinated set of plans:

1. In a plan view, go to `View > Scope Boxes` and create scope boxes covering each zone or level area you need drawings for.
2. Name each scope box using this pattern: `STING::<drawing-type-id>::<level-code>::<optional-tag>`

   Examples:
   ```
   STING::arch-plan-A1-1to100::L01
   STING::arch-plan-A1-1to100::L02
   STING::mep-coord-A1-1to50::L01::east-wing
   STING::mep-coord-A1-1to50::L01::west-wing
   STING::pipe-spool-A1-1to50::L03::HWS
   ```

3. Go to **DOCS > Drawing Types > From Scope Boxes** in the STING dock panel.
4. Click the button. The engine scans every scope box, finds the ones named with the `STING::` prefix, creates a view for each, applies the matching drawing type recipe, and crops the view to the scope box boundary.
5. The command is **idempotent** — if you run it again, it finds the existing stamped views and re-applies the profile instead of creating duplicates.

> **Stuck?** If "From Scope Boxes" did nothing, check that your scope box names match the pattern exactly. The `::` separators must be double colons. The drawing type id must match exactly — copy it from the editor list rather than typing it.

### SyncStyles — what drift is and how to fix it

**What is drift?** Drift happens when a view no longer matches the recipe that created it. This can happen because:
- Someone changed the view's scale by hand.
- Someone changed the view template manually.
- The BIM coordinator updated a drawing type recipe after the view was created.
- A style pack was edited after the view was produced.

Think of drift like a piece of furniture that has been moved from its designated spot. It does not belong where it is. Sync Styles puts it back.

**How to detect drift:**
1. Click **DOCS > Drawing Types > Inspect**. The headline shows "Drift: N view(s) drifted."
2. A drifted view is one whose `STING_DRAWING_TYPE_ID_TXT` stamp points to a recipe, but the view's actual scale / detail level / template no longer matches that recipe.

**How to fix drift:**
1. Click **DOCS > Drawing Types > Sync Styles**.
2. A preview dialog shows the first 10 drifted views.
3. Click Confirm. The engine re-applies each recipe's settings inside one Revit Transaction. Annotation pass is skipped (to avoid re-tagging already-tagged views).
4. Views with `STING_STYLE_LOCKED_BOOL = 1` are skipped — use this to protect hand-tuned views.

> **Stuck?** If Sync Styles ran but the view still looks wrong, check whether the view has an active view template that locks the categories the pack is trying to override. An active template's locked settings beat the pack's overrides. Either remove the lock on those categories in the template, or (in managed mode — see Stage 4) let STING manage the template itself.

### The pipeline order (Steps 1–10) explained in plain English

When STING produces a sheet, it runs these 10 steps in order. Knowing the order tells you which step to investigate when something looks wrong:

| Step | What happens | Where to look if it fails |
|---|---|---|
| 1 | Lock check — if the view is locked (`STING_STYLE_LOCKED_BOOL = 1`), skip the whole pipeline for this view | Check the lock parameter on the view |
| 2 | Stamp the drawing type id — write `STING_DRAWING_TYPE_ID_TXT` to the view so the browser organizer and Sync Styles know which recipe owns this view | Usually never fails |
| 3 | Set scale — `view.Scale = recipe.scale` | Check that the recipe's scale is a valid Revit scale |
| 4 | Set detail level — Coarse / Medium / Fine | Check recipe's `detailLevel` field |
| 5 | Apply view template — look up the template by name and apply it | Check the template name exists; validator will warn if it is missing |
| 6 | Apply crop strategy — compute crop boundary (scope box, bounding box, room boundary, or none) | Check scope box exists if using `ScopeBox` strategy |
| 7 | Apply view style pack — graphic overrides, filters, line weights, colours | Check the pack's `extends` chain for missing entries |
| 8 | Annotation pass — run auto-dim and auto-tag rules | Check tag family names exist; check category names are spelled correctly |
| 9 | (Sheet creation only) Stamp the sheet — write the drawing type id to the sheet itself | Usually never fails |
| 10 | (Sheet creation only) Fill title-block cells — write every entry in `titleBlockParams` into the title-block instance | Check parameter names match the title-block family's shared parameter names |

Every step is wrapped in a `try/catch` — a failure in one step becomes a warning, not a crash. The engine reports warnings in the result dialog after the run. Read the warnings from top to bottom; fix the first one, re-run, repeat.

---

## Stage 4 — Managed View Templates: Letting STING Take the Wheel

### Why managed templates exist (the problem they solve)

Revit view templates are the traditional way to control how views look. A view template is a saved collection of graphic overrides — which categories are visible, what colour, what line weight, what filters are on. When you apply a template to a view, the view looks like the template.

The problem: **hand-authored view templates drift, diverge, and multiply.** Here is the typical failure pattern:

1. Jane creates view template "STING - MEP Coordination" and sets duct colour to blue, pipe to green.
2. John opens another project, cannot find Jane's template, creates "MEP Coordination Plan" with duct colour dark blue, pipe colour teal.
3. The BIM coordinator decides pipe should be lime green. They open 6 templates across 4 projects and change it in each. They miss one.
4. Three months later, nobody remembers which templates are authoritative. Two new team members create two more templates.

**How managed mode solves this:**

In managed mode, you do not hand-author view templates in Revit at all. Instead:

1. You author a **View Style Pack** in the STING editor (a JSON record describing all the visual settings).
2. You flip the pack's `templateMode` switch to `managed`.
3. STING automatically generates a Revit view template named `STING:<pack-id>:<ViewType>` in your project.
4. Whenever a drawing type uses that pack, STING binds the view to the auto-generated template.
5. When you later edit the pack in the editor and click Save, the auto-generated template is updated to match — instantly, without you having to open the view templates dialog in Revit.
6. If someone manually edits the auto-generated template in Revit, the drift detector notices the mismatch and Sync Styles fixes it on next run.

The result: **one JSON pack → one auto-generated Revit template, always in sync.** You never open Revit's Visibility/Graphics dialog to manage templates again.

### Managed mode vs External mode — when to use each

Each view style pack carries a `templateMode` setting with two options:

| Mode | What it means | When to use it |
|---|---|---|
| `managed` | STING auto-generates and maintains the Revit view template. Pack JSON is the source of truth. | For all new packs and most corporate work. Use this for maximum consistency. |
| `external` | Pack names a Revit template by name. STING applies VG overrides on top of it. | For legacy projects where a team has already invested heavily in custom Revit templates, or for Revit template features STING does not yet model (e.g. sun settings, photographic exposure). |

> **Layman tip:** if you are starting a new project, use `managed`. If you are adopting STING on a project that already has 30 hand-crafted templates the structural team spent weeks on, use `external` to keep their work and layer STING's automation on top.

### How STING auto-generates and maintains Revit view templates

The `ManagedTemplateSyncer.EnsureTemplate(doc, pack, viewType)` function is the engine behind managed mode. Here is what it does, in plain English:

**When the template does not yet exist in the project:**
1. Create a stub Revit view of the correct type (e.g. a FloorPlan for a plan view, a Section for a section).
2. Detach it from any active template.
3. Apply all the pack's visual settings: VG overrides, filter overrides, detail level, phase filter, visual style, view range, underlay, annotation crop, far clip, etc.
4. Stamp the template with two shared parameters: `STING_PACK_ID_TXT` (the pack's id) and `STING_PACK_CHECKSUM_TXT` (a hash of the pack's JSON content).
5. Convert the stub view into a Revit view template.
6. Cache the template's ElementId for reuse.

**When the template already exists and matches (no drift):**
- Check the checksum. If `STING_PACK_CHECKSUM_TXT` matches the current pack JSON, do nothing. This is a no-op — very fast.

**When the template exists but has drifted (someone edited it in Revit's UI, or the pack was updated):**
- Re-apply only the fields listed in `managedFields` (see next section).
- Update the checksum stamp.
- The template is now in sync with the pack again.

STING generates **one template per (pack-id, view type) pair**. The template names follow this pattern:
- `STING:corp-coordination:Plan`
- `STING:corp-coordination:Section`
- `STING:corp-fabrication-shop:Plan`
- `STING:corp-standard-plan:Plan`

These templates appear in the Revit Project Browser under the Templates section, prefixed with `STING:`. You can see them there, but you should not edit them by hand — any manual edits will be overwritten by the next `EnsureTemplate` call.

### The fields STING controls (the managed fields whitelist)

One of the smartest features of managed mode is the `managedFields` list. This is a list of which settings STING owns and will overwrite on sync. Any setting NOT in this list is left alone — you can tweak it manually in Revit's template UI and STING will not touch it.

| Managed field | What it controls | Which view types |
|---|---|---|
| `vg` | All Visibility/Graphics overrides (category line weights, colours, patterns, halftone, transparency) | All |
| `filters` | Which parameter filters are applied and their VG overrides | All |
| `detailLevel` | Coarse / Medium / Fine | All |
| `discipline` | Architectural / Structural / Mechanical / Electrical / Plumbing / Coordination | All |
| `visualStyle` | Wireframe / Hidden Line / Shaded / Consistent Colors / Realistic | All |
| `phaseFilter` | Which phase filter is active (Show All, Show New, Show Complete, etc.) | All |
| `phaseName` | Which project phase the view is set to | All |
| `annotationCrop` | Whether the annotation crop box is separate from the model crop box | All |
| `farClipMm` | How far behind the cut plane a section or elevation extends | Sections, Elevations |
| `viewRange` | The top, cut plane, bottom, and view depth of a plan view | Plans |
| `underlay` | Whether to show a level below as an underlay, and whether it is halftoned | Plans |

**Tip:** the `managedFields` list can be configured per pack. If you only want STING to control VG and filters but leave phase and view range to the project team, remove those fields from the list. STING will control only what you tell it to.

### Drift detection — what it is, how to read it, what to do

Drift detection for managed templates adds a new drift kind to the existing detector: `MANAGED_TEMPLATE`.

A `MANAGED_TEMPLATE` drift is detected when:
1. A view is bound to a STING-managed template (identifiable by `STING_PACK_ID_TXT` on the template).
2. The template's `STING_PACK_CHECKSUM_TXT` no longer matches the current pack JSON checksum.

This means the pack was edited after the template was generated, but the template has not been updated yet.

**How to read drift in the Inspect output:**

When you run `DOCS > Drawing Types > Inspect`, look for lines like:
```
MANAGED_TEMPLATE drift: 3 template(s)
  - STING:corp-coordination:Plan (checksum mismatch)
  - STING:corp-standard-plan:Plan (checksum mismatch)
  - STING:corp-fabrication-shop:Plan (checksum mismatch)
```

This means three managed templates need to be regenerated from their packs.

**What to do:** Click `DOCS > Drawing Types > Sync Styles`. The sync engine detects `MANAGED_TEMPLATE` drift and calls `EnsureTemplate` on each drifted template with the updated pack settings. The templates are updated in one Transaction.

Alternatively, use `Regenerate Pack Templates` (see command table below) to force-regenerate all managed templates for a specific pack, regardless of drift status.

### Step-by-step: Converting a pack to Managed mode

Follow these steps to switch a View Style Pack from `external` mode to `managed` mode:

1. Go to **DOCS > Drawing Types > Edit Types** in the STING dock panel.
2. Click the **View Style Packs** tab.
3. Find the pack you want to convert in the list on the left (e.g. `corp-coordination`).
4. On the **Identity card**, find the `Template Mode` toggle. Switch it from `External` to `Managed`.
5. New fields appear below (they are hidden in external mode): Visual Style, View Discipline, Phase Filter, Phase, View Range sub-card, Far Clip, Annotation Crop, Managed Fields multi-select.
6. Fill in the managed-mode fields (see section below).
7. Click **Save**.
8. Alternatively, click the **Convert Pack to Managed** button in the toolbar above the pack form. This button opens a picker: "Choose an existing Revit template to import settings from." STING reads the chosen template's settings and populates the pack fields, then sets `templateMode = managed` and removes the `ViewTemplate` name pointer. The original Revit template is renamed to `<original-name>_legacy` for safety.

> **Stuck?** After converting a pack to managed mode, the pack's `ViewTemplate` field will be blank (or show "(STING-managed)"). This is correct — STING now generates the template from the JSON. If you see "template not found" warnings, run `Regenerate Pack Templates` to force-create the templates in the active project.

### Step-by-step: Detaching from Managed mode

Sometimes you want to hand the template back to Revit and stop STING managing it:

1. In the **View Style Packs** tab of the editor, find the managed pack.
2. Click the **Detach from STING Management** button in the toolbar.
3. A dialog asks: "Create a new Revit view template from current pack settings? Yes / No." Click Yes.
4. STING creates a new plain Revit view template named `<pack-name> (Detached)`, copies the managed template's current settings into it, and switches the pack's `templateMode` to `external`, pointing `ViewTemplate` at the new detached template.
5. Click Save.

From this point, the view template is a regular Revit template. STING still applies it (because `ViewTemplate` field points to it) but will not overwrite manual edits.

### Step-by-step: Regenerating pack templates

If you want to force STING to regenerate all managed templates for a pack (for example, after a major pack edit or on a new project where the templates do not yet exist):

1. In the **View Style Packs** tab, select the pack.
2. Click the **Regenerate Pack Templates** button in the toolbar.
3. A confirmation dialog shows which templates will be created or updated.
4. Click Confirm.
5. STING creates or updates `STING:<pack-id>:Plan`, `STING:<pack-id>:Section`, `STING:<pack-id>:Elevation`, `STING:<pack-id>:3D`, `STING:<pack-id>:Detail`, and `STING:<pack-id>:Drafting` in the active project.

> **Stuck?** If regeneration fails with "cannot create view template," check that the project does not already have a non-STING template with the same name (`STING:corp-coordination:Plan`). If it does, rename or delete it before regenerating.

### The two shared parameters STING stamps (STING_PACK_ID_TXT, STING_PACK_CHECKSUM_TXT) — what they mean

Every STING-managed view template carries two shared parameters stamped on it:

| Parameter | What it stores | What it looks like |
|---|---|---|
| `STING_PACK_ID_TXT` | The id of the View Style Pack that owns this template | `corp-coordination` |
| `STING_PACK_CHECKSUM_TXT` | A SHA-256 hash of the pack's JSON, limited to the `managedFields` content | `a3f7c9d2e1b8...` (64 hex characters) |

**How the checksum works in plain English:**

Think of the checksum like a fingerprint of the pack's settings. When STING generates the template, it takes a fingerprint of the pack JSON and stamps it on the template. Next time STING checks the template, it takes a new fingerprint of the pack JSON and compares it to the stamped fingerprint. If they match, the template is up to date. If they differ, the pack was edited — the template needs to be updated.

This is exactly like the "file integrity check" on a downloaded software installer — you verify the file has not changed by checking its hash.

You can read the `STING_PACK_CHECKSUM_TXT` parameter on any managed template in Revit's Properties panel (switch to the template view and look in instance properties). It is a 64-character hexadecimal string. You do not need to read or edit it manually — it is managed by the engine.

### Editor controls for managed mode — every field in the View Style Packs tab explained

When `templateMode = managed`, additional fields appear in the Appearance card of the View Style Packs tab:

| Field | What it does | Typical value |
|---|---|---|
| Visual Style | Controls how geometry is rendered — how "3D" the view looks | `HiddenLine` for production drawings; `Shaded` for presentation; `Wireframe` for coordination |
| View Discipline | Filters which categories are visible by discipline | `Architectural`, `Structural`, `Mechanical`, `Electrical`, `Plumbing`, `Coordination` |
| Phase Filter | Which phase filter is active | `Show All`, `Show New`, `Show Complete`, `Show Previous + New` |
| Phase | Which project phase the view is set to | `New Construction`, `Existing`, `Demolition` |
| View Range sub-card | Sets the cut plane height (where the view is sliced horizontally) and the view depth (how far down the view extends) | Cut plane: 1200 mm above level. Bottom: 0 mm. View depth: -300 mm. Only applies to plan views. |
| Far Clip (mm) | How far behind the cut the view looks in sections and elevations | `30000` mm for exterior elevations; `5000` mm for interior sections |
| Annotation Crop | Whether the annotation crop box is independent of the model crop box | `true` (separate annotation crop) or `false` (linked) |
| Managed Fields multi-select | Which settings STING owns and will re-apply on sync. Uncheck any field you want to control manually. | Default: all managed fields ticked |

> **Important:** `displayOptions` (shadows, sketchy lines, ambient shadows) are flagged as warnings in the editor because Revit's API does not expose them programmatically. These must still be set by hand in the Revit view template dialog if you need them.

### When things go wrong — MANAGED_TEMPLATE drift kind explained

There are four drift kinds the detector can report. Here is what each one means and what to do:

| Drift kind | What it means | Fix |
|---|---|---|
| `SCALE` | View's scale does not match the recipe's `scale` field | Click Sync Styles |
| `DETAIL` | View's detail level does not match the recipe's `detailLevel` field | Click Sync Styles |
| `TEMPLATE` | View's active template is not the one the recipe specifies | Click Sync Styles |
| `MANAGED_TEMPLATE` | A STING-managed template's checksum does not match the current pack JSON | Click Sync Styles, or click Regenerate Pack Templates for that pack |
| `TOKEN_PROFILE_DRIFT` | View's tag style or segment mask does not match the recipe's token profile | Click Sync Styles |

After clicking Sync Styles, run Inspect again to confirm drift count dropped to 0. If it did not, check the log file (`StingTools.log`) for warnings from `ManagedTemplateSyncer` — they usually name the specific field that could not be applied and why.

---

## Stage 5 — View Style Packs: Controlling How Drawings Look

### What is a View Style Pack?

A View Style Pack is the "plating standard" from the kitchen analogy. It answers every visual question:
- How thick are the wall lines?
- What colour are the ducts?
- Are existing elements shown halftoned?
- What text style is used for dimensions?
- Which categories are visible and which are hidden?

By separating visual rules from layout rules (which are in the Drawing Type), STING allows many drawing types to share one set of visual rules. You have 40 drawing types but only 11 visual packs — because most plan drawings share the same visual standard, and most fabrication drawings share another.

### The 11 corporate packs — table with id, name, and purpose

| Pack id | Extends | Purpose | What makes it distinctive |
|---|---|---|---|
| `corp-base` | (root — inherits nothing) | The corporate house style baseline, shared by all other packs | Line weight 1.0 baseline; 2.5 mm Arial Narrow text; ISO 13567 monochrome palette; 5 universal phase filters (Existing / Demolished / New / Temporary / Out-of-Scope); grids and levels visible at weight 3; reference planes hidden |
| `corp-standard-plan` | `corp-base` | Architectural plan | Walls cut at weight 5; doors and windows at weight 2; floors halftoned; ceilings hidden; rooms transparent fill at 80%; structural columns cut at weight 5; pipes and ducts hidden |
| `corp-standard-rcp` | `corp-standard-plan` | Reflected ceiling plan | Walls halftoned; floors hidden; ceilings shown at weight 3; lighting at weight 2; air terminals blue weight 2; mechanical equipment shown |
| `corp-standard-section` | `corp-base` | Building section | Walls cut at weight 5; floors cut at weight 4; structural columns cut at weight 5; insulation halftoned; rooms and furniture hidden |
| `corp-standard-elevation` | `corp-base` | Exterior elevation | Walls at weight 3; structural columns at weight 4; topography halftoned; curtain panels and mullions at weight 2 |
| `corp-standard-detail` | `corp-base` | Construction detail | Text 2.0 mm; walls cut at weight 6; structural framing cut at weight 5; rebar red at weight 2; insulation halftoned |
| `corp-clarification` | `corp-base` | RFI and mark-up sheets | RFI query filter: red weight 3; design intent filter: blue weight 2; generic annotations: red weight 2 |
| `corp-coordination` | `corp-base` | MEP coordination | Ducts: blue weight 3; pipes: green weight 3; electrical: amber weight 3; mechanical equipment: purple weight 3; fire alarm: red weight 3; structural: halftoned purple |
| `corp-fabrication-shop` | `corp-coordination` | Fabrication spool shop drawing | Text 2.0 mm Shop font; ordinate dimensions; pipes weight 5; ducts weight 5; walls and structural framing hidden |
| `corp-presentation-rich` | `corp-standard-plan` | Internal design review (IDR / TDR) | Text 3.0 mm Presentation font; walls deep slate cut at weight 6; floors light grey; rooms desaturated; topography halftoned green; grids and dimensions hidden |
| `corp-presentation-mono` | `corp-presentation-rich` | Client greyscale presentation | All colours collapse to greyscale (walls black, floors light grey, furniture mid grey) |

### Pack inheritance — the extends chain explained

Think of inheritance like a family tree of style rules. `corp-base` is the great-grandparent. Every other pack is a child or grandchild that inherits all the great-grandparent's rules and adds its own.

When the engine applies a pack, it walks the `extends` chain from root to leaf:
1. Apply `corp-base` settings.
2. Apply `corp-standard-plan` settings (overriding any `corp-base` settings where they clash).
3. (If the view is a reflected ceiling plan) Apply `corp-standard-rcp` settings.

A child pack only needs to record the fields that **differ** from its parent. Everything else is inherited silently.

**Why this matters in practice:** If the BIM coordinator decides next month that all drawings should use 2.8 mm text instead of 2.5 mm, they edit **one field in `corp-base`** — and every drawing type that descends from it automatically inherits the change. That is 40 drawing types updated with one edit.

Resist the urge to duplicate settings from the parent into the child. Every field you duplicate is one more place to maintain.

> **Layman tip:** if you find yourself editing 11 packs to make the same change, you have put the setting in the wrong pack. Move it to `corp-base`.

### Corporate vs project packs — the SHA-256 drift detection explained in plain English

The SHA-256 mechanism that protects the corporate packs works exactly like a digital tamper seal on medicine packaging:
- When the corporate catalogue ships, every pack has its "seal" computed (a 64-character hash of the pack's JSON content).
- When the engine loads the catalogue, it computes the seal again and compares it to the stored value.
- If they match, the entry is `origin: "corporate"` — untouched.
- If they differ, someone has edited the corporate file directly — the entry's origin flips to `origin: "project"`.

**Why you cannot accidentally corrupt the corporate baseline:** even if you edit `STING_VIEW_STYLE_PACKS.json` directly on disk, the engine detects the mismatch on next load and treats the changed entries as project-level overrides. The "corporate" designation is re-earned only when the BIM manager deliberately updates the corporate file and bumps the kit version.

**What happens to your project when corporate updates:**
- Corporate packs you have NOT overridden per-project: they update automatically.
- Corporate packs you HAVE overridden per-project: your override stays; the corporate update does not reach those packs.
- To pick up a corporate update for an overridden pack: delete your project override for that pack, reload, then re-apply only the project-specific fields.

### Editing a pack via the editor — every card explained

All pack editing happens in the **View Style Packs** tab of the Drawing Type Editor (`DOCS > Drawing Types > Edit Types`).

**Identity card**

| Field | What to fill in |
|---|---|
| Id | Stable identifier — never change once a pack is in use |
| Name | Human-readable label |
| Description | Two-line synopsis of what this pack is for |
| Extends (parent pack id) | Pick the parent from the dropdown. Leave blank for root packs. |
| Origin | Read-only — `corporate` or `project`. |

**Appearance card**

| Field | What to fill in |
|---|---|
| Line-weight scale | A multiplier on all line weights. `0.8` = lighter, `1.0` = default, `1.5` = heavier (for fabrication shop drawings where bold lines aid readability on the workshop floor) |
| Text style name | Pick from the dropdown of loaded text note types, merged with STING corporate styles (`STING - 2.0mm`, `STING - 2.5mm`, `STING - 3.0mm Presentation`, `STING - 2.0mm Shop`, `STING - 3.5mm Large Format`) |
| Dimension style name | Pick from the dropdown of loaded dimension types, merged with STING corporate styles (`STING - Linear`, `STING - Ordinate`, `STING - Chain`) |
| Hatch palette | `ISO 13567 monochrome` / `ISO 13567 colour` / `AIA NCS` / `BS 1192 mono` / `Project custom` |
| Colour scheme | `Monochrome` / `Discipline` / `Pastel` / `RAG` / `Spectral` / `Warm` / `Cool` / `High Contrast` / `PresentationRich` / `ClarificationRed` |

**Filter rules grid**

One row per parameter filter rule. Columns:

| Column | What to fill in |
|---|---|
| Filter name | Pick from the dropdown of `ParameterFilterElement` names in the project, merged with 20 common STING filters |
| Visible | Tick to show elements matching this filter; untick to hide them |
| Halftone | Tick to render matching elements at half opacity |
| Proj-Col | Projection line colour as hex `#RRGGBB` (e.g. `#C00000` for red) |
| Proj-Wt | Projection line weight 1–16 |
| Cut-Col | Cut line colour |
| Cut-Wt | Cut line weight |
| Trans% | Surface transparency 0 (opaque) to 100 (invisible) |

Click `+ Add row` to add a filter rule. Click `×` on a row to delete it.

**VG overrides grid**

One row per category VG override. Columns are identical to the filter rules grid, but the first column is **Category** (pick from a merged list of Revit category names and known engineering categories). The category dropdown is editable — type `OST_Walls` or `<Room Separation>` for subcategories not in the default list.

**Tag families map**

One row per category → tag family mapping. Columns: Category (dropdown), Tag family name (text, must match a loaded tag family name exactly).

This map is what the annotation rules `AutoTag` rule type uses when it needs to know which tag family to place. If a category is not in this map, the engine uses the default tag family for that category.

---

## Quick Reference

### All Drawing Type commands

| Button label in STING panel | Panel tab | What it does | When to use it |
|---|---|---|---|
| **Edit Types** | DOCS > Drawing Types | Opens the two-tab editor for Drawing Types and View Style Packs | Set up or adjust drawing types, style packs, annotation rules, slots, token patterns |
| **Inspect** | DOCS > Drawing Types | Read-only report: all drawing types, routing table, validator results, drift count | Weekly check; first step when troubleshooting a wrong-looking sheet |
| **Reload JSON** | DOCS > Drawing Types | Re-reads `STING_DRAWING_TYPES.json` and `STING_VIEW_STYLE_PACKS.json` from disk, clears cache | After hand-editing the JSON files, or after the BIM manager pushes a corporate update |
| **Group Browser** | DOCS > Drawing Types | Reports whether the "STING - by Drawing Type" browser organisation exists and is active; gives instructions for creating it (one-time per project) | First-time project setup; when the browser grouping disappears |
| **Sync Styles** | DOCS > Drawing Types | Re-applies each drifted view's drawing type recipe settings inside one Transaction | After editing a pack or drawing type; after discovering drift in Inspect |
| **From Scope Boxes** | DOCS > Drawing Types | Walks every scope box named `STING::<id>::<level>::<tag>`, creates views, applies recipes, crops to boxes | Fastest way to produce a coordinated set of plans across multiple zones and levels |

### All Managed Template commands

| Button label in STING panel | Panel tab | What it does | When to use it |
|---|---|---|---|
| **Convert Pack to Managed** | DOCS > Drawing Types (View Style Packs editor toolbar) | Reads an existing Revit template's settings into the pack JSON, then sets `templateMode = managed`; renames the original to `<name>_legacy` | Migrating an existing project from hand-authored templates to STING-managed |
| **Detach from STING Management** | DOCS > Drawing Types (View Style Packs editor toolbar) | Creates a new plain Revit template from the current pack settings; switches the pack to `external` mode | When you want to hand-author the template going forward without STING overwriting it |
| **Regenerate Pack Templates** | DOCS > Drawing Types (View Style Packs editor toolbar) | Force-creates or force-updates all managed templates for the selected pack in the active project | On a new project where managed templates do not yet exist; after a major pack edit |

### All View Style Pack commands

These are accessed via the View Style Packs tab in the Drawing Type Editor (DOCS > Drawing Types > Edit Types > View Style Packs tab).

| Action | How to trigger it | What it does |
|---|---|---|
| New pack | Click **+ New** in the left column | Creates a blank project-origin pack |
| Clone pack | Select a pack, click **Clone** | Copies the selected pack to a new project-origin pack (useful for starting from a corporate pack without modifying the original) |
| Delete pack | Select a project-origin pack, click **Delete** | Removes the pack from the project override file (cannot delete corporate packs) |
| Save pack | Click **Save** in the footer | Writes all project-origin packs to `<project>/_BIM_COORD/view_style_packs.json` |
| Push template to bound types | Click **Push template → bound types** | Copies this pack's view template name to every drawing type that references this pack |
| Use pack template | Click **Use pack template** (on Drawing Types tab) | Sets the drawing type's view template to the one declared in its associated pack |
| Push to pack | Click **↑ Push to pack** (on Drawing Types tab) | Copies the drawing type's view template name up to the pack |

### Glossary of terms used in this guide

| Term | Plain-English meaning |
|---|---|
| **Drawing Type** | A named JSON recipe describing how one kind of drawing should look — sheet size, scale, slots, annotation, numbering. 40 ship corporate; you clone and edit. |
| **View Style Pack** | A reusable collection of graphic style settings (line weights, filters, VG overrides) shared by many drawing types. 11 ship corporate. |
| **Slot** | A named rectangle on the drawable zone of a sheet. Declares "the main plan view goes here" or "the key plan goes here." The auto-placer routes views to slots by matching purposeTag. |
| **Purpose tag** | A vocabulary label assigned to each slot (e.g. `main-plan`, `key-plan`, `fabrication-isometric`). The auto-placer matches incoming views to slots by reading this label. |
| **Routing rule** | A `(discipline, phase, docType)` → DrawingType mapping. The dispatcher walks rules in order and picks the first match. |
| **Annotation rule pack** | The "what to auto-tag and how to auto-dim" payload inside a Drawing Type. Consumed by the AnnotationRunner at pipeline step 8. |
| **Crop strategy** | How a view's crop box is computed: scope box, tight bounding box, room boundary, or none. |
| **Style stamp** | The `STING_DRAWING_TYPE_ID_TXT` parameter written onto every STING-produced view, recording which recipe produced it. Used by browser organisation and Sync Styles. |
| **Style lock** | The `STING_STYLE_LOCKED_BOOL` parameter. When set to 1, Sync Styles skips the view. Use it to protect hand-tuned views. |
| **Drift** | A stamped view whose actual scale, detail level, or template no longer matches its recipe. Found by `DrawingDriftDetector`, fixed by Sync Styles. |
| **Project override** | A JSON file at `<project>/_BIM_COORD/drawing_types.json` (or `view_style_packs.json`) holding project-specific variants of corporate entries. Never mutates the corporate baseline on disk. |
| **Token** | A `{disc}` / `{lvl}` / `{seq:D4}` placeholder in a pattern string, resolved at sheet-creation time using the context (current discipline, current level, etc.). |
| **Managed template** | A Revit view template auto-generated and maintained by STING from a View Style Pack in managed mode. Named `STING:<pack-id>:<ViewType>`. |
| **External template** | A hand-authored Revit view template; STING applies VG overrides on top of it but does not overwrite it. |
| **STING_PACK_CHECKSUM_TXT** | A 64-character SHA-256 fingerprint stamped on each managed template. The engine compares this to the current pack JSON to detect drift. |
| **extends chain** | The parent-child inheritance tree of View Style Packs. `corp-base` is the root; child packs inherit all parent settings and add or override specific fields. |
| **BIM family** | A title block family that auto-fills labels from shared parameters and declares slot JSON. The engine reads it fully. |
| **Non-BIM family** | A simpler title block where the drafter fills in project information by hand. The engine still places viewports but does not route by purposeTag. |
| **Drawable zone** | The rectangle inside the title block where viewports may be placed. Declared by `TB_DRAWZONE_X/Y/W/H_MM` parameters on the title block family. |
| **ISO 19650** | The British and European standard for naming and managing construction documents. Defines the 9-segment sheet number STING can produce. |
| **Suitability** | The ISO 19650 status code — `S0` (work in progress) through `S7` (issued for tender), plus `A1`–`A5` and `B1`–`B5` variants for published deliverables. |
| **Spool** | A pre-fabricated assembly of pipework, ductwork, or conduit shipped as one unit. STING creates one fabrication sheet per spool. |
| **Inspect** | The read-only diagnostic command that shows the full catalogue, routing table, validator results, and drift count. Run it first when troubleshooting. |
| **Sync Styles** | The propagate-changes command that re-applies each stamped view's recipe after a recipe or pack edit. |

---

## Cross-references

> **This guide uses:**
> - `docs/title_blocks/SLOT_TAXONOMY.md` — canonical purpose tag vocabulary
> - `docs/guides/TITLE_BLOCK_CREATION_GUIDE.md` — detailed title block authoring steps
> - `docs/guides/DRAWING_TYPE_MANAGER_GUIDE.md` — detailed drawing type manager reference
> - `docs/STING_MANAGED_TEMPLATES_DESIGN.md` — managed templates architecture design
> - `StingTools/Data/DRAWING_TEMPLATE_GUIDE.md` — quick-start template guide
>
> **This guide is referenced by:**
> - `docs/guides/ELECTRICAL_WORKFLOW_GUIDE.md` — references electrical drawing types (`elec-power-A1-1to100`, `elec-lighting-A1-1to100`, `elec-fire-alarm-A1-1to100`, `elec-riser-A2-1to100`)
> - `docs/guides/PLUMBING_WORKFLOW_GUIDE.md` — references plumbing drawing type (`plumb-drainage-A1-1to100`)
> - `docs/guides/HEALTHCARE_WORKFLOW_GUIDE.md` — references healthcare-specific drawing types and the managed template pattern for clinical facility drawings
> - `docs/guides/MEP_FOUNDATION_GUIDE.md` — uses drawing types to annotate MEP views after production
