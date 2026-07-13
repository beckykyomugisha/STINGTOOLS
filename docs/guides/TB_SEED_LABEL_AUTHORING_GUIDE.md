# Seed `.rfa` Label-Authoring Guide — STING Title Blocks

The one thing code can't do. **Revit 2025 removed `Document.FamilyCreate.NewLabel`**, so
title-block **label cells** can only be created/rebound by hand in the Family Editor. All the
data (shared params, JSON label contract, drawing-type stamp maps) is already in place — each
cell "lights up" the moment you author its label. This guide is the manual half.

Master seed: **`STING_TB_A1_BIM_v2.0.rfa`** at
`D:\Work 2026\tendo test\Families\TitleBlocks\_seeds\`. Do the A1 first, then propagate.

---

## 0. Before you start (once)

1. **Point Revit's shared-parameter file at the deployed set** so the renamed/new params are
   available: *Manage → Shared Parameters → Browse* →
   `C:\Dev\STING_PLACEMENT_GOLD\data\MR_PARAMETERS.txt` (or the branch copy). Re-Browse the same
   file to force a fresh read if you edited it.
2. **Two monitors:** Edit-Label / Type-Properties dialogs may open on the *other* screen while
   the canvas stays on the main one. If Revit "stops responding" to clicks, a modal dialog is
   waiting on the second monitor.
3. Work at a **zoomed-in** view of the strip — the cells are small; picks land wrong when zoomed out.

---

## 1. Core recipe — author one label cell

1. **Create** tab → **Label** → click where the cell goes. *Edit Label* opens.
2. If the param is **not** in the left *Category Parameters* list, add it to the pool:
   pool-add icon (bottom-left of the list) → *Parameter Properties* → **Select…** →
   choose **group** (`PRJ_INFORMATION` holds the `PRJ_*` params) → pick the param → **OK** → **OK**.
   > ⚠ **NEVER close Edit Label before moving the param to the right side** — a pooled-but-unused
   > param is dropped when the dialog closes and you'll have to re-add it.
3. Select the param in the left list → click the green **→** (add to label).
   Set **Prefix** = the caption you want printed (e.g. `SECURITY: `) and a **Sample Value**.
4. **OK.** The label appears at the oversized default type (**7 mm**).
5. **Fix the size:** select the label → **Edit Type** → *Type* dropdown → pick a small type —
   **`2`** (2 mm regular) for metadata rows, **`B 2`** (2 mm bold) for value cells — → **OK**.
6. **Position:** drag into the cell; use *Modify → Align* to line it up with the neighbouring
   cells (match their left inset + baseline).

**Faster alternative — copy an existing cell.** Select a correctly-styled label, **Copy/Paste**,
drag it to the target cell, then **Edit Label** to rebind the param and change the Prefix. It
inherits the type/size, so you skip step 5. This is the reliable way to keep a row consistent.

**Static text** (fixed notices like DO-NOT-SCALE / copyright) is easier: **Create → Text**,
click, type, then set its text type to `2mm`. No param, no pool.

**Do NOT blind-type on the canvas** with an element selected — letters fire keyboard shortcuts
(e.g. `RO` = Rotate). Always edit text via select → *Edit Text*, and label captions via Edit Label.

---

## 2. The remaining cells to author / rebind (A1 master)

### 2a. DRAWING TITLE → built-in **Sheet Name**
The contract binds it to `{"param":"Sheet Name","builtin":true}`. If the cell still shows a
project-name binding, delete it and add a Label bound to the Revit **built-in "Sheet Name"**.
(The separate PROJECT cell must stay on `PRJ_ORG_PROJECT_NAME_TXT` — don't touch it.)
*In the current seed this already reads "Drawing title" (= Sheet Name) — just confirm.*

### 2b. Box 3 of the bottom row: **PURPOSE → LOD**
You authored `PURPOSE:` bound to `PRJ_DWG_ISSUE_PURPOSE_TXT`; the data now expects **LOD**.
Rebind that cell to **`PRJ_DWG_LOIN_LOD_TXT`** and change the caption to `LOD: ` (sample e.g.
`LOD 300`). Row becomes `SECURITY | CDE REF | LOD | SYSTEM`.

### 2c. Suitability — show the **code**, add the description
- SUITABILITY chip: keep `PRJ_DWG_SUITABILITY_COD_TXT`, set its **sample to a code** (`S4` / `A`),
  not "CONSTRUCTION" — that removes the old overlap with the purpose cell.
- Optionally add a small cell bound to **`PRJ_DWG_SUITABILITY_DESC_TXT`** (the code's plain-English
  description; the applier now auto-fills it from the code).

### 2d. Rebind any cell still showing an **old `STING_*` name**
The rename preserved GUIDs, so a cell bound to a former `STING_*` param keeps working but displays
the OLD name until re-picked. Select each such cell → Edit Label → re-pick the new name (same GUID).
Contract-expected labels that were renamed — check these exist and read the new name:

| Cell / strip | New param |
|---|---|
| ISO 7-segment ID strip | `PRJ_SHEET_PROJECT_/ORIG_/VOLUME_/LEVEL_/TYPE_/ROLE_/SEQ_TXT` |
| Full sheet ref | `PRJ_SHEET_FULL_REF_TXT` |
| "n of total" | `PRJ_SHEET_OF_TOTAL_TXT` |
| System | `PRJ_SHEET_SYSTEM_TXT` |
| Federation status | `PRJ_TB_FEDERATION_STATUS_TXT` |
| Authorised by / date | `PRJ_TB_AUTHORISED_BY_TXT` / `_DATE_TXT` |

### 2e. Optional info cells
Copyright / DO-NOT-SCALE can stay **static text** (they are today) or be bound to
`PRJ_TB_COPYRIGHT_TXT` / `PRJ_TB_DO_NOT_SCALE_TXT` if you want per-project overrides. Contact block
labels use `PRJ_ORG_CONTACT_PHONE_/EMAIL_/WEBSITE_TXT` + `PRJ_ORG_REG_NO_TXT`.

### 2f. Static **graphics to bake into the family** (not labels)
These are view-independent, so they DO belong in the `.rfa`:
- **Projection symbol** (1st/3rd-angle) — draw with symbolic lines or load a small symbol.
- **Company logo** — image/raster in the logo cell.

---

## 3. What is NOT `.rfa` work (don't hand-author these)

North arrow, scale bar, key plan, discipline legend, and QR are **placed onto sheets by
commands**, not drawn in the title block (they're view-/scale-/sheet-dependent). To use them:
1. Run **`TitleBlock_BuildGraphicsFamilies`** once (builds the annotation `.rfa`s; confirm it
   reports success, not `[FAIL]`).
2. Ensure the title block reserves each slot — either the JSON slot bounds
   (`STING_TITLE_BLOCKS.json`, already present) or a named reference plane in the `.rfa`.
3. Run **`TitleBlock_StampSheetGraphics`** (or `…All`), each graphic gated by its
   `PRJ_TB_SHOW_*` toggle. Re-running replaces, doesn't duplicate.
> Note: after the review fix, **QR now SKIPS when the title block has no `qr-code` slot** (it no
> longer auto-drops to the bottom-right corner). Make sure the slot exists if you want QR.

---

## 4. Propagate A1 → other sizes

Once the A1 master is complete, generate the other sizes (A0 / A3, portrait variants) with the
factory's **master-seed propagation** — it copies the whole design (labels, captions, lines,
filled regions) with paper-ratio position remap and ISO 3098 text-tier stepping (A3 drops one
tier). You author once, on the A1.

---

## 5. Verify before you trust it

For each authored cell: select it → **Edit Label** shows the expected param (new name, no
`STING_*`). Then load the family into a test project on a **seeded** sheet and confirm:
- Security / CDE / LOD / System populate; **suitability shows the code** and its description fills
  (no "PRJ_DWG_SUITABILITY_COD_TXT is empty" validator warning).
- Sheet-ID strip + full-ref render.
- No cell prints a raw parameter name (that means an unbound/mis-bound label).

Keep `SEED_FOLLOWUP.md` as the live checklist; tick items here as you author them.
