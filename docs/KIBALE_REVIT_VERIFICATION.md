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

## 2b. G-2 — the CSV parser now keeps quote characters (**blocking**)

`ParseCsvLine` was deleting every quote character from field *content*. Fixing it changes
**13,432 rows across 13 shipped data files** — and only ~88 of those are the formula
engine. **~13,300 are the `STING_TAG_CONFIG_v5_0_*` files, which carry the Revit label
formulas written into tag families.** This is the widest-reaching change in the batch by
row count.

**Do:**
1. **Tag families.** Run the tag-family creation / sync path against a test family.
   Inspect a label formula in the Revit family editor.
2. **Concatenation.** Tag an element whose tag uses `ELC_FIX_TAG_1_TXT`
   (`ASS_ID_TXT + "-" + ASS_TAG_1_TXT`).
3. **String comparison.** Put a sprinkler head with
   `FLS_PROT_SPRINKLER_HED_TYPE_TXT = "Standard Response"` in the model and evaluate
   `FLS_SFTY_COVERAGE_AREA_SQ_M`.

**Pass:**
- Label formulas read `if(GATE_BOOL, ASS_TAG_2_TXT, "")` — **with** the empty-string
  argument, not `if(GATE_BOOL, ASS_TAG_2_TXT, )` which Revit rejects.
- The concatenated tag reads `ABC-123`, **not** `ABC123` or `ABC 123` — the separator is
  back.
- Sprinkler coverage returns **12** for Standard Response and **9** for Quick Response,
  not the 9 fallback on everything.

**Fail:**
- Any tag family formula that previously applied now errors on load → a formula that was
  only valid *because* it was being mangled. Report it; do not re-break the parser.
- Tag text gains stray `"` characters → the field was double-quoted in the source data
  and now round-trips one level too few. Check the source row.

**Why this is blocking:** it rewrites the text of every tag label formula the tag config
produces. Field *counts* were verified unchanged on all 67,006 rows, so no column
indexing moved — but the content did, on 13,432 of them.

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

## 3b. G-6 + G-3 — 32 formulas that never loaded, and 65 that never evaluated (**blocking**)

Two changes that compound. G-6 repaired 32 truncated rows so they load at all; G-3 gave
the TEXT path a real `if()` evaluator. Together: **formulas that load rose 270 → 302, and
TEXT formulas containing `if()` that actually evaluate rose 0 → 65.**

**Expect warnings and narrative text to appear on elements that have never shown any.
That is two inert features coming alive, not a regression.**

**Do:**
1. **Load count.** Run the evaluator and read `StingTools.log`. The new short-row warning
   should report **zero** dropped rows.
2. **Narrative.** Tag a wall whose `TAG_PARA_STATE_3_BOOL` is set and inspect
   `ARCH_TAG_7_PARA_WALL_TXT`.
3. **Warnings.** Give a wall a `PER_THERM_U_VALUE_W_M2K` above 0.70 and check
   `WARN_PER_THERM_U_VALUE_W_M2K_NR_WALLS`.
4. **The empty branch.** Put a wall with `BLE_WALL_FUNCTION_TXT` **blank** through the
   same formula.

**Pass:**
- No "DROPPED n row(s)" warning in the log.
- The narrative parameter carries a real sentence, not a fragment and not the literal
  `if(`.
- The warning parameter reads ` [!U > 0.70]` when over threshold and is **empty** when
  under it.
- The blank-input case yields the **false branch** (empty), and the formula is *written*,
  not skipped. This is the distinction `TryStringCondition` exists for: an empty
  parameter is a legitimate false, not a failure.

**Fail:**
- A narrative that is truncated mid-sentence → a branch is terminating early; check the
  nesting depth of that formula (36 of the 65 are nested).
- A `WARN_*` that is **blank** where the threshold is exceeded → the condition failed and
  was reported as empty rather than absent. Under G-5 semantics an uncomputable warning
  must be *absent*; a blank one reads as "no warning", which is wrong in the unsafe
  direction.
- A formula skipped where the input is merely empty → `TryStringCondition` is failing on a
  present-but-empty value instead of returning false.

**Note on the 32 repaired rows:** their `Input_Parameters` were derived from their
expressions, and their GUIDs read from `MR_PARAMETERS.txt`. Spot-check two against the
parameters they claim to consume — if a repaired row references a parameter that is not
bound on the element, it will now report `unknown identifier` rather than silently not
existing, which is the intended trade.

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

## 10. A-3 — the link under-count gate

**Do:** link a model, place it **twice**, tick it for inclusion in the BOQ, and leave the
per-link ×N multiplier **off**. Build the BOQ, open the Audit Trail sheet, then run
`BOQ → Prep for export`.

**Pass:** the Audit Trail carries a red banner and a row naming the link, `×2`, the rows it
contributed and the UGX shortfall. Prep for export shows
**"Linked models taken off at their placed count"** as `?` with `[CONFIRM]`, and — if that
is the only failing gate — asks once whether it is intended. Answering **Yes** returns
success; **No** fails.

**Fail:** no warning anywhere (the finding is not being recorded); or the gate hard-blocks
with no way to proceed (a shared reference model placed twice must remain exportable); or
it blocks while a genuine hard failure is also present but unreported.

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

---

# MIGRATIONS — not verification items

**Everything above asks "did this run correctly?". The two below change values that are
already sitting in delivered models.** They cannot be settled by checking one element,
because the old value was written by a previous run and will stay wrong until the model is
re-processed. Treat each as a data migration with a re-run step, not a test.

## M-1 · G-4 — the unit conversion is gone

The formula writer no longer converts results to Revit internal units. It was the only
call site, and it could not be correct anywhere: `MR_PARAMETERS.txt` declares **no LENGTH,
AREA or VOLUME parameters** (TEXT 2,819 / YESNO 265 / NUMBER 221 / INTEGER 93), so there is
no target for which feet are the right storage.

**What moves, and what does not** — this is the part that matters, because it is not
uniform:

| Unit tag in `FORMULAS_WITH_DEPENDENCIES.csv` | Before | After | Shift |
|---|---|---|---|
| `m2`, `SQ_M`, `M`, `MM`, `CM`, `IN`, `M3`, `CU_M`, `L`, `C` | converted to internal units | metric | **changes** — ×0.3048ⁿ reversed |
| `m²`, `m³`, `%`, `nr`, `kg`, `bags`, `UGX`, `text`, blank … | passed through unchanged | metric | **no change** |

The switch carried `M2`/`SQ_M`/`SQUARE_METERS` but **not `m²`**, so which parameters were
corrupted depended on which glyph the author typed. The worked case, two rows with
**byte-identical** expressions (`CST_S_MAS_WALL_AREA_SQ_M − CST_S_MAS_OPENING_AREA_SQ_M`):

| Parameter | Unit | Before | After |
|---|---|---|---|
| `CST_S_MAS_NET_AREA_SQ_M` | `m²` | metric (never converted) | metric — **unchanged** |
| `CST_S_MAS_NET_WALL_AREA_SQ_M` | `m2` | metric × **10.7639** | metric — **÷10.7639** |

**Acceptance:** after this build, those two parameters must hold **identical values** on
the same wall. They currently differ by 10.7639× for no reason but a character.

**Migration steps, per affected project:**
1. Record the current value of one `m2`-tagged parameter on a known element — this is the
   before.
2. Install the build.
3. **Re-run Master Setup (or any command that runs the formula pipeline) across the whole
   model.** Checking is not sufficient: values written by the previous run persist until
   overwritten. A model that is opened and inspected but not re-processed will still hold
   the old numbers.
4. Confirm the recorded parameter has fallen by 10.7639×, and that its `m²`-tagged twin has
   not moved.
5. **Re-issue anything priced off an `m2`-tagged quantity.** Masonry net wall area is in
   this set and feeds the mortar chain.

**Do not** "fix" a model by re-running only the elements that look wrong — the population
that changed is defined by the *unit tag on the formula*, not by which values look odd.

## M-2 · F-2 — untagged elements move from `BLD1` to `XX`

Elements whose LOC could not be derived were silently filed under whichever building code
sorts first (`LocCodes.FirstOrDefault(...) ?? "BLD1"`). They now carry `XX`.

**Expect existing models to show `XX` where they showed a building code.** That is the
correction: those elements were never *in* that building, and on a multi-building project
the first building was absorbing every unplaceable element — its cost and quantities wrong
while looking entirely plausible.

**Migration steps:**
1. Baseline the per-building element counts and cost totals **before** installing.
2. Install, then **re-run tagging across the whole model** — existing tags are not
   rewritten by opening the file.
3. Compare. **The first building's element count and cost total should fall.** The
   difference is what it was absorbing.
4. Read the new log line: `N element(s) had no derivable LOC and were tagged XX`.
   Investigate each — every one is an element the tagger could not place, which is
   information that was previously being discarded rather than reported.

**Strict-mode note.** `XX` is now explicitly accepted by `ISO19650Validator` for LOC, the
same escape LVL already had. It is deliberately **not** added to `LocCodes`
(`BLD1/BLD2/BLD3/EXT`) — that list is the set of real buildings, and a placeholder must not
be selectable as a location. Without the escape this change would have traded a silent
defect for a loud one on strict-mode projects: elements that used to be mis-filed would
start failing validation as `LOC 'XX' not in valid set`. Lenient mode, the default, already
accepted it.

**Expect the count to be non-zero on any real model.** If it is zero, check that LOC
derivation is actually running — a zero here previously meant "everything was assigned to
BLD1", not "everything resolved".

## M-5 · E-2 — carbon figures will change on 497 materials

`CarbonFactorResolver`'s exact-match tier reads `STING_EMB_CARBON_NR`
(`CarbonFactorResolver.cs:62`); its split tiers read `STING_EMB_CARBON_FOSSIL_NR` and
`STING_EMB_CARBON_BIOGENIC_NR` (`:141`, `:150`). **Nothing wrote any of the three.** The
material library's measured carbon (`PROP_CARBON_KG_M3`, CSV column 61) was written to a
shared parameter of the same name that no resolver consults, so every material fell through
to the keyword / Revit-material-class estimate — including materials carrying a real
measured figure.

`MaterialCommands.SharedParamMappings` now writes all three. `PROP_CARBON_KG_M3` is still
written as well, so any existing schedule built on it keeps working.

**Expect embodied-carbon figures to change** on materials that carry a real
`PROP_CARBON_KG_M3` — **497 of 815 rows** in `BLE_MATERIALS.csv`, and the same columns exist
in `MEP_MATERIALS.csv`. Materials without one keep resolving by keyword, unchanged. The
direction of change is not predictable per material: the deterministic value replaces an
estimate that could have been high or low.

**Two parameters are new** — `STING_EMB_CARBON_FOSSIL_NR` (`ae43ffaf-…`) and
`STING_EMB_CARBON_BIOGENIC_NR` (`6e28ba35-…`), UUIDv5 under the same namespace as A-2,
bound `Materials` / `Instance` exactly as `STING_EMB_CARBON_NR` already was. No collision
against the 3,403 existing GUIDs.

**Migration steps, per affected project:**
1. Baseline the embodied-carbon total **before** installing.
2. Install, run **Load Shared Parameters** (the two new ones will not bind otherwise —
   same mechanism as M-3), then **re-run material population**. Carbon values are written
   at material creation; opening the file changes nothing.
3. Re-run the carbon report and compare. **Expect movement on ~60 % of materials.**
4. Spot-check one timber material. Biogenic carbon is allowed to be negative —
   sequestration is the point — so a timber row moving negative is correct, not a defect.
5. If the total does **not** move at all, step 2 did not take: check that the materials
   actually carry `STING_EMB_CARBON_NR` with a non-zero value, because the resolver
   requires `StorageType.Double` and silently skips anything else.

## M-4 · G-10 — drainage flow rate was 1000× too small

`PLM_DRN_FLW_RATE_LPS` is declared in **L/s** but its formula produced **m³/s**: it applied
the 50 %-full factor and omitted the m³/s → L/s conversion its two siblings both carry.

```
PLM_PPE_FLW_LPS        …  * PLM_VEL_MPS     * 1000          ← correct
HVC_PIPE_FLOWRATE_LPS  …  * HVC_VEL_MPS     * 1000          ← correct
PLM_DRN_FLW_RATE_LPS   …  * PLM_DRN_VEL_MPS * 0.5           ← was missing * 1000
```

**Expect drainage flow figures to increase by exactly 1000×.** A 100 mm drain at 1 m/s read
**0.0039 L/s**; it now reads **3.93 L/s**. The old figure was not merely small, it was
physically impossible for a 100 mm pipe, which is why nothing downstream ever flagged it —
no gate checks a value for plausibility, only for presence.

**The one derived consumer is independent confirmation.** `PLM_DRN_FILL_RATIO_NR` is
`PLM_DRN_FLW_RATE_LPS / PLM_PPE_FLW_LPS`, described as "hydraulic fill ratio (actual/full
bore)". For a half-full pipe it must read **0.5**. It read **0.0005**. It now reads 0.5.

**Migration steps, per affected project:**
1. Baseline any drainage sizing sign-off or schedule that quotes `PLM_DRN_FLW_RATE_LPS` or
   `PLM_DRN_FILL_RATIO_NR` **before** installing.
2. Install, then **re-run the formula pass** — existing parameter values are not recomputed
   by opening the file.
3. Confirm `PLM_DRN_FILL_RATIO_NR` now reads ~0.5 on a half-full run, not ~0.0005.
4. **Re-check any drain sized against the old number.** A pipe sized to carry a flow
   reported 1000× low was sized against a meaningless figure — this is a re-design trigger
   on any project where drainage capacity was signed off from these parameters, not just a
   re-print. Two schedule definitions display it (`MR_SCHEDULES.csv`,
   `TPL_SCHEDULE_METADATA.csv`); both will change.

**No C# reads either parameter** — the entire blast radius is the one derived formula plus
those two schedules. Verified by grep across `--include=*.cs` and `StingTools/Data/`.

## M-3 · A-2 — five new parameters must be loaded before classification writes anything

`ClassificationReader` — the resolver BOQ, COBie, handover and IFC export all use — reads
five parameters that **did not exist anywhere in the shared parameter file**:

| Parameter | Binding | Purpose |
|---|---|---|
| `UNICLASS_PR_TXT` | Type | Uniclass 2015 Products (`Pr_`) |
| `UNICLASS_SS_TXT` | Type | Uniclass 2015 Systems (`Ss_`) |
| `UNICLASS_EF_TXT` | Type | Uniclass 2015 Elements / Functions (`EF_`) |
| `NBS_CODE_TXT` | Type | NBS specification clause |
| `ASSET_RFI_URL_TXT` | Instance | Asset RFI / product-data URL |

They were added in `316f70375` and are bound to the same 44 model categories
`ASS_DESCRIPTION_TXT` uses. `CSI_SECTION_TXT` / `CSI_TITLE_TXT` were bound in the same
commit — they existed but shipped with **zero** binding rows, so `CsiAssignCommand`'s writes
were equally swallowed.

**Existing models do not get these parameters by opening the file.** A shared parameter
only appears on a category once it has been bound into *that document*.

**Until `LoadSharedParams` has been re-run on a model, every classification write is a
no-op** — `ParameterHelpers.SetString` looks the parameter up on the element, finds nothing,
and returns `false`. Nothing throws. The old code had exactly this shape and reported
"Uniclass codes written to N elements" while writing none.

**Migration steps, per affected project:**
1. Open the model and run **Load Shared Parameters**.
2. Run **Uniclass Classify**. Read the report's tail:
   - `Types written: N` — how many element types were stamped.
   - A per-parameter breakdown (`UNICLASS_PR_TXT: n`, `UNICLASS_SS_TXT: n`, …).
   - `⚠ N type(s) do not carry the UNICLASS_* parameters at all` — **if this is
     non-zero, step 1 did not take on those categories.** This line is the whole point;
     the previous version of the command could not tell you this.
3. Spot-check one door and one wall type. A door must hold `Pr_30_59_24` in
   `UNICLASS_PR_TXT`, **not** in `UNICLASS_SS_TXT` — the writer now routes by table
   prefix, and a product code appearing in the systems parameter means an old build.
4. Confirm BOQ grouping provenance now reads `via: Uniclass.Pr` / `Uniclass.Ss` rather
   than falling through to `Native.Family` on every row.

**Note on categories.** The shipped map covers 25 of the 43 categories these parameters
bind to; the 18 without an entry are listed by name in the header of
`StingTools/Data/STING_UNICLASS_MAP.csv`. Those elements fall through to the reader's
CSI → OmniClass → Native tiers, which is the pre-existing behaviour, not a regression.
Adding them is now a **data edit** (baseline CSV, or a project override at
`<project>/_BIM_COORD/uniclass_map.csv`) followed by **Uniclass reload map** — no rebuild.

**Two map rows are knowingly inert.** `OST_StructuralColumns` and `OST_StructuralFraming`
carry correct codes but are not in the bound 44, so their writes no-op and they will be
counted in the ⚠ line above until the binding set is extended.

## Sign-off

| Item | Result | Who | Date |
|---|---|---|---|
| 1 `lookup()` vs hand take-off | | QS + BIM | |
| 2 Mortar | | QS | |
| 2b G-2 quoted literals / tag label formulas | | BIM | |
| 3b G-6 load count + G-3 warnings/narrative | | BIM | |
| 10 A-3 link under-count gate | | QS | |
| 3 G-5 skip step change | | BIM | |
| 4 IFC Qto refuse + write | | BIM | |
| 5 Room finishes | | BIM | |
| 6 K-3 floor creator | | BIM | |
| 7 Scope Box Manager | | BIM | |
| 8 Sheet numbers | | Doc control | |
| 9 Regression sweep | | BIM | |

Items 1, 2, 2b, 3b and 4 are **blocking**. Items 6 and 7 are expected to surface adjustments —
finding one is not a reason to hold the whole batch, provided it is logged and the
affected command is not relied on until fixed.
