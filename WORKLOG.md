# WORKLOG — continuous fix-and-improve loop (ACC first)

Standing task (2026-10-01): unattended loop — resume → research → record → fix → verify → commit → merge → update ROADMAP/WORKLOG → repeat. Priority: (1) ACC integration, (2) everything ACC touches, (3) rest of the codebase.

## Resume here
1. Deploy the INTEGRATION branch (`claude/acc-work-review-gaps-7e2ac7`, = origin/main + all ACC work) to `C:\Dev\STING_KUT_LIVE` once `tasklist | grep Revit` is empty. Revit has been open all session. Verify per "Deploy".
2. Area 3, continued: the 74 UNGATED lines in `tools/unattended_cycle_baseline.txt`. Next: Mobilisation (CDEStatus, LoadSharedParams, CreateFilters/Worksets), Deliverable A–D (LOD, ProgramAudit, OwnerStandards, DeviceCoordination, Fohlio), LifecycleReconcile (CSI, SpecLink, Niagara, KUT valuation), MonthlyReport. Pattern: PresetDialog.Show(..., ref message); a picker reads WorkflowEngine.StepParam and fails when the param is missing.
3. Then a fresh deeper ACC audit pass (round 3): auth/token expiry, paging and rate limits across every ACC client (AccHttp consumers), the BCC ACC card end to end, and webhooks.

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
- **C1 recovery on KUT:** look in `_data/coord` (the `_BIM_COORD` alias) for `transmittals.json.corrupt.*` and `deliverables.json.corrupt.*`. If any exist, the old gate quarantined them: merge their rows back into the live array, and keep the newer rows on any id clash.
- **C6 PDF byte-stability:** export one sheet twice from the Export Centre with no change, then compare SHA-256 (`certutil -hashfile x.pdf SHA256`). If they differ, re-exports land as HELD (reported, not failing), and a normalised hash can be added.
- **Revision/issue workflow (R1-R14):** in Revit, on a copy: (1) break a sheet stamp and run the KUT fortnightly preset; step 1 fails, and steps 6/7 show BLOCKED with nothing uploaded. (2) Run with no clouds; step 2 fails with 'no sheet carries a cloud'. (3) Lock a title block (PRJ_TB_LOCK) with a stale revision; Leak Chk lists it as LOCKED, and the preset passes step 1. (4) Supersede a deliverable whose PDF+DWG went up through the Export Centre; both are archived and the ledger shows retiredUtc. (5) The BIM > Revision Management buttons 'Per-Sheet #' and 'Leak Chk' run.
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
- **CHANGELOG merge conflicts:** always keep both entries (tool: scratchpad keepboth.py refuses >1 region).
