# Kibale integration — Revit verification plan

Everything in `claude/kibale-integration` is **compile-verified only**. This document is
the checklist for a real model, and it is the actual risk in this change: the batch alters
numbers that end up in a bill, and no gate that has been run so far can see a wrong
quantity — only a wrong *build*.

**Nothing here should be merged until items 1, 2 and 4 have passed and a QS has signed off
the take-off figures.**

## Before you start

| | |
|---|---|
| Build | `deploy.bat` from this checkout, then restart Revit. Confirm the manifest points here: `grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin \| sort -u` |
| Model | A real project with masonry walls, floors, rooms with finishes, and at least one sheet already issued. A partially-populated model is *better* than a complete one for items 3 and 6. |
| Baseline | **Before installing this build**, run the current production build and export: a BOQ to XLSX, the sheet register to CSV, and `StingTools.log`. You cannot tell a step change from a steady state without a before. |
| Log | Clear `StingTools.log` between items so each is attributable. |

---

## 1. `lookup()` — the highest-risk change in the batch

**29 call sites went from writing `0` to writing real numbers.** Nothing else here has
that reach. If this is wrong, it is wrong across cement, sand, aggregate, blocks, bricks,
formwork, rebar laps, tiles, adhesive, grout, paint, primer, putty and carbon.

**Do:**
1. Pick **one wall** and **one slab** with known geometry. Record area/volume from Revit.
2. Run the formula evaluator (TEMP → Formula Evaluator, or any tagging command that runs
   the pipeline).
3. Read back the `CST_S_*` and `BLE_FINISH_*` parameters on those two elements.
4. Hand-calculate the same quantities from `StingTools/Data/MATERIAL_LOOKUP.csv` — the
   table, key and column are visible in each formula's `Revit_Formula` in
   `FORMULAS_WITH_DEPENDENCIES.csv`.
5. Have a QS repeat the take-off independently from drawings.

**Pass:** plugin figures match the hand take-off within rounding on both elements. Cement
bags, sand m³ and block counts are the ones to check hardest — they carry the most money.

**Fail:**
- A quantity that is still `0` where the hand take-off is non-zero → the lookup is
  resolving to a missing row. Check `StingTools.log` for
  `lookup(TABLE,KEY,COLUMN) found no value`.
- A quantity out by a clean factor (2×, 10×, 1000×) → a unit-basis error like the mortar
  one. **Stop and check the column's own `Unit`/`Description` in `MATERIAL_LOOKUP.csv`
  before trusting any other lookup-derived figure.**
- A quantity that looks plausible but disagrees with the QS → worst case, and the reason
  this item is first. Do not wave it through.

**Also check:** the key parameters actually populate. `lookup(CONCRETE,
CST_CONCRETE_GRADE_TXT, ...)` falls back to the `DEFAULT` row when the key is empty, which
is *correct behaviour* but silently gives you a generic answer. Confirm graded elements
carry their grade.

---

## 2. Mortar — the arithmetic changed

**Do:** one **200 mm blockwork wall**. Record its net area. Run the evaluator. Read
`CST_S_MAS_MORTAR_VOLUME_CU_M`.

**Pass:** `area × 0.025 m³/m²`. For 50 m² that is **1.25 m³**. Then check the two
dependants: `CST_S_MAS_CEMENT_BAGS_NR` and `CST_S_MAS_SAND_VOLUME_CU_M` should fall in
proportion.

**Fail:**
- `2.40×` that figure (3.00 m³ for 50 m²) → the old `× thickness × 12` is still running;
  the data edit did not take. Check `FORMULAS_WITH_DEPENDENCIES.csv` line 119 in the
  *deployed* `data/` folder, not the source tree.
- Any other multiple → a different bond's ratio is being picked up. Check
  `BLE_BRICK_BOND_TYPE_TXT` on the wall.

**QS sign-off required.** This is a live change to a priced quantity.

**Known open finding, not fixed here:** this formula queries the `BRICK_BOND` table
*unconditionally*, including for blockwork. A block wall with no
`BLE_BRICK_BOND_TYPE_TXT` lands on `BRICK_BOND DEFAULT = 0.025 m³/m²` — a **brick**
figure. `BLOCK 400x200 MORTAR_VOLUME_FACTOR = 0.011 m³/m²`, less than half. The C# path
(`CompoundTakeoffBuilder.cs:99`) *does* branch on material and uses the block table, so
the two take-off paths disagree by ~2.3× on blockwork. **Expect the formula figure to be
roughly double the C# figure on block walls; that is the open finding, not a new
regression.** Record both numbers.

---

## 3. G-5 — expect a step change in skipped formulas

On a partly-populated model, **many more formulas will now be skipped than before.**
That is the fix working: each skip is a formula that previously wrote a wrong number.
It will still look like a regression to anyone who does not know why.

**Do:** run the evaluator on a partially-populated model. Compare written-value count
against the pre-install baseline. Read `StingTools.log`.

**Pass:** the count falls, and every skip has a log line naming a reason. Spot-check five
skipped elements and confirm each genuinely lacked the input.

**Telling a real skip from a missing input — the log reason distinguishes them, because
they enter the evaluator by different routes:**

| Log reason | Meaning | Where to fix |
|---|---|---|
| `unknown identifier 'X'` | Not on the element at all — never entered the context | **Binding** problem. Check `CATEGORY_BINDINGS.csv`, and which setup command was run (gap G-8: the two binders disagree on Type vs Instance) |
| `non-numeric value for 'X'` | Present but **empty**, or holds text | **Data** problem. Populate the input. Expected majority case — 190 of 191 numeric-formula inputs are declared `TEXT` |
| `unresolved function 'lookup()'` | Should now be **rare** | If you still see these after this build, `lookup()` is not reaching the table — investigate before trusting item 1 |
| `division by zero` | An input resolved to zero and was used as a divisor | Usually a knock-on of the two above |
| `undefined power (…)` | `0^-1`, `(-1)^0.5` | Genuine arithmetic error in the formula (gap G-10) |

**Fail:** skips with **no** log line (the throttle is 200 per batch — if you suspect
truncation, re-run on a smaller selection); or a formula skipped on an element that
demonstrably has every input populated.

**Warn budget:** it now resets per batch. Run two batches back to back and confirm the
second still logs.

---

## 4. IFC Qto — confirm it refuses, then confirm it writes

**Do, in this order:**

**(a) Unbound.** On a model **without** the `Qto_*BaseQuantities` shared parameters bound,
run `BOQ → Export QTO IFC`.

**Pass:** the command **refuses** with `Result.Failed` and a message naming the cause. No
IFC is written. If `Pset_StingCost` *is* bound, the message should be the "cost written,
zero quantities" variant, not the "nothing bound at all" one — and it should say the
`Qto_*` names are not part of the standard parameter load.

**Fail:** an IFC is produced. That is the original H-1 defect surviving; check
`QuantitiesWritten` is being counted only from the `StampQuantity` calls.

**(b) Bound.** Add the `Qto_*` shared parameters, bind them to the relevant categories,
re-run.

**Pass:** export succeeds; the panel reports **Elements visited**, **Quantities written**
and **Parameters written** as three distinct numbers, with quantities > 0. Open the IFC in
a text editor or Cost-X and confirm `Qto_WallBaseQuantities.NetArea` carries values.

**Fail:** quantities written is 0 but the export proceeds; or the IFC has the Pset but no
Qto values.

**(c)** Repeat (a) for `Cost_StampIfcQuantities` — it is gated identically and is a
separate entry point.

---

## 5. Room finishes — parameters must bind first

**The four new shared parameters do not exist until `LoadSharedParams` is run.** Nothing
will work before that, and the failure looks like the feature being broken.

**Do:**
1. SETUP → **Load shared parameters**. Confirm `BLE_ROOM_FINISH_{FLOOR,WALL,CEILING,BASE}_COD_TXT`,
   `STING_FINISH_CODE_TXT` and `STING_FINISH_SRC_ROOM_ID_TXT` appear and bind (Rooms
   instance, Floors instance).
2. Populate finish codes on a handful of rooms.
3. Run **WriteToRooms**.
4. Open the ISB room-finish schedule.

**Pass:** both parameter families fill — the `_COD_TXT` code fields **and** the existing
`_TXT` description fields. The ISB schedule is populated where it was previously empty.
The reported count matches the number of rooms actually written.

**Fail:**
- Only one family fills → the K-1/K-12 fix did not take.
- Schedule still empty → the schedule's fields point at the other family; check
  `MR_SCHEDULES.csv`.
- Count reported is higher than rows changed → the honest-counting part regressed.

---

## 6. K-3 floor creator — most likely to need adjustment

Room boundary geometry is where this will break. Test the awkward cases deliberately.

**Do:** run **Create floors from rooms** against, separately:
1. A simple rectangular room — the sanity case.
2. An **L-shaped** room.
3. A room with an **inner loop** — a core, a column, a lift shaft.
4. A room bounded partly by a **Floor** with `WALL_ATTR_ROOM_BOUNDING` set. This is the
   documented-but-odd Revit parameter id and the most likely single point of failure.

**Pass:** floor outline follows the room boundary in all four; inner loops become **holes,
not separate floors**; `STING_FINISH_CODE_TXT` and `STING_FINISH_SRC_ROOM_ID_TXT` are
stamped on each created floor; re-running does not duplicate.

**Fail:**
- L-shape produces a bounding rectangle → only the outer loop is being read.
- Inner loop produces a second floor, or is ignored → loop handling is wrong.
- The room-bounding Floor case throws or is skipped → the parameter id is being read
  wrongly. **Expected to be the one that needs work.**
- Re-running creates duplicates → idempotence check missing.

---

## 7. Scope Box Manager — never opened against a real model

This dialog has never run. Basic interaction is unverified.

**Do:** DOCS → **Scope Box Manager**.

**Pass:**
- Opens without blocking Revit; you can still select and pan while it is open (**modeless**).
- The scope box list matches the model.
- **Click-to-zoom** moves the active view to the selected box.
- Drawing type is **picked from a list, never typed**.
- **Two-pass rename on an A→B / B→A swap**: rename box `A` to `B` and `B` to `A` in one
  operation. Both end up correct.

**Fail:**
- Revit locks while the dialog is open → it is behaving modally.
- The A→B/B→A swap leaves a name collision, a `B(1)`, or one box unrenamed → the two-pass
  rename is not two-pass.
- Zoom does nothing, or zooms the wrong view.

---

## 8. Sheet numbers — `{lvl}` and no unexpected changes

**Do:**
1. Produce a sheet through a drawing type whose pattern includes `{lvl}`.
2. Compare the **full sheet register** against the pre-install baseline export.

**Pass:** `{lvl}` renders a real level code (or `ZZ` for a non-level-specific drawing) —
**not an empty segment**. `A-COT01-ZZ-DR-A-1001`, not `A-COT01--DR-A-1001`. `{type}`
renders the type's own value rather than always `DR`. **No already-issued sheet number
changed.**

**Fail:** any issued sheet number differs from baseline. **Stop** — a changed number on an
issued drawing is a document-control incident, not a bug. Note that the K-7 fix changes
what `{lvl}` *renders*, so a sheet whose number was previously produced with an empty
segment **will** now differ; that is the fix, but it must be a conscious re-issue
decision, not a surprise.

**Related trap:** if you populate `"level"` on a corporate drawing type in
`STING_DRAWING_TYPES.json`, its checksum changes. Re-run
`dotnet run --project tools/StampDrawingTypeChecksums -- --check` and re-stamp, or the
registry will demote the type to `project` origin.

---

## 9. Regression sweep

Quick passes over things this batch touched indirectly.

| Check | Pass |
|---|---|
| Material rates (E-1b) | A material-priced element bills at a sane UGX rate — roughly `USD × 3700`, not the raw USD figure. Compare one element against the baseline BOQ. |
| Empty-BOQ gates (H-3) | On a model with no modelled BOQ rows, the rate-gap and carbon-gap reports show **`n/a`**, never "100 % priced". |
| Prep for export (A-1) | `BOQ → Prep for export` shows the new gate **"Measured lines with no resolvable quantity = 0"**. Force a failure by unbinding a quantity source and confirm it goes red. |
| Auto-tagger | Still tags on element creation — `PostTagCleanup` was modified to reset the warn budget. |
| Material data | Materials still create from the 73-column CSVs; spot-check one with a populated `MAT_COST_UNIT_OF_MEASURE`. |

---

## Sign-off

| Item | Result | Who | Date |
|---|---|---|---|
| 1 `lookup()` vs hand take-off | | QS + BIM | |
| 2 Mortar | | QS | |
| 3 G-5 skip step change | | BIM | |
| 4 IFC Qto refuse + write | | BIM | |
| 5 Room finishes | | BIM | |
| 6 K-3 floor creator | | BIM | |
| 7 Scope Box Manager | | BIM | |
| 8 Sheet numbers | | Doc control | |
| 9 Regression sweep | | BIM | |

Items 1, 2 and 4 are **blocking**. Items 6 and 7 are expected to surface adjustments —
finding one is not a reason to hold the whole batch, provided it is logged and the
affected command is not relied on until fixed.
