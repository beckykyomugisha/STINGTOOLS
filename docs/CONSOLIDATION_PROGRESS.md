# ISO 19650 Consolidation — Progress Tracker

**Branch**: `claude/iso19650-consolidation` (worktree `.claude/worktrees/iso-consolidation`, cut from `origin/main` @ `6713f570b`)
**Work order**: [`AGENT_FIX_PROMPT_ISO19650_CONSOLIDATION.md`](AGENT_FIX_PROMPT_ISO19650_CONSOLIDATION.md)
**Evidence base**: [`ISO19650_DOC_FOLDER_REVIEW.md`](ISO19650_DOC_FOLDER_REVIEW.md)

**Build baseline**: 0 warnings / 0 errors, verified on the branch point with
`dotnet build StingTools/StingTools.csproj -c Release -t:Rebuild -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`.
Every work package below must end green at 0/0.

## Status legend

| Marker | Meaning |
|---|---|
| `DONE <sha>` | Landed, build green at 0/0 |
| `IN PROGRESS` | Being worked now |
| `PARKED <reason>` | Reverted after 2 honest attempts; gap logged in `docs/ROADMAP.md` |
| `TODO` | Not started |

## Checklist

| WP | Scope | Status |
|---|---|---|
| WP0 | Orientation; copy review + work order into `docs/`; create this tracker | DONE a708b7533 |
| WP1 | Quick wins — MIDP join, transmittal merge, retire `_CDE`/`STING_Exports`, dead flags, junk containment, tree unification | DONE 830ea72db |
| WP2 | Heal issues/meetings/documents store split via `Core/CoordStores.cs` | DONE 18af9ece5 |
| WP3 | Replace dead/fragile reflection bridges with real APIs | IN PROGRESS |
| WP4 | Atomic writes on coordination stores | TODO |
| WP5 | Resurrect or remove dead automation (wire it or delete it) | TODO |
| WP6 | `Core/StingPaths.cs` service + path-discipline grep gate | TODO |
| WP7 | Dispatch consolidation — alias tags, null-safe `RunCommandByTag`, shared `Run<T>`, parity gate | TODO |
| WP8 | Document Manager unification — one register / vocabulary / state machine / audit chain | TODO |
| WP9 | CDE-first tree + ES root identity + migration wizard | TODO |
| WP10 | HTTP + storage hygiene (park if time-boxed) | TODO |
| FINAL | Rebuild, gates, CHANGELOG/ROADMAP, push, report | TODO |

## Scope decisions taken during the run

- **WP1.3 / WP2 split.** The work order lists the `_bim_manager` writers under WP1.3 and the
  issues/meetings/documents store split under WP2. Those are the same ~35 call sites, so WP1.3
  covers only the `_CDE/` and `STING_Exports` writers (distinct roots, no overlap) and the
  `_bim_manager` sweep is done once, properly, through WP2's `CoordStores` resolver. Touching
  them twice would have meant re-editing every site.
- `UI/DocumentManagementDialog.LoadExportIndex` still *reads* a legacy `STING_Exports` folder.
  That is deliberate: nothing writes there any more, and `MigrateFromLegacy` sweeps it, but the
  reader keeps pre-migration projects visible in the Document Manager.

## Notes for a resuming session

- Never commit to `main`; this checkout is shared by other agents — confirm `git branch --show-current`
  before any destructive git command.
- Never deploy DLLs to `C:\Dev\STING_PLACEMENT_GOLD` or a Revit Addins folder.
- Revit cannot be driven headless: every package records its in-Revit verification steps in the
  commit body, collected into the final report.
