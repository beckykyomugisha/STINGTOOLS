# BOQ Cost Manager → Unified Inline 5D Workspace — Implementation Prompt

**Branch:** `claude/boq-implementation` (continue here, worktree `C:\Dev\STINGTOOLS-boq-impl`)
**Owner of this brief:** the implementing agent. Build in **verifiable vertical
slices**, one logical commit each. **Research further before and during** (see
§6) — this is not a closed spec; you are expected to find and append enhancement
gaps, not just execute the slices as written.

---

## 1. Mission

Turn `UI/BOQCostManagerPanel.cs` from a BOQ-only panel into a **single inline 5D
cost+programme workspace**: BOQ · Materials · **Schedule (4D/5D)** · Cash-flow,
where every action is performed **inline through editable grids and dropdowns**
with **zero external reporting popups** (no `TaskDialog`, no `StingResultPanel.Show()`
windows). Results, warnings, and confirmations render **in the panel**. The user
should be able to see, edit, and drive cost AND programme from one transparent,
interactive surface.

This unifies what is today split across the BOQ Cost Manager (cost) and the 13-tab
BIM Coordination Center / `BIMManager/SchedulingCommands.cs` (4D/5D) — **by
referencing the existing engine, never forking it.**

---

## 2. Current state (verified anchors — re-read before editing; lines drift)

**The panel already has every mechanism you need — reuse, don't reinvent:**

- **Inline view swap** — `BOQCostManagerPanel.ShowTenderSetupInline()` /
  `HideTenderSetupInline()` swap `_mainTabs` for a `_contentHost` child built by
  `BuildTenderSetupView()` (`BOQCostManagerPanel.cs:~2068-2105`). A Schedule view
  is the same move.
- **Editable-tabs-from-a-dialog** — `BOQTenderDialog.CreateInlineTabs()`
  (`:2097`) is the canonical "build a dialog's controls, embed them inline
  instead of `ShowDialog()`" pattern. The tender setup is already inline,
  editable grids/dropdowns, **no modal**. Mirror it.
- **Revit-thread dispatch, no popup** — `DispatchAction("tag")` →
  `StingBOQActionHandler` ExternalEvent (`:2031`). It sets
  `StingCommandHandler.SetExtraParam(...)` then dispatches, so the command runs on
  the Revit API thread and the tag resolves through `StingCommandHandler`'s case
  map. Action buttons already use it (`:283` Refresh, `:884` Import, `:885`
  Export, `:1506/1716` per-row edits).
- **Inline status (the no-popup convention)** — `FlashHint(vm, message)`
  (`:2039`) flashes an amber message in the coverage strip for 4s then restores.
  Use/extend this (and an embedded result region) instead of message boxes.
- **Per-row inline edit + write-back** — `:1715-1720` shows the pattern: set
  ExtraParams (`BOQEditRateUGX`, `BOQEditNRM2Para`, …) → `DispatchAction(
  "BOQWriteItemParams", refreshAfter:false)`. Editable grids drive Revit writes
  through this exact channel.

**The 4D/5D engine to reference (do NOT fork):**
`BIMManager/SchedulingCommands.cs` — `internal static Scheduling4DEngine` (`:39`)
plus ~20 commands: `AutoSchedule4DCommand` (`:1211`), `ImportMSProjectCommand`
(`:1291`), `ExportSchedule4DCommand` (`:1467`), `AutoCost5DCommand` (`:1542`),
`ImportCostRatesCommand` (`:1629`), `CostReport5DCommand` (`:1678`),
`CashFlow5DCommand` (`:1745`), `PhaseSummaryCommand` (`:1931`),
`MilestoneRegisterCommand` (`:2014`), `WorkingCalendarCommand` (`:2090`),
`NavisworksTimeLinerExportCommand`, `ElementCostTraceCommand`, `ExportFor4DViewerCommand`,
P6 link (`P6LiveLinkConfigCommand`/`P6WritebackCommand`/`P6SyncNowCommand`).

**Already done on this branch (build on, don't redo):** INT-0 GlobalId
(gold-standard + canonical encoder, 8/8 tests), INT-2 priced round-trip + P2-8,
INT-1 `BOQExportIfcQtoCommand` (tag `BOQExportIfcQto`, dispatch case already in
`StingCommandHandler` at `:3511`).

---

## 3. The one real refactor: engine returns data, panel renders it

"Zero external popups" is impossible while commands call `TaskDialog`/
`StingResultPanel.Show()` themselves. The structural change:

- **Separate logic from presentation.** Each scheduling/cost action must be able
  to **return its result as plain data** (task list, phase dates, period costs,
  cash-flow series, validation messages) instead of popping a window. Most of this
  already lives in `Scheduling4DEngine` / `BOQCostManager` — surface it.
- **Render inline.** Bind results to `ObservableCollection`-backed **editable
  `DataGrid`s** and inline charts in the panel; render status/warnings via the
  coverage strip / an embedded results region.
- **`StingResultPanel` as an embedded control.** Where a rich result view is
  wanted, host `StingResultPanel` as a `FrameworkElement` inside `_contentHost`
  rather than `.Show()`-ing it as a window. (Small change to how it's hosted; keep
  the `.Show()` API for non-panel callers.)
- **Don't break headless callers.** Commands invoked from workflows/ribbon still
  need a default presentation — gate the inline-vs-popup behaviour on an
  ExtraParam (e.g. `InlineHost=1`) the panel sets, mirroring the existing
  `SkipDialog` ExtraParam pattern the tender export already uses.

---

## 4. Hard requirements

1. **No external reporting popups** from any action driven by this panel —
   replace `TaskDialog.Show` / `StingResultPanel.Show()` with inline rendering.
   (Genuine *blocking confirmations* — "overwrite N rates?" — may use an inline
   confirm row, not a modal.)
2. **Everything editable inline** — grids with editable cells + dropdowns
   (`ComboBox` cells) for rates, phases, trades, calendars, currency, markups.
   Edits write through the existing `DispatchAction` + ExtraParam channel on the
   Revit thread.
3. **Reference the engine, never duplicate** — call `Scheduling4DEngine` /
   `BOQCostManager`; do not re-implement 4D/5D math in the panel.
4. **Revit-thread safety** — all model reads/writes via the panel's ExternalEvent
   (`DispatchAction`); never touch the Revit API directly from a WPF event.
5. **Performance** — BOQs/schedules can be thousands of rows. Use
   `VirtualizingStackPanel`/virtualized `DataGrid`; don't rebuild the whole view
   on every edit.
6. **One slice per commit; no-build sandbox** — this is WPF + Revit-API code that
   **cannot be compile-checked here**. Every Revit/WPF call must use a documented
   signature; mark uncertainty with `// TODO-VERIFY-API`; note the no-build caveat
   in the commit + CHANGELOG; **verify each slice in Revit before the next**. Do
   **not** push or merge.

---

## 5. Phased build

### Slice 1 — QTO button + establish the inline-result convention (small, first)
- Add a **"⛁ QTO IFC"** (or similar) button to the Cost Manager action row beside
  Export/Import (`:885` area) → `DispatchAction("BOQExportIfcQto")`. The dispatch
  case already exists in `StingCommandHandler` (`:3511`) — **verify** the
  `DispatchAction` path actually reaches it (it routes through `StingCommandHandler`;
  if the panel's `StingBOQActionHandler` has its own switch, add the case there too).
- Make `BOQExportIfcQtoCommand` (and ideally the standard Export) **render their
  result inline** when invoked from the panel (set `InlineHost=1`): output path +
  "elements stamped" + the LIMITATIONS notes go to an **inline results region /
  coverage strip**, not `StingResultPanel.Show()`. This proves the no-popup
  pattern end-to-end on one action and is the template for everything after.

### Slice 2 — Schedule (4D/5D) inline tab
- Add a **Schedule** tab/inline view (mirror `CreateInlineTabs`/`BuildTenderSetupView`)
  with editable grids:
  - **Tasks/phases** — name, trade, predecessor, start/finish, duration, % complete
    (editable; dropdowns for trade + predecessor). Drives `AutoSchedule4D` /
    `PhaseSummary` / `WorkingCalendar`.
  - **Cost-by-period (5D)** — period × cost grid feeding the **cash-flow**; editable
    rate/calendar inputs. Drives `AutoCost5D` / `CashFlow5D` / `CostReport5D`.
  - **Cash-flow S-curve** — simple inline bar/line (cumulative cost vs time). No
    external chart window.
  - **Import/Export** — MS Project XML (`ImportMSProject`/`ExportSchedule4D`),
    Navisworks TimeLiner, P6 sync — all results **inline** (status strip), file
    pickers are the only acceptable OS dialog.
- All actions via `DispatchAction`; all results inline.

### Slice 3 — Sweep remaining BOQ/scheduling commands to engine-returns-data + inline
- Convert the remaining cost/scheduling command popups (`CostReport5D`,
  `PhaseSummary`, `MilestoneRegister`, `ElementCostTrace`, BOQ validate/delta/
  rate-audit, etc.) to the inline pattern. Retire their `TaskDialog`s when invoked
  from the panel (keep a popup fallback for ribbon/workflow callers via the
  `InlineHost` gate).

---

## 6. RESEARCH MANDATE — find more, then append

Before/while building, **research and append enhancement gaps** beyond this brief.
At minimum:

1. **Industry 5D cost/programme UIs** (RIB iTWO, CostX workbooks, Candy, cove.tool,
   Power BI cost dashboards): what interactivity do their inline cost+cash-flow
   grids offer — in-cell editing, grouping/pivot, drill-down to element, undo,
   live re-cost on edit, variance/EVM colouring — that STING's panel should match?
   Cite sources; propose the high-value subset.
2. **WPF interactivity & a11y** patterns for large editable grids: virtualization,
   in-place validation, keyboard nav, copy/paste from Excel, multi-select edit,
   theming (the panel already has Navy/Amber theme brushes), undo/redo.
3. **Cross-check the existing BOQ review** (`docs/BOQ_REVIEW_AND_HARDENING_PROMPT.md`,
   Part A P0–P3 + Part B INT-0..INT-8) for items that should be **surfaced inline
   in this panel** rather than hidden, e.g.:
   - **P0-3** uncosted "value at risk" → an inline **red banner** + a filter to the
     unpriced rows (not a buried per-row confidence).
   - **P0-4** rate-confidence gate → inline confidence colouring + an export gate
     prompt in-panel.
   - Rate-source heat-map (`BOQRateSourceHeatMapCommand`) → an inline column/legend.
   - **INT-2** estimator-imported rates → provenance badge inline.
   - Carbon (P0-7/INT-7) → an inline carbon column + RIBA-stage rollup beside cost.
4. **STING's own backlog** — scan `docs/ROADMAP.md` and `Planscape.Server/docs/
   PLANSCAPE_GAPS.md` for cost/5D/UX gaps to fold in.
5. **EVM** (`Core/Evm/`), **Variations** (`Core/Variation/`), **Payment certs**
   (`Core/PaymentCert/`) already exist (Phase 191) — propose inline tabs/columns so
   the workspace covers the full cost-control lifecycle, not just the bill.

For each enhancement you adopt: add it to a slice, justify it in the commit/
CHANGELOG, and keep it inline + editable + popup-free. **Log what you researched
and what you deliberately deferred** (no silent scope cuts).

---

## 7. Acceptance criteria

- [ ] BOQ Cost Manager hosts BOQ · Materials · **Schedule (4D/5D)** · Cash-flow
      inline; no `TaskDialog`/`StingResultPanel.Show()` window appears for any
      panel-driven action (file pickers excepted).
- [ ] Cost AND programme are **editable in-grid** (cells + dropdowns); edits write
      to the model via the ExternalEvent and refresh inline.
- [ ] Cash-flow S-curve renders inline and updates on edit.
- [ ] `BOQExportIfcQto` reachable from a panel button, result shown inline.
- [ ] 4D/5D logic comes from `Scheduling4DEngine` (no forked math).
- [ ] At least the P0-3/P0-4/heat-map/provenance/carbon items from §6.3 are
      surfaced inline.
- [ ] Large-model performance: virtualized grids, no full-rebuild per keystroke.
- [ ] A short in-Revit smoke-test checklist accompanies each slice.
- [ ] CHANGELOG entry per slice with the no-build caveat; nothing pushed/merged.

## 8. Guardrails

- Continue on `claude/boq-implementation`; build on its commits; don't regress
  INT-0/INT-1/INT-2.
- **Reference, don't fork** the 4D/5D + EVM/Variation/PayCert engines.
- One logical slice per commit; `// TODO-VERIFY-API` on uncertain Revit/WPF calls;
  no `dotnet build` here — **verify in Revit before each next slice**.
- Don't push or merge. Surface decisions; do not silently cut scope.

---

## 9. RESEARCH FINDINGS & APPENDED GAPS (per §6 — added by implementing agent)

Researched 2026-06-26 while building Slice 1. Web-citation depth was partially
constrained by one dropped connection during deep research; the high-value claims
below are cited, and the rest are from STING's own verified backlog
(`docs/ROADMAP.md`, `docs/BOQ_REVIEW_AND_HARDENING_PROMPT.md`). Nothing here is a
silent scope cut — each gap is tagged **[adopt: Slice N]** or **[defer]** with a
reason.

### 9.1 Industry 5D cost/programme UI — what inline interactivity to match

- **RIB CostX / iTWO** — *live-linked workbooks*: spreadsheet cells live-link to
  the drawing/model AND to the rate library, so a rate edit re-costs the bill
  immediately; **double-click drill-down** from a subtotal → cost breakdown and
  from a quantity cell → quantity breakdown (auto-generated formula). Lesson:
  (a) **live re-cost on edit**, (b) **drill-down from a cost line to its source**
  (STING already has `SelectInRevit` per row — surface it as the drill-down).
  Source: <https://www.rib-software.com/en/rib-costx/bim>,
  <https://www.rib-software.com/en/blogs/rib-costx-functions>
- **BuildSoft Candy / cove.tool / Power BI cost dashboards** — pivot/grouping,
  variance colouring (budget vs actual), and an **S-curve / cash-flow** chart as a
  first-class view, not a popup. Lesson: grouping is already in STING
  (`BoqGroupingMode`); add **variance colour** + an **inline S-curve** (Slice 2).
- **High-value subset adopted:** in-cell editing (have it), live re-cost on edit
  **[adopt: Slice 2/3]**, drill-down-to-element **[adopt: Slice 3 — promote
  `SelectInRevit`]**, variance/EVM colouring **[adopt: Slice 2]**, inline S-curve
  **[adopt: Slice 2]**. Pivot beyond the current grouping modes **[defer]** —
  current WorkSection/Discipline/Level grouping covers the common case.

### 9.2 WPF large-editable-grid patterns to apply

- `DataGrid.EnableRowVirtualization` is **on by default** (creates/recycles
  `DataGridRow` only for visible items); `EnableColumnVirtualization` is **off by
  default** — turn it on for wide cost grids. Add
  `ScrollViewer.IsDeferredScrollingEnabled="true"` + `VirtualizingPanel.IsVirtualizing`
  / `VirtualizationMode=Recycling` for thousands of rows.
  Source: <https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.datagrid.enablerowvirtualization>,
  <https://docs.telerik.com/devtools/wpf/controls/radgridview/features/ui-virtualization>
- In-place validation via `IDataErrorInfo` / `ValidationRules`; keyboard nav is
  native to `DataGrid`; **Excel copy/paste** — `DataGrid` supports Ctrl+C
  (`ClipboardCopyMode`); paste-from-Excel needs a `CommandBinding` for
  `ApplicationCommands.Paste` that splits `\t`/`\r\n`. Multi-select bulk-edit via
  `SelectionMode=Extended` + apply-to-selection. Undo/redo: keep a per-edit
  command stack (the model edits already round-trip through `BOQWriteItemParams`,
  so an undo = re-dispatch the prior value).
  **[adopt: Slice 2 grids use a real `DataGrid` with virtualization; copy/paste +
  undo adopt: Slice 3]**. NOTE: today's BOQ tab renders **section `StackPanel`s,
  not a single `DataGrid`** — the Schedule tab (Slice 2) is the first true
  virtualized `DataGrid`; converting the BOQ bill grid is **[defer]** (large, and
  the existing per-section rendering already preserves open/closed state).

### 9.3 EVM presentation (Core/Evm already exists — surface it inline)

- Metrics: PV(BCWS), EV(BCWP), AC(ACWP); **CV = EV−AC**, **SV = EV−PV**;
  **CPI = EV/AC**, **SPI = EV/PV**; **EAC = BAC/CPI** (or `AC+(BAC−EV)`),
  **ETC = EAC−AC**, **VAC = BAC−EAC**. S-curve = cumulative PV/EV/AC vs time.
- Conventional RAG on CPI/SPI: **green ≥ 0.95, amber 0.85–0.95, red < 0.85**
  (some teams: green > 1, amber ≈ 1, red < 0.9).
  Source: <https://www.planacademy.com/7-earned-value-management-formulas/>,
  <https://www.gatherinsights.com/en/earned-value/cpi-spi>
- **[adopt: Slice 2/3]** EVM strip on the Schedule/Cash-flow tab: PV/EV/AC S-curve
  + CPI/SPI RAG chips, reading from `Core/Evm`. **ROADMAP caveat folded in:** BCWS
  still comes from a QS-entered planned % (no cost-loaded-schedule wiring) — show
  that provenance inline rather than implying a real 4D baseline.

### 9.4 BOQ-review items to surface inline (from §6.3 — verified in BOQ_REVIEW)

- **P0-3 (uncosted "value at risk")** — unmatched rate → silent £0 line folded
  into the total. **[adopt: Slice 1 banner scaffold → Slice 3 full]**: an inline
  **red banner** with the count + UGX/USD value-at-risk and a one-click filter to
  the unpriced rows (reuse `_searchBox`/`RebuildSectionsView`).
- **P0-4 (rate-confidence export gate)** — `RateConfidence` computed but never
  gates export. **[adopt: Slice 3]**: inline confidence colouring (already partly
  present per row) + an in-panel acknowledge row before export below
  `MinRateConfidenceForExport` (default 60).
- **Rate-source heat-map** (`BOQRateSourceHeatMapCommand`) — **[adopt: Slice 3]**
  as an inline column + legend, not a separate view.
- **INT-2 estimator-imported rates** — **[adopt: Slice 3]** provenance badge inline
  on rows whose rate came from the priced round-trip import.
- **Carbon (P0-7/INT-7)** — embodied-carbon card already exists; **[adopt: Slice 3]**
  an inline carbon column + RIBA-stage rollup beside cost. Honour P0-7: do **not**
  add a new carbon engine — read the existing `CarbonFactorResolver` numbers.

### 9.5 Full cost-control lifecycle (Phase 191 engines already exist)

- `Core/Evm`, `Core/Variation`, `Core/PaymentCert` exist. **[adopt: Slice 3 — thin
  inline tabs/columns]**: a Variations grid (create/quote/approve, budget impact)
  and a Payment-cert summary, each **referencing** the existing engines (no forked
  math), so the workspace covers bill → variation → cert → EVM, all inline +
  editable + popup-free.

### 9.6 Deliberately deferred (logged, not silently cut)

- Converting the existing per-section BOQ bill `StackPanel` into one virtualized
  `DataGrid` (large refactor; current rendering preserves section state).
- Full pivot beyond the existing grouping modes.
- Cost-loaded 4D baseline for a real EVM BCWS (ROADMAP open item — needs schedule
  wiring; Slice 2 shows the QS-planned-% provenance instead of faking it).
- Excel-style copy/paste + a formal undo/redo stack (Slice 3 if budget allows).

---

## 10. Live-test feedback (Slice 1.5) — Actions master-detail + Materials expand fix

From an in-Revit test of Slice 1. Four items; do them as **Slice 1.5** (one
commit), keeping the zero-popup + inline conventions from Slice 1.

### 10.1 — Actions tab → master-detail (buttons LEFT, report RIGHT) [PRIORITY]
**Problem:** the Actions tab shows grouped buttons (QS Round-trip, Automation,
Cost Plan, Payment Certs, Variations, EVM) but clicking them shows nothing inline
("no functions seen") — results still go to popups or nowhere.
**Build:** split the Actions tab into a **two-column master-detail** view:
- **Left rail** — the existing grouped action buttons (P3/P2/P4/P5.1/P5.2/P5.3),
  vertical, scrollable; the clicked button gets a selected/highlight state.
- **Right pane** — a single **inline report region** (reuse `BuildInlineContent`
  + the `BOQInlineResults`/`InlineHost=1` convention from Slice 1) that renders
  **the result of whichever button was last clicked**. Title = the action name.
- **Every action routes its result here** — set `InlineHost=1` before each
  `DispatchAction`, so EVM metrics, VO register, cert register, cost-plan
  comparison, validation output, etc. all render in the right pane. **No
  `TaskDialog`/`StingResultPanel.Show()`** for any Actions-tab button.
- Where the action's output is tabular/editable (EVM metrics, VO register, cert
  register, cost-plan rows), render an **editable `DataGrid`** in the right pane,
  not static text — consistent with §4.2.
- Empty state: right pane shows "Select an action on the left." until first click.

This makes the no-popup convention the default for the whole Actions surface and
is the template the Schedule tab (§5 Slice 2) will reuse.

### 10.2 — Materials Expand-all / Collapse-all don't work (BOQ does) [BUG]
**Root cause (verified):** the `⊞ Expand all` / `⊟ Collapse all` toolbar buttons
(`BOQCostManagerPanel.cs:462-473`) only mutate the BOQ `_openSections` set and
call `RebuildSectionsView()` — they never touch the Materials tab. The Materials
expanders (`BuildMaterialsContent` `:1818-1821`) are hardcoded `IsExpanded=false`
with no state tracking and no `Expanded/Collapsed` handlers.
**Fix:** give Materials the same treatment as BOQ — an `_openMaterialSections`
set (or a shared expand-state), `Expanded/Collapsed` handlers that persist it,
`IsExpanded` seeded from it, and make Expand-all/Collapse-all **apply to the
active tab** (or both): set the Materials state then `RebuildMaterialsTab()`.
Also persist per-section state across `Refresh` so a manual chevron toggle
survives a rebuild (BOQ already does this via `_openSections`).

### 10.3 — Schedule (4D/5D) still absent [= Slice 2, expected]
Confirmed not built yet — that is **Slice 2** (§5). The Actions tab already
surfaces P5.x cost-control (certs/variations/EVM); the Schedule tab adds the 4D
programme + 5D cash-flow grids referencing `Scheduling4DEngine`. Build it after
1.5 lands, reusing the §10.1 master-detail/inline-report pattern.

### 10.4 — "No functions seen yet"
Largely a symptom of 10.1 (actions produced no visible inline output). Once the
right-pane report is wired, every Actions button should visibly do something
inline. While testing, confirm each P-group button actually reaches its command
(the `DispatchAction` tag resolves in `StingCommandHandler`); log any tag that
falls through so it can be wired.

**Guardrails unchanged:** reference engines (don't fork), Revit-thread via
`DispatchAction`, virtualized grids, `// TODO-VERIFY-API` on uncertain calls, no
sandbox build → verify each in Revit, one commit for Slice 1.5, don't push/merge.
