# StingTools — EDGE / LEED Sustainability Module

**Implementation prompt + design spec**
**Date:** 2026-06-26
**Driver project:** WBG Country Office, Bangui, Central African Republic (RFP 26-0404) — target **EDGE Advanced**, LEED v5 alongside.
**Status:** Approved design. Build Phase 1; scaffold Phase 2 (LEED) behind a flag.

---

## 0. How to use this document

This is a self-contained brief for an implementing agent. It carries the approved
architecture, the JSON schemas, the calculation methods with formulas, the exact
files to create, the Revit-API wiring, and the acceptance criteria. Build to it.
The pilot building (Bangui office) is **just one row of data** — every function
must generalise to other countries, climates, building types, and certifications.

**Non-negotiable design rules** (a violation = a bug):
1. **Two material metric tracks, never one.** Embodied **carbon** (kgCO₂e, EN 15978)
   and embodied **energy** (MJ, CED) are different metrics, datasets, and
   certifications. Never let one masquerade as the other on screen.
2. **Baselines key on climate-zone, never country.** Country is the first lookup key
   but the fallback chain proxies on climate zone, not "nearest country." Every
   fallback hop is written to a visible **proxy log**. No baseline number is ever
   invented in code.
3. **Certifications are data, not code.** No `if (scheme == "EDGE")` ladder. EDGE,
   LEED v5, and future schemes are JSON gate-lists pointing at named metric providers.
4. **Annual ≠ peak.** `BlockLoadEngine` (peak Watts) is for plant sizing only. The
   energy gate uses a new monthly annual estimator. Never reuse peak W as annual kWh.
5. **Every conversion constant is data.** PV yield kWh/kWp, grid/diesel carbon factor,
   plant COP/SEER, water usage frequency — all per-project/per-location registry
   values with a climate-derived default. The climate registry is the single source
   of locational truth (energy, water, PV, carbon).
6. **EDGE owns the certified number.** STING shows an *indicative* estimate beside the
   EDGE-app official number; it never claims to certify. Label estimates "indicative".

**Build caveat (state in the commit + CHANGELOG):** this repo builds in a Linux
sandbox without the Revit API, so the module ships **without `dotnet build`
verification**. Every Revit API call must use a documented signature; mark uncertain
calls with `// TODO-VERIFY-API`. Verify in Revit 2025/2026/2027 before merge.

---

## 1. Source requirements (client, verbatim anchors)

RFP 26-0404 Technical Proposal — "Environmental Certification Experience (5 pts)":
"aiming to get specific green building certification, LEED and or EDGE Advanced …
additional optimum sustainability measures applicable within the local context …
for which a life cycle cost benefit analysis will be required during the project
development stages."

Annex B SoW deliverables: "Complete developed Environmental LEED and or EDGE Advanced
certification." / "All sustainability goals, including LEED and EDGE certification
targets, must be identified and incorporated into the Cost/Budget Estimate within the
Design Development Submittal."

Reading: contractual target = **EDGE Advanced**, LEED as "and/or". Sustainability is
scored, must be **costed at Design Development**, and must carry a **life-cycle cost
benefit analysis** → the tool feeds cost/LCC, not just % gates.

Pilot building: G+4 office, ~2,550 m² office space, Bangui CAR; hot tropical,
cooling-dominated, weak/diesel grid. Occupancy is still being refined → **occupancy
is a parameter, not a constant.** Named systems in scope: high-performance HVAC +
BMS, solar PV, rainwater harvesting, grey-water reuse, black-water treatment, standby
generators, UPS, laminated solar-control glazing.

**Certification rules to encode:**
- EDGE Certified = ≥20% predicted savings in **each** of energy, water, embodied
  energy in materials vs a standard local baseline.
- **EDGE Advanced = ≥40% energy + ≥20% water + ≥20% materials** (default target).
- EDGE materials metric = embodied **energy** %, computed by the EDGE app from
  material selections — **not** our kgCO₂e number.
- LEED v5 (mandatory for registrations from 1 Jul 2026): mandatory prerequisite
  *Quantify & Assess Embodied Carbon* (WBLCA, cradle-to-gate A1–A3 GWP, name three
  hotspots); MR credits need product-specific Type III EPDs (ISO 14025 / EN 15804);
  *Reduce Embodied Carbon* credit scores kgCO₂e reduction (steps to ~40% → up to 6 pts).

---

## 2. Locked decisions (do not relitigate)

| # | Decision | Consequence |
|---|---|---|
| D1 | **Hybrid** engine role | Build EDGE-aligned simplified annual estimators in-Revit for live indicative %, AND export model quantities to the official EDGE app; dashboard shows "STING estimate vs EDGE official" side by side. |
| D2 | **EDGE-first, LEED-ready layer** | Full EDGE three-gate dashboard + LCC now. Add `SUS_EPD_REF_TXT` param + dual-metric materials now. Defer `Sustain_LeedScorecard` + WBLCA-prerequisite report to Phase 2 behind a flag, switched on when client/PM confirms LEED is contractual. |
| D3 | **True dockable pane** | New 5th `IDockablePaneProvider` (Main, Electrical, Plumbing, HVAC, **Sustainability**), built on the `StingPlumbingPanel` template (Provider + CommandHandler + XAML + stable PaneGuid). |
| D4 | Region = **CAR (Bangui)** for the pilot | A single *selected* data row, not a baked-in constant. The engine is location-agnostic; CAR is chosen in `project_setup.json` (§2.5), not authored in code. |
| D5 | Target = **EDGE Advanced** | A *selected* level. Gate thresholds come from the scheme's `levels` map (Certified 20/20/20 vs Advanced 40/20/20 vs Zero Carbon); the user picks the level. |

---

## 2.5 Project options surface — **zero hardcoding** (cross-cutting requirement)

Nothing that defines a project may be a literal in engine code. CAR, "office",
"Advanced", any COP, any carbon factor, any usage frequency — all are **user
selections or registry rows**. The pilot is one configuration; a different project is
a different configuration, with **no recompile**.

**`Sustain_ProjectSetup`** command + a **SETUP tab** in the panel let the user choose
every project-defining value, each from an extensible catalogue (never a fixed list):

| Option | Source / picker | Notes |
|---|---|---|
| Certification scheme(s) | `GreenSchemeRegistry` (multi-select: EDGE, LEED v5, future BREEAM…) | Runs all selected schemes; dashboard shows each. |
| Target level | the chosen scheme's `levels` map | EDGE Certified/Advanced/Zero Carbon; LEED band. |
| Country + climate zone | ASHRAE 169 / Köppen catalogue; zone **auto-suggested** from the project's climate site / lat-long, **user-overridable** | Drives baseline resolution (§5). |
| Building use(s) | building-use catalogue; **single or per-zone mixed-use** | Selects water profile + internal-gain profile per zone. |
| Occupancy | parameter — total or per-zone; density default by use, **overridable** | Drives water + part of energy. |
| Plant efficiency | COP/SEER per system, user-entered with climate default | The efficiency story of the energy gate. |
| Supply | mode (grid_tied/off_grid/hybrid) + PV kWp + grid/diesel carbon factors + diesel fraction | §7 supply layer; PV optional. |
| Factor datasets | carbon + embodied-energy source order (EPD→EC3→ICE→Ecoinvent) per project/region | §9 resolver. |
| Units | SI / IP toggle (kWh/m² ↔ kBtu/ft²; L ↔ gal) | LEED IP support; default SI. |

All persisted to `_BIM_COORD/sustainability/project_setup.json`. Every engine reads its
inputs from this config or from a user-extensible registry — **never a constant.**

**Flexibility guarantees (each is testable):**
1. No country / climate zone / building use / scheme / level / conversion constant
   appears as a literal in engine code.
2. Every catalogue (schemes, baselines, building uses, water profiles, climate,
   factor sources) is extensible by dropping rows into the matching `_BIM_COORD/`
   override file — adding a row needs no code change.
3. Removing the CAR / office / Advanced rows changes **no code** — they are ordinary
   catalogue rows + selected config values.
4. **Mixed-use buildings** are first-class: building use is resolved **per zone**;
   the building-level rollup is area-weighted (energy, materials) and
   occupancy-weighted (water). A ground-floor-retail + offices-above tower works
   without special-casing.
5. Sensible defaults are offered (so a user isn't forced to fill every field) but
   every default is **overridable** and sourced from a registry, not hardcoded.

---

## 3. Architecture — 4 generic engines, 3 data layers, 1 locational truth

```
StingTools/Commands/Sustainability/        ← IExternalCommands (tag-dispatched)
StingTools/Core/Sustainability/            ← engines (read data, embed nothing)
  ClimateMonthlyRegistry.cs                extends climate data to monthly means + GHI
  GreenSchemeRegistry.cs                   loads STING_GREEN_SCHEMES.json
  GreenBaselineRegistry.cs                 loads STING_GREEN_BASELINES.json + proxy resolver
  WaterUsageProfileRegistry.cs             loads STING_WATER_USAGE_PROFILES.json
  GreenMeasureRegistry.cs                  loads STING_GREEN_MEASURES.json (cost handles)
  IMetricProvider.cs                       interface + MetricProviderRegistry
  AnnualEnergyEstimator.cs                 monthly quasi-steady kWh/m²·yr  (NEW MATH)
  SupplyAndGenerationLayer.cs              PV + grid/diesel carbon, between demand & gates
  AnnualWaterEstimator.cs                  L/person·day  (NEW MATH)
  MaterialsRollup.cs                       dual-metric: kgCO₂e/m² AND MJ/m²
  SchemeEvaluator.cs                       walks scheme gates → result
  SustainabilityResult.cs                  POCOs (gate results, intensities, proxy log)
  EdgeKpiSnapshot.cs                       mirrors KutKpiSnapshot
StingTools/Data/
  STING_GREEN_SCHEMES.json                 EDGE + LEED v5 gate definitions
  STING_GREEN_BASELINES.json               climate-zone-keyed baselines + provenance
  STING_WATER_USAGE_PROFILES.json          per building-use fixture-use frequency
  STING_GREEN_MEASURES.json                measures + cost handles for LCC
StingTools/UI/Sustainability/
  StingSustainabilityPanel.xaml(.cs)       tiles + proxy-log + drill-down
  StingSustainabilityPanelProvider.cs      IDockablePaneProvider (new PaneGuid)
  StingSustainabilityCommandHandler.cs     IExternalEventHandler, tag dispatch
```

**Climate registry = single source of locational truth.** Extend the existing
41-city `ClimateRegistry` / `STING_CLIMATE_DATA.json` with, per site: 12 monthly mean
dry-bulb °C, 12 monthly mean wet-bulb (or RH) °C, 12 monthly global horizontal
irradiance (GHI, kWh/m²·day), annual GHI, and a grid carbon factor default. Energy,
water, PV yield, and carbon all read from here so a project's location is set once.
Add monthly columns additively; sites lacking them fall back to design-day values
with a logged warning.

---

## 4. Data layer 1 — `STING_GREEN_SCHEMES.json` (certification-agnostic)

A scheme = ordered gates, each pointing at a registered `IMetricProvider` plus an
operator/threshold (and, for points-based schemes, a step function + aggregation).
The evaluator never names a scheme.

```jsonc
{
  "schema": "sting.green.schemes/v1",
  "schemes": [
    {
      "id": "EDGE", "version": "3.x", "level": "Advanced",
      "aggregation": "all_required",            // EDGE = AND of required gates
      "levels": {                                // configurable thresholds per level
        "Certified": { "energy": 20, "water": 20, "materials": 20 },
        "Advanced":  { "energy": 40, "water": 20, "materials": 20 }
      },
      "gates": [
        { "id": "energy",    "metric": "energy_savings_pct",          "provider": "AnnualEnergyEstimator", "operator": ">=", "required": true, "unit": "%" },
        { "id": "water",     "metric": "water_savings_pct",           "provider": "AnnualWaterEstimator",  "operator": ">=", "required": true, "unit": "%" },
        { "id": "materials", "metric": "embodied_energy_savings_pct", "provider": "MaterialsRollup",       "operator": ">=", "required": true, "unit": "%", "delegated": "EDGE_APP" }
      ]
    },
    {
      "id": "LEED", "version": "5", "aggregation": "pointSum",
      "certificationBands": { "Certified": 40, "Silver": 50, "Gold": 60, "Platinum": 80 },
      "gates": [
        { "id": "ec_prereq", "metric": "wblca_completed", "provider": "MaterialsRollup", "operator": "==", "threshold": true, "required": true },
        { "id": "ec_reduce", "metric": "gwp_reduction_pct", "provider": "MaterialsRollup",
          "points": [ { "pct": 10, "pts": 1 }, { "pct": 20, "pts": 3 }, { "pct": 40, "pts": 6 } ] }
      ]
    }
  ]
}
```

**Code shape:** `interface IMetricProvider { MetricResult Evaluate(SchemeContext ctx); }`
where `MetricResult` carries the named metric values it can supply (a provider may
supply several, e.g. MaterialsRollup supplies `embodied_energy_savings_pct`,
`gwp_reduction_pct`, `wblca_completed`). `MetricProviderRegistry` maps the `provider`
string → instance. `SchemeEvaluator.Evaluate(scheme, ctx)` walks gates, pulls the
named metric, applies operator/threshold or the points step function, then
aggregates (`all_required` → AND; `pointSum` → Σ points → band). Adding BREEAM /
Green Star later = new scheme JSON + reuse providers, **zero engine changes.**
`delegated: "EDGE_APP"` marks a gate whose authoritative number comes from the EDGE
app — the dashboard shows STING-indicative + an entry field for the official value.

Per-project override: `_BIM_COORD/green_schemes.json` merged by `id`.

---

## 5. Data layer 2 — `STING_GREEN_BASELINES.json` (climate-zone proxy + provenance)

Store method parameters and proxy keys, **never invented absolute numbers**. Sources,
in legitimacy order: EDGE published defaults → ASHRAE 90.1-2022 / 62.1 → CIBSE Guide F
/ TM54. Resolution proxies on **climate zone, not country.**

```jsonc
{
  "schema": "sting.green.baselines/v1",
  "climateZoneSystem": "ASHRAE_169_2021",
  "resolutionOrder": [
    "country+climateZone+buildingUse",
    "climateZone+buildingUse",
    "buildingUse",
    "global"
  ],
  "proxyLog": true,
  "baselines": [
    {
      "key": { "country": "*", "climateZone": "0A", "buildingUse": "office" },
      "source": "ASHRAE_90.1_2022 + EDGE_defaults",
      "provenance": "indicative",
      "energy": {
        "method": "endUseIntensity",
        "endUses": {
          "cooling":  { "eui_kwh_m2_yr": 95 },
          "fans":     { "eui_kwh_m2_yr": 22 },
          "lighting": { "lpd_w_m2": 9.0 },
          "equipment":{ "epd_w_m2": 12 },
          "dhw":      { "eui_kwh_m2_yr": 4 }
        },
        "baselineSystemCOP": { "cooling": 2.8 }
      },
      "water": {
        "fixtureBaselines": { "wc_lpf": 6.0, "urinal_lpf": 4.0, "basin_tap_lpm": 8.0, "shower_lpm": 10.0, "kitchen_tap_lpm": 8.0 }
      },
      "materials": { "embodiedEnergyBaseline_mj_m2": null, "note": "EDGE app owns the certified materials %; STING tracks selections + indicative MJ" }
    }
  ]
}
```

`GreenBaselineRegistry.Resolve(country, climateZone, buildingUse)` returns the matched
baseline **plus a `ResolutionPath`** (which key matched, which hops were skipped). The
panel renders the path: e.g. "no CAF baseline → fell back to climate-zone 0A office,
source ASHRAE 90.1 — indicative." `provenance` is never "certified". `null`
embodied-energy baseline means "must round-trip to the EDGE app."

The pilot's CAR / zone-0A numbers are **populated as data** (from the EDGE app /
ASHRAE), not authored in code. If CAR is absent from the EDGE app, agree the nearest
hot-humid (0A/1A) proxy and capture the official baseline into this file — that is a
data task, not a code task.

---

## 6. Data layer 3 — water usage profiles + measures

`STING_WATER_USAGE_PROFILES.json` — keyed by building use; engine identical across
types, building type only selects the profile. Sources: EDGE methodology defaults,
CIBSE Guide G, WELL.

```jsonc
{
  "schema": "sting.water.usage/v1",
  "profiles": [
    {
      "buildingUse": "office", "operatingDaysPerYear": 250,
      "fixtureUse_per_person_day": {
        "wc": { "uses": 3 },
        "urinal": { "uses": 1 },
        "basin_tap": { "uses": 4, "min_per_use": 0.25 },
        "shower": { "frac_people": 0.05, "min_per_use": 5 },
        "kitchen_tap": { "min_per_person_day": 0.3 }
      }
    }
    // residential / healthcare / retail / hotel each add their own row
  ]
}
```

`STING_GREEN_MEASURES.json` — the measures the tool tracks (PV, solar-control glazing,
efficient HVAC, low-flow fixtures, RWH, grey-water, etc.), each with: id, gate it
affects (energy/water/materials), a description, and a **cost handle** (a BOQ rate key
or `CST_*` link) so `Sustain_LccBenefit` can roll capex + lifetime opex/savings into
the Design Development Cost/Budget Estimate via the BOQ Cost Manager.

---

## 7. Engine — `AnnualEnergyEstimator` (the core new math)

**Method:** monthly quasi-steady-state energy balance (EN ISO 13790 / ISO 52016-1
simplified) with a gain-utilisation factor. Generalises across climates and aligns
with how EDGE estimates internally. Output: annual kWh by end-use, then kWh/m²·yr.

**Inputs** (reuse the `LoadZone` inventory `BlockLoadEngine` already gathers — floor
areas, LPD/EPD W/m², envelope U/SHGC/orientation/area, OA, schedules — plus monthly
climate from `ClimateMonthlyRegistry`, plus per-project plant COP/SEER).

**Per month m, per zone:**
- Transmission + ventilation loss/gain coefficient:
  `H = Σ(U·A) + 0.33·(OA_flow_m3h + infiltration_m3h)` (W/K).
- Heat balance vs setpoint over the month's hours `t_m`:
  conduction `Q_cond = H · (T_out,m − T_set) · t_m`;
  solar `Q_sol = Σ(A_glazing · SHGC · shading · GHI_vertical,m)` (project GHI onto
  each façade orientation);
  internal `Q_int = (LPD + EPD + occupant_W)·floorArea·utilisation_schedule·t_m`.
- **Cooling-dominated vs heating-dominated handled by one utilisation factor that
  flips** (gain-utilisation for heating demand, loss-utilisation for cooling demand) —
  driven by climate sign, not a building-type flag.
- Monthly cooling/heating **thermal** demand → **electricity** via seasonal COP/SEER:
  `E_cool,m = Q_cool,m / SEER`. (COP/SEER is an input; never a fixed kWh.)
- `E_fans/pumps,m`, `E_light,m = LPD·area·hours_m`, `E_equip,m`, `E_dhw,m` similarly.

Sum months → annual kWh per end-use → `EUI = ΣkWh / floorArea`.
`energy_savings_pct = (baseline_EUI − design_EUI) / baseline_EUI · 100`, where
`baseline_EUI` is the resolved baseline's end-use intensities (§5).

**Pitfalls to encode:** latent/solar dominate in the tropics (degree-days alone are
wrong — that's why the monthly solar term is explicit); COP/SEER carries the whole
efficiency story; flag any zone with no envelope data.

### `SupplyAndGenerationLayer` (between demand and the gates)
Clean pipeline **demand → on-site generation → net import → carbon factor**, each
stage optional and data-driven:
- PV: `annual_PV_kWh = kWp · (annualGHI · PR)` (PR default 0.75; GHI from climate
  registry — never a fixed kWh/kWp). `kWp = 0` → no-op.
- `supply.mode` = grid_tied | off_grid | hybrid; `dieselFraction` blends factors.
- **Energy % (kWh) is computed before carbon.** Carbon (LEED + dashboard) =
  `net_import_kWh · gridFactor + diesel_kWh · dieselFactor`, applied **downstream**.
  So a cleaner grid improves carbon only; PV improves both. Grid-tied/off-grid/diesel
  all "just work" by data.

```jsonc
// _BIM_COORD/green_supply.json (per project)
{ "generation": { "pv": { "kWp": 0, "PR": 0.75, "yield_kwh_kwp_yr": null } },
  "supply": { "mode": "grid_tied", "gridCarbon_kgco2e_kwh": 0.45,
              "dieselCarbon_kgco2e_kwh": 0.8, "dieselFraction": 0.0 } }
```

---

## 8. Engine — `AnnualWaterEstimator`

```
L_person_day = Σ_fixture ( flow_per_use × uses_per_person_day )      // taps/showers: flow_lpm × min_per_use
annual_demand_L = L_person_day × occupancy × operatingDaysPerYear     // occupancy is a PARAMETER
net_demand_L    = annual_demand_L − RWH_yield_L − greywater_reuse_L   // RWH from RainwaterHarvestingCalc
water_savings_pct = (baseline_L_person_day − design_L_person_day) / baseline_L_person_day × 100
```

Fixture flows read from the model (low-flow fixture types) and matched to the building
use's frequency profile (§6). Building type selects the profile only — no code
branch. RWH yield reuses the existing `RainwaterHarvestingCalc`; grey-water reuse is a
project fraction.

---

## 9. Engine — `MaterialsRollup` (dual metric, honest)

| Track | Metric | Serves | Dataset (swappable, resolver chain) |
|---|---|---|---|
| A — embodied **carbon** | kgCO₂e/m² GWP (EN 15978) | LEED v5 Reduce-EC + hotspots + dashboard | EPD-specific → EC3/regional → ICE v3 → Ecoinvent |
| B — embodied **energy** | MJ/m² (CED) | EDGE materials gate (indicative) | EPD PERT/PENRT → ICE v3 MJ → regional |

- Quantities from BOQ takeoff (existing). Carbon factor via `CarbonFactorResolver`
  **Tier-1 path** (`STING_EMB_CARBON_NR`); **do not** use
  `CarbonTrackingEngine.EnsureLoaded()` — it is dead at runtime (reads the
  `MATERIAL_LOOKUP.csv` comment banner as a header, finds no carbon column, returns
  empty). Generalise the resolver to also resolve **MJ** via `SUS_MAT_ENERGY_MJ_M2_NR`
  / EPD PERT+PENRT.
- **EDGE materials %** is the EDGE app's number. STING tracks material **selections**
  + quantities (export them) and shows an **indicative MJ** estimate; never claims the
  kgCO₂e number is the EDGE materials %.
- **LEED v5**: kgCO₂e WBLCA (A1–A3) + identify the three largest hotspots.
- One EPD feeds both tracks (GWP + PERT/PENRT). Add `SUS_EPD_REF_TXT` on
  materials/types so the resolver can prefer product-specific EPD data.

Factor-source order is per-project data:
```jsonc
{ "factorSources": {
    "embodied_carbon": { "order": ["EPD_specific","EC3_regional","ICE_v3","Ecoinvent"] },
    "embodied_energy": { "order": ["EPD_PERT_PENRT","ICE_v3_MJ","regional_db"] } },
  "region": "Africa_Central" }
```

---

## 10. Shared parameters (`MR_PARAMETERS` convention, TXT + CSV mirror)

| Name | Group | Type | Purpose |
|---|---|---|---|
| `SUS_ENERGY_KWH_M2_NR` | SUS_SUSTAINABILITY | Number | Design annual EUI |
| `SUS_WATER_L_PD_NR` | SUS_SUSTAINABILITY | Number | Design L/person·day |
| `SUS_MAT_CARBON_KGM2_NR` | SUS_SUSTAINABILITY | Number | Embodied carbon intensity |
| `SUS_MAT_ENERGY_MJ_M2_NR` | SUS_SUSTAINABILITY | Number | Embodied energy intensity (EDGE track) |
| `SUS_EDGE_LEVEL_TXT` | SUS_SUSTAINABILITY | Text | Achieved level (None/Certified/Advanced) |
| `SUS_EPD_REF_TXT` | SUS_SUSTAINABILITY | Text | Type III EPD reference on material/type |

Follow the existing stable-GUID convention (UUIDv5 in the established namespace). Bind
project-info-scoped params to ProjectInformation; `SUS_MAT_*` + `SUS_EPD_REF_TXT` to
materials/types as appropriate. Register the new group in `PARAMETER_REGISTRY.json`.

---

## 11. Delivery layer — panel, commands, persistence

**`StingSustainabilityPanel`** (5th dockable pane; `StingPlumbingPanel` template;
new stable `PaneGuid` — generate once and never change so `UIState.dat` relocates it):
- Three EDGE gate tiles (Energy / Water / Materials): design % vs baseline, RAG
  colour, Certified/Advanced status, and **STING-indicative vs EDGE-official side by
  side** (official is a user-entered field for `delegated` gates).
- A **proxy-log strip** showing the baseline resolution path (rule D2/§5).
- Drill-down tables: per-end-use energy, per-fixture water, per-material carbon+MJ
  with the three carbon hotspots highlighted.
- Header context: scheme (EDGE/LEED), level (Certified/Advanced), occupancy parameter,
  supply mode. Snapshot header state into static fields before `ExternalEvent.Raise()`
  (the proven panel pattern).
- Reuse `StingResultPanel` RAG/tile/table builders inside the pane for visual parity
  with KutKpiDashboard.

**Commands** (tag-dispatched via `StingSustainabilityCommandHandler`, unknown tags
fall through to `StingCommandHandler`):

| Tag | Class | Tx | Purpose |
|---|---|---|---|
| `Sustain_ProjectSetup` | `SustainProjectSetupCommand` | Manual | The options surface (§2.5): pick scheme(s), level, country+climate zone, building use(s)/per-zone, occupancy, COP/SEER, supply, factor datasets, units → `project_setup.json` |
| `Sustain_Dashboard` | `SustainDashboardCommand` | ReadOnly | Run all selected schemes' providers, render the pane, persist a snapshot |
| `Sustain_SetBaseline` | `SustainSetBaselineCommand` | Manual | Resolve + stamp baseline intensities for the setup's country/climate-zone/use + show proxy path |
| `Sustain_SupplyConfig` | `SustainSupplyConfigCommand` | Manual | Edit PV / grid / diesel supply layer (`green_supply.json`) |
| `Sustain_EdgeExport` | `SustainEdgeExportCommand` | ReadOnly | ClosedXML workbook of model quantities + selections for EDGE-app upload |
| `Sustain_LccBenefit` | `SustainLccBenefitCommand` | ReadOnly | Per-measure life-cycle cost benefit → BOQ Cost Manager → DD Cost/Budget Estimate |
| `Sustain_EpdAssign` (Phase 2) | `SustainEpdAssignCommand` | Manual | Attach `SUS_EPD_REF_TXT` to materials/types |
| `Sustain_LeedScorecard` (Phase 2) | `SustainLeedScorecardCommand` | ReadOnly | LEED v5 WBLCA prerequisite report + MR credit scorecard (flag-gated) |

**Persistence:** `EdgeKpiSnapshot` mirrors `KutKpiSnapshot` (JSONL POCO with computed
`%` props) → `_BIM_COORD/sustainability/edge_kpi_log.jsonl` for trend/burn-down.
Snapshot fields: timestamp, energy/water/materials design intensities + %, EDGE level,
carbon kgCO₂e/m², embodied energy MJ/m², occupancy, supply mode, proxy-path summary.

**Registration:** register the provider + IUpdater-free panel in `StingToolsApp`
alongside the other panels; add a ribbon toggle (`ToggleSustainabilityPanelCommand`)
following `ToggleHvacPanelCommand`; invalidate the climate/baseline/scheme registries
on `OnDocumentClosing`.

---

## 12. Phasing

**Phase 1 (this build) — EDGE three-gate dashboard:**
ClimateMonthlyRegistry, the four engines, the three+measures data files, the scheme
abstraction (EDGE scheme live; LEED scheme present but scorecard deferred), the
dockable panel + **SETUP tab**, `Sustain_ProjectSetup` (options surface, §2.5) +
`Sustain_Dashboard/SetBaseline/SupplyConfig/EdgeExport/LccBenefit`, the params,
the snapshot. Dual-metric materials (carbon + MJ) is built now (it's the
shared layer). `SUS_EPD_REF_TXT` param added now.

**Phase 2 (flag-gated, when LEED confirmed):**
`Sustain_EpdAssign`, `Sustain_LeedScorecard` (WBLCA A1–A3 prerequisite report, three
hotspots, MR credit step-function scoring + bands). No engine changes — new scheme is
already data; only the LEED-specific commands + EPD register are new.

---

## 13. Acceptance criteria (Phase 1)

1. `Sustain_SetBaseline` resolves CAR/zone-0A/office (or logged proxy) and stamps
   baseline intensities; the resolution path is visible.
2. `Sustain_Dashboard` shows design-vs-baseline % for energy, water, materials, each
   with RAG + Certified/**Advanced** status, for the ~2,550 m² G+4 office at the chosen
   **occupancy parameter**, and shows STING-indicative beside an EDGE-official field.
3. Energy % comes from the monthly `AnnualEnergyEstimator` (not peak W); COP/SEER and
   PV are inputs; with-PV / off-grid / diesel all change the result correctly.
4. Materials shows **both** kgCO₂e/m² and MJ/m²; carbon reads `CarbonFactorResolver`
   Tier-1 (not the dead engine); three carbon hotspots are identified.
5. `Sustain_EdgeExport` produces a workbook that opens cleanly for EDGE-app upload.
6. `EdgeKpiSnapshot` persists to `_BIM_COORD/sustainability/` in the `KutKpiSnapshot`
   shape.
7. Each measure carries a cost handle and `Sustain_LccBenefit` rolls life-cycle cost
   benefit into the BOQ Cost Manager output.
8. Adding a hypothetical second scheme (e.g. a stub BREEAM JSON) requires **no engine
   code change** — proves the abstraction.
9. **Flexibility proof:** via `Sustain_ProjectSetup` alone (no recompile), switching to
   a different country + climate zone + building use + scheme + level re-runs the
   dashboard correctly — e.g. a temperate residential LEED-Gold project produces
   coherent, different numbers from the Bangui office.
10. **Mixed-use proof:** a building with per-zone uses (e.g. ground-floor retail +
    offices above) rolls up area-weighted (energy/materials) and occupancy-weighted
    (water) with no special-casing.

---

## 14. Testing

Add a `StingTools.Sustainability.Tests` project (xUnit, mirror `StingTools.Boq.Tests`):
- `SchemeEvaluator`: EDGE all-required AND logic (one gate fails → fail); level
  threshold switch (Certified 20 vs Advanced 40); LEED pointSum + band mapping.
- `GreenBaselineRegistry`: resolution fallback order + proxy-path correctness; project
  override merge.
- `AnnualEnergyEstimator`: monthly balance on a known synthetic zone reproduces a
  hand-calc within tolerance; cooling-dominated vs heating-dominated utilisation flip;
  COP/SEER scaling; PV offset reduces net import; off-grid/diesel carbon math.
- `AnnualWaterEstimator`: L/person·day for the office profile; occupancy scaling;
  RWH/greywater subtraction; a residential profile yields a different number with the
  same engine.
- `MaterialsRollup`: dual-metric resolution; Tier-1 carbon path; MJ via EPD/ICE;
  hotspot identification; EPD-specific factor preferred when `SUS_EPD_REF_TXT` set.

- **Flexibility/options:** the same engine + two different `project_setup.json`
  configs (Bangui-office-EDGE-Advanced vs temperate-residential-LEED-Gold) produce
  coherent, different results with no code change; per-zone mixed-use rolls up
  area-weighted (energy/materials) and occupancy-weighted (water); a config citing a
  catalogue row absent from the baseline registry resolves via the climate-zone proxy
  and logs it.

Tests must not depend on the Revit API (pure-POCO engines), matching the existing
test projects.

---

## 15. Caveats to record in the commit + `docs/CHANGELOG.md`

1. Built without `dotnet build` / Revit verification (Linux sandbox). Verify in Revit.
2. `AnnualEnergyEstimator` and `AnnualWaterEstimator` are **indicative** simplified
   methods — the EDGE app owns the certified energy/water %, and the EDGE app owns the
   certified materials (embodied-energy) %. The dashboard must label them indicative.
3. Baseline numbers in `STING_GREEN_BASELINES.json` are seeded from ASHRAE 90.1 /
   EDGE defaults as a starting catalogue; project teams confirm/replace per the EDGE
   app for the certified run. CAR may need a hot-humid proxy — captured as data.
4. LEED Phase-2 needs product-specific Type III EPDs, scarce in Central Africa — the
   resolver degrades gracefully to ICE/EC3 with a warning.
5. Monthly climate columns must be added to `STING_CLIMATE_DATA.json`; sites lacking
   them fall back to design-day values with a logged warning.

---

## Sources

Client: RFP 26-0404 (Technical Proposal forms); Annex B SoW; Annex C Draft Contract Art. 32.
EDGE: https://edgebuildings.com/ · https://edge.gbci.org/certification · EDGE Methodology Report.
LEED v5: https://www.usgbc.org/leed/v5 · One Click LCA / SWA LEED v5 embodied-carbon analyses.
Methods: ISO 52016-1 / EN ISO 13790 monthly quasi-steady method; CIBSE TM54 / Guide F / Guide G; ASHRAE 90.1-2022 / 62.1; EN 15978 / RICS WLCA; EC3 (buildingtransparency.org); ICE v3.
