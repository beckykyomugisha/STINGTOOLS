# BOQ & Cost Manager — Enhanced Rebuild: full gap-closure & 4D/5D consolidation

You are a terminal coding agent on the **StingTools** C# Revit plugin. This prompt
is the complete brief to evolve the BOQ & Cost Manager into the consolidated 4D/5D
cost-control workspace — fixing a critical stability bug, removing 4D/5D
duplication, and closing **every** gap across automation, integration, flexibility,
accuracy and performance. It is large and **phased**: do phases in order, one gap
per commit, compile-verify + commit + (on request) deploy each, and **never batch
several gaps into one unverified turn**. Pause after each phase for review.

---

## 0. Environment, build & deploy — READ FIRST (this has cost hours; do not skip)

- **Branch & checkout:** work in the **main checkout `C:\Dev\STINGTOOLS`** on
  **`claude/placement-centre-review-audit`** (the single unified branch — BOQ +
  placement + seed-family all live here). `git branch --show-current` to confirm.
  Do **not** create worktrees; do **not** run `deploy.bat`/`deploy-gold.bat` from
  any other checkout (that repoints Revit's addin and "loses" the build).
- **Live DLL path:** Revit loads the assembly in
  `%APPDATA%\Autodesk\Revit\Addins\2025\StingTools.addin`, which points at
  **`C:\Dev\STING_PLACEMENT_GOLD\StingTools.dll`** (an isolated GOLD folder).
  `deploy-gold.bat` (run from THIS checkout, **Revit closed**) builds + copies into
  GOLD + re-pins the addin. If a build "doesn't show up", FIRST
  `grep -i '<Assembly>' "$APPDATA/Autodesk/Revit/Addins/2025/StingTools.addin"` —
  don't assume the path.
- **Compile-verify (headless via Nice3point — MANDATORY before every commit):**
  `dotnet build StingTools/StingTools.csproj -c Release -t:Rebuild --nologo -v minimal` → `0 Error(s)`.
- **Git:** one gap per commit (imperative subject + `Co-Authored-By: Claude Opus
  4.8 <noreply@anthropic.com>`). **Do NOT push or merge.** Log each in
  `docs/CHANGELOG.md` with its **Revit smoke test** (the human runs it; you can't).

## 1. Conventions to obey (reuse, don't fork)

- **Single source of truth.** No parallel data stores for the same concept (see
  Phase 1 — there are currently TWO schedule stores; never add a third).
- **Inline, no nested message pumps.** Results render via `StingResultPanel`
  (`InlineSink`). **Do NOT use `Dispatcher.PushFrame` / `DispatcherFrame` inside an
  ExternalEvent** (that is the deadlock — Phase 0). Input is a **modal dialog** or
  an **inline form** that dispatches once; never a nested pump.
- **Reuse engines:** `BOQCostManager`, `RateProviderRegistry`/`IRateProvider`/
  `RateResult`, `Scheduling4DEngine`, `Core/Evm/EvmCalculator`, `CarbonFactorResolver`,
  `MeasurementStandards`, `WasteFactor`, `BoqSnapshotHasher`, `BoqSyncCoordinator`,
  `BcisHttpRateProvider`, `BOQBccBridge`, `StingProgressDialog`, `StingWizardDialog`.
- **Persistence:** project-scoped JSON under `<project>/_BIM_COORD/…`.
- **Data facts:** `BOQLineItem` has `RateUGX`, `RateSource`, `RateConfidence`,
  `Quantity`, `TotalUGX`, `NRM2Section`, `Source`, `SourceModel`, `Note`,
  `LabourUGX/PlantUGX/MaterialUGX` (G4). Dispatch is `StingDockPanel.DispatchCommand`
  → `SetCommand` + `ExternalEvent.Raise()` (one event at a time).

## 2. Architecture decision (consolidate, don't duplicate)

5D **cost-control** consolidates into the **BOQ Cost Manager** (cost-loaded
programme, EVM, cash-flow, certs, variations, cost reports). But the schedule
**engine + data are unified once** and surfaced as thin views: Cost Manager = the
cost/5D lens; **BCC's 4D/5D tab becomes a read-only summary / deep-link onto the
same model** (Phase 1). Pure 4D model-visualisation (task↔element, date-driven
colouring) is built once in the shared engine and shown where it fits.

---

# PHASE 0 — Stop the bleeding (stability) — DO FIRST, BLOCKS EVERYTHING

**Why:** confirmed deadlock. `StingListPicker.ShowInline` uses
`Dispatcher.PushFrame` (a nested message loop) **inside** the running
`ExternalEvent` handler; `TaskDialog`s pump too. With **no busy-guard**, clicking a
second action during the first re-enters → `Raise()` returns Pending, `SetCommand`
overwrites the tag, the event never idles → **every later button (incl. footer QTO
IFC) is dead**.

- **0.1 Dispatch busy-guard.** Add a panel-wide "command running" flag. Set true
  when an action is dispatched; ignore/disable new action clicks (Actions buttons +
  footer dispatches) until completion; reset on the command-completion hook
  (`PendingActionResolve` / `StingDockPanel.NotifyCommandComplete`). Visually grey
  the buttons while running. This alone stops the re-entry deadlock.
- **0.2 Remove the inline `DispatcherFrame` pump.** Delete `ShowInline` /
  `InlineHost` / `_inlineFrame` use from `StingListPicker`; `Show(...)` always uses
  modal `ShowDialog`. Input pickers become modal (stable, standard Revit). Results
  stay inline (`StingResultPanel.InlineSink` is safe — it sets a Border child, no
  pump). Keep `PendingActionResolve` so the pane never sticks on "Running…".
- **0.3 True no-popup input = inline forms (not pumps).** For the input-gathering
  commands that still pop a `TaskDialog` (Export QS Bill priced/unpriced, Issue Cert
  contract form, Variation pickers, etc.), migrate them to the **`ShowInlineForm`**
  pattern (render fields in the Actions pane → dispatch once with values via the
  ExtraParam contract → command reads them, skips its dialog; dialog kept as
  ribbon-caller fallback). This is the *only* safe zero-popup path.
- **0.4 Verify** (human): rapidly click 5 different actions in sequence, click a
  second while a modal is open, run QTO IFC after several actions — nothing hangs,
  the panel stays alive, results render inline.

**Acceptance:** no "lifeless panel" under rapid/sequential clicking; no nested
pump anywhere; inputs are modal or inline-form; results inline.

---

# PHASE 1 — Single source of truth: unify 4D/5D schedule

**Why:** there are **two** schedule systems that don't share state —
`Scheduling4DEngine` (`STING_BIM_MANAGER/schedule_4d.json`, used by BCC) and the
Cost Manager Schedule tab (`_BIM_COORD/boq_schedule.json` + `EvmCalculator`). Edit
one, the other is stale.

- **1.1 One `ScheduleModel` + one store.** Define a single persisted schedule model
  (`_BIM_COORD/schedule.json`) with tasks/phases (id, WBS, start, end,
  %complete, predecessors, costLoad, elementIds, milestone flag). Migrate both
  `schedule_4d.json` and `boq_schedule.json` into it (one-time importer that reads
  either legacy file). One engine façade wrapping `Scheduling4DEngine` +
  `EvmCalculator`.
- **1.2 Cost Manager owns the 5D view; BCC reads.** The Cost Manager Schedule tab
  reads/writes the unified model. **BCC's `Tab4D5D`** becomes a read-only summary
  (SPI/CPI, cash-flow, milestones) bound to the *same* model, with a "open in Cost
  Manager" deep-link. Remove BCC's independent schedule write paths.
- **1.3 Programme import.** Wire `Scheduling4DEngine.ImportMSProject` (and add P6
  XML + CSV) into the unified model so a real programme from MS Project / Primavera
  flows into the cost view. Import is **MSP/P6 XML** (state clearly: native `.mpp`
  needs a paid lib; XML export is the supported path).

**Acceptance:** editing a phase in either panel updates the other; one JSON store;
an MSP/P6 XML programme imports into the Schedule tab.

---

# PHASE 2 — Accuracy (the trust gap)

- **2.1 NRM2 rules-based measurement layer.** Today the take-off is **raw Revit
  geometry**, not NRM2-*measured* quantity. Add a measurement-rules layer
  (`Core`/`BOQ/MeasurementStandard`) that applies billing rules per item:
  deductions for openings only above a size threshold, girth/perimeter +
  corner allowances, lap/waste allowances, "no deduction for voids < 0.5 m²", board
  vs centre-line measurement, etc. Drive it from
  `Data/STING_MEASUREMENT_RULES.json` (per category × per standard). Show **measured
  qty vs model qty** with a flag where they differ, so it's auditable.
- **2.2 True standard re-measure.** `MeasurementStandards` (NRM2/SMM7/CESMM)
  currently mostly relabels; make Set Standard actually re-apply the standard's
  *measurement* + *grouping* + *unit* rules via 2.1's rule set.
- **2.3 Waste + confidence + aggregation correctness.** Extend `WasteFactor`
  coverage; calibrate `RateConfidence` (e.g. lower it when interpolated/defaulted,
  raise on QS-imported/actual-backed); ensure `AggregateLineItems` never merges
  items that should price separately (different rate/standard/spec) — key on the
  spec dimensions, not just category+size.

**Acceptance:** measured quantities reflect billing rules (with a model-vs-measured
audit), standard switching re-measures, aggregation is spec-correct.

---

# PHASE 3 — Efficiency & performance

- **3.1 Incremental take-off.** `RefreshAsync` rebuilds the whole bill every time.
  Add change-tracking (Revit `DocumentChanged` / element-version hashes) so a
  refresh re-measures only changed elements and reuses cached line items for the
  rest. Host take-off should not re-walk the entire model for a one-element edit.
- **3.2 True background rebuild.** Complete the deferred G8: read Revit data into
  POCOs **on the API thread**, run grouping/rate/aggregation **off-thread** with
  `StingProgressDialog` (cancellable), marshal the result back for `RefreshDisplay`.
  **Never call the Revit API off-thread.** If a clean split is infeasible for some
  path, keep that path synchronous-with-progress and document why.
- **3.3 Delta snapshots + caches.** Store snapshots as **deltas** vs a baseline
  (via `BoqSnapshotHasher`) instead of full copies, to bound `_BIM_COORD` growth.
  Cache the rate-provider lookups per (category, spec) within a build.

**Acceptance:** a one-element change refreshes fast (incremental); large-model
refresh runs off-thread without freezing; snapshots store deltas.

---

# PHASE 4 — Integration

- **4.1 BCIS live rates inline.** Surface `BcisHttpRateProvider` in the panel — a
  "fetch live rates" action + per-item BCIS source flag (needs API key in config;
  degrade gracefully without).
- **4.2 Planscape sync inline.** Surface `BoqSyncCoordinator` — push/pull status +
  buttons in the panel; show last-sync + conflicts inline.
- **4.3 Estimator return path.** Beyond the Excel round-trip, add an import for
  priced results from CostX / iTWO (CSV/IFC-back), joined on the GUID key.
- **4.4 ERP / accounting export.** Add cost export to Sage / QuickBooks / SAP CSV
  (cost codes + values + WBS), so cost leaves Revit/Excel for finance systems.
- **4.5 Carbon → live EPD/EC3.** Extend G5's EPD overrides to pull from an EC3 /
  EPD database (online lookup + cache), keep ICE fallback.
- **4.6 FF&E / classification.** Surface the existing Fohlio (`ExLink`) + CSI/
  Uniclass classification in the cost panel (a classification column + filter), so
  FF&E and spec classification ride the same bill.

**Acceptance:** each external surface is reachable + functional from the panel,
degrading gracefully when unconfigured.

---

# PHASE 5 — Automation logic

- **5.1 Auto-reprice + cost-drift alert.** On refresh, re-price changed quantities
  automatically and flag any line/section whose total moved beyond a configurable
  threshold (`COST_DRIFT_ALERT_PCT`); surface a "cost drift since last snapshot"
  inline report.
- **5.2 Rate learning.** Mine priced snapshots / prior projects to **auto-suggest**
  a rate (+ confidence) for unpriced items — a "suggest rates" action feeding the
  rate-gap report (G1).
- **5.3 Triggered cost workflows.** Wire `WorkflowEngine` cost presets to triggers
  (document save, data-drop, schedule import) the way `StingAutoTagger` hooks
  `DocumentChanged` — opt-in, off by default.
- **5.4 Indexation / escalation.** Apply inflation/price indices across time so a
  baseline can be escalated to a target date (`Data/STING_COST_INDICES.json`).

**Acceptance:** quantities auto-reprice with drift alerts; unpriced items get
suggested rates; cost workflows can run on triggers; a bill can be escalated.

---

# PHASE 6 — Flexibility

- **6.1 User-defined WBS / CBS.** Beyond the fixed groupings (NRM2/Level/Zone/
  Location/Source), let a project define its own work/cost breakdown structure
  (`_BIM_COORD/boq_wbs.json`) and group/roll-up by it.
- **6.2 Rate build-up library + regional factors.** A reusable rate build-up store
  (labour+plant+material per item, feeding G4's columns) + regional adjustment
  factors (location multipliers) applied to the library.
- **6.3 Scenario / what-if branches.** Parallel cost scenarios (not just sequential
  snapshots) — integrate with the existing `DesignOptions` cost/carbon calculator so
  options can be costed side-by-side in the panel.
- **6.4 Client bill templates / formats.** Per-client bill structure + tender format
  templates beyond the built-ins (extend `BOQTemplateLibraryExtensions`).

**Acceptance:** a project can impose a custom WBS, use rate build-ups + regional
factors, cost scenarios side-by-side, and export client-specific bill formats.

---

# PHASE 7 — 4D/5D depth (full programme + Excel-like Gantt + model 4D)

Build on the unified `ScheduleModel` (Phase 1). All inline, no popups.

- **7.1 Editable WBS/task grid** — a virtualized WPF `DataGrid` (WBS, task, start,
  end, %, predecessors, cost-load), Excel-like (sort/filter/edit), persisted.
- **7.2 Gantt canvas** — WPF `Canvas` timeline: bars, today-line, milestones (same
  technique as the existing S-curve). v1 read-only bars + grid edits; drag-resize
  later.
- **7.3 Task ↔ element linking** — assign Revit elements to tasks (by selection or
  rule), stored in **ExtensibleStorage**; cost-load tasks from the BOQ via those
  links.
- **7.4 Date-driven 3D playback** — a date slider that colours/isolates elements by
  task status (not-started / in-progress / done) in the active 3D view via view
  overrides + phases. In-Revit 4D simulation (not a Navisworks animation — state
  that limit).
- **7.5 Cost-loaded EVM** — PV/EV/AC per period from the linked cost-loaded
  programme; the existing EVM strip + S-curve, now driven by real tasks.
- **7.6 CPM + write-back** — critical-path calc + dependency editing; export the
  programme back to MS Project / P6 **XML**.

**Acceptance:** a real programme imports, displays Excel-like + as a Gantt, links to
model elements, drives a date-based 3D simulation and cost-loaded EVM, and exports
back to XML.

---

## Definition of done & order
- One gap per commit; compile-verify (`0 Error(s)`, `-t:Rebuild`) before each;
  deploy per phase via `deploy-gold.bat` (Revit closed) with the human's smoke test.
- **No popups via nested pumps; no engine forks; single source of truth;**
  `_BIM_COORD` JSON; `StingLog` for errors. **Do NOT push or merge.**
- **Order is mandatory:** **PHASE 0 (deadlock) → 1 (unify schedule) → 2 (accuracy)
  → 3 (performance) → 4 (integration) → 5 (automation) → 6 (flexibility) → 7 (4D/5D
  depth)**. Pause after each phase for review. Phase 0 must be human-verified before
  anything else — the workspace is unusable until it lands.
- As gaps close, update `docs/BOQ_QS_LAYMANS_GUIDE.md` §10 and add CHANGELOG entries.
