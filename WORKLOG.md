# WORKLOG — continuous fix-and-improve loop (ACC first)

Standing task (2026-10-01): unattended loop — resume → research → record → fix → verify → commit → merge → update ROADMAP/WORKLOG → repeat. Priority: (1) ACC integration, (2) everything ACC touches, (3) rest of the codebase.

## Resume here
1. ACC automation follow-ups (docs/ACC_AUTOMATION_RESEARCH_2026-10.md, ROADMAP ACC-AUT-8..14): first ACC-AUT-9 (issue attribute mappings) and ACC-AUT-8 (folder permission pre-flight). Read each response schema on the APS reference page before coding.
2. ACC-LOCK-1 (cross-process lock on the shared ACC state files); then ACC-PAGE-1 / ACC-CMT-1.
3. Redeploy KUT live only when the user asks (the queued task said no deploy.bat). Main #1036-#1041 is now in the integration branch.

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
Last full green (2026-10-01, after F1-F10 + main #1029-#1035, `27302d544`): build 0/0; Acc 710, Tags 5281, Cost 171, Mep 87; full server suite 1134 passed / 20 skipped / 1 failed, the one failure being HandoffReplayGuardTests.Handoff_ReplayGuardUnavailable_FailsOpen, which passes alone (4/4) and is not touched by this branch: a load-dependent flake, logged as ROADMAP TEST-FLAKE-1, not altered; `run_ci_gates.py --quick` 36/0; drawing-type checksums OK.

## Deploy (KUT live)
`git -C C:/Dev/STING_KUT_LIVE checkout --detach <commit>` then `cmd.exe //c 'C:\Dev\STING_KUT_LIVE\deploy.bat'` with Revit closed. Verify: manifests point at STING_KUT_LIVE; deployed DLL == `StingTools/bin/Release/StingTools.dll`; 210 tag families in `CompiledPlugin/data/TagFamilies`; **no `Seeds/` folder** (the 137 seed families are superseded — never restore them). Last deploy: `57c3b236e` (claude/kut-combined-acc-tags = integration incl. main through #1028 + round 4 + SRV-11) at 07:00 on 2026-10-01; verified (3 manifests, DLL hash match, 210 tag families, no Seeds/). User told to re-run Load Shared Parameters (drawing stamps now bind to Views).

## Findings (open)
ACC seam audit A1–A16: all fixed (see "Findings (done)"). Open work is the revision/ACC table below.

## Findings — revision/issue workflow → ACC (audit 2026-10-01)
| Id | Area | Where | Sev | Defect | Plan |
|---|---|---|---|---|---|
| R1 | Workflow gating | Core/WorkflowEngine.cs:612; WORKFLOW_KUT_FortnightlyIssue.json | P0 | Optional failOnError steps (ACCPublish, ACC_UploadLastBundle) ran after the leak check / pairing gate failed | **Done** 6d5df2dde: WorkflowStepGate; `runAfterFailure` opt-in |
| R2 | Bundle upload | Clash/AccUploadModelCommand.cs:379-415; PlatformLinkCommands.cs:1109-1174 | P0 | Uploads whatever bundle was last recorded: a Cancelled ACCPublish (skip) lets step 7 re-upload last fortnight's ZIP and re-mark its transmittal SENT; re-running duplicates | **Done** ae496b322: in a run only the bundle built since the run started goes (AccBundleRecord.IsFromThisRun); refusals in a preset fail |
| R3 | Bundle content | PlatformLinkCommands.cs:116-171 | P0 | The ACC bundle carries JSON registers/COBie, not the sheet PDFs step 5 exported; the issue set never reaches ACC | **Done** ae496b322 (honest wiring): drawings reach ACC via the Export Centre profile "Upload to ACC" in step 5; bundle = IM data; preset text + playbook §8 corrected |
| R4 | Pairing on upload | AccUploadModelCommand BuildOptions; ExportCenterEngine.cs:1088-1130 | P1 | Pairing checked against the file name only (bundle names carry no P/C); Export Centre per-sheet upload unchecked | **Closed by A12** (036d9a0b1): AccUploadGate checks recorded revision + suitability on every upload path, Export Centre included |
| R5 | Deliverables | Docs/Templates/DeliverableLifecycle.cs:43-45,196-214; RevisionIssueCompletion.cs:92-94 | P1 | Publish fills A1 over a P revision; ApplyToDeliverables keeps old suitability with new revision → P03/A1 persisted | **Done** 5597e81c5: completion clears a contradicted stale code + IsoConflict + report; Publish of a sheet-linked deliverable refused when its sheets' revision cannot carry the code |
| R6 | IssueSheets | RevisionManagementCommands.cs:1622-1640,1719-1735 | P1 | In a workflow suitability is blank → pairing NotApplicable → revision issued with no suitability | **Done** 23960084a: RevisionIssueGate refuses in a preset; blank takes the one code all target sheets agree on |
| R7 | IssueSheets | RevisionManagementCommands.cs:1615-1620 | P1 | Zero-sheet guard only when IsUnattended (never set by the KUT preset): burns a number, proposes RESPONDED for every open issue | **Done** 23960084a: zero targets never issued (attended or not) |
| R8 | Ordering | RevisionManagementCommands.cs:1656-1683 + CompleteIssue | P1 | Register/deliverables/issues marked issued before export/upload succeed; not reverted | **Deferred (decision)**: the Revit issue IS the issue event (locked in Revit, irreversible); proposals are RESPONDED, never CLOSED. A separate "transmitted" state is ROADMAP REVWF-1 |
| R9 | Export step | Docs/ScheduledExportRunner.cs:35-104,167-190 | P1 | Returns Succeeded when nothing was due/blocked/failed; unconditional TaskDialog | **Done** 1343f2dda: ScheduledExportSummary verdict |
| R10 | Leak check | RevisionNumberingCommands.cs:145-151 vs TitleBlockRevisionSyncer.cs:246-256 | P1 | Leak check flags locked title blocks the syncer deliberately skips → step 1 fails forever | **Done** 4b0bdca41: locked title blocks reported as LOCKED, not leaks |
| R11 | Retire | Clash/AccRetireDeliverable.cs:43-51 | P1 | Looks for acc_version_urn on register rows; nothing writes it → supersede/replace never archives | **Done** 0c4f6dc9b: retire resolves live renditions from the upload ledger (folderUrn recorded), marks them retired |
| R12 | Reachability | UI/StingCommandHandler.cs:3026 | P2 | Revision_SetPerSheetNumbering has no button; CreateRevision tells users to run it | **Done** 4bf7eced5: BIM > Revision Management gets "Per-Sheet #" and "Leak Chk" |
| R13 | Revision label | Core/Drawing/SheetRevisionReader.cs:77-83 | P2 | Invents "R{seq}" when numbering is None; reaches title block, file names, ACC | **Done** 06b37ea0e: no invented R{seq}; empty + log |
| R14 | Register default | BIMManager/ExportRegisterUpsert.cs:57,63 | P2 | Non-sheet rows default S0/WIP, later read as a real suitability for ACC | **Done** 8ca3727ec: suitability_defaulted flag; AccFileIso does not send a defaulted S0 |

## Findings — area 2, ACC-adjacent (audit 2026-10-01)
| Id | Area | Where | Sev | Defect | Plan |
|---|---|---|---|---|---|
| C1 | Stores | Core/PluginSchemaVersion.cs:120-131 via TransmittalOrchestrator.cs:151, DeliverableLifecycle.cs:584 | P0 | JSON-ARRAY stores (transmittals.json, deliverables.json) fail JObject.Parse → quarantined to .corrupt.* and replaced by a version object; AppendTransmittalsJson "starts fresh" → every earlier transmittal lost | **Done** 1b87820dc + 3646b1cf1: array root never touched; Next/Append refuse; proven red without the guard |
| C2 | Export→ACC | ScheduledExportRunner.cs:105-113; ScheduledExportSummary Verdict; ExportCenterEngine.cs:1165; StingExportCenterDialog.cs:1794 | P0 | ACC upload failures only Warnings: step 5 green while nothing reached ACC; dialog Take(8) can hide the ACC line | **Done** d476d088a: ExportAccUploadTally; Verdict fails on blocked/failed (refused in a preset); HELD re-issues reported not fatal; ACC line first |
| C3 | Issues→server | PlanscapeServerClient.cs:624-646; IssueStore.cs:452-517 | P1 | No Idempotency-Key on create (timeout during cold start → duplicate BimIssue → both pushed to ACC); reconcile writes stale snapshot over concurrent server_code | **Done** e2a9969d2: X-Idempotency-Key per (project, issue id); reconcile stamps per row under lock; server replay test (tenant double) |
| C4 | JSON stores | BIMManagerCommands.cs:775-838 | P1 | LoadJsonArray: unreadable → empty (then overwritten); SaveJsonFile swallows → "SENT" claimed after failed save | **Done** db0dbaa13: JsonStoreFile (Replace keeps .bak, moves unreadable aside; TryLoadArray); SaveJsonFile→bool; ACC-path writers refuse/report |
| C5 | Ledger key | V6/AccUploadLedger.cs:103-137 | P1 | Key ignores suitability/folder: S2→S3 or WIP→SHARED under same revision skipped/refused | **Done** 9b9302b81: suitability-aware ledger check; UploadStatusChange; legacy entries keep the old rule |
| C6 | Hash stability | AccUploadGate.cs:83-112 | P2 | Revit PDFs likely not byte-stable → re-exports refused not skipped | NEEDS MANUAL CHECK (see below); mitigated by C2 HELD category (re-exports never fail a step) |
| C7 | Unified register | Core/DocumentRegisterMerge.cs:44-146 | P2 | Ignores suitability_defaulted/iso_unset/IsoConflict; backfills cleared suitability | **Done** 573cd50f3: no backfill over IsoConflict/default; IsoNote in CSV + canonical. Grid marker = ROADMAP DOCX-REG-1 |
| C8 | Register pairing | RevisionIssueCompletion.cs:177-179 | P2 | ApplyToRegister keeps a contradicted stale suitability (R5 guard missing there) | **Done** af0b67309: ApplyToRegister clears a contradicted code + iso_conflict + report |
| C9 | CDE routing | AccModelUpload.cs:263-290; ExportCenterEngine.cs:1148 | P2 | No cdeFolders → every state to one folder; row drops FolderReason | **Done** c4d960d03: WIP refused without cdeFolders (UnroutedWipRefusal); rows name their folder |
| C10 | Server push | Planscape.Server AccSyncService.cs:398-485 | P2 | Planscape edits (status/title) never reach ACC after first push | **Done** 1760fcf38: PATCH mapped Planscape-born issues edited since last push; never reopen an ACC close; legacy baseline; precision bug caught by the test |

## Findings — ACC round 3: transport and lifecycle (audit 2026-10-01)
| Id | Area | Where | Sev | Defect | Plan |
|---|---|---|---|---|---|
| D1 | Plugin auth | UI/BIMCoordinationCenter.cs:5626-5642 Gather/SaveAcc; V6/AccIssueSync.cs:267,413 | P1 | The card writes the refresh token loaded at open over a rotated one (Save/Test/Sign in/Find/Upload); ResolveIssueTypeAsync saves unlocked; adopt takes any DIFFERENT token, so a stale file poisons valid sessions | **Done** 48eea85b2: Gather keeps the current token unless edited; SaveCredentials keeps a newer file token (KeepNewerFileToken); adopt only newer — red without the guard |
| D2 | Server sync | Aps/ApsRetry.cs:49,57; AccSyncService catches `!OperationCanceledException` | P1 | An HttpClient timeout (TaskCanceledException) escapes, aborts the multi-tenant sweep, leaves the old OK status, Hangfire retries 10x; an unclear create is POSTed again → duplicate ACC issues | **Done** 1fa814d89: timeouts are failures; unclear creates go to accIssuePendingVerify and are verified (filter[createdAt] + title) before any re-send; job AutomaticRetry 0 |
| D3 | Clash paging | V6/AccModelCoordSync.cs:178-262,367-391 | P1 | Clash tests / model sets read one page; continuationToken not followed → latest test may be missed | **Done** 0bc418825: ReadAllTestsAsync follows continuationToken; model sets too (NEEDS MANUAL CHECK live) |
| D4 | Plugin refresh race | AccIssueSync.cs:222,242 | P2 | Losing a refresh race (lock wait 20 s < possible hold) reports "sign in again" though the file holds a rotated token | **Done** 1dac10c1f: refused grant re-reads the file and adopts the winner — red without it |
| D5 | Server token | AccSyncService.cs:389 | P2 | Token refreshed once per sync; a long push outlives it → 401s counted as rejections | **Done** 1fa814d89: RefreshMidRunAsync before each request — red without it |
| D6 | Server token | AccTokenRefresher.cs:118-123 | P2 | Rotated refresh token saved under the request's cancellation → lost rotation → RECONNECT_REQUIRED for the team | **Done** 22ff9add6: rotation + save on CancellationToken.None — red without it |
| D7 | BCC card | BIMCoordinationCenter.cs:5605,5673 | P2 | "Saved" shown when the credentials save failed | **Done** 48eea85b2: card reports a failed credential save; project ids still saved |
| D8 | Paging caps | V6/AccReviews.cs:321-361,481-509; server GetDmPagedAsync, issue types, read-back | P2 | Caps return success with next pages unread | **Done** 44c09f6ed (plugin) + server commit after the suite: INCOMPLETE at every cap |
| D9 | Retire copy | V6/AccDocsLifecycle.cs:73,112-130 | P2 | Own HttpClient: no 429/Retry-After, no refresh | **Done** e7c19d905: copy through AccHttp (429/503 retry, per-attempt timeout, 401 refresh with creds) |
| D10 | Policy key | AccOperatingPolicy.cs:199; AccRetireDeliverable.ReadMode | P2 | retireSupersededInAcc accepted but never validated; a typo silently = "ask" | **Done** e7c19d905: parsed in Load (ask/always/never else Malformed); ReadMode removed |

## Findings — ACC round 4: data contract (audit 2026-10-01)
| Id | Area | Where | Sev | Defect | Plan |
|---|---|---|---|---|---|
| E1 | Escalation | Clash/AccSyncIssueStatusCommand.cs:119-138; AccPullClashesCommand.cs:226 | P1 | An escalation closed/voided in ACC Issues is untracked while the clash stays active → re-escalated every cycle; a deleted issue stays tracked forever | **Done** 0503259c3: AccEscalationReconcile.Decide; un-track only when the clash is absent from a complete pull (acc_clash_presence.json), else hold in closedInAcc; NOT_FOUND un-tracked |
| E2 | Server update (my C10) | AccSyncService.cs MapStatus/AccIssueUpdatePlan | P1 | MapStatus case-sensitive, IN_PROGRESS→open; Plan always sends status/title/description → reverts an assignee's ACC in_progress/completed, overwrites ACC wording | **Done** 42b57d9e3: per-issue pushed snapshot (status + text hashes); only changed fields sent; status only when ACC still shows the last pushed value; IN_PROGRESS mapped; server suite 1122/0 |
| E3 | Reviews | V6/AccReviewProposals.cs:348-362 FindTransmittal | P1 | version-or-item match, first hit → approval recorded on the v1 transmittal | **Done** 8492912fe: exact version first; item fallback only when unique and no other version recorded; else refused with a reason |
| E4 | Reviews → register | AccReviewProposals.cs:394-397; BIMManagerCommands UpdateDocumentSuitability | P1 | Register rows matched on doc_id only (never matches export rows); write failure silent but reported applied; no revision check on accept | **Done** af7f7e85e: match doc_number/file_name/doc_id; ApprovedRevision from the file name; revision mismatch refused; contradicted code flagged iso_conflict; save result reported |
| E5 | MIDP dates | Core/Delivery/MidpCsv.cs:120-127 | P1 | dd/MM dates with day <= 12 parsed as MM/dd (invariant TryParse first) | **Done** b5c9f636a: ISO first, then day-first (step param dateOrder=mdy for month-first); unreadable dates refused and counted |
| E6 | Import defaults | V6/AccIssueImport.cs:387-427; IssueSchema.Create | P1 | Imported ACC issues get MEDIUM priority, now as created date, importer as raiser → SLA/overdue wrong | **Done** 082e61015: ACC createdAt/createdBy kept; priority blank + priority_defaulted, SLA "no SLA"; CSV adds display id + assignee name |
| E7 | MIDP header | MidpCsv.cs:72,88-89 | P2 | Renamed code column → all rows dropped silently | **Done** b5c9f636a: MidpCsv.ParseDetailed names missing required columns (file refused); skipped rows counted; header synonyms |
| E8 | Watermark | V6/AccIssueImportState.cs:96-118 | P2 | Local clock watermark; skew > overlap loses updates until the weekly full read | **Done** 9c2f6f8cf: watermark from ACC Date header, else newest updatedAt; skew logged (lastSkewSeconds) |
| E9 | Comments push | V6/AccIssuePush.cs:110-131,237 | P2 | Comment-only changes never pushed; notes lost on AgreeFields/non-pushable status | **Done** e3a760d86: comment-only changes pushed; content-hash keys in acc_comments_pushed_keys; failed post fails the run |
| E10 | CSV injection | AccClashCsv.cs:41; AccImport/Push/SyncIssueStatus CSV writers | P2 | Leading = + - @ from third-party names not neutralised | **Done** da2897ccc: AccCsv.Cell/Guard (always quoted; leading = + - @ tab CR neutralised) in every ACC CSV writer |
| E11 | Culture | V6/AccIssueSync.cs:554 | P2 | dueDate formatted in current culture | **Done** 116e04b60: dueDate InvariantCulture |

## Findings — ACC round 5: what rounds 3–4 changed (audit 2026-10-01)
| Id | Area | Where | Sev | Defect | Plan |
|---|---|---|---|---|---|
| F1 | Escalation holds | Clash/AccPullClashesCommand.cs:257; V6/AccModelCoordSync.cs:342-348,396 | P1 | A pull with no document names (scope file missing) has no signatures yet is recorded COMPLETE → ReleaseAbsent releases every hold → re-escalation of everything closed in ACC | **Done** ee08bf5b7: AccClashPresence.IsCompleteEvidence (untruncated AND no live clash without a signature) |
| F2 | Review accept | Clash/AccReadReviewsCommand.cs:452,332 | P1 | Partial apply (register refused) marked Accepted → refused part never retried | **Done** f3676f944: AppliedTargets + PartialCode; Settle -> Complete/Partial/NothingApplied; a retry applies only the rest |
| F3 | Server update | AccSyncService.cs PushUpdatesAsync | P1 | Withheld status: pushedAt + snapshot advance → divergence reported once then forgotten; Planscape CLOSED never sent later | **Done** 591e353f6: pushedAt not advanced on a withhold; re-reported every sync; sent once ACC returns (red/green) |
| F4 | Server update | AccSyncService.cs AccIssueUpdatePlan | P2 | Status guard uses previous sweep's read-back (stale or null → unchecked PATCH) | **Done** 09ef75594: fresh single-id read before a status PATCH; unreadable -> status withheld + named (red/green) |
| F5 | Clash presence | V6/AccEscalationRecord.cs:293-300 | P2 | Presence = union of every model set ever pulled; stale sets keep holds alive forever | **Done** f66c11f72: a set 30 days staler than the newest pull no longer counts (newest always counts) |
| F6 | MIDP | Commands/Delivery/DeliveryCommands.cs:480,504 | P2 | JArray.Parse turns history timestamps into dates; (string) gives MM/dd/yyyy HH:mm:ss which E5's parser refuses → issued deliverable shown not issued | **Done** 1f10e8d12: MidpCsv.TryDateValue reads the stored DateTime |
| F7 | MIDP | Core/Delivery/MidpCsv.cs:156 | P2 | Unreadable actual date silently becomes "not delivered" | **Done** 1f10e8d12: BadActualDate + DateProblems line |
| F8 | Transmittals | Clash/AccReadReviewsCommand.cs:499 | P2 | ACC_ReadTransmittals ignores the save result and reports success | **Done** 4842e721f: a failed save fails the command with the reason |
| F9 | Review queue | V6/AccReviewProposals.cs:151,167 | P2 | Zero-byte queue read as empty → decided proposals re-proposed; Save failure silent | **Done** 4842e721f: an empty queue file is unreadable; Save uses a unique temp name |
| F10 | Concurrency | AtomicFile.TryWrite, AccIssueImportState.Save, AccReviewQueue.Save | P2 | Fixed `.tmp` name + no cross-process lock on read-modify-write (two Revit sessions on the shared KUT folder) | **Part done** 5f6f8e8eb: unique temp names + cleanup in every ACC state writer. OPEN: a cross-process lock around load-modify-save (ROADMAP ACC-LOCK-1) |
| ACC-SRV-11 | Server sync | AccSyncService.cs | P2 | Issue raised and closed between sweeps never reached ACC, nothing said | **Done** 351c2d1f6: accClosedBetweenSweeps report (default) / create |

Speculative (needs live check): offset paging in PullIssuesAsync can skip an issue → NOT_FOUND untrack → re-raise (re-read by id before untracking); E9 comment keys hash JSON whose dates may re-serialise per time zone.

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
- **ACC automation (AUT-5..7):** see docs/ACC_AUTOMATION_RESEARCH_2026-10.md §6.
  1. Reconnect server ACC to gain `data:create`. Subscribe should then create 5 hooks.
  2. Close a review in ACC; clients should get `acc.review.closed`.
  3. SSA setup end to end: env vars + `accAuthMode` ssa, then sync. Confirm the robot is the issue creator.
- **C1 recovery on KUT:** look in `_data/coord` (the `_BIM_COORD` alias) for `transmittals.json.corrupt.*` and `deliverables.json.corrupt.*`. If any exist, the old gate quarantined them: merge their rows back into the live array, and keep the newer rows on any id clash.
- **C6 PDF byte-stability:** export one sheet twice from the Export Centre with no change, then compare SHA-256 (`certutil -hashfile x.pdf SHA256`). If they differ, re-exports land as HELD (reported, not failing), and a normalised hash can be added.
- **Revision/issue workflow (R1-R14):** in Revit, on a copy: (1) break a sheet stamp and run the KUT fortnightly preset; step 1 fails, and steps 6/7 show BLOCKED with nothing uploaded. (2) Run with no clouds; step 2 fails with 'no sheet carries a cloud'. (3) Lock a title block (PRJ_TB_LOCK) with a stale revision; Leak Chk lists it as LOCKED, and the preset passes step 1. (4) Supersede a deliverable whose PDF+DWG went up through the Export Centre; both are archived and the ledger shows retiredUtc. (5) The BIM > Revision Management buttons 'Per-Sheet #' and 'Leak Chk' run.
- **ACC audit fixes (Agent Y):** BCC ACC card — Save / Discover / model set / escalation writes land in the open project's
  acc_settings.json and are refused after switching to another project without Refresh; ACC_UploadModel twice on the same
  file → second is "not uploaded again"; changed file same revision → refused; ACC_StartReview after a new upload → review
  on the new version (live DM `items/{item}` tip read unproved).
- **Live ACC (KUT):** run `ACC_SelfCheck` first; then playbook §7 V1–V8 (docs/KUT_ACC_DAY1_PLAYBOOK.md). Confirm: Admin API/member list permission (needs Project/Account Admin or Custom Integration), Locations tree readable by members, Model Properties `indexes:batch-status` spelling, Reviews approval-status path encoding, issue `filter[updatedAt]` open range, comment `body` field, `customAttributes`/`rootCauseId` on create, BCF `.bcfzip` as issue attachment, `planscape://` links clickable in ACC web.
- **Revit:** cloud model root mapping prompt + `Cloud_SetProjectRoot`; workshared central-root + Move; `planscape://revit/select` handler; ACC card buttons; `ACC_SyncProjectInfo` write path.
- **ACC round 4 (E1, E4, E5/E7, E6, E8, E9):**
  1. E1: escalate a clash, void its issue in ACC, then run Sync Issue Status. Expect "untrack + hold" and a `closedInAcc` entry. Pull Clashes again: it reports "CLOSED IN ACC, STILL CLASHING" and creates no new issue. Resolve the clash, then re-run the clash test and Pull Clashes: the hold is released. Delete an issue in ACC, then run Sync: it reports NOT_FOUND and un-tracks the issue.
  2. E4: approve an Export Centre file (`…-0001-C01.pdf`) in a review mapped to A1, then Read Reviews and Accept. The doc_number row shows A1. With the row at C02, the result is NOT applied, giving the revision reason.
  3. E6: import an issue several weeks old. `issues.json` carries ACC's created date and creator name, and priority is blank. Confirm Issues v1 returns `createdBy`.
  4. E8: import twice. The log shows the ACC Date watermark and a skew line, and the state file has `lastSkewSeconds`.
  5. E9: add only a comment to an imported issue and run Push Issue Changes. The comment appears in ACC, and a second run posts nothing.
  6. E5/E7: import a MIDP CSV with `05/03/2027`, which lands on 5 March. A CSV with no Ref column is refused, naming the column.
- **ACC-SRV-11 create mode:** with `accClosedBetweenSweeps=create`, raise and close an issue in Planscape between syncs, then sync. Confirm that ACC accepts the POST with `status: closed` (or reports why), and that the issue shows as closed in ACC.
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
- **R2 staleness rule:** a bundle is "this run's" when built at or after the outermost preset start (WorkflowEngine.CurrentRunStartedUtc, thread-static). There is no run id in the record, because timestamps from one machine are enough and older records stay readable.
- **R3:** STING does not add sheet PDFs to the ACCPublish bundle. Per-sheet upload through the Export Centre is ledgered, pairing-gated and CDE-routed per file; a ZIP is none of those. The fix is to wire the existing path and state it plainly.
- **R5:** a stale suitability that contradicts a new Revit revision is cleared (and flagged), not kept. The revision is the Revit fact; the old code no longer describes the document.
- **R8:** not re-ordered. Revit locks an issued revision, so "issued" cannot be undone after a failed export. Transmission gets its own state later (ROADMAP REVWF-1).
- **R10:** a locked title block is the user's explicit freeze (T-3). It is reported, not failed, because the advised fix (RevisionSync) cannot touch it.
- **R14:** the register's S0 convention is kept for display and back-compat and flagged `suitability_defaulted`, rather than blanked. Blanking would change every Document Manager reader.
- **Deploy source (2026-10-01):** origin/main now carries the tag-families work (#1019), so the integration branch (main + ACC) is the deploy branch. `claude/kut-combined-acc-tags` is kept but retired: it only differs by an older uncommitted tag-families snapshot, superseded by the reviewed PR.
- **C1:** the schema gate skips array roots (no sidecar versioning). Array stores have never carried `$schemaVersion`, and versioning them would mean changing every reader. Projects already hit can recover from `*.corrupt.*` (NEEDS MANUAL CHECK).
- **C2 held vs refused:** a "re-issue without a revision change" is HELD and reported, never failing the step. Byte-unstable PDFs (C6) would otherwise fail every whole-set scheduled run. Only unset or contradictory ISO fields, which are content problems, fail a preset.
- **C4 scope:** `LoadJsonArray` keeps its read-as-empty contract for its 56 callers. The fix makes the following save non-destructive (the unreadable file is moved aside, and a `.bak` now really exists). Only the ACC-path writers switch to the refusing `TryLoadJsonArray`; the rest can migrate incrementally.
- **C5:** suitability became part of the ledger decision, not of the key, so existing ledgers stay valid. Legacy entries with no suitability keep the old rule.
- **C9:** WIP without cdeFolders is refused, not opted in by a new key. ISO 19650 keeps WIP out of shared areas, and the fix is mapping cdeFolders (a key that already exists).
- **C10:** the status is withheld (and reported) whenever ACC last said closed and Planscape says anything else, completed included. Undoing ACC's close is never the sync's call.
- **Area 3 RetagStale:** the scope is a step param, failing when missing, not defaulted. KUT steps set "project" because their labels say "since the last gate".
- **Area 3 remaining KUT steps (UNGATED 74 → 0) — the input rule.** Results and report windows are quiet in ANY preset (`PresetDialog.Show`, main's design). Every INPUT a KUT step takes (picker, fill/overwrite mode, input file, scope, LOD milestone, CDE status) follows one rule, `PresetDialog.CanAsk` = `!WorkflowEngine.IsUnattended`:
  1. The step's `params` value, when set, is used, attended or not, so a workflow author can always pin it. A set but invalid value fails.
  2. Missing, and a person is present (a button click, or an ATTENDED preset run): the original prompt or picker, exactly as before.
  3. Missing, and the run is unattended: the step fails naming the param. Never defaulted.
- **Why this rule (reversal within this branch):** the first pass gated inputs on `PresetDialog.Quiet` (any preset), which made a person running a KUT workflow by hand get "needs params.x" failures instead of pickers. The coordinator chose the most flexible option above. Pickers are back for attended runs, so their baseline lines read "shown only when a person is present (PresetDialog.CanAsk)". No baseline count rose: every picker had stayed in its person branch.
- **Params set in the WORKFLOW_KUT_*.json files** (pinned for attended and unattended runs alike, because the label or the workflow's purpose settles them):
  - Mobilisation `CDEStatus` `status=WIP`: "Initialise CDE state register" at kick-off, and ISO 19650 information starts in WIP.
  - Deliverable A/B/C/D `LOD_Verify` `milestone=deliverable-a/-b/-c/-d`: each label already named the id to pick.
  - Deliverable D `LOD_Stamp` `milestone=deliverable-d`: it stamps "the verified milestone", which step 6 verifies.
  - Deliverable A and GateAudit `PreTagAudit` `scope=project`: a deliverable gate audits the model, not whichever view is open.
- **Params deliberately NOT set.** Attended runs ask as before; unattended runs fail naming the param:
  - GateAudit `LOD_Verify` milestone: "the milestone you are approaching" is the author's call.
  - Every input file: `midpCsv`, `programTemplate`, `specToc`, `stationExport`, `fohlioExport`, `finishesExport`. None has a canonical location in the project.
  - `CSI_Assign` / `Fohlio_Import` / `Fohlio_ImportFinishes` `mode` (fill | overwrite): fill keeps stale values and overwrite discards manual ones, and no label says which.
- **Optional inputs, unattended:** reported, not failed, where a person's Cancel already meant "skip":
  - MIDP relative-month rows without `params.m0` are left out and counted.
  - `KUT_LifecycleReconcile` without `params.stationExport` skips the commissioned-unpriced check and the report names the param.
- **Gate overrides:** `CDEStatus`'s compliance-gate and role-gate override confirms follow rule 2/3. A person present is asked as before; an unattended run never overrides and fails naming the gate.
- **ExportSheetRegister save path — the one deliberate exception to rule 3:** `params.output` (a folder or `.csv`, relative to the routed SheetRegister folder) when set; else the save prompt for a person; else (unattended) the routed SheetRegister folder, the prompt's own "Project folder for this export" choice. It does not fail, because where an export lands is a project convention, not an input someone must supply.
- **Helpers in `PresetDialog`:**
  - `CanAsk`, `Param(key)` and `MissingParam(...)`.
  - `InputFile(doc, command, key, what, ask, ref msg, out stop)`, where `ask` is the command's own picker lambda. Keeping the picker in the command keeps it visible to the unattended gate.
  - `Show(title, instruction, content, ref msg)` for the hand-built result window.
- **D1:** the token fix is at the one choke point (SaveCredentials) and in the adoption rule, not in each card button. Any future writer gets the same protection.
- **D2:** an unclear create is VERIFIED, not abandoned. The issue is still created when ACC proves it absent. Unknown (search failed) = not sent and reported. A duplicate assigned to real people is worse than one more sweep's delay.
- **D9:** the copy goes through AccHttp with an optional AccCredentials, so the token-only signature still works. Moving the rest of AccDocsMetadata stays deferred (see A11).
- **Area 3 input rule (final, 6fc1d2de4):** a step param, when set, is always used; when missing, a person present is asked (PresetDialog.CanAsk = !IsUnattended); unattended runs fail naming the param. Result windows are quiet in any preset. Exception: ExportSheetRegister's unattended output defaults to the project's routed SheetRegister folder, which is a project convention rather than an invented value, so it does not fail.
- **CHANGELOG merge conflicts:** always keep both entries (tool: scratchpad keepboth.py refuses >1 region).
- **E1:** the hold is a separate, mutable `closedInAcc` list in the origins file, and the append-only `entries` are untouched. "Present" means the clash is in the latest *complete* pull of any recorded model set. With no complete pull on record, a closed escalation is held, never assumed gone.
- **E3:** a single item match that records a different version is refused too. That is stricter than the brief, so v3's approval cannot land on v1's only transmittal.
- **E4:** a contradicted code keeps the existing suitability, writes `iso_conflict` and is NOT applied. With no revision in the file name, the code is judged against the row's own revision.
- **E5:** the default is day-first (UK/Uganda); the KUT MIDP template has no explicit dates, so this is a convention, not something read from it. The order is overridable per workflow step (`dateOrder`), following the `m0` param pattern.
- **E6:** earlier imports with a defaulted MEDIUM priority are left as they are, because a deliberate MEDIUM cannot be told apart from a default.
- **E8:** the full-read timer still uses local time; using the max updatedAt there would force a full read on every run.
- **DOCX-REG-1:** the ISO note is its own display field. Suitability drives CDE logic, so a marker must never be written into it.
- **ACC-SRV-11:** the default is report, not create. It sends nothing to ACC, and a closed issue appearing in ACC is a visible change people did not ask for. Create mode is one config key away (`accClosedBetweenSweeps`). The baseline is the first sync's time (`accIssueSyncSince`), not the previous sweep's: a sweep that broke mid-run would otherwise lose an issue for good. Each issue is reported once (`accIssueClosedReported`). Whether ACC accepts a create whose status is `closed` is NEEDS MANUAL CHECK.
- **KUT build merge (c73ded891):** the Curtain System Tag conflict was resolved to main, which replaced it with the Temporary Structure Tag. Revit 2025 measured that a Curtain System tag cannot be made. The tag-families session was told.
- **F2:** the proposal stays PENDING on a partial apply rather than gaining a new state. Every existing reader already handles pending, and the targets already applied are recorded on the proposal, so a retry cannot apply twice. The first code chosen sticks, so a retry never asks again or mixes codes.
- **F4:** a fresh single-id GET is made only when a status is about to be sent, so a normal sweep costs nothing extra. If the read fails, the title/description still go and the status waits.
- **F5:** staleness is measured against the newest pull, not the clock, so a project that has not pulled for a month keeps its holds.
- **AUT-5:** subscribe only to `review.closed-1.0` by default. The close is when a decision exists, and `review.created-1.0` would only add noise (it is configurable via `Autodesk:WebhookEvents:autodesk.construction.reviews`). Receiving it is a notice, never an automatic CDE change: ACC review approval cannot be read reliably from the event payload alone.
- **AUT-6:** one batched scope change, on the server only (`data:create`). The plugin already had it. `account:write` was not added: nothing built needs it, and adding it would force everyone to sign in again for no benefit.
- **AUT-7:** SSA lives on the server, not in the plugin. The private key then sits in one secret store, not on every workstation. It is opt-in per connection, and a half configuration is refused by name; there is never a silent fall-back to the refresh-token path.
