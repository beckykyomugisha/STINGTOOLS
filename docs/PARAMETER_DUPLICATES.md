# STING Tools — Parameter Duplicate Analysis

> **Status (2026-04-16):** Consolidation EXECUTED in commit history. Registry shrunk from 2,378 → 2,361 `PARAM` lines (−17). All callers migrated to canonical names; no residual references outside this historical doc. Sections A and B below describe the consolidation that was performed.

Scope: `StingTools/Data/MR_PARAMETERS.txt` (was 2,378 `PARAM` lines).
Method: for every candidate pair, count tag-config CSV references and C# compile-time references. A duplicate is flagged only when BOTH variants are genuinely used or one is provably dead.

**Total deprecation candidates: 12 TRUE + 6 UNUSED = 18 params (0.76 % redundancy).**

---

## A. TRUE DUPLICATES — both names used, consolidate

| # | Duplicate pair | Recommendation |
|---|---|---|
| 1 | `ASS_STATUS_TXT` vs `ASS_STATUS_COD_TXT` | **Keep `ASS_STATUS_TXT`** (94 CSV refs, 27 code refs). `ASS_STATUS_COD_TXT` has 0 CSV refs — deprecate. |
| 2 | `ASS_MANUFACTURER_TXT` vs `BLE_MAT_MANUFACTURER_TXT` vs `MAT_MANUFACTURER` | **Keep `ASS_MANUFACTURER_TXT`** (BIMManager, GapAnalysis, formulas). `BLE_MAT_MANUFACTURER_TXT` used only in `MaterialCommands.cs:188` — redundant with `MAT_MANUFACTURER` (`MaterialCommands.cs:121`). Map the CSV column to `ASS_MANUFACTURER_TXT`. |
| 3 | `PER_THERM_U_VALUE_W_M2K` vs `PER_THERM_U_VALUE_W_M2K_NR` | Tag CSVs use the un-suffixed form (6 refs); `_NR` variant only referenced by its own `WARN_*_NR` wrappers. **Keep `PER_THERM_U_VALUE_W_M2K`** — rename warnings. |
| 4 | `CST_TAG_7_PARA_CONCRETE_TXT` vs `CST_TAG_7_PARA_CONC_TXT` | **Keep `CST_TAG_7_PARA_CONC_TXT`** (`ParamRegistry.cs:1737`). `CST_TAG_7_PARA_CONCRETE_TXT` has zero non-definition refs — deprecate. |
| 5 | `BLE_WINDOW_U_VALUE` vs `BLE_WINDOW_U_VALUE_W_M_2K_NR` | **Keep `BLE_WINDOW_U_VALUE_W_M_2K_NR`** (formulas, LABEL_DEFINITIONS, schedules). Unsuffixed variant only in `TagConfig.cs:5708` legacy reader — deprecate. |
| 6 | `BLE_FIRE_RATING_TXT` vs `PER_FIRE_RATING_HR` | `PER_FIRE_RATING_HR` has 17+ tag-CSV refs; `BLE_FIRE_RATING_TXT` has 2 code refs. Route element-level fire rating through `PER_FIRE_RATING_HR` and deprecate `BLE_FIRE_RATING_TXT`. (The other `_FIRE_RATING_*` variants serve distinct scopes — see Section C.) |
| 7 | `ASS_WARRANTY_START_TXT` vs `COM_WARRANTY_START_TXT` | **Keep `COM_WARRANTY_START_TXT`** (`BIMManagerCommands:2719, 3165`, `StingExportDialog`). `ASS_WARRANTY_START_TXT` only in ParamRegistry alias — deprecate. |
| 8 | `ASS_WARRANTY_EXPIRATION_DATE_TXT` vs `MNT_WARRANTY_EXPIRY_TXT` | **Keep `MNT_WARRANTY_EXPIRY_TXT`** (100+ LABEL_DEFINITIONS refs, `TagConfig.cs:5023`). Migrate `ASS_WARRANTY_EXPIRATION_DATE_TXT` callers in DocAutomation + IoT. |
| 9 | `ASS_WARRANTY_PERIOD_TXT` + `ASS_WARRANTY_DUR_TXT` vs `ASS_WARRANTY_DURATION_PARTS_YRS` + `ASS_WARRANTY_DURATION_LABOR_YRS` | Four overlapping duration fields. Keep the COBie-aligned `DURATION_PARTS_YRS` / `DURATION_LABOR_YRS` + `ASS_WARRANTY_DUR_UNIT_TXT`. Deprecate `ASS_WARRANTY_PERIOD_TXT` and `ASS_WARRANTY_DUR_TXT`. |
| 10 | `ASS_UNIT_COST_TXT` vs `ASS_CST_UNIT_PRICE_UGX_NR` | **Keep `ASS_CST_UNIT_PRICE_UGX_NR`** (BOQ_TEMPLATE, formulas, BIMManager, pyRevit manifest). `ASS_UNIT_COST_TXT` only in `DataPipelineCommands:2860` — migrate and deprecate. |
| 11 | `BLE_STRUCT_FIRE_PROTECTION_TYPE_TXT` vs `STR_FIRE_PROTECTION_TYPE_TXT` | **Keep `STR_FIRE_PROTECTION_TYPE_TXT`** (5 tag-CSV refs). `BLE_STRUCT_FIRE_PROTECTION_TYPE_TXT` has no CSV refs — deprecate. |
| 12 | `CST_CALC_CONCRETE_M3` vs `CST_S_CON_VOLUME_CU_M` | **Keep `CST_S_CON_VOLUME_CU_M`** (pyRevit manifest + structural pipeline). `CST_CALC_CONCRETE_M3` used only in cost-calc preset — consolidate. |

## B. UNUSED DUPLICATES — dead parameters

| Parameter | Notes |
|---|---|
| `ASS_REPLACE_COST_TXT` | 0 external refs; superseded by `PER_REPLACEMENT_COST_UGX` |
| `CST_TAG_7_PARA_CONCRETE_TXT` | 0 refs; active alias is `CST_TAG_7_PARA_CONC_TXT` |
| `ASS_STATUS_COD_TXT` | 0 tag-CSV refs (126 LABEL_DEFINITIONS refs appear residual) |
| `BLE_RAMP_HANDRAIL_TXT` | Only the `_BOOL` variant is used (`ARCH:251`) |
| `BLE_DOOR_GLAZING_PCT` | Only the `_BOOL` variant is used |
| `ELC_PNL_RATED_BOOL` | Only the `_KW` variant is used (formulas, MEP CSV) |
| `ELC_PNL_SPARE_WAYS_PCT` | Only the `_NR` variant is actively referenced; `_PCT` only in `TagConfig.cs:5755` legacy reader |
| `BLE_STRUCT_FIRE_PROTECTION_TYPE_TXT` | Superseded by `STR_FIRE_PROTECTION_TYPE_TXT` |

## C. APPARENT DUPLICATES — distinct, keep

Document these to prevent accidental consolidation in future merges.

| Family | Variants | Why distinct |
|---|---|---|
| Tile quantity | `BLE_CEILING_FIN_TILE_QTY_NR` / `BLE_FINISH_TILE_QUANTITY_NR` / `BLE_FLR_TILE_QTY_NR` | Element scope differs: ceiling / wall-finish / floor |
| Currency pairs | `MAT_COST_UGX` + `MAT_COST_USD`, `MAT_COST_ASSEMBLY_UGX` + `MAT_COST_ASSEMBLY_USD` | Dual-currency BOQ is intentional |
| Fire-rating hierarchy | `BLE_FIRE_RATING_TXT` (element) / `PER_FIRE_RATING_HR` (performance) / `PROP_FIRE_RATING` (material) / `RGL_FIRE_RATING_TXT` (regulatory) / `SLV_FIRE_RATING_TXT` (sleeve) | 5 distinct ISO 19650 scopes. Only BLE↔PER overlap is a true-dup candidate (row 6 above) |
| Warranty family | PARTS / LABOR × DURATION / TXT | COBie V2.4 pattern — keep as-is |
| CST groups | `CST_CALC_*` / `CST_S_*_*` / `CST_TOTAL_*_COST` | Calculator inputs / structural sub-group / aggregate totals — orthogonal |
| Description axes | `ASS_DESCRIPTION_TXT` / `ASS_CLASS_DESC_TXT` / `ASS_UNIFORMAT_DESC_TXT` / `ASS_ASSEMBLY_DESC_TXT` | Distinct classification axes |

## Deprecation process

1. Pick one victim from Section A or B.
2. Grep the repo (code + CSVs + JSON) for all references; rewrite to the canonical name.
3. Remove the deprecated `PARAM` row from `MR_PARAMETERS.txt` AND the matching row in `MR_PARAMETERS.csv`.
4. Run the tag-family audit (`grep -P '^PARAM\t[^\t]+\tDEPRECATED_NAME\t' StingTools/Data/MR_PARAMETERS.txt` should return empty).
5. Bump the schema version comment in affected CSVs.
6. Document the change in CLAUDE.md.

---

## D. PARAMETER_REGISTRY.json duplicate audit (Phase 168)

A walk over `PARAMETER_REGISTRY.json` for every `param_name` field showed three categories of repeated names. Counts were taken on 2026-05-02.

### D.1 Cross-reference (not a real duplicate) — 27 cases

Pattern: a param defined once in `source_tokens` / `support_params` / `container_groups[].params` / `extended_params.<facet>[]` and then referenced by name (no `guid` field) in `ifc_property_mapping.mappings[]` or another facet to attach IFC pset/property metadata. These are intentional — the second occurrence carries no GUID and is just attaching extra metadata.

**Action:** none. Leave as-is.

### D.2 Same-GUID duplicates inside `extended_params` — handful of cases

Pattern: `support_params[N]` defines a param fully, and `extended_params.slv_sleeve[]` (or similar facet array) re-lists the same param with the same GUID. The second occurrence groups the param under a categorisation key (e.g. for the facet drop-down in the editor).

**Action:** none. Same GUID means there is no functional collision; the second occurrence serves as a categorisation index. Removing it would lose the categorisation without saving anything functionally meaningful.

### D.3 `WARN_*` different-GUID collisions — 99 cases — `TODO: GUID_COLLISION_REVIEW`

The registry contains TWO sibling `warning_thresholds` arrays:

- `extended_params.warning_thresholds[]` — added when warnings were grouped under `extended_params`
- top-level `warning_thresholds[]` — the original location

For 99 of the entries, **both arrays have a row with the same `param_name` but different GUIDs.** This is a real collision: each row would create a separate Revit shared parameter with the same display name, which is illegal at the project level. Examples:

| param_name | extended_params guid | top-level guid |
|---|---|---|
| `WARN_BLE_RAMP_SLOPE_PCT_RAMPS` | `e8750740-…` | `d1d95fa4-…` |
| `WARN_ELC_VLT_DROP_PCT_ELECTRICAL_EQUI` | `7dd4922a-…` | `b442923a-…` |
| `WARN_HVC_DCT_SOUNDLVL_DB` | `be2ddf28-…` | `c397432d-…` |
| `WARN_PER_THERM_U_VALUE_W_M2K_NR_FLOORS` | `05aac695-…` | `5de3531a-…` |
| `WARN_STR_TRUSS_SPAN` | `230b7d16-…` | `f81ac5b3-…` |

(The full list is 99 entries — the regex `^WARN_` against the duplicate-name set yields all of them.)

**Why this was not auto-resolved:** the audit instruction was *"Different GUIDs = report this but do NOT auto-fix"*. Choosing one GUID over the other risks invalidating any Revit project that has data stored under the discarded GUID. The "live" GUID can only be confirmed by inspecting an open project that uses the warning system.

**Recommended next step (deferred):**

1. Open a representative project that has had warnings populated.
2. Read each `WARN_*` parameter via the Revit API; the GUID returned is the live one.
3. Drop the orphan row from whichever JSON array does not match.
4. If both arrays match no live GUID, the parameter has never been bound — drop the row from `extended_params.warning_thresholds[]` and keep top-level (top-level is the historic primary).

Until that audit is done, both rows are kept so existing projects continue to resolve the warning regardless of which GUID their data is stored under. Both rows also stay tagged with the same `description` text so the editor UI is consistent.
