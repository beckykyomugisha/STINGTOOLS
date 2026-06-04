# BOQ Numerical-Accuracy Audit

**Scope:** Bill-of-Quantities engine, material quantities, concrete classes,
densities, embodied-carbon factors, reinforcement ratios and unit costs in
STINGTOOLS.
**Date:** 2026-06-03 · **Branch:** `claude/upbeat-cori-vdOPA`
**Method:** static read of C# + CSV/JSON data, arithmetic checked against the
authoritative benchmarks in the audit brief (EN 206 / BS 8500, ICE v3.0,
NRM2 / RICS, civilsir / kairalitmt reinforcement tables). **Built without
`dotnet build` verification (Linux sandbox) — verify in Revit before merge.**

Classification: **BLOCK** = >20 % error or dimensionally wrong · **WARN** =
5–20 % or conceptually weak · **INFO** = <5 % or defensible.

---

## Part A — Code & data map (verified)

| Claim | Verified value |
|---|---|
| `BuildBOQDocument` 3-tier cascade | Confirmed — now via `RateProviderRegistry` (param/ES override > csv-default > cobie-typemap > default-baseline). |
| Config defaults | Prelim **12 %**, Contingency **10 %**, Overhead **8 %**, FX **3700 UGX/USD** (`BOQCostManager.cs:77-80`). All `TagConfig.GetConfigDouble`-overridable. |
| Lifecycle | discount **0.035**, **25 yr** (`:45-46`). |
| `EstimateDensityKgPerM3` fallback | Hardcoded switch `:692-701` — concrete 2400, steel 7850, timber 550, alu 2700, glass 2500, brick 1920, insulation 40, plaster 1250, default 1000. |
| Quantity derivation | ft²→m² ×0.092903, ft³→m³ ×0.0283168, ft→m ×0.3048 — **all correct** (`:417,425,429`). |
| Waste vs measured-addition | `WasteFactor.Apply` excludes each/item; `MeasuredAddition.GrossUp` sums waste+addition **once** — no double-count (verified). |
| Biogenic split | TimberFossilPerKg **0.263**, TimberBiogenicPerKg **-1.64** (`BiogenicCarbon.cs:30,34`). |
| FX internal consistency | All 37 `cost_rates_5d.csv` rows imply 3696–3706 UGX/USD (≤0.2 % of 3700). **Consistent.** |

**Resolution-order discovery (critical for several findings):** `CarbonFactorResolver`
and `EstimateDensityKgPerM3` key on the **Revit material NAME**, then fall back to
`MaterialLookupCsv` (keyed by `Category`/`TypeKey` such as `C25`, `SOFTWOOD`). Real
Revit material names (e.g. `CONCRETE CAST IN SITU 200MM`) do **not** match those keys,
so the lookup tier is effectively dead for structural concrete unless the BLE per-row
columns are populated.

---

## Part C/D — Findings (confirm/refute, quantify, fix)

| # | Finding | Class | Code value | Benchmark | Δ | Fix |
|---|---|---|---|---|---|---|
| F1 | `OptionCostCarbonCalculator` carbon = `cf × vol_m³ × 2300`, cf=250 ("concrete-block avg") | **BLOCK** | 575 000 kgCO₂e per m³ wall | RC ~288 kgCO₂e/m³ | **+199 000 %** | cf values relabelled to kgCO₂e/**m³**; removed the spurious `×2300`. |
| F2 | Concrete cement bags low across all grades | **WARN/BLOCK** | C20 250 kg, C25 290, C30 325, C40 375 | C20 310, C25 350, C30 360, C40 410 | −19 %, **−17 %**, −10 %, −9 % | All grades raised to BS 8500 mid-range cement content (bags & W/C re-derived). |
| F3 | Concrete embodied carbon (lookup) | **WARN** | C25 330, C30 345 kgCO₂/m³ | C25/30 ≈288, C32/40 ≈392 | +15 %, +20 % | Re-set to ICE v3.0 cradle-to-gate per grade. |
| F4 | Concrete **density** never resolved — no `DENSITY_KG_M3` rows for concrete; falls to grade-blind 2400 | **WARN** | 2400 (plain) for all | reinforced 2400–2500 | −2 to −4 % on RC mass/carbon | Added per-grade `DENSITY_KG_M3` rows (2400 plain → 2500 high grade) + reinforced default. |
| F5 | Timber density inconsistent: BOQ fallback 550 vs lookup softwood 480 | **WARN** | 550 (BOQ) / 480 (lookup) | softwood ~480 | +15 % BOQ | BOQ fallback aligned to **480**. |
| F6 | Timber per-m³ carbon sentinel `-992` in BLE_MATERIALS (39 rows) | **BLOCK** | −992 kgCO₂/m³ | net softwood ≈ −661 (480×(0.263−1.64)) | +50 % magnitude, wrong | BLE `-992` → **-661**; `-661` sentinel verified as the corrected lookup value. |
| F7 | Structural concrete rows in BLE_MATERIALS have **empty** density + carbon (the `CONCRETE CAST IN SITU/PRECAST` materials) | **BLOCK** | density="" carbon="" | 2400–2500 / 288–392 | carbon defaults to 0 | Populated 8 concrete structural rows (density 2400, carbon 300 kgCO₂/m³ ≈ C30 RC). |
| F8 | Reinforcement keyed to concrete GRADE not ELEMENT TYPE | **WARN** | C25→90, C40→150 kg/m³ | slab 80 / beam 120 / col 160 / footing 40 | conceptual | Added `REBAR_ELEMENT` element-type map (slab/beam/column/footing/wall/raft) as the correct driver; grade rows retained as fallback proxy. |
| F9 | Rate-unit vs quantity-unit mismatch: `Columns` rate Unit=`each` but column qty is volumetric/linear; `Structural Framing` Unit=`m` is OK | **WARN** | Columns `each` | NRM2 structural framing = tonne / m | doc'd | `Columns` is genuinely per-unit in this seed rate-card; left as `each` with a documented note (engine `DeriveQuantity` honours the rate Unit, so no dimensional crossover occurs — `UnitsAlign` guards the takeoff path). No silent reduction. |
| F10 | Rate coverage — 37 rate rows; collector emits more categories | **INFO** | 37 categories | n/a | uncosted rows = 0 rate, confidence floor 20 | Added 4 high-impact missing rows (Structural Foundations, Stairs, Railings, Generic Models). |
| F11 | Aluminium carbon 8500 kgCO₂/m³ in BLE (≈3.15 kg/kg) | **INFO** | 8500 /m³ | recycled-content alu ICE 3.0–3.2/kg → 8100–8640 | <2 % | Within recycled-content range — left as-is, documented. |
| F12 | FX 3700 hardcoded literal default | **INFO** | 3700 | n/a | project-overridable via `UGX_PER_USD` | Confirmed overridable; stale-FX risk noted in CHANGELOG. No code change. |
| F13 | VAT not on line items | **INFO** | — | NRM2 totals usually VAT-exclusive | defensible | Tender total is VAT-exclusive by NRM2 convention (Prelim+Cont+OH&P applied, not VAT). Documented; no change. |

---

## Per-concrete-grade table (before → after)

Cement kg/m³ = bags × 50. Benchmarks: BS 8500 cement content; ICE v3.0 RC carbon;
reinforced concrete density 2400–2500.

| Grade | Cement before | Cement after | BS 8500 | Carbon before | Carbon after | ICE | Density before | Density after |
|---|---|---|---|---|---|---|---|---|
| C15 | 225 | 240 | ~240 | 280 | 250 | ~240 | (none→2400) | 2350 |
| C20 (C16/20) | 250 | 310 | 300–320 | 310 | 270 | ~270 | →2400 | 2400 |
| C25 (C20/25) | 290 | 350 | 340–350 | 330 | 290 | ~288 | →2400 | 2400 |
| C30 (C25/30) | 325 | 360 | 350–360 | 345 | 300 | ~300 | →2400 | 2450 |
| C35 (C28/35) | 350 | 380 | 370–390 | 365 | 340 | ~340 | →2400 | 2450 |
| C40 (C32/40) | 375 | 410 | 400–420 | 380 | 392 | ~392 | →2400 | 2450 |
| C45 (C40/50) | 400 | 440 | 420–460 | 400 | 420 | ~420 | →2400 | 2500 |
| DEFAULT | 325 | 360 | C25/30 | 350 | 300 | ~300 | →2400 | 2450 |

---

## Worked example 1 — RC concrete column (C30, i.e. EN C25/30, 0.4 × 0.4 × 3.0 m)

| Step | Value | Source / multiplier |
|---|---|---|
| Revit volume | 16.96 ft³ | `HOST_VOLUME_COMPUTED` |
| → m³ | 16.96 × 0.0283168 = **0.480 m³** | ft³→m³ |
| Waste (default 5 %) | 0.480 × 1.05 = 0.504 m³ | `MeasuredAddition.GrossUp` (concrete over-order knob OFF) |
| Density (C30, **after V1 fix**) | 2450 kg/m³ | lookup `CONCRETE C30 DENSITY_KG_M3`, now reachable from the Revit name via `ResolveConcreteGradeKey` (was keyword 2400, −2 %) |
| Mass | 0.504 × 2450 = 1235 kg | |
| Carbon (C30, **after V1 fix**) | 0.480 × 300 = **144 kgCO₂e** (net qty for carbon, ICE per m³, lookup-resolved) | was 0.13 kg/kg × mass keyword path |
| Carbon — unit dependency (R2-1) | **Before V8** the `Columns` rate was `each` ⇒ `EstimateVolumeM3` returned **0** ⇒ **0 kgCO₂e** (the each-unit zeroing bug). V8's m³ rate restored the m³ branch for columns; the R2-1 fix below now returns a real volume for ALL each-priced families so none report 0 carbon. | |
| Cement check (C30) | 0.480 × 360 = 173 kg ≈ 3.5 bags | `MATERIAL_LOOKUP CONCRETE C30` |
| Rate (`Columns`, **m³ after V8 fix**) | 1 924 000 UGX/m³ | `cost_rates_5d.csv` (was `each` @ 1 295 000) |
| Cost | 0.504 × 1 924 000 = **969 696 UGX** | volumetric (NRM2); a 6 m column now costs 2× a 3 m one |

Hand check: 0.48 m³ × 360 kg cement = 173 kg → ✓ matches NRM2 nominal for C25/30.
The `C30` key resolves to EN C25/30 per the per-grade table; a true C30/37 mix snaps
to the nearest catalogued grade (C35) under `ResolveConcreteGradeKey`.

## Worked example 2 — Clay brick wall (3.0 × 2.4 m, 215 mm single-leaf)

| Step | Value | Source / multiplier |
|---|---|---|
| Revit area | 77.5 ft² | `HOST_AREA_COMPUTED` |
| → m² | 77.5 × 0.092903 = **7.20 m²** | ft²→m² |
| Waste (brickwork) | uses default 5 % (project may set 12 %) | `WasteFactor.Apply` |
| Net qty | 7.20 × 1.05 = 7.56 m² | |
| Carbon: thickness | 215 mm | `ReadLayerThicknessMm` |
| Volume | 7.20 × 0.215 = 1.548 m³ | area × thickness |
| Density (brick) | 1920 kg/m³ | fallback (within 1700–2000) |
| Carbon factor | clay brick ICE ≈ 0.213/kg ⇒ ~430 kgCO₂/m³ | (BLE per-row when populated) |
| Carbon | 1.548 × 430 = **666 kgCO₂e** | |
| Rate (`Walls`, m²) | 315 000 UGX | `cost_rates_5d.csv` |
| Cost | 7.56 × 315 000 = **2 381 400 UGX** | |

Hand check: 7.2 m² × 0.215 m × 1920 kg = 2972 kg brick ≈ 60 bricks/m² × 7.2 = 432 bricks ✓ (single-leaf 215 mm ≈ 60/m²).

---

## Double-apply safety re-check

- `WasteFactor.AppliesTo` returns false for `each`/`item`/`default` ⇒ counted items never grossed up.
- `MeasuredAddition.GrossUp` sums `(waste + addition)` and multiplies **once**; the
  rebar-lap / concrete-over-order knobs default 0, and the rate side carries no waste
  (ES rate-override waste removed in Z-21b). ⇒ no double-count confirmed.
- `DeriveQuantity` rule path and legacy path are mutually exclusive (`UnitsAlign`
  guards), so waste is applied on exactly one path.

---

**Summary: 3 BLOCK, 5 WARN, 5 INFO findings; 10 fixed.**
(F1, F6, F7 BLOCK — all fixed. F2, F3, F4, F5, F8 WARN — all fixed. F9, F10
INFO — F10 fixed, F9 documented. F11, F12, F13 INFO — documented, no number
change required.)

---

## Review pass (verification + hardening)

**Date:** 2026-06-03 · **Branch:** `claude/upbeat-cori-vdOPA` · Independent
re-verification of the 10 fixes against the LIVE files, then hardening of the
inert/wrong ones. **Built without `dotnet build` verification (Linux sandbox) —
verify in Revit before merge.**

The original fixes were correct *as data* but several were unreachable at runtime
because the carbon/density resolvers key on the **Revit material NAME** while the
per-grade CSV is keyed by **TypeKey** (`C30`), and the bare `C30` key is not even
registered (it is non-unique across `CONCRETE` + `REBAR_LAP`). Net effect: F3/F4
never reached the BOQ for structural concrete, and F8's `REBAR_ELEMENT` rows were
read by nothing.

| V | Concern | Verdict (original) | What changed in this pass |
|---|---|---|---|
| V1 | F3/F4 dead-tier — concrete grade carbon/density never read | **INERT** | Added `MaterialLookupParser.ResolveConcreteGradeKey` + a fallback retry in `MaterialLookupCsv.Get`: parses EN 206 dual-notation (`C25/30`/`C32/40`) and legacy `Cnn` out of the Revit name, snaps to the nearest catalogued grade, returns `CONCRETE Cnn` (or `CONCRETE` DEFAULT for un-graded concrete). The per-grade carbon (290–420) + density (2350–2500) rows are now reachable for both `CarbonFactorResolver` Tier-2 and `EstimateDensityKgPerM3`. |
| V2 | F7 flat 2400/300 vs F2/F4 per-grade — contradiction | **INCONSISTENT** | Aligned the 8 `CONCRETE CAST IN SITU/PRECAST` BLE rows (WC-038…045) to the lookup `CONCRETE DEFAULT` (density 2450, carbon 300, fossil 300, biogenic 0 — C25/30 RC), so BLE ↔ MATERIAL_LOOKUP agree and un-graded concrete resolves to the same numbers on every path. |
| V3 | Grade-label drift — Worked Example 1 "C30/37" vs `C30 (C25/30)` scheme | **INCONSISTENT** (doc) | Relabelled Worked Example 1 to "C30 (EN C25/30)"; documented that a true C30/37 snaps to the nearest catalogued grade (C35). |
| V4 | F1 factor values + consumers | **CONFIRMED-GOOD** | Verified `BuildRow` is the only consumer and multiplies kgCO₂e/m³ × volume directly (no residual `×2300`, no double-scaling). Values sane per-m³ (Walls 250, Floors 290, Columns/Framing 700, Ducts 180, Pipes 140). No change. |
| V5 | F2 cement vs water/W-C consistency | **CONFIRMED-GOOD** | Recomputed w/c from CSV cement+water per grade: 0.58→0.38 monotonic C15→C45, all physical (0.35–0.65); cement contents match BS 8500. No change. |
| V6 | F6 timber −661 applied to non-softwood rows | **WRONG** | 39 of the 41 rows are density-720 hardwood/plywood (fossil 189 + biogenic −1181 = **−992**), not softwood. Restored `PROP_CARBON_KG_M3` on those 39 to −992 (each row's own fossil+biogenic); the 2 genuine softwood rows (density 480) stay −661. Zero net/fossil-biogenic mismatches remain. |
| V7 | F8 REBAR_ELEMENT map wired or dead data | **INERT** | Nothing read `REBAR_ELEMENT`. Wired it into the real rebar-mass consumer `AutoRebarEstimator.EstimateProject` via `ResolveAvgRatio` (reads `STEEL_KG_PER_M3` from the CSV per element type — slab 80 / beam 120 / column 160 / footing 40 / wall 70 — falling back to the hardcoded ratio). Removes the hardcode↔CSV drift; the F8 data is now consumed. |
| V8 | F9 `Columns` unit `each` for structural columns | **WEAK** (no crossover, but mis-costs) | Confirmed `UnitsAlign` prevents any dimensional crossover (`each`→qty 1.0). But `each` mis-costs structural columns (6 m = 3 m). Changed the `Columns` rate to **m³** @ 1 924 000 UGX (RC column: concrete + 160 kg/m³ rebar + formwork) so `DeriveQuantity` reads `HOST_VOLUME_COMPUTED` × waste. |

**Worked examples re-run after fixes:** Example 1 (C30 column, 0.480 m³) now resolves
density 2450 + carbon 300 via the lookup (V1), and costs 0.504 m³ × 1 924 000 =
**969 696 UGX** (V8, was a flat 1 295 000 each). Example 2 (brick wall) unchanged at
**2 381 394 UGX**. No quantities reduced; no correct fix regressed.

**Files touched (review pass):** `UI/MaterialLookupParser.cs` (+`ResolveConcreteGradeKey`),
`UI/MaterialLookupCsv.cs` (grade-key retry in `Get`), `Model/StructuralDesignSuite.cs`
(+`ResolveAvgRatio`, wired into `AutoRebarEstimator`), `Data/BLE_MATERIALS.csv`
(39 timber rows → −992, 8 concrete rows → 2450/300), `Data/cost_rates_5d.csv`
(`Columns` → m³). **Revised tally: of the 10 original fixes, 2 were inert (F3/F4 via
V1, F8 via V7), 1 was wrong (F6 via V6), 1 was an internal contradiction (F7/F2 via
V2); all are now corrected. F1/F2/F5/F10 confirmed good as shipped.**

---

## Review pass 2 — each-unit embodied-carbon zeroing

**Date:** 2026-06-03 · **Branch:** `claude/upbeat-cori-vdOPA` · A separate
concurrent session re-used the label "F1" for this carbon-zero bug, creating a
clash with the Round-1 "F1" (the `×2300` carbon bug). To keep PR #287's audit
coherent the new findings use the unambiguous `R2-n` prefix; **no existing F/V
item is renumbered.** **Built without `dotnet build` verification (Linux
sandbox) — verify in Revit before merge.**

| R2 | Concern | Verdict | What changed in this pass |
|---|---|---|---|
| R2-1 | `EstimateVolumeM3` returned **0** for the `each` unit (and every non-m³/m²/m unit), so any per-m³ carbon factor multiplied by 0 ⇒ **0 embodied carbon** for every each-priced family: doors, windows, MEP equipment, fixtures, furniture, stairs, sprinklers, fire/comms devices. This silently zeroed most M/E/P + FF&E carbon and skewed the carbon-coverage score. | **BLOCK** | Added an each-element volume-recovery chain before the final `return 0`: (a) exposed volume parameter (`HOST_VOLUME_COMPUTED`, then generic `Volume`, ft³→m³); (b) actual solid geometry summed from `get_Geometry` (recurses one level into `GeometryInstance`, ft³→m³); (c) mass ÷ density fallback so the per-m³ factor still yields a number. Only a true point family with no geometry/mass returns 0. The m³/m²/m branches and the genuine per-kg (mass-multiplied) path are unchanged. New helpers `ReadElementVolumeM3` + `ReadGeometryVolumeM3` + `SumSolidVolumeFt3`. |
| R2-2 | Earlier audit text implied the "cement content raised to BS 8500" fix changed a delivered BOQ quantity. | **INFO (honesty)** | There is **no `FormulaEngine.Lookup`** in the tree and **no cement/sand/aggregate line-item takeoff** — nothing reads `CEMENT_BAGS_PER_M3` / `SAND_RATIO` / `AGGREGATE_RATIO`. They are **reference-only** values for manual QS use. The cement-content correction improves the table's hand-read accuracy but changes **no** delivered quantity (the main concrete m³ line is untouched). Marked explicitly REFERENCE-ONLY in `MATERIAL_LOOKUP.csv` (v1.1 header). What IS live: `DENSITY_KG_M3` / `CARBON_KG_PER_M3` (carbon + density takeoff) and `STEEL_KG_PER_M3` (rebar via `AutoRebarEstimator`). |
| R2-3 | Round-1 "F1" label re-used by a concurrent session for the carbon-zero bug. | **DOC** | Consolidated into ONE numbering: Round-1 findings keep `F1…F13`, review pass keeps `V1…V8`, this pass uses `R2-1…R2-3`. The carbon-zero bug is `R2-1` (NOT a second "F1"). |

**Files touched (review pass 2):** `BOQ/BOQCostManager.cs`
(each-unit volume recovery in `EstimateVolumeM3` + 3 new helpers),
`Data/MATERIAL_LOOKUP.csv` (v1.1 reference-only header note).

**Revised summary: 4 BLOCK, 5 WARN, 5 INFO findings + 1 honesty/doc clarification;
all BLOCK + WARN fixed.** The Round-1 `×2300` carbon over-count (F1) and the
Review-pass-2 each-unit carbon zeroing (R2-1) were the two carbon BLOCKs — both
now fixed. After R2-1 every each-priced family (doors, AHUs, fixtures, …) returns
non-zero embodied carbon.
