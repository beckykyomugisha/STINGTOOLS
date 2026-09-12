# Unreachable Commands Triage

Which `IExternalCommand` classes no dispatch layer can reach.

> **Companion doc — the other direction.** This file asks "which *commands* have no
> button?". For "which *buttons* reach no command?", see
> [`SILENT_BUTTONS_TODO.md`](../SILENT_BUTTONS_TODO.md) (repo root), whose 2026-08-06
> re-audit found **zero** dead buttons. Both directions had the same defect and both
> are now derived rather than hand-counted.

**Do not hand-edit the numbers below.** They are produced by
[`tools/recount_unreachable_commands.py`](../tools/recount_unreachable_commands.py),
which prints every list in full and, with `--check`, fails if this file has drifted
from the code. A count in this file drives deletions, so a stale one is an invitation
to delete live code.

```
python tools/recount_unreachable_commands.py            # report
python tools/recount_unreachable_commands.py --check    # CI gate
```

## Counts — re-derived 2026-09-09

- **Total IExternalCommand classes**: **1723**
- **Reached by a dispatch layer**: **1691**
- **Referenced only from non-dispatch code**: **0**
- **Named nowhere outside their own file**: **10**
- **Ambiguous — name declared twice**: **22** (under 11 names)

The four buckets partition all 1723; the script fails if they stop adding up.

**One of the ten is an abstract base class and MUST NOT be deleted.**
`Clash/AccUploadModelCommand.cs` declares `public abstract class
AccUploadCommandBase : IExternalCommand`, the shared body of the two concrete
upload commands. An abstract class cannot be instantiated, so it can never be
dispatched and is correctly counted as unreached — but it is live code, not
a stray. `recount_unreachable_commands.py` does not exclude abstract
declarations, so it will keep appearing here. That matters because of this
file's own rule: a count here drives deletions, and an abstract base listed as
ORPHANED is an invitation to delete the base two working commands inherit from.

*+1 on both totals since the 2026-09-08 (WF-7) derivation:*
`Commands/Materials/RegisterAuditCommand` (`Materials_RegisterAudit`), which is
dispatched from `StingCommandHandler` and has a button on the SETUP tab — so it
lands in "reached", not in any of the three problem buckets.

### What the old 126 was, and why it was wrong

The previous headline — *"Total 1,288 · Wired in `StingCommandHandler.cs` 1,162 ·
Not in dock-panel dispatcher **126**"* — measured against **one** of six dispatch
layers. Dispatch is:

| Layer | Where | Commands reached |
|---|---|---|
| handler switches | `UI/**/…CommandHandler.cs` (6 files) | 1654 |
| `CommandRegistry` modules | `UI/Modules/*CommandModule.cs`, consulted *before* the handler switch | 613 |
| `WorkflowEngine.ResolveCommand` | reachable from a preset, with no button at all | 554 |
| panel code-behind | `**/*.xaml.cs` `Cmd_Click` suite runners | 19 |
| legacy ribbon | `Core/StingToolsApp.cs`, `typeof(X).FullName` → `PushButtonData` | 30 |
| markup + data | `.addin`, `.xaml`, shipped `.json` / `.csv` | 56 |

Layers overlap, so those figures sum to more than 1689; a command reached twice is
counted once in the total.

Two further corrections the re-derivation forced, both of which had inflated the
old figure and neither of which was visible from a single-layer count:

- **A name mentioned in a comment is not a reference.** Before comments were
  stripped, `BatchPrintSheetsCommand` and `ClashDetectionCommand` read as "wired via
  an alternative entry point". Both mentions are prose — a note about a validation
  hook, and a numbered list of features.
- **A dispatcher may declare the command it dispatches.** All 13 `Hub*Command`
  classes live inside `Core/StingToolsApp.cs` alongside the ribbon call that reaches
  them. Skipping a class's own declaring file reported every one as dead.

## The 9 with no reference anywhere

Nothing in any handler, module, workflow, code-behind, ribbon, XAML, `.addin` or
shipped data file names these. Each needs a decision — wire it or delete it —
and neither is made here, because "compiles and is never called" does not say
which was intended.

| Class | Declared in | Note |
|---|---|---|
| `BatchPrintSheetsCommand` | `Docs/SheetTemplateCommands.cs` | Named only in a comment in `Docs/TitleBlockCommands.cs`. Its tag already routes to a newer command. |
| `EnsureSeedsCommand` | `Commands/Placement/EnsureSeedsCommand.cs` | Seed-family ensure step; the penetration presets express this intent through `skipIfFamilyLoaded`, a key the engine does not bind (WF-4). |
| `HubSchedulingDashboardCommand` | `Core/StingToolsApp.cs` | The one `Hub*Command` with no `AddButton` call; its 12 siblings all have one. Most likely an omission when the hub panel was assembled. |
| `PanelScheduleCommand` | `Temp/MEPScheduleCommands.cs` | Superseded by the `Commands/Panels/` suite (Phase 176). |
| `PlatformEventDrainCommand` | `BIMManager/PlatformEvents/PlatformEventDrainer.cs` | Manual drain for the platform event queue. No button, no preset step. |
| `PluginOnboardingWizardCommand` | `BIMManager/PluginOnboardingWizardCommand.cs` | **The Phase 177 note below claimed this was wired under the tag `PlanscapeOnboarding`. That tag appears nowhere in the repository.** |
| `TeamWorkloadCommand` | `BIMManager/GapFixCommands.cs` | `UI/Modules/BimCommandModule.cs:184` says in a comment that it is "implemented directly in BIMCoordinationCenter" — so the class is a superseded duplicate, and the comment is the only trace of the decision. |
| `TierTemplateClearGuidesCommand` | `Tags/TagTierCommands.cs` | Pair with the one below. |
| `TierTemplatePrepCommand` | `Tags/TagTierCommands.cs` | Drops guide notes for tag-tier authoring; the clear-guides command removes them. A usable pair with no way to run either. |

## The 22 whose name is declared twice

A name-reference scan cannot say which of two same-named classes a
`RunCommand<ClashDetectionCommand>` binds — that depends on the referring file's
`using` directives, which is exactly how a dead twin hides behind a live one.
These are reported separately rather than folded into one:

| Name | Declared in |
|---|---|
| `AccessControlCommand` | `BIMManager/CoordinationCenterCommands.cs` · `Commands/PlacementExt/PlacementExtCommands.cs` |
| `ArcFlashCommand` | `Commands/Electrical/ArcFlash/ArcFlashCommand.cs` · `Commands/StandardsExt/StandardsBulkWrappers.cs` |
| `BOQExportCommand` | `BOQ/BOQExportCommand.cs` · `Temp/DataPipelineCommands.cs` |
| `BatchPDFExportCommand` | `Docs/PrintManagerCommands.cs` · `ExLink/AutomationEngine.cs` |
| `ClashDetectionCommand` | `Clash/ClashDetectionCommands.cs` · `Temp/DataPipelineCommands.cs` |
| `CrossModelClashCommand` | `Clash/ClashDetectionCommands.cs` · `Temp/DataPipelineCommands.cs` |
| `EnergyAnalysisCommand` | `Commands/StandardsExt/StandardsBulkWrappers.cs` · `Temp/IoTMaintenanceCommands.cs` |
| `LifecycleCostCommand` | `Commands/StandardsExt/StandardsExtCommands.cs` · `Temp/IoTMaintenanceCommands.cs` |
| `MEPClearanceValidationCommand` | `Clash/ClashDetectionCommands.cs` · `Temp/DataPipelineCommands.cs` |
| `NamingConventionAuditCommand` | `Clash/ClashDetectionCommands.cs` · `Temp/DataPipelineCommands.cs` |
| `StickyNoteDashboardCommand` | `BIMManager/BIMManagerCommands.cs` · `ExLink/StickyNotesEngine.cs` |

Five of the eleven are the same pair of files — `Clash/ClashDetectionCommands.cs`
against `Temp/DataPipelineCommands.cs` — which suggests one wholesale supersession
rather than eleven coincidences. Resolving these is the natural follow-up to this
audit and is tracked in [`ROADMAP.md`](ROADMAP.md).

## Methodology, and what it does not claim

This is a **name-reference analysis, not a call graph**. A class counts as reached
if a dispatch-layer file names it in code (comments stripped). Consequences worth
stating plainly:

- **An entry in "the 9" is not proof of death.** It is proof that no dispatcher,
  markup file or data file names it. A reflectively constructed command would look
  the same. Read the class before deleting it.
- **Being "reached by a layer" is not proof of life** either: a registry entry can
  exist for a button that no panel shows. That is the *other* direction, and it is
  covered by Tier 4 of `tools/check_workflow_wiring.ps1`, which currently finds zero.
- The script carries three instrument checks, because a broken reader reports the
  whole codebase as broken and that reads exactly like a finding: it fails if fewer
  than 800 command classes parse, if any `*CommandHandler.cs` / `*CommandModule.cs` /
  `*CommandRegistry.cs` file is not classified as a dispatch layer, and if the four
  buckets stop summing to the number of declared classes.

---

## Superseded — Phase 177 triage (2026-07-20)

Kept for history. **Its category tables (A: 103, A′: 20, B: 0, C: 3 — total 126)
were a hand-maintained roster of what the script above now derives, and they are no
longer maintained.** One claim in them is false and was corrected by this audit:
`PluginOnboardingWizardCommand` was recorded as "wired with new tags + XAML buttons"
under the tag `PlanscapeOnboarding`; that tag exists nowhere in the repository, and
the command is in "the 9" above.

The three classes Phase 177 called "left as dead code" —
`BatchPrintSheetsCommand`, `ClashDetectionCommand` (Temp variant) and
`PanelScheduleCommand` (Temp variant) — are corroborated: two appear in "the 9", and
`ClashDetectionCommand` appears in the ambiguous list because its Temp twin shares a
name with the live `Clash/` class. That is the strongest independent evidence that
the derivation and the hand triage agree where the hand triage was right.

Phase 177 also recorded two commands as still bypassed —
`DrawingSyncStylesCommand` and `GenerateFromScopeBoxesCommand`, whose claimed alias
tags `DrawingTypes_SyncStylesDirect` / `DrawingTypes_ScopeBoxesDirect` were never
added. Both are now reached by a dispatch layer, so they no longer appear here;
whether the dock-panel button runs the class or the handler's inline
reimplementation is a separate question, tracked as review finding W-5 in
`DRAWINGS_PRODUCTION_REVIEW.md`.
