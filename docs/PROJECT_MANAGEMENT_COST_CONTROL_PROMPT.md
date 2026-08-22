# Implementation Brief — Project-Management & Cost-Control Completion (PM-1 … PM-8)

> **For the implementing agent.** This is a self-contained spec born of a deep
> research + code-audit pass (June 2026). Read it fully before touching code.
> It targets one outcome: **a project manager / quantity surveyor can control a
> project's cost and delivery from first estimate to final account entirely
> within StingTools + Planscape, without reaching for a tool StingTools lacks.**
>
> Every finding below was produced by reading the *actual* code — file:line
> references are given. **Verify each reference before editing** (line numbers
> drift); the audit was taken at commit `55ac92fe3`. Do **not** assume a finding
> still holds without opening the file.
>
> This brief does **not** ask you to rebuild a scheduling engine (P6) or a cloud
> cost platform (ACC) inside Revit. It asks you to (1) fix correctness bugs,
> (2) wire the existing silos together, (3) fill the genuine QS/PM gaps, and
> (4) correct the carbon/EDGE/Uganda defaults — each on the correct side of the
> Revit API line.

---

## 0. Mission & context

StingTools already contains a **substantial** PM/cost platform — it is not
greenfield. The audit confirmed solid, production-grade engines for: BOQ/5D cost
(`BOQCostManager`), EVM (`Core/Evm`), payment certificates (`Core/PaymentCert`),
variations (`Core/Variation`), 4D scheduling (`SchedulingCommands` + `FourdGanttReader`),
clash + issues (`Clash/`, `IssueTrackerDashboard`), transmittals/CDE
(`TransmittalOrchestrator`, `DeliverableLifecycle`), a workflow engine + 40
presets, a 13-tab BIM Coordination Center, server-side meetings/issues/workflows
(`Planscape.Server`), and a full Phase-195 EDGE/LEED sustainability engine
(`Core/Sustainability/`).

**The problem is not absence — it is correctness, integration, and finish.** The
modules largely work in isolation but do not pass data to one another, several
carry real arithmetic bugs, and a handful of QS lifecycle stages are missing.

**Guiding principle (unchanged from the BOQ briefs):** the model owns
*quantities*; the QS owns *rates and commercial judgement*; the cloud
(Planscape) owns *multi-party, time-phased, contractual state*. Revit's job is
**model-anchored capture at authoring/measurement time**; everything contractual
and longitudinal is brokered by the server.

**Deployment reality:** Uganda / East-Africa practice (Planscape). Dual currency
**UGX/USD** (NOT GBP). VAT **18%** (Uganda), not 20%. High-inflation market →
**fluctuations matter**. Intermittent connectivity → **offline-tolerant +
Excel/CSV + QuickBooks/Sage export** matter more than a live ACC connector.
Measurement: **NRM2** primary (NRM1 cost plan is a gap — see PM-3). ISO 19650
naming throughout. Carbon defaults must follow **EDGE methodology + Uganda grid
(0.05 kgCO2e/kWh)**, not UK/ICE defaults.

### Revit-API reality the implementation must respect

The single hardest constraint: **the Revit API is single-threaded and never
thread-safe.** All model reads/writes run on Revit's main thread inside a valid
API context (command, `IExternalEventHandler`, or `ExternalEvent.Raise()`). This
dictates where each PM feature can live:

| Capability | Verdict | API reason |
|---|---|---|
| Stamp cost/carbon/date/status/WBS on elements (shared params + Extensible Storage) | **In-process ✓** | `ExtensibleStorage.Schema` (16 MB/string cap — keep light); `Core/Storage/` already does this |
| Generate ViewSchedules, sheets, revisions, PDF/DWG/IFC/NWC exports | **In-process ✓** | `ViewSchedule`, `Revision`, `Document.Export` (NWC gated on `OptionalFunctionalityUtils.IsNavisworksExporterAvailable()`) |
| WPF Gantt / kanban / dashboard / S-curve **rendering** in a dockable pane | **In-process ✓** | Pure WPF; you already ship 13-tab panels. All model writes via `ExternalEvent`, never a timer thread |
| Read worksets/owners for a coordination view | **In-process ✓ (advisory)** | `WorksharingUtils` — but data is cached/**stale**; cannot be an authoritative lock |
| Background HTTP sync to Planscape, file watchers | **In-process ✓** | OK on worker threads; **funnel all model writes through `ExternalEvent`** (existing `BoqSyncCoordinator`/StingBridge pattern) |
| Read **ViewSchedule row data** as a live source | **Avoid** | No row-data API — `ViewSchedule.Export()`-and-parse only. Compute PM metrics from elements/ES instead |
| CPM/Gantt **scheduling engine**, 4D time simulation | **In your code or hand off** | `Phase` is an ordered enum with **no dates/dependencies**. The *engine* (forward/backward pass) is yours to write or delegate to P6/Asta/SYNCHRO; Revit gives you zero scheduling math |
| Real-time multi-user task boards, presence, locking, audit history, attachments | **Planscape only** | No real-time collaboration API; ES perf degrades with size |
| eTransmit-style packaging | **DIY in-process** | eTransmit has no public API — build your own collector (you already do) |

**Do NOT build inside Revit:** a CPM solver UI replacing P6, resource leveling,
or the contractual valuation/pay-cycle. Build the *measurement + capture* hooks
in Revit; the *engine and ledger* in Planscape; export to P6/Excel/QuickBooks.

---

## 1. Current state — verified strengths (do not re-discover; do verify before editing)

| Concern | File(s) | Status |
|---|---|---|
| 5D BOQ build | `BOQ/BOQCostManager.cs` (`BuildBOQDocument`, `DeriveQuantity`, `GroupIntoSections`, `AssignBoqLineRefs`) | Solid; bugs in §2 |
| Rate provider chain | `BOQ/Rates/` (`RateProviders`, `RateProviderRegistry`, `MaterialLibraryRateProvider`, `Providers/`) | **Clean & extensible — model for new work** |
| Markup waterfall | `BOQ/BOQModels.cs` `BoqTotals.Compute` | Single canonical source — good |
| EVM | `Core/Evm/EvmCalculator.cs` | Works; EAC/BAC bugs in §2 |
| Payment certs | `Core/PaymentCert/` | Works; VAT/SOV/retention bugs in §2 |
| Variations / VO | `Core/Variation/`, WP4a VO approval | Works; not wired to certs/EVM (§3) |
| Final account + tender adjudication | `Commands/Cost/FinalAccountCommands.cs` (WP4a) | **Genuine strength** — but ignores cert series (§3) |
| Carbon (fossil + biogenic split) | `BOQ/CarbonFactorResolver.cs`, `BiogenicCarbon.cs`, `Data/STING_CARBON_FACTORS_UG.json` | **Methodologically correct**; B6/benchmark bugs in §4 |
| EDGE/LEED engine | `Core/Sustainability/` (~45 files), `Data/STING_GREEN_SCHEMES.json`, `GridCarbonRegistry` | **EDGE 20/20/20 byte-exact; Uganda grid correct** — see §4 |
| 4D scheduling | `BIMManager/SchedulingCommands.cs`, `V6/FourdGanttReader.cs` | Foundation; no CPM/float, parser bugs (§3) |
| Workflow + presets | `Core/WorkflowEngine.cs`, `Docs/Workflow/`, `Data/WORKFLOW_*.json` | Solid; naming collision + SLA bug (§2/§3) |
| Dashboards | `UI/BIMCoordinationCenter.cs`, `SchedulingCostDashboard.cs`, `IssueTrackerDashboard.cs` | Solid; data-source inconsistencies (§3) |
| Server | `Planscape.Server` Issues/Meetings/Transmittals/Workflows/Compliance controllers | Solid |

**Mandatory conventions (read `CLAUDE.md` §"Conventions for AI Assistants"):**
- Doc acquisition: `var doc = ParameterHelpers.GetDoc(commandData);` (dock-panel commands receive **null** `ExternalCommandData`).
- `[Transaction(TransactionMode.Manual)]` for mutating, `ReadOnly` for diagnostics; wrap DB writes in a named `Transaction` (`"STING …"`).
- `StingLog.Info/Warn/Error` — **no silent catches**. `TaskDialog`, not `MessageBox`.
- Data-driven: new behaviour → JSON/CSV under `StingTools/Data/` with a project override under `<project>/_BIM_COORD/`; corporate baseline stays pristine; project edits flip `origin` via checksum drift (mirror existing registries).
- Reuse UI hosts: `StingListPicker`, `StingResultPanel`, `StingDataGridDialog`, `StingProgressDialog`, `StingExportDialog`.
- **Build to verify:** `dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"` → 0 errors before each commit (note in commit if the sandbox cannot build).
- **No snapshot regressions** — additive JSON model fields only, with safe defaults so old `deliverables.json`/snapshot/VO/cert JSON still deserialises.
- **Branch hygiene:** one branch per work package off latest `main` (e.g. `claude/pm1-cost-correctness`). Commit logically; do not push/PR unless asked. End commit messages with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## 2. Findings — CORRECTNESS BUGS (fix first; highest commercial risk)

Each is a real defect found in code. Severity in brackets. **Verify line, then fix.**

### Cost / EVM / Cert
- **[HIGH] EVM EAC collapses to 0 when CPI=0.** `Core/Evm/EvmCalculator.cs:51-53` — `Eac => Cpi > 0 ? Bac/Cpi : 0`. At project start (AC>0, EV=0 → CPI=0), EAC=0, ETC goes **negative**, VAC falsely reads full-BAC "on budget" exactly when a PM needs the forecast. **Fix:** `EAC = AC + (BAC−EV)` fallback when CPI=0; add the `AC + (BAC−EV)/(CPI×SPI)` schedule-blended variant; derive ETC from the chosen EAC.
- **[HIGH] EVM BAC is re-derived from the *live* BOQ every run, not a frozen baseline.** `Commands/Cost/VariationAndEvmCommands.cs:632,638` — `bac = boq.GrandTotalUGX`; as the model grows, BAC/EV/PV drift with zero progress. **Fix:** anchor BAC on the frozen contract sum (the FinalAccount/AFC code already resolves one — `FinalAccountCommands.cs:156-167`) plus approved variations.
- **[HIGH] Cost-plan vs BOQ variance compares GBP to UGX with no FX.** `Commands/Cost/CostPlanCommands.cs:212-217` diffs `CostPlanDocument.TotalLikely` (defaults **GBP**, `Core/CostPlan/CostPlanLine.cs:64`) against UGX BOQ totals — every variance is ~3700× wrong. **Fix:** default cost plan to project currency (UGX); FX-convert before any cross-document diff.
- **[HIGH] `AssignBoqLineRefs` middle segment is a hard-coded constant.** `BOQCostManager.cs:3437-3441` — `string sectionIndex = "1";` never incremented, so every ref is `{prefix}.1.{n}`; the hierarchy the format promises is dead, and the wrong ref is **stamped onto elements** (`ASS_BOQ_LINE_REF`). **Fix:** increment per section group.
- **[MED] Cert VAT defaults to UK 20% on Uganda projects.** `Core/PaymentCert/PaymentCertModels.cs:93` (`VatPercent = 20.0`); `PaymentCertEngine.CreateDraft` never sets it from the project. **Fix:** seed VAT from project/BOQ (18%).
- **[MED] Cert SOV omits prelims/OH&P/contingency.** `PaymentCertEngine.SovFromSnapshot:100-111` sets `ContractValue = BOQSection.TotalUGX` (works-only, ex-markup), while AFC/FinalAccount use grossed-up `GrandTotalUGX` — two contract bases for one project. **Fix:** value certs against the same grossed-up basis (or explicitly carry prelims as an SOV line).
- **[MED] Cert progress vs valuation inconsistent.** `PaymentCertModels.cs:113-122` — `OverallPercentComplete` excludes MaterialsOnSite while `GrossThisCert` (`:52`) includes it; retention-halving keys off the MoS-excluding %. **Fix:** reconcile the two.
- **[MED] PERT mean computed from pre-rounded line totals.** `Core/CostPlan/CostPlanLine.cs:41-46`. **Fix:** weight then round.
- **[MED] `StarRateLine.LineTotal` uses `Max(Hours,Quantity)`.** `Core/Variation/VariationModels.cs:237` — silently mis-costs a line with both fields set. **Fix:** select by resource type, not `Max`.
- **[LOW] Retention never halves** — `EffectiveRetentionPercent` default `HalfRetentionAtPercent = 100.0` makes the feature inert (`PaymentCertModels.cs:126`). **Fix:** default to practical-completion trigger, not 100%.

### Scheduling / Workflow
- **[HIGH] SLA deadlines fire at the wrong time (timezone).** `Docs/Workflow/WorkflowEngine.cs:128` parses the `…Z` UTC deadline with `DateTime.TryParse` (no `AssumeUniversal`) → `Local` kind, compared against `DateTime.UtcNow`. In Kampala (UTC+3) breaches fire **3h early**. (`AuditLog.cs:111` does it correctly — copy that.) Same defect `:114`. **Fix:** `DateTimeStyles.AssumeUniversal | AdjustToUniversal`.
- **[HIGH] Two MS Project duration parsers disagree.** `SchedulingCommands.cs:587` (`XmlConvert.ToTimeSpan / 8h`) vs `FourdGanttReader.cs:51` (derives from Start/Finish only) — same `.xml`, different durations; neither applies the project calendar. **Fix:** converge on one parser.
- **[MED] P6 predecessor links dropped → no schedule logic.** `SchedulingCommands.cs:546` (`predecessors = []`), `FourdGanttReader.ParsePrimaveraXer:84` never reads `TASKPRED`. Without this, CPM/float (PM-4) is impossible. **Fix:** parse the relationship table.
- **[MED] XER date parse over-strict, silently drops rows.** `FourdGanttReader.cs:103-106` — only `yyyy-MM-dd HH:mm`/`yyyy-MM-dd`; real XER often has seconds. **Fix:** add formats + warn on skip.
- **[MED] % complete normalized in one of three import paths only.** `SchedulingCommands.cs:532` rescales 0..1→0..100 for P6 but MSP-XML (`FourdGanttReader.cs:67-69`) and XER do not. **Fix:** normalize once, centrally.
- **[MED] Working-calendar is computed but never used; UK bank holidays hard-wired.** `SchedulingCommands.cs:2241-2284` (Computus) feeds only a CSV; `AutoGenerateSchedule` (`:328,349`) uses calendar-days and only nudges weekend end-dates. Wrong holiday set for Uganda. **Fix:** feed a Uganda working-calendar into schedule generation.
- **[LOW] Silent quantity fabrication in 5D estimate.** `SchedulingCommands.cs:900` defaults rebar `0.888 kg/m`; `qty += 1` fallbacks (`:866,878,890`) substitute "1 unit" when geometry missing, not surfaced in `skipped`. **Fix:** surface as low-confidence.

### Robustness
- **[MED] Silent `catch {}` swallow cert write failures.** `PaymentCertEngine.cs:224,235,246`; `CostControlCommands.cs:119`. **Fix:** `StingLog.Warn` with the reason.
- **[MED] Non-atomic cert save (delete-then-write).** `PaymentCertCommands.cs:254-255` — crash between = lost certificate; filename embeds `ValuationDate` so re-save can orphan/duplicate. **Fix:** write-temp-then-rename; stable filename keyed on cert id.
- **[MED] Snapshot hasher drops zero-valued lines.** `BoqSnapshotHasher.cs:40` (`DefaultValueHandling.Ignore`) — an unmeasured (qty 0) row is invisible to the checksum/dedupe. **Fix:** include zeros in the canonical JSON.
- **[MED] EVM actuals CSV header heuristic can mis-count.** `EvmCalculator.cs:114-131` — silent `continue` on parse failure under-reports ACWP with no warning. **Fix:** explicit header flag + surfaced parse-error count.
- **[LOW] Money math is all `double`.** UGX billions × thousands of lines accumulates float residue; rounding masks it. **Consider** `decimal` for money totals (scoped, optional).

---

## 3. Findings — INTEGRATION & AUTOMATION GAPS (the silos)

The modules work alone but **do not pass data to each other**. This is the
central structural gap — a PM must hand-re-key the same numbers between stages.

- **[HIGH] CostPlan → budget → EVM PV is unwired.** EVM BAC comes from the live BOQ, never the cost plan's `GrandTotalLikely`; nothing seeds `PROJECT_BUDGET_UGX` from the plan. (`CostPlanEngine` NRM1→NRM2 map is one-way, hard-coded.)
- **[HIGH] Variation → Cert → EVM is unwired.** An approved VO (`VariationApproveCommand`) adds no SOV line, adjusts no contract value (`PaymentCertEngine.CreateDraft`), and never moves EVM BAC (`VariationAndEvmCommands.cs:632`). A £500k VO is invisible to the next valuation and the forecast. Only the read-only AFC/FinalAccount reports see variations.
- **[HIGH] No schedule-driven cash-flow / S-curve.** `GenerateCashFlow` (`SchedulingCommands.cs:1036,1061-1093`) spreads the **grand total only** over a fixed sigmoid — ignores per-task start/finish/value, so a front-loaded and a back-loaded programme give the *same* curve. EVM PV is hand-keyed (`VariationAndEvmCommands.cs:634-637` "No 4D wiring yet"). There is no true time-phased Planned Value.
- **[HIGH] Clash → Issue → Transmittal chain not wired.** `ClashSlaIntegration.CreateIssues` builds in-memory `CoordIssue` that flow to BCF only (`ClashRunCommand.cs:252,287`); clashes never reach `issues.json`; nothing escalates or bundles into a transmittal.
- **[HIGH] Issue-status enum diverges across four subsystems.** `"OPEN"` (BIMManager) vs `"Open"` (Clash `ClashSlaIntegration.cs:55-57`) vs `"open"` (ACC `AccIssueSync.cs:62`) vs `"Resolved"/"Void"` (KPI `KutKpiDashboardCommand.cs:185-188`). The workflow gate `has_open_issues` matches only `"OPEN"` (`Core/WorkflowEngine.cs:688`) → never sees clash/ACC issues. **Fix:** one shared `IssueStatus` normalizer + reconcile `clashes.json` ↔ `issues.json`.
- **[MED] FinalAccount/AFC compute "contract sum" from different sources** (`FinalAccountCommands.cs:156-167` vs `CostControlCommands.cs:332-345`) → two final numbers for one project. FinalAccount also ignores the cert series entirely (uses config/snapshot, not certified-to-date).
- **[MED] PaymentCert carry-forward keyed by free-text section string** (`PaymentCertEngine.CreateDraft:57-60`) — renaming a section silently resets cumulative valuation to 0 and over-pays. **Fix:** element-id/line-id binding for cumulative valuation.
- **[MED] KPI dashboard has honesty gaps** — three headline KPIs are literal strings, not computed (`KutKpiDashboardCommand.cs:347-349`); no auto-snapshot (manual trigger only).
- **[MED] MIDP/TIDP exists only as a CSV template, not data/engine** — deliverable lifecycle isn't joined to a delivery *plan*, so "is delivery X on programme?" is unanswerable.

**Automation a PM/QS currently does by hand that should be automated:** cost
plan → budget → EVM PV; VO approval → next cert SOV line; cash-flow generation;
retention half-release at PC; schedule % complete from model state; clash →
tracked issue with SLA; KPI fortnightly snapshot; MIDP drift detection.

---

## 4. Findings — MATERIAL / CARBON vs EDGE & UGANDA

The carbon system is **largely correct and EDGE-aware** — the Phase-195 engine
(`Core/Sustainability/`) implements EDGE 20/20/20 byte-exact, keeps embodied
**energy (MJ)** separate from **carbon (kgCO2e)** as EDGE requires, and resolves
the **Uganda grid at 0.05 kgCO2e/kWh** (`GridCarbonRegistry`). Biogenic carbon
is handled per RICS WLCA (reported separately, not netted). **Do not "fix" these.**

The defects are in the **older BOQ/V6 carbon path** that bypasses the new engine:

| Item | StingTools value | Expected (EDGE/Uganda) | Action |
|---|---|---|---|
| **B6 operational factor** | `0.233 kgCO2e/kWh` **hard-coded** (`V6/CarbonStageTracker.cs:54`) | Uganda 0.05 (already in `GridCarbonRegistry`) | **[HIGH]** Route B6 through `GridCarbonRegistry` — currently ~**5× too high** for Uganda |
| **Benchmarks in ISO-14064 export** | LETI 625 / RIBA 300 kgCO2e/m² hard-coded (`CarbonStageTracker.cs:206`) | UK targets, inappropriate | **[HIGH]** Replace with project/region targets or remove |
| **Ugandan fired-clay brick** | 350 kgCO2e/m³ | Clamp-fired artisan brick ≈17 MJ/kg (5.7× generic) → likely understated; block (140) correctly < brick | **[MED]** Raise UG brick factor + note clamp-kiln biomass; reinforce "block greener than brick locally" |
| **Per-material waste** | single `COST_DEFAULT_WASTE_PCT` knob (`WasteFactor.cs`) | NRM2: rebar ~2.5%, masonry ~5%, timber ~10%, concrete ~5% | **[MED]** Add a per-material/category waste table |
| **Green baselines** | no Uganda rows; Kampala falls back to 0A hot-humid proxy | Kampala ~1200 m → behaves ~2A/3A, much milder | **[MED]** Add UG/Kampala baseline rows (`STING_GREEN_BASELINES.json`) |
| **Two carbon subsystems** | BOQ path (kgCO2e/m³) + Phase-195 engine (MJ + carbon) reconciled only partly | One source of truth | **[LOW]** Consolidate grid factor + benchmarks on `GridCarbonRegistry` everywhere |
| **Stainless steel** | plain-steel carbon (`MATERIAL_LOOKUP.csv`) | ~6.15 kg/kg (≈4× higher) | **[LOW]** Correct factor |

**Already correct (keep):** EDGE 20/20/20 thresholds; EDGE-as-energy (MJ) with
materials gate delegated to EDGE app; Uganda grid 0.05; CEM I high-clinker
concrete + imported-primary steel/aluminium provenance; biogenic separate-line.

---

## 5. PM cost-control lifecycle — coverage matrix & missing capabilities

The benchmark: **a PM must be tooled for every stage from feasibility to final
account.** Mapping the 8-stage QS/PM journey (RICS NRM + RICS Black Book) against
StingTools, and where each belongs relative to the Revit API line:

| Stage | Belongs | StingTools today | Gap |
|---|---|---|---|
| 1. Order of cost estimate / feasibility (NRM1, £/m² GIFA) | QS tool / Revit GIFA export | GIFA schedules exist | **No OCE / benchmark workflow** → PM-3 |
| 2. Elemental cost plan (NRM1) | Hybrid (Revit-assisted, QS-owned) | `Core/CostPlan` exists but **GBP silo, NRM2-led, no FX** | **NRM1 elemental structure + currency fix** → PM-1/PM-3 |
| 3. NRM2 BoQ / tender pricing | Revit authoring-time ✓ | **Strong** (BOQ export, tender adjudication WP4a) | OK |
| 4. Contract sum / baseline budget | Cloud + Revit stamp | CostStamp/budget basis exists | **Frozen baseline → EVM not wired** → PM-2 |
| 5. Cost control (valuations, retention, variations, dayworks, PC/provisional, fluctuations, L&E) | Cloud + QS tool; Revit = VO remeasure | Certs ✓, variations ✓, retention partial | **Variation→cert unwired; retention release; dayworks; fluctuations; L&E** → PM-2/PM-3 |
| 6. Forecasting (CTC / AFC / EVM / cash-flow S-curve) | Cloud / BI | EVM ✓ (buggy), AFC ✓ | **Schedule-driven S-curve; CTC at line level; EAC fix** → PM-1/PM-2 |
| 7. Final account / retention release | QS tool / cloud | FinalAccount reconciliation ✓ (WP4a) | **Ignores cert series; no retention-release ledger** → PM-2/PM-3 |
| 8. CVR + client cost report | Cloud / QS tool | — | **No CVR report** → PM-3 |

**"No PM should lack this" — capabilities entirely absent today:**
1. **Commitments register** (sub-contracts / POs linked to budget lines) — absent.
2. **Schedule-driven cash-flow S-curve** (planned vs actual cumulative) — synthetic only.
3. **Retention-release ledger** (withholds only today; no release entries).
4. **Fluctuations** (NEDO/BCIS index-linked) — flat config number only.
5. **Dayworks** sheet/build-up workflow — enum value exists, no workflow.
6. **Loss & expense / compensation events** — EOT days captured, no valuation.
7. **CVR** (cost-value reconciliation) — absent.
8. **NRM1 elemental cost plan + OCE** — NRM2-led only.
9. **Cost-to-complete at line level** — only the (broken) EVM aggregate.
10. **ERP/accounting export** (QuickBooks/Sage/Excel) — the Uganda-pragmatic bridge.

---

## 6. Work packages

Sequenced so correctness lands before integration before new features. Each WP is
its own branch off latest `main`. **PM-1 → PM-2 are mandatory before PM-3+**
(you cannot build forecasting on broken EVM or unwired silos).

### PM-1 — Cost & schedule correctness (fix the bugs in §2)
Branch `claude/pm1-correctness`. Fix, with a regression note per item:
- EVM EAC/CPI=0 guard + EAC variants + ETC (`EvmCalculator.cs:51-53`).
- EVM BAC anchored on frozen contract sum + approved variations (`VariationAndEvmCommands.cs:632`).
- CostPlan default currency = project (UGX) + FX before any diff (`CostPlanLine.cs:64`, `CostPlanCommands.cs:212-217`).
- `AssignBoqLineRefs` section increment (`BOQCostManager.cs:3437`).
- Cert VAT from project (`PaymentCertModels.cs:93`, `CreateDraft`).
- SLA timezone `AssumeUniversal` (`Docs/Workflow/WorkflowEngine.cs:114,128`).
- B6 grid factor via `GridCarbonRegistry`; remove UK benchmarks (`CarbonStageTracker.cs:54,206`).
- Cert atomic save; silent-catch logging; hasher zero-inclusion (§2 robustness).
**Done = each bug has a before/after and the build is clean.**

### PM-2 — Wire the silos (integration, §3)
Branch `claude/pm2-integration`. No new features — connect what exists:
- **CostPlan → budget → EVM PV**: cost plan `GrandTotalLikely` auto-seeds `PROJECT_BUDGET_UGX`; EVM reads it as BAC.
- **Variation → Cert → EVM**: approving a VO appends an "Adjustments / Variations" SOV section to the next cert and moves EVM BAC.
- **Clash → Issue → Transmittal**: clash run optionally raises tracked issues (one `IssueStatus` normalizer) into `issues.json` with SLA; reconcile `clashes.json` ↔ `issues.json`; `has_open_issues` sees all.
- **Unify the contract-sum source** so FinalAccount and AFC agree; FinalAccount reconciles against certified-to-date.
- **PaymentCert cumulative valuation** keyed by line/element id, not section string.

### PM-3 — Close the QS lifecycle gaps (§5 missing capabilities)
Branch `claude/pm3-lifecycle`. Each is model-anchored capture in Revit + engine/ledger in `Core/` (server later):
- **Schedule-driven cash-flow S-curve** — time-phase per-task value over its start/finish (needs PM-4 dates); planned vs actual cumulative; this becomes the real EVM PV.
- **Retention-release ledger** — release entries (half at PC, final at end-of-defects) alongside withholds (`PaymentCertEngine.ComputeLedger`).
- **Fluctuations engine** — index-linked (NEDO/BCIS-style) instead of the flat config number (`FinalAccountCommands.cs:185`).
- **Dayworks** — labour/plant/material build-up sheet using the existing `StarRate` model, surfaced as a `BOQRowSource.Dayworks` flow.
- **Loss & expense / compensation events** — valuation tied to the EOT days already captured on VOs.
- **CVR report** — cost vs value at a common cut-off (over/under-claim, WIP, margin).
- **NRM1 elemental cost plan + OCE** — map Revit categories → NRM1 element groups; £/m² (UGX/m²) GIFA benchmarking.
- **Cost-to-complete at line level**; **commitments register** (sub-contracts/POs → budget lines).
- **QuickBooks/Sage/Excel export** path (offline-first).

### PM-4 — Scheduling depth (CPM, the engine Revit won't give you)
Branch `claude/pm4-scheduling`. This is *your* engine (Revit has no scheduler):
- Converge the two MSP/XER parsers; **read P6 predecessors** (`TASKPRED`) and MSP links.
- **Forward/backward pass → critical path, total/free float.**
- **Baseline-vs-actual variance**; **model-driven % complete** (phase reached / elements placed vs planned — currently always 0, `SchedulingCommands.cs:391`).
- Uganda working-calendar fed into generation (not UK bank holidays).
- Keep exporting parameter-driven 4D to Navisworks/SYNCHRO/P6 — do **not** try to be P6.

### PM-5 — Carbon/EDGE/Uganda accuracy (§4)
Branch `claude/pm5-carbon`. Mostly data + one wiring fix:
- Per-material waste table; UG brick factor; Kampala/UG green-baseline rows; stainless factor; consolidate grid/benchmarks on `GridCarbonRegistry`. (B6/benchmark wiring already in PM-1.)

### PM-6 — Performance & robustness (§2 perf, §3 perf)
Branch `claude/pm6-perf`:
- Add `ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums)` to `AutoGenerateSchedule` (`:245`) and `GenerateCostEstimate` (`:760`) — the heaviest unfiltered UI-thread sweeps.
- Cache one `BuildBOQDocument(doc)` across cert→EVM→AFC in a command run (currently rebuilt 3×).
- Replace per-element `LookupParameter` sweeps in `WeightedPctComplete`/`AggregatePercentComplete` (`VariationAndEvmCommands.cs:718`, `PaymentCertCommands.cs:114`).
- `AuditLog` last-hash in memory (`:168`); `MilestoneRegister` collect-once (`:2129`); `VariationEngine.ListVariations` single-load (`:177`).

### PM-7 — Architecture hygiene
Branch `claude/pm7-hygiene`:
- **Rename one `WorkflowEngine`** — `Core` = preset runner, `Docs/Workflow` = doc state-machine; the name collision is worked around with a `using` alias (`TransmittalOrchestrator.cs:24-28`) and is a latent wrong-bind hazard. Suggest `PresetRunner` vs `DocWorkflowEngine`.
- Consolidate two `ContractForm` enums (`PaymentCertModels.cs:27` 3-value vs `VariationModels.cs:57` 8-value).
- De-duplicate `SuggestLiability` (`VariationAndEvmCommands.cs:353,1033`) and `MapProviderIdToLegacySource` (`BOQCostManager.cs:826`, `CostStamp.cs:227`).
- Unify CST_* param storage types (string vs number drift) and the three sidecar-folder root strategies (`_BIM_COORD` vs `STING_BIM_MANAGER` vs `_bim_manager`).

### PM-8 — Delivery-management layer (the ISO 19650 PM surface)
Branch `claude/pm8-delivery`. Thin in Revit, engine in Planscape:
- **MIDP/TIDP engine** — promote the CSV template to a tracked deliverable register joined to the lifecycle; stamp each sheet/container's suitability + delivery status; **drift detection** ("which deliverables are off-programme").
- **KPI time-series** — make `KutKpiDashboard` real (compute the three string-only KPIs; persist auto-snapshots server-side; trend multiple metrics).
- **Risk register** — the clearest missing PM primitive (only a narrow Lightning model exists). Data model + lifecycle in Planscape; thin "raise risk against this element/zone" hook in Revit, reusing issue/SLA/audit machinery.

---

## 7. How a PM uses the finished system (acceptance narrative)

When PM-1…PM-8 land, a Planscape PM/QS should be able to, **without leaving
StingTools + Planscape**: take a model at RIBA 2 → produce an NRM1 elemental cost
plan in UGX with GIFA benchmarks (PM-3) → that plan seeds the budget and EVM PV
(PM-2) → produce an NRM2 BoQ + tender pricing and adjudicate tenders (exists) →
freeze a contract-sum baseline (PM-2) → run monthly interim valuations + payment
certificates with correct VAT/retention (PM-1), with approved variations flowing
straight into the next cert and the forecast (PM-2) → track dayworks,
fluctuations, loss & expense (PM-3) → see a **schedule-driven cash-flow S-curve**
and correct EVM CPI/SPI/EAC/CTC (PM-1/PM-4) → run a CVR and client cost report
(PM-3) → close with a final account reconciled against certified-to-date and a
retention-release ledger (PM-2/PM-3) → with carbon reported on EDGE/Uganda
defaults throughout (PM-5) — and export the lot to QuickBooks/Sage/Excel offline
(PM-3). **If any of those steps still needs a tool outside StingTools, the work
package that owns it is incomplete.**

---

## 8. Out of scope (deliberately not built in Revit)

- A CPM **UI** replacing P6/Asta; resource leveling histograms — export to those tools instead.
- A live ACC/Procore connector as a dependency — Uganda connectivity is intermittent; Excel/CSV/QuickBooks first, ACC API as an *optional* later bridge.
- Real-time multi-user valuation/locking inside the model — Planscape is the system of record.
- True 4D animation/time simulation — parameter-driven export to Navisworks/SYNCHRO.

---

### Appendix — research basis

This brief consolidates: a full codebase PM inventory; a cost-engine code audit;
a scheduling/automation/dashboard code audit; a material/carbon-vs-EDGE/Uganda
audit; deep research on ACC Cost Management + the RICS NRM/Black Book cost
lifecycle; PM-platform feature landscape (ACC, Procore, Aconex, P6, Asta,
SYNCHRO, CostX/Candy); and Revit API feasibility limits. Sources are recorded in
the session that produced this document. All file:line references were valid at
commit `55ac92fe3` — **re-verify before editing.**
