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
| WP3 | Replace dead/fragile reflection bridges with real APIs | DONE f21dcdf34 |
| WP4 | Atomic writes on coordination stores | DONE 44f3af74f |
| WP5 | Resurrect or remove dead automation (wire it or delete it) | DONE 2e79439ee |
| WP6 | `Core/StingPaths.cs` service + path-discipline grep gate | PARTIAL — per-doc root cache landed (see notes); facade + sibling-writer migration + grep gate still TODO |
| WP7 | Dispatch consolidation — alias tags + parity gate | PARTIAL (see notes) |
| WP8 | Document Manager unification — one register / vocabulary / state machine / audit chain | NOT REACHED — see ROADMAP |
| WP9 | CDE-first tree + ES root identity + migration wizard | NOT REACHED — see ROADMAP |
| WP10 | HTTP + storage hygiene (park if time-boxed) | NOT REACHED — see ROADMAP |
| FINAL | Rebuild, gates, CHANGELOG/ROADMAP, push, report | DONE |

## Where this run stopped

WP0-WP5 completed in full and WP7 partially, each committed green at 0/0. WP6, WP8, WP9
and WP10 were **not reached** — they were never started, so there is no half-applied
change to unpick. Every deferred item is written up in `docs/ROADMAP.md` with enough
detail to resume cold, including a sequencing note (WP9 should follow WP6, because with
`StingPaths` in place the tree change is one resolver rather than every writer).

No package needed the stop-loss rule: nothing was reverted, and the branch is green.

## Scope decisions taken during the run

- **WP1.3 / WP2 split.** The work order lists the `_bim_manager` writers under WP1.3 and the
  issues/meetings/documents store split under WP2. Those are the same ~35 call sites, so WP1.3
  covers only the `_CDE/` and `STING_Exports` writers (distinct roots, no overlap) and the
  `_bim_manager` sweep is done once, properly, through WP2's `CoordStores` resolver. Touching
  them twice would have meant re-editing every site.
- **WP7 was scoped down honestly.** The work order asked for alias tags plus a parity
  gate. The gate revealed 183 unreachable panel tags, not the ~6 the review named.
  Aliasing 183 tags blind would have been worse than the drift, so the six named cases
  were fixed properly, the rest were recorded as a baseline the gate enforces against,
  and the gap went to `ROADMAP.md`. The gate was verified in both directions: it passes
  on the current tree and fails when a new unwired tag is injected.
- `UI/DocumentManagementDialog.LoadExportIndex` still *reads* a legacy `STING_Exports` folder.
  That is deliberate: nothing writes there any more, and `MigrateFromLegacy` sweeps it, but the
  reader keeps pre-migration projects visible in the Document Manager.

## Review claims that were already fixed on `main`

The review branch predates several fixes now on `main`. These WP5 items were
**verified against the current tree and found already correct** — no change was made,
and none should be "re-fixed" by a later session:

| Review claim | Actual state on `main` |
|---|---|
| `StingStaleMarker.Register` has zero callers | Called from `StingToolsApp.OnStartup` (~L147) |
| `CableManifestUpdater` orphaned | Registered at startup |
| `HvacEnvelopeStaleUpdater` orphaned | Registered at startup (~L154) |

Genuinely dead and therefore fixed in WP5: `SLDSyncUpdater`, `LiveStandardsUpdater`,
`SlaScanner.Scan`, `PluginUpdateChecker.CheckAsync`, `DeliverableServerSync.FireAndForget`,
`WorkflowScheduler.LoadFromConfig`, `ProjectFolderEngine.FileChanged`.

## Post-review fixes (follow-up commit)

Three items surfaced by the code review of this branch, fixed directly here:

| Item | Fix |
|---|---|
| **HIGH** — `DocumentManagementDialog.GetBimManagerDir` still resolved the raw `<rvtDir>/STING_BIM_MANAGER` sibling, so after WP2 moved `BIMManagerEngine`/`WarningsManager`/BCC to `<root>/_data/STING_BIM_MANAGER` the Document Manager and BIM Coordination Center diverged (the exact issues/register/meetings split WP2 targeted). | Routed `GetBimManagerDir` through `ProjectFolderEngine.GetMetaPath(doc, "STING_BIM_MANAGER")` (sibling fallback only for unsaved docs) and moved all 5 dialog transmittal sites to `CoordStores.Transmittals(doc)` — the dialog now shares the canonical stores. |
| **LOW** — the "already fixed on `main`" note wrongly listed `GetAvailablePresets` as de-duplicated; it was still triplicated on both `main` and this branch. | Removed the 2 redundant `RemoveAll`/cache blocks and corrected this table. |
| **LOW** — `tools/check_dispatch_parity.ps1` `$RepoRoot` default threw under `powershell -File` (empty `$PSScriptRoot`). | Added a `$MyInvocation.MyCommand.Path` fallback with a clear error if the root still can't be derived. |

## WP6 (partial) — per-document root cache

`ProjectFolderEngine` cached the resolved project root in a single static `_rootPath` and
also treated it as the fast-path in `GetRootPath`. A second project opened before its own
`project_setup.json` was bootstrapped would return the *first* project's root — writing its
exports into the wrong project's folder tree (cross-document contamination, flagged in the
review). `SaveRootToConfig` then persisted that computed root as `PROJECT_FOLDER_ROOT`, so the
leak survived into the next session as a global override.

Fix: added an authoritative per-document cache (`_rootByDoc`, keyed on `.rvt` path, cleared on
document close), demoted `_rootPath` to an *explicit global override only* (never written from
per-doc computation), added `_lastResolvedRoot` purely for the document-less `RootPath` display
getter, and made `SaveRootToConfig` persist `PROJECT_FOLDER_ROOT` only when a real override is set.

Also fixed the `OptionFolderManager` nesting: per-option deliverable folders minted
`20_MISC/_BIM_COORD/options/...` (a third `_BIM_COORD` one level deep inside an export
bucket — a literal "folder repeated inside another folder"). They now mint under the WIP
CDE container (`<WIP>/options/<set>/<option>/...`), matching the header's stated intent.

Still TODO for WP6: the `Core/StingPaths.cs` facade, migrating the ~40 remaining hardcoded
`<rvtDir>/_BIM_COORD` sibling writers (review Part 1 §A2) onto `GetMetaPath`, and the
`tools/check_path_discipline` grep gate. Tracked in `docs/ROADMAP.md`.

## Notes for a resuming session

- Never commit to `main`; this checkout is shared by other agents — confirm `git branch --show-current`
  before any destructive git command.
- Never deploy DLLs to `C:\Dev\STING_PLACEMENT_GOLD` or a Revit Addins folder.
- Revit cannot be driven headless: every package records its in-Revit verification steps in the
  commit body, collected into the final report.
