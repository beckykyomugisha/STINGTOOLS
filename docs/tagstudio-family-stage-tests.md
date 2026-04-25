# Tag Studio — Family-Stage Manual Test Checklist

Manual tests for the Tag Studio family-stage refactor (Tasks 1–8 on
branch `claude/review-sting-components-vyjgU`). Every test below must be
executed in Revit 2025 with a real project — the Linux sandbox cannot
validate Revit API behaviour, so these checks are the compile-test for
the refactor. Tick off each one; record deviations in the row header.

Prerequisites
-------------
- Revit 2025, 2026, or 2027 with the current `claude/review-sting-components-vyjgU`
  build loaded (`StingTools.addin` + `StingTools.dll` + `data/` folder).
- A blank project with at least one level + ground plan view, or the repo's
  V6 smoke-test project.
- The Tag Studio dockable panel visible (STING panel → TAGS tab).

---

## Test 1 — Migrate creates all standard variants + params

**Goal:** MigrateTagFamilies populates every tag family with the full
parameter set and the standard variants from `tag_style_catalogue.json`.

1. Start from a blank project.
2. Run **TAGS → Automation → Create Tag Families** for 3 categories
   (Ducts / Walls / Doors is a good spread).
3. Run **TAGS → Automation → Migrate Tag Families**.
4. Open one family in Family Editor. Verify:
   - `TAG_PARA_STATE_1_BOOL` through `TAG_PARA_STATE_10_BOOL` are present.
   - All 128 `TAG_{size}{style}_{colour}_BOOL` params are present
     (4 sizes × 4 styles × 8 colours).
   - `TAG_BOX_COLOR_R/G/B_INT`, `TAG_BOX_VISIBLE_BOOL`, `TAG_BOX_STYLE_TXT`
     are present.
   - `TAG_LEADER_COLOR_R/G/B_INT` are present.
   - `TAG_SCALE_TIER_AUTO_BOOL` and `TAG_DEPTH_TIER_INT` are present.
   - At minimum, all variants enumerated by
     `TagStyleCatalogue.EnumerateStandardVariants()` exist as types.  If
     the on-demand strategy is in force, at least the 8 disciplinary
     defaults + the compact-black baselines per size must be present.

**PASS** when the Excel report has `ParamsAdded > 0` and `TypesCreated > 0`
for every family and none are `FAILED`.

---

## Test 2 — ParaDepth slider drives the tag variant

**Goal:** The `ParaDepth` Tag Studio slider routes through
`ResolveTagTypeForPlacement` and picks the variant whose name ends `_T{n}`.

1. Load a test project with ducts.
2. Set the **Tokens & Depth → Paragraph Depth** slider to **5**.
3. Click **TagStudio_SmartPlace** (Smart place).
4. Select any placed duct tag. Verify:
   - The tag's type name ends `_T5` (not `_T1`, `_T3`, etc.).
   - Opening the type in the Project Browser shows `PARA_STATE_1..5 = Yes`
     and `PARA_STATE_6..10 = No`.

**PASS** when every new tag picks a `_T5` variant.

---

## Test 3 — Three arrowhead selections = three different type variants

**Goal:** Changing `cmbArrowStyle` routes to distinct type variants rather
than mutating one shared type's arrowhead.

1. Set the Leader & Elbow → **Arrow style** combo to **Filled**.
   Place 3 duct tags via Smart place.
2. Change combo to **Open**. Place 3 more.
3. Change combo to **Dot**. Place 3 more.
4. Inspect each placed tag's type name:
   - Batch 1 types end `_Filled30_T{depth}` (or similar canonical)
   - Batch 2 types end `_Open30_T{depth}`
   - Batch 3 types end `_DotFilled_T{depth}`

**PASS** when the three batches land on three DIFFERENT type variants.

**FAIL** if all 9 tags share a single type whose `LEADER_ARROWHEAD` has
been mutated — that is the regressed behaviour.

---

## Test 4 — Selection arrowhead override is local to the selection

**Goal:** `OverrideArrowheadOnSelection` writes the INSTANCE
`LEADER_ARROWHEAD` parameter only on the selected tags.

1. Place 12 duct tags in a view, all with arrowhead **Filled**.
2. Select exactly 5 of them.
3. Set Leader & Elbow → **Arrow style** = **Dot Filled**.
4. Click **Set arrows** (or use the selection-override path).
5. Verify:
   - The 5 selected tags now draw with a Dot Filled arrowhead.
   - The 7 unselected tags still draw with Filled.
   - Looking at any one type's `LEADER_ARROWHEAD` in the family editor,
     the **type** value is unchanged — the override lives on the instance.

**PASS** when only the 5 selected tags change appearance.

---

## Test 5 — Style Audit flags a degraded family

**Goal:** StyleAuditCommand reports Red status with correct missing counts.

1. In a migrated project, open one tag family in the Family Editor.
2. Delete three type variants (e.g. `2.5_BOLD_RED_Filled30_T3`,
   `2.5_BOLD_BLUE_Filled30_T3`, `2_NOM_BLACK_None_T1`).
3. Also delete one parameter (e.g. `TAG_PARA_STATE_5_BOOL`).
4. Load back into the project.
5. Run **TAGS → Automation → Style Audit**.
6. Open the exported Excel report. Verify:
   - The degraded family's row has `MissingParams = 1`, `MissingVariants ≥ 3`,
     `Status = RED`.
   - TaskDialog offers the "Run Migrate Tag Families now" command link.

**PASS** when the numbers match the degradation and status is `RED`.

---

## Test 6 — Category filter is honoured before placement

**Goal:** The `CategorySkipList` gate runs before any tag is created —
a category ticked off should produce zero tags.

1. In a project with ducts and pipes, open **Tag Config** and add
   `Ducts` to the skip list (or untick Ducts in the Tag Studio category
   filter panel).
2. Click **TagStudio_SmartPlace** (Smart place).
3. Verify:
   - Zero duct tags were created.
   - Pipe tags were placed as normal.
   - The StingTools.log shows `Skipped N by category filter` where N is
     the count of ducts in the view.

**PASS** when ducts are fully excluded and pipes are tagged.

---

## Common diagnostics

- **All tests failing with "no type variant found" warnings:**
  MigrateTagFamilies has not been run in this project. Run it.
- **Arrowhead tests fail with "no OST_ArrowHeads type matches":**
  The project template is missing named arrowhead types. Load a
  Revit-shipped title block or manually create arrowheads matching the
  catalogue names in Data/tag_style_catalogue.json.
- **Test 2 tags land on `_T3` regardless of slider:** The
  `ParaDepth` ExtraParam is not being read — confirm the slider commit
  hook `SetTokenDepthParams` fires on slider change (Tag Studio → Tokens
  & Depth sub-tab) and that `ParaDepth` shows up in the StingTools.log.

