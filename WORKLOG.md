# WORKLOG — continuous fix-and-improve loop (ACC first)

Standing task (2026-10-01): unattended loop — resume → research → record → fix → verify → commit → merge → update ROADMAP/WORKLOG → repeat. Priority: (1) ACC integration, (2) everything ACC touches, (3) rest of the codebase.

## Resume here
1. Merge `claude/revision-core` into the integration branch when its agent reports (build + Acc/Tags/Cost/Mep tests + gates), then merge the integration branch into `claude/kut-combined-acc-tags` and redeploy `C:\Dev\STING_KUT_LIVE` (only when `tasklist` shows no Revit.exe; see "Deploy" below).
2. Work the open ACC findings in the table below, highest severity first.
3. Start the next ACC audit pass (integration seams between the branches merged on 2026-09-30/10-01).

## Branches
- **Integration branch:** `claude/acc-work-review-gaps-7e2ac7` (worktree `.claude/worktrees/acc-work-review-gaps-7e2ac7`). Not pushed.
- **Deploy branch:** `claude/kut-combined-acc-tags` = integration + `origin/main` + snapshot of the (uncommitted) tag-families work from `claude/tag-families-setup-976ed0`. Deployed from the permanent detached checkout `C:\Dev\STING_KUT_LIVE` (all three Revit manifests point there).
- Merged feature branches (kept, not deleted): acc-selfcheck, acc-server-followups, acc-server-gaps, acc-issue-locate, acc-federated-compliance, acc-account-data, acc-issue-twoway, acc-reviews-readback, acc-revision-export, cloud-model-project-root.
- In progress: `claude/revision-core` (per-sheet issued revision resolver, revision/suitability rules, C01 contractual, issue-completion hook, supersede→ACC archive, KUT fortnightly issue workflow).

## Verification recipe (run after every change)
```
dotnet build StingTools/StingTools.csproj -c Debug            # expect 0 errors / 0 warnings
dotnet test StingTools.Acc.Tests ; dotnet test StingTools.Tags.Tests ; dotnet test StingTools.Cost.Tests ; dotnet test StingTools.Mep.Tests
dotnet build Planscape.Server/src/Planscape.API/Planscape.API.csproj   # server changes
dotnet test Planscape.Server/tests/Planscape.Tests --filter "FullyQualifiedName~Acc"   # full suite ~20 min
powershell -File tools/check_path_discipline.ps1 ; tools/check_workflow_wiring.ps1 ; tools/check_export_routing.ps1
python tools/check_kut_workflow_tags.py ; python tools/check_unattended_cycle.py
```
Last full green (2026-10-01, `23354ba63`): build 0/0; Acc 526, Tags 4215, Cost 147, Mep 87; all gates OK.

## Deploy (KUT live)
`git -C C:/Dev/STING_KUT_LIVE checkout --detach <commit>` then `cmd.exe //c 'C:\Dev\STING_KUT_LIVE\deploy.bat'` with Revit closed. Verify: manifests point at STING_KUT_LIVE; deployed DLL == `StingTools/bin/Release/StingTools.dll`; 210 tag families in `CompiledPlugin/data/TagFamilies`; **no `Seeds/` folder** (the 137 seed families are superseded — never restore them). Last deploy: `4ad350c46` at 23:13 on 2026-09-30.

## Findings (open)
| # | Area | File | Sev | Finding | Plan / owner |
|---|---|---|---|---|---|
| A1 | ACC realtime | Core/AccIssueRealtimeBridge.cs:78-84 → StingDockPanel.DispatchCommand → StingCommandHandler.SetCommand | P0 | Auto-import overwrites the single shared command slot from a background thread: can drop a user's queued command or run the import interactively on a project that never enabled it | Own ExternalEvent + handler for auto-import (me) |
| A2 | ACC lifecycle gaps | Commands/Twin/KutPushLifecycleGapsToAccCommand.cs:50-169 | P0 | Ignores AccOperatingPolicy: modal dialogs, no cap/opt-in, Succeeded after failed pushes, sidecar saved after loop, files under Clash subtype | Agent X |
| A3 | ACCPublish | BIMManager/PlatformLinkCommands.cs:1138,1158 | P1 | Modal dialog in unattended branch; cancelling the suitability picker silently picks S3 (invented code reaches bundle record + upload) | Agent X |
| A4 | Settings | Clash/AccProjectSettingsFile.cs:66-68 | P1 | Malformed acc_settings.json reads as "not configured" (wrong file named), returns Cancelled, and an unattended project falls back to prompting | Report LoadError, return Failed (me) |
| A5 | Upload | Clash/AccUploadModelCommand.cs:75,415-421 | P1 | "Not set up" and "uploadUnattended not set" return Cancelled → failOnError step reads as skip; header/playbook say no KUT workflow uploads | Fail when unattended; fix comments/docs (me) |
| A6 | Escalation record | Clash/AccPullClashesCommand.cs LoadPushed/SavePushed; AccSyncIssueStatusCommand | P1 | Unreadable pushed_clashes.json → empty map → re-escalates everything and overwrites history; failed save still counted as pushed | Tri-state load like AccUploadLedger; save result counts (Agent X) |
| A7 | Reviews | Clash/AccReadReviewsCommand.cs AccReviewStarter ~545-591 | P1 | Review may start on the previous revision's version (queue not refreshed on upload) | Refresh on upload / re-read latest version (Agent Y) |
| A8 | Gate | tools/check_unattended_cycle.py | P1 | Only scans CoordinationCycle and ignores TaskDialog.Show; A2/A3 pass it | Scan all WORKFLOW_KUT_*.json, count ungated TaskDialog.Show (Agent X) |
| A9 | BCC ACC card | UI/BIMCoordinationCenter.cs:5431,5702,5734 | P1 | Document captured once; Revit/StingPaths used off API thread and after await; Save can hit the wrong project | Resolve path per action on API thread, cache string (Agent Y) |
| A10 | Issue push | V6/AccIssuePush.cs:185-203 | P2 | Pushed assignee not resolved (email/name → 400) | Reuse AccProjectMembers resolution (Agent Y) |
| A11 | Docs metadata | V6/AccDocsMetadata.cs | P2 | Own HttpClient: no region, no 401 refresh, token may expire before stamping; missing attributes only a Warn | Deferred (see Decisions); make incomplete metadata visible (Agent Y) |
| A12 | Upload paths | ExportCenter upload vs ACC_UploadModel | P2 | Ledger only in Export Centre; pairing check only in command | Share ledger + checks (Agent Y) |
| A13 | Card | BIMCoordinationCenter issue type save; AccIssueSync.ResolveIssueTypeAsync comment | P2 | Changing type keeps old subtype; "cached per container" comment false | Clear subtype on type change; fix comment (Agent Y) |
| A14 | CSV | AccPullClashesCommand.WriteCsv; card escalation parse | P2 | Culture-dependent decimals break CSV / parsing | InvariantCulture (Agent X) |
| A15 | Round trip | AccSyncIssueStatusCommand:107 vs AccImportIssues origin | P2 | Sync removes closed escalations Import needs to recognise STING-raised issues | Keep an origin record separate from the tracking set (Agent X) |
| A16 | Docs | KUT_ACC_DAY1_PLAYBOOK.md §3.5/§6/§8, ACC_INTEGRATION_STRATEGY.md | P2 | 13 keys missing from §6; stale claims; escalateExcludeStatuses example drops defaults; assignee "both or neither" wrong; card tooltip wrong | Agent Y (+ doc test loading §6 examples through AccOperatingPolicy.Load) |

## Findings (done)
See docs/CHANGELOG.md entries dated 2026-09-30 / 2026-10-01 (ACC hardening, follow-ups, workarounds, account data, federated compliance, two-way issues, reviews read-back, revision export, MIDP, duplicate-issue loop, webhook lineage).

## NEEDS MANUAL CHECK
- **Live ACC (KUT):** run `ACC_SelfCheck` first; then playbook §7 V1–V8 (docs/KUT_ACC_DAY1_PLAYBOOK.md). Confirm: Admin API/member list permission (needs Project/Account Admin or Custom Integration), Locations tree readable by members, Model Properties `indexes:batch-status` spelling, Reviews approval-status path encoding, issue `filter[updatedAt]` open range, comment `body` field, `customAttributes`/`rootCauseId` on create, BCF `.bcfzip` as issue attachment, `planscape://` links clickable in ACC web.
- **Revit:** cloud model root mapping prompt + `Cloud_SetProjectRoot`; workshared central-root + Move; `planscape://revit/select` handler; ACC card buttons; `ACC_SyncProjectInfo` write path.
- **Server (Render):** set `DataProtection:CertificateBase64/Password` on API + worker; after deploy call `GET /api/acc/reconnect-required`; register webhooks via `POST acc/webhooks/subscribe`.

## Decisions
- **Duplicate-issue loop:** plugin skips pushing ACC-owned rows to the server (not "push with origin") because the live Render server may be older than the plugin; server also links ACC-origin issues. Both ends guard.
- **Machine-file ACC settings fallback:** retired; old ids shown on the card and adopted only by explicit Save (explicit over silent).
- **Deploy location:** permanent detached worktree `C:\Dev\STING_KUT_LIVE` (deploy.bat refuses temporary worktrees; the shared checkout must not be switched under other agents).
- **CHANGELOG merge conflicts:** always keep both entries (tool: scratchpad keepboth.py refuses >1 region).
