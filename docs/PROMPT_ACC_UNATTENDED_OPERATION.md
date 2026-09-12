# Work prompt 3 — make the ACC coordination cycle run without prompting

**For an agent executing autonomously with no further input.** You cannot ask questions.
Everything you need is here. Where something is genuinely unknowable, the task says to leave it
blank with a marker and report it — never to guess.

This is the **third** build pass on the KUT/ACC work. The first two fixed correctness — a failed
read is no longer mistakable for an empty one. This one is about **operation**: the fortnightly
coordination cycle cannot currently run without a human clicking four dialogs.

Read first, in this order:
- `docs/KUT_INTEGRATION_READINESS_2026-09.md` — the per-integration verdicts and the evidence
- `docs/PROMPT_KUT_INTEGRATION_BUILD.md` and `_2.md` — the conventions this pass continues
- `docs/ROADMAP.md` row **IM-17** — a pending decision you must NOT pre-empt (see §6)

---

## 0. Non-negotiable rules

**Worktree.** Several agents share `C:\Dev\STINGTOOLS`. **Do not work in it.** Create your own
worktree and do everything there:

```bash
git worktree add C:/Dev/wt-acc-unattended -b claude/acc-unattended-operation origin/main
cd C:/Dev/wt-acc-unattended
```

**Base.** This work edits `AccPullClashesCommand.cs`, which PR #932 also edits. Check what has
landed before branching:

```bash
git log --oneline origin/main | head -8
ls StingTools/V6/AccCommandOutcome.cs StingTools/V6/AccFetchOutcome.cs
```

If those files are on `origin/main`, branch from `main` as above. If they are not, #932 is still
open — branch from `origin/claude/kut-integration-build-2` instead and say so in your report. Do
not re-implement anything from #927 or #932; reuse `AccFetchResult<T>`, `AccFetchStatus` and
`AccCommandOutcome`.

**Never run `deploy.bat`.** It rewrites the live Revit add-in manifest to point at whichever
checkout ran it, which silently hijacks the add-in slot from every other agent and from the user.
`build.bat` is safe. `dotnet build` is safe. **Do not open Revit.**

**Never force-push.** Not with `--force`, not with `--force-with-lease`. If a push is rejected,
rebase or merge and push again.

**Never commit secrets.** No API key, client secret, refresh token or credential of any kind enters
the repo — not in code, not in a test, not in an example file, not in a commit message.

**Never invent data.** If a container id, model-set id, threshold or code is unknown, leave it blank
with a `REPLACE_WITH_...` or `TODO(KUT):` marker and report it. A guessed value that reaches an
issued container has to be renumbered afterwards, and the number is already in transmittals.

**Never delete a deprecated shared parameter or its GUID.**

---

## 1. Standing project constraints

- **The tooling is never named in any KUT-issued document** — no product name, command name,
  parameter prefix (`ASS_`, `COM_`, `PRJ_`, …), file path, or `.py` / `.json` extension.
  `KUT_BIM_MANAGER_PLAYBOOK_INTERNAL_STINGTOOLS.docx` is the sole exception and is never shared.
  Internal `docs/` files (including this one) may name anything.
- **Never hand-edit a generated document.** Edit the generator under `tools/` and regenerate.
  `tools/check_kut_documents.py` (100 assertions, 5 documents) must stay green.
- **Speckle / "Speck-Link" is private.** Never name it as a dependency or a CDE in anything issued.
- **Documents carry no AI attribution.** PR bodies may.
- **Close Microsoft Word** before any git operation touching a `.docx`.
- End commit messages with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`; end PR bodies
  with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

---

## 2. The finding this pass acts on

**The ACC clients are already host-free.** Measured 2026-09-10:

```
StingTools/V6/AccIssueSync.cs         0 Autodesk.Revit references
StingTools/V6/AccOAuthFlow.cs         0
StingTools/V6/AccModelCoordSync.cs    0
StingTools/V6/AccModelUpload.cs       0
  vs the command shells that wrap them:
StingTools/Clash/AccPullClashesCommand.cs      3
StingTools/Clash/AccSyncIssueStatusCommand.cs  3
```

So nothing architectural blocks unattended ACC operation. **Four interactive dialogs do.**

**Three automation substrates already exist and are wired — none carries ACC:**

| Substrate | Where | Carries ACC? |
|---|---|---|
| Named workflow on document open | `Core/StingToolsApp.cs:1155-1161`, `TagConfig.AutoRunWorkflowOnOpen` | no |
| Periodic timer, lazy-started per document | `Core/StingToolsApp.cs:1292-1309`, `Clash/ClashScheduler.cs`, gated by `TagConfig.AutoStartClashScheduler` | no |
| APS Design Automation, true headless | `StingTools.Headless/HeadlessRunner.cs` — four read-only engines | no |

**The four seams:**

| # | Seam | Where |
|---|---|---|
| 1 | Coordination model-set picker | `Clash/AccPullClashesCommand.cs:87` (`StingListPicker.Show`) |
| 2 | Escalate-to-ACC-Issues confirmation, with a hardcoded cap of 10 | `Clash/AccPullClashesCommand.cs:150-168` (`Math.Min(10, scored.Count)`) |
| 3 | Suitability picker | `BIMManager/PlatformLinkCommands.cs:1122-1129` |
| 4 | File picker for upload | `Clash/AccUploadModelCommand.cs:134` — **already solved** by #932's `ACC_UploadLastBundle`; do not redo it |

`AccSyncIssueStatusCommand` has **zero** interactive gates. It is already unattended-capable and
nothing schedules it.

**There is no unattended / no-prompt concept anywhere in the codebase.** Verified: no
`Unattended`, `SuppressPrompt`, `NoPrompt` or equivalent in `TagConfig` or `StingOfflineConfig`.
That is why A1 comes first — four commands each inventing their own "skip the dialog if configured"
is precisely the vocabulary drift this codebase produces (see `docs/ROADMAP.md` IM-14, and IM-17
for a live example of two vocabularies for one field reconciled by a silent fallback).

---

## 3. A design constraint that will bite you if you follow the nearest precedent

`acc_credentials.json` lives at `%APPDATA%\Planscape\acc_credentials.json` — it is **per machine
and per user**, not per project. `AccCredentials` already carries `ProjectId` and
`CoordContainerId` there, which means **the existing design silently assumes one ACC project per
coordinator.**

Do not extend that assumption. A coordination **model-set id is project-scoped**: the same
coordinator working on KUT and another job needs a different one per model. Project-scoped settings
belong under the project, resolved through `StingPaths` — the documented rule is *"Never build a
project path by hand. Resolve it through `Core/StingPaths.cs`"*:

```csharp
StingPaths.MetaFile(doc, "_BIM_COORD", "acc_settings.json")
```

`ExLink/FohlioLink.cs` (`FohlioMap.ProjectFile`) is the pattern to copy. **Credentials stay
machine-scoped; operating settings become project-scoped.** Say in your report that
`ProjectId` / `CoordContainerId` remain mis-scoped — do **not** move them in this pass; that is a
migration with its own verification, and moving a credential file's contents is not a side quest.

---

## 4. How to work

**Build the gate before the feature, and break your own gate once.**

Every gate written on this project so far was wrong on its first run. #932's A1 gate passed 37/37
against sabotaged code because a downstream check reached the same verdict by another route — an
**alternative-path escape** — and only deliberate sabotage found it.

For each gate:

1. Write the gate first.
2. Run it. If it passes before the fix exists, **the gate is wrong**. Fix the gate.
3. Write the fix. The gate must now pass.
4. **Sabotage deliberately** — re-break the exact thing the gate exists to catch — and confirm it
   **fails**. Restore.
5. Report per gate: the command, the pass result, the sabotage, and that it failed.

**The testability problem specific to this pass, and its answer.** These seams are Revit UI
dialogs; you cannot assert "it did not show a TaskDialog" in a headless test. So **extract the
decision, not the dialog**: a pure, Revit-free, log-free policy object that answers *given this
configuration, should we prompt, and if not what do we do instead?* — then the command asks it and
only shows UI when the answer says to. `V6/AccFetchOutcome.cs` and #932's `V6/AccCommandOutcome.cs`
are the precedent; `StingTools.Acc.Tests/TestHelpers/StingLogShim.cs` is how a source that logs
becomes linkable.

**Assert on non-empty data.** A test that passes against an empty result set proves nothing.

---

## 5. SECTION A — the work

Ordered so each task unblocks the next. **A1 before everything**, or you will build four
inconsistent config keys.

---

### A1 — One source of truth for "may I prompt?"

**Why first.** Four commands need the same decision. There is no existing concept, so whatever the
first task invents becomes the convention by accident. Decide it deliberately, once.

**Files**
- NEW `StingTools/V6/AccOperatingPolicy.cs` — Revit-free and **log-free** (so it links into tests).
  It must answer at least: may this run prompt? is there a configured model set? what is the
  escalation policy? what suitability should an unattended publish use? Load from the
  project-scoped file named in §3, with a documented default when the file is absent.
- NEW `StingTools.Acc.Tests/AccOperatingPolicyTests.cs`.

**Contract that matters most:** *absent configuration must mean "prompt", never "assume".* A missing
settings file is the normal state for every existing project, and a default that silently picks a
model set or a suitability code would make an unconfigured project act on a guess. Interactive
behaviour is the safe default; unattended is opt-in.

**Done means** one type answers all four questions, its defaults are prompt-preserving, and nothing
else in the codebase parses that file.

**Prove it**

```bash
dotnet test StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo --filter "FullyQualifiedName~OperatingPolicy"
```

Required cases: absent file → every answer is "prompt"; malformed JSON → "prompt" **and** the
failure is distinguishable from absence (a corrupt settings file must not read as a deliberate
default — that is the IM-17 shape); each field present → that field's answer, others still "prompt".

**Sabotage:** make the absent-file case return a configured default instead of "prompt"; confirm
red. Then make malformed JSON indistinguishable from absent; confirm red.

---

### A2 — Remember the coordination model set

**Why.** `AccPullClashesCommand.cs:87` opens a picker every run. It is the single dialog standing
between the fortnightly cycle and unattended operation, and the answer is the same every fortnight.

**Files**
- `StingTools/Clash/AccPullClashesCommand.cs` — when the policy names a model set **and** that set
  is present in what ACC returned, use it and skip the picker. Otherwise prompt as today.
- The policy from A1 + the project-scoped settings file.
- A way to change the remembered set without hand-editing JSON — the BCC ACC card
  (`UI/BIMCoordinationCenter.cs`, `BuildAccDetail`) is where the other ACC settings live.

**Two contracts.**
- **A remembered id that ACC no longer returns must not fail silently or fall back to "the first
  one".** A model set can be renamed, archived or replaced. Report it by name and id, and prompt
  (interactive) or fail with that reason (unattended). Pulling clashes from the *wrong* set is
  worse than pulling none, because it looks like a clean federation — the exact failure #927 fixed.
- Persisting the choice writes a project file. Do it only on an explicit choice, never as a side
  effect of a read.

**Done means** a configured project runs `ACC_PullClashes` with no picker; an unconfigured one
behaves exactly as today; a stale id is named, never guessed past.

**Prove it** — the resolution is a pure function (remembered id + available sets → chosen set or a
reason). Test it, not the dialog:

```bash
dotnet test StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo --filter "FullyQualifiedName~ModelSet"
```

Required: remembered id present in the list → chosen; remembered id absent → a *named* failure, not
the first set; no remembered id → "prompt"; empty available list → not a resolution failure but the
existing `EmptyOk` path (do not conflate them).

**Sabotage:** make the stale-id case fall back to `sets[0]`; confirm red. This is the one that
matters — it is the difference between a wrong federation and no federation.

---

### A3 — Escalation becomes a policy, not a dialog and a magic 10

**Why.** `AccPullClashesCommand.cs:150-168` shows a triage report and offers *"Push top N clashes
to ACC Issues"*, where N is `Math.Min(10, scored.Count)` — hardcoded. Escalation **creates ACC
Issues assigned to real people**, so this is the seam where automation is most dangerous and needs
the most explicit policy.

**Files**
- `StingTools/Clash/AccPullClashesCommand.cs`
- The policy from A1.

**Contracts.**
- The policy expresses *how many* and *above what triage score*. Both, not either — a count alone
  escalates trivia on a clean model; a score alone escalates hundreds on a bad one.
- **Unattended escalation is opt-in and off by default.** An unattended run with no escalation
  policy pulls, triages, writes the CSV and escalates **nothing**. That is a useful cycle on its
  own and it creates no work for anyone.
- Keep the interactive dialog for interactive runs, and show the policy that *would* apply.
- Idempotency is already correct (order-invariant signature + `pushed_clashes.json`); do not touch
  it, and do not let a policy change re-push what is already tracked.

**Done means** an unattended run cannot create an ACC Issue unless someone configured it to, and a
configured run escalates exactly the set the policy names.

**Prove it**

```bash
dotnet test StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo --filter "FullyQualifiedName~Escalat"
```

Required: no policy → zero escalations even with 500 scored clashes (**assert the count is 0 against
a non-empty input** — a test that feeds it nothing proves nothing); count-only and score-only
policies rejected or documented; both set → exactly the intersection; already-pushed excluded.

**Sabotage:** make the no-policy case escalate the top 10; confirm red.

---

### A4 — Suitability for an unattended publish

**Why.** `PlatformLinkCommands.cs:1122-1129` asks S1/S2/S3/S4 and defaults to `S3` on cancel.

**Files** — `StingTools/BIMManager/PlatformLinkCommands.cs`, the policy from A1.

**Contract.** When the policy names a suitability, use it; otherwise prompt exactly as today. **Do
not change the interactive default from S3.** If you configure a KUT default, S1 (*Fit for
Coordination*) is what the playbook calls the fortnightly cycle's state
(`GUIDES/KUT_PROJECT_DELIVERY_PLAYBOOK.md:315` — *"SHARED — coordination-ready. Suitability S1–S4.
This is where the fortnightly cycle happens."*) — but **do not write a KUT settings file yourself**;
name the recommendation in your report and leave the choice to the Information Manager.

**Prove it**

```bash
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary --nologo
dotnet test StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo --filter "FullyQualifiedName~Suitab"
```

**Sabotage:** make the unset case silently pick S1; confirm red.

---

### A5 — Schedule the reconcile that already needs no human

**Why.** `AccSyncIssueStatusCommand` has zero interactive gates. It pulls ACC Issues and un-tracks
clashes whose issues closed, so a recurrence re-raises. It writes **nothing to ACC** — only a local
sidecar — which is exactly why it is safe to automate and the upload is not.

**Files** — `StingTools/Data/WORKFLOW_KUT_CoordinationCycle.json`.

Add it as a step **after** the clash pull. Mark it `"optional": true`, matching the other ACC steps
in that file, so a credential-less run does not fail the chain. Write the `label` to say what it
does in plain words a KUT operator reads — and, given #932 relabelled step 6 for exactly this
reason, make sure it does not overstate.

**Done means** the fortnightly cycle reconciles issue status without anyone remembering to click a
button, and the step resolves.

**Prove it**

```bash
python tools/check_kut_workflow_tags.py .    # must stay 100%, with the new step counted
```

State the new step total explicitly (it was 8 in that file, 97 across 10 files).

**Sabotage:** misspell the new `commandTag`; confirm the checker exits non-zero; restore.

---

### A6 — Prove the cycle is actually unattended

**Why.** A1–A5 each remove one prompt. Nothing yet proves the *chain* has none left — and "I
removed the dialogs I knew about" is exactly the claim that needs a check rather than a memory.

**Files** — NEW `tools/check_unattended_cycle.py`.

For every `commandTag` in `WORKFLOW_KUT_CoordinationCycle.json`, resolve the command class through
`WorkflowEngine.ResolveCommand`'s case labels (reuse the extraction in
`tools/check_kut_workflow_tags.py` — **import it, do not copy it**), find that class's source file,
and report any interactive construct reachable in it: `StingListPicker.Show`, `OpenFileDialog`,
`AddCommandLink`, `.ShowDialog()`, `TaskDialogResult`.

**Be honest about what this is.** It is a **source-level smoke check, not a proof**: it cannot
follow calls into helpers, cannot tell a gated prompt from an ungated one, and will flag a dialog
that A1's policy correctly bypasses. So it must produce a **reviewed inventory**, not a pass/fail on
count alone — every remaining hit is either baselined with a one-line reason (e.g. "shown only when
the policy says prompt") or removed. Model the baseline mechanic on
`tools/param_name_targets_baseline.txt`: **the baseline only shrinks** — the gate fails on a new
hit, and also on a baselined hit that no longer exists.

`TaskDialog.Show` for a *result* message is not a prompt; do not baseline what is not a gate, and
say in the tool's docstring how you distinguish them.

**Prove it**

```bash
python tools/check_unattended_cycle.py .
```

**Sabotage:** add a `StingListPicker.Show` to a command in the cycle; confirm a new hit fails the
gate. Then delete a baselined entry's underlying code; confirm the stale-baseline arm fails too.

---

## 6. SECTION B — do NOT do these

**Do not automate the upload into the CDE.** #932 wired `ACC_UploadLastBundle` to run without a file
picker and deliberately did not add it to any workflow. Keep it that way. Uploading to the CDE is
*issuing*, which the BEP makes a per-stage act with a transmittal and client review; and
`docs/ROADMAP.md` **IM-17** records that `ACCPublish` already writes a transmittal row claiming
`ISSUED` for a package that never left the disk. Automating the upload before that row is resolved
would make a false record true in the worst possible way — by issuing on a timer.

**Do not touch the transmittal status vocabulary.** IM-17 names three defensible options and states
plainly that choosing is an Information-Manager decision about an ISO 19650 record. IM-9 in the same
table is the precedent for what happens when that judgement is guessed. Leave it.

**Do not move the ACC read path off Revit.** The clients are host-free and could run in a service —
that is the right long-term shape, and it is **not this pass**. It relocates credential custody from
one coordinator's `%APPDATA%` to a server, which is a security decision, and it should follow the
first live verification rather than precede it.

**Do not move `ProjectId` / `CoordContainerId`** out of the credentials file (§3). Report the
mis-scoping; do not migrate it here.

**Do not weaken any #927 / #932 guarantee.** A failed read must still be impossible to mistake for
an empty one. If a change of yours makes an unattended run swallow a failure to keep the chain
going, you have reintroduced the defect two passes removed. **An unattended run must fail loudly and
stop, not carry on quietly.**

**Do not refactor the god-files** (`StingCommandHandler.cs`, `BIMCoordinationCenter.cs`). Add your
cases and controls; change nothing else in them.

---

## 7. SECTION C — blocked on someone else

| # | Blocked work | Who must act |
|---|---|---|
| **B1** | One live ACC pull and one live issue push, to confirm the `bim360/clash/v3` sub-paths | **Owner / Autodesk account holder** (APS app, Traditional Web App, callback `http://localhost:8910/callback`, scopes incl. `data:create` — see PR #933); **ACC project admin** (container ids) |

**Overdue** — mobilisation began the week of 25 August 2026 and the fortnightly cycle depends on it.
Do not mock it, do not mark it done. If your work changes what a failure looks like, **update
`docs/KUT_LIVE_VERIFICATION_RUNBOOK.md`** so its expected-output sections still match reality. That
is offline work and it is in scope.

---

## 8. Finishing

```bash
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary --nologo   # 0 errors, 0 warnings
dotnet test  StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo     # baseline 67 passing
dotnet test  StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo     # baseline 1,335 passing
python tools/check_kut_workflow_tags.py .                                  # 100%, new step counted
python tools/check_unattended_cycle.py .                                   # new in A6
python tools/check_kut_documents.py                                        # 100 assertions, 5 docs
python tools/check_docs_index.py                                           # every docs/ file indexed
python tools/check_roadmap_ids.py                                          # one row per id
```

Both test baselines must **rise, not fall** — say by how much and why.

Then commit and open a PR stating, per task: **what changed** with `file:line`; **the command that
proves it** and its real output; **the sabotage you ran** and that the gate failed under it;
**every marker** you introduced; and **what you could not do** and who must act.

Report failures plainly. If a gate will not go green, say so with the output rather than narrowing
the gate until it passes — a gate relaxed to pass is worse than a red one, because it reports health
it has not checked.

**If you finish only part of Section A, A1 + A2 in full beat all six half-done.** A2 is the seam
that actually blocks the fortnightly cycle; everything after it is refinement. Scaling the work down
is the user's call, not yours — say explicitly what you left and why.
