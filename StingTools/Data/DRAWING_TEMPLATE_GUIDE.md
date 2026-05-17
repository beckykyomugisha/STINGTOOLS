# STING Drawing Template Manager — Layman's Guide

A plain-English handbook to making every drawing in your project look right
the first time, without manually fiddling with sheet templates, view templates,
title blocks, scale, line weights, or sheet numbers.

If you have ever:

- spent a Friday afternoon resizing 30 viewports because someone changed scale,
- argued with a colleague about which title block to use,
- discovered after print that half your sheets are at 1:50 and half at 1:100,
- found that "Sheet A101" already exists in three other projects,
- watched the QC team reject a deliverable because the suitability code is wrong,

…this guide is for you.

---

## TL;DR (30 seconds)

STING ships a JSON catalogue of **40 drawing types** and **11 visual style packs**.
Each profile says everything a single drawing needs to know — sheet size, title
block, scale, view template, where the views land on the sheet, what to tag,
how to dimension, what the sheet number looks like.

You pick a profile (`arch-plan-A1-1to100`, `pipe-spool-A1-1to50`, …) once. From
then on, every batch command — fabrication composer, batch sections, batch
elevations, batch views, sheet manager — produces sheets that obey that profile.
Edit the profile, every sheet using it updates together.

You never set scale, template or title block by hand again.

---

## Why this exists (the problem)

Architecture, MEP and fabrication firms produce hundreds of drawings per
project. Without a template manager:

- **Every team member guesses** scale / template / title block per sheet.
- **Style drift compounds** — week 1 sheets look different from week 12 sheets,
  presentation sheets look like production sheets and vice versa.
- **Editing line weights** project-wide means visiting 40 view templates by hand.
- **Sheet numbers collide** because nobody owns a numbering scheme.
- **ISO 19650 compliance** is hand-typed and easy to get wrong.
- **Fabrication shop drawings** end up in the architect's title block by accident.
- **Corporate standards erode** as each project freelances its own variant.

The Drawing Template Manager is one place to fix all of that. Its job is to be
the **single source of truth for how a drawing looks**, so every command that
produces drawings reads from the same record.

---

## The three big concepts

You only need three ideas to use the system end-to-end.

### 1. Drawing Type — *"what kind of drawing am I making?"*

A Drawing Type answers every question for a single produced sheet:

| Question | Field |
|---|---|
| What is this drawing for? | `purpose` (Plan / RCP / Section / Elevation / Detail / Schedule / Spool / Coordination / Legend / 3D) |
| Which discipline? | `discipline` (A / S / M / E / P / FP / LV / G / *) |
| What sheet size? | `paperSize` (A0–A4) |
| Which title block? | `titleBlockFamily` |
| What scale? | `scale` (1:N) |
| Which view template? | `viewTemplateName` |
| What sheet number format? | `sheetNumberPattern` (`A-{lvl}-{seq:D3}`, full ISO, etc.) |
| Where do views land on the sheet? | `slots[]` — normalised 0..1 boxes |
| What gets auto-tagged? | `annotation` rule pack |
| How is the view cropped? | `crop` strategy |
| Which graphic style does it inherit? | `viewStylePackId` |

40 ready-made drawing types ship with the plugin, covering RIBA Stage 4
(technical design) production, fabrication, FM/handover, and client
presentation. Pick the closest match, clone it, tweak. You almost never start
from scratch.

### 2. View Style Pack — *"how does it look on the page?"*

A typical corporate catalogue has 40 drawing types but only ~11 distinct visual
styles. Plans share one look, sections share another, presentation sheets share
a third, fabrication shop drawings have their own.

A View Style Pack collects those shared bits in one record:

- Line-weight scale
- Text style and dimension style names
- Filter-rule overrides ("colour fire-rated walls red", "halftone existing")
- Per-category VG overrides ("halftone all grids", "weight 3 on walls")
- Default tag-family map per category

Drawing Types reference a pack by id (`viewStylePackId: "corp-standard-plan"`).
Edit the pack once, every drawing type that uses it updates together.

### 3. Project Override — *"this project does it slightly differently"*

You don't edit the corporate baseline directly. Instead, you Clone a corporate
profile, rename it for the project, edit it, and Save — STING writes the
project-scoped variant to a JSON file inside your `.rvt`'s folder
(`<project>/_BIM_COORD/drawing_types.json`). That file is what changes; the
shipped corporate JSON stays pristine.

Why? So next month, when corporate updates the master `arch-plan-A1-1to100`
profile, your project-specific variant doesn't get clobbered. And when you start
a new project, you start from the latest corporate baseline.

---

## Quick start: your first profile-driven sheet (5 minutes)

1. **Open Revit**, load the project.
2. **DOCS tab → Inspect** — read the catalogue + check which referenced assets
   are missing in this project (validator output is at the bottom).
3. **DOCS → Edit Types** — opens the editor.
4. Pick `arch-plan-A1-1to100` on the left → **Clone** → rename to
   `<your-project>-arch-plan` (or whatever convention you use).
5. Right-hand panel shows form cards. Adjust:
   - Title block family → pick from the dropdown of loaded title blocks
   - Sheet number pattern → click `Generate ISO SheetNumberPattern` for a
     compliant default
6. **Save** — JSON written to `<project>/_BIM_COORD/drawing_types.json`.
7. **Routing**: in the JSON file, prepend a routing rule sending your
   `discipline + docType` to the new profile id. Or: rename the corporate
   profile id with the same value so your project-scoped clone wins by id.
   (See "Routing" below for the cleaner approach.)
8. **DOCS → Automation → Batch Views** — every plan it creates uses your
   profile.

You're done. Every sheet has correct scale, view template, slot layout,
annotation, ISO sheet number — and they all match each other.

---

## Where everything lives (file map)

| What | Where | Editable by |
|---|---|---|
| Corporate Drawing Types catalogue (40) | `<plugin>/data/STING_DRAWING_TYPES.json` | BIM manager only — edits flagged via SHA-256 drift |
| Corporate Style Packs (11) | `<plugin>/data/STING_VIEW_STYLE_PACKS.json` | Same — corporate-locked |
| Project-specific Drawing Types | `<project>/_BIM_COORD/drawing_types.json` | Project team — created on first Save |
| Project-specific Style Packs | `<project>/_BIM_COORD/view_style_packs.json` | Project team — created on first Save |
| Thumbnail cache | `%TEMP%/sting_drawing_thumbs/` | Auto-managed |
| Activity log | `<plugin>/StingTools.log` | Read-only diagnostic |

**Rule of thumb**: never hand-edit the corporate JSON. Use the editor dialog
(DOCS → Edit Types) — it writes only project overrides.

---

## The 8 buttons on the DOCS tab

Under **📐 DRAWING TYPES**:

| Button | What it does |
|---|---|
| **Edit Types** | Open the two-tab editor (Drawing Types + View Style Packs) |
| **Inspect** | Read-only catalogue + routing table + validator report + drift count |
| **Reload JSON** | Re-read the JSON from disk after hand edits |
| **Group Browser** | Tells you whether the "STING - by Drawing Type" Project Browser organisation is set up + active. Manual one-time setup per project (Revit's API doesn't expose creation) |
| **Sync Styles** | Re-apply each stamped view's profile (catches drift after a profile/pack edit) |
| **From Scope Boxes** | Walk every scope box named `STING::<profile>::<level>::<tag>` and create + style + crop a view per box |

(Two more diagnostic commands ship as classes but aren't on a button:
`DrawingTypes_FromScopeBoxes` is the only one users routinely click.)

---

## How a sheet actually gets produced (the pipeline)

When any STING command creates or restyles a view, this 8-step pipeline runs
inside its Transaction:

1. **Lock check** — if the view carries `STING_STYLE_LOCKED_BOOL = 1` it's
   skipped (you froze it on purpose).
2. **Stamp DrawingType id** — `STING_DRAWING_TYPE_ID_TXT` is written so the
   browser organiser can group by it and SyncStyles knows the source profile.
3. **Scale** — `view.Scale = profile.Scale`
4. **Detail level** — Coarse / Medium / Fine
5. **View template** — looked up by name + applied
6. **Crop strategy** — scope box, tight bbox, room boundary, or none (with mm
   margin)
7. **View style pack** — graphic overrides + filters from the referenced pack
8. **Annotation pass** — auto-dim grids + levels, auto-tag rooms / doors /
   windows / equipment / welds / bends / supports, pick tag families from the
   pack

For sheet creation paths (fab, sheet manager), two extra steps run after the
sheet is minted:

9. **Stamp DrawingType id on the sheet itself** (sheet level, not view level)
10. **Title-block parameter binding** — every entry in the profile's
    `titleBlockParams` map is written into the title-block instance, with
    `${PRJ_ORG_xxx}` and `{disc}/{lvl}/{sys}/{seq:Dn}` token substitution

Every step is `try/catch`-wrapped — a partial failure on one step (missing tag
family, unloaded view template) becomes a warning, not a crash. The run
continues and you get a list of what to fix in the result dialog.

---

## Tokens you can use anywhere a pattern appears

These work in `sheetNumberPattern`, `sheetNamePattern`, and any
`titleBlockParams` value template:

| Token | Resolves to | Source |
|---|---|---|
| `{project}` | Project code | `ProjectInformation.PRJ_ORG_PROJECT_CODE` |
| `{originator}` | Originator code | `ProjectInformation.PRJ_ORG_ORIGINATOR_CODE` |
| `{vol}` | Volume / system | `DrawingType.IsoNaming.Volume` |
| `{type}` | Document type | `DrawingType.IsoNaming.Type` (DR / SH / VS / M3 / SP) |
| `{role}` | Discipline role | `DrawingType.IsoNaming.Role` (A / S / M / E / P / FP) |
| `{suit}` | Suitability | `DrawingType.IsoNaming.Suitability` (S0–S7 / A1–A5 / B1–B5) |
| `{rev}` | Revision | `DrawingType.IsoNaming.Revision` (P01 / C01 / …) |
| `{disc}` | Discipline letter | derived |
| `{discipline}` | Full discipline name | derived |
| `{sys}` | System code | from element / spool |
| `{lvl}` | Level code | derived |
| `{spool}` | Spool number | fabrication assembly |
| `{mark}` | Section / detail mark | from view |
| `{seq}` | Sequence (4-digit padded) | counter |
| `{seq:D2}` / `{seq:D3}` / `{seq:D4}` | Sequence with explicit pad width | counter |

In `titleBlockParams` only: `${PRJ_ORG_PROJECT_CODE}` style references read any
project info parameter directly.

Unknown tokens pass through as literal text — so a pattern like
`A-{lvl}-{seq:D3} (REV {revision})` resolves the recognised tokens and leaves
`{revision}` alone for hand-finishing.

---

## ISO 19650 sheet naming explained

The format every UK / EU public-sector job needs:

```
<Project>-<Originator>-<Vol>-<Lvl>-<Type>-<Role>-<Number>-<Suit>-<Rev>
   PLNS  -    ABC     - 01 - L02 - DR  -  A  -  0003  - S2  - P01
```

Each segment:

| Segment | Meaning | Allowed values | Where it comes from |
|---|---|---|---|
| **Project** | Project code | Up to 6 chars, A-Z 0-9 | `PRJ_ORG_PROJECT_CODE` (set once per project) |
| **Originator** | Who authored it | 3-letter co. code | `PRJ_ORG_ORIGINATOR_CODE` |
| **Vol** | Volume / system | `01`, `02`, `ZZ` | `DrawingType.IsoNaming.Volume` |
| **Lvl** | Building level | `GF`, `01`, `B1`, `RF`, `XX` | derived from view |
| **Type** | Document type | `DR` drawing · `SH` schedule · `M3` 3D model · `M2` 2D model · `VS` visualisation · `CA` calculation · `SP` specification · `TC` technical query · `AN` analysis · `RP` report · `PR` programme | `DrawingType.IsoNaming.Type` |
| **Role** | Discipline | `A` arch · `S` struct · `M` mech · `E` elec · `P` public health · `FP` fire · `LV` low voltage · `G` general · `ZZ` not specified | `DrawingType.IsoNaming.Role` |
| **Number** | Sequence | 4-digit zero-padded | counter, per profile/level |
| **Suit** | Suitability | `S0` WIP · `S1` shared (coordination) · `S2` shared (info) · `S3` shared (review) · `S4` stage approval · `S5` published · `S6` issued for construction · `S7` for tender · `A1`–`A5` & `B1`–`B5` published variants · `C1`–`C3` published reissue | `DrawingType.IsoNaming.Suitability` |
| **Rev** | Revision code | `P01`–`P99` preliminary · `C01`–`C99` contract · `T01` tender · custom | `DrawingType.IsoNaming.Revision` |

**To turn it on for a profile:**

1. DOCS → Edit Types → pick (or clone) the profile
2. Open the **ISO 19650 naming** card
3. Pick values from the dropdowns (each is sourced from the standard)
4. Click **Generate ISO SheetNumberPattern**
5. Click **Generate ISO SheetNamePattern**
6. Save

**To set the project codes once:**

1. Project Information → fill `PRJ_ORG_PROJECT_CODE` (e.g. `PLNS`) and
   `PRJ_ORG_ORIGINATOR_CODE` (e.g. `ABC`)
2. Every sheet generated thereafter inherits both codes automatically — no
   per-sheet typing.

**Seven shipped profiles already carry ISO payloads:** `arch-plan-A1-1to100`,
`arch-section-A1-1to50`, `struct-plan-A1-1to100`, `mep-plan-A1-1to100`,
`elec-power-A1-1to100`, `pipe-spool-A1-1to50`, `pres-3d-axon-A1`. Use them as
templates.

---

## How to do common things — workflow recipes

### W1: Use a corporate profile out-of-the-box

1. **DOCS → Inspect** to confirm the profile's referenced assets are loaded
2. **DOCS → Automation → Batch Views** (or Sections / Elevations) — pick the
   discipline + level
3. The dispatcher resolves the profile from the routing table and applies it

That's it. No per-sheet decisions. The 40 profiles cover most production needs.

### W2: Customise a corporate profile for one project

1. **Edit Types** → pick the corporate profile → **Clone**
2. Rename id with your project's code so it's unique (e.g.
   `acme-arch-plan-A1-1to100`)
3. Right-hand cards: change title block, view template, ISO codes, etc.
4. **Save** → written to `<project>/_BIM_COORD/drawing_types.json`
5. Open the JSON and prepend a routing rule that points your discipline to the
   new id (see "Routing" below)
6. **Reload JSON** in case the cache didn't pick it up

Project routing wins because project rules are prepended to the corporate ones.
Corporate baseline is untouched.

### W3: Brand-new profile from nothing

1. **Edit Types → ＋ New** — gives you a blank profile with project origin
2. Fill the cards top-to-bottom
3. Add slots manually (Slots card has `＋ Add slot` with normalised X / Y / W /
   H so the layout works on any paper size)
4. Save

Faster path: clone the closest existing profile and edit, rather than starting
empty.

### W4: Make presentation drawings look different from production

The presentation pack ships with this already wired:

- Production: `arch-elev-A1-1to100` → mono, dimensioned, hard line weights
- Presentation: `pres-exterior-elev-A1` → material callouts, halftone links,
  lighter weights, `STING_TB_SHEET_A1_PRESENTATION` title block

**To switch a project to presentation output:** set the project's phase (or your
custom `PRJ_ORG_PHASE` parameter) to `PRESENTATION`. The routing rules use
`phase: "PRESENTATION"` as the discriminator, so the same `ELEVATION` doc type
dispatches to the presentation profile automatically.

For a single sheet (one client meeting): clone the presentation profile, change
its routing rule to match a specific `mark` or `level`, save.

### W5: Generate 50 sheets in one batch

1. **Inspect** first — confirm the profile validates against the project (no
   missing title block / view template)
2. Place scope boxes if the profile's crop strategy is `ScopeBox` or
   `ScopeBoxOrBbox`
3. **Batch Views** → pick disciplines + levels → confirm
4. Watch the result dialog — created / updated / warnings counts
5. Sheets land in the project, browser groups them under the DrawingType node
   if you've set the Browser Organisation up

Time per sheet: <2 seconds. Time spent reviewing them: 0 — they all match.

### W6: Auto-create views from scope boxes

Useful when you want one view per box without manually picking each one.

1. Place scope boxes in a plan view
2. **Rename each scope box** to follow the magic pattern:
   `STING::<drawing-type-id>::<level-code?>::<tag?>`
   - `STING::arch-plan-A1-1to100::L02`
   - `STING::pipe-spool-A1-1to50::L01::HWS`
3. **DOCS → From Scope Boxes** → walks every matching scope box, creates the
   view, applies the profile, crops to the box
4. **Idempotent** — re-running finds existing views with the same
   (DrawingType, scope-box) pair and re-applies the style instead of creating
   duplicates

The fastest way to spin up a coordinated set of plans across multiple zones.

### W7: Sync all views after editing a corporate style

Imagine you edited `corp-standard-plan` to change the dimension style from
`STING - Linear` to `STING - Ordinate`. Forty existing plan views still use the
old setting.

1. **Edit Types → View Style Packs** tab → edit the pack → Save
2. **Reload JSON**
3. **Inspect** → headline reports `Drift: 38 view(s) drifted`
4. **Sync Styles** → preview lists the first 10 affected views → confirm
5. All 40 views snap to the new pack settings inside one Transaction

Lock individual views first (`STING_STYLE_LOCKED_BOOL = 1`) if you've
hand-tuned them and don't want them resynced.

### W8: Switch a project from informal numbering to ISO 19650

1. Set `PRJ_ORG_PROJECT_CODE` and `PRJ_ORG_ORIGINATOR_CODE` once in
   Project Information
2. **Edit Types** → for each profile in use:
   - Open ISO 19650 naming card
   - Pick volume / type / role / suitability / revision from the dropdowns
   - Click **Generate ISO SheetNumberPattern**
   - Save
3. New sheets created from the profile use the ISO pattern
4. Existing sheets keep their old numbers — to renumber them, use Revit's
   built-in "Renumber Sheets" or a STING bulk-rename command

### W9: Diagnose why a sheet looks wrong

1. **Inspect** — top-level summary tells you missing assets + drift count
2. **Edit Types → Validation strip** at the bottom of the form — per-profile
   error/warning codes (DT-010 missing title block, DT-040 missing tag family,
   etc.)
3. Open `<plugin>/StingTools.log` for the per-step warning trace from the last
   batch run (look for `DrawingTypePresentation`, `AnnotationRunner`,
   `ViewStylePackApplier` lines)

---

## Routing — how the dispatcher picks a profile

The routing table lives at the bottom of `STING_DRAWING_TYPES.json`:

```json
"routing": [
  { "discipline": "A", "phase": "*", "docType": "PLAN",      "drawingTypeId": "arch-plan-A1-1to100" },
  { "discipline": "M", "phase": "*", "docType": "SPOOL",     "drawingTypeId": "duct-spool-A1-1to50" },
  { "discipline": "*", "phase": "PRESENTATION", "docType": "ELEVATION", "drawingTypeId": "pres-exterior-elev-A1" }
]
```

When a generator command needs to know "which profile do I use for this view?",
it calls `DrawingDispatcher.Resolve(doc, discipline, phase, docType)`. The
resolver walks the routing table from top to bottom and returns the **first**
rule whose `discipline` / `phase` / `docType` all match (`*` is a wildcard).

**Project routing rules win**: rules in `<project>/_BIM_COORD/drawing_types.json`
are *prepended* to the corporate ones, so a project rule for `("A", "*",
"PLAN")` shadows the corporate `("A", "*", "PLAN")` line entirely.

**Conditional rules** (Week 6): a rule may also carry regex predicates:

```json
{
  "discipline": "M", "docType": "PLAN",
  "levelMatches": "^B\\d+",
  "drawingTypeId": "mep-basement-plan-A1-1to50"
}
```

This rule only fires for MEP plans on basement levels. Useful for "everything
in the plant rooms uses 1:50, everything else uses 1:100" without a separate
profile per level.

**Available predicates**: `disciplineMatches`, `phaseMatches`, `docTypeMatches`,
`levelMatches`, `projectCodeMatches`. All set predicates must match (logical
AND). Use sparingly — most projects do fine with the wildcard rules.

---

## Prefabrication drawings — a special case worth its own section

Fabrication shop drawings are different from production drawings in three ways:

1. **One sheet per assembly** — every spool / duct module gets its own A1
2. **ISO 6412 isometric symbols** — welds, bends, supports, flanges all carry
   numbered glyphs that match a parts list
3. **The shop reads them, not the architect** — line weights are heavier,
   dimensions are ordinate (zero-based from one corner), the title block needs
   spool number / weight / fab location / status

STING handles fab via a dedicated pipeline: **TAGS → Fabrication → Generate
Package**. It runs in this order:

```
Generate Package
  ├── Collect MEP elements (selection / view / project)
  ├── For each discipline (Pipe / Duct / Conduit / Hanger):
  │     ├── FabricationCoordinator.Group elements into assemblies
  │     ├── AssemblyBuilder.Create one Revit AssemblyInstance per group
  │     ├── AssemblyViewBuilder.BuildViews — Plan + ISO + Elev0 + Elev90 +
  │     │   3D + BOM schedule + Material takeoff
  │     ├── IsoSymbolPlacer drops weld / bend / support symbols on the views
  │     └── ShopDrawingComposer.ComposeSheet — minted ViewSheet, places views
  │         at fixed slot positions, populates title-block cells
  └── Per-discipline CSV / PCF / MAJ exports
```

### Where the Drawing Template Manager kicks in

Two profiles ship for fab:

- **`pipe-spool-A1-1to50`** — for piping work, points at
  `STING_TB_ASSEMBLY_PIPE` title block, ordinate dimensioning, weld + bend +
  support tags enabled in the annotation pack
- **`duct-spool-A1-1to50`** — same shape but `STING_TB_ASSEMBLY_DUCT`,
  duct-specific tag families

`ShopDrawingComposer` consults the registry via a 3-tier fallback chain (no
regression risk):

1. **User options** from the `Configure…` dialog (one-off override per run)
2. **Drawing Type profile** if the registry has a match
3. **Historic hard-coded per-discipline lookup** (last resort)

So out of the box, fabrication produces sheets with the registry's title block,
sheet numbering pattern, and slot layout — and you can override any of it
without touching the registry by clicking `Configure…` on the fab toolbar.

### What the fab title block gets populated with

Two layers stack:

- **Hard-coded fab cells** (always written): `SPOOL_NR_TXT`, `WEIGHT_KG`,
  `FAB_LOC_TXT`, `FAB_STATUS_TXT`, `BOM_REV_TXT`, `DISCIPLINE`
- **Declarative `titleBlockParams` cells** (from the profile): the seeded
  `pipe-spool-A1-1to50` ships with 12 entries — Client Name, Project Code,
  Originator, Company Name, Company Address, Appointing Party, Discipline,
  Suitability, Sheet Status, Spool Number `{spool}`, System Code `{sys}`,
  Level Code `{lvl}`, Sheet Number `SP-{disc}-{sys}-{lvl}-{seq:D4}` (or full
  ISO if you've enabled it)

Same token set used for the sheet number resolves the title-block cells, so
the spool number on the title block always matches the spool number in the
sheet number — zero drift.

### Sheet number for a fab spool

Default: `SP-{disc}-{sys}-{lvl}-{seq:D4}` → e.g. `SP-P-HWS-L02-0003`

ISO 19650 (after running Generate ISO SheetNumberPattern):
`{project}-{originator}-{vol}-{lvl}-{type}-{role}-{seq:D4}-{suit}-{rev}` → e.g.
`PLNS-ABC-02-L02-SP-P-0003-A-C01`

Fab uses `type=SP` (specification / spool) and `suit=A` (delivery-team variant)
which differs from production drawings (`type=DR`, `suit=S2`). You set those
once in the profile's IsoNaming card — every spool sheet then inherits them.

### Slot layout for a fab spool sheet

Standard A1:50 slot map (you can edit any of this in the Editor's Slots card):

```
┌────────────────┬────────────────┬─────────┐
│  Plan          │  ISO           │  BOM    │
│  (TL)          │  (TR)          │  (RIGHT)│
├────────┬───────┼────────────────┤         │
│ Elev0  │ Elev90│  3D            │         │
│ (BL)   │ (ML)  │  (BR)          │         │
└────────┴───────┴────────────────┴─────────┘
```

All slots are normalised to 0..1 over the title-block's drawable zone, so the
layout works even if a different paper size is chosen.

### What makes fab sheets reliable in practice

- **Crop strategy** is `TightBbox` with 300 mm margin — every spool sheet's
  views auto-crop to the assembly with consistent breathing room
- **Detail level** is `Fine` — connector geometry, fittings, weld symbols all
  rendered
- **Annotation rule pack** has `autoTagWelds`, `autoTagBends`,
  `autoTagSupports` enabled and `dimensionStrategy = "Ordinate"` so dims
  read from one corner consistently
- **`denseUntilScale = 100`** — at scales coarser than 1:100, per-element
  tagging is skipped to keep the sheet legible
- **Sheet name pattern** `{discipline} spool {spool}` produces e.g.
  "Pipe spool SP-P-HWS-L02-0003"

### The shop's handoff

After Generate Package runs, the shop gets:

- **Sheet PDF** (use Batch Print → Sheets → filter by "STING" prefix)
- **Cut list CSV** (per-discipline, exported by ExportCutList)
- **Weld map CSV** (ExportWeldMap)
- **Isometric PCF** (ExportPcf — for plant-design integration)
- **MAJ XML** (ExportMaj — for fabrication MIS systems)

Each export reads from the same Drawing Type profile, so sheet number / spool
number / system code / level code are consistent across all of them.

---

## The cadence — daily, weekly, per-project

### Daily: just produce drawings

Run the batch commands. The registry does the work. Don't think about the
profile system.

### Weekly: catch drift

1. **Inspect** — note any drift count or new validation warnings
2. **Sync Styles** if drift > 0 — re-applies profiles to drifted views
3. Quick scan of `<project>/_BIM_COORD/drawing_types.json` for project-scoped
   variants that should be promoted to corporate (talk to the BIM lead)

### Per-project setup (once)

1. Project Information → set `PRJ_ORG_PROJECT_CODE`, `PRJ_ORG_ORIGINATOR_CODE`,
   `PRJ_ORG_CLIENT_NAME`, `PRJ_ORG_COMPANY_NAME`, `PRJ_ORG_COMPANY_ADDRESS`,
   `PRJ_ORG_APPOINTING_PARTY`
2. Load the title block families the corporate profiles reference
3. Run **Inspect** — confirm validator warnings are limited to assets you
   intentionally aren't using yet
4. **Group Browser** — follow the one-time steps to create the
   "STING - by Drawing Type" Project Browser organisation in Revit's UI
5. Save the project once so `<project>/_BIM_COORD/` exists

After that, the system runs itself for the project lifetime.

### Quarterly (BIM lead)

1. Open `<plugin>/data/STING_DRAWING_TYPES.json` and `STING_VIEW_STYLE_PACKS.json`
2. Compare to project overrides across recent jobs — what's diverging?
3. Promote useful project-specific variants back to corporate (after design
   review)
4. Bump the corporate JSON version, ship to the team — every project picks up
   the update on next open

---

## Troubleshooting cookbook

### "Sheets are produced but they look the same as before — the profile didn't apply"

- Did you press **Reload JSON** after editing? The registry caches per-document.
- Is the routing rule actually matching? Open Inspect, look at the routing
  table dump — find a rule that covers your discipline + docType.
- Are you using a generator command that consults the registry? Fab composer,
  Batch Sections, Batch Elevations, Batch Views (doc-package path), Sheet
  Manager CreateFromTemplate all do. View Manager / hand-created views do not.

### "Inspect reports 'Title block family STING_TB_SHEET_A1 not loaded'"

- The corporate profile references a title block your project doesn't have.
- Two fixes: (a) Load → File → Family → load `STING_TB_SHEET_A1.rfa` from your
  corporate templates folder, or (b) Edit Types → change the profile's title
  block to one that IS loaded.
- The validator only warns — generation still runs, picking the first available
  title block as fallback.

### "Sheet number is empty / wrong / missing the project code"

- ISO patterns need `${PRJ_ORG_PROJECT_CODE}` and `${PRJ_ORG_ORIGINATOR_CODE}`
  set in Project Information. Empty values resolve to empty strings, which
  produces `--01-L02-DR-A-0003-S2-P01`.
- Open Project Information → fill those parameters → re-run the batch command.

### "View has the right scale but the wrong tags"

- Annotation pass runs after view template — but if the view template locks the
  graphic style, the pack overrides won't stick.
- Either remove the view template's lock on the affected category, or accept
  that the template wins (and reflect that in the pack — set the same overrides
  in both).

### "I edited a corporate profile in the Editor but it shows back as 'project' origin"

- That's intentional. Editing a corporate profile clones it to project origin
  on save (so the corporate JSON on disk stays pristine). Your edits are saved
  to `<project>/_BIM_COORD/drawing_types.json`.
- To fold project edits back to corporate, the BIM lead reviews the project
  override JSON and copies relevant entries up to `STING_DRAWING_TYPES.json`.

### "DOCS → From Scope Boxes did nothing"

- Scope box names must follow the magic pattern exactly:
  `STING::<drawing-type-id>::<level-code?>::<tag?>`
- Exact id match is case-insensitive but exact-spelled. Open Inspect to check
  ids.
- Check the result dialog's Skipped count + warnings list for the reason.

### "The annotation pass tagged everything in the view, including elements I'd
already tagged manually"

- AnnotationRunner doesn't check existing tags before placing new ones — it's
  designed for fresh views. To prevent re-tagging, lock the view first
  (`STING_STYLE_LOCKED_BOOL = 1`) so SyncStyles skips it, and clear the
  annotation flags on the profile if you want a one-shot run.

### "Inspect says 'Drift: 12 view(s) drifted' — what does that mean?"

- A view is "drifted" when its actual scale / detail level / template doesn't
  match the profile its `STING_DRAWING_TYPE_ID_TXT` stamp points to. Either
  someone changed the view manually, or the profile changed after the view
  was created.
- **Sync Styles** brings them back in line. Don't worry about drift — a
  little is normal during design.

### "Sync Styles tried to re-apply but the view template stopped it"

- Active view template settings beat per-view overrides for whatever the
  template controls. The pack's pack-level VG override goes into the view
  template instead — which then propagates to every view using the template.
- Workaround: edit the corresponding view template's VG / filter settings
  directly, or remove the template from the view.

### "The Browser Organizer button told me to do something manually"

- Yes. Revit's API does not let plugins create or activate Browser
  Organisations — those operations are UI-only. The button's job is to tell
  you (a) whether the named org exists, (b) whether it's currently active,
  (c) how many DrawingType stamps are on your views.
- Manual setup is one-time per project template. Once done, every project
  spawned from that template inherits the org.

---

## Glossary — every term in one place

| Term | Plain meaning |
|---|---|
| **DrawingType** | A named JSON record describing how one kind of drawing should look — sheet size, scale, slots, annotation, numbering. 40 ship corporate; you clone + edit. |
| **ViewStylePack** | A reusable graphic-style payload (line weights, filters, VG overrides) that many DrawingTypes share. 11 ship corporate. |
| **Slot** | A normalised (0..1) rectangle on the drawable zone of a sheet, declaring "the Plan view goes top-left and takes 70% width × 80% height". |
| **Routing rule** | A `(discipline, phase, docType)` → DrawingType mapping. The dispatcher walks rules in order and picks the first match. |
| **Annotation rule pack** | The "what to auto-tag and how to auto-dim" payload inside a DrawingType. Consumed by AnnotationRunner during the pipeline. |
| **Crop strategy** | How a view's crop box is computed — scope box / tight bbox / room boundary / none. |
| **Style stamp** | The `STING_DRAWING_TYPE_ID_TXT` parameter on every STING-produced view, recording which profile produced it. Used by browser org + sync styles. |
| **Style lock** | The `STING_STYLE_LOCKED_BOOL` parameter — when 1, SyncStyles skips the view. Lock hand-tuned views. |
| **Drift** | A stamped view whose scale / detail level / template no longer matches its profile. Found by `DrawingDriftDetector`, fixed by `Sync Styles`. |
| **Project override** | A JSON file at `<project>/_BIM_COORD/drawing_types.json` (or `view_style_packs.json`) that supplies project-specific variants without mutating the corporate baseline. |
| **Token** | `{disc}` / `{lvl}` / `{seq:D4}` style placeholders that resolve at sheet-creation time using the caller's context. |
| **ISO 19650** | The British / European standard for naming construction documents. Defines the 9-segment sheet number STING produces. |
| **Suitability** | The ISO 19650 status code — `S0`–`S7` (delivery-team variants) and `A1`–`A5` / `B1`–`B5` (published variants). |
| **Originator** | The 3-letter company code that authored the drawing. Lives in `PRJ_ORG_ORIGINATOR_CODE`. |
| **Volume** | An ISO 19650 grouping for cross-system content. `01` is a typical building volume; `ZZ` means "not applicable". |
| **Spool** | A pre-fabricated assembly of pipework / ductwork shipped as one unit. STING creates one sheet per spool via the fab pipeline. |
| **Inspect** | The read-only diagnostic command — catalogue, routing, validator, drift count. Run it weekly. |
| **Sync Styles** | The propagate-changes command — re-applies each stamped view's profile after a profile / pack edit. |
| **Validator** | The pre-flight checker (`DrawingTypeValidator`) — finds missing title blocks, view templates, tag families before generation runs. |
| **Title-block parameter binding** | The declarative `titleBlockParams` map on a profile — every entry is written into the title-block instance with `${PRJ_ORG_xxx}` + token substitution at sheet creation. |

---

## Where to dig deeper

- **`docs/STING_DRAWING_TYPES.json`** — read the corporate catalogue source
- **`docs/STING_VIEW_STYLE_PACKS.json`** — read the corporate style packs
- **`<plugin>/data/CLAUDE.md` "Drawing Template Manager" section** — architectural
  reference, class names, file paths for engineers
- **Inspect command output** — fastest way to see what's loaded + what's drifted
- **`<plugin>/StingTools.log`** — per-step warnings from the last batch run

---

## One-paragraph summary for your colleagues

> STING ships 40 ready-made drawing-type profiles and 11 visual style packs.
> Every batch command (fab, sections, elevations, views, sheets) reads from
> the same registry — so every drawing you produce automatically picks the
> correct sheet size, title block, scale, view template, slot layout,
> annotation, and ISO 19650 sheet number. Editing a profile updates every
> sheet using it. Editing a style pack updates every profile using it. Project
> overrides let you customise without breaking corporate. Open the Edit Types
> button on the DOCS tab — you'll see the catalogue, edit it like a list of
> recipes, save, and watch the next batch run produce coordinated drawings
> instead of 50 freelancers.
