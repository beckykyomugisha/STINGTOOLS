# STINGTOOLS — One-Time Revit Smoke Test · `claude/pm-complete`

The consolidated branch holds: BOQ (master → measurement → round-3) · PM-1…8 · sustainability (SUS-1…7) · CA-1…5 (cost/carbon basis) · P0-7 (one canonical costing) · MAT-1…4 (building materials + slab systems) · RC-1…4 (ratio/cost integrity). All compile + headless-test verified (0 errors; Cost 90 · Boq 196 · Scheduling 38 · Sustainability 438 = 762 tests green). This is the runtime gate before merge to `main`.

**Test model needs:** priced elements across disciplines; ≥2 design options; a multi-storey shaft; a compound/cavity wall; block, brick, RC and (if possible) a hollow-pot / clay-pot and a maxspan/beam-and-block slab; a material with a per-element rate override; timber + masonry + steel + a non-ferrous (Al/Cu) material; and a small MS Project/P6 schedule export.

---

## 0 — Pre-flight (do first)
- [ ] Build `StingTools.dll` (Release) → load into Revit 2025; BOQ & Cost Manager opens.
- [ ] **Migration 1 (existing projects):** re-run **Set Contract Sum / Award** on any project frozen before CA-2 — `COST_CONTRACT_SUM_UGX` is now **net of VAT**.
- [ ] **Migration 2 (server, if using Planscape sync):** `dotnet ef migrations add BoqBaselineMarkup` + `database update` so the server persists markup.

## A — BOQ takeoff, totals & currency (CA-1)
- [ ] Refresh → grand total renders; panel KPI **equals** the Tender BOQ Contract Sum (VAT-inclusive).
- [ ] **A material-library / curated material cost prices at UGX magnitude — NOT ~3,700× inflated** (CA-1).
- [ ] **A category absent from `cost_rates_5d.csv` prices at UGX magnitude, not ~3,700× too low** (CA-1).
- [ ] Sized column/foundation prices to real m³; a compound wall shows per-material cost + carbon slices.
- [ ] Floor pierced by a shaft is deducted (or flagged); design options aren't double-counted.
- [ ] A stamped `CST_MODELED_TOTAL_UGX` equals its bill row — incl. an aggregated row **and an element with a rate override** (CA-5).
- [ ] Unpriced category triggers the at-risk export gate; a valid-rate zero-qty row also blocks/warns.
- [ ] Edit an element → bill auto-refreshes incrementally; 4D cash-flow reflects the new rate.
- [ ] IFC Qto: `Gross*` fields carry gross, `Net*` carry net (CA-5).

## B — Building materials & slab/wall systems (MAT-1…4)  ★ new
- [ ] **Hollow-pot / clay-pot slab measures NET concrete (~55–70% of gross), not solid** — rebar scales off the net volume too (MAT-1/4.1).
- [ ] **Maxspan / beam-and-block slab bills separately: in-situ topping (m³) + precast ribs (m or nr) + infill blocks (nr/m²) + mesh** — precast ribs are **NOT** in the in-situ concrete line (MAT-4.2).
- [ ] Ribbed / waffle slab concrete = topping + ribs (net of voids); a slab modelled with real rib geometry uses its actual solid volume (MAT-4.1).
- [ ] **Formwork for pot/maxspan slabs = rib/edge forms or props, not gross soffit area** (MAT-4.2).
- [ ] RC beam = section × net length (column overlap deducted); formwork = soffit + 2 sides (MAT-4.3).
- [ ] Walls distinguish block / brick / RC / cavity; **plaster measured × number of faces**; mortar derived (MAT-3).
- [ ] Mortar/plaster/screed quantities use the corrected ratios (mortar sand no longer ~20–30% short; screed + plaster mixes present) (MAT-2).
- [ ] A solid slab and a legacy-mode bill are **unchanged** (no regression).

## C — Ratio integrity / silent-DEFAULT (RC-1…3)  ★ new
- [ ] **A mis-formatted `BLE_BLOCK_SIZE_TXT` (`440X215`, `440×215`, `440 x 215`) resolves to 10/m² — NOT the DEFAULT 12.5** (RC-1 normalisation).
- [ ] **An unset ratio param does NOT silently use DEFAULT** — the row is confidence-lowered, appears in the at-risk rollup, and a "DEFAULT used" warning is logged (RC-1).
- [ ] A `1 : 3` mortar resolves to the 1:3 mix (not DEFAULT 1:6) (RC-1).
- [ ] **A material with `BLE_STRUCT_CONCRETE_GRADE_TXT=C40` but a generic material name carbons/densities as C40** (RC-2 grade-param-first).
- [ ] A curtain wall in Revit category "Walls" files under the curtain-wall section, not the plain wall section (RC-2).
- [ ] (If system rates implemented) an element re-typed via `ASS_SYSTEM_TYPE_TXT` routes to the system-specific rate (RC-2).
- [ ] **Large-model refresh is faster** and the cert→EVM→AFC chain builds the BOQ once (RC-3 memoisation) — no multi-minute stall on a big model.

## D — Carbon (EDGE / Uganda) & one convention (CA-3)
- [ ] **Timber model: EDGE dashboard and BOQ panel report the SAME fossil kgCO₂e/m²** (CA-3).
- [ ] Timber row shows fossil headline + separate negative biogenic line — identical on panel, pivot, IFC GWP.
- [ ] `CST_EMBODIED_CARBON_KG` is single-valued regardless of pass order; `BREEAMAssessment`/`LifecycleAssessment` match the BOQ/EDGE numbers (parallel LCA engine retired).
- [ ] Panel carbon RAG is kgCO₂e/m² intensity; B6 uses Uganda 0.05; COBie handover carbon comes from `CST_EMBODIED_CARBON_KG`.
- [ ] Per-material waste applies the same factors to cost and carbon.

## E — Carbon-as-cost / LCC (CA-4)
- [ ] `COST_CARBON_PRICE_UGX_PER_KG` = 0 → no carbon term in LCC (opt-in default).
- [ ] Set a carbon price → `LifecycleCostInclCarbonUGX` = capital + NPV maintenance + (price × carbon), sane, on panel/ICMS3.

## F — Cost lifecycle & one basis (CA-2)
- [ ] Payment certificate: VAT 18%; retention calculates; cumulative valuation survives a section rename.
- [ ] A cert at 100% complete reconciles up to the frozen Contract Sum on the net-of-VAT basis (CA-2).
- [ ] Set Contract Sum freezes (net of VAT); Final Account + AFC show the same contract sum, reconcile against certified-to-date.
- [ ] Retention Release writes a release entry; balance updates.
- [ ] Approve a variation → "Adjustments / Variations" SOV section on next cert + moves EVM BAC.
- [ ] EVM at project start (EV=0): EAC a sensible forecast (not 0); **CPI ≈ 1.0 when earned = actual on a like (net) basis** (CA-2).
- [ ] Panel inline EVM BAC = the `Evm_Calculate` BAC (both via `ContractSumResolver`).
- [ ] Fluctuations appear in both AFC and Final Account; `Cvr_Report`/`LossExpense_Value`/`CostToComplete_Lines`/`Commitments_Report`/dayworks all sane.
- [ ] NRM1 cost plan reconciles to the BOQ Contract Sum; QuickBooks IIF + Sage CSV export produce openable files.

## G — Scheduling (PM-4) & canonical 5D (P0-7)
- [ ] `Sched_Import` reads MSP/P6; predecessors parse; `Cpm` computes critical path + float; `ModelPercent` reflects model state.
- [ ] `SCurve` differs for front- vs back-loaded programmes (schedule-driven); EVM PV tracks it.
- [ ] `Export5DCostData` / COBie ReplacementCost read the canonical `CST_*` cost (one number, matches the bill) (P0-7).

## H — Integration, delivery & sustainability
- [ ] `Clash_SyncIssues`: clash run raises tracked issues into `issues.json` with SLA; `has_open_issues` sees them; bundle into a transmittal.
- [ ] `Midp_DriftReport` shows deliverable status + off-programme drift; KUT KPI dashboard shows computed numbers; `Risk_Raise`/`Report` attaches a risk.
- [ ] EDGE design-measures sheet + evidence pack; WBLCA A1-A3 report matches the BOQ carbon take-off; `Sustain_Report` HTML renders; EDGE 20/20/20 + ZeroCarbon gates evaluate.
- [ ] Server sync (if used): baseline carries `WorksValue` + `ContractSum` + markup, currency UGX — after Migration 2.

---

**On completion:** fix anything failing on `claude/pm-complete`; once all ticks pass in Revit (and both migrations done), that's the single merge-to-`main` for the entire stack.
