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
