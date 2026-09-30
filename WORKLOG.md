# WORKLOG — continuous fix-and-improve loop (ACC first)

Standing task (2026-10-01): unattended loop — resume → research → record → fix → verify → commit → merge → update ROADMAP/WORKLOG → repeat. Priority: (1) ACC integration, (2) everything ACC touches, (3) rest of the codebase.

## Resume here
1. Merge `claude/acc-audit-fixes-x` and `claude/acc-audit-fixes-y` when their agents report (build + Acc/Tags/Cost/Mep tests + gates). Agent X was told to use `AccProjectSettingsFile.NotConfigured` in KutPushLifecycleGapsToAccCommand. Check that at merge.
2. Merge the integration branch into `claude/kut-combined-acc-tags` and redeploy `C:\Dev\STING_KUT_LIVE`, only when `tasklist` shows no Revit.exe (see "Deploy" below).
3. Start the next ACC audit pass (the fixes from A1–A16 plus the revision-core seams), then move to area 2 (everything ACC touches).

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
ACC seam audit A1–A16: all fixed (see "Findings (done)"). Open work is the revision/ACC table below.

## Findings — revision/issue workflow → ACC (audit 2026-10-01)
| Id | Area | Where | Sev | Defect | Plan |
|---|---|---|---|---|---|
| R1 | Workflow gating | Core/WorkflowEngine.cs:612; WORKFLOW_KUT_FortnightlyIssue.json | P0 | Optional failOnError steps (ACCPublish, ACC_UploadLastBundle) ran after the leak check / pairing gate failed | **Done** 6d5df2dde: WorkflowStepGate; `runAfterFailure` opt-in |
| R2 | Bundle upload | Clash/AccUploadModelCommand.cs:379-415; PlatformLinkCommands.cs:1109-1174 | P0 | Uploads whatever bundle was last recorded: a Cancelled ACCPublish (skip) lets step 7 re-upload last fortnight's ZIP and re-mark its transmittal SENT; re-running duplicates | Stamp bundle record with run id + uploaded versionUrn; refuse a stale/already-uploaded bundle in a preset (after agent X merges — A3 touches ACCPublish) |
| R3 | Bundle content | PlatformLinkCommands.cs:116-171 | P0 | The ACC bundle carries JSON registers/COBie, not the sheet PDFs step 5 exported; the issue set never reaches ACC | Upload the run's Export Centre rows (ledgered per-sheet path) as the upload step; correct preset text |
| R4 | Pairing on upload | AccUploadModelCommand BuildOptions; ExportCenterEngine.cs:1088-1130 | P1 | Pairing checked against the file name only (bundle names carry no P/C); Export Centre per-sheet upload unchecked | Check agent Y's AccUploadGate (A12) covers recorded revision+suitability; close or extend |
| R5 | Deliverables | Docs/Templates/DeliverableLifecycle.cs:43-45,196-214; RevisionIssueCompletion.cs:92-94 | P1 | Publish fills A1 over a P revision; ApplyToDeliverables keeps old suitability with new revision → P03/A1 persisted | Run Iso19650RevisionRules.Check; record conflict instead of persisting |
| R6 | IssueSheets | RevisionManagementCommands.cs:1622-1640,1719-1735 | P1 | In a workflow suitability is blank → pairing NotApplicable → revision issued with no suitability | **Done** 23960084a: RevisionIssueGate refuses in a preset; blank takes the one code all target sheets agree on |
| R7 | IssueSheets | RevisionManagementCommands.cs:1615-1620 | P1 | Zero-sheet guard only when IsUnattended (never set by the KUT preset): burns a number, proposes RESPONDED for every open issue | **Done** 23960084a: zero targets never issued (attended or not) |
| R8 | Ordering | RevisionManagementCommands.cs:1656-1683 + CompleteIssue | P1 | Register/deliverables/issues marked issued before export/upload succeed; not reverted | Defer issue-resolution proposals to after transmit; record pending state |
| R9 | Export step | Docs/ScheduledExportRunner.cs:35-104,167-190 | P1 | Returns Succeeded when nothing was due/blocked/failed; unconditional TaskDialog | **Done** 1343f2dda: ScheduledExportSummary verdict |
| R10 | Leak check | RevisionNumberingCommands.cs:145-151 vs TitleBlockRevisionSyncer.cs:246-256 | P1 | Leak check flags locked title blocks the syncer deliberately skips → step 1 fails forever | Honour the lock (informational) |
| R11 | Retire | Clash/AccRetireDeliverable.cs:43-51 | P1 | Looks for acc_version_urn on register rows; nothing writes it → supersede/replace never archives | Resolve through AccUploadLedger by DocumentNumber (store FolderUrn) |
| R12 | Reachability | UI/StingCommandHandler.cs:3026 | P2 | Revision_SetPerSheetNumbering has no button; CreateRevision tells users to run it | Add BIM-tab button; fix log text |
| R13 | Revision label | Core/Drawing/SheetRevisionReader.cs:77-83 | P2 | Invents "R{seq}" when numbering is None; reaches title block, file names, ACC | Return empty + report |
| R14 | Register default | BIMManager/ExportRegisterUpsert.cs:57,63 | P2 | Non-sheet rows default S0/WIP, later read as a real suitability for ACC | Leave blank; let upload refuse/ask |

## Findings (done)
- 2026-10-01 audit fixes on `claude/acc-audit-fixes-y` (not pushed): **A7** `eec4bf778` (review on the live tip version) ·
  **A10** `17a5ebb59` (push assignee resolved via AccProjectMembers) · **A12** `036d9a0b1` (AccUploadGate shared by
  ACC_UploadModel/LastBundle + Export Centre; `uploadAllowReissue`) · **A9 / A13 / A14-card** `a25803741` (card writes via
  ApiDispatcher on the API thread; subtype cleared on type change; invariant parsing) + `a72dddc49` (issue-type comment) ·
  **A16** `1b81d44a3` (playbook §6 every key + doc tests; strategy doc). Acc tests 562 passed; build 0/0; gates OK.

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

Seam audit (2026-10-01):
- **A1** (13dafb71c) — Auto-import overwrites the single shared command slot from a background thread: can drop a user's queued command or run the import interactively on a project that never enabled it
- **A4** (686930e8a) — Malformed acc_settings.json reads as "not configured" (wrong file named), returns Cancelled, and an unattended project falls back to prompting
- **A5** (686930e8a) — "Not set up" and "uploadUnattended not set" return Cancelled → failOnError step reads as skip; header/playbook say no KUT workflow uploads

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
- **A2 opt-in shape:** one strict object key `lifecycleGapEscalation` = {maxCount (required, >0), issueTypeId, issueSubtypeId} rather than reusing escalateMaxCount/MinScore: gaps have no score, a separate cap keeps clash and gap volumes independent, and the cap being required IS the opt-in. Interactive without it offers at most 25 and asks. Issue type resolved by NAME "Lifecycle" when unset; no match = refuse (never the clash subtype, never cached into the credentials file).
- **A6/A15 file layout:** tracking sets keep their names and format (backward compatible); the origin record is a new file beside them. Existing projects are backfilled on the next Pull/Sync/lifecycle push (Absorb), and Import also still reads both tracking sets.
- **A8 gate scope:** the no-document guard is exempt (WorkflowEngine will not start without a document context); every other modal message counts. Non-ACC UNGATED lines are recorded, not fixed here - they are the backlog for unattended KUT workflows beyond ACC.
- **A4 helper:** KutPush carried a TODO naming `AccProjectSettingsFile.NotConfigured(...)`; wired after the merge.
- **Duplicate-issue loop:** plugin skips pushing ACC-owned rows to the server (not "push with origin") because the live Render server may be older than the plugin; server also links ACC-origin issues. Both ends guard.
- **Machine-file ACC settings fallback:** retired; old ids shown on the card and adopted only by explicit Save (explicit over silent).
- **Deploy location:** permanent detached worktree `C:\Dev\STING_KUT_LIVE` (deploy.bat refuses temporary worktrees; the shared checkout must not be switched under other agents).
- **A4 — malformed settings file:** named with its load error. It fails; it is not read as "not configured". A file that parses but is invalid and still says `"unattended": true` keeps the run non-interactive: a typo must not make a scheduled run sit on a dialog nobody answers. Every other setting is still discarded.
- **A5 — unattended upload without `uploadUnattended`:** fails the step with the reason. It is not skipped. The workflow step exists to upload, and a silent skip would read as "issued" to the IM. The test is covered by build only: the command needs a Revit document, so no unit test is possible without extraction (logged as a gap).
- **AccDocsMetadata onto AccHttp:** deferred. It already has Retry-After, and the tested wait-cap semantics would be at risk for no user-visible gain.
- **CHANGELOG merge conflicts:** always keep both entries (tool: scratchpad keepboth.py refuses >1 region).
