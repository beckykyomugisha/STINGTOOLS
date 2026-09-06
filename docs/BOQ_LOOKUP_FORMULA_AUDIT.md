# The `lookup()` formulas — measurement, not a fix list

**Measured 2026-08-10 on `claude/kibale-integration`.** Part 2 of the BOQ take-off pass.
No formula in this table was changed. The count is the deliverable.

---

## Why this exists

G-15 was found **by accident**, while fixing unrelated arithmetic. The 28 formulas that call
`lookup()` wrote `0` until `a9eec757f` implemented `lookup()`; implementing it turned all 28 on at
once. **None had been checked against a hand take-off.**

G-15 turned out to be a 2.27× over-measure that nothing flagged on either side. The question this
answers is: how many more are shaped like it?

## Headline

| | |
|---|---|
| `lookup()` formulas **before** this pass | **28** |
| after deleting the G-15 trio | **25** |
| individual `lookup()` **calls** in those 25 | **26** |
| calls where a **C# path computes the same physical quantity** | **15 of 26** |
| calls whose table has a **`DEFAULT` row** (empty key resolves silently) | **26 of 26** |

**Every single lookup can silently take a DEFAULT.** That is the G-15 mechanism, and it is universal
— not a property of the masonry formula, a property of the design.

**15 of 26 have two owners.** Each is a potential G-15: two implementations of one quantity, in two
languages, against the same table, with nothing comparing them.

---

## The table

`C# owner` = a path in `CompoundTakeoffBuilder` / `CompoundTakeoff` computing the same physical
quantity. `DEFAULT row` = the lookup table ships a `DEFAULT` key, so an absent or unmatched
parameter resolves to a number instead of reporting that it could not be measured.

| formula | table | key param | column | C# owner? | DEFAULT row? |
|---|---|---|---|---|---|
| `CST_CALC_BLOCKS_NR` | BLOCK | `BLE_BLOCK_SIZE_TXT` | BLOCKS_PER_M2 | **masonry units + mortar** | **yes** |
| `CST_S_MAS_BLOCKS_NR` | BLOCK | `BLE_BLOCK_SIZE_TXT` | BLOCKS_PER_M2 | **masonry units + mortar** | **yes** |
| `CST_S_MAS_BRICK_QUANTITY_NR` | BRICK_BOND | `BLE_BRICK_BOND_TYPE_TXT` | BRICKS_PER_M2 | **masonry units + mortar** | **yes** |
| `CST_S_MAS_BRICK_QUANTITY_NR` | BRICK_BOND | `BLE_BRICK_BOND_TYPE_TXT` | WASTE_PCT | **masonry units + mortar** | **yes** |
| `CST_CALC_STEEL_KG` | CONCRETE | `BLE_STRUCT_CONCRETE_GRADE_TXT` | STEEL_KG_PER_M3 | **concrete + rebar + formwork** | **yes** |
| `CST_S_CON_AGGREGATE_VOLUME_CU_M` | CONCRETE | `BLE_STRUCT_CONCRETE_GRADE_TXT` | AGGREGATE_RATIO | **concrete + rebar + formwork** | **yes** |
| `CST_S_CON_CEMENT_BAGS_NR` | CONCRETE | `BLE_STRUCT_CONCRETE_GRADE_TXT` | CEMENT_BAGS_PER_M3 | **concrete + rebar + formwork** | **yes** |
| `CST_S_CON_SAND_VOLUME_CU_M` | CONCRETE | `BLE_STRUCT_CONCRETE_GRADE_TXT` | SAND_RATIO | **concrete + rebar + formwork** | **yes** |
| `CST_S_CON_WTR_VOLUME_L` | CONCRETE | `BLE_STRUCT_CONCRETE_GRADE_TXT` | WATER_PER_BAG | **concrete + rebar + formwork** | **yes** |
| `PER_SUST_CARBON_FOOTPRINT_KG` | CONCRETE | `BLE_STRUCT_CONCRETE_GRADE_TXT` | CARBON_KG_PER_M3 | **concrete + rebar + formwork** | **yes** |
| `CST_S_FRM_PROPS_QUANTITY_NR` | FORMWORK | `CST_FORMWORK_TYPE_TXT` | PROPS_PER_M2 | **formwork area** | **yes** |
| `CST_S_FRM_RELEASE_AGENT_L` | FORMWORK | `CST_FORMWORK_TYPE_TXT` | RELEASE_AGENT_M2_PER_L | **formwork area** | **yes** |
| `CST_S_FRM_TIMBER_VOLUME_CU_M` | FORMWORK | `CST_FORMWORK_TYPE_TXT` | TIMBER_THICKNESS_M | **formwork area** | **yes** |
| `CST_CALC_PLASTER_M3` | PLASTER | `BLE_PLASTER_TYPE_TXT` | THICKNESS_M | **plaster vol + cement/sand** | **yes** |
| `CST_CALC_PLASTER_M3` | PLASTER | `BLE_PLASTER_TYPE_TXT` | WASTE_PCT | **plaster vol + cement/sand** | **yes** |
| `BLE_FINISH_GROUT_WEIGHT_KG` | GROUT | `BLE_TILE_JOINT_WIDTH_MM` | GROUT_KG_PER_M2 | — | **yes** |
| `BLE_FINISH_PAINT_VOLUME_L` | PAINT | `BLE_PAINT_TYPE_TXT` | COVERAGE_M2_PER_L | — | **yes** |
| `CST_CALC_PRIMER_LITERS` | PAINT | `PRIMER` *(literal)* | COVERAGE_M2_PER_L | — | **yes** |
| `CST_S_FRM_PLYWOOD_SHEETS_NR` | PLYWOOD | `CST_PLYWOOD_SIZE_TXT` | AREA_M2 | — | **yes** |
| `CST_CALC_PURLINS_M` | PURLIN | `BLE_ROOF_LOAD_CLASS_TXT` | SPACING_M | — | **yes** |
| `CST_CALC_PUTTY_KG` | PUTTY | `BLE_SURFACE_CONDITION_TXT` | KG_PER_M2 | — | **yes** |
| `CST_S_REI_LAP_LENGTH_MM` | REBAR_LAP | `BLE_STRUCT_CONCRETE_GRADE_TXT` | TENSION_LAP_FACTOR | — | **yes** |
| `CST_CALC_FASTENERS_NR` | ROOF_SHEET | `BLE_ROOF_SHEET_PROFILE_TXT` | FASTENERS_PER_M2 | — | **yes** |
| `CST_CALC_SHEETS_NR` | ROOF_SHEET | `BLE_ROOF_SHEET_PROFILE_TXT` | COVERAGE_M2 | — | **yes** |
| `BLE_FINISH_ADHESIVE_WEIGHT_KG` | TILE | `BLE_TILE_SIZE_TXT` | ADHESIVE_KG_PER_M2 | — | **yes** |
| `BLE_FINISH_TILE_QUANTITY_NR` | TILE | `BLE_TILE_SIZE_TXT` | WASTE_PCT | — | **yes** |

---

## What the table says

**1. The 15 dual-owner rows are the G-15 class.** Masonry was one of them and was 2.27× out. The
other four groups — CONCRETE (6), FORMWORK (3), PLASTER (2), BLOCK/BRICK_BOND (4) — have **not** been
compared against their C# counterparts. Each is a candidate for the same defect and none is visible
on the page, because both numbers are individually plausible.

**2. The `DEFAULT` row is the mechanism, not the missing data.** All 26 tables ship one. An element
with no `BLE_STRUCT_CONCRETE_GRADE_TXT` does not fail — it silently receives the DEFAULT grade's
cement, sand, aggregate, water, steel and carbon figures. The parameter being absent is normal; the
number arriving anyway is the defect.

This directly contradicts the principle established by A-1 and H-1: **a quantity that cannot be
measured must report that it could not, never a number.** The C# side already does this —
`CompoundTakeoffBuilder` carries a `Resolution` object that flags every lookup falling to DEFAULT
(RC-1). The formula side has no equivalent.

**3. `CST_CALC_PRIMER_LITERS` keys on the literal `PRIMER`**, not a parameter — so it always resolves
one row. Whether that is intended or a mis-typed key needs an owner decision; it is listed for
completeness, not diagnosed.

**4. The highest-value rows for KNP26** — a lodge whose floor finishes are the product being sold —
are TILE (2), GROUT (1), PAINT (2) and PLASTER (2). Seven of the 26, all silently defaultable, and
five of the seven have no C# counterpart to disagree with, which means **no second opinion exists**
for them at all.

---

## What was NOT done here

Per the brief: **no formula in this table was changed.** The count is the deliverable, same as the
603 readership violations. Deciding what to do about 15 dual owners is an owner decision, and the
options differ per group — masonry was resolved by deleting the formula and letting C# own it, but
that only works where a C# path already exists.
