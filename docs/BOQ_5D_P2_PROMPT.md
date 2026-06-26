# BOQ 5D Cost Manager — P2 Implementation Prompt (4D tab, inline forms, link multiplier)

You are a terminal coding agent on the **StingTools** C# Revit plugin. The BOQ &
Cost Manager is now a working inline workspace (master-detail Actions pane, inline
pickers + results, linked-models per-link selection, persisted UI state). This
prompt covers the remaining **P2** enhancements. Implement them in order, compile-
verifying, committing, and deploying each so the human can smoke-test.

---

## 0. Environment, build & deploy — READ THIS FIRST (hard-won, non-obvious)

**Branch & checkout.** All work now lives on **`claude/placement-centre-review-audit`**
in the **main checkout `C:\Dev\STINGTOOLS`**. The earlier BOQ worktree has been
merged in — placement + BOQ are one branch. Verify before editing:
```bash
cd /c/Dev/STINGTOOLS && git branch --show-current   # must print claude/placement-centre-review-audit
```
Do NOT create a worktree. Do NOT run `deploy.bat` from any other checkout (that
repoints Revit's addin away from this one — it's what caused a multi-hour
"my changes never show up" saga).

**The addin → DLL path (the thing that bit us repeatedly).** Revit loads the DLL
named in `%APPDATA%\Autodesk\Revit\Addins\2025\StingTools.addin`. It now points to:
```
C:\Dev\STINGTOOLS\CompiledPlugin\StingTools.dll
```
That is exactly where `build.bat` writes. So **`build.bat` from `C:\Dev\STINGTOOLS`
updates the live plugin directly** — no repoint needed. If a deploy ever "doesn't
show up", FIRST check that addin still points at `C:\Dev\STINGTOOLS\CompiledPlugin`
(don't assume — `grep -i '<Assembly>' "$APPDATA/Autodesk/Revit/Addins/2025/StingTools.addin"`).

**Build (compile-verify — headless works here via Nice3point).** MANDATORY before
every commit:
```bash
dotnet build StingTools/StingTools.csproj -c Release -t:Rebuild --nologo -v minimal   # must say 0 Error(s)
```
**Deploy.** Revit locks `CompiledPlugin\StingTools.dll` while open, so the human
must close Revit, then either run `build.bat` (compiles + stages to CompiledPlugin)
or you copy `StingTools/bin/Release/StingTools.dll` → `CompiledPlugin/StingTools.dll`.
Confirm the deployed timestamp changed. The human restarts Revit to load it.

**Git.** Commit per task (imperative subject + `Co-Authored-By: Claude Opus 4.8
<noreply@anthropic.com>`). **Do NOT push or merge.** Log each task in
`docs/CHANGELOG.md` and state its Revit smoke test (the human runs it — you can't).

---

## 1. The inline conventions to REUSE (don't reinvent; don't fork)

The Actions tab is a 2-column master-detail in `UI/BOQCostManagerPanel.cs`:
`BuildActionsTab` → `BuildActionReportPane` (`_actionReportHost` Border +
`_actionReportTitle`), and `_actionsTab` is added to `_mainTabs`. Commands report
inline because:
- `StingResultPanel.InlineSink` is set during `RunActionInline`; `Builder.Show()`
  routes to it → `ShowInlineResult` renders into the pane (with a bottom **action
  bar**: "Open file" via `SetCsvPath` + `Action(...)` buttons).
- `StingListPicker.InlineHost`/`InlineSafe` render pickers inline (modal fallback
  while a Revit transaction is open — `IsModifiable` guard).
- `PendingActionResolve` resolves the "Running…" placeholder when a command
  finishes without posting inline.
- `StingCommandHandler.Execute`'s `finally` clears all these statics.

**Anything you add must follow this no-popup convention.** New result surfaces go
through `StingResultPanel`; new pickers through `StingListPicker`; new tabs render
their own inline content in `_mainTabs`. Reuse engines — **never fork**
`Scheduling4DEngine`, `EarnedValueEngine`, `BOQCostManager`, `StingResultPanel`,
`StingListPicker`.

---

## P2.1 — 4D / Schedule tab (the real 5D payoff) — LARGEST, do first

Add a **Schedule** tab to `_mainTabs` (next to Bill of Quantities / Materials /
Actions) that brings 4D programme + EVM into the workspace, rendered inline.

**Engines to call (read them first — do not fork):**
- `BIMManager/SchedulingCommands.cs` — `Scheduling4DEngine` (32-trade construction
  sequences, phase dates, cash-flow forecasting, Gantt). Commands there
  (`AutoSchedule4D`, `ViewTimeline4D`, `AutoCost5D`, `CostReport5D`, `CashFlow5D`,
  `PhaseSummary`, `MilestoneRegister`, …) show how to invoke it.
- `Core/Evm/` — `EvmPeriod` (Bac/Bcws/Bcwp/Acwp → Cv/Sv/Cpi/Spi/Eac/Etc/Vac) +
  `EvmReport` (`Periods`, `Latest`) + the compute engine (`EarnedValueEngine` /
  `EvmDashboardCommand`). Read the engine to find its compute entry point.

**Tab content (all inline — no popups, editable where noted):**
1. **Phase / milestone grid** — a WPF `DataGrid` of project phases (from Revit
   `Phase`s and/or `Scheduling4DEngine`) with **editable** start/end dates and
   %-complete. Persist edits to `<project>/_BIM_COORD/boq_schedule.json` (same
   `_BIM_COORD` JSON convention as `boq_links.json` / `boq_ui_state.json`). On
   commit, push dates back via the existing `AssignPhaseDates` / `LinkPredecessors`
   command paths (call them — don't reimplement).
2. **Cash-flow S-curve** — a simple WPF chart (Polyline/Path or stacked bars on a
   `Canvas`; **no external chart lib**) of planned vs earned vs actual cumulative
   cost over the programme, fed by `Scheduling4DEngine`'s cash-flow forecast +
   the BOQ grand total. Keep it lightweight and theme-consistent (navy/grey).
3. **EVM strip** — PV / EV / AC / SPI / CPI / EAC / ETC / VAC from `EvmReport.Latest`,
   shown as labelled metrics with RAG colouring (SPI/CPI <0.95 amber, <0.9 red).
   Provide an **"Import actuals"** affordance (inline editable AC per period, or a
   CSV import via `StingListPicker`/file path) and a **"Recalculate"** button that
   re-runs the EVM engine and refreshes the strip + S-curve.
4. **Export** — an "Export schedule/EVM" button that builds a `StingResultPanel`
   (or an XLSX via the existing `ExportSchedule4D` / `EVM_Export` commands) and,
   when run from this tab, shows the result inline with the **Open file** button.

**Wiring:** add the tab in the same place as `_actionsTab`
(`_mainTabs.Items.Add(...)`). Refresh its data on `RefreshDisplay` (cheap reads
only; heavy recompute on explicit button press). Persist tab-specific UI state via
the existing `boq_ui_state.json` mechanism.

**Acceptance:** a Schedule tab exists; phase dates + %-complete are editable and
persist across reopen; the S-curve renders; EVM metrics compute from real data;
export renders inline with Open-file. No popups. Engines are called, not forked.

**Scope note:** this is multi-commit. Land it in slices (grid first, then S-curve,
then EVM, then export), compile-verifying + committing each.

---

## P2.2 — Inline editable forms for input-heavy actions

Some Actions want a small **editable form** (combos + numeric fields), not a
pick-then-report list — e.g. **Set % Complete** (`PayCert_*` / cost-plan progress),
**EVM actuals** (AC per period), **Set Budget**. Today these either pop a dialog or
chain `StingListPicker`s.

**Build a reusable inline form host** in the Actions pane: a method that renders a
titled form (labelled rows of `ComboBox` / numeric `TextBox` / `CheckBox`) + a
**Run** button into `_actionReportHost`, collects the values, and dispatches the
command with the values passed via the **ExtraParam contract**
(`StingCommandHandler.SetExtraParam(key, value)` — the command reads them with
`GetExtraParam` instead of popping its own dialog). This mirrors how `InlineHost=1`
already gates the QTO command.

- Convert **2–3 commands** as the proof (Set % Complete, EVM actuals, Set Budget),
  each: panel renders the inline form → on Run sets ExtraParams + dispatches → the
  command reads ExtraParams (skips its dialog when present, falls back to its
  dialog when absent so ribbon callers still work) → posts its result via
  `StingResultPanel` (inline).
- Then sweep any remaining input-heavy Actions the same way.

**Do NOT** change command logic/transactions — only the input-gathering surface.
Keep the dialog fallback for ribbon/other-surface callers.

**Acceptance:** the converted actions present an inline editable form in the pane
(no popup), run with the entered values, and report inline. Dispatched from the
ribbon, they still prompt via their dialog (fallback intact).

---

## P2.3 — Linked-instance multiplier (correctness option)

`BOQCostManager.CollectLinkedItems` dedups by link **Title** (`seenTitles`), so a
model legitimately placed **twice** (e.g. mirrored wings) is taken off **once** —
an undercount. Add an **optional** per-link instance multiplier:

- Count the loaded `RevitLinkInstance`s per link Title in the host.
- In the `ChooseLinkedModels` picker (`UI/BOQCostManagerPanel.cs`), show the
  instance count in each item's `Detail` (e.g. "3 instances").
- Add a per-link opt-in (persisted in `boq_links.json` alongside the included
  set — e.g. a parallel map of Title → multiply-by-instance-count bool, default
  **off** since a shared reference model placed once is the common case).
- When on for a link, multiply that link's line-item quantities (and derived
  cost/carbon) by its instance count in `CollectLinkedItems`, and note it in the
  row (`[Linked: <model> ×N]`).

**Acceptance:** a link placed N times can optionally be taken off ×N; off by
default; the count is visible in the chooser; the multiplier persists.

---

## P2.4 — (Optional stretch) background-thread the BOQ rebuild

Only if profiling shows the **host** takeoff (not just links — links are already
cached) janks the UI on large models: move the `BuildBOQDocument` call in
`BOQCostManagerPanel.RefreshAsync` onto a background thread with the existing
`UI/StingProgressDialog`, marshalling the result back for `RefreshDisplay`. Revit
API reads MUST stay on the API thread — read element data into POCOs on the API
thread, compute/group off-thread. If unsure, **skip this** — the link cache already
removed the per-refresh link cost. Don't speculatively multithread.

---

## Constraints & definition of done
- One logical slice per commit; compile-verify (`0 Error(s)`, `-t:Rebuild`) before
  each; deploy per group; tell the human to close Revit + restart to test.
- **No popups** — every new surface renders inline per §1. Reuse engines; **no
  forks**. Use `StingLog` for errors (never silent-catch), `_BIM_COORD` JSON for
  persistence, `[Transaction]` attributes correctly.
- **Do NOT push or merge.** Log every slice in `docs/CHANGELOG.md` with its Revit
  smoke test.
- Suggested order: **P2.1 (grid → S-curve → EVM → export) → P2.2 → P2.3**, then
  P2.4 only if profiling justifies it.
