# Drawing production — the Revit check (DTW-82)

Nothing merged in the drawing-types review loop (rounds 1-9, DTW-1..227) has been run
in Revit. The worklog's *NEEDS REVIT CHECK* list is the full inventory
([`WORKLOG_DRAWING_TYPES.md`](WORKLOG_DRAWING_TYPES.md)). This script splits it in two:

- **Automated — one click.** `DrawingTypes_SelfTest` (dock → **DOCS** → **📐 DRAWING
  TYPES** → **Self-Test**) runs every check the open model can answer by itself, inside a
  TransactionGroup it always **rolls back**. Section 1.
- **Manual.** What needs a second user, a keypress, Undo, or eyes on the screen.
  Sections 2-5, in priority order.

Roughly 90 minutes for P1 + P2 on a real project. Use a **copy** of a real model for the
manual steps: unlike the self-test, they keep what they make.

---

## 0. Make sure you are running THIS build

```bash
grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u
```

That path is what Revit loads. Building into another folder succeeds silently and you
then test code that never ran. Close Revit (and the Planscape Companion tray app) before
deploying. The log is **date-stamped**: `StingTools_yyyyMMdd.log` next to the DLL.

---

## 1. P1 — Self-Test (automated)

1. Open the model. Run **Load Shared Parameters** first if it has never been run on it.
2. Dock → **DOCS** → **📐 DRAWING TYPES** → **Self-Test**.
3. Wait. It produces one plan twice, so allow a minute on a large model.

**Expected:** a result panel headed *ROLLED BACK*, a CSV under the project's Validation
export folder (`STING_DrawingSelfTest_<timestamp>.csv`), and afterwards **no change to
the model**: no new view, sheet, filter, template or line in the Project Browser, and
nothing to undo. Check that last point — it is the command's own contract.

| Check | What it proves | Worklog item |
|---|---|---|
| a. Bindings | every Views / Sheets parameter in `tools/drawing_binding_contract.json` is bound to its category; `PRJ_SHEET_BIM_MODE_TXT` to Project Information; `STING_MATCH_*` bound to Lines **and** resolvable on a new detail line | DTW-55..59, DTW-152, **DTW-56** |
| b. Stamp and re-run | `STING_DRAWING_TYPE_ID_TXT` / `STING_VIEW_CONTEXT_TAG_TXT` write and read back; Produce Per Level twice on one level makes no second view or sheet | "re-produce, no duplicates" |
| c. Managed V/G | a managed pack's template controls V/G Model and V/G Filters and carries the pack's filters; which scale parameter the template exposes | DTW-163, DTW-170, round 8 |
| d. AEC filters | five filters created (healthcare `clin-press-negative`, `phase-demolished`, `struct-concrete`, MEP, electrical); which Revit refuses; whether PHASE_CREATED / PHASE_DEMOLISHED / STRUCTURAL_MATERIAL_TYPE are filterable on walls and floors | DTW-164, DTW-166, round 8 |
| e. Fill patterns | "Solid fill" resolves to a pattern with `IsSolidFill` | DTW-165 |
| f. Title blocks | the A3 spec resolves to an A3 `.rft`; a schedule placed in a slot has its top-left inside the slot | DTW-149, DTW-151 |
| g. Sheet numbering | the project pattern and both policies (Profile, ISO) give well-formed numbers; the level gets a valid ISO level code | DTW-44, DTW-105 |
| h. Worksharing | the pre-flight verdict for the produced item and the sheet counters (SKIP when not workshared) | DTW-195, DTW-220 |
| i. Match lines | with two adjacent STING-AREA boxes, Generate twice leaves the curve count unchanged | DTW-47, round 3 #6 |
| j. Crop | whether a view-scoped collector excludes elements outside the crop | round 8 |

**Reading it.** PASS and FAIL are verdicts. SKIP always says what the model lacked —
run it again on a model that has it. INFO rows answer open design questions; copy them
into the worklog. Two of them decide work:

- **`STING_MATCH_* → Lines` / "on a detail line"**: *REVIT REFUSED LINES* means DTW-56
  is decided — the match-line keys move to Extensible Storage.
- **Scale parameter id** and **view-scoped collector vs crop**: the answers to the round 8
  questions; record them against DTW-170 and DTW-172.

Paste the CSV into the worklog's NEEDS REVIT CHECK section. Inside a workflow preset the
command shows no dialog, writes the report to the log, and returns Failed when any row
fails.

---

## 2. P1 — manual

### 2.1 Re-run idempotency, by hand

The self-test proves it on one level and one type. Prove it on a real run:

1. **Produce Per Level**, three levels, two plan types. Note the view and sheet counts
   in the Project Browser.
2. Run it again with the same choices.

**Expected:** the report says reused; counts unchanged; no `(1)` suffixes on view names;
sheet numbers unchanged.

3. Rename "Level 1" to "Ground Floor" and run it a third time.

**Expected:** still no new views, sheets or numbers; the reused sheets' **names** follow
the new level name (DTW-209 / DTW-221).

### 2.2 Managed V/G is visible

1. Produce an MEP plan whose pack is managed (`corp-mep-*`) and a healthcare plan
   (`corp-healthcare-*`).
2. Open each.

**Expected:** the pack's overrides and filter fills **show** — coloured duct/pipe
systems, healthcare pressure-regime fills. Visibility/Graphics → Filters lists the
pack's filters and is greyed (template-controlled). A plan that is visually plain while
Self-Test check c passed is a rendering problem, not a binding one: write down which.

### 2.3 Worksharing with two users

Needs two Revit sessions on one central (two machines, or two user names).

1. User B opens one produced plan and moves its crop (B now owns the view). Do **not**
   sync.
2. User A runs **Produce Per Level** for that level and type plus one other level.

**Expected:** that one item is **skipped with a line naming user B**; the other level is
produced; no Revit "cannot edit element" dialog; nothing else rolls back (DTW-195).

3. User B edits Project Information (owns it), no sync. User A produces a level that
   needs a **new** sheet.

**Expected:** one run-level note about the sheet-number counters; items that reuse their
sheet still run; the new-sheet item is refused, not numbered with a colliding number
(DTW-194 / DTW-220).

4. Both sync. A re-runs. **Expected:** everything completes.

---

## 3. P2 — Undo and Escape

### 3.1 Undo

1. Run **Produce Per Level** (one level, one type, new sheet).
2. Ctrl+Z once per transaction the Undo list shows for it.

**Expected:** the view and sheet are gone, the next run takes the **same** sheet number
(the counter rolled back with the model), and the log has no error.

3. Renumber a sheet with **Renumber**, undo it, then run **Sheet Number Restore**.

**Expected:** no history collision; the restore offers the right put-back (DTW-200).

### 3.2 Escape mid-run

1. Produce Per Level for 10+ levels so the progress window appears.
2. Press **Escape** (or the window's Cancel) after two or three items.

**Expected:** the run stops after the item in hand; the report says how far it got
("stopped after N of M"); what committed stays; no half-made view or empty sheet
(DTW-204).

---

## 4. P2 — how it looks

Each step: produce, open, look. Write down what you see, not whether it "looks right".

| # | Do | Expected |
|---|---|---|
| 4.1 | Produce an A3 type (`arch-detail-A3-1to20`) and an A1 plan | A3 border on the A3 sheet, **no** A1 border over it; **one** revision table per sheet, not two (DTW-149, DTW-156) |
| 4.2 | Build one master-path title block (TB factory) from a master that already has a revision table | one revision schedule in the family |
| 4.3 | Produce a sheet with a schedule slot (`door-schedule-A2`) | the schedule sits inside its slot's frame, top-left aligned; nothing runs off the slot edge (DTW-151) |
| 4.4 | Fire Alarm Schematic, twice | one view "STING - Fire Alarm Schematic", still on its sheet, no "Drafting N" |
| 4.5 | Drainage Schematic on a model with a stack and a vent | the stack at its real levels with level names; vent only where a vent pipe connects; no invented branches. No stack: no view, and a message saying why |
| 4.6 | Supply Schematic on a model with a water meter | source = the meter; no kPa labels without `plumbing_system_config.json`; glyphs about 3 mm on paper |
| 4.7 | MEP coordination step (MEPDrawingProduction) | plans coloured by system; with a template controlling filters, the template is coloured once |
| 4.8 | Healthcare filters on a healthcare plan | fills render (solid, not blank); no Revit warning on creation |
| 4.9 | Room / space / area tags on a plan | one tag per room at its location; a re-run adds none; a manual tag is left alone |
| 4.10 | Rotated grids at 30° | two dimension chains, perpendicular to their grid sets |
| 4.11 | Interior elevations on one room | four compass-named views, one marker, on the room's own plan |
| 4.12 | Exterior elevations, twice, 1+4 | four faces on one sheet; no duplicates on the second run |

---

## 5. P3 — renaming flows and the Excel round-trip

### 5.1 Renaming

| Rename | Then | Expected |
|---|---|---|
| A board (panel) | re-run the panel door diagram | the same sheet, not a duplicate |
| A STING-AREA box | Match Line Generate | lines follow; captions show the sheet refs; no orphan curves |
| A level that a `STING::<type>::L01` box sits on | produce from scope boxes | produces on that level (box resolves by id, not name) |
| A drawing type id, with `replaces` naming the old id | produce | the old sheets are adopted and re-stamped, not duplicated (DTW-203) |

### 5.2 Excel round-trip in de-DE

1. Windows → Settings → Time & language → Region → **German (Germany)** (decimal comma).
   Restart Revit.
2. **↓ Export to Excel**. Open the workbook; change one scale and one margin
   (e.g. `12,5`). Save.
3. **↑ Import from Excel**.

**Expected:** the preview lists exactly the two edits; import succeeds; reopening the
editor shows `12.5` mm; an unedited second import writes nothing to the project
override (DTW-181 / DTW-182). Set the region back afterwards.

---

## Recording results

Do not record results here. Paste the Self-Test CSV and one line per manual step into
the worklog's NEEDS REVIT CHECK section, and move anything that failed into its
findings table with a new DTW number.
