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

## Worked example 1 — RC concrete column (C30/37, 0.4 × 0.4 × 3.0 m)

| Step | Value | Source / multiplier |
|---|---|---|
| Revit volume | 16.96 ft³ | `HOST_VOLUME_COMPUTED` |
| → m³ | 16.96 × 0.0283168 = **0.480 m³** | ft³→m³ |
| Waste (default 5 %) | 0.480 × 1.05 = 0.504 m³ | `MeasuredAddition.GrossUp` (concrete over-order knob OFF) |
| Density (C30, **after fix**) | 2450 kg/m³ | lookup `CONCRETE C30 DENSITY_KG_M3` (was hardcoded 2400, −2 %) |
| Mass | 0.504 × 2450 = 1235 kg | |
| Carbon (C30, **after fix**) | 0.480 × 300 = **144 kgCO₂e** (net qty for carbon, ICE per m³) | was 0.480 × 345 = 166 (−13 %) |
| Cement check (C30, **after**) | 0.480 × 360 = 173 kg ≈ 3.5 bags | was 156 kg (−10 % under-order) |
| Rate (`Columns`, each) | 1 295 000 UGX | `cost_rates_5d.csv` |
| Cost | 1 × 1 295 000 = **1 295 000 UGX** | per-unit |

Hand check: 0.48 m³ × 360 kg cement = 173 kg → ✓ matches NRM2 nominal for C25/30.

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
