# AGENT PROMPT — StingTools ISO 19650 Consolidation (autonomous fix run)

> Paste everything below the line into a fresh Claude Code session opened at `C:\Dev\STINGTOOLS`.
> Companion evidence document: `docs/ISO19650_DOC_FOLDER_REVIEW.md` (read it first — it contains all file:line findings).

---

## MISSION

You are working autonomously in the **StingTools** repo — a C# .NET 8 (`net8.0-windows`) Revit 2025/2026 plugin. Your mission is to consolidate the fragmented folder-management, document-management, automation, and integration layers into **one coherent ISO 19650 system**, executing the work packages below **in order**, committing after each one. Do not ask for permission between packages. A full evidence base with file:line references is in `docs/ISO19650_DOC_FOLDER_REVIEW.md` — read it before writing any code.

## GROUND RULES (non-negotiable)

1. **Isolation**: this checkout is shared by multiple agents. Work in a dedicated git worktree on a new branch cut from latest `origin/main`:
   `git fetch origin && git worktree add .claude/worktrees/iso-consolidation -b claude/iso19650-consolidation origin/main`
   Never commit to `main`. Before ANY destructive git command, run `git branch --show-current` and confirm you are on your branch.
2. **Build verification is mandatory** — this machine CAN build the plugin. After EVERY work package run:
   `dotnet build StingTools/StingTools.csproj -c Release -t:Rebuild -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`
   The baseline is **0 warnings / 0 errors**. Keep it there. Never commit a red or newly-warning build. Never write "built without dotnet build verification" — that caveat does not apply on this machine.
3. **Do NOT deploy.** Never copy DLLs to `C:\Dev\STING_PLACEMENT_GOLD` or any Revit Addins folder. Deployment is a manual human step.
4. **Repo conventions** (from CLAUDE.md): read before edit; targeted edits over rewrites; `[Transaction(TransactionMode.Manual)]` for state-changing commands, `ReadOnly` for queries; every DB change inside a named `Transaction` ("STING …"); `TaskDialog` not MessageBox; `StingLog.Info/Warn/Error` — no silent catch blocks; `FilteredElementCollector` with filters; PascalCase public / camelCase locals; no new files unless the work package requires them.
5. **JSON discipline**: whenever you touch a `STING_*.json` / store schema, verify the Newtonsoft deserialization types in the consuming C# POCO match the JSON field types — valid JSON with mismatched types is runtime-dead and still builds green.
6. **Runtime caveat**: you cannot run Revit headless. For each package, list in the commit body what needs in-Revit verification.
7. **Progress tracking for resumability**: maintain `docs/CONSOLIDATION_PROGRESS.md` — a checklist of WP0–WP10 with status (`DONE <commit-sha>` / `IN PROGRESS` / `PARKED <reason>`). Update it in every commit. If your session ends mid-run, the next session resumes from this file.
8. **Stop-loss**: if a package still breaks the build after 2 honest attempts, revert it cleanly, mark it `PARKED` with the reason in `docs/CONSOLIDATION_PROGRESS.md` and a gap entry in `docs/ROADMAP.md`, and move to the next package. Never leave the branch red.
9. **Bookkeeping**: at the end of the run append one `#### Completed (Phase N — ISO 19650 Consolidation)` block to `docs/CHANGELOG.md` summarising all landed packages, and push the branch. Do not open a PR and do not merge.

## ARCHITECTURE TARGET (north star — do not deviate)

**One root, two zones.** Per project, StingTools creates exactly ONE folder beside the `.rvt`:

```
<rvtDir>/<PROJECT>/
  00_WIP/  01_SHARED/  02_PUBLISHED/  03_ARCHIVE/     ← the CDE; ONLY these at top level
     └─ <Discipline>/<ContentType>/                    ← lazy-created on first write
  _data/                                               ← ALL machine state
     register.json  transmittals.json  doc_sequences.json
     audit/  templates/  workflows/  search_index/
     staging/  recycle/
```

- ISO 19650 organises by **state**, not file type: every export is born in `00_WIP/<disc>/<type>/`; Share/Publish/Archive are physical moves + register update + audit entry.
- **One PathService** (`Core/StingPaths.cs`) is the only legal source of project paths: `Cde(doc, state, disc, contentType)`, `Meta(doc, bucket)`, `Staging(doc, channel)`, `Recycle(doc)`. Everything else delegates to it.
- **Root identity is stored, not recomputed**: an Extensible Storage stamp in the RVT points at the root; `DetectProjectCode` names the folder once at creation only. No per-folder code suffixes.
- **One register, one transmittal store, one vocabulary, one state machine, one audit chain** for documents.

---

## WORK PACKAGES (execute in order)

### WP0 — Orientation (no code)
Read `docs/ISO19650_DOC_FOLDER_REVIEW.md` fully, then skim: `Core/ProjectFolderEngine.cs`, `Core/ProjectSetup.cs`, `Core/OutputLocationHelper.cs`, `Docs/Templates/DeliverableLifecycle.cs`, `Docs/Workflow/WorkflowEngine.cs`, `BIMManager/BIMManagerCommands.cs` (regions around lines 57-153, 698-800, 2121-2199, 4923-4999), `UI/DocumentManagementDialog.cs` (regions 4159-4269, 5216-5447). Create `docs/CONSOLIDATION_PROGRESS.md` with the WP checklist. Commit.

### WP1 — Quick wins (low risk, high value)
1. **Fix the MIDP drift join** (`Commands/Delivery/DeliveryCommands.cs:331-341`): read deliverables via `ProjectFolderEngine.GetMetaPath(doc, "_BIM_COORD")` + `deliverables.json` instead of the legacy `<rvtDir>/_BIM_COORD` sibling, and join on the field actually persisted by `DeliverableLifecycle` (`DocNumber` — see `DeliverableLifecycle.cs:184`). Today the join silently resolves empty.
2. **Merge the stub transmittal path**: make `ProjectFolderEngine.AutoLogTransmittal` (`Core/ProjectFolderEngine.cs:1735-1782`) delegate to `Planscape.Docs.Templates.TransmittalOrchestrator.Create` (keep a thin auto-log wrapper: reason "CDE state change", recipients from `DistributionGroups` default group when available, else the current stub recipient). One transmittal store, one ID scheme (`TX-NNNN`).
3. **Retire divergent CDE/metadata roots**: reroute writers of `<rvtDir>/_CDE/` (`Core/MultiBuildingExtraCommands.cs:99-109`, `BOQ/BOQBccBridge.cs:531`), `<rvtDir>/_bim_manager/` (`BIMManager/GapFixCommands.cs:34-42`, `UI/StingCommandHandler.cs:9166`, `Core/WorkflowEngine.cs:684,2130`, `Core/Lightning/LpsAutoIssueRaiser.cs:50`, `BIMManager/WarningsManager.cs` sites, `BIMManagerCommands.cs:9702,10049,10295,10686`) and `STING_Exports` (`Temp/ScheduleEnhancementCommands.cs:1478`) onto `ProjectFolderEngine.GetMetaPath` / `GetExportFolder`. `MigrateFromLegacy` already sweeps these names on open — after this step nothing recreates them.
4. **Dead flags**: wire `AUTO_CREATE_CDE_FOLDERS` (`TagConfig.cs:231,904-908`) to actually gate the folder bootstrap in `StingToolsApp.OnDocumentOpened` (~L1069-1100), and implement `AUTO_RUN_WORKFLOW_ON_OPEN` (`StingToolsApp.cs:929-942`) to enqueue the named preset via the existing pending-preset consume path instead of just logging — or, if you judge execution-on-open unsafe, remove both flags and their parsing entirely. No parsed-but-unused config may remain.
5. **Junk containment**: single `_data/recycle/` (replace per-folder `_RECYCLE`, `ProjectFolderEngine.cs:1199-1203` + `RestoreFile`), move `_acc_mirror_tmp` (`:2018-2027`) and `sharepoint_queue` (`:2043-2044`, fix the `_DATA` casing) under `_data/staging/`.
6. **Unify the two tree builders**: make `CreateFolderStructure` (`ProjectFolderEngine.cs:941-1000`) build from `ProjectSetup.CreateBIM` definitions so both entry points produce the identical tree (agree the superset: 5 default disciplines from `ProjectSetup.DefaultBimDisciplines`; keep the 14 issue subfolders as the richer set by updating `BimFolderDefaults`).
Build, commit ("fix(folders): quick-win consolidation — MIDP join, transmittal merge, retire _CDE/_bim_manager/STING_Exports, dead flags, junk containment, tree unification").

### WP2 — Heal the issues/meetings/documents store split (top desync)
`_bim_manager/issues.json` (WarningsManager, LPS auto-raiser) vs `STING_BIM_MANAGER/issues.json` (BCC/BIMManager register) are different physical files for the same concept; same for `meetings.json` (BCC UI `BIMCoordinationCenter.cs:11462` vs `CoordinationCenterCommands.cs:830`) and `documents.json` vs `document_register.json` (`WarningsManager.cs:3669`).
1. Introduce ONE resolver: `ProjectFolderEngine.GetMetaPath(doc, "STING_BIM_MANAGER")` (already the dialog's effective home) and route ALL readers/writers of issues/meetings/documents/transmittals/revisions through a small static `CoordStores` helper (`Core/CoordStores.cs`) exposing typed paths: `Issues(doc)`, `Meetings(doc)`, `Register(doc)`, `Transmittals(doc)`, `Revisions(doc)`.
2. On first access, `CoordStores` merges any legacy file found in the other folder(s) into the canonical one (append-merge by id; write a `.migrated` marker; never lose rows).
3. Rename the `documents.json` writers to the register file. Build, commit.

### WP3 — Replace dead/fragile reflection bridges with real APIs
1. **BCC deliverable selection**: `DeliverableLifecycleCommands.cs:96-99` / `TransmittalCommands.cs:130-134` reflect fields `SelectedDeliverable`/`SelectedDeliverables` that DO NOT EXIST — every lifecycle command believes nothing is selected. Add real `public static DeliverableRow SelectedDeliverable` / `public static IReadOnlyList<DeliverableRow> SelectedDeliverables` (or better, a small `IDeliverableSelectionProvider` static on a Core type that BCC sets on selection-changed) to `UI/BIMCoordinationCenter.cs`, wire the grid selection-changed to populate them, and replace the reflection with direct calls.
2. **Idle geometry sync**: `StingIdlingScheduler.cs:164-167` reflects `PlanscapeServerClient.PushDirtyGeometryAsync(Document)` which does not exist. Either implement the method on the client (POST the `GeometrySyncQueue` payload — follow the existing partial-class + auth pattern) or delete the job and its enqueue sites. No permanent silent no-op may remain.
3. **DrawingTypeStamper**: replace the 7 copy-paste `Type.GetType("StingTools.Core.Drawing.DrawingTypeStamper")` blocks (`Core/SLD/SLDGenerator.cs:478`, `Commands/SLD/SLDRiserDiagramCommand.cs:228`, `Commands/Electrical/Reports/VoltageDropScheduleCommand.cs:112`, `FaultCurrentScheduleCommand.cs:82`, `Commands/Electrical/PanelViewScheduleCommand.cs:187`, `Commands/Electrical/ArcFlash/ArcFlashLabelSheetCommand.cs:137`, `ArcFlashScheduleCommand.cs:76`) with direct `DrawingTypeStamper.Stamp(...)` calls.
4. **RevisionHistoryEntry**: `DeliverableLifecycle.cs:152-160` builds `BIMCoordinationCenter+RevisionHistoryEntry` via `Activator`+`SetValue`. Move the POCO to `Core` (or make it public) and construct it directly.
5. Sweep the remaining cross-subsystem `Type.GetType`/`GetMethod` bridges listed in the review Part 5 (§2 items 12-18) — replace with direct references where both sides are in the same assembly (they all are; it's one DLL). Build, commit.

### WP4 — Atomic writes on coordination stores
`OutputLocationHelper.WriteAllTextAtomic` exists but covers ~13 of 395 write sites. Convert the hot store writers to it: `BIMManagerCommands.cs` (31 raw writes — issues/register/transmittals/revisions), `UI/DocumentManagementDialog.cs` (21), `BIMManager/WarningsManager.cs` (12), `BIMManager/CoordinationCenterCommands.cs` (5), `BIMManager/PlatformLinkCommands.cs` (10), `BIMManager/GapAnalysisFixCommands.cs` (5), plus the `CoordStores` helper from WP2 (make atomic writes the only path it offers). Delete the two private re-implementations (`BOQ/BOQBccBridge.cs:560-566` `AtomicJsonWrite`; keep `LoadJsonArray`'s `.bak` fallback as the read-side complement). Build, commit.

### WP5 — Resurrect or remove dead automation (every item: wire it OR delete it; nothing stays dormant)
1. `StingStaleMarker` (`Core/StingAutoTagger.cs:1318-1350`): call `Register` from `StingToolsApp.OnStartup` next to `StingAutoTagger.Register` (start disabled), so the config restore at `StingToolsApp.cs:908` and the Tag-Studio toggle work.
2. `LiveClashUpdater` (`Clash/LiveClashUpdater.cs:84-107`): it registers with live triggers for ALL models at startup while the comment claims deferred triggers. Gate trigger attachment behind the existing clash-enable state (attach on first clash session / `AutoStartClashScheduler`), or update the comment and add a config off-switch — pick one, implement it.
3. Orphaned updaters `SLDSyncUpdater`, `CableManifestUpdater`, `HvacEnvelopeStaleUpdater`, `LiveStandardsUpdater`: register each in `OnStartup` disabled behind its existing toggle IF a UI toggle exists (SLD has one writing `sld_sync_enabled` — make it real); otherwise DELETE the class and its dead toggle. No headers may claim registration that doesn't happen.
4. `SlaScanner.Scan` (`Docs/Workflow/SlaScanner.cs:26`): invoke from BCC open/refresh and from the `SyncScheduler` tick bridge. Surface breaches via the existing warnings pipeline.
5. `WorkflowScheduler` (`Core/Phase75Enhancements.cs:50`): implement `LoadFromConfig` invocation at startup reading `_data/workflow_triggers.json` (document the shape in the file header), so `CheckDocumentOpenTriggers` can actually fire — or delete the trigger engine and its checks. No always-empty trigger list.
6. `PluginUpdateChecker` (`Core/PluginUpdateChecker.cs:27`): fire `CheckAsync` from `OnStartup` (async, respect its 24h cooldown), or delete the class.
7. `DeliverableServerSync.FireAndForget` (`Docs/Templates/DeliverableServerSync.cs:38`): call it from each `DeliverableLifecycle` transition (Issue/ReIssue/Publish/Cancel/Supersede/Replace) so the server mirror is event-driven, keeping the periodic reconcile as backstop.
8. `ProjectFolderEngine.FileChanged` event (`:1874`): subscribe the Document Manager refresh to it (replacing/deduping the private callback), or remove the event and its 13 raise sites.
9. `Core.WorkflowEngine.GetAvailablePresets` (`Core/WorkflowEngine.cs:2369-2388`): remove the triple-duplicated block.
10. **Auto-registration**: collapse the two facades (`BIMManagerEngine.AutoRegisterExport` `BIMManagerCommands.cs:798` vs `DocAutoRegister.RegisterExport` `:4958`) into one method with one schema (keep `AutoRegisterExport`'s shape, fold in `cde_status`), update the 2 `DocAutoRegister` call sites, and add registration calls to the ExLink batch exports (`ExLink/AutomationEngine.cs:429, 463, 497, 528, 828`). Every file the plugin exports must land in the register.
Build, commit (this WP may be split into 2-3 commits).

### WP6 — StingPaths service + the grep gate
1. Create `Core/StingPaths.cs` as the single path API (wrapping `ProjectFolderEngine` internals): `Cde(doc, state, disc?, contentType?)`, `Meta(doc, bucket, params subParts)`, `Staging(doc, channel)`, `Recycle(doc)`, `Export(doc, exportTypeKey, disc?)`. `OutputLocationHelper.GetOutputDirectory/GetOutputPath` and `ProjectFolderEngine.GetFolderPath/GetExportFolder/GetMetaPath` delegate to it (keep signatures for compatibility).
2. Migrate the ~40 remaining `<rvtDir>/_BIM_COORD` sibling writers (list in review Part 1, §A2) to `StingPaths.Meta(doc, "_BIM_COORD", ...)` — mechanical, but verify each file's reads AND writes move together.
3. Fix `Core/DesignOptions/OptionFolderManager.cs:19-20` (currently mints `_BIM_COORD` inside `20_MISC`) → `StingPaths.Meta(doc, "_BIM_COORD", "options")`.
4. Add `tools/check_path_discipline.ps1` (or .sh): greps `StingTools/` for `Directory.CreateDirectory` and `Path.Combine(<rvtDir-pattern>` outside `Core/` and fails non-zero on new occurrences above the recorded baseline count (store baseline in the script). Wire a note into CLAUDE.md conventions section.
5. Kill the static cross-document root cache (`ProjectFolderEngine._rootPath`) — per-document dictionary only.
Build, commit.

### WP7 — Dispatch consolidation (drift removal, not a rewrite)
Do NOT rewrite the 8 dispatch maps. Instead:
1. Add the missing alias tags so every panel tag resolves in `Core.WorkflowEngine.ResolveCommand`: `Panel_FillSlots→FillSparesAllSchedulesCommand`, `Panel_AddSpare`, `Panel_AddSpace`, `Panel_ConvertSpaceToSpare`, `Panel_ClearSlots`, `Panel_ExcelExport` (see `StingElectricalCommandHandler.cs:149-159` vs `WorkflowEngine.cs:1435-1439`), and add the Plumbing-handler-only tags (`PlumbSym_*` 26 tags, `Plumb_CreateVents`, `Plumb_SupplySchematic`, `Plumb_PumpSelect`…`Plumb_WaterSafetyPlan`) to `ResolveCommand` so workflows can reach them.
2. Harden `RunCommandByTag` (`Core/WorkflowEngine.cs:1353`) against null resolution (documented NRE history at `:1919`): unknown tag → logged, step marked failed, run continues.
3. Extract the 5 copy-paste `Run<T>()` helpers (HVAC `:447`, Plumbing `:215`, LPS `:211`, Sustainability `:100`, Electrical `:474`) into one shared internal helper in `UI/`.
4. Add `tools/check_dispatch_parity.ps1`: extracts tag strings from the panel handlers and asserts each exists in `ResolveCommand` (or a recorded exception list) — the drift regression gate. Build, commit.

### WP8 — Document Manager unification (the ISO 19650 core)
1. **One register**: merge `STING_BIM_MANAGER/document_register.json` into the richer `deliverables.json` lifecycle model → canonical `_data/register.json` managed by a new `Core/DocumentRegister.cs` (typed POCO, snake_case, `schema_version`, atomic writes, id = ISO container name). Provide one-time append-merge migration from both legacy stores. `DocumentManagementDialog`, `BIMCoordinationCenter`, `DocAutoRegister`→`AutoRegisterExport` (WP5.10) all read/write through it.
2. **One vocabulary**: make `Core/Drawing/Iso19650Vocabulary.cs` the single source for suitability (S0-S7 + A1-A6 + B1-B6 + CR), doc types, roles; delete/redirect the other four tables (`BIMManagerEngine.SuitabilityCodes` `BIMManagerCommands.cs:57-69`, `DocStatusCodes` `:4923-4949`, `TemplateManifest.SuitabilityScheme` `TemplateManifest.cs:71-72`, `MidpEngine.SuitRank` `MidpEngine.cs:69-75`). Add per-state legality (A-codes only in PUBLISHED; S0 only in WIP).
3. **One state machine, running**: on `DeliverableLifecycle.Issue`, `WorkflowEngine.Start("deliverable_issue_default", …)`; store `workflow_instance_id` on the register row; CDE transitions call `WorkflowEngine.Transition`; make `Transition` enforce `allowed_roles` (`Docs/Workflow/WorkflowEngine.cs:66-98`) resolving the current user's role from the existing `project_team.json` (`CdeApprovalGate` pattern). Wire Check→Review→Approve as required transitions before Publish; add P→C revision promotion on the authorize transition. Reconcile the three CDE transition tables (`BIMManagerCommands.cs:88-111`, `DocumentManagementDialog.cs:4159-4166`, workflow JSON) into the workflow JSON as the single definition.
4. **Close the physical loop**: `TemplateEngine` (`Docs/Templates/TemplateEngine.cs:29,60-66`) renders into `StingPaths.Cde(doc, WIP, disc, "Documents")` instead of `_data/_BIM_COORD/generated/`, registers the file, and every state transition = `ProjectFolderEngine.MoveFile` + register update + audit entry. Register `file_reference` must always equal the physical path.
5. **One audit chain**: register mutations and CDE moves append to the SHA-256 `AuditLog` chain (`Docs/Workflow/AuditLog.cs`), replacing plain-text-only `LogActivity` for document events (keep `LogActivity` as convenience mirror).
6. **Acknowledgement**: add a "Mark acknowledged" action on transmittals driving the workflow `acknowledge` transition.
This is the largest WP — split into 3-4 commits (register, vocabulary, state machine, physical loop). Build between each.

### WP9 — CDE-first tree + root identity
1. New layout per the north star: states at top (`00_WIP…03_ARCHIVE`), content types inside states; drop `05_MODELS…20_MISC` from the default `ProjectSetup.BimFolderDefaults`, remap `ExportRoutes` values to `(state, contentType)` pairs (default state WIP); delete the duplicate `ProjectFolderEngine.ExportTypeToFolder` (routes live only in `ProjectSetup`). Keep `Mini` mode as the second preset.
2. **Drop per-folder code suffixes** (`ProjectSetup.WithCodeSuffix` usage on children; root keeps the project-code name).
3. **Root identity**: new `Core/Storage/StingProjectRootSchema` ES schema stamping `{rootRelativePath, rootGuid}` in the RVT; `GetRootPath` resolves stamp → project_setup.json → create-and-stamp. Replaces the 8-char filename-prefix multi-model heuristic (`ProjectFolderEngine.cs:237-268`); multi-model = same stamp.
4. **Migration wizard**: extend `MigrateFromLegacy` into `Folders_ConsolidateAll` command with a DRY-RUN report (TaskDialog + CSV) before moving; folds the old numbered-tree content into the new state tree by the extension→type map it already has. Timestamped package folders (`CDE_PACKAGE_*`, `SharePoint_*`, `*_Briefcase_*` — `BIMManager/PlatformLinkCommands.cs:1278-1300, 2506-2517`) become ZIPs under `01_SHARED/Z_General/Packages/` + register entries.
Build, commit (2-3 commits).

### WP10 — HTTP + storage hygiene (lower priority; park if time-boxed)
1. `Core/PluginTelemetry.cs:112` per-call `new HttpClient()` → static pooled client.
2. Route the unauthenticated ad-hoc clients (`CoordinationCenterCommands.cs:51`, `MobileIssueBridge.cs:50`) through `PlanscapeServerClient` where they hit Planscape endpoints.
3. Pick the storage-ownership rule and write it into CLAUDE.md: ES = per-element/per-document plugin state; `_data` JSON = project coordination data; shared params = user-visible/schedulable only. Resolve the workflow-state dual storage (ES `StingWorkflowStateSchema` vs `workflow_state.json`) in favour of the JSON store (migrate + retire the ES schema or vice-versa — pick one, document why).

### FINAL — Wrap-up
1. Run the full rebuild + both grep gates. 2. Update `docs/CONSOLIDATION_PROGRESS.md` (final states), `docs/CHANGELOG.md` (one phase block), `docs/ROADMAP.md` (parked items + follow-ups: in-Revit verification list, Planscape server-side implications). 3. Push the branch. 4. Final report: per-WP status, commit SHAs, build result, complete in-Revit verification checklist (each item: command to run in Revit + expected observable result). Do not merge, do not open a PR, do not deploy.
