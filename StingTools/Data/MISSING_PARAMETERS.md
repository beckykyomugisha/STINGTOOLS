# Missing Parameters Audit — Code vs Shared-Parameter Files

> **Status (2026-05-19):** RE-VERIFIED on branch
> `claude/review-parameter-alignment-riKvx`. The 25 net-new
> parameters introduced by Phase 184 (cost / payment cert /
> variation, P0.2 + P5) and Phase 182-183 (HVAC sizing
> audit-trail + refrigerant / capacity) are now aligned across
> **all eight data surfaces**:
>
> 1. `MR_PARAMETERS.txt` — pure 7-bit ASCII PARAM rows (no longer
>    UTF-16 LE — see commit `dffa3a3e5` for the Revit-parser fix).
> 2. `MR_PARAMETERS.csv` — CSV mirror with v6.3 header bump.
> 3. `PARAMETER_REGISTRY.json` — registry entries (v5.12).
> 4. `Core/ParamRegistry.cs` — `public const string` declarations.
> 5. `CATEGORY_BINDINGS.csv` — 266 binding rows added (v3.3).
> 6. `LABEL_DEFINITIONS.json` — tier_5 cost rows + tier_7 HVAC
>    audit rows across 19 / 12 categories (v5.10).
> 7. `STING_TAG_CONFIG_v5_0_{ARCH,GEN,MEP,STR}.csv` — regenerated
>    via `regenerate_tag_config_csvs.py` (DEFAULT T5 + new
>    `HVC_DUCT_AUDIT_T7` / `HVC_PIPE_AUDIT_T7` REPLACE sets).
> 8. `FAMILY_PARAMETER_BINDINGS.csv` + `PARAMETER_CATEGORIES.csv`
>    + `BINDING_COVERAGE_MATRIX.csv` — broader binding tables
>    updated with 25 new rows (189 row inserts total).
>
> `PRJ_ORG_PRESSURE_PROFILE_TXT` is correctly absent from
> `LABEL_DEFINITIONS.json` and tag CSVs because it is a
> project-level parameter on `ProjectInformation`, not an
> element-level parameter that needs tag-label coverage.
>
> ---
>
> **Status (2026-04-30):** SUPERSEDED. The 73 `v4-YYYY-xxxx` /
> `v6-YYYY-xxxx` placeholder GUIDs documented below were replaced in
> Phase 169 (commit `3d262e13`). 46 fab/LPS/cost rows now carry the
> canonical UUIDv5 GUIDs from `Core/Fabrication/FabricationParamsV4.cs`
> (namespace `7f9f5e3a-a7c0-b2e4-4d91-4a557c5e3a00`); the 27
> tag-label-only rows (T4 commissioning, T6 carbon, T8 clash,
> T9 as-built/health, T10 ACC/IFC, plus three CST_LABOUR_/INSTALL_
> overrides) carry the `5753b5aa-000T-4000-8000-…` placeholder pattern
> matching the V6 region in `ParamRegistry.cs`. All 73 names are
> aligned across `MR_PARAMETERS.txt`, `MR_PARAMETERS.csv`,
> `STING_PARAMS_V4.txt`, `STING_PARAMS_V6.txt`,
> `PARAMETER_REGISTRY.json`, and `ParamRegistry.cs`. Every reference
> to "placeholder GUIDs" or "real GUIDs will arrive…" in the body
> below is therefore historical context — the placeholders are gone.
> See `docs/CHANGELOG.md` Phase 169 for the repair details.

Generated on `2026-04-22`. Scope: every `public const string … = "…"` declared
in `StingTools/Core/ParamRegistry.cs` and `StingTools/Core/Fabrication/
FabricationParamsV4.cs` is cross-checked against the three authoritative
shared-parameter files that the plugin loads at runtime.

## Files involved

| File | Role | Params (before) | Params (after) |
|---|---|---|---|
| `Data/MR_PARAMETERS.txt` | Master shared-parameter file (loaded by `LoadSharedParamsCommand`) | 2408 | 2408 (unchanged) |
| `Data/Parameters/STING_PARAMS_V4.txt` | v4 MVP fragment — placement + fabrication + LPS + dual-currency | 6 | **52** |
| `Data/Parameters/STING_PARAMS_V6.txt` | v6 MVP fragment — carbon stages, clash triage, ACC sync, health, commissioning | 20 | **27** |

`LoadSharedParamsCommand` loads all three files, so parameters bound via
`ParamRegistry` lookups resolve as long as they appear in at least one of
them.

## What was missing

Before this audit, **79 of the 137** parameters declared as
`public const string` in the two registry files had no corresponding
`PARAM` row in any shared-parameter file. Six of the 79 were already
present in `STING_PARAMS_V4.txt` from the original V4 MVP commit; the
remaining **73** are new to the data tree.

### Group 1 — v4 fabrication / spool (`ASS_*`, 20 params)

Declared in `FabricationParamsV4.cs` class `AssyParams`, consumed by
`Phase 5 AssemblyBuilder` + `ShopDrawingComposer`. Added to
`STING_PARAMS_V4.txt` with placeholder `v4-0001-0000-0000-...` GUIDs
pending family-library authoring.

| Parameter | Datatype | Purpose |
|---|---|---|
| `ASS_SPOOL_NR_TXT` | TEXT | Spool / bundle number assigned by AssemblyBuilder |
| `ASS_WEIGHT_KG` | MASS | Assembly total weight (sum of member volume × material density) |
| `ASS_TEST_PRESSURE_BAR` | NUMBER | Hydrostatic / pneumatic test pressure in bar |
| `ASS_FAB_LOC_TXT` | TEXT | Fabrication location (SHOP / FIELD / VENDOR) |
| `ASS_FAB_SEQ_NR` | INTEGER | Fabrication sequence number within the fab package |
| `ASS_FAB_STATUS_TXT` | TEXT | Fabrication status (NOT_STARTED → INSTALLED) |
| `ASS_SHIP_DATE_TXT` | TEXT | ISO 8601 ship date |
| `ASS_INSTALL_DATE_TXT` | TEXT | ISO 8601 install date |
| `ASS_BOM_REV_TXT` | TEXT | Bill of materials revision code |
| `ASS_QC_INSPECTOR_TXT` | TEXT | QC inspector who released the assembly |
| `ASS_WELD_COUNT_NR` | INTEGER | Number of welds in the assembly |
| `ASS_BOLT_COUNT_NR` | INTEGER | Number of bolted connections |
| `ASS_FLANGE_COUNT_NR` | INTEGER | Number of flanged connections |
| `ASS_FITTING_COUNT_NR` | INTEGER | Number of inline fittings |
| `ASS_LENGTH_TOTAL_MM` | LENGTH | Total linear length of the assembly |
| `ASS_CUT_COUNT_NR` | INTEGER | Number of cut operations |
| `ASS_INSULATION_AREA_M2` | AREA | External insulation surface area |
| `ASS_SUPPORT_COUNT_NR` | INTEGER | Number of supports / hangers |
| `ASS_FAB_NOTES_TXT` | TEXT | Free-text fabrication notes |
| `ASS_SPOOL_DRAWING_REF_TXT` | TEXT | Shop / spool drawing sheet reference |

### Group 2 — v4 lightning-protection system (`ELC_LPS_*`, 18 params)

Declared in `FabricationParamsV4.cs` class `LpsParams`, aligned with
BS EN 62305. Consumed by the v4 LPS validator and shop drawing
populators. Added to `STING_PARAMS_V4.txt` with `v4-0002-0000-0000-...`
placeholder GUIDs.

| Parameter | Datatype | Purpose |
|---|---|---|
| `ELC_LPS_CLASS_TXT` | TEXT | LPS class per BS EN 62305 (I / II / III / IV) |
| `ELC_LPS_ROLLING_SPHERE_RADIUS_M` | LENGTH | Rolling sphere radius in metres |
| `ELC_LPS_MESH_SIZE_M` | LENGTH | Air terminal mesh grid size |
| `ELC_LPS_AIR_TERMINAL_COUNT_NR` | INTEGER | Count of air terminals / lightning rods |
| `ELC_LPS_DOWN_CONDUCTOR_COUNT_NR` | INTEGER | Count of down conductors |
| `ELC_LPS_EARTH_ELECTRODE_COUNT_NR` | INTEGER | Count of earth electrodes |
| `ELC_LPS_EARTH_RESISTANCE_OHM` | NUMBER | Measured earth resistance in ohms |
| `ELC_LPS_BOND_TYPE_TXT` | TEXT | Equipotential bonding type |
| `ELC_LPS_PROTECTION_ANGLE_DEG` | NUMBER | Protection cone half-angle in degrees |
| `ELC_LPS_ZONE_TXT` | TEXT | LPZ zone per BS EN 62305-4 |
| `ELC_LPS_RISK_ASSESSMENT_TXT` | TEXT | Risk assessment reference document |
| `ELC_LPS_SURGE_PROTECTION_LVL_TXT` | TEXT | SPD level (Type 1 / 2 / 3) |
| `ELC_LPS_SEPARATION_DISTANCE_MM` | LENGTH | Separation distance between LPS and services |
| `ELC_LPS_CONDUCTOR_CROSS_SECT_MM2` | NUMBER | Down conductor cross section (mm²) |
| `ELC_LPS_EARTH_TYPE_TXT` | TEXT | Earth electrode type |
| `ELC_LPS_INSPECTION_INTERVAL_MONTHS` | INTEGER | Inspection interval in months |
| `ELC_LPS_TEST_DATE_TXT` | TEXT | ISO 8601 date of last LPS test |
| `ELC_LPS_CERT_REF_TXT` | TEXT | LPS certificate reference |

### Group 3 — v4 dual-currency pricing (`CST_*`, 8 params)

Declared in `FabricationParamsV4.cs` class `CostParams`, used by the
BOQ Cost Manager and fabrication takeoff. Added to
`STING_PARAMS_V4.txt` with `v4-0003-0000-0000-...` placeholder GUIDs.

| Parameter | Datatype | Purpose |
|---|---|---|
| `CST_INTL_PRICE_USD` | NUMBER | International supplier unit price in USD |
| `CST_UG_PRICE_UGX` | NUMBER | Local Uganda unit price in UGX |
| `CST_FX_RATE_USD_UGX` | NUMBER | USD→UGX exchange rate snapshot on quote date |
| `CST_LABOUR_HOURS` | NUMBER | Estimated labour hours per element |
| `CST_LABOUR_RATE_UGX` | NUMBER | Labour rate in UGX per hour |
| `CST_SHIPPING_UGX` | NUMBER | Shipping cost component in UGX |
| `CST_DUTY_PCT` | NUMBER | Import duty percentage on USD price |
| `CST_QUOTE_REF_TXT` | TEXT | Supplier quote reference number |

### Group 4 — v6 labour + commissioning (`CST_LABOUR_*`, `COMM_*`, 7 params)

Declared in the `#region V6 parameters` block of `ParamRegistry.cs` but
omitted from `STING_PARAMS_V6.txt`. The N-G12 crew-aware labour engine
and the N-G16 QR commissioning workflow both read these. New GROUP `22
COMMISSIONING` was added to host the five `COMM_*` rows; the two
`CST_LABOUR_*` rows reuse existing GROUP `2 CST_PROC`.

| Parameter | Datatype | Purpose | GUID |
|---|---|---|---|
| `CST_LABOUR_CREW_TXT` | TEXT | Labour crew / role (e.g. `PIPEFITTER_2P`) | `v6-0001-…-000000000015` |
| `CST_LABOUR_RATE_GBP` | NUMBER | Labour rate in GBP per hour (UK override) | `v6-…-000000000016` |
| `COMM_STATE_TXT` | TEXT | Commissioning state (NOT_STARTED → HANDED_OVER) | `v6-…-000000000017` |
| `COMM_DATE_TXT` | TEXT | ISO 8601 date of commissioning state change | `v6-…-000000000018` |
| `COMM_OPERATIVE_TXT` | TEXT | Operative who performed the test | `v6-…-000000000019` |
| `COMM_WITNESS_TXT` | TEXT | Witness (client / CA representative) | `v6-…-00000000001a` |
| `COMM_NOTES_TXT` | TEXT | Free-text commissioning notes | `v6-…-00000000001b` |

## Why they were missing

- **Fabrication / LPS / cost (46)** — `FabricationParamsV4.cs` was
  introduced as part of the v4 MVP work (see the `v4 MVP` section of
  `CLAUDE.md`). That commit shipped the C# constants with placeholder
  GUIDs but deferred the actual shared-parameter-file authoring, noting
  the dependency on family-library work. Until the family library is
  authored with real GUIDs, the placeholder rows in
  `STING_PARAMS_V4.txt` make the parameters bindable via
  `LoadSharedParamsCommand` so that validators, Excel exporters, and
  schedule generators that reference them (`AssemblyBuilder`,
  `ShopDrawingComposer`, N-G4-group LPS validator, BOQ Cost Manager) do
  not silently no-op on missing bindings.
- **Labour + commissioning (7)** — these landed in `ParamRegistry.cs`
  after the original `STING_PARAMS_V6.txt` fragment was written. The
  fragment was never back-filled, so bindings for the crew / GBP-rate
  override and the QR commissioning workflow depended on the author
  hand-running the loader against a user-supplied file.

## Workflow

1. `LoadSharedParamsCommand` reads all three files in order. Each
   `PARAM` row defines GUID, name, datatype, and group, then
   `ParamRegistry` binds the parameter to the category set declared in
   `PARAMETER_REGISTRY.json`.
2. Before the family-library effort finalises the v4 GUIDs, the
   placeholder `v4-YYYY-xxxx` strings are accepted by the loader but
   should not be deployed to production projects (they will be
   rewritten in place when real GUIDs are assigned).
3. When real GUIDs arrive, replace both the `STING_PARAMS_V4.txt` row
   AND the matching `…_GUID` constant in `FabricationParamsV4.cs`. The
   two must stay in sync; the registry’s `EnsureLoaded()` routine
   resolves the string value against the GUID list on first use and
   caches the mapping.

## Verification

Running the coverage diff after this update reports **zero**
registry-declared parameters missing from the combined data files:

```
# params declared in ParamRegistry.cs + FabricationParamsV4.cs
#   137
# params covered by MR_PARAMETERS.txt + STING_PARAMS_V4.txt + STING_PARAMS_V6.txt
#   137
# delta: 0
```

## Non-parameters intentionally excluded

The raw code scan also surfaces ~700 additional upper-snake-case
string literals that are **not** Revit shared parameters. They are not
included in this audit. Representative categories:

- **Revit built-in parameter enum names** — `ALL_MODEL_COST`,
  `ALL_MODEL_MARK`, `ALL_MODEL_MANUFACTURER`,
  `ALL_MODEL_MATERIAL_ASSET_NAME`, `ANALYTICAL_HEAT_TRANSFER`, etc.
  These are `BuiltInParameter` values, not `SharedParameter` names.
- **Config keys** — `TAG_FORMAT`, `TAG_PREFIX`, `TAG_SUFFIX`,
  `SEQ_INCLUDE_ZONE`, `COMPLIANCE_GATE_PCT`, `CUSTOM_VALID_LOC`,
  `CUSTOM_VALID_ZONE`, `ACTIVE_PRESET`, `ACTIVE_SECTOR_PACK` —
  consumed from `project_config.json`, not Revit bindings.
- **Short token names / partial matches** — `ASS_TAG_1`, `ASS_LOC`,
  `ASS_DISC`, `ASS_SYS`, `ASS_TAG`, `ASS_REV`. These are substrings of
  real parameters (`ASS_TAG_1_TXT`, `ASS_LOC_TXT`, etc.) used when the
  code strips the `_TXT` suffix for display.
- **DWG layer keywords** — `ARK_VAEG`, `ARK_VEGG`, `ARC_WAND` — used by
  `CADToModelEngine.LayerMapper` for multi-lingual layer name
  matching.
- **UI / presentation constants** — `PRES_BLUE_INT`, `PRES_CANDY_INT`,
  `PRES_EARTH_INT`, `FILTER_NONE`, `FILTER_CRITICAL`, etc.

If any of these later become real parameters, re-run this audit (the
extraction commands are reproducible — see the file history of this
document).
