# Phase Z-22 Stage 1 — Formula Dependency-Cycle Audit (read-only)

**Scope:** map the dependency-cycle set in
`StingTools/Data/FORMULAS_WITH_DEPENDENCIES.csv` (278 deduped formulas).
No engine or data changes — this pass only diagnoses. Companion
visualisation: [`formula_cycles.dot`](formula_cycles.dot).

---

## TL;DR

| Metric | Value |
|---|---|
| Formula nodes (deduped by `Parameter_Name`, first-wins) | **278** |
| Elementary cycles | **19 — all 1-node self-loops** |
| Multi-node elementary cycles | **0** |
| Genuine fixed-point cycles (the "hard core") | **0** |
| Nodes the audit counted "in/downstream of cycles" | **63** = 19 self-refs + 44 non-cyclic downstream |
| Cycles feeding a **delivered BOQ quantity** | **1** (`BLE_STRUCT_CONCRETE_GRADE_TXT` → 17 `CST_*` take-off nodes) |
| Quick-win cycles (spurious / ordering — no engine math) | **19 (all of them)** |

**Bottom line:** there are **no real circular computations**. Every
one of the 63 flagged nodes traces to a **spurious self-reference**
data artifact in the `Input_Parameters` column. Removing the 19 self
entries makes the graph fully acyclic (`Kahn` orders 278/278). There
is **nothing to solve with fixed-point iteration.**

---

## 1. Engine detector vs the numeric audit — reconciliation (key finding)

The runtime evaluator (`FormulaEvaluatorCommand.cs:457–541`) builds its
dependency graph by **tokenising each `Revit_Formula` expression**,
keeping only tokens that are themselves formula names, and **explicitly
skipping self-tokens** (`:485 if (token == ParameterName) continue;`).
Replicating that algorithm exactly:

> **Kahn orders all 278/278 — the engine finds ZERO cycles and never
> logs "Formula cycle detected" on the shipped data.**

The numeric audit's "215/278 sorted, 63 in cycles" is reproduced only by
a **different** graph: edges taken from the **`Input_Parameters` column**
**with self-loops kept**. The 63 are precisely the 19 self-referencing
rows plus their 44 transitive dependents (which a naïve Kahn cannot
release because their self-looping ancestor never reaches in-degree 0).

| Graph | Self-loops | Result |
|---|---|---|
| Engine (expression tokens) | skipped | **278/278 sorted — 0 cycles** |
| `Input_Parameters` column | **kept** | 215/278 — **63 unsorted** (= audit) |
| `Input_Parameters` column | removed | **278/278 sorted — 0 cycles** |

**Implication:** the audit's "cycle nodes run last with stale inputs →
non-deterministic BOQ" risk does **not** fire through the engine's own
ordering (it sorts cleanly). The real defect is **data quality** in the
`Input_Parameters` metadata — and, for 18 of the 19, a **mis-keyed
validation formula** (see §3).

---

## 2. The 19 elementary cycles (all self-loops `A → A`)

Grouped by domain. **Fix type is "spurious self-reference" for all 19.**
"BOQ?" = does the node feed a delivered BOQ quantity (directly or via
downstream `CST_*` take-off)?

| # | Node (cycle chain `A→A`) | Domain | Downstream nodes | BOQ? | Notes |
|---|---|---|---|:---:|---|
| 1 | `BLE_STRUCT_CONCRETE_GRADE_TXT` | Concrete take-off | **20 (17 `CST_*`)** | **YES** | Only BOQ-critical one. Lookup-table text param; self entry is pure metadata noise (expr does not self-mention). Root of the entire concrete/steel/masonry take-off chain. |
| 2 | `BLE_DOOR_WIDTH_MM` | Architectural | 4 | no | Warning formula `if(SELF<900," [!DOOR<900mm]","")` keyed onto the width param. |
| 3 | `BLE_STAIR_GOING_MM` | Architectural | 2 | no | `if(or(SELF<250,SELF>400)," [!GOING]","")` |
| 4 | `BLE_STAIR_RISE_MM` | Architectural | 2 | no | `if(or(SELF<150,SELF>190)," [!RISE]","")` |
| 5 | `BLE_STAIR_WIDTH_MM` | Architectural | 0 | no | `if(SELF<1000," [!WIDTH<1000]","")` |
| 6 | `BLE_WINDOW_U_VALUE_W_M_2K_NR` | Thermal | 0 | no | `if(SELF>3.00," [!U>3.00]","")` |
| 7 | `BLE_WINDOW_SOLAR_HEAT_GAIN_COEFFICIENT_NR` | Thermal | 0 | no | `if(SELF>0.40," [!SHGC>0.40]","")` |
| 8 | `ASS_DESIGN_LTG_LVL_LUX_NR` | Lighting | 0 | no | `if(SELF<300," [!LUX<300]","")` |
| 9 | `ELC_CDT_CBL_FILL_PCT` | Electrical | 0 | no | `if(SELF>40," [!FILL>40%]","")` |
| 10 | `ELC_CTR_FILL_PCT` | Electrical | 0 | no | `if(SELF>45," [!FILL>45%]","")` |
| 11 | `ELC_PNL_CONNECTED_LOAD_KW` | Electrical | 1 | no | `if(SELF>ELC_PNL_RATED_KW*0.8," [!OVERLOADED]","")` |
| 12 | `ELC_PNL_SHORT_CIRCUIT_KA` | Electrical | 1 | no | `if(SELF<6," [!SC<6kA]","")` |
| 13 | `HVC_VEL_MPS` | HVAC | 6 | no | `if(SELF>6.0," [!VEL>6.0]","")` |
| 14 | `HVC_DCT_TERMINAL_SOUND_DB_NR` | HVAC | 0 | no | `if(SELF>35," [!NOISE>35dB]","")` |
| 15 | `PLM_VEL_MPS` | Plumbing | 5 | no | `if(SELF>2.0," [!VEL>2.0]","")` |
| 16 | `PLM_PSR_KPA` | Plumbing | 6 | no | `if(or(SELF<150,SELF>600)," [!PSR]","")` |
| 17 | `FLS_PROT_FLS_RESISTANCE_RATING_MINUTES_MIN` | Fire | 1 | no | `if(SELF<30," [!FRR<30]","")` |
| 18 | `FLS_SFTY_TEMP_RATING_C` | Fire | 0 | no | `if(or(SELF<57,SELF>79)," [!TEMP]","")` |
| 19 | `PER_ACOUSTICS_BACKGROUND_NOISE_DB` | Acoustics | 0 | no | `if(SELF>40," [!NOISE>40dB]","")` |

Self-ref domain spread: BLE 7 · ELC 4 · HVAC 2 · PLM 2 · FIRE 2 · ASS 1 · PER 1.

### Fix-type breakdown

| Fix type | Count | Which |
|---|---|---|
| **Spurious self-reference** | **19** | all of the above |
| Parameter elimination | 0 | — |
| **Fixed-point iteration** | **0** | — *(no genuine circular maths anywhere)* |
| Pure ordering bug | 0 (cyclic); 44 *downstream* nodes are non-cyclic, only mis-counted | — |

---

## 3. Two distinct defects behind the 19

**(a) 18 mis-keyed validation/warning formulas.** Rows 2–19 follow one
template: `if(SELF <comparison> threshold, " [!WARN]", "")`. The formula
produces a **warning-suffix string** but is stored under the **value
parameter it is checking** (e.g. a formula returning `" [!VEL>2.0]"` is
assigned to `PLM_VEL_MPS`, a velocity number). Each also lists
`[SELF, TAG_WARN_VISIBLE_BOOL, TAG_WARN_SEVERITY_FILTER_TXT]` as its
`Input_Parameters` — an auto-generated artifact.
- Topologically harmless at runtime (engine skips the self-edge).
- **But a real correctness/type concern:** a numeric param whose own
  formula reads its value and returns a string. Stage 2 should retarget
  these to a dedicated `*_WARN_TXT` annotation param rather than
  overwriting the value, and drop `SELF` from `Input_Parameters`.

**(b) 1 metadata-only self-ref — `BLE_STRUCT_CONCRETE_GRADE_TXT`.** Its
expression (`(text comparison via lookup table)`) does **not** mention
itself; only the `Input_Parameters` column wrongly lists it. Pure
ordering/metadata noise. **It is the only self-ref that feeds delivered
BOQ** — it is the root of the concrete take-off, reaching 17 `CST_*`
nodes (cement bags, sand, aggregate, water, steel, masonry & concrete
cost totals): `CST_S_CON_CEMENT_BAGS_NR`, `CST_S_CON_SAND_VOLUME_CU_M`,
`CST_CALC_STEEL_KG`, `CST_TOTAL_MATERIAL_COST`, … (full list in the
`.dot`). The 6 nodes the numeric audit named
(`CST_S_CON_CEMENT_BAGS_NR`, `CST_S_CON_SAND_VOLUME_CU_M`,
`CST_CALC_STEEL_KG`, `PLM_HED_M`, `HVC_PIPE_FLOWRATE_LPS`,
`FLS_SFTY_DEMAND_LPS`) are all **downstream dependents**, not self-refs.

---

## 4. Graphviz visualisation

[`docs/formula_cycles.dot`](formula_cycles.dot) renders the cyclic
subgraph only: the 19 red self-loops, their downstream edges, and the
17 `CST_*` BOQ-feeding nodes highlighted in orange
(`BLE_STRUCT_CONCRETE_GRADE_TXT` in bold red).

```
dot -Tsvg docs/formula_cycles.dot -o formula_cycles.svg
dot -Tpng docs/formula_cycles.dot -o formula_cycles.png
```

---

## 5. Recommended resolution order (Stage 2+)

1. **`BLE_STRUCT_CONCRETE_GRADE_TXT` first** — the only BOQ-critical
   self-ref. Remove the spurious `SELF` entry from `Input_Parameters`
   (it is a lookup-keyed text param, not computed from itself). One-line
   data fix; unblocks the whole concrete/steel/masonry take-off chain in
   any naïve consumer of the metadata. **No runtime number change** —
   the engine already orders this correctly (it ignores the column).
2. **Batch-fix the 18 validation formulas** — remove `SELF` from each
   `Input_Parameters`, and decide the warning convention: retarget the
   `if(SELF…)` suffix to a dedicated `*_WARN_TXT` param so a numeric
   value param is never overwritten with a string. (This is the only
   item with a *behavioural* question; topo-ordering is already fine.)
3. **No fixed-point work.** There are zero genuine circular
   dependencies — no iterate-to-converge solver is needed for any of the
   63 flagged nodes.

**HARD RULE compliance:** read-only audit. No edits to
`FORMULAS_WITH_DEPENDENCIES.csv` or `FormulaEvaluatorCommand.cs`.
Engine detector replicated exactly and cross-checked against an
independent Tarjan SCC + Kahn implementation (both agree: 0 cycles with
self-loops removed; 19 self-loops are the entire cyclic set).
