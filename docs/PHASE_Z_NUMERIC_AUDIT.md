# Phase Z — Deep Numeric / Formula / Cost Accuracy Audit

**Branch:** `audit/numerics-deep-review` · **Date:** 2026-05-31 · **Mode:** READ-ONLY (no source fixes; each fix is a separate follow-up branch with its own `dotnet build`).
**Method:** systematic sampling, not exhaustive. Every finding cites `file:line` + the public reference verified against (ICE database v3.0, CIBSE Guide A 2015 / Guide B, NRM2 2013, BS 7671 18th Ed, BS EN 806).

**Severity:** **P0** = silently wrong cost/carbon/sizing in *delivered* BOQs/RFQs (fix before next export). **P1** = wrong but <25% or path-limited (next sprint). **P2** = cosmetic / label / no math impact.

---

## Findings by category

### 1. Unit mismatch — mostly clean ✓

| # | Finding | File:line | Current | Expected | Impact | Sev |
|---|---|---|---|---|---|---|
|1.1| BOQ qty unit conversions | `BOQCostManager.cs:386,393,399` | ft²→m² `0.092903`, ft³→m³ `0.0283168`, ft→m `0.3048` | exact | units balance ✓ | — |
|1.2| FormulaEngine unit conversions | `FormulaEvaluatorCommand.cs:810-863` | `/0.3048`, `/0.09290304`, `/(0.3048³)`, L→m³ `/1000`, m→mm `×1000` | exact | ✓ | — |
|1.3| Carbon per-kg vs per-m³ | `BOQCostManager.cs:485-512` | `CarbonFactorResolver` branches kg-factor×mass vs m³-factor×volume | explicit | **the historical 1000× LCA bug is already fixed** ✓ | — |
|1.4| Linear-element carbon fallback cross-section | `BOQCostManager.cs:551` | `areaMm2 = 1000.0; // ~32 mm circular equiv` | 1000 mm² = Ø35.7 mm, not 32 | comment vs value mismatch; no math error | P2 |

**0 P0 / 0 P1 / 1 P2.** Dimensional analysis through BOQ + FormulaEngine balances.

### 2. Material constants vs published references

| # | Finding | File:line | Current | Expected (ref) | Impact | Sev |
|---|---|---|---|---|---|---|
|2.1| BLE per-row props are templated defaults | `BLE_MATERIALS.csv` (e.g. "STEEL CEILING TILE 0.6MM") | ρ=2300, k=1.3, C=400 | steel ρ=7850, k≈50 (ICE/CIBSE) | non-concrete rows carry a generic 2300/2400 + 150/400 block → wrong carbon/thermal/U-value | P1 |
|2.2| Fiberglass/glass-wool tile density | `BLE_MATERIALS.csv` "FIBERGLASS ACOUSTIC TILE 15MM" | ρ=2300 | glass/mineral wool 10–100 kg/m³ (CIBSE) | ~30× high; wrong mass/U-value | P1 |
|2.3| Cement plaster density | `BLE_MATERIALS.csv` "CEMENT PLASTER FINISH 12MM" | ρ=2400 | cement:sand render ≈1900–2100 | +15–25% mass | P2 |
|2.4| Embodied-carbon undercount: steel | `MEP_MATERIALS.csv` galv. steel duct | 2500 kgCO₂/m³ (≈0.32 kg/kg) | ICE steel 1.5–2.8 kg/kg → ~12,000–22,000/m³ | **~6× under-reported** | P1 |
|2.5| Embodied-carbon undercount: copper | `MEP_MATERIALS.csv` "COPPER PIPE TYPE L" | 3500 kgCO₂/m³ (≈0.39 kg/kg) | ICE copper 2.7–3.8 kg/kg → ~24–34k/m³ | **~7× under** | P1 |
|2.6| Embodied-carbon undercount: glass | `MEP_MATERIALS.csv` glass | 850 kgCO₂/m³ (≈0.34 kg/kg) | ICE glass 1.4–1.7 kg/kg → ~3500–4250/m³ | ~4× under | P1 |
|2.7| Softwood density = hardwood | `BLE_MATERIALS.csv` "SOFTWOOD SKIRTING" | ρ=720 | softwood ≈420–550 (CIBSE) | +30–70% mass; uses hardwood value | P2 |
|2.8| Timber biogenic carbon mixed with gross | `BLE_MATERIALS.csv` timber rows | C=−900 kgCO₂/m³ | ICE sawn softwood ≈−1.6 kg/kg *with sequestration* | −900 valid only if **all** materials use A1–A3-incl-biogenic; mixing −900 timber with gross concrete/steel makes project totals misleadingly low | P1 |

**Verified-correct (✓):** concrete ρ=2400 / k=1.4 (CIBSE dense 1.4–2.0); mineral wool ρ=100 / k=0.038 (CIBSE 0.035–0.040); MEP steel ρ=7850, copper ρ=8960/k=385, float glass ρ=2500 — densities right, only the **carbon** columns are low.
**~5 P1 / 2 P2.**

### 3. Cost-rate realism (`cost_rates_5d.csv`, 38 rows)

| # | Finding | File:line | Current | Expected | Impact | Sev |
|---|---|---|---|---|---|---|
|3.1| USD↔UGX internal consistency | `cost_rates_5d.csv` all rows | e.g. wall 85 USD × 3700 = 314,500 ≈ 315,000 UGX | consistent at ~3700 UGX/USD | ✓ no 10×/0.1× typos found | — |
|3.2| Flat single-rate per category | `cost_rates_5d.csv:11` AHU=$8,500 ea | one rate for all AHUs | real AHU $3k–$50k by size | crude — small/large priced identically | P2 |
|3.3| Sparse coverage | 38 rate rows total | most categories rely on default fallback | full rate book | many elements get fallback rate | P2 |

**0 P0 / 0 P1 / 2 P2.** No typo-class errors; rates are reasonable round placeholders for EA market, not size-calibrated.

### 4. Waste / bulking / takedown ratios

| # | Finding | File:line | Current | Expected (ref) | Impact | Sev |
|---|---|---|---|---|---|---|
|4.1| **Sand bulking — DRY value non-physical** | `MATERIAL_LOOKUP.csv:219-222` | DRY=1.15, DAMP=1.25, WET=1.10, DEFAULT=1.20 | dry sand does **not** bulk (≈1.00); bulking peaks *damp* (~1.25–1.30); saturated/wet collapses to ≈1.00–1.05 (CIRIA / IS 2386) | DRY over-orders 15%; WET treats inundated sand as +10% when it should be ≈0 | P1 |
|4.2| Brick cutting waste by bond | `MATERIAL_LOOKUP.csv:80-95` | 3–8% (stretcher 5, Flemish 8) | NRM2 5–10% | ✓ | — |
|4.3| Mortar ratios per bond / block | `:79-126` | 0.025–0.035 m³/m², bag ratios | within range | ✓ | — |
|4.4| Tile waste | `:142` mosaic 20% | high-waste small-format | NRM2 5–10% std, more for mosaic | ✓ | — |
|4.5| Rebar wastage absent | `MATERIAL_LOOKUP.csv` | only STEEL_KG_PER_M3 *ratios* (50–170), no cutting/lapping waste % | NRM2 rebar 5–7% | rebar tonnage may omit waste | P2 |
|4.6| Concrete "order +5%" not baked in | (not found) | no over-order factor for concrete | QS commonly +5% | minor under-order | P2 |

**1 P1 (sand) / 2 P2.**

### 5. Formula dependency graph

| # | Finding | File:line | Current | Expected | Impact | Sev |
|---|---|---|---|---|---|---|
|5.1| **Dependency cycles present** | `FORMULAS_WITH_DEPENDENCIES.csv` (278 rows) | static Kahn sort orders only **215/278**; **63 nodes in/downstream of cycles** | acyclic DAG | cycle nodes run **last with stale inputs** (`FormulaEvaluatorCommand.cs:530-539` logs "Formula cycle detected"; "results may be inaccurate") | P1 |
|5.2| Cycle nodes feed BOQ takeoff | same | cycle set includes `CST_S_CON_CEMENT_BAGS_NR`, `CST_S_CON_SAND_VOLUME_CU_M`, `CST_CALC_STEEL_KG`, `PLM_HED_M`, `HVC_PIPE_FLOWRATE_LPS`, `FLS_SFTY_DEMAND_LPS` | clean order | concrete/steel-takeoff + plumbing-head can compute against stale/zero inputs → wrong quantities | P1 |
|5.3| Hardcoded factors in formulas | e.g. `BLE_FINISH_PAINT_AREA_SQ_M = …×0.85` | 24 formulas carry literal 0.8–1.2 factors | project-overrideable params | paint/finish deductions not tunable per project | P2 |

Level distribution (acyclic part): `{0:130, 1:58, 2:29, 3:11, 4:1, 5:1}`.
**Note:** exact *elementary*-cycle count should be confirmed from the engine's runtime `StingLog` "Formula cycle detected" lines (needs Revit run); static analysis definitively shows the set is **not** acyclic (215 < 278). **2 P1 / 1 P2.**

### 6. BOQ aggregation (end-to-end trace of a concrete wall line)

Trace: element → `DeriveQuantity` (HOST_VOLUME ft³ × `0.0283168` → m³) → `× rate` (UGX/m³, `ResolveRate`) → `× (1 + WastePercent/100)` → line `TotalUGX = Qty×RateUGX` (`BOQModels.cs:67`) → section `Sum` → `GrandTotalUGX = Subtotal × (1 + Prelim% + Contingency% + Overhead%)` (`BOQModels.cs:162-163`). Each multiplication is dimensionally correct ✓.

| # | Finding | File:line | Current | Expected | Impact | Sev |
|---|---|---|---|---|---|---|
|6.1| **VAT excluded from the headline total** | `BOQModels.cs:162-163` vs `BOQProfessionalExportCommand.cs:1537` | `GrandTotalUGX` = `Subtotal×(1+prelim+conting+OH)` — **no VAT**; VAT only added in the *professional Word* export (`subTotal + vat`) | one consistent contract sum | standard XLSX, budget-variance (`:164`), dashboard & BCC show a total **18% short** of the Word export's contract sum | P1 |
|6.2| Confirm `VatPct` default | `BOQProfessionalExportCommand.cs:1537` (`m.VatPct`) | reads tender-config VatPct | Uganda VAT 18% | if VatPct defaults 0, even the Word export shows no VAT — verify | P1 |
|6.3| Waste only on TakeoffRule path | `BOQCostManager.cs:368-372` | `q *= 1+Waste%` applied **only** when a TakeoffRule matches + units align; **legacy fallback (`:380+`) applies 0% waste** | waste on all costed qty | elements routed through fallback are **under-quantified** | P1 |
|6.4| Contingency / Prelim / OH rollup | `BOQModels.cs:163` | additive % of subtotal | each % of net | ✓ | — |
|6.5| Currency | `BOQCostManager.cs:206-208,272-275` | UGX primary; USD = UGX/3700; per-currency rate resolution | no silent USD/UGX mixing | ✓ (FX defaults configurable via TagConfig) | — |
|6.6| Provisional-sum reconciliation uses `Abs` | `BOQCostManager.cs:1409` | `diff = Math.Abs(mod.TotalUGX − psTotal)` | signed (credit vs overrun) | magnitude only — may not distinguish unused-PS credit-back from overrun | P2 |

**3 P1 / 1 P2.**

### 7. Cross-file drift (shared materials)

| # | Material | BLE | MEP | MATERIAL_LOOKUP | Impact | Sev |
|---|---|---|---|---|---|---|
|7.1| Concrete carbon | 150 kgCO₂/m³ | — | 345 (C30) | **2.3× mismatch** — which source wins decides the answer | P1 |
|7.2| Steel | ρ=2300/C=400 (ceiling-tile row, wrong) | ρ=7850/C=2500 | — | density drift from BLE templated default (see 2.1) | P1 |
|7.3| Glass | ρ=2300/C=400 (fiberglass tile) | ρ=2500/C=850 | — | density + carbon differ | P1 |

**3 shared-material mismatches** (concrete-carbon, steel, glass). Copper present only in MEP. **Tie-in to §2 data-quality.** Root: `MATERIAL_LOOKUP` carries carbon only for concrete grades; metals/glass have **no** LOOKUP carbon → resolver falls back to the under-stated BLE/MEP columns.

### 8. Standards conformance (CIBSE / BS 7671)

| # | Finding | File:line | Current | Expected | Sev |
|---|---|---|---|---|---|
|8.1| CIBSE duct/pipe velocities | `StandardsEngine.cs:34-44` | supply main 3–10, branch 2–6, CHW/HW 0.5–3, DCW/DHW 0.5–2, condensate 0.3–1.5 m/s | CIBSE Guide B | ✓ (supply-main max 10 slightly permissive vs ≤7.5–9 noise guidance) | P2 |
|8.2| BS 7671 circuit reqs | `StandardsEngine.cs:69-78` | ring 32A/2.5mm² ✓, shower 45A/10mm² ✓, radial 20A/2.5 ✓, EV 32A/6 ✓ | BS 7671 18th Ed | ✓ (cooker 45A/6mm² borderline — OK clipped, marginal in insulation) | P2 |

**0 P0 / 0 P1 / 2 P2.** Standards tables are sound.

---

## Top-3 — fix these first (they invoice/spec/report wrong today)

1. **VAT is missing from the headline `GrandTotalUGX`** (`BOQModels.cs:162-163`) — only the professional Word export adds it (`BOQProfessionalExportCommand.cs:1537`). Every standard XLSX export, budget-variance, dashboard and BCC total is **~18% short** of the true contract sum. A client reading those surfaces is quoted/invoiced low. *(P1; verify `VatPct` default = 18 while fixing.)*
2. **Sand bulking DRY = 1.15 is non-physical** (`MATERIAL_LOOKUP.csv:219`) — dry sand does not bulk (≈1.00); the value over-orders dry sand by 15% and WET=1.10 mis-models saturated sand. Direct material-quantity / cost error on every sand line. *(P1)*
3. **Embodied-carbon undercount for metals & glass (~4–9× low)** + **concrete-carbon cross-file drift (BLE 150 vs LOOKUP 345 kgCO₂/m³)** (§2.4–2.6, §7.1) — any delivered carbon report or RIBA-stage carbon number is materially wrong/low. *(P1)*

**Runners-up:** waste% skipped on the BOQ legacy-fallback path (`BOQCostManager.cs:380+`, §6.3) → under-measured quantities; and 63/278 formulas in dependency cycles incl. concrete-takeoff (§5.1-5.2) → stale-input math.

---

## Summary stats

| Category | P0 | P1 | P2 |
|---|---|---|---|
| 1 Unit mismatch | 0 | 0 | 1 |
| 2 Material constants | 0 | 5 | 2 |
| 3 Cost realism | 0 | 0 | 2 |
| 4 Waste/bulking | 0 | 1 | 2 |
| 5 Formula graph | 0 | 2 | 1 |
| 6 BOQ aggregation | 0 | 3 | 1 |
| 7 Cross-file drift | 0 | 3 | 0 |
| 8 Standards | 0 | 0 | 2 |
| **Total** | **0** | **14** | **11** |

- **Formula dependency graph has CYCLES: YES — 63 of 278 nodes unsortable** by Kahn's algorithm (static analysis; engine runs them last with stale inputs). Levels 0–5.
- **Cross-file drift on shared materials: 3 mismatches** (concrete-carbon 2.3×, steel density, glass density+carbon).
- No P0s: no single value is *both* wrong *and* on an unconditional delivered path — but the Top-3 P1s reach delivered BOQs/carbon reports via common export paths and should be treated as fix-next.

**Fixes are intentionally NOT applied** (read-only pass). Each becomes its own branch + `dotnet build` + numeric regression check — numeric changes are exactly the class that must be reviewed PR-by-PR, not shotgunned.
