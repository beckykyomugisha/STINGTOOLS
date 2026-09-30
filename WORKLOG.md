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
| # | Area | File | Sev | Finding | Plan |
|---|---|---|---|---|---|

## Findings (done)
- 2026-10-01 audit fixes on `claude/acc-audit-fixes-y` (not pushed): **A7** `eec4bf778` (review on the live tip version) ·
  **A10** `17a5ebb59` (push assignee resolved via AccProjectMembers) · **A12** `036d9a0b1` (AccUploadGate shared by
  ACC_UploadModel/LastBundle + Export Centre; `uploadAllowReissue`) · **A9 / A13 / A14-card** `a25803741` (card writes via
  ApiDispatcher on the API thread; subtype cleared on type change; invariant parsing) + `a72dddc49` (issue-type comment) ·
  **A16** `1b81d44a3` (playbook §6 every key + doc tests; strategy doc). Acc tests 562 passed; build 0/0; gates OK.
See docs/CHANGELOG.md entries dated 2026-09-30 / 2026-10-01 (ACC hardening, follow-ups, workarounds, account data, federated compliance, two-way issues, reviews read-back, revision export, MIDP, duplicate-issue loop, webhook lineage).

## NEEDS MANUAL CHECK
- **ACC audit fixes (Agent Y):** BCC ACC card — Save / Discover / model set / escalation writes land in the open project's
  acc_settings.json and are refused after switching to another project without Refresh; ACC_UploadModel twice on the same
  file → second is "not uploaded again"; changed file same revision → refused; ACC_StartReview after a new upload → review
  on the new version (live DM `items/{item}` tip read unproved).
- **Live ACC (KUT):** run `ACC_SelfCheck` first; then playbook §7 V1–V8 (docs/KUT_ACC_DAY1_PLAYBOOK.md). Confirm: Admin API/member list permission (needs Project/Account Admin or Custom Integration), Locations tree readable by members, Model Properties `indexes:batch-status` spelling, Reviews approval-status path encoding, issue `filter[updatedAt]` open range, comment `body` field, `customAttributes`/`rootCauseId` on create, BCF `.bcfzip` as issue attachment, `planscape://` links clickable in ACC web.
- **Revit:** cloud model root mapping prompt + `Cloud_SetProjectRoot`; workshared central-root + Move; `planscape://revit/select` handler; ACC card buttons; `ACC_SyncProjectInfo` write path.
- **Server (Render):** set `DataProtection:CertificateBase64/Password` on API + worker; after deploy call `GET /api/acc/reconnect-required`; register webhooks via `POST acc/webhooks/subscribe`.

## Decisions
- **A7:** the review version is re-read from ACC at start time (one source of truth) rather than refreshing the local
  queue on every upload path; a failed tip read refuses the review.
- **A12:** revision-less uploads (models, unregistered files) still upload — only an identical re-send is skipped;
  the re-issue rule needs a revision. Commands take `uploadAllowReissue` from acc_settings; the Export Centre keeps its
  profile flag.
- **A13:** the resolved issue type is NOT persisted per project (that needs the Revit side in the escalation commands,
  owned by another worker); the comment was corrected instead and the card pins it.
- **A9:** BCC card writes go through a queued `ApiDispatcher` on the existing BCC ExternalEvent and refuse when the
  active model's settings path differs from the card's (built on the API thread).
- **Duplicate-issue loop:** plugin skips pushing ACC-owned rows to the server (not "push with origin") because the live Render server may be older than the plugin; server also links ACC-origin issues. Both ends guard.
- **Machine-file ACC settings fallback:** retired; old ids shown on the card and adopted only by explicit Save (explicit over silent).
- **Deploy location:** permanent detached worktree `C:\Dev\STING_KUT_LIVE` (deploy.bat refuses temporary worktrees; the shared checkout must not be switched under other agents).
- **CHANGELOG merge conflicts:** always keep both entries (tool: scratchpad keepboth.py refuses >1 region).
