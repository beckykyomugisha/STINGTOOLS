# Work prompt — close the KUT integration gaps (Fohlio · Niagara · ACC)

**For an agent executing autonomously with no further input.** You cannot ask questions. Everything
you need is here. Where something is genuinely unknowable, the task says to leave it blank with a
marker and report it — never to guess.

Derived from `docs/KUT_INTEGRATION_READINESS_2026-09.md` (measured 2026-09-10 @ `d318dcbee`). Read
that report first; it carries the `file:line` evidence behind every task below.

---

## 0. Non-negotiable rules

**Worktree.** Several agents share `C:\Dev\STINGTOOLS`. **Do not work in it.** Create your own
worktree and do everything there:

```bash
git worktree add C:/Dev/wt-kut-build -b claude/kut-integration-build origin/main
cd C:/Dev/wt-kut-build
```

Branch from `origin/main`, never from another unmerged branch. If you are already in an isolated
worktree provided by the harness, use that one rather than creating a second.

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

These hold for everything you touch.

- **The tooling is never named in any KUT-issued document** — no product name, command name,
  parameter prefix (`ASS_`, `COM_`, `PRJ_`, …), file path, or `.py` / `.json` extension. The issued
  set is `KUT_BIM_Execution_Plan.docx`, `KUT_Project_Delivery_Playbook.docx`,
  `KUT_Master_Information_Delivery_Plan.xlsx` and the mobilisation/document guides.
  `KUT_BIM_MANAGER_PLAYBOOK_INTERNAL_STINGTOOLS.docx` is the **sole** exception and is never
  shared. Internal `docs/` files (including this one) may name anything.
- **Never hand-edit a generated document.** Edit the generator under `tools/` and regenerate. The
  gate `tools/check_kut_documents.py` (100 assertions, 5 documents) must stay green.
- **Speckle / "Speck-Link" is private.** Never name it as a dependency or a CDE in anything issued.
- **Documents carry no AI attribution.** PR bodies may.
- **Close Microsoft Word** before any git operation touching a `.docx`.
- End commit messages with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`; end PR bodies
  with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

---

## 2. Context you do not have

The Kampala Uganda Temple (KUT) project is live — `docs/INDEX.md` records mobilisation as having
begun the week of **25 August 2026**. Three external integrations are named in the issued KUT
documents and are therefore promises:

| Integration | Role on KUT | Measured state |
|---|---|---|
| **ACC** (Autodesk Construction Cloud) | The CDE; clash and issue exchange | **WIRED BUT UNPROVEN** — four real HTTP clients, full 3-legged OAuth, dispatch complete, never run against a live tenant, **zero tests** |
| **Fohlio** | FF&E / finishes register, round-tripped against Revit rooms | **WORKS** on the contracted CSV tier; the REST tier is a dead stub the BEP already declares out of scope |
| **Niagara** (Tridium) | BMS / digital-twin readback at handover | **WIRED BUT UNPROVEN** — the file-mediated path the playbook promises is built; the live oBIX client has never met a station and ships no config template |

Where the code lives:

```
StingTools/V6/AccIssueSync.cs          ACC Issues push/pull + token refresh   (Revit-free)
StingTools/V6/AccOAuthFlow.cs          3-legged OAuth sign-in                 (Revit-free)
StingTools/V6/AccModelCoordSync.cs     Model Coordination clash read          (Revit-free)
StingTools/V6/AccModelUpload.cs        ACC Docs upload                        (Revit-free)
StingTools/Clash/AccIssuesClient.cs    DEAD — zero callers
StingTools/Clash/AccPullClashesCommand.cs     command shell (Revit-bound)
StingTools/Clash/AccSyncIssueStatusCommand.cs command shell (Revit-bound)
StingTools/ExLink/FohlioLink.cs        Fohlio map + dead REST stub
StingTools/ExLink/FohlioCommands.cs    Fohlio_Export / _Import / _Audit
StingTools/ExLink/FohlioFinishesCommands.cs   Fohlio_ExportFinishes / _ImportFinishes
StingTools/Core/Twin/NiagaraJsonClient.cs     live oBIX client (Revit-free)
StingTools/Core/Twin/NiagaraPointParser.cs    pure parser, log-free, already unit-tested
StingTools/Core/Twin/CommissioningSource.cs   live→cached→none resolver
StingTools/Commands/Twin/NiagaraCommands.cs   Niagara_ExportPoints / _Reconcile
```

**Dispatch is three layers and all three matter.** A command needs all of:
1. a `case "Tag":` in `StingTools/UI/StingCommandHandler.cs` (so a button works),
2. a `case "Tag":` in `WorkflowEngine.ResolveCommand` — `StingTools/Core/WorkflowEngine.cs`,
   lines 1395–2244, exactly one `switch` (so a workflow step works),
3. a `<Button ... Tag="Tag" Click="Cmd_Click"/>` in `StingTools/UI/StingDockPanel.xaml`
   (so the button exists at all).

Some commands are additionally registered in `StingTools/UI/Modules/*.cs`. One-layer audits have
twice produced large false-positive figures on this repo — check all three.

**Testing pattern.** Revit-free plugin sources are pulled into a test project with
`<Compile Include="..\StingTools\...\X.cs" Link="X.cs" />`. See
`StingTools.Boq.Tests/StingTools.Boq.Tests.csproj:22-29`. **Do not try to link
`StingTools/Core/StingLog.cs`** — it drags in `StingTools.Core.Drawing`. Follow the
`NiagaraPointParser` precedent instead: put the pure decision in a log-free file, link that, and
leave the logging in the caller. `.github/workflows/stingtools-unit-tests.yml` globs
`StingTools.*.Tests/*.csproj`, so a new test project is picked up with no workflow edit.

---

## 3. How to work

**Build the gate before the feature, and break your own gate once.**

Every gate written on this project so far was wrong on its first run, and only deliberate sabotage
revealed it. So for each task that names a gate:

1. Write the gate (test / checker) **first**.
2. Run it. If it passes before you have written the fix, **the gate is wrong** — a gate that passes
   against the unfixed code proves nothing. Fix the gate.
3. Write the fix. Run the gate; it must now pass.
4. **Sabotage deliberately:** re-break the thing the gate is supposed to catch (revert one line,
   rename one key), run the gate, and confirm it **fails**. Restore.
5. Record in your final report, per gate: the command, the pass result, and what sabotage you used
   and that it failed.

The two gate faults that survive casual reading are **circular expectations** (the gate asserts
what the code does rather than what is required) and **alternative-path escapes** (the gate checks
one route while the code has two). Check for both explicitly.

**An absent side effect never tells you why.** A silent no-op looks identical to a wrong fix, a bad
credential and a call that never ran. If something reportedly works, get the observable proof; if it
doesn't, get the error rather than inferring one.

**Assert on non-empty data.** A test that passes against an empty result set proves nothing. Show
the count.

---

## 4. SECTION A — work you can complete offline

No credential, no third party, no network. Do these in order; the ordering is by what unblocks KUT
soonest, not by interest or effort.

**A1–A5 are the five fixes the readiness report flags as needed before the first coordination
cycle.** A1 is the only load-bearing one — the rest are small. **A6–A8 are additional offline work**
that does not block the cycle: removing a latent false green, making the workflow check reusable, and
authoring the runbook for the blocked items in Section B. If you run short of time, A1–A5 in full
beats all eight half-done.

---

### A1 — Make an empty ACC clash result distinguishable from a failed one

**Why first.** This is the one defect that can make a coordination gate *pass without having
checked*. `AccModelCoordSync.cs:40-42` says a wrong sub-path "fails soft (logs the HTTP status,
returns empty)". `AccPullClashesCommand.cs:87-93` then reports zero clashes as *"Either the model
set is clash-clean, or a clash test has not completed in ACC yet"* and returns
**`Result.Succeeded`**. On a wrong container id or a changed APS sub-path, the fortnightly KUT
Coordination Cycle reports a clean federation. KUT runs that cycle every two weeks.

**Files to touch**
- NEW `StingTools/V6/AccFetchOutcome.cs` — Revit-free and **log-free** (so it links into tests).
  An outcome discriminator plus a result carrier. At minimum it must distinguish:
  `Ok` (request succeeded, payload parsed) · `EmptyOk` (succeeded, genuinely nothing) ·
  `AuthFailed` · `NotFound` · `TransportFailed` (HTTP error, network error, or unparseable body).
- `StingTools/V6/AccModelCoordSync.cs` — `ListModelSetsAsync` and `GetClashesAsync` return the
  outcome alongside the data instead of a bare empty list. Keep the existing logging where it is.
- `StingTools/Clash/AccPullClashesCommand.cs` — branch on the outcome:
  - `EmptyOk` → keep today's "clash-clean or test not completed" message, `Result.Succeeded`.
  - Anything else → say which failure it was, name the container id that was used, and return
    **`Result.Failed`**. It must be impossible for a non-`Ok` outcome to read as a clean cycle.
- NEW `StingTools.Acc.Tests/` (project + `.csproj`), linking the Revit-free ACC sources.

**Done means**
- No code path reports "no clashes" for a request that did not succeed.
- A transport failure returns `Result.Failed`, so a workflow step fails instead of passing.
- The new test project compiles and contains real tests (an empty test project is a false coverage
  signal — do not create one without tests).

**Prove it**

```bash
dotnet test StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo
```

Must report a non-zero passing count with 0 failing. Required cases, at minimum:
- a pure mapping test: HTTP 401 → `AuthFailed`; 404 → `NotFound`; 500 → `TransportFailed`;
  200 with `{"modelSets":[]}` → `EmptyOk`; 200 with one set → `Ok`.
- **at least one loopback test** against a local `System.Net.HttpListener` you start in the test,
  proving end-to-end that a 404 does **not** produce `EmptyOk`. A pure-function test alone is an
  alternative-path escape — it would not catch the real client mapping a 404 to empty.

**Sabotage to run and report:** make the 404 branch return `EmptyOk`, confirm the suite fails,
restore. Then confirm the suite fails a second way: make `GetClashesAsync` swallow a thrown
`HttpRequestException` and return `EmptyOk`.

Also run, and paste the last 5 lines:

```bash
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary --nologo
```

Must stay **0 errors, 0 warnings**.

---

### A2 — Fix the 429 retry in `AccIssueSync.PushIssueAsync`

**Why.** The retry cannot execute. The `HttpRequestMessage` is built once at
`AccIssueSync.cs:215`, outside the loop, and re-sent at `:222`; on .NET 8 the second send throws
`InvalidOperationException: The request message was already sent.` The call site catches
(`AccPullClashesCommand.cs:178`), so the issue is **dropped** while the log says
`ACC 429 — retrying in 1s`. Bulk-escalating clashes to ACC Issues is exactly the workload that
provokes 429s. `PullIssuesAsync` already builds its request inside its loop (`:255`) — copy that.

**Files to touch**
- `StingTools/V6/AccIssueSync.cs` — move the `HttpRequestMessage` (and its `StringContent`)
  construction inside the `for (int attempt ...)` loop. Dispose each attempt's request.
- `StingTools.Acc.Tests/` — tests.

**Done means** four real attempts happen against a persistent 429, and a 429-then-success sequence
returns the created issue id.

**Prove it** — loopback tests that count requests server-side:

```bash
dotnet test StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo --filter "FullyQualifiedName~Retry"
```

Required cases:
- listener always 429 → the client makes **4** requests (assert the server-side count is 4, not
  that "it didn't throw" — counting is what proves the retry ran) and no
  `InvalidOperationException` escapes.
- listener 429, 429, then 201 with `{"id":"abc"}` → returns `"abc"`.

Keep the back-off delays short in tests (inject the delay or use a test-only override) so the suite
does not sleep 15 seconds. **Do not** change production back-off timings to suit the test.

**Sabotage to run and report:** move the request construction back outside the loop, confirm both
cases fail, restore.

---

### A3 — Stop `ACCPublish` claiming it published to ACC, and make the real uploader dispatchable

**Why.** `ACCPublish` builds a local ZIP and tells the user to upload it by hand
(`PlatformLinkCommands.cs:1230`). The UI is honest; the KUT workflow is not —
`StingTools/Data/WORKFLOW_KUT_CoordinationCycle.json` step 6 reads *"Publish coordination data to
ACC"*. An operator running the fortnightly cycle will believe the drop reached the CDE. Meanwhile
the real uploader, `AccModelUpload`, is reachable only from one BCC button and from no workflow.

**Two parts. The first is mandatory; the second is the build.**

**A3a — relabel (mandatory, trivial).** In `WORKFLOW_KUT_CoordinationCycle.json`, change step 6's
`label` so it states what happens: that it packages the deliverables into a local ACC-ready bundle
for upload, not that it publishes. Do not change the `commandTag`. Keep the wording plain — a KUT
operator reads this string.

**A3b — expose the real uploader as a command.** Add an `ACC_UploadModel` tag that runs
`AccModelUpload.UploadAsync` with a file picker, mirroring the existing BCC button
(`BIMCoordinationCenter.cs:5581`). Wire **all three** layers:
- `case "ACC_UploadModel":` in `StingTools/UI/StingCommandHandler.cs`
- `case "ACC_UploadModel":` in `WorkflowEngine.ResolveCommand`
- a `<Button ... Tag="ACC_UploadModel" Click="Cmd_Click"/>` in `StingTools/UI/StingDockPanel.xaml`,
  beside the existing ACC Pull / ACC Sync buttons (around `:3134`)

**Do not add the upload to any KUT workflow.** A workflow step cannot answer "upload which file?",
and inventing an answer (guessing the model path, or silently uploading the active document) is
exactly the guessing this prompt forbids. Leave that decision to a human and say so in your report.

**Done means** the step-6 label matches behaviour; `ACC_UploadModel` is reachable from a button and
resolvable by a workflow; no workflow step was invented.

**Prove it**

```bash
# all three layers carry the new tag (expect >= 1 hit per layer)
grep -n '"ACC_UploadModel"' StingTools/UI/StingCommandHandler.cs
grep -n '"ACC_UploadModel"' StingTools/Core/WorkflowEngine.cs
grep -n 'Tag="ACC_UploadModel"' StingTools/UI/StingDockPanel.xaml

# no KUT workflow still claims to publish to ACC
grep -rn -i "publish.*coordination data to ACC" StingTools/Data/WORKFLOW_KUT_*.json
```

The last command must return **nothing**. Before trusting that silence, prove the pattern can match:
run it against the pre-edit file (`git stash`-free: `git show HEAD:StingTools/Data/WORKFLOW_KUT_CoordinationCycle.json | grep -i "publish.*coordination data to ACC"`)
and confirm it **does** match there. A grep you have not seen succeed is not evidence.

Then re-run the workflow resolve check from A7 and confirm it is still 97/97 (or higher if you added
steps — you should not have).

---

### A4 — Resolve `StingTools/Clash/AccIssuesClient.cs`

**Why.** 45 lines, a real `SendAsync` at `:37`, **zero callers**, and an endpoint
(`bim360/docs/v1/projects/{id}/issues/bulk`) that disagrees with the live client's
`construction/issues/v1`. It is a second, orphaned, differently-shaped ACC path that will mislead
the next reader into thinking BCF bulk upload is available.

**Do: delete the file.** The live path is `AccIssueSync`. If you believe it should be wired instead,
you would need a BCF-bulk endpoint confirmed against live ACC — which you cannot do offline — so
deletion is the only honest offline action.

**Done means** the type no longer exists and the build is still 0/0.

**Prove it**

```bash
# verify the instrument FIRST on a type that IS used — must return 20+ hits
grep -rn "AccIssueSync" --include="*.cs" . | grep -v /obj/ | grep -v /bin/ | wc -l
# then the deleted type — must return 0
grep -rn "AccIssuesClient\|PostBcfAsync" --include="*.cs" . | grep -v /obj/ | grep -v /bin/
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary --nologo
```

---

### A5 — Ship a Niagara connection example, gated against the reader

**Why.** `NiagaraJsonClient.FetchPoints` is a real transport, but **no `niagara_connection.json`
or `.example` exists anywhere in the repo** — not in `project-templates/KUT/_BIM_COORD/`, not in
`docs/examples/KUT/`. The required shape lives only in a code comment
(`NiagaraJsonClient.cs:11-13`). Fohlio ships an example; Niagara does not. Whoever enables this at
Stage 3.1 (~M40) must read the source to learn the field names.

**Files to touch**
- NEW `docs/examples/KUT/niagara_connection.json.example`
- `docs/examples/KUT/README.md` — add a row for it
- a test gating the example against the reader (see below)

**The keys `NiagaraConnection.Load` reads** (`NiagaraJsonClient.cs:49-53`) — use exactly these:
`baseUrl`, `pointsPath`, `apiKey`, `username`, `password`.

**Do not invent the station URL, the oBIX path, or any credential.** Use
`REPLACE_WITH_STATION_BASE_URL`, `REPLACE_WITH_API_KEY` etc. Note in the `_comment` that
`pointsPath` defaults to `/obix` and is station-specific, that it must be confirmed with the
controls contractor, and that the real file is gitignored and never committed. Mirror the tone of
`docs/examples/KUT/fohlio_connection.json.example`.

**Done means** the example's keys are exactly the keys the loader reads, and a gate enforces that so
the two cannot drift.

**Prove it** — add the test to `StingTools.Boq.Tests` (which already links
`NiagaraPointParser.cs` and already gates the shipped KUT Fohlio map the same way; copy the
`<None Include ... Link="Data\...">` pattern at `StingTools.Boq.Tests.csproj:184-186` so the test
reads the **real** example file rather than a copy that can drift):

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~NiagaraConnectionExample"
```

The test must assert every key the example declares is one the loader reads, **and** that every key
the loader reads appears in the example. Both directions — a one-directional check is an
alternative-path escape that lets the example go stale.

**Sabotage to run and report:** rename `pointsPath` to `pointPath` in the example, confirm the test
fails, restore. Then delete `password` from the example and confirm it fails the other direction.

---

### A6 — Remove the Fohlio false green

**Why.** `FohlioRestTransport.TestConnection()` (`FohlioLink.cs:190-195`) returns `true` whenever
`BaseUrl` and `ApiKey` are merely non-empty — **no network call at all**. Its siblings
`ListItems` / `GetItem` / `UpdateItem` honestly throw `NotImplementedException` (`:198-204`). It is
harmless today only because nothing calls it; the first person to wire a "Test connection" button
inherits a test that passes against a typo.

**Do:** make `TestConnection()` throw `NotImplementedException` with the same shape as its siblings,
naming the CSV path as the working route. This is the smallest change that removes the false green
while preserving the documented future interface. **Do not** implement the REST transport — the BEP
(`GUIDES/KUT_BEP_TEMPLATE.md:326`) declares the file route as the mitigation and
`project-templates/KUT/README.md:148-150` says REST stays stubbed for this contract, so building it
is out of scope and needs an API key you do not have.

Also correct the two comments that describe a "Test connection" gate which does not exist
(`FohlioLink.cs:19-21` and `:183`) — say the REST tier is unimplemented and the CSV path is the
contracted route.

**Done means** no method in the file returns a success value without doing the work it claims.

**Prove it**

```bash
# no bare 'return true' remains in the REST stub region
sed -n '185,210p' StingTools/ExLink/FohlioLink.cs
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary --nologo
# the five CSV commands are untouched and still dispatch on all three layers
grep -c 'Tag="Fohlio_' StingTools/UI/StingDockPanel.xaml    # expect 5
grep -c 'case "Fohlio_' StingTools/UI/StingCommandHandler.cs # expect 5
grep -c 'case "Fohlio_' StingTools/Core/WorkflowEngine.cs    # expect 5
```

Then confirm the Fohlio suites still pass — you must not regress the working tier:

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~Fohlio"
```

---

### A7 — Re-prove every KUT workflow still resolves

**Why.** A3 edits a workflow file and adds dispatch entries. A step whose `commandTag` does not
appear in `ResolveCommand` parses fine and silently does nothing.

**Done means** all 10 `WORKFLOW_KUT_*.json` files resolve 100% of their steps, and every step still
uses the `commandTag` + `label` field names (not `command` / `name`, which parse and do nothing).

**Prove it.** Write the checker as `tools/check_kut_workflow_tags.py` so it is reusable and
CI-gateable, not a throwaway. It must:
- extract `case "X":` labels from `WorkflowEngine.cs` **lines 1395–2244 only**, and assert that
  window contains **exactly one** `switch` (if a future edit moves the method, the checker must fail
  loudly rather than silently widen its view and report false passes),
- self-test before reporting: a known-good tag (`Fohlio_Export`) must be found and a nonsense tag
  (`Fohlio_ExportZZZ_NOT_A_REAL_TAG`) must not; abort if either check misbehaves,
- report per-file resolve rate and exit non-zero on any unresolved tag or wrong field name.

```bash
python tools/check_kut_workflow_tags.py .
```

Expected baseline: **97/97 steps across 10 files (100%)**. If your A3 work changed the count, state
the new count and why.

**Sabotage to run and report:** add a step with `commandTag: "NotARealTag"` to one KUT workflow,
confirm the checker exits non-zero, remove it. Then change a step's `commandTag` key to `command`
and confirm the checker catches that too.

---

### A8 — Write the live-verification runbook (offline authoring of blocked work)

**Why.** Section B cannot be executed without third parties, but it can be made ready. The KUT
README already prescribes *"do one live pull during mobilisation"*
(`project-templates/KUT/README.md:142-143`) without saying how.

**Files to touch** — NEW `docs/KUT_LIVE_VERIFICATION_RUNBOOK.md`, plus a row in `docs/INDEX.md`
(under "Audits, proposals and role model", which is where dated reports live).

**Content.** For each of B1/B2/B3 below: who must supply what, the exact steps once it arrives, the
observable proof that it worked, and what to do when it does not. Specifically for ACC:
- the APS app type required (**Traditional Web App** — confidential, uses a Client Secret, matching
  the Basic-auth token exchange the code already does),
- the exact callback URL to register: `http://localhost:8910/callback`
  (`AccOAuthFlow.cs:42`, `:50`) — the sign-in fails with a clear message if it is missing,
- the four fields and where they go (`ClientId`, `ClientSecret`, `ProjectId` = the `b.<guid>` Issues
  container, `CoordContainerId` if Model Coordination differs),
- that `IssueTypeId` does **not** need hand-entry — `EnsureIssueTypeAsync` resolves and caches it,
- the single highest-value check: one `ACC_PullClashes` against a model set known to contain
  clashes, to confirm the `bim360/clash/v3` **tests** and **resources** sub-paths, which
  `AccModelCoordSync.cs:40-42` flags as the only real residual,
- that after A1 a wrong path now returns `Result.Failed` with a named reason instead of looking
  clean — so the runbook should say what that message looks like.

**Do not invent** a Client ID, container id, station URL, or any credential. Leave
`REPLACE_WITH_...` markers.

**Done means** a competent BIM manager could execute it without reading any source code.

**Prove it**

```bash
python tools/check_docs_index.py
```

This is the gate `.github/workflows/docs-index.yml` runs; it must pass. Also confirm the new doc is
listed:

```bash
grep -n "KUT_LIVE_VERIFICATION_RUNBOOK" docs/INDEX.md
```

---

## 5. SECTION B — blocked on someone else. Do not attempt.

These need a person or a credential you do not have. **Do not** fabricate a credential, mock a
service and report it as verified, or mark any of these done. Your only task here is A8 — write the
runbook so they are ready.

| # | Blocked work | Who must act first | Why you cannot proceed |
|---|---|---|---|
| **B1** | One live ACC pull to confirm the `bim360/clash/v3` tests/resources sub-paths, and one live issue push | **Owner / Autodesk account holder** — must create an APS app and register the callback URL; **ACC project admin** — must supply the container ids | No APS Client ID or Secret exists. Without them `EnsureAuthAsync` cannot get a token, and the only genuine unknown in the ACC path stays unknown. This is **overdue**, not upcoming — mobilisation began the week of 25 August 2026. |
| **B2** | One live Niagara station fetch | **Controls / commissioning contractor** — station base URL, the oBIX points path, and credentials | No station exists to reach, and `pointsPath` defaults to a station-specific guess (`/obix`). Not due until Stage 3.1 (~M40). |
| **B3** | Confirm the Fohlio field mapping is agreed | **Interior Designer** — owns the Fohlio record | `KUT_PROJECT_DELIVERY_PLAYBOOK.md:708` makes "field mapping agreed with Fohlio" a mobilisation obligation. The shipped map is STING-authored; nothing in the repo evidences sign-off. This is a meeting, not a code task, and it is on the mobilisation critical path. |

If you find yourself tempted to "verify" any of these with a mock, stop: a fabricated verification
is worse than an open item, because it closes the item in everyone's mind.

---

## 6. What you must NOT do

- Do not implement the Fohlio REST transport (out of contract scope; needs an API key).
- Do not implement BACnet or OPC-UA readback (`TwinReadback.cs`). Stubbed by design, an FM add-on,
  not promised by any KUT document.
- Do not add an ACC upload step to any KUT workflow (A3b explains why).
- Do not refactor the god-files (`StingCommandHandler.cs`, `BIMCoordinationCenter.cs`). Add your
  cases; change nothing else in them. They are the most contended files in the tree.
- Do not edit a generated KUT document. Edit its generator and regenerate.
- Do not change `CLAUDE.md` headline counts as a side-effect; if you notice drift, note it in your
  report.
- Do not delete or rename any shared parameter or GUID.

---

## 7. Finishing

Run the full gate set and paste real output for each — not a summary, the actual last lines:

```bash
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary --nologo   # 0 errors, 0 warnings
dotnet test  StingTools.Acc.Tests/StingTools.Acc.Tests.csproj --nologo
dotnet test  StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo     # baseline 1311 passing
python tools/check_kut_workflow_tags.py .                                  # 97/97 across 10 files
python tools/check_kut_documents.py                                        # 100 assertions, 5 docs
```

`check_kut_documents.py` must stay green even though you should not have touched a generated
document — run it to prove you did not.

Then commit on your branch and open a PR. In the PR body, state for each task:

- **what changed**, with `file:line`;
- **the command that proves it**, and its real output;
- **the sabotage you ran** and confirmation that the gate failed under it;
- **anything you left blank with a marker**, and why — list every `REPLACE_WITH_...` and
  `TODO(KUT):` you introduced;
- **anything you could not do**, and who must act.

Report failures plainly. If a gate will not go green, say so with the output rather than narrowing
the gate until it passes — a gate relaxed to pass is worse than a red one, because it reports
health it has not checked. If you finish only part of Section A, finish the tasks you did complete
properly and say explicitly which you left and why; scaling the work down is the user's call, not
yours.
