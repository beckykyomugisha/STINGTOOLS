# Cost Management Implementation Plan

**Branch:** `claude/revit-api-cost-management-qH8Vv`
**Scope:** BOQ / 5D cost / Quantity Surveyor (QS) workflow enhancements
**Authoring date:** 2026-05-18
**Status:** Advisory plan — pending engineering review

---

## 0. Executive summary

STING already ships ~7.2k LOC of BOQ + 5D code (`StingTools/BOQ/`,
`BIMManager/SchedulingCommands.cs`, `Core/Storage/StingCostRateOverrideSchema.cs`),
matching server-side entities (`BoqDocument`, `QuantityLine`, `TakeoffRule`,
`BoqBaseline`, `BoqVariation`, `CostItem`), and a mobile cost dashboard.

The infrastructure is present. The gaps are **professional QS workflow
gaps** — not Revit-API gaps. Geometry primitives, schedule generation,
parameter binding, IFC export and Extensible Storage are all sufficient.
What's missing is:

1. **Flexibility** — rate sourcing is hard-wired to one CSV; NRM2 is the
   only measurement standard; UGX/USD are encoded into parameter names;
   per-element overrides cover only unit rate (no waste / dayworks /
   markup); take-off rules are C# code, not data.
2. **Integration** — plugin BOQ snapshots never reach the server
   (`BoqController` baselines stay empty); the server's `TakeoffRule`
   entity is never consumed by the plugin; 4D and 5D engines compute
   quantities independently; IFC export does not populate IFC4 `Qto_*`
   sets; mobile cost-dashboard is read-only.
3. **Automation** — no IUpdater for cost staleness; no
   `WORKFLOW_BOQ_*.json` presets; no pre-flight validators; tag pipeline
   runs FormulaEngine but does not write cost params; no approval state
   machine on rows or snapshots.

This document specifies a **9-phase implementation** (P0 → P8) that
converts the current estimating tool into a full contract-administration
suite covering NRM1 cost plans, multi-standard take-off, payment certs,
variations, EVM and final account.

Each phase declares deliverables, files to add/edit, data contracts,
acceptance criteria and dependencies. Phases A (P0–P3) close
flexibility + integration gaps and unblock everything downstream; phase
B (P4–P5) adds the high-leverage QS workflows (payment certs +
variations + EVM); phase C (P6–P8) adds multi-standard, external
integrations and ICMS3 carbon-cost.

---

## 1. Diagnosed gaps (with file:line evidence)

### 1.1 Flexibility gaps

| # | Gap | Evidence |
|---|---|---|
| F-1 | Rate-lookup chain is hard-coded; no `IRateProvider` interface. CSV path baked in. | `BOQCostManager.cs:205-272` `ResolveRate()`; `cost_rates_5d.csv` loaded by filename at `:1215` |
| F-2 | NRM2 section codes hard-coded by string matching; no CESMM4 / POMI / ICMS3 / MMHW path. | `BOQCostManager.cs:1390-1411` `DeriveNrm2Section()` |
| F-3 | Quantity unit derivation is C# if/switch on category names; QS cannot author new rules. | `BOQCostManager.cs:278-318` `DeriveQuantity()` |
| F-4 | UGX/USD baked into parameter names (`ASS_CST_UNIT_PRICE_UGX_NR`); single global FX rate; no inflation indexing. | `MR_PARAMETERS.txt`; FX at `BOQCostManager.cs:150-152` |
| F-5 | Per-element overrides cover unit rate only — no `WastePercent`, `DayworksRate`, `OverheadPct`. | `StingCostRateOverrideSchema.cs:30-36` |
| F-6 | Wastage / laps / formwork uplift applied globally at export, not per item. | `BOQProfessionalExportCommand.cs:1535-1546`, `:1707-1720` |

### 1.2 Integration gaps

| # | Gap | Evidence |
|---|---|---|
| I-1 | Plugin `SaveSnapshot()` writes local JSON only; never POSTs to `BoqController`. | `BOQCostManager.cs:609`; missing `PlanscapeServerClient.PushBoqSnapshot()` |
| I-2 | Server `TakeoffRule` entity exists but plugin never reads it. | `Planscape.Core/Entities/TakeoffRule.cs:24`; no plugin caller |
| I-3 | `Scheduling4DEngine` carries its own `DefaultCostRates` dictionary, independent of BOQ. | `SchedulingCommands.cs:107-150` |
| I-4 | `CashFlow5DCommand` rebuilds costs from 4D, doesn't consume BOQ totals. | `SchedulingCommands.cs:1460+` |
| I-5 | IFC export does not populate IFC4 `Qto_*` quantity sets — cost tools (Cost-X, Candy, CostOS) can't ingest. | No `Qto_*` writer; only `IfcBoqExtractor.cs` (read-only) |
| I-6 | Native Revit Material Takeoff schedules (`MR_SCHEDULES.csv`) never feed the BOQ. | `BuildBOQDocument` scans elements, ignores `ViewSchedule.GetScheduleData()` |
| I-7 | `BOQSnapshotMeta` has no checksum / server-facing ID; can't reconcile with `BoqBaseline.Checksum`. | `BOQCostManager.cs:216`; `BoqBaseline.Checksum` unused by plugin |
| I-8 | Mobile `cost-dashboard.tsx` is read-only; no VO approval / payment cert sign-off. | `Planscape/app/(tabs)/cost-dashboard.tsx` |

### 1.3 Automation gaps

| # | Gap | Evidence |
|---|---|---|
| A-1 | Tag pipeline runs FormulaEngine but doesn't compute `ASS_CST_TOTAL_UGX_NR = qty × rate`. | `TagPipelineHelper.RunFullPipeline` does not call cost write-back |
| A-2 | No `IUpdater` flags cost as stale when geometry / material / type changes. `StingStaleMarker` has no cost analog. | `StingAutoTagger.cs` (geometry-only) |
| A-3 | No `WORKFLOW_BOQ_*.json` chained presets — every step is a manual command. | `Data/WORKFLOW_*.json` (no BOQ entries) |
| A-4 | No pre-flight validators (missing material, untyped category, unpriced PROD, zero quantity). | `RunAllValidatorsCommand` only covers v4 routing |
| A-5 | NRM2 section auto-classification is hard-coded; QS cannot author rule overrides. | `BOQCostManager.cs:1390-1411` |
| A-6 | No quantity-recompute trigger when geometry edits; manual `BuildBOQDocument` re-run required. | No IUpdater on cost-relevant changes |
| A-7 | No approval state machine on `BOQDocument` or rows — snapshots are immutable timestamps, not workflow states. | `BOQModels.cs` `BOQDocument` has no `Status` |

---

## 2. Architectural principles

1. **Server is the source of truth for committed BOQs.** Plugin can
   work offline (already does via `OfflineQueue`) but committed
   baselines, variations and approvals live on the server. Mirror the
   pattern already used for `BimIssue` + `DocumentRecord`.
2. **One quantity engine, two consumers.** 4D scheduling and 5D
   cost must read the same `QuantityLine` rows. Eliminate
   `Scheduling4DEngine.DefaultCostRates`; route through a shared
   `IRateProvider`.
3. **Data-driven take-off rules.** The C# rule chain in `DeriveQuantity`
   /`DeriveNrm2Section` must move into editable JSON loaded through a
   registry, mirroring `STING_DRAWING_TYPES.json` /
   `STING_AEC_FILTERS.json`. QS edits JSON; no rebuild.
4. **Currency- and standard-agnostic core.** Rename / re-bind
   `ASS_CST_*_UGX_NR` to currency-neutral storage with an explicit
   `CurrencyCode` + `FxRateAtDate` field. NRM2 becomes one of N
   measurement-standard plugins behind an `IMeasurementStandard`
   interface.
5. **Extensible Storage for QS-private data.** Markup, overhead %,
   waste % per element must not pollute shared params — use
   `StingCostRateOverrideSchema` (extend its schema, don't replace).
6. **Idempotent + IUpdater-driven.** Same pattern as `StingAutoTagger`
   / `StingStaleMarker`. Cost staleness is detected, not polled.
7. **Workflow-driven, not command-driven.** A QS shouldn't run 5
   commands sequentially — `WorkflowEngine` should chain them.
8. **Mobile is a first-class write surface.** Variation approvals and
   payment cert sign-offs must be possible from the field, not just the
   desktop.

---

## 3. Phased roadmap

| Phase | Theme | Duration | Closes gaps | Unblocks |
|---|---|---|---|---|
| P0 | Foundations — `IRateProvider`, currency-neutral params, data-driven take-off rules | 2 sprints | F-1, F-3, F-4 (part), A-5 | Everything |
| P1 | Server sync — push snapshots, pull baselines, share checksums | 1 sprint | I-1, I-7 | P3, P5, P6 |
| P2 | Automation engine — IUpdater + workflow presets + validators | 1 sprint | A-2, A-3, A-4, A-6 | P3, P4 |
| P3 | 4D/5D unification — single quantity engine | 1 sprint | I-3, I-4, I-6, A-1 | P5 |
| P4 | NRM1 elemental cost plan (early-stage estimating) | 1 sprint | (new capability) | — |
| P5 | Contract administration — payment certs + variations + EVM | 2 sprints | (new capability) | P7 |
| P6 | Multi-standard take-off (CESMM4 / POMI / ICMS3 / MMHW) | 2 sprints | F-2 | P8 |
| P7 | Mobile write surface — approvals + sign-off | 1 sprint | I-8 | — |
| P8 | External rate connectors + IFC4 `Qto_*` round-trip + ICMS3 carbon-cost | 1–2 sprints | I-5, F-1 (live) | — |

Total: ~12 sprints. Recommend P0–P3 in lock-step (4 sprints) before
any new feature work — they're enabling refactors.

---

## 4. Phase specifications

### P0 — Foundations (2 sprints)

**Goal:** Make the rate engine pluggable, the parameter set
currency-neutral, and the take-off rules data-driven. This phase is
pure refactor; user-visible features should not change.

**Deliverables:**

- New interface `StingTools/BOQ/IRateProvider.cs`:
  ```
  public interface IRateProvider
  {
      string Id { get; }              // "csv-default", "bcis-http", "project-card"
      int Priority { get; }           // higher wins
      RateLookup Resolve(RateRequest req);  // returns null if no match
      bool RequiresNetwork { get; }
  }
  public record RateRequest(string ProdCode, string MatCode, string Discipline,
                            string Unit, string CurrencyCode, DateTime AsOf,
                            string LocationCode, string ProjectId);
  public record RateLookup(decimal UnitRate, string CurrencyCode, string Unit,
                           string SourceId, DateTime FetchedUtc, string Provenance);
  ```
- Concrete providers:
  - `CsvRateProvider` (wraps existing `cost_rates_5d.csv` load)
  - `ParameterOverrideRateProvider` (reads `ASS_CST_*` params)
  - `ExtensibleStorageRateProvider` (reads `StingCostRateOverrideSchema`)
  - `ProdCodeRateProvider` (PROD code fallback)
  - `ScheduledRateProvider` (last-resort `Scheduling4DEngine.DefaultCostRates`)
- `RateProviderRegistry` — composition root, priority-ordered, returns
  first non-null lookup. Cache results per `(elementId, asOf)`.
- Refactor `BOQCostManager.ResolveRate()` to call
  `RateProviderRegistry.Get(doc).Resolve(req)`. All five existing
  branches become providers. **No behaviour change.**

**Data-driven take-off rules:**

- New file `StingTools/Data/STING_TAKEOFF_RULES.json`:
  ```
  {
    "version": "1.0",
    "rules": [
      {
        "id": "wall-area",
        "match": { "category": "OST_Walls", "structural": "*" },
        "unit": "m2",
        "quantityParam": "HOST_AREA_COMPUTED",
        "unitConversion": "ft2_to_m2",
        "wastePercent": 0,
        "description": "Wall, {material}, {thicknessMm}mm thick",
        "nrm2Section": "14"
      },
      { "id": "rebar-mass", "match": { "category": "OST_Rebar" },
        "unit": "kg", "quantityParam": "REBAR_TOTAL_MASS",
        "wastePercent": 5, "nrm2Section": "15.3" }
    ]
  }
  ```
- New loader `StingTools/BOQ/TakeoffRuleRegistry.cs` (mirrors
  `DrawingTypeRegistry`):
  - corporate baseline ships in `Data/`
  - project override at `<project>/_BIM_COORD/takeoff_rules.json` wins
  - per-rule SHA-256 drift detection so corporate edits flag as
    `origin: project`
- Refactor `DeriveQuantity()` + `DeriveNrm2Section()` to call
  `TakeoffRuleRegistry.Match(element)`. **Hardcoded paths removed.**

**Currency-neutral parameter set:**

- Add new shared params (UUIDv5 in cost namespace):
  - `ASS_CST_UNIT_RATE_NR` — currency-neutral
  - `ASS_CST_CURRENCY_TXT` — ISO 4217 code (`USD`, `UGX`, `GBP`, `EUR`)
  - `ASS_CST_FX_TO_BASE_NR` — rate at write-time
  - `ASS_CST_FX_DATE_DT` — when the FX rate was fixed
  - `ASS_CST_AS_OF_DT` — pricing reference date
- Keep `ASS_CST_*_UGX_NR` / `ASS_CST_*_USD_NR` as **derived view
  params** populated from the neutral set at export time. No
  breaking change for existing schedules.

**Per-element override extension:**

- Extend `StingCostRateOverrideSchema` schema:
  ```
  public class Override
  {
      public decimal RateGbp;          // existing
      public string Unit;              // existing
      public decimal WastePercent;     // NEW
      public decimal OverheadPercent;  // NEW
      public decimal ProfitPercent;    // NEW
      public string DayworksCode;      // NEW
      public string LockedByUser;      // NEW — pessimistic lock
      public DateTime? LockedUntilUtc; // NEW
      public string Note;
      public long StampedUtcTicks;
      public string StampedBy;
  }
  ```
- Schema version bump from `1` → `2`; migration on read.

**Acceptance criteria:**

- BOQ output identical before and after the refactor for the demo
  project (regression test).
- `dotnet test` against a unit-test fixture project asserts all five
  legacy fallback paths still resolve.
- Adding a new rate provider (e.g. `BcisHttpRateProvider` stub) is a
  ~50-LOC change, no edits to `BOQCostManager`.
- Editing `STING_TAKEOFF_RULES.json` on disk + running
  `TakeoffRules_Reload` reflects in the next BOQ build without a
  rebuild.

**Risks:**

- Param GUID churn — must ship a one-time migration command that
  copies `_UGX_NR` → neutral params with `CurrencyCode="UGX"`. Don't
  delete the old params; mark them `Derived` in
  `PARAMETER_REGISTRY.json`.

---

### P1 — Server sync (1 sprint)

**Goal:** Plugin snapshots reach the server and become first-class
`BoqBaseline` rows. Variations cross-link cleanly. Same pattern as
`PlanscapeServerClient.PushBimIssue()`.

**Deliverables:**

- New client method:
  `PlanscapeServerClient.PushBoqSnapshot(BoqSnapshotPayload)` posts to
  `POST /api/projects/{id}/boq/baselines`. Returns server-assigned
  `Id` + `Checksum`.
- New client method:
  `PlanscapeServerClient.PullBoqBaselines(projectId, since)` returns
  baselines plus variation states so the plugin shows
  "approved on web" status.
- Extend `BOQSnapshotMeta` (`BOQCostManager.cs:216`):
  - `ServerBaselineId` (Guid?)
  - `Checksum` (SHA-256 of canonicalised line items)
  - `SyncState` (`Local | Pending | Synced | Conflict`)
- Hash function: stable JSON canonicalisation (sort by `Id`, normalise
  numbers, exclude server-only fields), SHA-256.
- `OfflineQueue` carries `BoqSnapshotPayload` so offline saves push
  later (same machinery the mobile app uses).
- Server side: `BoqController` accepts an optional client checksum;
  rejects with `409 Conflict` if it doesn't match the recomputed one
  on receive (defence in depth).

**Variation linkage:**

- `BOQSnapshotDiff` (plugin) now mints a server-side
  `BoqVariation` per cluster on submit. Status flows
  `Draft → Submitted → Reviewed → Approved | Rejected → Incorporated`.
- Approval triggers a SignalR `boq.variation.approved` event; plugin
  panel refreshes the affected snapshot.

**Acceptance criteria:**

- A snapshot saved in Revit appears in the server dashboard within 60 s
  (online) or on next sync (offline).
- Two engineers saving snapshots from different machines produce two
  distinct `BoqBaseline` rows with non-colliding checksums.
- A variation approved on the web shows as `Approved` next to the row
  in the plugin BOQ panel within 30 s (SignalR).

---

### P2 — Automation engine (1 sprint)

**Goal:** Detect cost staleness automatically, run BOQ workflows in
one click, and gate exports behind validators.

**Deliverables:**

- New IUpdater `StingTools/Core/StingCostStaleMarker.cs`:
  - Triggers on `Element.GetChangeTypeGeometry()`,
    `GetChangeTypeParameter(...)` for `MaterialIds`, type swap.
  - Marks element `ASS_CST_STALE_BOOL = 1` + writes
    `ASS_CST_STALE_REASON_TXT` (`Geometry | Material | Type | Rate`).
  - Registered alongside `StingAutoTagger` / `StingStaleMarker`.
- New validators (under `StingTools/Core/Validation/Cost/`):
  - `MissingMaterialValidator` — element with cost rule but
    `MaterialIds.Count == 0`
  - `UntypedCategoryValidator` — element in cost-bearing category
    with no matching `TakeoffRule`
  - `UnpricedProdValidator` — `RateProviderRegistry` returns null
  - `ZeroQuantityValidator` — quantity rule yields zero/negative
  - `StaleCostValidator` — counts `ASS_CST_STALE_BOOL == 1`
  - All implement existing `IValidator` shape; surfaced via
    `RunAllValidatorsCommand` and a new `Cost_ValidateAll` command.
- New workflow presets in `StingTools/Data/`:
  - `WORKFLOW_BOQ_FullRefresh.json` — validate → take-off rebuild →
    rate apply → snapshot → export → push to server
  - `WORKFLOW_BOQ_QuickValuation.json` — % complete update + EVM
    recompute + cost report export
  - `WORKFLOW_BOQ_TenderPack.json` — un-priced BOQ + preambles +
    drawings register + form of tender (uses Template Engine v1.1)
- New commands wired into `StingCommandHandler`:
  - `Cost_RunWorkflow` (preset picker)
  - `Cost_ValidateAll`
  - `Cost_ClearStale` (resets `ASS_CST_STALE_BOOL` after recompute)

**Acceptance criteria:**

- Editing a wall's thickness flips `ASS_CST_STALE_BOOL` on that wall
  within one transaction without explicit user action.
- `Cost_ValidateAll` returns a structured `ValidationResult` listing
  all five validator categories with counts and selectable element
  IDs.
- Running `WORKFLOW_BOQ_FullRefresh` produces the same artefacts as
  five manual commands run in sequence, with rollback on failure
  (existing `TransactionGroup` pattern in `WorkflowEngine`).

---

### P3 — 4D/5D unification (1 sprint)

**Goal:** One quantity engine. `Scheduling4DEngine` consumes
`QuantityLine` rows. Cash-flow comes from BOQ, not from a parallel
copy of cost rates.

**Deliverables:**

- Delete `Scheduling4DEngine.DefaultCostRates`
  (`SchedulingCommands.cs:107-150`). Replace with
  `_rateProvider.Resolve(...)` calls.
- `CashFlow5DCommand` reads `BoqDocument.LineItems` (in-memory) +
  `Scheduling4DEngine.TaskAssignments` (which lines map to which
  tasks), producing a per-task cash-flow projection.
- New join table on the server: `BoqLineTaskLink`
  (`QuantityLineId` ↔ `Scheduling4DTaskId`, `PercentAllocation`).
- Native Revit Material Takeoff schedule integration:
  - New rule kind in `STING_TAKEOFF_RULES.json`:
    `source: "schedule:STING - Concrete Takeoff"` — uses
    `ViewSchedule.GetScheduleData()` rather than geometry parameters.
  - Use case: rebar bar bending schedules where the BBS schedule is
    authoritative.
- Tag pipeline write-back:
  - `TagPipelineHelper.RunFullPipeline` gains an optional step 10:
    `WriteCostParams(doc, el, takeoffRule, rate)` — guarded by
    `project_config.json` flag `WRITE_COST_ON_TAG=true` (default
    false; opt-in to avoid surprise transactions).

**Acceptance criteria:**

- A wall costed via BOQ shows the same per-unit rate when surfaced in
  the 4D task that places it.
- Switching the wall to a new type that has a different rate updates
  both the BOQ row and the 4D cost projection on next workflow run.
- Removing `Scheduling4DEngine.DefaultCostRates` does not regress any
  existing 4D test.

---

### P4 — NRM1 elemental cost plan (1 sprint)

**Goal:** Early-stage (RIBA 1–3) cost planning before there's a
detailed model. Drives £/m² GIFA elemental benchmarks.

**Deliverables:**

- New folder `StingTools/Core/CostPlan/`:
  - `NrmElement.cs` — NRM1 element tree (1 Substructure, 2.1 Frame,
    2.2 Upper floors, 2.5 External walls, 3.1 Wall finishes, …)
  - `CostPlanLine.cs` — element × quantity × benchmark £/unit
  - `CostPlanRegistry.cs` — loads `STING_NRM1_BENCHMARKS.csv`
- New data file `StingTools/Data/STING_NRM1_BENCHMARKS.csv`:
  - Building type × element × £/m² GIFA + low/likely/high band +
    source (BCIS / RICS Building Cost Information Service)
  - Seed with ~30 building types (office, school, hospital,
    residential, healthcare clean room, lab, …)
- New commands:
  - `CostPlan_Create` — picks building type + GIFA target + risk %
  - `CostPlan_Compare` — compares cost plan vs. live BOQ once model
    matures (gap analysis)
  - `CostPlan_Export` — RICS-format elemental cost report (.xlsx +
    .docx via Template Engine)
- New mobile screen `Planscape/app/cost-plan/` — read-only KPI cards.
- Server entity `CostPlan` with elements + benchmarks frozen at issue.

**Acceptance criteria:**

- A QS can produce a cost plan from `GIFA = 8,500 m²,
  building_type = office_cat_B` in under five clicks.
- Once detailed BOQ exists, `CostPlan_Compare` shows variance per NRM1
  element with traffic-light status.

---

### P5 — Contract administration (2 sprints)

**Goal:** Payment certificates + variations + Earned Value Management.
This is where a QS spends 60–70% of billable hours on a live project.

#### P5.1 — Payment certificates

- New folder `StingTools/Core/PaymentCert/`:
  - `ScheduleOfValues.cs` — line items with `% Complete` + retention %
  - `PaymentCertEngine.cs` — `qty × rate × %complete − retention
    − previous certs` per JCT 2024 / NEC4 / FIDIC Red Book 2017
  - `RetentionLedger.cs` — withheld + released over time
- New shared params:
  - `ASS_PMT_PCT_COMPLETE_NR`
  - `ASS_PMT_CERT_NO_NR`
  - `ASS_PMT_CERT_DATE_DT`
  - `ASS_PMT_LAST_VALUED_DT`
- New template `Docs/_template_sources/payment_certificate.docx`
  (uses existing Template Engine v1.1; tokens for cert number,
  period, gross/net amount, retention, deductions, signature block).
- New commands:
  - `PaymentCert_Issue` — produces certificate doc + workflow start
  - `PaymentCert_Approve` — contractor accept / dispute
  - `PaymentCert_Register` — historic register export
- Server entity `PaymentCertificate` with status machine `Draft →
  Issued → Disputed → Agreed → Paid` and audit trail.

#### P5.2 — Variations / change orders

- New folder `StingTools/Core/Variation/`:
  - `VariationEngine.cs` — turns `BOQSnapshotDiff` clusters into
    numbered `VariationInstruction` documents
  - `StarRateBuilder.cs` — when diff contains items with no matching
    BOQ rate, build a star rate from first principles
    (labour + plant + materials + overhead + profit)
  - `ClaimsTemplate.cs` — EOT cost claim per JCT / NEC4 / FIDIC
- New shared params (extend existing `ASS_VAR_*`):
  - `ASS_VAR_NO_TXT`
  - `ASS_VAR_INSTRUCTION_DT`
  - `ASS_VAR_VALUATION_UGX_NR` (until P0 currency-neutral lands)
- New template `Docs/_template_sources/variation_instruction.docx`.
- New commands:
  - `Variation_FromDiff` — mints VOs from `BOQSnapshotDiff` output
  - `Variation_BuildStarRate` — interactive rate-build dialog
  - `Variation_ExportRegister`
- Server side wires the existing `BoqVariation` state machine.
- Mobile gets a `variations/` screen for approval.

#### P5.3 — Earned Value Management

- New folder `StingTools/Core/Evm/`:
  - `EvmCalculator.cs` — BCWS (planned value), BCWP (earned value),
    ACWP (actual cost), CPI = BCWP/ACWP, SPI = BCWP/BCWS, CV, SV,
    EAC = BAC/CPI, ETC = EAC − ACWP, VAC = BAC − EAC
  - `ActualsImporter.cs` — imports `actuals.csv` (date, task, cost)
    from the contractor or accounting system
  - `EvmReport.cs` — S-curve + variance dashboard
- BCWS comes from `Scheduling4DEngine.GenerateCashFlow()` (after P3).
- BCWP comes from `BoqDocument.LineItems` × `% Complete`.
- ACWP comes from `actuals.csv` import (later: live SAP/Oracle
  connector).
- New commands:
  - `Evm_Calculate`
  - `Evm_ImportActuals`
  - `Evm_ExportReport`
- New mobile screen — EVM KPIs + S-curve.

**Acceptance criteria:**

- Issuing payment cert #4 for £125k correctly subtracts certs #1–3
  + retention + previous deductions to net amount on the rendered
  doc.
- Diff between snapshot Tender→Interim-1 produces a numbered VO list;
  each VO inherits its parent baseline.
- EVM dashboard shows CPI / SPI to two decimal places; matches a
  hand-calculation spreadsheet within 0.001.

---

### P6 — Multi-standard take-off (2 sprints)

**Goal:** CESMM4 (civil), POMI (international), ICMS3 (carbon-cost),
MMHW (highways) alongside NRM2.

**Deliverables:**

- New interface `StingTools/BOQ/IMeasurementStandard.cs`:
  ```
  public interface IMeasurementStandard
  {
      string Id { get; }     // "nrm2", "cesmm4", "pomi", "icms3", "mmhw"
      string Version { get; }
      string ClassifyRow(QuantityLine line);
      string BuildDescription(QuantityLine line, Element el);
      decimal ApplyDeductions(QuantityLine line);
      string PreferredUnit(string category);
  }
  ```
- Concrete: `Nrm2Standard` (extracted from current code),
  `Cesmm4Standard`, `PomiStandard`, `Icms3Standard`, `MmhwStandard`.
- Each ships its own:
  - Section / item code grammar
  - Description templates in `BOQ_DESCRIPTIONS_<std>.json`
  - Deduction rules (e.g. CESMM4 window/door opening deductions)
  - Unit conventions (CESMM4 brickwork in m², block in m³, formwork
    by perimeter not area)
- `BOQDocument` gains `MeasurementStandardId` field.
- BOQ export wizard adds a standard picker before paper-format
  picker.

**Bonus deliverable — reinforcement BBS:**

- New `Core/CostPlan/Reinforcement/` with BS 8666 bar-bending
  schedule generation, lap allowances + waste uplift via
  `WastePercent` field added in P0.

**Acceptance criteria:**

- Same demo project produces a BOQ in NRM2 and CESMM4 with
  format-correct section codes and descriptions.
- Switching standards is a single dropdown change, not a code path.

---

### P7 — Mobile write surface (1 sprint)

**Goal:** Field-level approvals — VOs and payment certs sign off from
the mobile app.

**Deliverables:**

- New screen `Planscape/app/variations/[id].tsx` — approve / reject /
  request more info, with attachments + comments.
- New screen `Planscape/app/payment-certs/[id].tsx` — contractor
  agree / dispute with signature pad.
- `cost-dashboard.tsx` upgraded with live KPIs (BCWP vs BCWS, CPI,
  SPI), drill-down to per-discipline cost.
- Offline queue gains `APPROVE_VARIATION` and `SIGN_PAYMENT_CERT`
  action types (mirror existing `TRANSITION_CDE`).
- Biometric lock + push-notification + audit trail (use existing
  `secureStorage` + `notificationService`).
- Signature capture via `react-native-signature-canvas` saved as PNG
  + uploaded as `DocumentRecord` attachment.

**Acceptance criteria:**

- A site engineer can approve a £15k variation from their phone
  offline; it syncs and shows `Approved` in the desktop plugin
  within 30 s of reconnection.
- Payment cert signature appears as embedded PNG in the rendered
  `payment_certificate.docx`.

---

### P8 — External integrations + IFC Qto_* + ICMS3 (1–2 sprints)

#### P8.1 — Rate connectors

- New providers under `StingTools/BOQ/Providers/`:
  - `BcisHttpRateProvider` — BCIS Online Service REST API (UK)
  - `SponsRateProvider` — Spon's Civil + Architectural Price Books
    (CSV import; HTTP if licensed)
  - `CesmmRateProvider` — CESMM4 reference rates
  - `ProjectRateCardProvider` — `<project>/_BIM_COORD/rate_card.json`
    (the corporate project-specific rate book)
- All register through `RateProviderRegistry` (P0 unblocks).
- Live rates cached for `RateCacheTtl` minutes (config) +
  fallback to last good value when offline.

#### P8.2 — IFC4 `Qto_*` round-trip

- New writer `StingTools/BOQ/IfcQuantitySetWriter.cs`:
  - On IFC export, populate `Qto_WallBaseQuantities`,
    `Qto_BeamBaseQuantities`, `Qto_SlabBaseQuantities`,
    `Qto_SpaceBaseQuantities` (etc.) from BOQ quantities.
  - Add an STING-specific property set
    `Pset_StingCost{UnitRate, Currency, TotalCost, ProvisionalSum}` so
    other cost tools can read prices directly.
- Hooks into existing IFC export in `Temp/DataPipelineCommands.cs`
  (`ExportIfcCommand`) — runs as a post-process before file close.

#### P8.3 — ICMS3 single ledger

- Join `SustainabilityEngine` carbon output with BOQ rows:
  - Each `QuantityLine` gains `EmbodiedCarbonKgCo2e` +
    `WholeLifeCarbonKgCo2e`.
  - Per ICMS3 grouping: cost + carbon in one row.
- New report `ICMS3_Report` — cost £ and kgCO₂e side by side per
  ICMS3 group code (01 Acquisition, 02 Construction,
  03 Operation, 04 End-of-life).
- Already half-built: `SustainabilityEngine` produces carbon;
  `BOQCostManager` produces cost. Just need the join.

**Acceptance criteria:**

- IFC export opened in Cost-X or CostOS shows populated
  `IfcElementQuantity` and `Pset_StingCost` data — no re-measurement
  needed.
- BCIS rate lookup for `WallStandardCase` returns current TPI-adjusted
  rate within 500 ms cached, 3 s live.
- ICMS3 report shows £ + kgCO₂e against the same row.

---

## 5. Data contracts (new + extended)

### 5.1 New shared parameters (UUIDv5 in cost namespace `b9d4e1a2-7c63-4f89-9e0a-1f5a2c8b3d40`)

| Name | Type | Group | Container | Purpose |
|---|---|---|---|---|
| `ASS_CST_UNIT_RATE_NR` | Number | Cost | Instance | Currency-neutral rate (P0) |
| `ASS_CST_CURRENCY_TXT` | Text | Cost | Instance | ISO 4217 (P0) |
| `ASS_CST_FX_TO_BASE_NR` | Number | Cost | Instance | Conversion to base currency (P0) |
| `ASS_CST_FX_DATE_DT` | Date | Cost | Instance | FX fix date (P0) |
| `ASS_CST_AS_OF_DT` | Date | Cost | Instance | Pricing ref date (P0) |
| `ASS_CST_STALE_BOOL` | Yes/No | Cost | Instance | IUpdater stale flag (P2) |
| `ASS_CST_STALE_REASON_TXT` | Text | Cost | Instance | Why stale (P2) |
| `ASS_PMT_PCT_COMPLETE_NR` | Number | Payment | Instance | % done (P5) |
| `ASS_PMT_CERT_NO_NR` | Number | Payment | Instance | Last cert no (P5) |
| `ASS_PMT_CERT_DATE_DT` | Date | Payment | Instance | Last cert date (P5) |
| `ASS_VAR_NO_TXT` | Text | Variation | Instance | VO number (P5.2) |
| `ASS_VAR_INSTRUCTION_DT` | Date | Variation | Instance | VO issued (P5.2) |

### 5.2 New data files

| Path | Purpose | Phase |
|---|---|---|
| `Data/STING_TAKEOFF_RULES.json` | Data-driven take-off | P0 |
| `Data/STING_NRM1_BENCHMARKS.csv` | NRM1 elemental £/m² GIFA | P4 |
| `Data/BOQ_DESCRIPTIONS_CESMM4.json` | CESMM4 narratives | P6 |
| `Data/BOQ_DESCRIPTIONS_POMI.json` | POMI narratives | P6 |
| `Data/BOQ_DESCRIPTIONS_ICMS3.json` | ICMS3 narratives | P6 |
| `Data/BOQ_DESCRIPTIONS_MMHW.json` | MMHW narratives | P6 |
| `Data/WORKFLOW_BOQ_FullRefresh.json` | Chained refresh preset | P2 |
| `Data/WORKFLOW_BOQ_QuickValuation.json` | Monthly valuation preset | P2 |
| `Data/WORKFLOW_BOQ_TenderPack.json` | Tender doc pack preset | P2 |

### 5.3 New embedded document templates

| Path | Purpose | Phase |
|---|---|---|
| `Docs/_template_sources/payment_certificate.docx` | JCT/NEC4/FIDIC payment cert | P5.1 |
| `Docs/_template_sources/variation_instruction.docx` | VO doc | P5.2 |
| `Docs/_template_sources/star_rate_buildup.xlsx` | Star rate worksheet | P5.2 |
| `Docs/_template_sources/cost_plan_nrm1.xlsx` | NRM1 elemental cost plan | P4 |
| `Docs/_template_sources/evm_report.xlsx` | EVM dashboard export | P5.3 |
| `Docs/_template_sources/final_account.docx` | Final account agreement | P5 |

### 5.4 New server entities

| Entity | Phase | Notes |
|---|---|---|
| `BoqLineTaskLink` | P3 | Many-to-many `QuantityLine` ↔ `Scheduling4DTask` |
| `CostPlan` | P4 | NRM1 elemental cost plan, parent of `CostPlanLine` |
| `CostPlanLine` | P4 | Element × benchmark × low/likely/high |
| `PaymentCertificate` | P5.1 | Parent of `PaymentCertLine` |
| `PaymentCertLine` | P5.1 | SOV line × % complete × valuation |
| `Retention` | P5.1 | Withheld + released ledger |
| `StarRate` | P5.2 | Labour + plant + materials build-up |
| `EvmReport` | P5.3 | BCWS / BCWP / ACWP / CPI / SPI / EAC per period |
| `RateCacheEntry` | P8.1 | External rate cache with TTL |

### 5.5 New SignalR events

| Event | Phase |
|---|---|
| `boq.snapshot.synced` | P1 |
| `boq.variation.approved` | P1 |
| `cost.stale.detected` | P2 |
| `payment.cert.issued` | P5.1 |
| `payment.cert.signed` | P5.1 |
| `evm.report.published` | P5.3 |

---

## 6. UI surfaces

### 6.1 Dock panel

- New `COST` tab on `StingDockPanel.xaml` (between `BIM` and `TAGS`)
  with sub-tabs:
  - **Estimate** — NRM1 cost plan (P4) + benchmark drill-down
  - **BOQ** — current document, line items, validators, workflow
    presets (P0–P3)
  - **Valuation** — payment certs, retention ledger, SOV (P5.1)
  - **Variations** — VO list, star rates, claims (P5.2)
  - **EVM** — KPI tiles, S-curve, variance (P5.3)
  - **Settings** — rate providers, measurement standard,
    currency / FX, validators (P0, P6)

### 6.2 BIM Coordination Centre

- New `Cost` tab on `BIMCoordinationCenter` showing:
  - Live BOQ grand total + per-discipline split
  - Top 10 stale-cost elements
  - Variation backlog by status
  - Last payment cert + days since issue
  - CPI / SPI gauge

### 6.3 Mobile

- New tabs as described in P7.

---

## 7. Workflow presets (P2)

### 7.1 `WORKFLOW_BOQ_FullRefresh.json`

```
{
  "id": "boq-full-refresh",
  "name": "BOQ — Full refresh and publish",
  "steps": [
    { "command": "Cost_ClearStale" },
    { "command": "Cost_ValidateAll", "haltOnError": true },
    { "command": "TakeoffRules_Reload" },
    { "command": "BOQ_Build" },
    { "command": "BOQ_ApplyRates" },
    { "command": "BOQ_Snapshot",
      "args": { "label": "Auto refresh {datetime}", "type": "Interim" } },
    { "command": "BOQ_Export", "args": { "format": "xlsx" } },
    { "command": "BOQ_PushToServer", "requiresOnline": false }
  ]
}
```

### 7.2 `WORKFLOW_BOQ_QuickValuation.json`

```
{
  "id": "boq-quick-valuation",
  "name": "Monthly valuation",
  "steps": [
    { "command": "Cost_RefreshPercentComplete" },
    { "command": "PaymentCert_Issue" },
    { "command": "Evm_Calculate" },
    { "command": "Evm_ExportReport", "args": { "to": "_BIM_COORD/evm/" } },
    { "command": "Mobile_NotifyContractor" }
  ]
}
```

### 7.3 `WORKFLOW_BOQ_TenderPack.json`

```
{
  "id": "boq-tender-pack",
  "name": "Tender pack production",
  "steps": [
    { "command": "Cost_ValidateAll", "haltOnError": true },
    { "command": "BOQ_BuildUnpriced" },
    { "command": "BOQ_ExportPreambles" },
    { "command": "Drawings_ExportRegister" },
    { "command": "Docs_RenderTemplate",
      "args": { "template": "form_of_tender" } },
    { "command": "Docs_Package",
      "args": { "out": "_BIM_COORD/tender/{date}/" } }
  ]
}
```

---

## 8. Acceptance / definition of done

Per phase, all of:

- Unit tests for new core engines (rate provider, take-off rule,
  payment cert, EVM calculator) — fixture project under
  `Tests/Cost/`.
- Integration test running the workflow preset end-to-end on the
  demo `.rvt` (Tests/Integration/CostWorkflow_*).
- Server side: `dotnet test` against `Planscape.API` controller
  endpoints; EF migration committed and `dotnet ef database update`
  run on the demo dev DB.
- Mobile screens have at least one Detox / Maestro flow test.
- Docs: each phase appends a section to `docs/CHANGELOG.md` and ticks
  the relevant gap row in `docs/ROADMAP.md`.
- Backward compat: a project saved before the phase opens without
  param-binding errors; legacy `_UGX_NR` params resolve via
  derivation.

---

## 9. Risks and mitigations

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R-1 | Param GUID churn breaks existing schedules | M | H | Keep legacy params as derived; one-time migration; param-registry version bump |
| R-2 | Server sync conflict on simultaneous edits | M | M | Snapshot checksum + 409 conflict + manual resolve UI (already pattern for `BoqVariation`) |
| R-3 | External rate APIs (BCIS, Spon's) unavailable offline | H | L | Cache TTL + last-good fallback (already implemented in `OfflineQueue`) |
| R-4 | IUpdater cost-stale flood on bulk geometry edits | M | M | 20-element-per-trigger guard mirroring `StingStaleMarker`; LRU eviction |
| R-5 | Measurement-standard description rendering edge cases | M | M | Template Engine `{{token}}` failure → fall back to NRM2; warn loudly in `BoqDocument.Warnings` |
| R-6 | EVM actuals format varies wildly (SAP, Oracle, Xero, CSV) | H | M | Start with CSV; add adapters per format on demand; isolate behind `IActualsImporter` |
| R-7 | Mobile signature legal validity varies by jurisdiction | M | L | Store signature image + timestamp + GPS + biometric proof of intent; don't claim ESIGN/eIDAS compliance until counsel review |
| R-8 | Build hasn't been verified on Linux sandbox (no Revit API there) | H | M | `// TODO-VERIFY-API` markers on every new Revit-API call; CI on Windows runner mandatory before merge |

---

## 10. Out of scope (deferred to v2)

- ERP integration (SAP, Oracle E-Business Suite) — beyond CSV
  actuals import
- Multi-tier sub-contractor cost roll-up (general contractor vs
  sub-contractor split)
- BIM Track / Aconex cost-module integration (separate research
  task)
- Forward FX hedging analysis
- Insurance / bond cost modelling
- Probabilistic cost forecasting (Monte Carlo on risk allowances)
- Live tender / e-auction integration
- ASMM (Australia) and SMM7 legacy UK — not in initial standard
  set; add on demand

---

## 11. Owner workflow walk-throughs

### 11.1 RIBA 1–3 (Concept → Spatial coordination) — QS produces a cost plan

1. Open project in Revit. Run `CostPlan_Create`.
2. Pick building type (`office_cat_B`), enter target GIFA (8,500 m²).
3. STING reads `STING_NRM1_BENCHMARKS.csv`, builds elemental plan
   with low/likely/high bands per element.
4. QS adjusts overrides per element (e.g. raise external wall
   benchmark for bronze-anodised façade).
5. Run `CostPlan_Export` → `cost_plan_nrm1.xlsx` + signed PDF via
   Template Engine.
6. Snapshot pushed to server as `CostPlan` entity; mobile shows live
   KPI tiles.

### 11.2 RIBA 4 (Tender) — QS produces the BOQ

1. QS runs `Cost_ValidateAll` — must be clean (zero errors) before
   `BOQ_Build`.
2. Run `WORKFLOW_BOQ_TenderPack` preset → produces priced + un-priced
   BOQ, preambles, drawings register, form of tender, all bundled.
3. Tender bundle stamped with checksum; pushed to server `BoqBaseline`
   as `Kind = "Tender"`.
4. Contractors download tender pack; STING never sees their pricing
   (offline negotiation).
5. Winning bidder's priced BOQ is imported back via Excel round-trip
   into the same `BoqDocument`; status `Tender → Contract`.

### 11.3 RIBA 5 (Construction) — QS values monthly

1. End of month: foreman updates `ASS_PMT_PCT_COMPLETE_NR` on tagged
   elements via mobile app (offline-friendly).
2. QS runs `WORKFLOW_BOQ_QuickValuation` on Friday.
3. Workflow: refresh % complete → issue cert #N (computes net
   amount including retention deduction) → calculate EVM → export
   report.
4. Cert sent via Template Engine workflow `payment_cert_default`
   (state machine: Issued → Disputed | Agreed → Paid).
5. Contractor signs in mobile app; signature embedded into final
   PDF; cert state `Agreed`.
6. EVM dashboard tile turns amber if CPI < 0.95 — early warning of
   cost overrun.

### 11.4 RIBA 5 — VO arrives mid-project

1. Architect changes wall thickness; `StingCostStaleMarker` IUpdater
   flags affected walls `ASS_CST_STALE_BOOL = 1` automatically.
2. QS sees stale-cost alert in BIM Coordination Centre.
3. QS runs `BOQ_Snapshot` (label "Pre-VO") then accepts the
   geometry change.
4. Runs `BOQ_Build` again; `BOQSnapshotDiff` produces a cluster.
5. Runs `Variation_FromDiff` → mints VO-007 with status `Draft`.
6. STING tries `RateProviderRegistry.Resolve()`; finds no match for
   the new wall composition → `Variation_BuildStarRate` opens for
   labour + plant + materials + overhead + profit.
7. VO doc rendered + pushed to server; site team approves on mobile
   (offline-friendly); state `Draft → Submitted → Approved →
   Incorporated`.
8. Next payment cert includes the VO line.

### 11.5 RIBA 6 (Handover) — Final account

1. QS runs `BOQ_Snapshot` (label "Final Account").
2. Compares against `Tender` baseline + every approved VO; produces
   final-account reconciliation report.
3. Releases half of retention via `Retention_Release` (P5.1).
4. Final account signed by both parties via mobile signature.
5. EVM closed; final EAC vs BAC variance archived.

---

## 12. Suggested sprint plan (12 sprints, ~26 weeks)

| Sprint | Phase | Focus |
|---|---|---|
| 1 | P0 | `IRateProvider` + registry + provider extraction |
| 2 | P0 | Data-driven take-off rules + currency-neutral params + Extensible Storage extension |
| 3 | P1 | Server sync + checksums + variation linkage |
| 4 | P2 | IUpdater + validators + workflow presets |
| 5 | P3 | 4D/5D unification + schedule integration + tag pipeline write-back |
| 6 | P4 | NRM1 cost plan engine + benchmarks + cost-plan-to-BOQ compare |
| 7 | P5.1 | Payment certs + retention ledger + JCT/NEC4 templates |
| 8 | P5.2 | Variation engine + star rates + claims template |
| 9 | P5.3 | EVM calculator + actuals import + S-curve dashboard |
| 10 | P6 | CESMM4 + POMI + ICMS3 + MMHW measurement standards |
| 11 | P7 | Mobile approval flows + signature capture |
| 12 | P8 | BCIS / Spon's connectors + IFC `Qto_*` + ICMS3 ledger |

P0–P3 are critical-path enabling refactors. If a release cut is
needed earlier, ship P0–P3 + P5.1 (payment certs) as v1; defer P4,
P5.2, P5.3, P6, P7, P8 to v2.

---

## 13. Open questions

1. **Currency base** — single base currency per tenant (e.g. UGX) or
   per project? Single per project is simpler; tenant-level
   consolidation needs FX conversion on read.
2. **NEC4 vs JCT 2024 default** — which payment-cert template ships
   as default? Recommend NEC4 (more common on UK public sector +
   East African Community projects).
3. **Star-rate ownership** — should star rates be project-scoped or
   tenant-scoped? Project-scoped is safer (no cross-project leak)
   but a corporate library is convenient. Two-tier: corporate
   baseline + project overrides (same pattern as DrawingTypes).
4. **EVM time bucket** — weekly or monthly? Monthly aligns with
   payment certs; weekly gives earlier warnings. Recommend
   monthly default, configurable.
5. **Variation rate hierarchy** — for VOs, prefer (a) BOQ rate
   for similar item, (b) BOQ rate ±%, (c) star rate from first
   principles, (d) dayworks. Confirm with QS stakeholders before
   coding `StarRateBuilder`.
6. **Should the BIM Coordination Centre Cost tab replace the dock
   panel COST tab?** Or complement? Recommend both — dock panel
   for active QS work, BCC for project-overview rollup.

---

## 14. References

- RICS — New Rules of Measurement (NRM1, NRM2, NRM3)
- ICE — Civil Engineering Standard Method of Measurement (CESMM4)
- RICS — Principles of Measurement (International) (POMI)
- ICMS Coalition — International Cost Management Standard 3 (ICMS3)
- Highways England — Method of Measurement for Highway Works (MMHW)
- ISO 15686-5:2017 — Buildings and constructed assets — Service
  life planning — Part 5: Life-cycle costing
- ISO 19650-2:2018 — Organization and digitization of information
  about buildings and civil engineering works
- BS 8666:2020 — Scheduling, dimensioning, bending and cutting of
  steel reinforcement for concrete
- JCT 2024 Standard Building Contract
- NEC4 Engineering and Construction Contract
- FIDIC Conditions of Contract for Construction (Red Book), 2017
- BCIS — Building Cost Information Service
- Spon's Price Books — A&B Civil + Architects'

---

**End of plan.**
