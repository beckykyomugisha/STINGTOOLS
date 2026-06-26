# BOQ Cost Manager — Slice 3: convert all Actions to INLINE reporting

You are a terminal coding agent on the **StingTools** C# Revit plugin. The BOQ
& Cost Manager **Actions** tab is a master-detail surface: buttons on the left, a
single **report pane on the right**. Inline **input pickers** already work; what
remains is **inline results** — most Actions commands still report through Revit
`TaskDialog` popups, so their result never lands in the pane. Your job: convert
the Actions-dispatched commands to report via `StingResultPanel` so **every
action renders its result in the right pane, with no popup**.

---

## 0. Environment, build & deploy (READ FIRST)

- **Work in this worktree:** `C:\Dev\STINGTOOLS-boq-impl`, branch
  `claude/boq-implementation`. Verify with `git branch --show-current` before
  editing. Do **not** build from `C:\Dev\STINGTOOLS` (different branch).
- **Compile-verify every change** (Nice3point headless build works here — the old
  "can't build in sandbox" caveat is dead):
  ```bash
  dotnet build StingTools/StingTools.csproj -c Release -t:Rebuild --nologo -v minimal
  ```
  Must report `0 Error(s)`. Use `-t:Rebuild` to truly recompile your edits.
- **Deploy** (the `.addin` loads this absolute path; Revit hard-locks it):
  ```bash
  cp /c/Dev/STINGTOOLS-boq-impl/StingTools/bin/Release/StingTools.dll \
     /c/Dev/STINGTOOLS/CompiledPlugin/StingTools.dll
  ```
  `Device or resource busy` ⇒ Revit is open; the human must close it. Confirm the
  deployed timestamp updated. Source commits alone change nothing in a running
  Revit.
- **Commit per file/group**, imperative subject + `Co-Authored-By: Claude Opus
  4.8 <noreply@anthropic.com>` trailer. **Do NOT push or merge.** Log in
  `docs/CHANGELOG.md`. Each task's CHANGELOG entry must state its Revit smoke
  test (the human runs it).

---

## 1. How inline reporting already works (you do NOT touch the panel)

The plumbing is done and shipped on this branch — **do not re-implement it**:
- `StingResultPanel.InlineSink` (static `Action<Builder>`). When set,
  `Builder.Show()` routes the result to the sink **instead of** popping a window.
- `BOQCostManagerPanel.RunActionInline` sets that sink (→ `ShowInlineResult`,
  which renders into the Actions pane), sets `InlineHost=1`, then dispatches.
- `StingCommandHandler.Execute`'s `finally` clears the sink and invokes
  `BOQCostManagerPanel.PendingActionResolve`, which — if the command did NOT post
  an inline result — resolves the "Running…" placeholder to "✓ completed
  (reported in a dialog)". **That safety net is why un-converted commands no
  longer look stuck — but the goal is to make them render inline instead.**

**Therefore: the ONLY change you make is inside each command — swap its terminal
`TaskDialog.Show(...)` report for a `StingResultPanel.Create(...)…Show()`.** When
you do, `Builder.Show()` automatically lands it in the pane (and flips
`_inlineResultPosted` true so the placeholder-resolver no-ops). **No edits to
`BOQCostManagerPanel`, `StingResultPanel`, `StingListPicker`, or
`StingCommandHandler` are needed or wanted.**

---

## 2. Scope — the Actions-dispatched commands

The Actions tab (`BuildActionsTab` in `UI/BOQCostManagerPanel.cs`) dispatches
tags backed by these files (all currently report via `TaskDialog`, none via
`StingResultPanel`):

| File | TaskDialog calls | Representative tags / actions |
|---|---|---|
| `Commands/Cost/CostCommands.cs` | 7 | `Cost_RunWorkflow`, `Cost_ClearStale`, `Cost_ToggleStaleMarker`, `Cost_MigrateCurrencyParams`, `Cost_MigrateEs*`, `Cost_ReloadRules`, `Cost_ValidateAll` |
| `Commands/Cost/CostPlanCommands.cs` | 6 | `CostPlan_Create`, `CostPlan_Update`, `CostPlan_Export`, `CostPlan_CompareStages`, `CostPlan_SetBudget` |
| `Commands/Cost/PaymentCertCommands.cs` | 7 | `PayCert_Create`, `PayCert_Export`, `PayCert_Reconcile`, `PayCert_Sign` |
| `Commands/Cost/VariationAndEvmCommands.cs` | 15 | `Var_Create`, `Var_Approve`, `Evm_Calculate`/`EVM_Dashboard`, `EVM_Export`, `EVM_Forecast` |
| `Commands/Cost/IfcAndIcmsCommands.cs` | 4 | `ICMS_Export`, `IFC_CostIngest`, `ICMS_Validate`, `IFC_CostBridge` |
| `Commands/Cost/MeasurementStandardCommands.cs` | 2 | `Meas_SetNRM`, `Meas_SetSMM7`, `Meas_SetCIOS`, `Meas_Audit`, `Meas_RuleReport` |
| `Commands/Cost/CostControlCommands.cs` | 3 (2 already use SRP) | `Cost_AnticipatedFinalCost`, `Cost_StandardInspect`, `ReconcileProvisionals` |

Plus the `BOQ*` tags already on the Actions tab (`BOQQsExport`, `BOQQsImport`,
`BOQAddManualRow`) — audit those too; convert any terminal `TaskDialog` report.

**First step:** enumerate the action tags by reading `BuildActionsTab`, map each
tag → command class (via `StingCommandHandler`'s switch + the `[…]` command
files), and build a checklist. Work file-by-file.

---

## 3. The conversion pattern

For each command, replace the **terminal result** `TaskDialog.Show(title, body)`
with a `StingResultPanel`. Keep the command's actual work/transactions identical
— only the reporting surface changes.

**Before:**
```csharp
TaskDialog.Show("STING Cost", $"Cleared {cleared} stale flag(s).");
return Result.Succeeded;
```
**After:**
```csharp
var rp = StingResultPanel.Create("Clear stale flags");
rp.AddSection("RESULT").Metric("Stale flags cleared", cleared.ToString());
rp.Show();   // routes inline automatically when the Actions pane is hosting
return Result.Succeeded;
```

Richer reports use multiple sections / metrics / text rows (see the existing
inline-capable example `BOQ/BOQExportIfcQtoCommand.cs` and the 2 SRP users in
`CostControlCommands.cs` for the API shape — `Create` / `SetSubtitle` /
`AddSection` / `.Metric(k,v)` / `.Text(...)`).

### Rules
1. **Terminal success/summary dialogs → `StingResultPanel`.** Always.
2. **Early-return guards** ("No active document", "Select a section first",
   "Pick a baseline"): prefer a one-line `StingResultPanel.Create(title)
   .AddSection("…").Text(msg).Show()` so they also land inline. You MAY leave a
   `TaskDialog` only for a genuine hard block that must interrupt the user (note
   any you keep, and why, in the CHANGELOG).
3. **Do NOT** alter command logic, transaction scope, parameter writes, or the
   `[Transaction]` attribute. Reporting surface only.
4. **Do NOT** set/clear `StingResultPanel.InlineSink` yourself — the panel owns
   it. Your command just calls `rp.Show()`.
5. Commands invoked from BOTH the Actions tab AND the ribbon/workflow still work:
   when no inline sink is set (ribbon), `Builder.Show()` falls back to the modal
   dialog automatically. So conversion is safe everywhere.
6. Long/multi-step commands (e.g. `Cost_RunWorkflow`) may show progress via the
   existing `StingProgressDialog` during the run, but their **final** report must
   be a `StingResultPanel` (a summary: steps run, totals, pass/fail).

### Verify each command is actually wired to the Actions tab
A few of the listed tags may not be on the Actions tab (some are ribbon-only).
Convert the ones reachable from `BuildActionsTab` first (that's the user-visible
gap); convert the rest opportunistically for consistency, but they're lower
priority.

---

## 4. Order of work (smallest blast radius first)
1. `CostCommands.cs` (7) — incl. `Cost_RunWorkflow`, the action the user tested.
2. `MeasurementStandardCommands.cs` (2) + `IfcAndIcmsCommands.cs` (4).
3. `CostPlanCommands.cs` (6).
4. `PaymentCertCommands.cs` (7).
5. `VariationAndEvmCommands.cs` (15) — largest; the pickers already render inline,
   so only the result dialogs remain.
6. `CostControlCommands.cs` (3 remaining) + `BOQ*` Actions tags.

Build + commit after each file. Deploy after each logical group so the human can
smoke-test incrementally.

---

## 5. Acceptance & smoke test
- **Build:** `0 Error(s)` on `-t:Rebuild` before every commit.
- **Per file:** every converted command's terminal report renders in the Actions
  right pane; the pane never shows the bare "Running…" placeholder after the
  command finishes, and **no `TaskDialog` pops** for a normal success path.
- **Regression:** dispatching the same command from the **ribbon** (no inline
  sink) still shows its result as a modal dialog (the automatic fallback).
- **Human smoke test (state it in CHANGELOG):** Actions tab → click **Run Cost
  Workflow**, **Calculate EVM**, **New Cost Plan**, **Issue Cert**, **Validate
  Cost** — each result appears in the right pane, no popups; the input pickers
  for New Cost Plan / Issue Cert / Variation still render inline (they already
  do — don't regress them).

## 6. Constraints
- One file (or small group) per commit; compile-verify first; deploy per group.
- Reporting surface only — **no logic, transaction, or panel changes**. No fork
  of `StingResultPanel` / `BOQCostManager`.
- Use `StingLog` for errors; never silent-catch. **Do not push or merge.**
- Log every file converted in `docs/CHANGELOG.md`, listing any `TaskDialog` you
  deliberately kept (hard blocks) and why.
