# BOQ 5D Cost Manager — Enhancement Implementation Prompt

You are a terminal coding agent working on the **StingTools** C# Revit plugin.
This prompt asks you to harden and extend the **BOQ & Cost Manager** inline
workspace + linked-models feature that already landed on this branch. Implement
the tasks in order (P0 → P1 → P2), compile-verifying and committing each, and
deploying after each so the human can smoke-test in Revit.

---

## 0. Environment, build & deploy protocol (READ FIRST — non-obvious)

- **Work in this worktree:** `C:\Dev\STINGTOOLS-boq-impl`, branch
  `claude/boq-implementation`. Do **not** build from the main checkout
  `C:\Dev\STINGTOOLS` — it sits on a different branch and does **not** contain
  the BOQ work. `git status` / `git branch --show-current` before you touch
  anything.
- **The plugin builds headlessly here** via the csproj's Nice3point reference-
  assembly fallback — the old "can't compile in sandbox" caveat is **obsolete**.
  You MUST compile-verify every change:
  ```bash
  dotnet build StingTools/StingTools.csproj -c Release -t:Rebuild --nologo -v minimal
  ```
  A clean forced rebuild is ~30 s and must report `0 Error(s)`. (Incremental
  builds report `Build succeeded` in ~1.5 s when nothing changed — use
  `-t:Rebuild` to truly recompile your edits.)
- **Deploy = a single file copy, but Revit hard-locks it.** The `.addin` loads
  from the absolute path `C:\Dev\STINGTOOLS\CompiledPlugin\StingTools.dll`
  (NOT the worktree bin). Deploy with:
  ```bash
  cp /c/Dev/STINGTOOLS-boq-impl/StingTools/bin/Release/StingTools.dll \
     /c/Dev/STINGTOOLS/CompiledPlugin/StingTools.dll
  ```
  If this fails with `Device or resource busy`, **Revit is open** — the human
  must close it first. Confirm the deployed file's timestamp updated after each
  copy. Source commits alone change nothing in a running Revit; the DLL must be
  rebuilt + copied + Revit restarted.
- **Git discipline:** commit per task (imperative subject + the
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer). **Do NOT
  push or merge** unless explicitly told. Many agents share `C:\Dev\STINGTOOLS`;
  never `reset --hard` without verifying the branch.
- Log each task in `docs/CHANGELOG.md` (prepend a `#### Completed (...)` block
  above the most recent one). Since builds now compile-verify, state
  `Compile-verified headless (Nice3point): 0 errors` rather than the old
  no-build caveat. **Behaviour still needs a real Revit run** — say so.

### Current state you are building on (already shipped this branch)
- **Inline Actions workspace:** `UI/BOQCostManagerPanel.cs` Actions tab is a
  2-column master-detail (`BuildActionsTab` → `BuildActionReportPane`,
  `_actionReportHost` / `_actionReportTitle` / `_actionsTab`). `RunActionInline`
  registers inline sinks then dispatches; `ShowInlineResult(b)` renders into the
  Actions pane when `_mainTabs.SelectedItem == _actionsTab`.
- **Inline pickers/results (no popups):**
  - `UI/StingListPicker.cs` (namespace **`StingTools.Select`**) has statics
    `InlineHost` (Border) + `InlineTitleSink`, a `_inlineFrame` DispatcherFrame,
    `ShowInline(string title)`, and `FinishInlineOrClose()`. Both `Show(...)`
    overloads short-circuit to `ShowInline` when `InlineHost != null`.
    `PopulateList` honours `ListItem.IsSelected` (pre-check).
  - `UI/StingResultPanel.cs` has a static `InlineSink`; `Builder.Show()` routes
    to it instead of `ShowDialog` when set.
  - `UI/StingCommandHandler.cs` `Execute(...)` `finally` (near
    `ClearAllExtraParams()`) clears `StingTools.Select.StingListPicker.InlineHost`
    / `InlineTitleSink` and `StingResultPanel.InlineSink`.
- **Linked models (per-link, persisted):** `BOQ/BOQCostManager.cs` has
  `GetIncludedLinkTitles(doc)` / `SetIncludedLinkTitles(doc, titles)` backed by
  `<project>/_BIM_COORD/boq_links.json` + `_linkInclusionCache`; `BuildBOQDocument`
  STEP 6c calls `CollectLinkedItems(doc, knownCats, csvRates, cobieCostCodes,
  grouping, includedTitles)`. Linked rows are neutralised for host write-back
  (`RevitElementId = -1`, `UniqueId = ""`, `ConstituentElementIds` cleared) and
  carry `SourceModel = <link Title>` + a `[Linked: <model>]` Note.
  `BoqGroupingMode.SourceModel` groups host vs each link; header `⛓ Links (N)`
  button (`_linksBtn` / `UpdateLinksBadge` / `ChooseLinkedModels`).

---

## P0 — Harden the inline message pump (do first; correctness/safety)

**Why:** `StingListPicker.ShowInline` pumps a nested `Dispatcher.PushFrame`
loop on the Revit API thread. Pumping a WPF message loop **while a Revit
transaction is open is unsafe** and can corrupt document state. Also the inline
statics are process-global; a leak would make unrelated pickers render into a
closed BOQ pane.

### P0.1 — Transaction-state guard (fall back to modal mid-transaction)
- The panel must tell the picker which `Document` is hosting. Add a static
  `public static Document InlineHostDoc;` to `StingListPicker` (it already uses
  `Autodesk.Revit.DB`). In `BOQCostManagerPanel.RunActionInline`, set
  `StingTools.Select.StingListPicker.InlineHostDoc = Doc;` alongside `InlineHost`,
  and clear it in `StingCommandHandler.Execute`'s `finally` with the others.
- In **`ShowInline`** (and at the top of the two `Show` inline gates), before
  pumping: if `InlineHostDoc != null && InlineHostDoc.IsModifiable` (a
  transaction is open), **do NOT pump** — fall through to the normal modal
  `ShowDialog()` path instead. Mirror the precedent in
  `Core/Drawing/DrawingTypeStamper.StampCrop` which guards on
  `el.Document.IsModifiable`. Log one `StingLog.Info` when it falls back so the
  human can see which command needed it.
- **Audit the Actions commands** under `Commands/Cost/*.cs` +
  `BOQ/*Commands*.cs` for any `StingListPicker.Show(...)` called **after** a
  `new Transaction(...).Start()`. List them in the CHANGELOG. (The guard makes
  them safe regardless, but they should be known.)

### P0.2 — Leak-proof the inline statics
- At the **top** of `RunActionInline` (before `HighlightActionButton`), defensively
  clear any stale inline registration from a prior aborted run, then set fresh.
- Clear the inline statics when the panel/window unloads. Find where the panel is
  torn down (the `BOQCostManagerWindow` host in `UI/BOQCostManagerWindow.cs`, or a
  `Unloaded` handler on the `UserControl`) and null out `InlineHost`,
  `InlineTitleSink`, `InlineHostDoc`, and `StingResultPanel.InlineSink` there.

**Acceptance (P0):** builds clean; with a transaction open the picker shows
modally (no pump); statics are null whenever no Actions command is mid-flight.
**Smoke test for the human:** run **Variation from Diff** (it shows 4 sequential
pickers) and **Issue Cert** from the Actions tab — confirm each picker renders
inline, OK/Cancel return correctly, and Revit never hangs.

---

## P1 — High value, low risk

### P1.1 — Surface `SourceModel` as a first-class column (grid + exports)
**Why:** provenance currently rides only the `[Linked: …]` Note string. The
`BOQLineItem.SourceModel` field already exists — expose it so a QS can
sort/filter host-vs-link without parsing text.
- **Grid:** in `BOQCostManagerPanel` where the DataGrid columns are built
  (around the `DataGridTextColumn` block, ~line 1445; "Ref" column is the
  anchor), add an optional **"Model"** column bound to `SourceModel` (show
  `"Host"` when blank). Make it toggle with the existing column-visibility
  dropdown (the comment near line 86 / 612 lists Level/Location/Source/Confidence/
  Carbon/Note toggles — add Model there). Add `SourceModel` to the
  `BOQItemViewModel` if the grid binds to a view-model wrapper.
- **Exports:** `BOQ/BOQExportCommand.cs` already writes a **Note** column
  (main sheet col 13 ~line 175/213; audit sheet col 14 ~line 251/271). Add a
  dedicated **"Source Model"** column next to Note on both sheets (write
  `item.SourceModel ?? "Host"`). Do the same in
  `BOQ/BOQProfessionalExportCommand.cs` takeoff/audit sheet if it lists rows.
- Keep the `[Linked: …]` Note tag too (don't remove — other tooling may read it).

**Acceptance:** Model column visible/toggleable in the grid and present in both
XLSX exports; host rows read "Host", linked rows read the link Title.

### P1.2 — Don't re-traverse links on every refresh (performance)
**Why:** `BOQCostManagerPanel.RefreshAsync()` runs `BuildBOQDocument(Doc, …)`
**synchronously on the UI thread**, and STEP 6c walks **every linked document**
each rebuild. On a real federated model this freezes Revit on every rate edit /
filter toggle.
- **Cache the per-link takeoff** in `BOQCostManager`. Key a cache on the link
  document's identity (e.g. `linkDoc.PathName` + `linkDoc.GetHashCode()` or a
  cheap change signal such as the link's `GetWorksharingTooltipInfo`/last-write
  if available; if no reliable signal exists, key on `PathName` and expose
  `InvalidateLinkCache()` called from `ChooseLinkedModels` and on document close).
  Return cloned line items from the cache so callers can mutate freely.
- Only re-run a link's `CollectCandidateElements` + per-link aggregate when its
  cache entry is missing/invalidated. Host takeoff stays as-is.
- Wire `InvalidateLinkCache()` into `StingToolsApp.OnDocumentClosing` next to the
  other per-doc cache invalidations, and call it from `SetIncludedLinkTitles`
  (selection changed ⇒ recompute is fine, but a reload of the *same* selection
  must hit the cache).
- **Stretch (optional):** if the host takeoff itself is slow on big models, move
  the whole `BuildBOQDocument` call in `RefreshAsync` onto a background thread
  with the existing `UI/StingProgressDialog`, marshalling the result back to the
  UI thread for `RefreshDisplay`. Only do this if profiling shows the host
  takeoff (not just links) is the bottleneck — Revit API reads must stay on the
  API thread, so this requires care (read element data into POCOs on the API
  thread, compute/group off-thread). If unsure, **leave host single-threaded and
  only cache links** — that alone removes the per-refresh link cost.

**Acceptance:** toggling a filter / editing a rate with links enabled does not
re-walk link docs (verify via a `StingLog.Info` count that fires only on cache
miss); first enable still computes once.

> **DONE (post-review):** P1.2 landed (commit `1e395a471`). A follow-up also
> wired the header **↻ Refresh** button to call `InvalidateLinkCache()` before
> rebuilding, so a link **Reloaded** mid-session is picked up on explicit Refresh
> (the cache otherwise only auto-invalidates on document close). Do **not**
> re-implement P1.2 — extend it only if profiling still shows a problem.

### P1.3 — Persist Cost Manager UI state per project
**Why:** the link selection persists (`boq_links.json`) but grouping mode,
display currency, column visibility, and the expand/collapse sets reset every
session — the same sustainability gap, still open.
- Add `<project>/_BIM_COORD/boq_ui_state.json` (same load/save shape as
  `BoqPrintProfile` ~line 138 / `ProjectRateCardProvider` ~line 50:
  `Path.Combine(parent, "_BIM_COORD", ...)` + `JsonConvert`). Persist:
  `_groupingMode`, `_displayCurrency`, the visible-column set, and optionally
  `_openSections` / `_openMaterialSections`.
- Load it when `Doc` is first set / first `RefreshAsync`; save on each change
  (grouping combo, currency toggle, column toggle, expand/collapse). Keep it
  best-effort (try/catch + `StingLog.Warn`), never block the UI.

**Acceptance:** set grouping = "Source model", USD, hide a column, reopen the
project → all restored.

---

## P2 — Larger slices (only after P0/P1 land + smoke-test green)

### P2.1 — 4D / Schedule tab (the real 5D payoff)
Add a **Schedule** tab to the Cost Manager `_mainTabs`, reusing the existing
`BIMManager/SchedulingCommands.cs` `Scheduling4DEngine` (32-trade sequences,
cash-flow forecasting, Gantt). Render **inline** in the same master-detail
pattern as Actions (no popups): phase-date assignment, a cash-flow S-curve
(simple WPF polyline/bars — no external chart lib), and EVM (PV/EV/AC/SPI/CPI)
pulled from `Core/Evm`. Editable grids/combos for phase dates + % complete.
Scope this as its own multi-commit slice with its own short design note; do not
fork `Scheduling4DEngine` — call it.

### P2.2 — Inline editable forms for input-heavy actions
Some actions (Set % Complete, EVM actuals) want an inline editable grid, not a
pick-then-report list. Build a small reusable inline-form host in the Actions
pane (combos + numeric fields + a Run button) and route those specific commands
through it via the ExtraParam contract (command reads values instead of popping
its own dialog). Convert 2–3 as a proof, then sweep.

### P2.3 — Linked-instance multiplier (correctness option)
`CollectLinkedItems` dedups by link **Title**, so a model legitimately placed
**twice** (mirrored wings) is taken off **once** (undercount). Add an optional
per-link instance count: when a link Title has N loaded `RevitLinkInstance`s,
offer to multiply its quantities by N (default off — a shared reference model
placed once is the common case). Surface the instance count in the
`ChooseLinkedModels` picker `Detail`.

---

## Constraints & definition of done
- One logical change per commit; compile-verify (`0 Error(s)`) before each commit;
  deploy after each task and tell the human to restart Revit + smoke-test.
- Reuse engines/helpers — **no forking** `BOQCostManager`, `Scheduling4DEngine`,
  `StingListPicker`, `StingResultPanel`. Follow existing conventions
  (`StingLog`, `_BIM_COORD` JSON overlays, `[Transaction]` attributes,
  `TaskDialog` for unavoidable messages).
- Keep linked rows **read-only for host write-back** — never let a linked
  element's id reach `doc.GetElement` (the `RevitElementId <= 0` guard in
  `IfcQuantitySetWriter.StampAllElements` + `CostStamp` must keep skipping them).
- Do **not** push or merge. Log every task in `docs/CHANGELOG.md`.
- Each task's CHANGELOG entry must list its **Revit smoke test** (the human runs
  it — you cannot). The inline message pump (P0) MUST be exercised in Revit
  before P2 begins: run a 4-picker command (Variation from Diff) and confirm no
  hang and no mid-transaction pump.

## Suggested order
P0.1 → P0.2 → **(human smoke-test)** → P1.1 → P1.2 → P1.3 → **(human smoke-test)**
→ P2.1 → P2.2 → P2.3.
