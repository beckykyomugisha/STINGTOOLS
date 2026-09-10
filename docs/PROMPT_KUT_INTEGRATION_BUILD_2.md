# Work prompt 2 — finish the KUT integration work (Fohlio · Niagara · ACC)

**For an agent executing autonomously with no further input.** You cannot ask questions. Everything
you need is here. Where something is genuinely unknowable, the task says to leave it blank with a
marker and report it — never to guess.

This is the **second** build pass. The first (PR #927, branch `claude/kut-integration-build`) fixed
the ACC clash-read path and is good work — verified by re-running its gates and by five independent
sabotages, all caught. **Read `docs/KUT_INTEGRATION_READINESS_2026-09.md` and
`docs/PROMPT_KUT_INTEGRATION_BUILD.md` first**; they carry the `file:line` evidence and the
conventions this pass continues.

---

## 0. Non-negotiable rules

**Worktree.** Several agents share `C:\Dev\STINGTOOLS`. **Do not work in it.** Create your own
worktree and do everything there:

```bash
git worktree add C:/Dev/wt-kut-build2 -b claude/kut-integration-build-2 origin/main
cd C:/Dev/wt-kut-build2
```

Branch from `origin/main`, never from another unmerged branch. If you are already in an isolated
worktree provided by the harness, use that one rather than creating a second.

**If PR #927 is not yet merged into `main`**, its work is a prerequisite for most of this pass —
`AccFetchResult<T>` and `StingTools.Acc.Tests` come from it. Check first:

```bash
git log --oneline origin/main | head -5
ls StingTools/V6/AccFetchOutcome.cs
```

If `AccFetchOutcome.cs` is absent from `origin/main`, branch from
`origin/claude/kut-integration-build` instead and say so in your report. Do not re-implement it.

**Never run `deploy.bat`.** It rewrites the live Revit add-in manifest to point at whichever
checkout ran it, which silently hijacks the add-in slot from every other agent and from the user.
`build.bat` is safe. `dotnet build` is safe. **Do not open Revit.**

**Never force-push.** Not with `--force`, not with `--force-with-lease`. If a push is rejected,
rebase or merge and push again.

**Never commit secrets.** No API key, client secret, refresh token, station password or credential
of any kind enters the repo — not in code, not in a test, not in an example file, not in a commit
message. Example files carry `REPLACE_WITH_...` placeholders only.

**Never invent data.** If a mapping, code, URL, container id, parameter name or credential is
unknown, leave it blank with a `REPLACE_WITH_...` or `TODO(KUT):` marker and say so in your final
report. A guessed value that reaches an issued container has to be renumbered afterwards, and the
number is already in transmittals. Guessing is worse than leaving a hole, because a wrong value
looks supplied.

**Never delete a deprecated shared parameter or its GUID.** Parameters are append-only here.

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

## 2. The defect class you are finishing

PR #927 introduced `StingTools/V6/AccFetchOutcome.cs` — `AccFetchStatus` (`Ok` / `EmptyOk` /
`AuthFailed` / `NotFound` / `TransportFailed`) and `AccFetchResult<T>` with a single `Succeeded`
predicate. It applied that discipline to **one** read path: ACC Model Coordination clashes.

**The same defect is still live in two other read paths**, and one of them is worse than the one
that was fixed. In both, a read that FAILED is returned as a collection that looks like a read that
SUCCEEDED and found nothing.

This is the house failure mode. `CLAUDE.md` describes it: *"an empty list standing in for an
error."* Your job is to finish applying the fix, not to invent a new mechanism — **`AccFetchResult<T>`
already exists; use it.**

Where things live:

```
StingTools/V6/AccFetchOutcome.cs              the outcome type (from #927) — reuse, do not re-invent
StingTools/V6/AccIssueSync.cs                 PullIssuesAsync  <-- TASK A1
StingTools/Clash/AccSyncIssueStatusCommand.cs its consumer     <-- TASK A1
StingTools/Core/Twin/NiagaraJsonClient.cs     ParsePoints / FetchPoints  <-- TASK A2
StingTools/Core/Twin/NiagaraPointParser.cs    the pure parser (already 43 tests)
StingTools/Core/Twin/CommissioningSource.cs   live->cached->none resolver <-- TASK A2
StingTools/Clash/AccPullClashesCommand.cs     fixed in #927; its branch is untested <-- TASK A3
StingTools.Acc.Tests/                          from #927 — extend it
StingTools.Boq.Tests/                          already links NiagaraPointParser.cs
```

**Two established patterns you must reuse rather than reinvent:**

- **Linking Revit-free sources into tests:**
  `<Compile Include="..\StingTools\...\X.cs" Link="X.cs" />`.
- **Linking a source that calls `StingLog`:** `StingTools/Core/StingLog.cs` cannot be linked (it
  drags in `StingTools.Core.Drawing`). Use the test-only no-op shim in the same namespace —
  `StingTools.Acc.Tests/TestHelpers/StingLogShim.cs` (also in `StingTools.Visibility.Tests`). This
  is how `CommissioningSource.cs` and `NiagaraJsonClient.cs` become testable.

**Dispatch is three layers** — `StingCommandHandler.cs` case, `WorkflowEngine.ResolveCommand` case
(`WorkflowEngine.cs` lines ~1395–2244, exactly one `switch`), and a `Tag="..." Click="Cmd_Click"`
button in `StingDockPanel.xaml`. One-layer audits have twice produced large false-positive figures
here.

---

## 3. How to work

**Build the gate before the feature, and break your own gate once.**

The #927 agent's A1 gate was wrong on its first run and **only deliberate sabotage found it**: it
swallowed an `HttpRequestException` as `EmptyOk` and 37/37 still passed, because a downstream
payload-shape check turned the bogus success into `TransportFailed` by another route — satisfying
the assertion without the transport failure ever being attributed. That is an **alternative-path
escape**, and it is the reason this rule is not optional.

For every gate:

1. Write the gate first.
2. Run it. If it passes before the fix exists, **the gate is wrong**. Fix the gate.
3. Write the fix. The gate must now pass.
4. **Sabotage deliberately** — re-break the exact thing the gate exists to catch — and confirm it
   **fails**. Restore.
5. Report, per gate: the command, the pass result, the sabotage used, and that it failed.

Watch for both gate faults: **circular expectations** (asserting what the code does rather than what
is required) and **alternative-path escapes** (checking one route when the code has two). Where a
result can be reached by more than one route, **assert the attribution, not just the verdict** —
#927 did this by asserting `HttpStatus == 0` for a transport-down case, which only passes if the
failure was attributed to the transport rather than inferred downstream. Copy that technique.

**Assert on non-empty data.** A test that passes against an empty result set proves nothing.

---

## 4. SECTION A — work you can complete offline

Ordered by what reaches KUT soonest. **A1 before A2** even though A2 is the more severe defect:
ACC coordination is running fortnightly *now* (mobilisation began the week of 25 August 2026), while
the Niagara path is not due until Stage 3.1 (~M40). Do not reorder.

---

### A1 — A failed ACC issue read must not read as "the issues are gone"

**The defect.** `AccIssueSync.PullIssuesAsync` (`AccIssueSync.cs:266`) returns a bare
`List<AccIssue>`:

- `:269` — auth failure → `return list` (**empty**)
- `:285` — HTTP error part-way through pagination → `return list` (**partial**)
- `:308` — success → `return list`

All three are indistinguishable to the caller. `AccSyncIssueStatusCommand` then builds `statusById`
from that list and reports every tracked clash whose issue is absent as `NOT_FOUND` with action
`keep`. So:

- **auth failure** → *every* escalated clash reports `NOT_FOUND`, which reads to a coordinator as
  "ACC deleted our issues", not "the read failed";
- **partial pagination failure** → the issues on the pages that did arrive are reconciled and
  un-tracked, while the rest report `NOT_FOUND` — a partial reconciliation presented as a complete
  one, and the sidecar is then **written** (`AccSyncIssueStatusCommand.cs:87`).

**Files to touch**
- `StingTools/V6/AccIssueSync.cs` — `PullIssuesAsync` returns `AccFetchResult<List<AccIssue>>`.
  Classify with the existing `AccFetchOutcome`. A partial read **is a failure**, not a success:
  if any page fails, the outcome must not be `Ok`/`EmptyOk`, and the `Detail` must say how many
  pages succeeded before it broke.
- `StingTools/Clash/AccSyncIssueStatusCommand.cs` — branch on `Succeeded`. On any failure: say what
  failed, name the container, return **`Result.Failed`**, and **do not write the sidecar**. Reuse
  the message shape `AccPullClashesCommand.FailureMessage` already establishes — it says
  *"NOTHING WAS CHECKED — this is not a clean result"* and never the words "not found".
- Any other caller of `PullIssuesAsync` (search for it; fix them all).
- `StingTools.Acc.Tests/` — tests.

**Done means**
- No caller can distinguish-by-accident: a failed or partial read cannot produce a reconciliation.
- The sidecar is never mutated on a read that did not fully succeed.
- `Result.Failed` on failure, so a workflow step cannot record a reconciliation it never did.

**Prove it**

```bash
dotnet test StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo
```

Required cases, using the loopback server already in
`StingTools.Acc.Tests/TestHelpers/LoopbackServer.cs`:
- auth failure → not `Succeeded`; the returned list is empty **and** the status is `AuthFailed`
  (assert the status, not just emptiness — emptiness alone is the bug);
- page 1 returns a full page, page 2 returns 500 → **not** `Succeeded`, and `Detail` names the page
  count. Assert the partial rows are *not* presented as a complete set;
- two full pages then a short page → `Ok`, with the **summed** count asserted non-zero;
- a container with genuinely zero issues → `EmptyOk`.

**Sabotage to run and report:** (a) make the pagination-failure branch return `Ok`; confirm failure.
(b) make the auth-failure branch return `EmptyOk`; confirm failure.

---

### A2 — A malformed Niagara feed must not read as "live, nothing commissioned"

**The defect, and why it is the worst one left.** Three steps compound:

1. `NiagaraPointParser.Parse` correctly sets `Error` on unparseable JSON and returns **empty**
   `Points` (`NiagaraPointParser.cs:80`).
2. `NiagaraJsonClient.ParsePoints` (`:68-78`) logs that failure — **and then returns `r.Points`
   anyway (`:77`), discarding the `Failed` flag.**
3. `FetchPoints` (`:82`) returns that empty dictionary. It is **not null**, so
   `CommissioningSource.Resolve` (`CommissioningSource.cs:69-77`) takes the live branch, sets
   `Source = Live`, and reports `"live (captured …)"`.

The result: a station that returns garbage — or an HTML error page, or a JSON error envelope like
`{"error":"unauthorized"}`, which parses as an object, yields zero points and **sets no error at
all** — is reported as a **successful live reading in which nothing is commissioned**.

And it is worse than a wrong number, because step 4 is:

4. `CommissioningSource.Persist` (`:75`, `:104-116`) **writes that empty result over
   `last_station_points.json`** — destroying the last good snapshot, which is the fallback that
   exists precisely for a station being unreachable.

`KUT_ValuationFromBms` consumes this to produce a commissioning valuation percentage, which the
cost module carries toward payment certification. A fabricated 0% presented as live, with the
recovery cache deleted, is the most consequential defect remaining in these three integrations.

**Files to touch**
- `StingTools/Core/Twin/NiagaraPointParser.cs` — the parser must be able to say *"this body was
  not a points feed I recognise"* separately from *"this was a points feed with no points"*. Today
  a JSON object of arbitrary keys silently produces zero points and no error (`:73` iterates
  `obj.Properties()` and adds nothing). Add a recognition signal — e.g. how many candidate entries
  were seen — so an error envelope is distinguishable from an empty station. **Do not** make a
  bare string/number/bool an error; the existing comment (`:75`) explains that choice and it is
  correct.
- `StingTools/Core/Twin/NiagaraJsonClient.cs` — `ParsePoints`/`FetchPoints` must not discard the
  failure. Return a result that carries it (mirror `AccFetchResult<T>`, or return `null` on a
  failed parse as the transport path already does — either is acceptable, but the caller must be
  unable to mistake it for a successful empty read).
- `StingTools/Core/Twin/CommissioningSource.cs` — two rules:
  - a failed live read must fall through to the cache exactly as an unreachable station does, and
    end as `Cached` or `None`, never `Live`;
  - **`Persist` must never overwrite an existing snapshot with the result of a read that did not
    succeed.** Overwriting a good cache with an empty one turns a recoverable outage into data loss.
- NEW or extended test coverage — `CommissioningSource.cs` and `NiagaraJsonClient.cs` are Revit-free
  but call `StingLog`; link them with the `StingLogShim` pattern named in §2.

**Done means**
- A malformed body, an HTML error page, and a JSON error envelope each end as a **failure**, not as
  `Live` with zero points.
- A genuinely empty station still ends as a successful empty live read (do not over-correct —
  a station with nothing commissioned yet is a real and expected state early in Stage 3).
- The cached snapshot survives every failure mode.

**Prove it**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~Niagara"
```

Required cases:
- unparseable body → failure; `CommissioningSource` returns `Cached` when a snapshot exists and
  `None` when it does not — **never** `Live`;
- `{"error":"unauthorized"}` → failure, **not** an empty live read. This is the case that sets no
  error today; assert it specifically;
- HTML (`<html>…`) body → failure;
- a valid feed with zero points → still `Live` and `EmptyOk`-equivalent (the legitimate empty case);
- **cache preservation:** write a good snapshot, then run a failed live read, then assert the
  snapshot file is **byte-identical** to what it was. This is the assertion that catches the data
  loss, and it must be a file-content comparison, not just "Resolve returned Cached".

**Sabotage to run and report:** (a) restore the `return r.Points` discard in `ParsePoints`; confirm
failures. (b) make `Persist` write unconditionally again; confirm the cache-preservation test fails.

---

### A3 — Test the last mile: the command branches that decide `Result.Failed`

**Why.** The whole point of #927's fix, and of A1 above, is that a workflow step **fails** instead
of passing. That decision lives in `AccPullClashesCommand` and `AccSyncIssueStatusCommand`, which
are Revit-bound and therefore linked into no test project. Today the branch that makes the fix
matter is verified by reading only. That is the same class of gap the whole exercise is about.

**Files to touch**
- Extract the pure decision — *given an `AccFetchResult`, what does the user see and what does the
  command return?* — into a Revit-free, log-free helper (the `AccFetchOutcome` precedent). The
  command keeps the Revit shell and calls the helper.
- `StingTools.Acc.Tests/` — tests over the helper.

**Done means** a table-driven test asserts, for **every** `AccFetchStatus` value, the resulting
`Result` and that the message does not contain the words `clash-clean`, `no clashes`, or `not found`
for any non-succeeding status.

Enumerate with `Enum.GetValues<AccFetchStatus>()` rather than listing cases, so a future status
member is covered without anyone remembering to add it. **Assert the enumeration is non-empty**, or
the test passes vacuously.

**Prove it**

```bash
dotnet test StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo --filter "FullyQualifiedName~Outcome"
```

**Sabotage to run and report:** add a new `AccFetchStatus` member and do **not** handle it in the
helper; confirm the enumeration test fails rather than silently ignoring it. Remove it afterwards.

---

### A4 — Gate the workflow-tag checker in CI

**Why.** `tools/check_kut_workflow_tags.py` (from #927) is well built — it self-tests on a
known-good and a nonsense tag, asserts its extraction window holds exactly one `switch`, and
confirms it is the *right* switch. Verified: exit 1 when sabotaged, 0 when clean. **But nothing
runs it.** A checker nobody runs rots, and this one guards the KUT presets a live project depends on.

**Files to touch** — NEW `.github/workflows/kut-workflow-tags.yml`.

Model it on `.github/workflows/kut-document-gate.yml`. Trigger on changes to
`StingTools/Data/WORKFLOW_KUT_*.json`, `StingTools/Core/WorkflowEngine.cs`, and the checker itself.
Python 3.11, stdlib only (the checker has no dependencies — keep it that way).

**Done means** the gate runs on PRs touching those paths and fails the build on an unresolved tag.

**Prove it**

```bash
python tools/check_kut_workflow_tags.py .        # exit 0
python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/kut-workflow-tags.yml')); print('workflow YAML parses')"
```

Then prove the wiring end-to-end locally by running exactly the command the workflow runs, with a
sabotaged tag in place, and confirming a non-zero exit. Paste both exit codes.

---

### A5 — Decide the ACC upload chaining, now that it is decidable

**Context.** #927 deliberately left this open, correctly: it added `ACC_UploadModel` (file picker,
all three dispatch layers) but did **not** put it in the fortnightly Coordination Cycle, because a
workflow step cannot answer *"upload which file?"* and guessing would put an unintended file in an
issued CDE container.

**It is now answerable.** `ACCPublish` already builds a ZIP at a deterministic path and registers
it: `PlatformLinkCommands.cs:1196` computes `zipPath` and `:1201` calls
`BIMManagerEngine.AutoRegisterExport(doc, zipPath, "CM", …)`. So *"upload the bundle step 6 just
produced"* is a file choice that is not a guess.

**Do this, and only this:**
- Make `ACCPublish` record the bundle it produced somewhere a later step can read — the existing
  export register is the natural home; a small sidecar under `_BIM_COORD/acc/` is acceptable. Do
  not change what `ACCPublish` itself does.
- Give `ACC_UploadModel` a non-interactive mode that uploads *that recorded bundle* when it exists,
  keeping the file picker for the manual case.
- **Do not add the step to `WORKFLOW_KUT_CoordinationCycle.json`.** Wiring the capability is offline
  work; putting an automatic upload into an issued CDE container is a decision for the Information
  Manager, and it should not happen before B1 (below) has proved one live round-trip. Say in your
  report that the capability is ready and the step is deliberately not added.

**Done means** the upload can run without a human picking a file, and no workflow uploads anything yet.

**Prove it**

```bash
grep -n "AutoRegisterExport\|zipPath" StingTools/BIMManager/PlatformLinkCommands.cs | head
grep -rn "ACC_UploadModel" StingTools/UI/StingCommandHandler.cs StingTools/Core/WorkflowEngine.cs StingTools/UI/StingDockPanel.xaml
grep -c "ACC_UploadModel" StingTools/Data/WORKFLOW_KUT_CoordinationCycle.json   # must be 0
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary --nologo
```

Before trusting that `0`, prove the grep can match — run it against a string you know is in the file
(`grep -c ACCPublish` on the same file must be ≥ 1). A grep you have not seen succeed is not evidence.

---

### A6 — Make `AccModelUpload` report failures like everything else now does

**Why.** `AccModelUpload.UploadResult` (`AccModelUpload.cs:42-45`) carries only `bool Ok` and a
`string Message`. Every other ACC path now carries a status kind, an HTTP status and a `Detail`. A
caller cannot tell an auth failure from a 404 folder from a network drop, which is the same
under-attribution — milder, because `Ok=false` at least does not read as success.

**Do:** align it with `AccFetchResult<T>` / `AccFetchStatus` — reuse, do not parallel-invent. Keep
`Ok` if callers depend on it (`BIMCoordinationCenter.cs` ~`:5581` reads `r.Ok`/`r.Message`); adding
the kind alongside is enough. Update callers to surface the kind.

**Done means** an upload failure names which kind of failure it was.

**Prove it**

```bash
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary --nologo
grep -rn "UploadAsync" --include="*.cs" StingTools/ | grep -v /obj/
dotnet test StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo
```

Add at least one loopback test: a 403 on storage creation must surface as an auth-kind failure, not
a bare `Ok=false`.

---

### A7 — Correct the stale claims in `CLAUDE.md`

**Why.** `CLAUDE.md` is the single onboarding surface; a wrong headline mis-informs every human and
agent who starts there. These were measured on 2026-09-10 and confirmed still stale on the #927
branch. **Correct only these**; do not restyle the file or re-measure anything you have not run.

| Line / claim | Measured | Fix |
|---|---|---|
| Test table row `\| Boq \| 121 \| ✅ 196 cases, 0 failing \|` (~`:132`) | **1316 passing, 0 failing** | Update the row; keep the table's "declared vs executed" caveat |
| Healthcare caveat 3 — *"`TwinReadback` BACnet / OPC-UA transports are abstract stubs"* | True of `TwinReadback.cs:39,46`, but it reads as "Niagara is absent" when the file-mediated path and a real oBIX HTTP client both exist | Keep the stub statement, add one sentence naming `NiagaraJsonClient` and `NiagaraCommands` |
| Anywhere claiming 9 KUT workflows | There are **10** | Correct the count |

**Also** add the two new test projects/counts if the file enumerates them, and note that
`StingTools.Acc.Tests` now exists.

**Done means** every number you touch is one you personally re-ran in this session.

**Prove it** — paste the output you based each correction on:

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo | tail -2
dotnet test StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo | tail -2
ls -1 StingTools/Data/WORKFLOW_KUT_*.json | wc -l
```

---

## 5. SECTION B — blocked on someone else. Do not attempt.

Unchanged from the first pass, and **B1 is now the only thing standing between the pack and a proven
coordination cycle.** Do not fabricate a credential, mock a service and call it verified, or mark any
of these done. `docs/KUT_LIVE_VERIFICATION_RUNBOOK.md` (from #927) is the procedure; if your work
changes what a failure looks like — A1 and A2 both will — **update that runbook's expected-output
sections** so it still matches reality. That is offline work and it is in scope.

| # | Blocked work | Who must act | Why you cannot proceed |
|---|---|---|---|
| **B1** | One live ACC pull + one live issue push | **Owner / Autodesk account holder** (APS app, Traditional Web App, callback `http://localhost:8910/callback`); **ACC project admin** (container ids) | No APS Client ID or Secret exists. **Overdue** — mobilisation began the week of 25 August 2026 and the fortnightly cycle depends on it. |
| **B2** | One live Niagara station fetch | **Controls / commissioning contractor** — base URL, the station-specific oBIX points path, credentials | No station to reach. `/obix` is the oBIX lobby, not a points feed. Not due until Stage 3.1 (~M40). |
| **B3** | Confirm the Fohlio field mapping is agreed | **Interior Designer** — owns the Fohlio record | `KUT_PROJECT_DELIVERY_PLAYBOOK.md:708` makes it a mobilisation obligation; the shipped map is authored on our side and nothing evidences sign-off. A meeting, not a code task, and on the mobilisation critical path. |

---

## 6. What you must NOT do

- Do not re-implement `AccFetchResult<T>` / `AccFetchOutcome`. Reuse them.
- Do not implement the Fohlio REST transport (out of contract scope — the BEP mitigates it with the
  file route) or BACnet/OPC-UA readback (`TwinReadback.cs`, stubbed by design, not promised).
- Do not add any upload step to a KUT workflow (A5 explains why).
- Do not "fix" a genuinely empty result into an error. An empty station, an empty container and a
  clash-clean model set are all real, expected states. The bug is failures *masquerading* as empty,
  not emptiness itself. Over-correcting here would break the legitimate cases and is worse than the
  original defect, because it would make a clean cycle unreportable.
- Do not refactor the god-files (`StingCommandHandler.cs`, `BIMCoordinationCenter.cs`). Add your
  cases; change nothing else in them.
- Do not edit a generated KUT document. Edit its generator and regenerate.
- Do not change `CLAUDE.md` beyond A7's three corrections.

---

## 7. Finishing

Run the full gate set and paste real output — the actual last lines, not a summary:

```bash
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary --nologo   # 0 errors, 0 warnings
dotnet test  StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo     # baseline 38 passing
dotnet test  StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo     # baseline 1316 passing
python tools/check_kut_workflow_tags.py .                                  # 97/97 across 10 files
python tools/check_kut_documents.py                                        # 100 assertions, 5 docs
python tools/check_docs_index.py                                           # every docs/ file indexed
```

Both test baselines must **rise, not fall** — say by how much and why. `check_kut_documents.py` must
stay green even though you should not have touched a generated document; run it to prove you did not.

Then commit on your branch and open a PR stating, per task:

- **what changed**, with `file:line`;
- **the command that proves it**, and its real output;
- **the sabotage you ran**, and confirmation the gate failed under it;
- **every `REPLACE_WITH_...` / `TODO(KUT):`** you introduced, and why;
- **what you could not do**, and who must act.

Report failures plainly. If a gate will not go green, say so with the output rather than narrowing
the gate until it passes — a gate relaxed to pass is worse than a red one, because it reports health
it has not checked. If you finish only part of Section A, finish what you did completely and say
explicitly which tasks you left and why. **A1 and A2 in full beat all seven half-done**; scaling the
work down is the user's call, not yours.
