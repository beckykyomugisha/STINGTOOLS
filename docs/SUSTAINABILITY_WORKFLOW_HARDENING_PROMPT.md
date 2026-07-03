# Implementation Brief — Sustainability / EDGE Workflow Hardening (SUS-1 … SUS-7)

> **For the implementing agent.** Self-contained spec from a deep two-part audit
> (engine internals + workflow/integration) of the StingTools Phase-195
> sustainability subsystem, June 2026. Read it fully before touching code.
>
> **Read this first — the subsystem is already very good.** It is the
> best-engineered module in the codebase: honesty-first, fully data-driven, with
> correct EDGE semantics. The audit's dominant conclusion is *do not break what
> works*. Each work package therefore lists an explicit **DO-NOT-TOUCH** set.
> The gaps are almost all at the **edges** — getting data *into* the EDGE App and
> *out* as auditor evidence — plus two physics fixes for the actual deployment
> latitude band and one carbon-engine reconciliation.
>
> All file:line references were valid at commit `732d2ca6a` (the carbon path is
> under active edit on `claude/boq-round3` — **re-verify every reference before
> editing**, and coordinate SUS-2 with that work).

---

## 0. Mission & context

StingTools' sustainability subsystem (`Core/Sustainability/` ~45 files +
`Commands/Sustainability/` + a dockable "♻ STING Sustainability" panel) is an
EDGE/LEED assessment engine: operational energy (EN ISO 13790 monthly balance),
water, embodied carbon + embodied energy, whole-life carbon, a scheme evaluator
with EDGE 20/20/20 gates, a least-cost "improve" loop, and a server snapshot/trend.

**Deployment reality:** Uganda / East-Africa practice (Planscape), where **EDGE
(IFC / World-Bank route) is the primary green-certification path**. Hot-climate,
often intermittently-occupied buildings. Uganda grid ≈ 0.05 kgCO₂e/kWh (hydro).
Climate band ~0–5°N (and the practice targets the wider sub-Saharan / southern
band). The module must produce numbers that *feed the EDGE App* and *survive an
EDGE auditor's desk review*, not replace them.

**The EDGE process (confirmed):** build/self-assess in the **EDGE App** → ≥20%
savings in **energy, water, AND embodied energy in materials** vs a local
baseline → register → **Design Audit (desk review of self-assessment + evidence)
→ Preliminary Certificate** → **Post-Construction Site Audit (visual + measured
water flow rates) → Final Certificate**. StingTools is a **feeder** to that
process: it pre-computes indicative savings and builds the business case for
reaching a level; the EDGE App owns the certified figure.

**Guiding principle:** StingTools owns **model-anchored measurement + the
improve-loop economics** at authoring time; the **EDGE App owns certification**.
The job of this brief is to make the *handoff in both directions* real (input
pack to the App, evidence pack to the auditor) and to fix the physics/integration
seams — without weakening the "never fabricate a pass" discipline.

### Revit-API reality the implementation must respect
- All model reads/writes on the main thread (single-threaded API). Dashboard runs
  already use the `ExternalEvent` + 60 s stale-cache + force-refresh pattern
  (mirrors `ComplianceScan`) — keep it. **No timer-thread model access.**
- Reports/exports (XLSX/CSV/HTML/PDF) and schedules are in-process-feasible
  (`ViewSchedule`, file IO, `ClosedXML`, MiniWord). Server push is background
  HTTP with results marshalled back via `ExternalEvent`.
- A blocking `.Wait(15s)` on the UI external event (SUS-6) is exactly the
  anti-pattern to remove.

---

## 1. What is already correct — DO NOT regress

Verified strengths. Treat these as load-bearing; changes must preserve them:

| Strength | Where |
|---|---|
| **"Never fabricate a pass from missing data"** — `Computed` gates; a not-computed gate can never pass; readiness gate forces `NotEvaluated` when location/use unset | `SchemeEvaluator.cs` (`gr.Passed = gr.Computed && Compare(...)`), `SustainabilityEngine.cs:299-316` |
| **Data-driven everything** — schemes/baselines/grid/water/embodied factors JSON + `<project>/_BIM_COORD/sustainability/` override merge; evaluator never names a scheme | `SustainabilityRegistries.cs:46-112`, `Data/STING_GREEN_*.json` |
| **Divide-by-zero centralised** (zero GIFA / zero baseline / NaN → "not meaningful", not garbage) | `SustainSavings.cs:17-23` |
| **EDGE materials gate = embodied ENERGY (MJ), delegated to EDGE App**, never conflated with GWP; carbon→LEED only | `STING_GREEN_SCHEMES.json:29`, `IMetricProvider.cs:124-137` |
| **EDGE-official round-trip** (user types certified % back, un-delegates the gate) | `SchemeEvaluator.cs:56-60` |
| **Two-tier caching** with deterministic `ContentHash` (energy-only edits don't re-walk model) | `SustainabilityEngine.cs:83-132`, `SustainProjectSetup.cs:233-289` |
| **WBLCA category scoping** instead of all-element sweep; PV-before-carbon; electricity-vs-fuel carbon separation | `SustainabilityEngine.cs:1049-1095`, `SupplyAndGenerationLayer.cs:44-80` |
| **New EDGE engine shares the BOQ `CarbonFactorResolver` + waste + biogenic split** | `SustainabilityEngine.cs:1120`, `SustainMaterialCarbon` |
| **Honesty-banner UX + readiness pre-warn on document open + stale `IUpdater` + auto-snapshot trend** | `SustainabilityCommands.cs:251-275`, `SustainStaleUpdater.cs`, `StingToolsApp.cs:626-652` |
| **Improve-loop** (least-cost `Sustain_TargetSeeker`, NPV `Sustain_LccBenefit`, `Sustain_CompareOptions`) | `SustainDeliverableCommands.cs:158`, `SustainExportCommands.cs:417-434` |

**Conventions (read `CLAUDE.md`):** `ParameterHelpers.GetDoc(commandData)`;
`[Transaction]` attributes; named `Transaction`; `StingLog` not silent catch;
`TaskDialog` not `MessageBox`; new behaviour → JSON under `Data/` with project
override; reuse `StingResultPanel`/`StingExportDialog`/etc; build clean with
`dotnet build … -p:RevitApiPath=…`; additive JSON fields only (no snapshot
regression); one branch per WP off latest `main`; commit trailer
`Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## 2. Findings by dimension (evidence)

### Accuracy
- **[MED] `opFrac` post-multiply wrongly discounts solar/conduction cooling load.** `AnnualEnergyEstimator.cs:289` scales the *whole* annual cooling sum by occupancy fraction, so a 250-day office's solar-driven cooling is cut to ~0.68× though solar enters the fabric 365 days/yr. **Understates cooling EUI for hot-climate, intermittently-occupied buildings — the EDGE target market.** Only internal-gain + DHW components should be calendar-scaled; conduction/solar should not. (The EN ISO 13790 monthly method itself, lines 326-332, is correct — don't touch that.)
- **[MED] `VerticalSolarFactor` is latitude-blind and assumes equator-faces-south.** `AnnualEnergyEstimator.cs:340-344` mis-assigns façade solar for southern-hemisphere / sub-Saharan sites (equator-facing façade is *north*). Latitude is already carried by `MonthlyClimateSynthesizer.Fill` — thread it in. **Biggest physics gap for the deployment band.**
- **[LOW] DHW ΔT hardcoded 30 K** (`AnnualEnergyEstimator.cs:316`) overstates DHW for tropical cold-feed (~20 K); matters for hotel/healthcare where DHW dominates.
- **[LOW] EDGE ZeroCarbon ties with Advanced.** `SchemeEvaluator.cs:201` ranks levels by `Values.Max()`; Advanced {40,20,20} and ZeroCarbon {40,20,20} both max at 40, so the reported level between them is dictionary-order-dependent and ZeroCarbon's real differentiator (net-zero operational carbon) isn't encoded as a gate.

### Integration
- **[MED] Two whole-life carbon numbers that disagree.** `V6/CarbonStageTracker.cs` (EN 15978 A1–C4) walks `SharedParamGuids.AllCategoryEnums`; the EDGE engine walks `WblcaCategories` (`SustainabilityEngine.cs:1049-1057`). Same model → different aggregate take-off, so `CarbonStageTracker.TotalA1A3` ≠ `res.Materials.TotalCarbonKg`, despite the `WholeLifeCarbon.cs:9-12` comment claiming they "agree on basis." They share the per-element resolver + 60-yr period, **not** the take-off scope.
- **[LOW/MED] EDGE engine re-collects material volumes** instead of reading the BOQ takeoff quantities — shares the resolver, not the quantities; double take-off + drift risk vs `TakeoffRule.cs`.

### Alignment (EDGE handoff)
- **[MED — highest leverage] EDGE export is a results dump, not EDGE-App-importable inputs.** `SustainExportCommands.cs:72-176` exports computed kWh/EUI/savings; the EDGE App's Design tab wants **measures**: WWR, glazing U-value & SHGC, wall/roof U-values, lighting controls, AC COP, per-fixture flow/flush rates. The engine already holds all of it (envelope from `ConstructionProfile`, WWR from `SumGlazingAreaM2`, flows from `ReadDesignFixtureFlows`) — it never reaches the workbook, so the user re-keys everything into EDGE by hand.
- **[MED] No certification-evidence artifact** (fixture schedule + cut-sheet refs, envelope spec table, EPD register) for the Design Audit; and **no HTML/PDF report at all** — the weakest output of any STING dashboard, though the most report-worthy.
- **[MED, documented] LEED is an explicit stub** (`SustainPhase2Commands.cs:77-78`).

### Automation
- **[MED] No scheduled re-assessment** — snapshot only on a manual Dashboard click; stale flag fires but nobody re-runs.
- **[MED] KUT monthly KPI report doesn't pull the EDGE snapshot** — two separate logs (`KutKpiDashboardCommand` vs `edge_kpi_log.jsonl`).
- **[MED] `Sustain_PublishToServer` blocks the UI thread** with `.Wait(15s)` (`SustainPublishToServerCommand.cs:102`); not auto-pushed after a run.

### Flexibility / UX
- **[MED] Dispatch mismatch.** The dock-panel handler `StingSustainabilityCommandHandler.cs:44-63` wires 10 tags but is **missing** `Sustain_ReadinessCheck`, `Sustain_GenerateDeliverable`, `Sustain_CompareOptions`, `Sustain_TargetSeeker`; those exist only via `WorkflowEngine.ResolveCommand` (`Core/WorkflowEngine.cs:2040-2043`). The improve-loop's best commands may have **no panel button**. Verify the XAML button set.
- **[LOW] In-code magic numbers** that break the otherwise-total data-driven posture: DHW ΔT + the `1.16` constant (`:316`), `VerticalSolarFactor` coeffs `0.27/0.35` (`:343`), and the `IndicativeClassFactors` table (`SustainMaterialCarbon.cs:99-118`) — promote the class-factor table to JSON.
- **[LOW] `LoadZone` defaults are office-biased** (`LoadInputs.cs:38-46`); a zone that fails profile resolution silently inherits office physics (surfaced as a note — honest, but worth a louder flag).

### Robustness / efficiency
- **[MED] Malformed override JSON is silently swallowed** — `GridCarbonRegistry.SafeRead` (`:70`) and every registry's `Apply` (`catch { return; }`, `:76`); a broken project override vanishes and the in-code default (e.g. grid 0.45) masquerades as a real factor with no warning.
- **[MED] Material sub-cache key omits carbon-factor content** (`SustainabilityEngine.cs:1066-1069`) — re-stamping an EPD and re-running within the 60 s window (without force-refresh) serves stale carbon; secondary consumers (export/LCC) reusing the cache are exposed.
- **[LOW] Per-run fresh collectors** outside the material cache: `ResolveUse` (Rooms, `:378-386`), `HasPlumbingFixtures`, `HasActiveClimateSite` run on every `Compute`.
- **[LOW] No schema-version gate** — data files carry `"schema": ".../v1"` but nothing reads/validates it; snapshots have no version field.

---

## 3. Work packages

Sequenced by leverage. SUS-1 and SUS-2 are the headline value; the rest harden.
One branch per WP off latest `main` (e.g. `claude/sus1-edge-input-pack`).
**SUS-2 must be coordinated with the live carbon work on `claude/boq-round3`.**

### SUS-1 — EDGE-App input pack (highest leverage)
Branch `claude/sus1-edge-input-pack`. Turn the export from a results dump into an
**EDGE-App-ready measure pack** + auditor-grade evidence.
- Extend `SustainExportCommands.cs` to emit the **design measures** the EDGE App
  consumes: WWR per orientation, glazing U-value & SHGC, wall/roof/floor U-values,
  lighting power/controls, AC system type + COP, and **per-fixture flow/flush
  rates** — all already available from `ConstructionProfile`, `SumGlazingAreaM2`,
  `ReadDesignFixtureFlows`. Lay the workbook out to mirror the EDGE App Design-tab
  fields so a user transcribes, not re-derives.
- Add an **evidence pack**: a fixture schedule (with a cut-sheet-ref column), an
  envelope spec table, and an **EPD register** (from `boq_epd_map.json`) — the
  artifacts an EDGE Design Auditor expects.
- Keep every figure labelled indicative; keep the materials-delegation note.
**Done = a user can open the workbook beside the EDGE App and fill its Design tab
without re-deriving anything, and hand the evidence sheets to an auditor.**

### SUS-2 — Reconcile the two whole-life carbon engines
Branch `claude/sus2-carbon-reconcile`. **Coordinate with `boq-round3`.**
- Make `V6/CarbonStageTracker` and the EDGE engine agree on **one take-off scope**
  (either both use `WblcaCategories`, or both read the BOQ takeoff quantities).
  Have the EDGE engine **read BOQ takeoff quantities** where a BOQ exists rather
  than re-collecting volumes, falling back to its own collector only when no BOQ.
- Make `WholeLifeCarbon.cs:9-12`'s "agree on basis" claim **true** — the two
  surfaced whole-life numbers must match for the same model.
**Done = EDGE dashboard A1–A3 == RIBA-stage tracker A1–A3 for the same model.**

### SUS-3 — Hot-climate accuracy fixes
Branch `claude/sus3-accuracy`.
- Thread **site latitude** into `VerticalSolarFactor` and fix the equator-facing
  assumption (handle southern hemisphere) — `AnnualEnergyEstimator.cs:340`.
- Stop discounting **solar/conduction cooling load** by `opFrac`; calendar-scale
  only internal-gain + DHW components — `:289`.
- Move DHW ΔT to JSON (per-climate cold-feed temp) instead of hardcoded 30 K — `:316`.
- Validate against a hand-calc for a Kampala office before/after (cooling EUI
  should rise for the intermittently-occupied, solar-dominated case).
**Done = cooling EUI no longer falls with occupancy-day count for a fixed fabric;
façade solar correct by hemisphere.**

### SUS-4 — Report → certify completion
Branch `claude/sus4-reporting`.
- Add an **HTML/PDF EDGE/whole-life report** (mirror `HealthDashboardEngine`'s
  HTML export pattern) — operational/embodied/water savings, level achieved,
  improve-loop recommendations, honesty caveats.
- Wire the LEED scorecard stub (`SustainPhase2Commands.cs`) far enough to produce
  the **WBLCA A1–A3 prerequisite report** (LEED v5 MR) from the already-computed
  carbon — or clearly fence it as out of scope and remove the dead button.
**Done = one click produces a shareable report; report→certify no longer
dead-ends at an XLSX dump.**

### SUS-5 — Dispatch parity, override-failure surfacing, magic-number cleanup
Branch `claude/sus5-ux-robustness`.
- Add the missing tags (`Sustain_TargetSeeker`, `Sustain_CompareOptions`,
  `Sustain_GenerateDeliverable`, `Sustain_ReadinessCheck`) to
  `StingSustainabilityCommandHandler.cs:44` and confirm panel buttons exist; add
  `Sustain_PublishToServer` to `WorkflowEngine.ResolveCommand` for symmetry.
- Replace the silent `catch { return; }` in every registry `Apply`/`SafeRead`
  with a `StingLog.Warn` + a user-visible "your override failed to load — using
  default" banner.
- Promote `IndicativeClassFactors` (`SustainMaterialCarbon.cs:99`) and the
  `VerticalSolarFactor` coefficients to JSON for consistency.
**Done = best improve-loop commands reachable from the panel; no silent
override-load fallback.**

### SUS-6 — Automation polish
Branch `claude/sus6-automation`.
- Make `Sustain_PublishToServer` **non-blocking** (background HTTP, no UI `.Wait`).
- Fold the latest `EdgeKpiSnapshot` into the **KUT monthly KPI report** so the
  management report carries the EDGE trend.
- Optional: a scheduled / morning-health re-assessment hook so the snapshot trend
  doesn't gap when nobody clicks Dashboard.
- Include carbon-factor content (EPD-map / material-param content hash) in the
  **material sub-cache key** so export/LCC can't serve stale carbon (`:1066`).

### SUS-7 — Robustness tail
Branch `claude/sus7-robustness`.
- Add a **schema-version gate** that reads the `"schema"` field on data/override
  files and warns on mismatch; add a version field to snapshots.
- Give EDGE **ZeroCarbon a distinct gate** (net-zero operational carbon) so it
  isn't a rank-tie with Advanced — `SchemeEvaluator.cs:201`, `STING_GREEN_SCHEMES.json`.
- Cache `ResolveUse`/`HasPlumbingFixtures`/`HasActiveClimateSite` results behind
  the run cache so they don't re-collect every `Compute`.
- Make the `LoadZone` office-bias fallback a louder warning (`SustainabilityEngine.cs:667`).

---

## 4. Acceptance narrative

When SUS-1…SUS-7 land, an EDGE assessor on a Kampala project should be able to:
set scheme/level/country on the panel → run the dashboard (with correct
hot-climate cooling and hemisphere-correct façade solar) → use the improve-loop
to find the least-cost route to EDGE Advanced with NPV payback → export an
**EDGE-App-ready input pack** and transcribe it straight into the EDGE App Design
tab → hand the **evidence pack** (fixture schedule + envelope spec + EPD register)
to the Design Auditor → publish a **formatted HTML/PDF report** to the client →
and see the EDGE KPI flow into the monthly BIM report — with **one** reconciled
whole-life carbon number across the EDGE dashboard and the RIBA-stage tracker, and
no silent fallbacks masquerading as real factors. **If any step still needs a
tool outside StingTools + the EDGE App, the owning WP is incomplete.**

---

## 5. Out of scope (deliberately not built)
- Replacing the **EDGE App** — StingTools is a feeder; the App owns the certified
  number and the baseline. Do not try to reproduce EDGE's certified calculation.
- A full anisotropic solar transposition / hourly energy model — the monthly
  quasi-steady method is the right fidelity for an EDGE feeder; SUS-3 fixes the
  latitude/occupancy errors within that method, not the method itself.
- LEED full certification end-to-end — SUS-4 produces the WBLCA prerequisite or
  fences LEED explicitly; the full LEED submission is a separate programme.

---

### Appendix — research basis
Consolidates: an engine-internals audit (energy/water/materials math, flexibility,
performance, robustness, EDGE-gate correctness) and a workflow/integration audit
(EDGE App alignment, the two-carbon-subsystem question, dashboard/KPI/server
integration, automation, design→certify completeness), cross-checked against the
USGBC EDGE certification process, the Sintali EDGE guide, and the EDGE App
Design-tab guidance. All file:line references valid at `732d2ca6a` — **re-verify
before editing; coordinate SUS-2 with the live carbon work on `claude/boq-round3`.**
