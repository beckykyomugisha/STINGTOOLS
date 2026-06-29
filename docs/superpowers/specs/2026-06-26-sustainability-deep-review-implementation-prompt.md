# STING Sustainability (EDGE/LEED) — deep-review fix prompt

**For the implementing agent.** This is a self-contained brief to harden the
EDGE/LEED Sustainability module to production quality. The module already works
end-to-end (panel, four engines, scheme abstraction, dual-metric materials, the
not-computed guards, LCC, EDGE export). This pass closes the **integration,
accuracy, formula, category-inclusion, performance, and option-functionality**
gaps found in a deep cross-check, and removes developer-jargon from user-facing
text.

The module lives in:
- `StingTools/Core/Sustainability/` — engines (pure POCO, Revit-free, unit-tested)
- `StingTools/Commands/Sustainability/` — IExternalCommands
- `StingTools/UI/Sustainability/` — dockable panel
- `StingTools/Data/STING_GREEN_*.json`, `STING_WATER_USAGE_PROFILES.json`, `STING_CLIMATE_MONTHLY.json`
- Tests: `StingTools.Sustainability.Tests/`

## Ground rules (unchanged from the original build)

1. **Engines stay pure POCO / Revit-free and unit-tested.** Revit-facing code goes
   in the commands / orchestration / UI. Every formula change needs a test.
2. **Zero hardcoding of project-defining values.** Country, climate zone, building
   use, occupancy, COP, factors, etc. come from setup or registries — never code.
3. **Two material metric tracks** (kgCO₂e + embodied energy MJ), never conflated.
4. **Baselines proxy on climate zone, never country**; provenance log preserved.
5. **EDGE owns the certified number** — STING figures are estimates shown beside the
   official field. A gate may only show a pass when it was computed from model data
   (the `Computed` flag — keep it intact).
6. **Build in a real Revit environment** (Windows + Revit 2025 API). The pure
   engines + test project compile and `dotnet test` without Revit — run them. The
   Revit-facing layer compiles only with the API. Verify both.
7. Match house patterns: `StingResultPanel`, `*Registry` JSON+`_BIM_COORD` override,
   `StingLog`, tag-dispatch, `[Transaction]` modes.

---

## Workstream A — Integration with the rest of StingTools (highest value)

The module currently reinvents capabilities that already exist elsewhere, weaker.
Find each existing subsystem and wire the sustainability engine into it.

### A1. Climate — stop forking the climate truth
**Gap:** `STING_CLIMATE_MONTHLY.json` + `ClimateMonthlyRegistry` ship monthly means
+ GHI for only 4 cities, separate from the existing **41-city `STING_CLIMATE_DATA.json`
+ `Core/Climate/ClimateRegistry`** (Phase 187). Two sources of locational truth.
**Fix:** extend the existing climate data with the monthly columns (dry-bulb, wet-bulb,
GHI per month, annual GHI, grid carbon factor) and have `ClimateMonthlyRegistry`
read from / fall back to `ClimateRegistry` so all 41 sites work, with a single
project override. PV yield and the energy estimator's monthly climate must come
from this one source. Keep the additive fallback (sites without monthly columns
synthesise from design-day values + a logged warning).

### A2. Energy loads — use the load-profile library + HVAC envelope + real solar
**Gap:** `AnnualEnergyEstimator` runs off bare `LoadZone` office defaults and ignores
the envelope (it warns "no envelope data — gains under-counted"). Three existing
assets are unused:
- `Core/Hvac/Loads/LoadProfileRegistry` + `STING_LOAD_PROFILES.json` — **12 ASHRAE/CIBSE
  space-type profiles** (Office/MeetingRoom/Classroom/PatientRoom/OperatingRoom/Retail/
  Restaurant/Kitchen/Lab/Warehouse/Plantroom/Corridor) with per-use LPD/EPD/occupant
  density/OA/setpoints/schedules. Apply the matching profile to each zone by building
  use (the `HvacBlockLoadCommand.ZoneFromSpace` path already does this — mirror it).
- HVAC envelope detection (walls/windows/roof → `EnvelopeSegment` with U/SHGC/area/
  orientation). Reuse it so the energy estimate has real conduction + solar, not just
  internal gains. When no envelope is derivable, keep the graceful fallback.
- `BlockLoadEngine`'s real solar geometry (`IncidenceFactor` + `ClearSkyDirectNormalWm2`,
  Phase 187c) instead of the flat `GHI × 0.5` vertical-projection placeholder. Project
  GHI onto each façade by its `OrientationDeg` (the field already exists, currently
  unused).

### A3. Materials — integrate the BOQ takeoff, not a parallel one
**Gap:** `SustainabilityEngine.GatherMaterialLines` iterates **every element** calling
`GetMaterialIds`/`GetMaterialVolume` — a private, weaker takeoff. The BOQ system already
has the rich version:
- `BOQ/Takeoff/TakeoffRule` (category → quantity → unit), `BOQ/CostStamp`,
  `BOQ/CarbonFactorResolver` (4-tier), `BOQ/WasteFactor`, `BOQ/BiogenicCarbon`,
  `BOQ/CarbonPivotByPhaseLevel`.
**Fix:**
- Build material lines from the BOQ takeoff / `BOQCostManager` quantities where
  available, so the carbon figure matches the BOQ exactly (single source).
- Use the **full `CarbonFactorResolver` chain** (per-m³ AND per-kg via material density
  — the current code skips per-kg factors, dropping any material priced per kg).
- Apply `WasteFactor` and credit `BiogenicCarbon` (timber sequestration) so A1–A3 is
  correct, not raw volume × factor.
- Honour `SustainProjectSetup.FactorSources` order (EPD → EC3 → ICE → Ecoinvent /
  EPD_PERT_PENRT → ICE_MJ) — it's currently stored and **never used**. Make it drive
  the resolver's source preference per project/region.

### A4. Water — read real fixtures + integrate rainwater/greywater
**Gap:** `ReadDesignFixtureFlows` is a permanent `null` stub, so water is always the
25%-over-baseline placeholder; RWH yield is hardcoded `0`.
**Fix:**
- Scan `OST_PlumbingFixtures` (and the Plumbing module's `PLM_SUP_*` data / fixture
  types) for real low-flow fixture flows; only fall back to the indicative default
  when none are found (keep the `IsIndicativeDefault` flag honest).
- Call the existing `Core/Plumbing/RainwaterHarvestingCalc` (BS 8515) with roof area
  (from roofs / `PLM_STORM_ROOF_M2`) + rainfall (from the climate registry) for a real
  RWH yield; wire greywater reuse.
- **EDGE credits alternative water** toward the water %: incorporate RWH + greywater
  into the water-savings %, not only fixture efficiency (currently they only reduce
  net demand). Verify against EDGE methodology and document the choice.

### A5. LCC — wire into the BOQ Cost Manager, not a loose CSV
**Gap:** `Sustain_LccBenefit` writes a CSV "the BOQ Cost Manager picks up" but there's
no actual link to `BOQ/BOQCostManager`, `CostStamp`, or the rate card.
**Fix:** feed the per-measure capex + lifetime saving into the BOQ Cost Manager / DD
Cost Estimate path (e.g. as measured additions / rate-card rows / `CST_*` stamps),
so the contractual "sustainability targets in the Cost/Budget Estimate" is real, not a
side file. Measure capex sizing should use real model quantities where possible
(PV kWp, glazing m², fixtures count) rather than the crude proxies in `EstimateCapex`.

### A6. Server sync (optional, lower priority)
`EdgeKpiSnapshot` is local JSONL only. HVAC and KUT push snapshots to Planscape
(`HvacController`, `PlanscapeServerClient`). If multi-user sync is wanted, add a
`Sustain_PublishToServer` mirroring the HVAC pattern (+ a server entity + EF
migration noted as a deployment step). Gate behind the offline-config flag.

---

## Workstream B — Make the project options 100% functional

Every control on SETUP must actually drive the result. Today several are dead.

### B1. FactorSources (carbon/energy dataset order) — **stored, never used.** Wire into
the materials resolver (A3).

### B2. Units SI/IP — **stored, never used.** The whole UI + export always show SI.
Implement a real SI↔IP conversion layer (kWh/m²·yr ↔ kBtu/ft²·yr, L ↔ gal,
kgCO₂e/m² ↔ lb/ft², m² ↔ ft²) applied to every displayed value, the energy/water/
materials tables, and the EDGE export workbook. LEED commonly reports IP.

### B3. Per-zone / mixed-use — the data model (`SustainProjectSetup.Zones`) and the
engine rollup support multiple zones, but the SETUP UI only ever writes **one** zone.
Add a small **zones grid** (use + area + occupancy + COP per row, add/remove rows) so a
mixed-use building (e.g. ground-floor retail + offices above) is reachable. The
rollup is area-weighted (energy/materials) + occupancy-weighted (water) — verify it.

### B4. Building-use catalogue alignment. The UI offers office/residential/healthcare/
retail/hotel; the water profiles, load profiles, and baseline catalogue may list
different/ more keys. Make one canonical building-use list, **data-driven** (read the
union of keys the registries actually support), so adding a use to the JSON shows it in
the dropdown — no hardcoded UI list. Add the common missing ones (education, warehouse,
lab, restaurant, industrial) backed by real baseline + profile rows.

### B5. EDGE-official feedback. The "EDGE official" textboxes on the dashboard are
entry-only and ignored. Feed an entered official % back into the level determination
(official overrides the indicative for that gate), so a user can record the EDGE app's
certified materials/energy/water number and see the real EDGE level.

---

## Workstream C — Formula / accuracy gaps (each needs a test)

### C1. Energy estimator
- **Utilisation factor** is a flat `0.9` (AnnualEnergyEstimator) — replace with the
  real EN ISO 13790 gain/loss utilisation factor (depends on the gain/loss ratio and
  zone time constant). Document the method.
- **Solar** is `GHI × 0.5` for all glazing regardless of orientation — replace with
  per-façade incident solar (A2, reuse `IncidenceFactor`).
- **Heating** is divided by `1.0` (electric resistance assumed). Support a heating
  source / seasonal efficiency (gas boiler ~0.9, heat-pump COP) as an input, like
  cooling COP. Carbon should use the right fuel factor (not only grid electricity).
- **Fans/pumps** are a flat `15%` of cooling — derive from actual fan power / specific
  fan power where available, or make the fraction a per-use registry value.
- **Baseline EUI consistency**: design lighting/equipment use `meanFrac × 24 × 365`
  while the baseline LPD/EPD conversion uses `annualOperatingHours`. Make the two use
  the same hours basis so the % is apples-to-apples.

### C2. Water — fold RWH/greywater into the EDGE water % (A4); verify the per-person·day
model against EDGE methodology; confirm the usage-frequency profiles are applied per
the resolved building use (single source with B4).

### C3. Materials — real embodied **energy** (MJ) from ICE/EPD PERT+PENRT, not the flat
`carbonKg × 12` ratio fallback; per-kg carbon via density; biogenic split (A3); A1–A3
scope explicit.

---

## Workstream D — Category-inclusion gaps

### D1. Materials takeoff scope. The collector is `WhereElementIsNotElementType()` over
**all** elements — it can include non-physical elements and miss embedded materials
(rebar in concrete, MEP content). Scope to the physical model categories the BOQ
takeoff uses (structure / enclosure / hardscape for LEED's WBLCA: walls, floors, roofs,
structural framing/columns/foundations, etc.), include reinforcement, and decide +
document handling of linked models (LEED WBLCA wants the whole building).

### D2. Energy zones. Use **Rooms** as real zones (per-room use + area), not only the
single synthetic fallback zone, so a Room-based architectural model gets per-room loads.
Keep Spaces preferred.

### D3. Water fixtures. Scan `OST_PlumbingFixtures` (D-ties to A4).

---

## Workstream E — Performance

### E1. Materials takeoff runs an O(n) `GetMaterialIds`/`GetMaterialVolume` over every
element on **every** dashboard run. Scope by category (D1), cache the per-document
takeoff, and don't recompute materials when only energy/water changed. Consider caching
the whole `SustainabilityRunResult` per (document, setup-hash) so repeated panel
interactions don't re-walk the model.

### E2. `SchemeEvaluator.HighestAchievableLevel` re-invokes every provider a second time;
reuse the gate results already computed in the main loop.

---

## Workstream F — Correctness / robustness

- Guard savings % against NaN / divide-by-zero and cap display sensibly; large negative
  savings should read clearly ("design EUI X vs baseline Y — over baseline").
- Climate-zone auto-derive: when the zone is blank, derive it from the project's
  climate site / lat-long (ASHRAE 169 classification from HDD/CDD), not a silent
  temperate default; surface the assumption.
- Confirm the `Computed`/delegated/not-computed logic stays intact through all the
  above changes (the regression tests must keep passing).

---

## Workstream G — Remove developer jargon from user-facing text

Audit every **user-visible** string (XAML `Content`/`Text`, `TaskDialog`, status bar,
result panels, export sheets) and remove coding artifacts. Code comments may keep
internal references, but nothing the user sees should contain them.
- Buttons **"EPD assign (P2)"** / **"LEED scorecard (P2)"** → "EPD register" /
  "LEED scorecard" (drop the "(P2)").
- Status **"Ready · Phase 195 EDGE/LEED · indicative"** → "Ready" (or a plain hint).
- **"Running: {tag}"** shows raw command tags (e.g. `Sustain_LccBenefit`) — map to
  friendly names ("Running: Life-cycle cost…").
- Remove any "Phase …", "P0/P1/P2", "§x.y", "spec", "TODO" from TaskDialog/result text.
- Keep "indicative" — it's a real qualifier, not jargon.
Apply the same audit to the rest of the module's user-facing strings.

---

## Workstream H — Second-pass gaps (automation reach, cross-gate consistency, carbon integration)

Found in a second deep cross-check after Workstreams A–G. Same ground rules apply
— recommend the most flexible, data-driven solution for each; no hardcoding.

### H1. Make the module reachable from the workflow + automation layer
**Gap:** `WorkflowEngine.ResolveCommand` has **no `Sustain_*` cases** and there is **no
`WORKFLOW_Sustainability*.json` preset**, while every peer module (HVAC, Plumbing,
BOQ, Healthcare, Cost) ships both. Sustainability can't be chained, can't appear in
the morning-health-check chain, and isn't reachable from NLP.
**Fix:** register the sustainability command tags in `WorkflowEngine.ResolveCommand`
(SetBaseline / AutoFill / Dashboard / EdgeExport / LccBenefit / EpdRegister / Scorecard);
ship `Data/WORKFLOW_SustainabilityAssessment.json` (auto-fill → set baseline →
dashboard → EDGE export → LCC) discoverable by `WorkflowEngine.AppendUserPresets`;
add the handful of NLP patterns ("run edge", "sustainability dashboard", "carbon
estimate") to `NLPCommandProcessor`. Confirm the tags also dispatch from the main
`StingCommandHandler` fall-through, not only the sustainability panel.

### H2. Unify occupancy across the energy and water estimators
**Gap:** energy derives per-zone `OccupantCount` from load-profile area density when the
model carries none; water uses `setup.TotalOccupancy`. On a model with no typed total,
**energy runs on density-derived people while water runs on 0** — two gates, two
populations, one building. (`SustainabilityEngine.cs` ~L463 vs `GatherZones`.)
**Fix:** compute one project occupancy = Σ(per-zone occupants) after the load-profile
pass, feed it to both estimators, and let `setup.TotalOccupancy` override only when
the user has explicitly set it. Surface which source won. Add a test that energy and
water see the same occupancy on a zero-setup model.

### H3. Guarantee `SUS_*` baseline params actually bind (so `SetBaseline` persists)
**Gap:** the 6 `SUS_*` params are in `PARAMETER_REGISTRY.json` (group 35,
`binding: project`) but have **no rows in `CATEGORY_BINDINGS.csv`**. If the bind
pipeline keys off the CSV, `SetBaseline`'s `LookupParameter` returns null and the
stamp silently no-ops — the dashboard works in-session but nothing persists to the
model / schedules / IFC.
**Fix:** verify the bind path; if the CSV is authoritative, add the `SUS_*` →
ProjectInformation bindings there (and mirror in any coverage matrix). Make
`SetBaseline` log a clear warning when a target param isn't bound rather than failing
silently. Add a smoke check that the 6 params resolve after `LoadSharedParams`.

### H4. Integrate carbon with the existing carbon subsystems (whole-life roll-up)
**Gap:** operational carbon (energy × grid factor) and embodied carbon (materials) are
computed but siloed from `BIMManager/CarbonTrackingCommands`, `V6/CarbonStageTracker`
(RIBA-stage), the AVF `VisualiseCarbonHeatmapCommand`, and
`DesignOptions/OptionCostCarbonCalculator`.
**Fix:** surface a single **whole-life carbon** figure (embodied A1–A3 + operational
over a study period from setup) and feed/align it with `CarbonStageTracker` so the
RIBA-stage carbon view and the EDGE dashboard agree. Where the AVF carbon heatmap
already exists, let the sustainability materials carbon drive it (per-element embodied
carbon overlay) — reuse, don't reimplement. Keep the two material tracks separate
(carbon vs MJ); whole-life is carbon only.

### H5. Align the EDGE KPI snapshot with the gate metric
**Gap:** the EDGE water gate now reads `WaterSavingsInclAltPct` (fixture + alternative
water), but `EdgeKpiSnapshot` likely still records the fixture-only `WaterSavingsPct`,
so the persisted trend disagrees with the on-screen pass/fail.
**Fix:** record the same inclusive metric the gate uses; add a test pinning snapshot
water % to the gate's value.

### H6. SI/IP-convert the EDGE export
**Gap:** Workstream B2 made the panel unit-aware, but the EDGE export still emits SI
regardless of `Setup.Units`.
**Fix:** run export intensities/values through `SustainUnitConverter` and label units
in the sheet header, matching the panel. Add a test on the IP export path.

---

## Workstream I — Live-panel review: honesty, flexibility, integration, automation

Found running the dashboard / baseline / LCC / materials / cost panels on a real model
(`Tendo Main house.rvt`, location unset). Same ground rules: most-flexible, data-driven,
zero hardcoding; pure engines stay Revit-free + tested. Items H2/H3/H4/H5 already
landed — do NOT redo them; the greywater-capex fix is also already merged.

### Honesty & correctness

**I1. Location + building-use must be resolved, not silently defaulted.** On a model
with no Country/City/Climate **and** no explicit use, the engine ran as a *temperate-4A
office* — on a residential house (occ 17 at office density). **Derive building use from
the model** (Revit building/project type → room program → BuildingUseCatalog), and
**block + banner** when location or use is unset: "Location/use not set — figures are a
generic proxy, not your project." Never default to office. This one fix cascades into
climate, baseline, grid factor, energy savings and LCC, so it is top priority.

**I2. Honest proxy log.** The baseline dialog calls a wildcard-country + defaulted-4A
resolution an **"exact match."** Label any wildcard/defaulted axis as "fallback /
default proxy"; surface that climate zone 4A was a *default* (not derived); make the
header (`*//office`) and the resolved key (`*/4A/office`) agree.

**I3. Grid carbon factor from location, labelled.** Operational carbon used a default
~0.45 kgCO₂e/kWh because country was unset (CAR's grid is hydro-dominant — far lower).
Source the grid factor per country/region from the location registry; label "default
factor" until a real one resolves.

**I4. Export honours the `Computed` flag.** Even after H6's unit labelling, the EDGE
export still emits a water value/% for a gate the dashboard shows as **"Not computed."**
Export must print "not computed / indicative default" for any gate whose `Computed`
flag is false, matching the dashboard — no bare number a user could paste into EDGE.

**I5. Materials sanity + coverage warning.** Two problems shown together: total
**5,212 kgCO₂e/m²** is ~10× a normal office and **92% comes from one "Steel Purlins"**
line (~4,775 kgCO₂e/m²) — implausible, likely a quantity/factor error; AND only
**15 of 31 materials are carbon-stamped (0 EPD)** so the total under-counts 16
materials. Add a prominent warning when one hotspot exceeds a sane share (e.g. >60%)
or the total exceeds a sane ceiling, and surface coverage ("15/31 stamped, 0 EPD") on
the dashboard + export, not only the Materials tab.

**I6. LCC integrity.** Measures whose savings derive from a **not-computed** gate (e.g.
energy measures saving 5–9/yr off the broken climate baseline) must be flagged
"indicative — gate not computed," not presented as confident negatives; the headline
"Net lifetime benefit −82,015" needs a health caveat when its inputs are proxies.

**I7. RWH yield needs real rainfall.** The engine can compute RWH but yield showed 0
because rainfall wasn't resolved (location unset). Pull annual rainfall from the
location/climate registry so `RainwaterHarvestingCalc` produces a real yield on rainy
sites (Bangui ≈ 1,500 mm/yr) instead of 0.

**I8. Cosmetic.** Dashboard header shows "office · **zone** · 170 m²" — a placeholder
"zone" with no value. Drop it or fill it.

### Flexibility

**I9. Global location resolution.** Sites not in the 41-city climate list (e.g. Bangui)
fall to a default. Add Köppen / lat-long → climate-zone derivation **and** a per-country
grid-emission-factor table so ANY country resolves a real climate + grid factor, with a
documented seed + project override — never a hardcoded temperate/0.45 default.

**I10. LCC currency + discounting.** Capex/savings show as bare numbers; the EDGE app
uses XAF. Pull project currency from the BOQ Cost Manager / Project Info and label every
value. Replace undiscounted `annual×years` with a configurable discount rate (NPV), and
align the LCC study period with H4's whole-life-carbon period (don't keep a separate
hardcoded 25 yr).

### Integration

**I11. Sustainability-readiness as a ComplianceScan/model-health dimension.** Surface
"location / use / occupancy / fixtures incomplete" in the morning health check + status
bar, so a mis-set project is caught before someone opens the dashboard — don't make the
dashboard the only place the problem shows.

**I12. One-click deliverable + per-option run.** Generate an EDGE/LEED summary
schedule/sheet through the Docs/Sheet engine (a drawing-set artefact, not just xlsx) and
feed the BEP; and run the dashboard **per Revit Design Option** (reuse
`OptionCostCarbonCalculator`) so users can compare EUI / carbon / EDGE % and pick the
greenest option.

### Automation

**I13. Auto-resolve on open + staleness.** Mirror the HVAC pattern: resolve
location/use/occupancy on `DocumentOpened` (pre-warn if unset), and register an IUpdater
that marks the sustainability result stale when envelope / fixtures / materials change
(mirror `HvacEnvelopeStaleUpdater`), so the dashboard signals when it's out of date.

**I14. Measure target-seeker.** The highest-value automation: an optimiser that
auto-selects the least-cost set of measures to reach the chosen EDGE level (40/20/20 or
Certified) using the per-measure £/%-gain already in the LCC — output the recommended
measure set + residual gap per gate. Keep it data-driven over the measure registry.

**I15. Gate the workflow.** The H1 `WORKFLOW_SustainabilityAssessment.json` chains steps
but doesn't gate them. Add conditional gates ("location set", "fixtures modelled") via
the existing `WorkflowEngine` conditional-step mechanism, auto-run AutoFill → SetBaseline
→ Dashboard in order, and skip the materials step when it's delegated to the EDGE app —
so the one-click run can't emit confident-wrong numbers on a mis-set project.

---

## Workstream J — Location actually drives the result (USA-vs-Uganda bug)

Found in the live Revit smoke test: picking a **Country** changes nothing — energy,
baseline and grid-carbon are identical for USA and Uganda because the engine keys off
the **Climate site (city) → latitude → zone** and the **climate zone → baseline**, and
the Country field populates **none** of them. Both fall to the temperate **4A** default
(`STING_GREEN_BASELINES.json` only has zones `*`, `0A`, `4A`; all `country:"*"`). Same
ground rules: data-driven, project-override, zero hardcoding; pure engines Revit-free +
tested. Items I1/I9 (gate + latitude classifier) exist — J makes them actually fire.

### J1. Country selection must cascade into the run
On Country change in SETUP, auto-populate **Climate site (default city), Climate zone
(via `AshraeClimateZone.ClassifyByLatitude` on the city's lat), Grid carbon factor and
Diesel factor** from the country seed (below), persist them with the setup, and feed
them into the dashboard run. Today Country is inert — only the manually-typed Climate
site drives climate and only the manually-typed zone drives the baseline. User-typed
values must still override the auto-populated ones (don't clobber an explicit entry).
Add a test that USA and Uganda resolve different zones + grid factors from Country alone.

### J2. Country + climate data is a researched seed (data-driven dropdown)
The dropdown must be **data-driven** from the seed file (never a hardcoded enum), with
**friendly labels** (`CAF — Central African Republic`, not bare `CAF`). Extend
`STING_GRID_CARBON_FACTORS.json` (or a sibling `STING_COUNTRIES.json`) so each entry
carries: `iso3`, `label`, `defaultCity`, `lat`, `lon`, `climateZone` (ASHRAE 169),
`gridKgCo2ePerKwh`, `dieselKgCo2ePerKwh`, `source`. Project override at
`<project>/_BIM_COORD/`. Seed at least these (indicative — grid factors from IEA / Ember
2023 ranges; climate zone is the capital's, refine per-project; altitude noted where it
shifts the latitude-only zone):

| ISO3 | Country | Default city | Lat | Climate zone | Grid kgCO₂e/kWh | Notes |
|---|---|---|---|---|---|---|
| CAF | Central African Republic | Bangui | 4.4 | 1A (hot-humid) | 0.07 | hydro (Boali) + diesel |
| UGA | Uganda | Kampala | 0.3 | 2A (alt 1190 m mild) | 0.05 | hydro-dominant |
| KEN | Kenya | Nairobi | -1.3 | 3A (alt 1795 m) | 0.10 | geothermal + hydro |
| TZA | Tanzania | Dar es Salaam | -6.8 | 0A/1A | 0.33 | hydro + gas |
| RWA | Rwanda | Kigali | -1.9 | 2A (alt 1567 m) | 0.30 | hydro + methane + solar |
| ETH | Ethiopia | Addis Ababa | 9.0 | 3A (alt 2355 m) | 0.03 | almost all hydro |
| COD | DR Congo | Kinshasa | -4.3 | 1A | 0.03 | hydro (Inga) |
| NGA | Nigeria | Lagos | 6.5 | 0A/1A | 0.42 | gas + diesel |
| GHA | Ghana | Accra | 5.6 | 1A | 0.35 | hydro + gas |
| ZAF | South Africa | Johannesburg | -26.2 | 3B/4B (alt 1753 m) | 0.92 | coal-heavy |
| EGY | Egypt | Cairo | 30.0 | 2B (hot-dry) | 0.45 | gas |
| MAR | Morocco | Casablanca | 33.6 | 3C | 0.61 | coal + renewables |
| GBR | United Kingdom | London | 51.5 | 4A (temperate) | 0.21 | gas + wind + nuclear |
| FRA | France | Paris | 48.9 | 4A | 0.06 | nuclear |
| DEU | Germany | Berlin | 52.5 | 4A | 0.38 | mixed |
| USA | United States | New York | 40.7 | 4A | 0.37 | mixed (use city for real zone) |
| IND | India | New Delhi | 28.6 | 2A/3A | 0.71 | coal-heavy |
| CHN | China | Beijing | 39.9 | 4A | 0.58 | coal + renewables |
| ARE | UAE | Dubai | 25.2 | 0B/1B (hot-dry) | 0.40 | gas |
| AUS | Australia | Sydney | -33.9 | 3A | 0.66 | coal + renewables |
| BRA | Brazil | São Paulo | -23.5 | 3A | 0.10 | hydro-dominant |
| `*` | (global default) | — | — | — | 0.45 | fallback, flagged as default |

These are **seed/indicative** values; mark them as such, keep the project-override path,
and the latitude classifier still applies for any city the user types that isn't listed.

### J3. The I1 gate must actually block degenerate input
A run with **Floor area 0 / Occupancy 0** still showed a full energy result
(28,209 kWh, −46.4 %) instead of the readiness banner. The dashboard must refuse to
display a computed EUI / energy savings when floor area or occupancy is 0 — show the
banner + not-computed state. Verify the E1 run cache is **re-keyed** when Country /
climate site / zone change, so a stale result isn't shown after a setup edit.

### J4. Nearest-zone baseline fallback, not hardcoded 4A
`GreenBaselineRegistry` falls back to climate-zone **4A** when no country/zone baseline
matches (the "fell back to climate-zone 4A office" log). With J2's zones resolving
correctly, add the tropical baseline rows (0A/0B/1A/1B/2A office + the project's other
uses) so hot-climate projects resolve a tropical base case; when still unmatched, fall
back to the **nearest available zone by latitude/zone-number**, never a hardcoded 4A.

---

## Workstream K — Comprehensive, flexible building-use data fabric

**Why.** The live Bangui run exposed that a *residential* building is energy-modelled
as an *office*: `SustainabilityEngine.ProfileIdForUse` hard-codes
`case "residential": return "Office"` (and `hotel → Office`), and the load library
(`STING_LOAD_PROFILES.json`) only ships **Office / PatientRoom / Retail**. So a 170 m²
house resolved 17 occupants (office density 10 m²/p), office LPD/EPD and a 9–5 schedule →
EUI ≈ 203 kWh/m²·yr (a dwelling should be ~60–120). The building use *is* derived
correctly; the data fabric behind it is incomplete and partly hard-coded.

**The fabric is fragmented.** A building use is the single pivot of the whole estimate,
but its parameters live in four disconnected places plus a C# switch, with mismatched
coverage:

| Surface | Today | Gap |
|---|---|---|
| `BuildingUseCatalog` (the list) | 10 uses: office, residential, hotel, healthcare, education, retail, restaurant, industrial, lab, warehouse | the vocabulary |
| `STING_LOAD_PROFILES.json` (energy) | **3**: Office, PatientRoom, Retail | 7 of 10 uses fall back to Office |
| `SustainabilityEngine.DhwForUse` (C# switch) | **4** cases + default 5 | education/lab/restaurant/warehouse silently get office DHW |
| `STING_WATER_USAGE_PROFILES.json` | **5**: office, healthcare, hotel, residential, retail | 5 uses have no water profile |
| `STING_GREEN_BASELINES.json` (`buildingUse`) | **2**: office, residential (+ `*`) | 8 uses have no baseline |

Adding a use today means editing four files **and** a code switch, and a missing entry
fails *silently* to office. K makes the fabric **complete, data-driven, one-vocabulary,
and gap-surfacing** — zero hard-coded per-use values.

### K1 — Comprehensive load-profile library (`STING_LOAD_PROFILES.json`)

Expand from 12 → ~22 profiles so every `BuildingUseCatalog` use (and the common EDGE
building types) has a real, researched profile. Each profile keeps the **full** existing
field set (occupantDensityM2PerPerson, lightingWPerM2, equipmentWPerM2, sensible/latent
W per person, oaLpsPerPerson, oaLpsPerM2, cooling/heating setpoint, infiltrationAch, the
three 24-h schedules) **plus four new fields** (see K5): `dhwLPerPersonDay`,
`operatingDaysPerYear`, `source`, `edgeBuildingType`. Researched seed values — ASHRAE
90.1-2019 Table 9.6.1 (LPD), 62.1-2019 Table 6.2.2.1 (ventilation), Handbook Fundamentals
Ch.18 (gains), CIBSE Guide A 2015, CIBSE Guide G (DHW), EDGE building-type taxonomy:

| profile id | dens m²/p | LPD W/m² | EPD W/m² | sens/lat W/p | OA L/s·p | OA L/s·m² | cool/heat °C | infil ACH | DHW L/p·d | op days | schedule archetype | EDGE type |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Office | 10 | 9 | 12 | 75/55 | 10 | 0.3 | 24/21 | 0.3 | 5 | 250 | weekday 9–6 | Offices |
| Residential | 35 | 4 | 4 | 70/45 | 7.5 | 0.3 | 25/21 | 0.5 | 45 | 365 | evening/night | Homes |
| HotelGuestroom | 25 | 6 | 5 | 70/45 | 7.5 | 0.3 | 24/21 | 0.4 | 120 | 365 | night-weighted | Hospitality |
| HotelPublic | 15 | 10 | 5 | 73/58 | 7.5 | 0.6 | 24/21 | 0.3 | 10 | 365 | day+evening | Hospitality |
| Retail | 15 | 12 | 5 | 73/58 | 7.5 | 0.6 | 24/21 | 0.3 | 3 | 360 | extended day | Retail |
| Restaurant | 2.0 | 9 | 25 | 80/80 | 7.5 | 0.9 | 24/21 | 0.4 | 15 | 360 | midday+evening | Retail |
| Kitchen | 8 | 11 | 50 | 95/110 | 10 | 0.9 | 24/18 | 0.5 | 20 | 360 | midday+evening | Retail |
| PatientRoom (healthcare) | 12 | 8 | 12 | 73/58 | 12.5 | 0.3 | 24/22 | 0.3 | 60 | 365 | 24/7 | Hospitals |
| ClinicOutpatient | 8 | 9 | 8 | 73/58 | 10 | 0.3 | 24/22 | 0.3 | 12 | 300 | weekday day | Hospitals |
| OperatingRoom | 6 | 12 | 20 | 73/58 | 25 | 0.6 | 21/21 | 0.2 | 30 | 300 | weekday day | Hospitals |
| Classroom (education) | 3 | 9 | 8 | 70/45 | 5 | 0.6 | 24/21 | 0.3 | 4 | 200 | weekday term | Education |
| LectureHall | 1.5 | 9 | 8 | 70/45 | 5 | 0.3 | 24/21 | 0.3 | 4 | 220 | weekday day | Education |
| Library | 12 | 10 | 5 | 73/58 | 5 | 0.6 | 24/21 | 0.3 | 3 | 300 | day | Education |
| Lab | 15 | 13 | 30 | 73/58 | 10 | 1.0 | 22/21 | 0.5 | 10 | 300 | weekday day | Offices |
| Warehouse | 100 | 4 | 2 | 73/58 | 0 | 0.3 | 26/16 | 0.4 | 1 | 300 | weekday day | Light Industry |
| IndustrialLightMfg | 35 | 11 | 30 | 80/80 | 5 | 0.6 | 26/18 | 0.6 | 5 | 300 | weekday shift | Light Industry |
| DataCentre | 200 | 5 | 800 | 73/58 | 0 | 0 | 24/18 | 0.2 | 0 | 365 | 24/7 | Light Industry |
| GymFitness | 7 | 9 | 5 | 130/160 | 10 | 0.3 | 24/18 | 0.4 | 30 | 360 | day+evening | Retail |
| WorshipAssembly | 1.5 | 9 | 3 | 73/58 | 5 | 0.3 | 24/18 | 0.3 | 2 | 150 | intermittent | Offices |
| CinemaTheatre | 1.0 | 10 | 5 | 73/58 | 5 | 0.3 | 24/18 | 0.3 | 2 | 320 | evening | Retail |
| Parking | 0 (no occ) | 2 | 1 | 0/0 | 0 | 0.9 | none/none | 0.6 | 0 | 365 | continuous vent | Light Industry |
| Corridor | 50 | 5 | 1 | 73/58 | 0 | 0.3 | 24/21 | 0.3 | 0 | 365 | follow host | Offices |

Numbers are **seed/indicative** (mark them so) and project-overridable at
`<project>/_BIM_COORD/load_profiles.json`. Author the three 24-h schedule arrays per
profile in the archetype shape (copy the existing 12 profiles' format; residential =
morning + evening/night peak, hotel = night-weighted, retail = extended day, healthcare =
flat 24/7, etc. — ASHRAE 90.1 Appendix G / NREL DOE reference-building schedules).

### K2 — Data-driven use → profile + DHW resolution (kill the hard-codes)

- **`ProfileIdForUse` becomes data-driven.** Resolve the load-profile id from the use id
  via (a) direct id match, (b) an `aliases[]` / `useIds[]` array on each profile (so
  `residential`/`dwelling`/`apartment`/`house` all map to `Residential`), (c) loose match
  (case/space/hyphen-insensitive, as the registry already does). When still unresolved,
  fall back to the **nearest sibling** (e.g. `clinic → PatientRoom`, `motel → HotelGuestroom`)
  and **log the fallback** — never a silent `→ Office`. Delete the
  `case "residential": return "Office"` / `hotel → Office` lines.
- **DHW moves out of the C# switch into the profile data.** `dhwLPerPersonDay` is a
  per-use property → it belongs in `STING_LOAD_PROFILES.json` (CIBSE Guide G values in the
  table above), read through the registry with the project override. Delete
  `SustainabilityEngine.DhwForUse`; resolve DHW from the resolved profile. Keep the old
  switch values as a `*Fallback` constant only.
- **Occupancy density now follows the resolved profile** (K1 fixes the "17 occupants on a
  house" bug — Residential density 35 m²/p → ~5 occupants).

### K3 — One canonical building-use vocabulary across all four surfaces

`BuildingUseCatalog`, the load profiles, `STING_WATER_USAGE_PROFILES.json`, and
`STING_GREEN_BASELINES.json` must share **one** canonical use-id set (+ aliases).

- Extend `BuildingUseCatalog` to cover the full list (add hotel-public, clinic, kitchen,
  library, gym, worship, cinema, data-centre, parking, etc. as needed) — data-driven,
  friendly-labelled, project-overridable (mirror the J2 country-dropdown pattern).
- Add **water-usage profiles** and **baseline rows** for the new uses (or a documented,
  logged fallback — e.g. office-like water for dry uses), so no use is silently office.
- Support **subtypes / mixed-use**: the per-zone building use (Workstream B3) already
  exists — make sure every zone independently resolves its own load + water + DHW +
  baseline. Allow a `parent`/`subtypeOf` field so an "office:trading-floor" subtype can
  overlay just the fields it changes.
- Every per-use file gets a `<project>/_BIM_COORD/` override and provenance.

### K4 — Coverage guard (surface the gap, don't default silently)

Add a small `BuildingUseCoverage` checker (pure, unit-tested) that, for each use in the
catalog, reports whether a load profile, water profile, DHW value, and baseline (exact or
nearest) exist. Wire it two ways:

- A **unit test** that fails if any `BuildingUseCatalog` use lacks a load profile + DHW +
  water profile (baseline may legitimately use nearest-zone fallback — that's logged).
- A **dashboard note**: when the active run resolves a use via fallback (profile, water,
  DHW, or baseline), surface "ℹ {use} {surface} resolved by fallback ({from} → {to}) —
  indicative" in the NOTES panel, the same honest-proxy style as I2/J. A gap must read as
  a *visible fallback*, never a silent office substitution.

### K5 — "Comprehensive info, not just profiles" (the full design-parameter set)

Each load profile carries the complete set a real load calc needs, with provenance:
occupant density · LPD · EPD · sensible+latent per person · OA per-person **and** per-m² ·
cooling+heating setpoints · infiltration ACH · 24-h occupancy/lighting/equipment schedules
· **dhwLPerPersonDay** · **operatingDaysPerYear** · **source** (the standard each number
came from) · **edgeBuildingType** (so the STING estimate maps cleanly onto the EDGE app's
building category). Values are seed/indicative and project-overridable. This makes the
building use a single, complete, defensible design context — not a thin label that silently
borrows office numbers.

**Ground rules** (unchanged): pure-POCO engines Revit-free + unit-tested; baselines proxy
on climate zone, never country; EDGE owns the certified number (keep Computed/Delegated
guards); any new pure file under `Core/Sustainability/` (or `Core/Hvac/Loads/`) added to
`StingTools.Sustainability.Tests.csproj` as `<Compile Include … Link=…>`. Numbers are
documented as indicative; nothing project-defining is hard-coded.

---

## Acceptance criteria

1. Energy uses the load-profile library + real envelope + per-façade solar from the
   single climate source; a temperate vs. hot-tropical office produce sensibly
   different EUIs; heating source + COP are inputs.
2. Materials carbon matches the BOQ for the same model (single takeoff), uses the full
   resolver chain incl. per-kg + waste + biogenic, and honours `FactorSources`.
3. Water reads real fixtures when present, computes RWH via `RainwaterHarvestingCalc`,
   and folds alternative water into the EDGE water %.
4. LCC rows feed the BOQ Cost Manager / DD Cost Estimate (not just a CSV).
5. **Every SETUP control changes the result**: FactorSources, Units (SI/IP fully
   converts), per-zone mixed-use, building-use list (data-driven), EDGE-official
   feedback — all functional.
6. Materials takeoff is category-scoped + cached; a large model's dashboard run is
   noticeably faster than the all-element walk.
7. No user-visible string contains "Phase/P2/§/spec/TODO/raw command tags".
8. `dotnet test StingTools.Sustainability.Tests` stays green and **grows** — new tests
   for: utilisation factor, per-façade solar, heating COP, per-kg + waste + biogenic
   materials, SI/IP conversion, RWH-in-water-%, mixed-use rollup, FactorSources order,
   EDGE-official override.
9. Full Revit 2025 build clean (0 errors); the not-computed guards + delegation still
   hold.

## Testing

Extend `StingTools.Sustainability.Tests` (xUnit, Revit-free). Engines must stay
testable; put Revit-only wiring behind thin commands. Run `dotnet test` and report the
verbatim pass/fail counts.

## Constraints / caveats to record

- Build + test in a real Revit 2025 environment; keep the pure-engine/test split.
- Where an external dataset isn't available (e.g. full ICE MJ table, EPD library),
  ship a documented seed + project-override path — never invent numbers in code.
- Log any coverage caps (top-N hotspots, sampled categories) rather than silently
  truncating.

## Operating-calendar basis (WS L1/L2)

Energy and water share ONE operating year per building use: `operatingDaysPerYear`
on the load profile (and propagated onto the LoadZone). This single field folds the
weekend/closure factor into the annualisation — a weekday-only use is seeded below
365 (office 250, education 200, worship 150), a 24/7 use is 365 (residential,
healthcare, data centre, parking). Both engines consume it:

- **Water**: `annual_demand = L/person·day × occupancy × operatingDaysPerYear`.
- **Energy**: `operatingHours = occMeanFrac × 24 × operatingDaysPerYear` (clamped
  ≤ 8760) drives lighting + equipment electricity and the baseline EUI conversion;
  the internal-gain-driven conditioning is scaled by `opFrac = operatingDaysPerYear
  / 365` (the gain/loss ratio γ is unchanged, only the magnitude); DHW is annualised
  on `operatingDaysPerYear`.

So a 250-day office is not billed 365 days of HVAC/lighting/DHW, and a weekday-only
use is not over-counted against a 24/7 use. The factor is seed/indicative and
project-overridable via `<project>/_BIM_COORD/load_profiles.json`.
