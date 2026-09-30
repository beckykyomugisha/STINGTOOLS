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
| A4 | Settings | Clash/AccProjectSettingsFile.cs:66-68 | P1 | Malformed acc_settings.json reads as "not configured" (wrong file named), returns Cancelled, and an unattended project falls back to prompting | Report LoadError, return Failed (me) |
| A5 | Upload | Clash/AccUploadModelCommand.cs:75,415-421 | P1 | "Not set up" and "uploadUnattended not set" return Cancelled → failOnError step reads as skip; header/playbook say no KUT workflow uploads | Fail when unattended; fix comments/docs (me) |
| A7 | Reviews | Clash/AccReadReviewsCommand.cs AccReviewStarter ~545-591 | P1 | Review may start on the previous revision's version (queue not refreshed on upload) | Refresh on upload / re-read latest version (Agent Y) |
| A9 | BCC ACC card | UI/BIMCoordinationCenter.cs:5431,5702,5734 | P1 | Document captured once; Revit/StingPaths used off API thread and after await; Save can hit the wrong project | Resolve path per action on API thread, cache string (Agent Y) |
| A10 | Issue push | V6/AccIssuePush.cs:185-203 | P2 | Pushed assignee not resolved (email/name → 400) | Reuse AccProjectMembers resolution (Agent Y) |
| A11 | Docs metadata | V6/AccDocsMetadata.cs | P2 | Own HttpClient: no region, no 401 refresh, token may expire before stamping; missing attributes only a Warn | Deferred (see Decisions); make incomplete metadata visible (Agent Y) |
| A12 | Upload paths | ExportCenter upload vs ACC_UploadModel | P2 | Ledger only in Export Centre; pairing check only in command | Share ledger + checks (Agent Y) |
| A13 | Card | BIMCoordinationCenter issue type save; AccIssueSync.ResolveIssueTypeAsync comment | P2 | Changing type keeps old subtype; "cached per container" comment false | Clear subtype on type change; fix comment (Agent Y) |
| A16 | Docs | KUT_ACC_DAY1_PLAYBOOK.md §3.5/§6/§8, ACC_INTEGRATION_STRATEGY.md | P2 | 13 keys missing from §6; stale claims; escalateExcludeStatuses example drops defaults; assignee "both or neither" wrong; card tooltip wrong | Agent Y (+ doc test loading §6 examples through AccOperatingPolicy.Load) |

## Findings (done)
| # | Commit(s) | What |
|---|---|---|
| A2 | be54474c0 | Lifecycle-gap push: Report everywhere; unattended needs `lifecycleGapEscalation.maxCount` (else creates nothing); interactive asks; Lifecycle issue type (never clash); save per issue; stop on AuthFailed; Failed on any failure |
| A3 | ab8829857 | ACCPublish: no modal window unattended; picker cancel = Cancelled (no invented S3) |
| A6 | ad14d9465 | pushed_clashes.json tri-state; unreadable refuses escalate/sync (Failed, untouched); failed save counted and reported |
| A8 | 8d210984a | Gate scans every WORKFLOW_KUT_*.json, counts TaskDialog.Show / new TaskDialog; reviewed baseline; probes fail |
| A11 (part) | a9a2e1659 | UploadResult.MetadataIncomplete; renew stale token before stamping; stamping throw no longer fails the upload; Export Centre lists it. Transport still deferred |
| A14 | 659561e54 | Clash CSV invariant culture (V6/AccClashCsv). BCC card escalation parse still open (owner: BCC worker) |
| A15 | ad14d9465 | acc_issue_origins.json append-only origin record; Sync records before untracking; Import reads it first |

Earlier: see docs/CHANGELOG.md entries dated 2026-09-30 / 2026-10-01 (ACC hardening, follow-ups, workarounds, account data, federated compliance, two-way issues, reviews read-back, revision export, MIDP, duplicate-issue loop, webhook lineage).

## NEEDS MANUAL CHECK
- **Live ACC (KUT):** run `ACC_SelfCheck` first; then playbook §7 V1–V8 (docs/KUT_ACC_DAY1_PLAYBOOK.md). Confirm: Admin API/member list permission (needs Project/Account Admin or Custom Integration), Locations tree readable by members, Model Properties `indexes:batch-status` spelling, Reviews approval-status path encoding, issue `filter[updatedAt]` open range, comment `body` field, `customAttributes`/`rootCauseId` on create, BCF `.bcfzip` as issue attachment, `planscape://` links clickable in ACC web.
- **Revit:** cloud model root mapping prompt + `Cloud_SetProjectRoot`; workshared central-root + Move; `planscape://revit/select` handler; ACC card buttons; `ACC_SyncProjectInfo` write path.
- **Server (Render):** set `DataProtection:CertificateBase64/Password` on API + worker; after deploy call `GET /api/acc/reconnect-required`; register webhooks via `POST acc/webhooks/subscribe`.

## Decisions
- **A2 opt-in shape:** one strict object key `lifecycleGapEscalation` = {maxCount (required, >0), issueTypeId, issueSubtypeId} rather than reusing escalateMaxCount/MinScore: gaps have no score, a separate cap keeps clash and gap volumes independent, and the cap being required IS the opt-in. Interactive without it offers at most 25 and asks. Issue type resolved by NAME "Lifecycle" when unset; no match = refuse (never the clash subtype, never cached into the credentials file).
- **A6/A15 file layout:** tracking sets keep their names and format (backward compatible); the origin record is a new file beside them. Existing projects are backfilled on the next Pull/Sync/lifecycle push (Absorb), and Import also still reads both tracking sets.
- **A8 gate scope:** the no-document guard is exempt (WorkflowEngine will not start without a document context); every other modal message counts. Non-ACC UNGATED lines are recorded, not fixed here - they are the backlog for unattended KUT workflows beyond ACC.
- **A4 helper:** KutPush carries a TODO naming `AccProjectSettingsFile.NotConfigured(...)` to wire at merge (the helper was not on this branch).
- **Duplicate-issue loop:** plugin skips pushing ACC-owned rows to the server (not "push with origin") because the live Render server may be older than the plugin; server also links ACC-origin issues. Both ends guard.
- **Machine-file ACC settings fallback:** retired; old ids shown on the card and adopted only by explicit Save (explicit over silent).
- **Deploy location:** permanent detached worktree `C:\Dev\STING_KUT_LIVE` (deploy.bat refuses temporary worktrees; the shared checkout must not be switched under other agents).
- **CHANGELOG merge conflicts:** always keep both entries (tool: scratchpad keepboth.py refuses >1 region).
