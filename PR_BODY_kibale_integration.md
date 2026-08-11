# Kibale review — formula engine, BOQ integrity, room finishes, scope boxes, material data

Integrates four branches into one reviewable change: **22 commits**, all originally
based on `ff054c77a`, merged in dependency order with **zero conflicts**.

---

## ⚠️ Read this first: compile-verified only. Nothing here has been run in Revit.

Every gate below is a static or headless check. **No part of this change has been
exercised against a real Revit model.** That matters more than usual, because this
batch alters numbers that end up in a bill:

- `lookup()` turns **29 call sites** from writing `0` to writing real quantities.
- The mortar formula's arithmetic changed; it feeds cement and sand.
- Two commands now **refuse to export** where they previously succeeded.
- Four new shared parameters do not bind until `LoadSharedParams` is run.

**Do not merge on the strength of the green gates alone.** The verification plan is
[`docs/KIBALE_REVIT_VERIFICATION.md`](docs/KIBALE_REVIT_VERIFICATION.md); it must be
worked through on a real model, and the take-off numbers signed off by a QS, first.

---

## ⚠️ Scope surprise: one unrelated commit rode in

`a22752858 refactor(quota): retire the per-user quota axes — D1 owns seat entitlement`

This is a **Planscape.Server + planscape-web change about D1 seat entitlement**. It has
nothing to do with Kibale. It is in this PR because `claude/kibale-np-bim-modeling-f5e653`
was branched from it rather than from `ff054c77a` — that branch's merge-base is
`b06f3dce0`, not `ff054c77a` as the other three.

It touches 6 files: `OnboardingController.cs`, `TenantAdminController.cs`,
`QuotaAttribute.cs`, `QuotaGuardService.cs`, `planscape-web/app/settings/team/page.tsx`,
`planscape-web/lib/types.ts`.

**Not acted on — this is a scope decision, not a merge conflict.** Options: land it here
and say so in the title; or rebuild the integration branch cherry-picking only
`0404d344d 344b90b73 e2f5cb76b a69ae361e` from that branch. Reviewer's call.

---

## What this closes, by gap id

### G-5 · Nothing failed loudly — every formula failure wrote a zero
`5ee46d27c` `5d7443105`

The recursive-descent evaluator substituted `0` on every failure path, and
`WriteNumericResult` writes when the current value is empty or near-zero — so a formula
that *could not be evaluated* stamped a real-looking `0` quantity into the model and
priced through to the bill.

Four paths now record a failure reason instead: division by zero, undefined power
(`0^-1`, `(-1)^0.5`), unknown identifier / non-numeric value, and an unresolved function
call. `EvaluateNumeric` turns that into `null`. It already returned `double?` and all
nine call sites already guarded on `HasValue`, so no caller changed.

`if()` is treated as **lazy** — both branches are parsed to advance the cursor, but only
the branch actually returned may fail the formula. Without that, a divide-by-zero in the
discarded branch would void results the model is entitled to.

The warn budget (200) resets per batch at `PostTagCleanup` and
`FormulaEvaluatorCommand.Execute`, rather than being a session-lifetime counter that one
messy model could exhaust into permanent silence.

### G-13 · `lookup()` implemented — 27 formulas turned back on
`a9eec757f` `b88cc4b4c`

`lookup(TABLE, KEY, COLUMN)` had no implementation: `lookup` fell through to the variable
branch, evaluated to `0`, and left its unconsumed argument list to terminate the parse —
so the *rest of the expression* was discarded too. 27 formulas / 29 calls, every one
writing a zero into a bill.

Verified against the shipped data before building: 29 calls, **zero missing tables, zero
missing columns**, and a simulation of the exact resolution order resolves all 29 under
both worst cases (key present-but-empty, key absent entirely).

Uses a new `MaterialLookupCsv.TryGetProperty` rather than `GetProperty`, because
`GetProperty` collapses *absent* and *present-and-legitimately-zero* into the same `0`.
`MATERIAL_LOOKUP.csv` holds eight true zeros, six reachable through these exact columns
(`CONCRETE C10/C7.5 STEEL_KG_PER_M3` unreinforced blinding; `ROOF_SHEET
CLAY_TILE/CONCRETE_TILE FASTENERS_PER_M2` nailed not fixed; `FORMWORK
COLUMN/FOUNDATION PROPS_PER_M2` self-standing). Treating those as a miss would have
failed a C10 blinding pour's steel formula and — composed with G-5 — skipped a write
whose correct answer is zero.

`b88cc4b4c` completes `Input_Parameters` for four formulas whose expressions referenced
names absent from column 5, so those names never entered the evaluation context
(`BLE_FLR_TILE_QTY_NR`, `CST_TOTAL_ROOFING_COST`, `CST_TOTAL_PLASTER_COST`,
`PER_SUST_WTR_RATING_NR`).

### Mortar volume — corrected because `lookup()` made it live
`6433778b3`

`CST_S_MAS_MORTAR_VOLUME_CU_M` read `NET_AREA × (THICKNESS/1000) × MORTAR_RATIO × 12`.
But `MORTAR_RATIO` is already **m³ per m² of wall face** — the data says so itself
("Mortar volume per m² (10mm joints)", "0.030→0.055 m³/m²"), and the analogous
`BLOCK.MORTAR_VOLUME_FACTOR` carries an explicit `m³/m²` unit column. The factor contains
the thickness; multiplying by it and then by 12 double-counts.

The `12` existed to reconstruct the "30 %" in the old description — `0.025 × 12 = 0.30`
exactly. That only holds for the `0.025` rows; for the one-brick bonds it implies
**58–66 % mortar by volume**, which is physically impossible.

Worked example, 50 m² of ENGLISH bond one-brick wall (215 mm, ratio 0.050):

| | before | after |
|---|---|---|
| Mortar | 6.4500 m³ | **2.5000 m³** (2.58× over) |
| Cement @ 1:5 | 48.37 bags | **18.75 bags** |
| Sand @ 1:5 ×1.2 | 10.06 m³ | **3.90 m³** |

Three fields changed, not one — the description is replaced because it is the *origin* of
the defect and would otherwise reintroduce the multiplier from the docs.

### E-1 / E-1b · Every material-library rate was ~3,700× too low, and it won
`2113dfedb`

`MaterialCommands` writes `ALL_MODEL_COST` from the library's `MAT_COST_UNIT_USD` column;
`MaterialLibraryRateProvider` read it back labelled `UGX`, suppressing the conversion the
value needed. At priority 95 it also outranks the correct category rate from
`CsvRateProvider` (90) — so the wrong figure won.

FX path verified end to end: `BOQCostManager:872` reads `UGX_PER_USD` →
`RateProviderRegistry.Get` → `ConvertCurrency` → `RateCurrency.ToUgx` case `"USD"` →
`× ugxPerUsd`. No new conversion code.

`MAT_COST_UNIT_UGX` is not the fix: measured across the library it is `USD × 3700` on all
815 BLE rows and 441 of 464 MEP rows, so reading it would freeze a 2026 rate into every
material permanently.

**Left open, recorded in the code:** a hand-typed UGX cost is now read as USD and
inflated. The provider cannot separate the two while both share `ALL_MODEL_COST`; that
needs the dedicated `STING_MAT_RATE_*` pair at material creation (gap E-12).

### H-1 · The IFC quantity writer reported success having written nothing
`60e24be6a` `ba703bb23`

`StampAllElements` incremented one counter per element *visited*, unconditionally, and the
command surfaced that as "Elements stamped". The stamp helpers returned `void` on the
unbound-parameter path, so where the `Qto_*` shared parameters were not bound every write
was a silent no-op and the IFC shipped with no quantities.

All helpers now return `bool`; the tally separates *elements visited* / *elements written*
/ *parameters written* / **quantities written**.

The gate is **quantities**, not parameters-of-any-kind. A combined gate would pass the
commonest broken configuration — `Pset_StingCost.*` bound (they ship with the standard
parameter load) and the IFC-standard `Qto_*` not — and `Pset_StingCost.Currency` is a
hardcoded non-empty string that would satisfy it on every element by itself. The two
failure shapes get different messages because they need different fixes.

Applied at both entry points: `BOQExportIfcQtoCommand` and `Cost_StampIfcQuantities`.

### H-3 · Readiness gates reported 100 % on an empty bill
`b11289a7e`

`pricedPct`, `epdPct` and `SchemeCoveragePct` returned **100** on a zero denominator, so a
BOQ with no modelled rows reported "100 % priced" and "100 % EPD-verified" to the QS. The
rest of the codebase already disagreed — `CompliancePercent`, `StrictPercent`,
`RevisionPercent`, `SheetCompliancePct`, `DataCompletenessPercent` and `BOQModels:423` all
return 0 there.

All three now distinguish *nothing to measure* from *measured, and it is zero*: the BOQ
reports display `"n/a"`, and `SchemeCoveragePct` becomes `double?` with a
`SchemeCoverageText` companion.

### A-1 · A failed take-off quantity silently became zero
`abeb6142c`

`FallbackQuantity` returned `0.0` for measured units (m/m²/m³/kg) when the rule's
quantity source did not resolve. The row still shipped with a description, a
classification, a rate and an NRM2 section — so it read as a genuine cheap item and priced
into the Contract Sum at nil.

**The defect is gate topology plus signal quality, not an absence of gating.** This is
worth stating precisely, because the obvious framing is wrong and a reviewer who checks
will find it wrong:

- The condition *was* already counted (`BoqUncostedRollup.CouldNotMeasureCount`) and
  `BlocksExport` *already included it*, so the professional/tender export path
  (`BOQProfessionalExportCommand:92`) already refused. That part worked.
- What failed is **topology**: `BOQPrepForExport` — the command whose entire job is to say
  "safe to export" — never consulted the rollup at all. Its gates were compliance,
  containers, stale, BOQ band, warnings, placeholders.
- And **signal quality**: `CouldNotMeasureCount` *infers* the condition from
  `Quantity ≈ 0`, so it cannot separate "never measured" from "measured, genuinely zero"
  (a demolition line, a nil provisional item). Carrying false positives, it can only ever
  be advisory — which is precisely why it sat in a rollup rather than a gate.

So the fix is on both axes: make the take-off report the failure *explicitly*
(`double?` → `QuantityResolved = false`) so the signal is false-positive-free, then gate
`BOQPrepForExport` on that. `QuantityResolved` defaults **true**, so every existing
construction site, snapshot and clone keeps its meaning. `CouldNotMeasureCount` is kept
as-is. `CostValidators` now separates `COST.QTY.UNRESOLVED` from `COST.QTY.ZERO`.

### K-1 / K-12 · Room finishes written to both parameter families
`daf87d34b`

Writes both room-finish parameter families and counts honestly.

### K-2 · Finish code legend, parameters, bindings
`9f02aa3bf`

Ships `STING_FINISH_CODES.csv` (28 codes), 6 new shared parameters, 10 new category
bindings, and finish columns in `MR_SCHEDULES.csv`.

### K-3 · Floor-finish elements from room finish codes
`6f700cbc2`

`FinishFloorCommands` + `FinishCodeRegistry` create floor elements from room finish codes.
**Highest-risk item in the Revit plan after `lookup()`** — room boundary geometry
(L-shapes, inner loops) is where this will need adjustment.

### K-7 / K-8 · ISO sheet numbering
`84a7919af` `15bc74155` `fd52eea1d`

`{lvl}` rendered as an empty segment with no warning because `IsoNaming` had no
profile-level default for Level, while Volume/Type/Role all did. `{type}` defaulted to
`DR` regardless. Plus one shared scope-box name parser (replacing duplicates) and the new
**Scope Box Manager** dialog — pick the drawing type, never type it.

### Material data alignment
`a69ae361e` and docs `0404d344d` `344b90b73` `e2f5cb76b`

580 identity classes and 34 material names normalised; new `MAT_COST_UNIT_OF_MEASURE`
column takes `BLE_MATERIALS.csv` / `MEP_MATERIALS.csv` from 72 to 73 columns, with
`MATERIAL_SCHEMA.json` updated to match. Plus the Kibale gaps register and modelling
playbook under `GUIDES/`.

---

## Known interactions — checked explicitly

| # | Interaction | Result |
|---|---|---|
| 1 | `MaterialPropertyHelper` column constants vs the new 73rd column | **PASS.** Highest constant is 69 (`ColTensStrength`); new column is index 72, so appending is safe. Stronger check also run: **all 70 constants still map to their documented header name** — no drift from the realignment. BLE and MEP headers identical. |
| 2 | Finish codes vs merged `BLE_MATERIALS.csv` | **PASS.** All 28 `MAT_NAME` references resolve against the 815 merged names — **0 missing**, matching the pre-merge result. |
| 3 | `StingDockPanel.xaml` + `StingCommandHandler.cs` touched by two branches | **PASS.** Auto-merged. All new buttons and cases present, nothing lost from base, **no duplicate case labels**. Only tag-count increases are the 2 new finishes tags and `ScopeBoxManager` 1→2, which came from the scope-box branch itself (a second entry point for the existing tag). Note: the new dialog is wired through the **pre-existing** `ScopeBoxManager` case — scope-box added no new handler case. |
| 4 | `CATEGORY_BINDINGS.csv` + `MR_PARAMETERS.txt` GUID collisions | **PASS.** No GUID collisions across the full 3,398-parameter set, no duplicate parameter names, no new GUID colliding with a pre-existing one, and all 10 new binding rows go 0→1 (none duplicated). **Counts differ from the brief: 6 new parameters (not 4) and 10 new bindings (not 8).** 201 duplicate binding rows pre-exist in base and are unchanged. |
| 5 | `MATERIAL_SCHEMA.json` vs merged CSVs | **PASS.** 73 declared columns, `column_order` an **exact ordered match** to both BLE and MEP headers; `required_columns` set matches. `MAT_COST_UNIT_OF_MEASURE` at position 72. |

---

## Verification gates

| Gate | Result |
|---|---|
| `dotnet build -c Debug -t:Rebuild` | ✅ **0 errors, 0 warnings** |
| `StampDrawingTypeChecksums -- --check` | ✅ **93/93 correct, 0 drifted** |
| `tools/check_path_discipline.ps1` | ✅ Tier 1 **0**, Tier 2 **0**, no new violations |
| `python tools/fix_material_data.py` (dry run) | ✅ **0 changes** on both files — identity class 0, names normalised 0, cost unit populated 0, element type filled 0. Material CSVs and schema merged **byte-identical** to the branch tip. |
| `StingTools/Data/**/*.json` parse | ✅ **266/266** |

### Tests — 1,142 passing, 2 failing, both pre-existing

| Project | Result |
|---|---|
| Sustainability | ✅ 438 / 438 |
| Tags | ✅ 243 / 243 |
| Boq | ✅ 196 / 196 |
| Cost | ✅ 90 / 90 |
| Clash | ⚠️ 63 / 64 — `DuplicateLiveClashUpdater_FileIsRemoved` (#596) |
| Routing | ⚠️ 44 / 45 — `ComputeRoute_StraightHorizontal_ProducesOneSegment` (#597) |
| Scheduling | ✅ 38 / 38 |
| SitePhotos | ✅ 16 passed, 1 skipped |
| Licensing | ✅ 14 / 14 |
| Connectivity | ➖ empty project, 0 tests |

**Both CI-excluded tests still fail** — neither has started passing. They are the only two
failures, which is also the evidence that this merge introduced no new ones.

### One forward-looking note on the checksum gate

The gate passes because the new `IsoNaming.Level` property carries
`NullValueHandling.Ignore` and is null in every shipped type, so it is omitted from the
serialisation the hash is taken over. **The moment any corporate drawing type sets
`"level"` in `STING_DRAWING_TYPES.json`, that type's checksum changes and
`StampDrawingTypeChecksums` must be re-run.** That is the documented behaviour, not a
defect — but it is a trap worth knowing about, since the K-7 fix exists precisely so
people start populating that field.

---

## Housekeeping

`PR_BODY_kibale_part1.md` at the repo root is **superseded by this file** — it describes
only the first branch's 4 commits as though they were the whole change. Delete it before
merge. Left in place rather than removed, since tidying was outside the brief.
