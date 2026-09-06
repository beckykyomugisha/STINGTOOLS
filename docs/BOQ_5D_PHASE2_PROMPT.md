# BOQ 5D Cost Manager — Phase 2 Implementation Prompt (the 5 enhanced-rebuild items)

You are a terminal coding agent on the **StingTools** C# Revit plugin. Phase 0
(dispatch busy-guard / deadlock fix), the full inline-forms sweep, Phase 1
(schedule unification), and the Schedule-tab interactivity are all landed and
smoke-tested in Revit. This prompt covers the five remaining enhanced-rebuild
items. Implement them as **Phases 2A → 2E, in order**, slicing each, compile-
verifying + committing + pausing for the human to smoke-test per group.

This prompt is **grounded in the real codebase** — the entry points below were
verified. Reuse them; do not reinvent or fork.

---

## 0. Environment, build, deploy, git — READ FIRST

**Branch & checkout.** All work is on **`claude/placement-centre-review-audit`**
in the main checkout **`C:\Dev\STINGTOOLS`**. The checkout is **shared with other
agents** (placement work commits interleave). Verify before editing:
```bash
cd /c/Dev/STINGTOOLS && git branch --show-current   # must print claude/placement-centre-review-audit
```
Do NOT create a worktree. Do NOT `git reset --hard` / rebase / force anything —
other agents' commits live here. Commit only the files you touched.

**Build (compile-verify — headless works via Nice3point). MANDATORY before every
commit:**
```bash
dotnet build StingTools/StingTools.csproj -c Release -t:Rebuild --nologo -v minimal   # must say 0 Error(s)
```
**Deploy is the human's job** (Revit locks the DLL). After a group of slices,
STOP and tell the human to close Revit; they deploy + restart + smoke-test, then
tell you to continue. **Do NOT push or merge.** Log every slice in
`docs/CHANGELOG.md` with its Revit smoke test.

---

## 1. Inline conventions to REUSE (no popups, no forks) — VERIFIED SIGNATURES

The Cost Manager is a no-popup inline workspace. Every new surface follows this:

- **Inline result pane:** `BOQInlineResults.Sink` (set in `BOQCostManagerPanel`,
  ~line 301; deregistered on Unload). Build a `StingResultPanel.Builder`, then
  `if (!BOQInlineResults.Post(rp)) rp.Show();` — inline when the panel hosts,
  modal fallback for ribbon callers.
- **Result builder** (`UI/StingResultPanel.cs`): `.SetTitle/.SetSubtitle/.AddSection/
  .Metric/.Finding/.PassFail/.RAGBar/.Table(headers, rows)/.Action(label, desc,
  click)/.Show()`. Use `.Table(...)` for diffs/candidate lists, `.Action(...)` for
  inline buttons (run with `null` Window, try/catch).
- **Inline input forms:** render a titled form (combos / numeric `TextBox` /
  `CheckBox`) + Run button into the Actions pane (`ShowInlineForm` / the
  `TryShowInlineFormFor` interceptor), collect values, set them via
  `StingCommandHandler.SetExtraParam(key, value)`, dispatch the tag; the command
  reads `GetExtraParam(key)` and **skips its dialog when present** (keep the modal
  as the ribbon fallback). `InlineHost=1` is the established gate.
- **Inline pickers:** `StingListPicker` (modal fallback while a Revit transaction
  is open — `IsModifiable` guard). **Never** use `DispatcherFrame`/`PushFrame`
  (that caused the deadlock — permanently banned).
- **Persistence:** project-scoped JSON under `<project>/_BIM_COORD/` (same as
  `boq_links.json`, `boq_ui_state.json`, `schedule.json`, `rate_card.json`).
- **Engines — call, never fork:** `BOQCostManager`, `RateProviderRegistry`,
  `MeasurementStandardRegistry`, `TakeoffRuleRegistry`, `BoqSnapshotHasher`,
  `BOQCostManager.CompareSnapshots`, `BoqSyncCoordinator`, `IfcQuantitySetWriter`,
  `Scheduling4DEngine`, `ScheduleStore`, `StingResultPanel`, `StingListPicker`.
- **Errors:** `StingLog.Info/Warn/Error` — never silent-catch. `[Transaction]`
  attributes correct; Revit API reads stay on the API thread.

---

## PHASE 2A — NRM2 rules-based measurement (the accuracy payoff) — DO FIRST

**The gap.** Today take-off is essentially *raw Revit geometry × rate*.
`BuildLineItemFromElement` (`BOQ/BOQCostManager.cs` ~line 382) resolves a quantity
via `TakeoffRuleRegistry` and a unit via `IMeasurementStandard.PreferredUnit`, but
**no NRM2 measurement rules are applied**: no girth, no void/opening deductions,
no over-measure conventions, no wastage. The hooks already exist but are unused:
`IMeasurementStandard.ApplyDeductions(BOQLineItem, Element)` (stub in
`NRM2Standard`) and `TakeoffRule.WastePercent` (reserved, never consumed).

**Build a measurement-rules layer** that turns *modelled geometry* into
*measured quantity* with an auditable trail.

1. **Implement `NRM2Standard.ApplyDeductions`** (and the CESMM4 stub) properly —
   per-category NRM2 rules. At minimum:
   - **Walls:** deduct openings (doors/windows/curtain panels hosted in the wall)
     over the NRM2 de-minimis threshold; measure area on the correct face; handle
     girth for linear items. Use the host/insert relationship
     (`Wall.FindInserts` / `FamilyInstance.Host`) to find openings.
   - **Floors/ceilings/roofs:** deduct large voids/openings; net area.
   - **Linear items (skirting, trim, pipe, conduit, beams):** girth / centre-line
     length with NRM2 measurement points.
   - Each rule reads thresholds from a new corporate JSON
     `Data/STING_NRM2_MEASUREMENT_RULES.json` (de-minimis areas, wastage %, over-
     measure conventions per NRM2 section) with a project override at
     `<project>/_BIM_COORD/nrm2_measurement_rules.json` (corporate + override merge,
     same pattern as `TakeoffRuleRegistry`).
2. **Consume `TakeoffRule.WastePercent`** (and the JSON wastage %) — apply wastage
   as a distinct, visible step (not folded silently into the rate).
3. **Make the maths auditable.** Add to `BOQLineItem`: `GrossQuantity`,
   `DeductionQuantity`, `WastageQuantity`, and `MeasurementNote` (e.g.
   *"Gross 43.0 m² − openings 5.2 m² + wastage 5% = 39.8 m²"*). `Quantity` stays
   the **net measured** value used for cost. Keep raw geometry in `GrossQuantity`
   so nothing is lost.
4. **Surface it.** In the BOQ row-details / a new optional column set
   (`Gross` / `Deduct` / `Waste` / `Net`, hidden by default via the existing
   `_hiddenColumns` mechanism), and in a new inline **"Measurement audit"** action
   that lists, per line, the gross→net derivation in a `StingResultPanel.Table`.
5. **Standard-aware.** The active `IMeasurementStandard` (NRM2 / CESMM4 / POMI /
   ICMS3 / MMHW — switched via the existing Measurement Standard action) drives
   which rules apply, so CESMM4 vs NRM2 give different nets.

**Acceptance:** a wall with a door+window measures **net of the openings** (not
gross); wastage is applied and visible; the gross→net derivation is auditable per
line; switching the measurement standard changes the nets; raw geometry is still
recoverable in `GrossQuantity`; rules load from JSON with a project override; no
popups; build 0 errors. **This is multi-commit** — slice by category family
(walls/openings first, then floors/slabs, then linear items, then wastage +
audit surface).

---

## PHASE 2B — Live rate feeds inline (BCIS / Planscape)

**The gap.** `BcisHttpRateProvider` is a **real** HTTP client (Bearer auth, hot +
disk TTL cache, `RequiresNetwork=true`, `Priority=50`) and `RateProviderRegistry`
already layers providers by priority and exposes
`ResolveAll(req) → IReadOnlyList<(IRateProvider, RateLookup)>` with
`Confidence` / `Provenance` / `MatchedKey` on every `RateLookup`. But the Cost
Manager never surfaces live feeds: there's no config UI, no "fetch live rates"
action, and the user can't see/choose among candidate rates.

1. **Config form (inline).** A "Rate feeds" action that renders an inline form:
   BCIS base URL + API key + TTL minutes; Planscape feed on/off (reuse the
   existing Planscape server client + auth if present). Persist to
   `<project>/_BIM_COORD/rate_feeds.json` (never commit secrets; the key lives in
   the project file only). Wire these into `RateProviderRegistry.Get(...)` so the
   BCIS provider is constructed with real config instead of being inert.
2. **Fetch + review (inline).** A "Fetch live rates" action that, for the current
   bill (or the selected lines), calls `RateProviderRegistry.ResolveAll(req)` per
   line and renders an inline `StingResultPanel.Table`: line ref · description ·
   current rate (source/confidence) · **candidate rates from each provider**
   (value · source · confidence · as-of). Provide an **Action** to *accept* the
   highest-confidence live rate onto the line (write `RateUGX` + `RateSource` +
   `RateConfidence` + `LastCosted`, mark `RateSource="BCIS"`/`"Planscape"`), with a
   per-line or bulk apply. Respect the existing override priority (a manual
   `Override` rate must not be silently clobbered — flag it instead).
3. **Confidence + freshness visible.** Show each fetched rate's confidence and
   fetch date; colour low-confidence (<60) amber. Cache hits vs network fetches
   noted (the provider already disk-caches by TTL).
4. **Offline-safe.** When `RequiresNetwork` providers are unreachable, degrade
   gracefully (keep existing rates, surface a "feed unreachable" note) — never
   throw, never blank a rate.

**Acceptance:** the user configures a BCIS/Planscape feed inline; "Fetch live
rates" shows per-line candidate rates with source + confidence; accepting applies
the live rate and re-totals; manual overrides are protected; offline degrades
cleanly; config persists; no popups; build 0 errors.

---

## PHASE 2C — Auto-reprice / drift alerts

**The gap.** `BOQCostManager.CompareSnapshots(pathA, pathB)` already produces
`BOQSnapshotDiff` with `SectionDiff`/`CategoryDiff` rows typed by `ChangeType`
(`RateRevised` / `QtyChanged` / `NewItem` / `ItemRemoved` / `PSAdded` /
`SourcePromoted`) and net-change metrics; `BoqSnapshotHasher.ComputeChecksum`
gives a deterministic per-bill hash. But nothing watches for drift or re-prices
changed lines automatically.

1. **Drift check (inline).** A "Drift check" action that builds the **live** bill,
   compares it against the **last saved snapshot** (latest `boq_snapshot_*.json` by
   timestamp) via the existing compare/hash, and renders an inline panel:
   headline (checksum changed? net Δ cost / Δ carbon / Δ %), then a
   `StingResultPanel.Table` of changed lines grouped by `ChangeType` (qty moved,
   new, removed, rate revised), each clickable to select the element(s) in Revit
   (use the `Finding(text, elementId)` overload).
2. **Auto-reprice changed lines.** An **Action** on the drift panel:
   *"Re-price N changed lines"* — for `QtyChanged` + `NewItem` rows, re-run
   `RateProviderRegistry.Resolve(req)` (incl. live feeds from 2B if configured) and
   update those lines only; leave manual `Override` rows untouched; re-total. Show
   the before/after in the result.
3. **Drift stamp / passive alert.** On `RefreshAsync`, cheaply compare the new
   live checksum against the last-snapshot checksum and, if changed, show a small
   non-blocking banner/metric on the dashboard strip ("Bill drifted from last
   snapshot — N lines changed · Drift check") that deep-links to the Drift check
   action. No popup, no modal — a dashboard affordance only.
4. **Persist** the last-seen checksum + drift summary in
   `<project>/_BIM_COORD/boq_drift.json` so the banner survives reopen until a new
   snapshot is saved.

**Acceptance:** Drift check lists exactly what changed vs the last snapshot,
element-linked; "Re-price changed lines" updates only drifted non-override lines
via the rate chain and re-totals; the dashboard shows a passive drift indicator
after a model change; manual overrides survive; no popups; build 0 errors.

---

## PHASE 2D — Incremental / background take-off (performance) — HIGHEST RISK, do carefully

**The gap.** `BuildBOQDocument` is **synchronous on the Revit API thread**
(`RefreshAsync`, `BOQCostManagerPanel` ~line 2793, shows a progress dialog for
≥1500-element models). A per-link cache exists (`_linkTakeoffCache`, raw items
keyed by linked file path, grouping applied post-cache). There is **no host-side
incremental take-off** — every Refresh walks every host element.

**Two independent, safe levers. Do the dirty-set first (lower risk), background
split second only if profiling still shows jank.**

1. **Dirty-set incremental host take-off.** Register a lightweight `IUpdater`
   (mirror `StingStaleMarker` in `Core/StingAutoTagger.cs`) that, on
   geometry/parameter change of cost-relevant categories, records the changed
   `ElementId`s into a per-document **dirty set**. `RefreshAsync` gains an
   *incremental* path: when a prior host take-off result is cached and a dirty set
   exists, only re-run `BuildLineItemFromElement` for dirty elements (+ removed
   elements drop their lines), then re-aggregate + re-group + re-total — instead of
   re-walking the whole model. A full rebuild stays available ("Refresh (full)")
   and runs on first open / when no cache exists / on demand. Cache the host raw
   items the same way links are cached (raw, pre-aggregate, pre-group) so grouping
   toggles stay free. Clear on document close (wire into `OnDocumentClosing`).
2. **Background the heavy compute (only if needed).** If the host walk still janks
   the UI on large models: read all needed element data into **POCOs on the API
   thread** (Revit reads MUST stay on the API thread), then do grouping / rate
   resolution / aggregation / carbon **off-thread**, marshalling the result back
   for `RefreshDisplay` via the existing `StingProgressDialog`. **Do NOT** touch
   the Revit API off-thread. If unsure whether it's needed, **skip 2D.2** — the
   dirty-set already removes most of the per-refresh cost.

**Acceptance:** after the first full build, editing a handful of elements and
pressing Refresh re-takes-off only those (visibly faster on a large model);
results are identical to a full rebuild (verify totals match); a full rebuild is
still available and correct; no Revit API call runs off the API thread; cache
clears on close; no regressions to linked-model take-off; build 0 errors.
**Be conservative — correctness over speed. If the incremental result ever
diverges from a full rebuild, fall back to full.**

---

## PHASE 2E — User-defined WBS/CBS + ERP export

**The gap.** `BoqGroupingMode` already offers WorkSection / Level / Zone /
LevelThenWorkSection / Location / SourceModel, but there's **no user-defined
WBS/CBS**. WBS lives on `ScheduleTask.Wbs` (`Core/Schedule/ScheduleModel.cs`), not
on `BOQLineItem`. Export is rich (8-sheet XLSX, IFC Qto + `Pset_StingCost`,
QS round-trip) but there's **no ERP-shaped cost export**.

1. **WBS/CBS on the bill.** Add `WbsCode` + `CbsCode` to `BOQLineItem`. Provide a
   **mapping editor (inline)**: rules that assign WBS/CBS from element attributes
   (category / discipline / NRM2 section / level / zone / system), persisted to
   `<project>/_BIM_COORD/boq_wbs_map.json`. Apply during `BuildLineItemFromElement`
   (or a post-pass). Add a **`Wbs` / `Cbs` grouping mode** to `BoqGroupingMode`
   + the Group dropdown, so the bill can be filed by the client's cost breakdown
   structure, not just NRM2.
2. **5D rollup link.** Where a `BOQLineItem`'s element is in a `ScheduleTask.ElementIds`,
   inherit that task's `Wbs` (so the 4D programme and the 5D bill share one WBS).
   Reconcile: WBS map rule wins if set; else inherit from the linked ScheduleTask.
3. **ERP export.** A new "Export to ERP" action producing a flat, import-ready
   file in a **standard ERP cost-import shape** — a CSV with columns
   `WBS, CBS, CostCode/NRM2, Description, Qty, Unit, UnitRate, Total, Currency,
   Level, Location, Source, ElementId/IfcGuid` (the union most ERP/accounting
   importers accept: SAP PS / Oracle Primavera Unifier / QuickBooks / Sage). Plus
   an optional **Primavera P6-style XML** activity-cost export if cheap to add
   (reuse the Phase 1c P6 XML writer direction). Render the result inline with the
   **Open file** button (set the CSV path on the builder). Keep the existing 8-sheet
   XLSX + IFC Qto exports unchanged.

**Acceptance:** lines carry WBS/CBS from a persisted, user-editable map (with
ScheduleTask inheritance for 5D); the bill can be grouped by WBS/CBS in the
dropdown; "Export to ERP" writes a flat import-ready CSV (and optional P6 XML)
with WBS/CBS columns and opens inline; existing exports unchanged; no popups;
build 0 errors.

---

## Order, slicing, definition of done

**Pacing (UPDATED): Phase 2A is already done, committed, and builds clean. Run
the rest — 2B → 2C → 2D → 2E — in ONE continuous pass. Do NOT pause between phase
parts.** The human will smoke-test **once at the very end**, covering 2A→2E
together. Only stop at the very end.

- **Order:** 2A (done) → 2B (live rates) → 2C (drift, depends on 2B's chain) →
  2D (performance) → 2E (WBS/ERP). 2E touches `BOQLineItem` (WBS/CBS fields) — its
  field additions sit alongside 2A's; keep them together to avoid churn.
- **Keep it bisectable:** one logical slice = one commit; compile-verify
  (`0 Error(s)`, `-t:Rebuild`) **before each commit**, so if the single end-to-end
  smoke test finds a problem the human can bisect to the exact commit and revert
  just that one.
- **No popups** — every new surface inline per §1. **Reuse engines; no forks.**
  `StingLog` for errors, `[Transaction]` correct, Revit reads on the API thread,
  `_BIM_COORD` JSON for persistence, **no secrets committed**.
- **2D is the highest-risk part — treat conservatively.** Correctness over speed:
  if an incremental take-off result ever diverges from a full rebuild, fall back
  to full. Never let 2D change a number that a full rebuild wouldn't.
- **Do NOT push or merge.** Shared checkout — no `reset --hard`/rebase/force.
  Log every slice in `docs/CHANGELOG.md`.
- **At the very end**, run a final full rebuild + a self-check and report it:
  (1) `BuildBOQDocument` totals are unchanged for non-deducted lines vs a full
  rebuild (2A/2D regression guard); (2) live-rate fetch, drift check + reprice,
  and ERP export all produce output and render inline; (3) no Revit API call runs
  off the API thread (2D); (4) no new popups for data entry. Then **STOP for one
  comprehensive human smoke test** covering 2A→2E, with a single consolidated
  smoke-test checklist in `docs/CHANGELOG.md`. Do not start any later phase.
