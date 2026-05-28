# STING Tools — Drawings Production Layman's Guide

**Who this is for:** Anyone who has to turn a Revit model into a clean,
consistent, ISO 19650-compliant drawing set — and wants STING Tools to
do the boring, repetitive parts automatically. No coding required. If
you can click a button and type a name, you can follow this guide.

**What you will be able to do by the end:**

1. Build (or import) your firm's title blocks once.
2. Define **Drawing Types** — recipes that say "an architectural plan at
   1:100 on A1 looks like *this*".
3. Define **View Style Packs** — the visual look (colours, line weights,
   filters, view templates) shared across many Drawing Types.
4. Let STING **produce** views and sheets automatically, crop them, style
   them, number them, and stamp the title block — all in one go.
5. Track every sheet, build a drawing register, manage revisions, issue
   transmittals, and tag/QR your assets.

---

## 0. The mental model (read this first)

Think of drawings production as a **factory line** with four stations.
STING gives you a tool for each:

| Station | What it answers | STING tool |
|---|---|---|
| **1. Title block** | "What does the sheet border look like?" | **Title Block Factory** |
| **2. Drawing Type** | "What *is* this drawing? (paper, scale, template, crop, slots, sheet number pattern)" | **Drawing Type Manager** |
| **3. View Style Pack** | "How does the drawing *look*? (colours, line weights, filters, view template)" | **View Style Pack editor** |
| **4. Filters** | "Which elements get coloured/hidden/emphasised?" | **AEC Filter Library** |

A **Drawing Type** is the master recipe. It *points at* a View Style
Pack (look) and a Title Block (border), and the View Style Pack *points
at* AEC Filters. Set them up once, and every future drawing follows the
recipe automatically.

```
                 ┌──────────────────────┐
                 │     DRAWING TYPE      │  "arch-plan-A1-1to100"
                 │  paper, scale, crop,  │
                 │  slots, sheet number  │
                 └─────────┬─────┬───────┘
              points at    │     │   points at
            ┌──────────────┘     └───────────────┐
            ▼                                     ▼
   ┌─────────────────┐                  ┌──────────────────┐
   │ VIEW STYLE PACK │                  │   TITLE BLOCK    │
   │ colours, weights│                  │  A1 border .rfa  │
   │ view template   │                  │  + param cells   │
   └────────┬────────┘                  └──────────────────┘
            │ points at
            ▼
   ┌─────────────────┐
   │   AEC FILTERS   │  fire walls red, MEP by service, etc.
   └─────────────────┘
```

**Where the buttons live:** Everything below is on the **STING dock
panel** (the tabbed panel on the right of Revit). If it's hidden, the
ribbon has a **STING Panel** button to toggle it. Drawing production
lives on the **DOCS** tab; revisions and transmittals live on the
**BIM** tab.

**Where your settings are stored:** STING ships a corporate baseline
inside the plugin. Your *project-specific* edits are saved next to your
`.rvt` file in a hidden folder called **`_BIM_COORD/`**:

- `_BIM_COORD/drawing_types.json` — your project's Drawing Types
- `_BIM_COORD/view_style_packs.json` — your project's Style Packs
- `_BIM_COORD/aec_filters.json` — your project's extra filters

You never *have* to edit these by hand — the editor dialogs write them
for you — but it's good to know they exist (they travel with the
project and can be copied to the next job).

---

## 1. One-time prerequisites (do this once per project)

Before producing anything, give STING the project metadata it stamps
onto title blocks.

### Step 1.1 — Load the shared parameters
**DOCS tab is not needed for this — go to the CREATE tab → Setup →
Load Params** (button tag `LoadSharedParams`).

This binds STING's parameters (the `PRJ_ORG_*`, `STING_DRAWING_TYPE_ID_TXT`,
crop-stamp, etc.) into your project. Without this, title-block stamping
and sheet tracking have nowhere to write. Run it once; it's idempotent
(safe to re-run).

### Step 1.2 — Fill in Project Information
Open Revit's **Manage → Project Information** and fill in the STING
organisation fields. These are what get printed in your title block:

| Parameter | Example value | Used for |
|---|---|---|
| `PRJ_ORG_PROJECT_CODE` | `2401-RVH` | Sheet numbers, title block |
| `PRJ_ORG_CLIENT_NAME` | `Royal Victoria NHS Trust` | Title block "Client" cell |
| `PRJ_ORG_COMPANY_NAME` | `Planscape Ltd` | Title block "Company" cell |
| `PRJ_ORG_COMPANY_ADDRESS` | `Kampala, Uganda` | Title block address cell |
| `PRJ_ORG_ORIGINATOR_CODE` | `PLNS` | ISO 19650 originator |
| `PRJ_ORG_APPOINTING_PARTY` | `RVH Trust` | Title block |
| `PRJ_ORG_LEAD_APPOINTED_PARTY` | `Planscape Ltd` | Title block |

> **Tip:** If your project already has values in old title-block
> parameters (`PRJ_TB_*`), you don't have to retype them. Run
> **DOCS → Drawing Types → Migrate TB Params** (`DrawingTypes_MigrateParams`)
> and STING copies `PRJ_TB_*` → `PRJ_ORG_*` and re-stamps every sheet.

---

## 2. Station 1 — Title Blocks

A title block is the printed border of your sheet (the box with client
name, sheet number, revision strip, scale, north point, etc.). STING can
**generate** title-block families for you from a JSON recipe, so every
size (A0/A1/A2/A3) is identical in layout and carries the exact
parameter cells STING knows how to fill.

### Step 2.1 — Create your title block families

**DOCS tab → Drawing Types card → Title Blocks sub-panel:**

| Button | Tag | What it does |
|---|---|---|
| **Create** | `TitleBlock_Create` | Pick **one** title-block family from the spec and build its `.rfa` |
| **Create All** | `TitleBlock_CreateAll` | Build **every** title-block size at once (A0/A1/A2/A3, BIM + NONBIM) |

What you get: a real loadable `.rfa` family per paper size, each
containing:

- **Border lines** at correct BS 1192 line weights (Wide/Medium/Thin).
- **Static text cells** — fixed labels like "CLIENT", "DATE", "SCALE".
- **Parameter labels** — the cells STING fills automatically (project
  code, client, sheet number, revision, suitability, status…).
- **A revision strip** — rows that STING syncs from Revit's revision
  table (more in §10).
- **Viewport "slots"** — invisible reference zones that tell the
  Drawing Type / Sheet Manager exactly where to drop each viewport.

> **Beginner path:** Just click **Create All**. You now have a complete,
> consistent title-block set with zero drafting. Load them into your
> project (Revit auto-loads families saved into the project folder, or
> use Insert → Load Family).

> **Already have firm title blocks?** You don't have to use STING's. You
> can keep your own `.rfa` files — just make sure each Drawing Type
> (§3) names your family in its **Title Block Family** field, and that
> your title block has parameters with names matching what you want
> STING to fill. STING fills *any* instance parameter you map.

### Step 2.2 — Understand how cells get filled (the token syntax)

Each Drawing Type carries a small table called **Title Block Params**
— a list of "parameter name → value template" pairs. The value template
uses two kinds of placeholder:

| Syntax | Meaning | Example → result |
|---|---|---|
| `${ParamName}` | Read a **Project Information** parameter | `${PRJ_ORG_CLIENT_NAME}` → `Royal Victoria NHS Trust` |
| `{token}` | A value supplied at production time | `{disc}` → `A`, `{lvl}` → `L02` |
| `{token:Dn}` | Same, zero-padded to *n* digits | `{seq:D4}` → `0042` |

Available production tokens: `{disc}` (discipline letter), `{lvl}`
(level code), `{sys}` (system), `{spool}`, `{mark}`, `{seq}` /
`{seq:D2}` / `{seq:D3}` / `{seq:D4}`.

**Worked example** — a "Sheet Number" cell template of:

```
${PRJ_ORG_PROJECT_CODE}-{disc}-{seq:D3}
```

on an architectural sheet produces: `2401-RVH-A-007`.

You set these mappings in the Drawing Type editor (§3.3), not by hand.

---

## 3. Station 2 — Drawing Types (the heart of the system)

A **Drawing Type** is the recipe for one kind of drawing. STING ships
~90 corporate Drawing Types (arch plans, RCPs, sections, elevations,
MEP coordination, structural, healthcare, presentation, etc.) so you
usually **start from an existing one and tweak**, rather than building
from scratch.

### Step 3.1 — Look at what you already have

**DOCS tab → Drawing Types → Inspect** (`DrawingTypes_Inspect`).

This opens a read-only report listing:

- Every Drawing Type and its key settings.
- The **routing table** (how disciplines/phases map to Drawing Types).
- A **validation report** — it flags any Drawing Type that references a
  title block, view template, or tag family that isn't loaded in your
  project. These appear as **Warnings**, not errors — the system still
  works, but the named asset is missing until you provide it.
- A headline summary of any **drift** (sheets/views that have wandered
  from their recipe) and any on-disk edits since last load.

> Run **Inspect** first on any new project. It tells you exactly which
> view templates / title blocks you still need to load.

### Step 3.2 — Open the editor

**DOCS tab → Drawing Types → Edit Types** (`DrawingTypes_Editor`).

This opens the **Drawing Type Editor** dialog with six tabs:

1. **Drawing Types** — the recipe list + form (this section).
2. **View Style Packs** — the visual look (§4).
3. **Viewport Tools** — viewport alignment helpers.
4. **Sheet Tools** — sheet-level helpers.
5. **Title Block** — title-block param binding card.
6. **Sheet Manager** — sheet/viewport layout.

On the **Drawing Types** tab: a searchable list on the left with
**＋ New / Clone / Delete** buttons, and a form on the right.

### Step 3.3 — Anatomy of a Drawing Type (every field explained)

| Field | Plain-English meaning | Example |
|---|---|---|
| **id** | Unique code name | `arch-plan-A1-1to100` |
| **name** | Human label | `Architectural Plan A1 @ 1:100` |
| **purpose** | What family of drawing | `Plan` / `RCP` / `Section` / `Elevation` / `Detail` / `Schedule` / `Coordination` / `3D` / `Legend` / `Clarification` |
| **discipline** | Routing key | `A` (Arch), `S`, `M`, `E`, `P`, `H`… or `*` for any |
| **phase** | Routing key | `*` (any), or `PRESENTATION`, `EXISTING`… |
| **paperSize** | Sheet size | `A1` |
| **titleBlockFamily** | Which border family to use | `STING_TB_SHEET_A1` |
| **orientation** | Landscape / Portrait | `Landscape` |
| **scale** | View scale denominator | `100` (= 1:100) |
| **detailLevel** | Coarse / Medium / Fine | `Fine` |
| **viewTemplateName** | Revit view template to apply | `STING - Architectural Plan` |
| **viewportTypeName** | Viewport type for the sheet | `STING - Standard Viewport` |
| **sheetNumberPattern** | Token recipe for sheet number | `${PRJ_ORG_PROJECT_CODE}-A-{seq:D3}` |
| **sheetNamePattern** | Token recipe for sheet name | `{lvl} - GA PLAN` |
| **crop** | How to crop the view (see below) | `RoomBoundary` |
| **viewStylePackId** | Which look to use | `corp-standard-plan` |
| **slots[]** | Where viewports land on the sheet | one full-bleed slot |
| **annotation** | What to auto-tag / auto-dimension | grid dims, room tags |
| **titleBlockParams** | The cell→value map (§2.2) | 11 corporate cells |

**Crop strategies** (the `crop.kind` field):

| Strategy | Behaviour |
|---|---|
| `ScopeBox` | Crop to a named scope box (fails if box missing) |
| `ScopeBoxOrBbox` | Crop to scope box if present, else element bounding box |
| `TightBbox` | Crop tight to model elements + a margin (mm) |
| `RoomBoundary` | Crop to room outlines (falls back to TightBbox if no rooms) |
| `None` | Don't crop |

### Step 3.4 — Make your own Drawing Type (worked example)

Goal: a 1:50 architectural plan on A1 for ground-floor detailed plans.

1. In the editor, select the closest existing type, e.g.
   `arch-plan-A1-1to100`, and click **Clone**.
2. Rename **id** to `arch-plan-A1-1to50` and **name** to
   `Architectural Plan A1 @ 1:50`.
3. Set **scale** to `50`.
4. Leave **titleBlockFamily**, **viewStylePackId**, **crop**, and
   **titleBlockParams** as inherited — they're already correct.
5. Click **Save**. Because this is a new entry, STING writes it to your
   project's `_BIM_COORD/drawing_types.json`. The corporate baseline on
   disk stays untouched.

You now have a reusable 1:50 plan recipe. Done.

> **Naming tip:** STING's convention encodes everything in the id —
> `discipline-purpose-papersize-scale`. Following it keeps the catalogue
> self-documenting and lets the validator check that the id matches the
> actual scale/paper.

### Step 3.5 — Edit Drawing Types in Excel (bulk editing)

Prefer a spreadsheet to a dialog? STING round-trips the whole catalogue:

- **Export to Excel** (`DrawingTypes_ExportExcel`) → an 8-sheet workbook
  with colour swatches and validation dropdowns.
- Edit in Excel (add types, change scales, tweak patterns).
- **Import from Excel** (`DrawingTypes_ImportExcel`) → validation
  pre-flight + a change preview before anything is written.

Great for setting up a whole firm standard at once.

---

## 4. Station 3 — View Style Packs, VG overrides & Managed Templates

A **View Style Pack** is the *visual identity* shared by many Drawing
Types — so "the company blue", "our line weights", "our standard
filters", and "our view template" are defined **once** and reused.
STING ships ~22 packs (`corp-standard-plan`, `corp-coordination`,
`corp-presentation-rich`, eight healthcare packs, etc.).

### Step 4.1 — Open the pack editor

In the Drawing Type Editor, click the **View Style Packs** tab. Same
shape as Drawing Types: list on the left, form on the right with cards:

- **Identity** — id, name, description, `extends` (parent pack to
  inherit from), origin.
- **Appearance** — line-weight scale, text style, dimension style, hatch
  palette.
- **Filter rules** — a grid: filter name, Visible?, Halftone?,
  projection colour + weight, cut colour + weight, transparency.
- **VG overrides** — a per-category grid (the full Revit
  Visibility/Graphics editor, replicated inside STING). Set cut/projection
  foreground & background, halftone, transparency, detail level per
  category.
- **Tag families** — category → tag-family-name map.

### Step 4.2 — The "extends" chain (inherit, don't repeat)

Packs can inherit from a parent via the **extends** field. Example:
`corp-demolition-phase` extends `corp-standard-plan` — it gets all the
standard-plan look, then **only** overrides the demolition specifics
(existing halftoned, demolished bold red dashed). You define the
difference, not the whole thing.

### Step 4.3 — External vs Managed templates (important choice)

Each pack has a **templateMode**: `external` or `managed`.

| Mode | What it means | When to use |
|---|---|---|
| **external** | The pack references a Revit view template *you* maintain by hand. STING applies it but doesn't own it. | You already have polished firm view templates. |
| **managed** | STING **auto-creates and maintains** a Revit view template named `STING:<pack-id>:<ViewType>` from the pack JSON. Edit the pack → STING regenerates the template. | You want one source of truth and zero manual template upkeep. |

**To make a pack managed:** select it and click **→ Managed Mode**
(`DrawingTypes_ConvertToManaged`). STING mints the `STING:*` templates.
**To go back:** **Detach Managed** (`DrawingTypes_DetachManaged`).
**To force a rebuild after editing:** **Regenerate Templates**
(`DrawingTypes_RegenerateTemplates`).

> **Recommendation for a clean automated pipeline:** use **managed**
> packs. Then your visual standard lives entirely in STING and you never
> have to hand-edit a Revit view template again — change the pack, hit
> Regenerate, and every drawing updates.

### Step 4.4 — Editing VG overrides like the real Revit dialog

The VG card is a faithful replica of Revit's Visibility/Graphic
Overrides window. For each model category you can set: Cut FG/BG,
Projection FG/BG, halftone, transparency, detail level. Pop-up
sub-dialogs let you pick fill patterns, line patterns, and colours from
dropdowns (no typos — names are resolved against the project). The
override cell shows the resolved colour swatch live as you edit.

---

## 5. Station 4 — AEC Filter Library (colour/hide/emphasise elements)

Filters are the rules that say "fire-rated walls → red wash",
"MEP pipes → colour by service", "demolished → halftone". STING ships
a corporate library of **~289 filters** covering Architectural, HVAC,
Structural, Fire, Electrical, Plumbing, FM/COBie, ISO 19650, and
healthcare disciplines, each with a ready-made override recipe.

### Step 5.1 — Create the filters in your project

**DOCS tab → Drawing Types → (AEC Filters)**:

| Button | Tag | What it does |
|---|---|---|
| **Create** | `AecFilters_Create` | Mint all referenced filters into the project (idempotent — safe to re-run) |
| **Inspect** | `AecFilters_Inspect` | Read-only diagnostic of what exists vs what packs reference |
| **Reload** | `AecFilters_Reload` | Refresh the filter cache after editing the JSON |

### Step 5.2 — How filters connect to packs (you usually do nothing)

A View Style Pack's **Filter rules** card lists filters *by name*. When
STING applies a pack to a view and a referenced filter isn't in the
project yet, it **lazy-creates it** from the library automatically under
the active transaction. So in practice: pick a pack that references the
filters you want, and they appear when needed.

If a filter relies on a shared parameter that isn't bound yet, STING
**warns and skips** that one filter (rather than failing the batch) —
bind the parameter and re-run.

### Step 5.3 — Add your own filter

Add a definition to your project's `_BIM_COORD/aec_filters.json` (or use
the corporate `STING_AEC_FILTERS.json` as a template). Each filter has a
name, a category list, a rule tree (parameter + operator + value), and a
default override recipe. Then reference its name in a pack's Filter
rules card. Run **AecFilters_Reload**, then **AecFilters_Create**.

---

## 6. Producing drawings — the automated pipeline

Now the payoff. With title blocks, Drawing Types, packs, and filters in
place, STING can build the drawings for you. There are several
production entry points depending on how your model is organised.

### What "apply a Drawing Type" actually does (the 10-step pipeline)

When STING applies a Drawing Type to a view, it runs, in order:

1. **Lock check** — skip if the view is locked (`STING_STYLE_LOCKED_BOOL`).
2. **Stamp** the view with its Drawing Type id (`STING_DRAWING_TYPE_ID_TXT`).
3. **Scale**.
4. **Detail level**.
5. **View template** (managed packs mint/maintain it automatically).
6. **Crop** (per the crop strategy).
7. **View Style Pack** (colours, weights, filters, VG overrides).
8. **Annotation pass** (auto-dimension grids, auto-tag rooms/elements).

Then, for sheets:

9. **Stamp** the sheet with its Drawing Type id.
10. **Title-block param binding** — fill every cell from
    `${PRJ_ORG_*}` + production tokens.

Every step is error-tolerant: a failure on one step collects a warning
and the pipeline keeps going.

### Option A — One-click: Produce & Export (the easy button)

**DOCS tab → Drawing Types → ⚡ Produce & Export**
(`DrawingTypes_ProduceAndExport`).

This is the "do everything" button: it produces plan views per level,
applies styles, syncs revisions, exports sheets to PDF, and writes a
sheet-register CSV. Great for a quick, consistent first issue. Start
here, then refine with the targeted options below.

### Option B — Produce per level

**DOCS → Drawing Types → Produce Per Level** (`DrawingTypes_ProducePerLevel`).

Opens a production-config dialog. Pick the Drawing Type, the levels, and
go — STING creates one cropped, styled, sheeted view per level, numbered
by the sheet-number pattern.

### Option C — Produce from Scope Boxes (best for big/zoned projects)

This is the most powerful path. Name your scope boxes with STING's
convention and STING figures out everything else.

**Scope-box naming convention:**

```
STING::<drawing-type-id>::[<level-code>]::[<tag>]
```

Examples:

- `STING::arch-plan-A1-1to100::L02`
- `STING::pipe-spool-A1-1to50::L01::HWS`
- `STING::mep-coord-A1-1to50`

(Allowed characters: letters, numbers, `.`, `_`, `-`. No spaces.)

Then click **DOCS → Drawing Types → From Scope Boxes (Produce)**
(`DrawingTypes_ProduceFromScopeBoxes`). STING:

- reads each scope box,
- looks up the Drawing Type from the id in the name,
- creates the view, applies the profile, crops to the box,
- puts it on a sheet, numbers it, stamps the title block.

**Idempotent:** re-running finds the views it already made and
re-applies rather than duplicating. So you can tweak a pack and re-run
safely.

> There's also a **suggest** helper: **From Scope Boxes**
> (`DrawingTypes_FromScopeBoxes`) scans your scope boxes and proposes
> Drawing Type stubs with auto-detected disciplines — handy for setting
> up the names.

### Option D — Produce Sections & Elevations

| Button | Tag | What it does |
|---|---|---|
| **Produce Sections** | `DrawingTypes_ProduceSections` | Create section views via grid alignment, room walls, or manual selection |
| **Exterior Elevations** | `DrawingTypes_ProduceExteriorElevations` | Auto-place 4 exterior elevations from the detected building footprint |
| **Interior Elevations** | `DrawingTypes_ProduceInteriorElevations` | Produce interior elevations for selected rooms |

### Option E — Re-stamp existing sheets

Already have sheets and just want to apply a Drawing Type to them?
**Re-Stamp Sheets** (`DrawingTypes_BulkReStamp`) re-applies the profile
to selected or all sheets — handy after changing a recipe.

---

## 7. Sheet tracking & the Drawing Register

Once sheets exist, you need to know their state and produce a register.

### Step 7.1 — How sheets are tracked

Every sheet STING produces is **stamped** with shared parameters so you
always know its provenance and status:

| Parameter | On | Meaning |
|---|---|---|
| `STING_DRAWING_TYPE_ID_TXT` | Sheet/view | Which Drawing Type recipe produced it |
| `STING_STYLE_LOCKED_BOOL` | Sheet/view | If `1`, sync/drift tools leave it alone |
| `PRJ_TB_DELIVERABLE_STATUS_TXT` | Title block | WIP / SHARED / PUBLISHED / ARCHIVE |
| `PRJ_TB_DELIVERABLE_CDE_TXT` | Title block | CDE container state (from ISO suitability) |
| `PRJ_TB_LAST_TRANSMITTAL_TXT` | Title block | Last transmittal id |
| `PRJ_TB_LAST_TRANSMITTAL_DATE_TXT` | Title block | Last issue date |

**ISO 19650 suitability → CDE mapping** (applied automatically):

| Suitability | CDE state |
|---|---|
| `S0` | WIP |
| `S1…S7` | SHARED |
| `A1…An` | PUBLISHED |
| `B1…Bn` | ARCHIVE |

### Step 7.2 — Build a Sheet Index (quick, inside Revit)

**DOCS tab → Sheet Index** (`SheetIndex`). Creates a Revit schedule named
**"STING - Sheet Index"** with Sheet Number, Sheet Name, Drawn By,
Checked By, Current Revision. Drop it on a cover sheet.

### Step 7.3 — Build a full Drawing Register (ISO 19650 CSV)

**Drawing Register Sync** (`DrawingRegisterSync`) exports a complete
register CSV with all the ISO 19650-2 Annex B fields:

Sheet Number, Sheet Name, Discipline, Revision, Status (WIP/SHARED/
PUBLISHED/ARCHIVE), **Suitability** (S0–S7, A, B…), **Document Type**
(DR/SH/SP/SK/RP), CDE Location, Originator, Phase, Drawn By, Checked By,
Approved By, Approval Date, Date, Scale, Paper Size, Viewport count,
Placeholder flag.

This is your issue register — send it with your transmittal.

### Step 7.4 — Keep numbers tidy

**Renumber** (`DrawingTypes_Renumber`) compacts gaps in sheet numbers
within each (Drawing Type, package) bucket — so deleting a few sheets
doesn't leave holes.

---

## 8. Sheet tags & legends

"Sheet tagging" in STING means making the meaning of your codes legible
on the sheet, plus consistent sheet identity.

- **Sheet identity** is the Sheet Number + Sheet Name + the
  `STING_DRAWING_TYPE_ID_TXT` stamp — all filled by the Drawing Type.
- **Discipline sheet-tag families** ship for arch / MEP / structural
  (`STING - Architectural Sheet Tag`, `STING - MEP Sheet Tag`,
  `STING - Structural Sheet Tag`).
- **Sheet Tag Legend** (`SheetTagLegend`, on the CREATE tab → Legends)
  builds a legend view explaining what each tag/code means, ready to
  place on a cover or key sheet.

> There's a large family of legend builders (discipline, system,
> material, fire-rating, colour-scheme legends) on the CREATE tab if you
> need to explain colours/filters you applied via packs.

---

## 9. QR codes — what STING actually does (read carefully)

**Honest answer:** STING's QR feature is for **asset/element
commissioning**, **not** for stamping a QR onto the sheet border. There
is **no built-in command that places a QR code into the title block or
sheet layout**. Here's what exists and how to get a QR on a sheet if you
need one.

### What the QR feature *does*

**`QRCodeCommand`** generates QR PNGs for **model elements**:

- Encodes an asset URL: `sting://asset/{projectCode}/{tagValue}`
  (read from each element's `ASS_TAG_1_TXT` tag).
- Writes PNG files to `_bim_manager/qr/` next to the `.rvt`.
- Scanning a code on site pulls up that element's asset data.

**Commissioning workflow** (V6): `QRAdvanceCommissioningCommand` walks
an element through `NOT_STARTED → RECEIVED → INSTALLED → TESTED →
COMMISSIONED → HANDOVER`, tracked in `COMM_STATE_TXT`, `COMM_DATE_TXT`,
`COMM_OPERATIVE_TXT`, `COMM_WITNESS_TXT`, `COMM_NOTES_TXT`, with an audit
log in `STING_Commissioning_Audit.json`. QR images are made with
ZXing.Net (no System.Drawing dependency).

### How to put a QR on a sheet (manual workaround)

Since there's no automatic sheet-QR command, do this:

1. Run **QRCodeCommand** (or any QR generator) to produce a PNG that
   encodes whatever you want the sheet QR to link to (e.g. your CDE
   sheet URL, or the project asset index).
2. On the sheet (or in your title-block family), use Revit's
   **Insert → Image** to place the PNG in a corner of the title block.
3. If you want it on *every* sheet automatically, place the image inside
   the **title-block family** itself and reload — then every sheet using
   that title block carries it.

> If automatic per-sheet QR (e.g. a QR that encodes the sheet number +
> revision and updates on issue) is a hard requirement, that's a feature
> gap — flag it and it can be built on top of the existing ZXing
> generator + title-block param pipeline.

---

## 10. Revisions

Revisions are managed from the **BIM tab → REVISION MANAGER** section.
STING bridges Revit's native revision objects with ISO 19650 revision
codes and your element tags, so the model, the sheets, and the title
block stay in sync.

### The 12 revision commands

| Button | Tag | What it does |
|---|---|---|
| **Create Revision** | `CreateRevision` | Make a new ISO 19650 revision (P##/C##/A–Z), snapshot tags, propagate the REV code to all tagged elements, notify the team |
| **Dashboard** | `RevisionDashboard` | See all revisions, their clouds, sheets, visibility, and change stats; export CSV or select clouds |
| **Auto Clouds** | `AutoRevisionCloud` | Auto-draw revision clouds around elements whose tags changed since the last snapshot (de-duped by location) |
| **Schedule** | `RevisionSchedule` | Export the revision register as CSV (sequence, date, issued, cloud count) with ISO naming validation |
| **Track Elements** | `TrackElementRevisions` | Compare before/after snapshots; per-element change history + which parameters changed |
| **Compare** | `RevisionCompare` | Side-by-side snapshot diff (added / modified / deleted), grouped by discipline or parameter |
| **Issue Sheets** | `IssueSheetsForRevision` | Create/update issue sheets for a revision; link open issues to the revision record |
| **Naming Check** | `RevisionNamingEnforce` | Validate all revision codes against ISO 19650 (P##, C##, A–Z); flag non-compliant ones |
| **Tag Integration** | `RevisionTagIntegration` | Keep tag REV tokens and Revit revision objects in lockstep; stamp elements on revision creation |
| **Bulk Stamp** | `BulkRevisionStamp` | Mass-update REV + STATUS on a selection or query (e.g. "everything modified in the last 48 h") |
| **Export** | `RevisionExport` | Export snapshots, clouds, element deltas, narratives to CSV/XLSX/JSON |
| **Auto Rev** | `AutoRevisionOnTagChange` | Watch for tag edits live; auto-snapshot (and optionally cloud) when core tokens change |

Snapshots are stored as JSON in `_BIM_COORD/Revisions/snapshot_*.json`.
Revision naming format: `REV-{ProjectCode}-{Seq:D3}-{Date}-{Desc}`.

### Step 10.1 — Sync revisions into the title-block strip

After creating a revision, push it into the printed revision strip:
**DOCS → Drawing Types → Sync Rev Strip** (`DrawingTypes_SyncRevisions`).
This fills the title block's `PRJ_TB_REV_COL_n` / `PRJ_TB_REV_DATE_n` /
`PRJ_TB_REV_DESC_n` rows from Revit's revision sequence so the cloud and
the strip agree.

### Typical revision cycle

1. Make your model changes.
2. **Create Revision** → snapshot + propagate the new REV code.
3. **Auto Clouds** → clouds appear where tags changed.
4. **Sync Rev Strip** → title-block revision rows fill in.
5. **Issue Sheets** → issue sheets created/updated, issues linked.
6. **Drawing Register Sync** → updated register CSV.
7. Issue via a transmittal (§11).

---

## 11. Issuing — transmittals

When the set is ready, issue it with a transmittal so the title block
records *who got what, when, at what suitability*.

- **Transmittal** (`Transmittal`, DOCS tab) — reads all sheets, checks
  their revision status, and exports an ISO 19650 transmittal report +
  CSV.
- **Auto-Issue / Stamp** — loads transmittals from
  `STING_BIM_MANAGER/transmittals.json` and stamps the selected sheets
  with transmittal id, issue date, suitability code, deliverable status,
  and CDE state. It **respects the lock flag** (`PRJ_TB_LOCK_BOOL`) —
  locked sheets are skipped so you can't accidentally re-stamp a frozen
  issue.

---

## 12. Keeping it consistent — drift, sync & doctor

Over a long project, individual sheets drift from their recipe (someone
changes a scale, a filter, a crop). STING detects and heals this.

| Button | Tag | What it does |
|---|---|---|
| **Sync Styles** | `DrawingTypes_SyncStyles` | Re-apply each Drawing Type's look to its stamped views; reports missing templates/viewport types |
| **Heal TBs** | `DrawingTypes_HealTitleBlocks` | Re-apply just the title-block param bindings (doesn't touch scale/crop/templates) |
| **Doctor** | `DrawingTypes_Doctor` | Audit the title-block layer for cross-stamps, wrong family swaps, missing title blocks, stale syncs |
| **Reload JSON** | `DrawingTypes_Reload` | Pick up edits you made directly in the JSON files on disk |
| **Group Browser** | `DrawingTypes_GroupBrowser` | Organise the project browser by Drawing Type discipline |

**What drift detection catches:** scale/detail/template drift, crop
drift (kind or margin changed since the view was cropped), VG-override
drift (a category's colour/weight/halftone/transparency no longer
matches the pack — reported with *all* mismatches, not just the first),
filter drift, and managed-template checksum drift. The **Inspect**
headline tells you the total drift count; **Sync Styles** heals it.

> **Locked views are never touched.** If you've hand-perfected a sheet
> and don't want STING to re-style it, set `STING_STYLE_LOCKED_BOOL = 1`
> on it (a checkbox in the editor / a parameter you can set). Sync and
> drift tools skip it.

---

## 13. Full worked example — start to finish

A small clinic. You want a clean architectural plan set, MEP
coordination, sections, a register, and a first issue.

**Setup (once):**

1. CREATE → Setup → **Load Params**.
2. Manage → **Project Information** → fill `PRJ_ORG_*` (project code
   `2405-CLN`, client, company, originator `PLNS`).
3. DOCS → Drawing Types → Title Blocks → **Create All** → load the
   title-block families.
4. DOCS → Drawing Types → **Inspect** → note any missing view templates;
   if your packs are **managed**, you don't need to hand-make them.
5. DOCS → Drawing Types → AEC Filters → **Create**.

**Pick/confirm recipes:**

6. DOCS → Drawing Types → **Edit Types**. Confirm `arch-plan-A1-1to100`
   uses `corp-standard-plan`, and `mep-coord-A1-1to50` uses
   `corp-coordination`. Clone a 1:50 arch plan if you need it (§3.4).
7. On the **View Style Packs** tab, set `corp-standard-plan` and
   `corp-coordination` to **Managed Mode** (`→ Managed Mode`) so STING
   maintains the templates.

**Produce:**

8. Name your scope boxes:
   `STING::arch-plan-A1-1to100::GF`,
   `STING::arch-plan-A1-1to100::L01`,
   `STING::mep-coord-A1-1to50::GF`, etc.
9. DOCS → Drawing Types → **From Scope Boxes (Produce)**. STING creates,
   crops, styles, sheets, numbers, and title-block-stamps everything.
10. DOCS → Drawing Types → **Exterior Elevations** for the 4 elevations;
    **Produce Sections** for your sections.

**Track & issue:**

11. DOCS → **Drawing Register Sync** → register CSV.
12. BIM → Revision Manager → **Create Revision** (P01), then
    **Auto Clouds** (none yet on a first issue) and DOCS →
    **Sync Rev Strip**.
13. DOCS → **Transmittal** → issue report + CSV; **Auto-Issue/Stamp** to
    stamp suitability `S2`, status `SHARED` onto every sheet.

**Later, after a design change:**

14. Edit the model. BIM → **Create Revision** (P02) → **Auto Clouds** →
    DOCS → **Sync Rev Strip**.
15. If any sheet drifted, DOCS → **Sync Styles** to heal.
16. DOCS → **Drawing Register Sync** → updated register; re-issue.

That's a fully automated, consistent, ISO 19650 set with almost no
manual sheet setup.

---

## 14. Quick reference cheat-sheet

**Setup**
- Load params → `LoadSharedParams` (CREATE tab)
- Title blocks → `TitleBlock_CreateAll` (DOCS)
- Filters → `AecFilters_Create` (DOCS)
- Inspect catalogue → `DrawingTypes_Inspect` (DOCS)

**Define recipes**
- Edit Drawing Types & Packs → `DrawingTypes_Editor` (DOCS)
- Bulk edit in Excel → `DrawingTypes_ExportExcel` / `DrawingTypes_ImportExcel`
- Make a pack managed → `DrawingTypes_ConvertToManaged`

**Produce**
- One-click → `DrawingTypes_ProduceAndExport`
- Per level → `DrawingTypes_ProducePerLevel`
- From scope boxes → `DrawingTypes_ProduceFromScopeBoxes`
  (name boxes `STING::<type-id>::<lvl>::<tag>`)
- Sections / elevations → `DrawingTypes_ProduceSections` /
  `DrawingTypes_ProduceExteriorElevations` /
  `DrawingTypes_ProduceInteriorElevations`
- Re-stamp existing → `DrawingTypes_BulkReStamp`

**Track**
- Sheet index → `SheetIndex`
- Drawing register → `DrawingRegisterSync`
- Renumber → `DrawingTypes_Renumber`

**Revisions (BIM tab)**
- `CreateRevision`, `AutoRevisionCloud`, `RevisionDashboard`,
  `IssueSheetsForRevision`, `RevisionExport`, …
- Sync to title block → `DrawingTypes_SyncRevisions` (DOCS)

**Issue**
- `Transmittal` (DOCS) + Auto-Issue/Stamp

**Maintain**
- Heal styles → `DrawingTypes_SyncStyles`
- Heal title blocks → `DrawingTypes_HealTitleBlocks`
- Audit → `DrawingTypes_Doctor`
- Reload after JSON edit → `DrawingTypes_Reload`

**Token syntax**
- `${PRJ_ORG_PROJECT_CODE}` — Project Info value
- `{disc}` `{lvl}` `{sys}` `{seq:D4}` — production tokens

**Scope-box name** → `STING::<drawing-type-id>::<level>::<tag>`

---

## 15. Common questions & gotchas

- **"The validator says my view template is missing."** That's a
  *Warning*, not an error. Either load/create the named template, or
  switch the pack to **managed** mode so STING makes it for you.
- **"A filter didn't apply."** It probably depends on a shared parameter
  that isn't bound. Run **Load Params**, then **AecFilters_Reload** +
  **AecFilters_Create**.
- **"My corporate baseline changed unexpectedly."** It didn't — STING
  never writes to the corporate JSON. Your edits go to
  `_BIM_COORD/*.json`. If you edited a corporate entry, STING silently
  forks it to a project copy (origin flips to `project`).
- **"I tweaked a pack but views didn't update."** Run **Sync Styles**
  (and **Regenerate Templates** for managed packs). Production isn't
  live/automatic — you heal on demand.
- **"Re-running production made duplicates."** It shouldn't — scope-box
  production is idempotent. If you renamed views by hand, the matcher
  can't find them; keep the STING stamp intact.
- **"Can I put a QR on every sheet automatically?"** Not out of the box
  — QR is for element commissioning. Place a generated QR PNG inside the
  title-block family for an all-sheets QR (§9).

---

*This guide covers the corporate baseline behaviour. Your firm may ship
project overrides in `_BIM_COORD/`. When in doubt, run **Inspect** — it
always reports the live, resolved state of your project's catalogue.*
