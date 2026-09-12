# KUT Integration Readiness — Fohlio · Niagara · ACC

**Measured 2026-09-10** on `claude/kut-integration-readiness-497572`, base `main` @ `d318dcbee` (0 commits behind).
Build verified on this machine: `dotnet build StingTools/StingTools.csproj -c Debug` → **0 errors, 0 warnings** (1m16s).

This is a research report. **No behaviour was changed.** Every claim carries a `file:line`.
Where this report and `CLAUDE.md` / `docs/` disagree, the code wins and the disagreement is named.

> Naming note: this is an internal `docs/` file, not a KUT-issued document, so product and
> parameter names appear freely here. They must not appear in anything issued to the Owner.

---

## 1. Verdicts

| Integration | Verdict | One-line basis |
|---|---|---|
| **ACC** (Autodesk Construction Cloud) | **WIRED BUT UNPROVEN** | Four real HTTP clients, complete 3-legged OAuth, dispatch complete on all three layers — and by the code's own admission never run against a live Autodesk tenant. Zero tests. |
| **Fohlio** | **WORKS** (CSV tier — the contracted route) | Five commands, real file I/O both ways, parameters defined + universally bound, 72 passing tests, and a data gate that validates the *real* shipped KUT map. The REST tier is a SHELL, and is explicitly out of contract scope. |
| **Niagara** (Tridium) | **WIRED BUT UNPROVEN** | The file-mediated path the playbook actually promises is built, dispatched and honest about empty results; the live oBIX client is a real transport that has never met a station and **ships no connection template anywhere**. BACnet/OPC-UA readback is a SHELL. |

Headline: **nothing here is ABSENT, and nothing fabricates data.** The gap is proof, not construction.

---

## 2. ACC — WIRED BUT UNPROVEN

### 2.1 Transport: real, in four places

| Client | Lines | Real send site | Endpoint base |
|---|---|---|---|
| `V6/AccIssueSync.cs` | 326 | `_http.SendAsync` at **100, 146, 222, 258** | `authentication/v2/token`, `construction/issues/v1` |
| `V6/AccOAuthFlow.cs` | 219 | `_http.SendAsync` at **135** | `authentication/v2/authorize` + token exchange |
| `V6/AccModelCoordSync.cs` | 289 | `_http.SendAsync` at **226, 245** | `bim360/modelset/v3`, `bim360/clash/v3` |
| `V6/AccModelUpload.cs` | 332 | `_http.SendAsync` at **207, 226, 324** | `data/v1`, `project/v1`, `oss/v2` (signed-S3) |

These are the documented APS paths, not invented ones. `AccIssueSync.cs:77` uses
`construction/issues/v1` — the current ACC Issues API, not the retired BIM360 shape.

### 2.2 Authentication: complete, and correctly absent from the assembly

`AccOAuthFlow.SignInAsync` is a full 3-legged flow — authorize URL (`:76`), loopback
`TcpListener` on `http://localhost:8910/callback` (`:50`, `:85`), CSRF `state` compared on
return (`:122`), `authorization_code` exchange (`:127`), tokens persisted (`:149`).
`AccIssueSync.EnsureAuthAsync` then refreshes silently (`:93`).

Credentials live at `%APPDATA%\Planscape\acc_credentials.json` (`AccIssueSync.cs:297-299`).
**No Autodesk secret is baked into the assembly** — correct, and it means the KUT team
must supply their own APS app. What they must produce:

| Needed | Where it goes | Who can supply it |
|---|---|---|
| APS Client ID + Secret ("Traditional Web App") | BCC ACC card → Save Credentials | Owner / Autodesk account holder |
| `http://localhost:8910/callback` registered in the APS app | Autodesk APS console | same |
| Issues container id (`b.<guid>`) | `ProjectId` | ACC project admin |
| Model Coordination container id (if different) | `CoordContainerId` | ACC project admin |

`IssueTypeId` does **not** need hand-entry — `EnsureIssueTypeAsync` (`:136-187`) resolves it
from the container, prefers a Clash/Coordination type, and caches it.

### 2.3 Reachability: complete across all three layers

| Tag | XAML button | Handler case | `ResolveCommand` |
|---|---|---|---|
| `ACCPublish` | `StingDockPanel.xaml:5373` (combo item) | `:2863` | `:1937` |
| `ACC_PullClashes` / `AccPullClashes` | `StingDockPanel.xaml:3134` | `:2873-2874` (both spellings) | `:1989` |
| `ACC_SyncIssueStatus` / `AccSyncIssueStatus` | `StingDockPanel.xaml:3136` | `:2875-2876` (both spellings) | `:1991` |
| `KUT_PushLifecycleGapsToAcc` | `StingDockPanel.xaml:3797` | `:3849` | `:1608` |

Plus a registry entry for `ACCPublish` at `UI/Modules/BimCommandModule.cs:160`.

The buttons use the `ACC_`-prefixed spelling while the BIM Coordination Center card uses the
`Acc` spelling; **both are deliberately accepted** (`StingCommandHandler.cs:2866-2872` explains
why). I checked this specifically because an alias mismatch here would have left the visible
buttons dead while workflows worked. It is sound.

The richest surface is the BCC → Platforms → **ACC** card (`BIMCoordinationCenter.cs:5428`
`BuildAccDetail`), which is special-cased away from the generic placeholder panel and offers:
Sign in with Autodesk (button `:5506`, call `:5516`), Save Credentials (`:5524`/`:5527`),
Test/Refresh (`:5532`, calling `EnsureAuthAsync` at `:5542`),
Pull Clashes / Sync Issue Status / ACC Publish (`:5560-5562`), and a live
**Upload Model to ACC** (`:5581` → `AccModelUpload.UploadAsync`).

### 2.4 What is wrong

**(a) `ACCPublish` does not publish to ACC.** It builds a local ZIP and says so:
`PlatformLinkCommands.cs:1230` — *"Upload the ZIP file to ACC/BIM 360 document management."*
The UI is honest (the card tooltip at `BIMCoordinationCenter.cs:5562` says *"local ACC-ready
bundle (manual upload)"*). **The KUT workflow is not**:
`WORKFLOW_KUT_CoordinationCycle.json` step 6 reads *"Publish coordination data to ACC"*.
An operator following the fortnightly cycle will believe publication happened. Meanwhile the
*real* uploader (`AccModelUpload`) is reachable only from the BCC card and is not in any workflow.

**(b) The 429 retry in `PushIssueAsync` cannot execute — verified empirically, not inferred.**
The `HttpRequestMessage` is built once at `AccIssueSync.cs:215`, outside the loop, and re-sent
at `:222`. I reproduced the exact shape against a local always-429 listener on .NET **8.0.23**,
the runtime the plugin targets:

```
attempt 0: HTTP 429
attempt 1: THREW InvalidOperationException: The request message was already sent.
           Cannot send the same request message multiple times.
```

Consequence is bounded, because the call site catches (`AccPullClashesCommand.cs:178`): the
issue is **dropped**, not retried, and the log reads `ACC 429 — retrying in 1s` immediately
followed by the exception — a log that claims a retry that never happened. Bulk-escalating
clashes to ACC Issues is precisely the workload that provokes 429s, and it is the one path
that cannot survive one. `PullIssuesAsync` builds its request *inside* the loop (`:255`) and is
correct — the asymmetry shows this is an oversight, not a decision.

**(c) A wrong endpoint path is indistinguishable from a clean model.**
`AccModelCoordSync.cs:40-42` states the residual honestly: *"the exact clash-service sub-paths
(tests / resources) live inside the APS SDK; confirm with one live pull… A wrong path fails
soft (logs the HTTP status, returns empty) — never throws."*
`AccPullClashesCommand.cs:87-93` then reports zero clashes as *"Either the model set is
clash-clean, or a clash test has not completed in ACC yet"* and returns **`Result.Succeeded`**.
It names two causes and not the third. So on a wrong container id or a changed sub-path, the
fortnightly Coordination Cycle **passes, reporting no clashes**. This is the house failure mode
CLAUDE.md describes — an empty list standing in for an error — surviving in the one place it
costs a coordination cycle.

**(d) `Clash/AccIssuesClient.cs` is dead code.** 45 lines, a real `SendAsync` at `:37`, and
**zero callers** — `grep` for the type and for `PostBcfAsync` finds only its own definition.
I verified the instrument on `AccIssueSync`, which returns 20+ call sites, so the absence is
real. Worse, its endpoint (`bim360/docs/v1/projects/{id}/issues/bulk`, `:28`) does not match the
live client's `construction/issues/v1`. It is a second, orphaned, differently-shaped ACC path
that will mislead the next reader. Delete it or wire it.

**(e) Zero test coverage.** No test in any of the 13 `StingTools.*.Tests` projects or
`Planscape.Server/tests` references `AccIssueSync`, `AccOAuthFlow`, `AccModelUpload`,
`AccModelCoordSync`, `AccIssuesClient` or `AccPullClashes`.

**(f) `AccPullClashes` needs an operator.** It opens a model-set picker
(`AccPullClashesCommand.cs:76`), so the Coordination Cycle cannot run unattended. Not a
defect — an operational fact worth knowing before anyone schedules it.

### 2.5 What is right

No-credential handling is exemplary and worth preserving as the pattern:
`AccPullClashesCommand.cs:48-58` names the file, the four required fields and the container
distinction, then returns `Result.Cancelled`. Request failures surface the actual error
(`:64-65`, `:85`). Nothing is fabricated anywhere in the ACC path.

---

## 3. Fohlio — WORKS (CSV tier)

### 3.1 The contracted route is the CSV route

This is not an interpretation — both KUT documents say it:

- `GUIDES/KUT_BEP_TEMPLATE.md:326` risk row: *"Fohlio API delay | Medium | Low | **Use
  file/Add-in route now (Option A)** | Info Mgr"*
- `project-templates/KUT/README.md:148-150`: *"Stay on the shipped CSV/XLSX link… **The REST
  tier stays stubbed — no API key needed for this contract**."*

So the missing REST transport is a **disclosed, mitigated risk**, not an undeclared exposure.
That materially changes the contractual reading and it would have been missed by reading code alone.

### 3.2 Transport and reachability

Five commands, all dispatched on all three layers:

| Tag | XAML button | Handler | `ResolveCommand` |
|---|---|---|---|
| `Fohlio_Export` | `StingDockPanel.xaml:3809` | `:3840` | `:1599` |
| `Fohlio_Import` | `:3811` | `:3841` | `:1600` |
| `Fohlio_Audit` | `:3813` | `:3842` | `:1601` |
| `Fohlio_ExportFinishes` | `:3815` | `:3843` | `:1602` |
| `Fohlio_ImportFinishes` | `:3817` | `:3844` | `:1603` |

Real file I/O, no network needed. `Fohlio_Export` writes CSV
(`FohlioCommands.cs:64`); `Fohlio_Import` previews a diff then writes back
(`:87-259`); `Fohlio_ExportFinishes` emits one row per placed room keyed by **room number**
(`FohlioFinishesCommands.cs:75`).

### 3.3 The write-back target is real — checked, because a named parameter is the classic silent no-op

| Check | Result |
|---|---|
| `FOHLIO_REF_TXT` defined | `Data/MR_PARAMETERS.txt:1774`, GUID `0ecf2056-1239-52bc-87f8-17c281e67209` |
| GUID agrees with code | `Core/ParamRegistry.cs:217-218` — identical |
| In the JSON registry | `Data/PARAMETER_REGISTRY.json:14910` |
| Actually **bound** | Yes — `UniversalParams` (`ParamRegistry.cs:2702`), so every category |
| `FOHLIO_UNIT_COST_NR` / `FOHLIO_CURRENCY_TXT` | Authored; previously *undefined* and silently unreadable — closed as ROADMAP `LIFE-5` |

Absence from `CATEGORY_BINDINGS.csv` is **not** evidence of unbinding — that file is the legacy
fallback. `RESOLVED_BINDINGS.csv` is the source of truth. (ROADMAP `LIFE-5` records an earlier
audit that got this exact point wrong.)

### 3.4 Two suspicions I raised and then disproved

Recording these because a report that only lists confirmed faults hides how much was checked.

- **`$FohlioQty` / `$FohlioLeadDays`** appear in the KUT map but not in
  `FohlioMap.ResolveValue`'s `switch` (`FohlioLink.cs:120-135`), which looked like a data/code
  mismatch that would silently export blanks. They are **handled** — on the import side, by
  header lookup at `FohlioCommands.cs:137-138`. No defect.
- **`ffe-fohlio-ref`** is declared in `project-templates/KUT/_BIM_COORD/owner_standards.json:40`
  and no `.cs` file mentions that id, which looked runtime-dead. It is dispatched **by type**,
  and `"paramRequired"` is implemented at `Core/Validation/OwnerStandardsPack.cs:151`. Live, at
  severity WARN as documented. No defect.

### 3.5 Test coverage — and a gate that is genuinely not circular

Run on this machine, `StingTools.Boq.Tests` (1311 passing overall, 0 failing):

| Suite | Cases |
|---|---|
| `NiagaraPointParserTests` | 43 pass |
| `FfeTreatmentTests` | 15 pass |
| `FohlioParameterBindingTests` | 10 pass |
| `ShippedFohlioMapTests` | 4 pass |

`ShippedFohlioMapTests` validates the **real** KUT file — `StingTools.Boq.Tests.csproj:184-186`
links `..\project-templates\KUT\_BIM_COORD\fohlio_map.json` into the test output rather than
keeping a copy, so the data and the gate cannot drift apart. That is the right shape.

### 3.6 What is wrong

**(a) `FohlioRestTransport.TestConnection()` is a false green.** `FohlioLink.cs:190-195`
returns `true` whenever `BaseUrl` and `ApiKey` are merely non-empty — **no network call at
all**. `ListItems` / `GetItem` / `UpdateItem` throw `NotImplementedException` (`:198-204`).
Mitigating fact, confirmed by grep: **`FohlioRestTransport` and `FohlioConnection` have zero
callers**, so the false green cannot currently reach a user. It is a loaded gun on a shelf —
the first person to wire the button inherits a connection test that passes against a typo.

**(b) The "Test connection gate" described in the code does not exist.** `FohlioLink.cs:19-21`
and `:183` describe a gate behind which REST sits; there is no such UI anywhere.

**(c) `docs/examples/KUT/fohlio_connection.json.example` configures a path that cannot run.**
It is honestly labelled *"the **future** REST transport"*, so this is low-severity, but it is a
KUT-facing example file for an unimplemented transport.

**(d) Unverifiable from the repo:** the playbook's mobilisation obligation is that the field
mapping is *"agreed with Fohlio"* (`KUT_PROJECT_DELIVERY_PLAYBOOK.md:708`). The shipped map is
STING-authored; nothing in the repo evidences Interior-Designer sign-off. That is a meeting,
not a code task, and it is on the mobilisation critical path.

---

## 4. Niagara (Tridium) — WIRED BUT UNPROVEN

### 4.1 CLAUDE.md understates this one

`CLAUDE.md` says only that *"`TwinReadback` BACnet / OPC-UA transports are abstract stubs."*
**True** — `Core/Twin/TwinReadback.cs:39` and `:46` both `return Array.Empty<TwinSnapshot>()`.
But it omits that a **separate, real** Niagara client exists, so the doc reads as "Niagara is
absent" when two-thirds of it is built. Corrected here.

### 4.2 Three paths, three different maturities

| Path | Files | State |
|---|---|---|
| **File-mediated** (what the playbook promises) | `Commands/Twin/NiagaraCommands.cs` (228) | Real. Exports a point list, reconciles a station export. |
| **Live oBIX/JSON over HTTP** | `Core/Twin/NiagaraJsonClient.cs` (5.9 KB) | Real transport — `Http.SendAsync` at **:98**, Bearer *or* Basic auth (`:91-97`). Never run against a station. |
| **BACnet / OPC-UA readback** | `Core/Twin/TwinReadback.cs` | **SHELL** — no-op by design, documented as plug-in points. |

All six Twin commands dispatch on all three layers:

| Tag | XAML | Handler | `ResolveCommand` |
|---|---|---|---|
| `Niagara_ExportPoints` | `:3829` | `:3845` | `:1604` |
| `Niagara_Reconcile` | `:3831` | `:3846` | `:1605` |
| `KUT_ValuationFromBms` | `:3795` | `:3847` | `:1606` |
| `KUT_LifecycleReconcile` | `:3793` | `:3848` | `:1607` |
| `KUT_PushLifecycleGapsToAcc` | `:3797` | `:3849` | `:1608` |
| `Healthcare_IoTRegistry` | `:5782`, `:5796` | `:319` | `:2161` |

### 4.3 The promise is file-mediated, which is the built part

`KUT_PROJECT_DELIVERY_PLAYBOOK.md:720-730` commits to exactly two things, and both land late:

| Stage | Commitment | Served by |
|---|---|---|
| **3.1 (~M40)** | *"Commissioning point list exported from the model for the controls contractor to load — model-driven, not hand-built"* | `Niagara_ExportPoints` ✅ |
| **3.3** | *"Reconcile: model equipment and points vs the live station"* | `Niagara_Reconcile` (against a station export) ✅ |

So **no Niagara obligation falls due at mobilisation**, and the live HTTP client is an *extra*
(it feeds `KUT_ValuationFromBms` for 5D commissioning valuation), not a playbook promise.

### 4.4 What is wrong

**(a) No connection template exists anywhere.** A case-insensitive sweep of all `*.json`,
`*.example`, `*.md` and `*.csv` finds Niagara named only in docs, guides and one workflow —
**there is no `niagara_connection.json` or `.example` in `project-templates/KUT/_BIM_COORD/`,
in `docs/examples/KUT/`, or anywhere else.** Fohlio ships one; Niagara does not. The required
shape exists only as a code comment (`NiagaraJsonClient.cs:11-13`). Anyone trying to enable the
live path at Stage 3.1 must read the source to learn the field names.

**(b) `pointsPath` defaults to `"/obix"`** (`NiagaraJsonClient.cs:33`), which is station-specific
and essentially certain to need changing. With no template, that is an undocumented guess.

**(c) Never exercised against a station**, by the code's own admission
(`NiagaraJsonClient.cs:15-17`): *"NETWORK CODE — not exercised in the dev sandbox (no live
station)… verify against a real Niagara station before production use."*

**(d) The point list will be empty until the MEP team populates BMS data.** `IoTDeviceRegistry`
harvests elements carrying `ICT_HEALTHIOT_DEVICE_ID_TXT` (`Data/MR_PARAMETERS.txt:3087`). On a
fresh KUT model nothing carries it, so `Niagara_ExportPoints` produces nothing. This is correct
per the playbook (BMS data is a Stage 2.3 activity) and it is reported honestly, not faked —
*"No BMS/IoT points found"* (`NiagaraCommands.cs:49`).

### 4.5 What is right

`Core/Twin/CommissioningSource.cs` is the best-built thing in this review. It resolves
live → cached → none (`:66-87`) and reports provenance explicitly (`:40-53`): `"live (captured
…)"`, `"CACHED … (station unreachable — last good read)"`, `"no BMS data"`. A valuation can
never silently run on stale points. And `FetchPoints` returns `null` on transport failure but an
empty dictionary for an empty station (`:82-109`), so the two are distinguishable — with
`ParsePoints` logging `SkippedNoId` (`:75-76`) precisely so a feed naming its id field something
unexpected cannot masquerade as an empty station. That is the CLAUDE.md failure mode being
actively designed out, and 43 tests hold it.

---

## 5. KUT workflows — all 97 steps resolve

Checked mechanically with a gate that I deliberately broke first.

```
ResolveCommand case labels : 607      StingCommandHandler cases : 1993
_allKnownCommandTags       : 222      XAML Cmd_Click button tags : 1592

GATE SELF-TEST
  known-good "Fohlio_Export"                    in ResolveCommand : True   (expect True)
  nonsense   "Fohlio_ExportZZZ_NOT_A_REAL_TAG"  in ResolveCommand : False  (expect False)
  gate discriminates correctly
```

| File | Steps | Resolve |
|---|---|---|
| `WORKFLOW_KUT_CoordinationCycle.json` | 8 | 8/8 (100%) |
| `WORKFLOW_KUT_DeliverableA.json` | 8 | 8/8 (100%) |
| `WORKFLOW_KUT_DeliverableB.json` | 15 | 15/15 (100%) |
| `WORKFLOW_KUT_DeliverableC.json` | 18 | 18/18 (100%) |
| `WORKFLOW_KUT_DeliverableD.json` | 13 | 13/13 (100%) |
| `WORKFLOW_KUT_FFESync.json` | 3 | 3/3 (100%) |
| `WORKFLOW_KUT_GateAudit.json` | 8 | 8/8 (100%) |
| `WORKFLOW_KUT_LifecycleReconcile.json` | 11 | 11/11 (100%) |
| `WORKFLOW_KUT_Mobilisation.json` | 5 | 5/5 (100%) |
| `WORKFLOW_KUT_MonthlyReport.json` | 8 | 8/8 (100%) |
| **Total** | **97** | **97/97 (100%)** |

Every step uses the correct `commandTag` + `label` field names — no step silently does nothing.
`_allKnownCommandTags` is a clean subset of `ResolveCommand` (0 false-known tags).

Because a false *pass* would be the worst outcome here, I confirmed the extraction window
(`WorkflowEngine.cs:1395-2244`) contains **exactly one** `switch` — at `:1397`, closing with
`default: return null;` at `:2240`. No foreign `case` labels inflate the resolvable set.

**The one workflow defect is semantic, not structural:** `CoordinationCycle` step 6 labels
`ACCPublish` *"Publish coordination data to ACC"* when it produces a local ZIP (§2.4a).

---

## 6. What breaks on day one, in order

> **"Day one" has already passed.** `docs/INDEX.md:125-126` records that mobilisation began the
> **week of 25 August 2026** and that the issued pack "[is] relied on now". As of this measurement
> that is ~2 weeks elapsed, so the first fortnightly coordination cycle is due about now. The ACC
> credential below is therefore **overdue, not upcoming** — it is the single most time-critical item
> in this report, and it is the one item nobody on the STING side can close alone.

Mobilisation itself is clean: `WORKFLOW_KUT_Mobilisation.json` is five local steps
(`LoadSharedParams`, `CreateWorksets`, `CreateFilters`, `GenerateBEP`, `CDEStatus`) and touches
**none** of the three integrations. Kick-off is not blocked.

| # | When | What happens | Severity | Blocked on |
|---|---|---|---|---|
| 1 | First fortnightly Coordination Cycle (Stage 2.2) | `ACC_PullClashes` stops at a dialog naming the four missing credential fields and returns `Cancelled`. Honest, but the step does not run. | **High** — the CDE rhythm is the project's spine | **Third party** — an APS app from the Owner's Autodesk account + callback registration + container ids |
| 2 | Same cycle, step 6 | `ACCPublish` writes a local ZIP while the step says it published to ACC. Operator believes the drop reached the CDE. | **High** — a false belief about an issued container | Nobody. Text + wiring fix, offline |
| 3 | First live ACC pull, whenever credentials arrive | A wrong container id or changed clash sub-path returns empty; the cycle reports **success, no clashes**. Indistinguishable from a clean federation. | **High** — silently skipped coordination | Nobody for the fix; one live pull to confirm paths |
| 4 | First bulk clash escalation | Under ACC rate-limiting, issues are dropped one-by-one; the log claims retries that never ran (§2.4b). | Medium | Nobody. Two-line fix, offline |
| 5 | First FF&E cycle (Stage 2.2) | Works — *provided* `fohlio_map.json` has been copied to `<project>/_BIM_COORD/` and the mapping is agreed with the Interior Designer. | Medium | Deployment: nobody. Agreement: **Interior Designer** |
| 6 | Stage 3.1 (~M40) | `Niagara_ExportPoints` exports nothing until the MEP team has populated `ICT_HEALTHIOT_*` from Stage 2.3. Reported honestly. | Low (months away) | MEP team's 2.3 authoring |
| 7 | Stage 3.1–3.3, only if the live BMS read is wanted | No `niagara_connection.json` template exists; the shape must be read out of source. | Low (months away) | Controls contractor for the station URL/credentials |

---

## 7. Promise vs. code

| KUT document promise | Code reality | Exposure |
|---|---|---|
| `BEP:171` CDE platform = ACC; `:175` *"Transmittals issued through the CDE"* | ACC is a product the team drives directly in its own web UI. The plugin only **feeds** it, and `ACCPublish` feeds it by hand. | **None on the promise** — ACC is the CDE regardless of the plugin. The exposure is that plugin automation is unproven, not that the CDE is missing. |
| `BEP:174`, `:220` *"ACC Issues and Reviews; BCF to ACC Issues"* | `ClashBcfExport` writes BCF 2.1 locally for manual import; `AccIssueSync.PushIssueAsync` pushes issues directly. Both real; the direct push is unproven and 429-fragile. | **Low** — two routes exist, one of them manual and reliable. |
| `BEP:268` *"Clash-free (high-priority) via ACC Model Coordination before every data drop"* | `ACC_PullClashes` is real but unproven, and an empty result reads as clean (§2.4c). | **Medium** — a gate that can pass without having checked. Fix #3 before the first drop. |
| `Playbook:709` Fohlio cycle: export → enrich → *"imported back, matched by **Room Number**, with a diff preview before anything is written"* | Exactly what the code does — `FohlioFinishesCommands.cs:75` keys on room number; `Fohlio_Import` previews a diff. | **None.** Unusually precise alignment. |
| `Playbook:711` *"Monthly currency check — model vs Fohlio — as a KPI line"* | `Fohlio_Audit` computes linked % / missing-ref / stale; surfaced in the monthly dashboard (`project-templates/KUT/README.md:115-116`). | **None.** |
| `BEP:326` *"Fohlio API delay → use file/Add-in route now"* | The CSV route is built and tested; REST is stubbed. | **None — already disclosed and mitigated in the issued BEP.** |
| `Playbook:726` Stage 3.1 *"Commissioning point list exported from the model"* | `Niagara_ExportPoints`. | **None** (timing: needs 2.3 data first). |
| `Playbook:727` Stage 3.3 *"model equipment and points vs the live station"* | `Niagara_Reconcile` against a station export taken from the live station. | **None** — satisfies the intent; no live transport required. |
| `BEP:70` *"Model reconciled to live points at handover"*; `:110` AIR *"Niagara at handover"* | Met by the Stage 3.3 file reconcile. The live HTTP client is an optional extra. | **Low**, and not due for ~3 years of programme time. |

**Net:** one medium exposure (#3, a coordination gate that can pass blind) and one high-severity
internal mislabel (#2, inside a STING workflow rather than an issued document). Everything else
the documents promise is either built or explicitly disclosed. **No promise in an issued KUT
document is contradicted by the code.** That is a better position than the 646 stub markers in
`CLAUDE.md` would lead anyone to expect.

---

## 8. Is it ready to start the KUT project?

**Yes — start. Not all-or-nothing, and the parts that are ready are the parts mobilisation needs.**

**Ready now, no credential required**
- Mobilisation end to end — all five steps local, 100% resolvable.
- The entire Fohlio FF&E / finishes stream (the contracted CSV route): 5 commands, real I/O,
  parameters bound, 72 tests, a non-circular data gate, and an Owner-standards WARN check.
- Niagara's file-mediated point-list and reconcile commands — built and dispatched, and not
  due until ~M40 anyway.
- All 10 KUT workflows resolve; 97/97 steps.
- The plugin builds 0/0.

**Ready on arrival of one credential**
- The whole ACC automation surface. It needs an APS app (Client ID + Secret), the
  `http://localhost:8910/callback` URL registered, and the ACC container ids. Then
  *one live pull* settles the only genuine unknown — the clash sub-paths. The KUT README
  already prescribes this: *"do one live pull during mobilisation"*
  (`project-templates/KUT/README.md:142-143`).

**Fix before the first coordination cycle — all offline, no third party**
1. Make an empty ACC clash result distinguishable from a clean one (#3). Highest value here:
   it is the difference between a gate and the appearance of one.
2. Correct the `CoordinationCycle` step-6 label, and decide whether the real `AccModelUpload`
   should be in the cycle instead of a manual ZIP (#2).
3. Move the `PushIssueAsync` request construction inside the retry loop (#4) — two lines.
4. Delete or wire `AccIssuesClient.cs` (§2.4d).
5. Ship a `niagara_connection.json.example` (§4.4a) — cheap now, and it removes a
   source-reading task from someone at M40.

**Not ready, and correctly so**
- Live BMS readback over BACnet/OPC-UA. Stubbed by design, an FM add-on, not promised.
- Fohlio REST. Stubbed, and the BEP already mitigates it.

The honest summary: this is not a prototype wearing a product's documentation. The transports
are real, the failure paths mostly tell the truth, and the internal documents
(`project-templates/KUT/README.md`, `docs/ROADMAP.md`) describe the state accurately. What is
missing is **one Autodesk credential and one live pull** — plus five small offline fixes, of
which only the first is load-bearing.

---

## 9. Corrections to existing documentation

Measured against the code on 2026-09-10; each is a doc fix, not a code fix.

| Claim | Where | Measured | Which is right |
|---|---|---|---|
| *"`TwinReadback` BACnet / OPC-UA transports are abstract stubs"* presented as the whole Niagara story | `CLAUDE.md` Healthcare Pack caveat 3 | True of `TwinReadback.cs:39,46`, but omits `NiagaraJsonClient` (real HTTP, `:98`) and the file-mediated `NiagaraCommands.cs` | **Code.** The statement is true but reads as "Niagara is absent"; two of three paths are built. |
| `StingTools.Boq.Tests` — *"121 declared / 196 cases"* | `CLAUDE.md` §3 test table (dated 2026-08-06) | **1311 passing, 0 failing** on this checkout | **Code.** The table is a month stale and understates by ~6.7×. |
| *"9 `WORKFLOW_KUT_*.json` files"* | this task's brief | **10** files | **Code.** `DeliverableA`–`D`, `CoordinationCycle`, `FFESync`, `GateAudit`, `LifecycleReconcile`, `Mobilisation`, `MonthlyReport`. |
| Repo-wide matches: *Fohlio 271 files, ACC 209, Niagara 86, Tridium 26* | this task's brief | Fohlio **76**, "Autodesk Construction Cloud" **53**, Niagara **32**, Tridium **11** (excluding `bin`/`obj`/`node_modules`/`.git`). Including build output: 107 / 65 / 47 / 11. Occurrence counts: Fohlio 996, Niagara 274, Tridium 17. | **Neither reproducible.** I could not reconstruct the brief's figures by file count, by including build artefacts, or by occurrence count. It does not affect any verdict — I enumerated the logic files directly rather than reasoning from counts. Recorded so the next person does not chase it. |

The brief's warning against searching bare `ACC` is well founded: it matches **1842** files in
this tree (`accept`, `access`, `according`, `accumulate`). Every ACC count above uses
`AccIssueSync` / `AccOAuthFlow` / `AccModelUpload` / `AccModelCoordSync` / `ACCPublish` /
`AutodeskConstruction` or the full product name. Note that `Core/Validation/AccessibilityAuditor.cs`
matches an `Acc*.cs` filename glob and is **not** ACC-related — a trap for the next file-name sweep.

---

## 10. Method — how to re-measure

```bash
# Build (safe; does NOT touch the Revit add-in manifest — never run deploy.bat for this)
dotnet build StingTools/StingTools.csproj -c Debug -clp:Summary

# Fohlio + Niagara test evidence
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --filter "FullyQualifiedName~FohlioParameterBindingTests"
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --filter "FullyQualifiedName~ShippedFohlioMapTests"
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --filter "FullyQualifiedName~NiagaraPointParserTests"

# Dead-code / caller checks (verify the instrument on a type you know IS used first)
grep -rn "AccIssuesClient" --include="*.cs" . | grep -v /obj/ | grep -v /bin/   # expect: definition only
grep -rn "AccIssueSync"   --include="*.cs" . | grep -v /obj/ | grep -v /bin/   # expect: 20+ call sites
grep -rn "FohlioRestTransport\|FohlioConnection" --include="*.cs" . | grep -v /obj/

# Workflow resolve rate (the gate self-tests before reporting)
#   ResolveCommand window = WorkflowEngine.cs:1395-2244, verified to hold exactly one switch:
awk 'NR>=1395 && NR<=2245 && /switch[[:space:]]*\(/ {print NR": "$0}' StingTools/Core/WorkflowEngine.cs
```

The 429 reproduction is a ~40-line console app: an `HttpListener` answering 429, and a single
`HttpRequestMessage` re-sent in a loop exactly as `AccIssueSync.cs:215-229` does it. On
.NET 8.0.23 attempt 1 throws `InvalidOperationException`.
