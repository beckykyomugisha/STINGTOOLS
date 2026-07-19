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
| WP6 | `Core/StingPaths.cs` service + path-discipline grep gate | NOT REACHED — see ROADMAP |
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
| `GetAvailablePresets` block duplicated 3× | Single block, plus a built-in preset cache |

Genuinely dead and therefore fixed in WP5: `SLDSyncUpdater`, `LiveStandardsUpdater`,
`SlaScanner.Scan`, `PluginUpdateChecker.CheckAsync`, `DeliverableServerSync.FireAndForget`,
`WorkflowScheduler.LoadFromConfig`, `ProjectFolderEngine.FileChanged`.

## Notes for a resuming session

- Never commit to `main`; this checkout is shared by other agents — confirm `git branch --show-current`
  before any destructive git command.
- Never deploy DLLs to `C:\Dev\STING_PLACEMENT_GOLD` or a Revit Addins folder.
- Revit cannot be driven headless: every package records its in-Revit verification steps in the
  commit body, collected into the final report.
