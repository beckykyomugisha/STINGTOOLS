# `planscape://` deep links — what works, what doesn't, and how to test it

Branch `claude/livekit-corporate-ui-research-3233e0`. Companion to
[`ACC_UI_SHELL_GRID_CONTRACT.md`](ACC_UI_SHELL_GRID_CONTRACT.md) (the web half of
the same pass).

---

## 1. The problem

StingTools has generated `planscape://` links for a long time:

| Link | Minted by |
|---|---|
| `planscape://dashboard/{project}/{yyyyMMdd-HHmm}` | `PlanscapeServerClient.BuildDashboardShareLink` — copied to the clipboard from `WarningsManager.cs:4647` and `StingCommandHandler.cs:9150` |
| `planscape://issue/{id}` | BCC Issues tab context menu (`BIMCoordinationCenter.cs:3274`) |
| `planscape://deliverable/{code}` | BCC Deliverables tab context menu (`BIMCoordinationCenter.cs:8838`) |

Nothing registered `planscape://` with Windows, so **clicking one did nothing**.
They were formatted strings that looked like links.

**The constraint that shapes the whole solution:** a Windows URL protocol handler
must point at a standalone `.exe`. StingTools is a DLL loaded inside `Revit.exe`,
so it cannot be the registered target. This could never be "add a registry key
pointing at StingTools.dll".

---

## 2. What was built

Option (a) from the brief — a minimal helper executable plus a file inbox.

```
user clicks planscape://issue/abc-123
  └─ Windows reads HKCU\Software\Classes\planscape\shell\open\command
       └─ launches  StingLink.exe "planscape://issue/abc-123"
            ├─ writes ONE file into %LOCALAPPDATA%\STING\link-inbox\*.link
            └─ finds the Revit window, restores + foregrounds it
                 └─ PlanscapeLinkWatcher (IIdlingJob, inside the plugin)
                      polls the inbox ~1×/s, takes the link, deletes the file
                        └─ BIMCoordinationCenterCommand.ShowFor(uiApp, "ISSUES")
```

| File | Role |
|---|---|
| `StingTools/Core/PlanscapeProtocol.cs` | The shared contract — parse, escape, inbox read/write, HKCU registration. **Revit-free**, because the helper compiles it in. |
| `StingTools.LinkHandler/` (`StingLink.exe`) | The registered target. ~180 lines, no UI framework, one `user32` message box for its three failure cases. |
| `StingTools/Core/PlanscapeLinkWatcher.cs` | `IIdlingJob` that drains the inbox on Revit's API thread. |
| `StingTools/Core/StingToolsApp.cs` | `RegisterPlanscapeProtocol()` on startup + enqueues the watcher. |
| `StingTools/Core/WarningsManager.cs` | `BIMCoordinationCenterCommand.ShowFor(uiApp, tab, out error)` — `Execute` now delegates to it. |
| `StingTools/UI/BIMCoordinationCenter.cs` | `NavigateToTab(string)` — an internal, dispatcher-marshalled face for the private `NavigateTo`. |

### Decisions worth knowing

- **Why a file inbox and not a named pipe.** A pipe needs a listener thread, and
  anything that thread receives must be marshalled onto Revit's API thread
  anyway — which is what the Idling job already does. The pipe would be a thread
  whose only job is to write to a queue the Idling job reads. The inbox also
  handles "Revit isn't running yet" for free.
- **Why not `FileSystemWatcher`.** Same reason: it fires on a threadpool thread.
  Polling an almost-always-empty directory once a second is cheaper than the
  thread it would replace.
- **HKCU only, never HKLM.** `HKCU\Software\Classes` needs no elevation, is
  scoped to the signed-in user, and takes precedence over HKLM for the same key.
  Writing machine-wide state unattended from a plugin's `OnStartup` is not
  something to do without a human agreeing to it.
- **Re-registered on every startup.** `EnsureRegistered` is a no-op when the
  command already matches, so the usual cost is one registry read. It re-points
  when the plugin folder moves — which it does; see the deploy-target memory —
  because a protocol registered against a deleted `.exe` fails as "broken app",
  which is more confusing than "no handler".
- **Links are parsed by hand, not via `System.Uri`.** Links minted before this
  pass never escaped the project name, so `planscape://dashboard/Kampala Uganda
  Temple/…` is not a legal URI and `new Uri(...)` throws on it. Being lenient is
  what keeps links already sitting in people's chat history working.
  `EscapeSegment` exists for new links.
- **Queued links expire after 30 minutes** (`PlanscapeProtocol.MaxAge`). A link
  is a "take me there now" gesture; acting on one clicked last Tuesday, because
  Revit happened to be shut, would be a jump-scare.
- **Reading a link deletes it.** With two Revit instances open, whichever reads
  first owns the link and the other never sees it. Both jumping to the same issue
  would be worse than one of them missing it.
- **A `dashboard` link naming a different project asks before opening.** It
  carries a project name; silently showing another model's coordination data
  because that is what happened to be open would be quietly wrong.

---

## 3. Honest status — read this before assuming anything works

### ✅ Built and machine-verified

- `dotnet build StingTools.LinkHandler -c Release` → **0 warnings, 0 errors**.
- `dotnet build StingTools -c Debug` and `-c Release` → **0 warnings, 0 errors**,
  matching the repo baseline, with `StingLink.{exe,dll,runtimeconfig.json,deps.json}`
  copied into the plugin output by the `CopyStingLinkHelper` target.
- **The helper was actually run** outside Revit:
  `StingLink.exe "planscape://dashboard/Kampala%20Uganda%20Temple/20260731-1430"`
  → wrote `%LOCALAPPDATA%\STING\link-inbox\20260731-112742-793-bd6c62bb.link`
  containing the URL verbatim, and (Revit not running) showed the queued-link
  notice. The test file was deleted afterwards.

### ⚠️ NOT verified — nobody has clicked a real link

**The end-to-end path has never run.** No Revit session with this build has been
started, no protocol has been registered on any machine, and no link has been
clicked. Everything in §4 is open.

### ❌ Not built at all — the "Revit is not running" case

The brief called this out as lower priority and it is **not implemented**. When
no Revit window is found, `StingLink.exe` queues the link and shows a message
saying so; it does not launch Revit. That is a decision, not an omission:

- Up to three Revit versions may be installed (2025 / 2026 / 2027) and the link
  says nothing about which to use.
- The link carries no `.rvt` path, so a launched Revit would open empty and the
  30-minute window would likely expire before the user opened the right model.
- Starting a multi-gigabyte application unprompted because someone clicked a link
  in a chat message is a bigger action than the click implies.

The queue makes the manual path work: open Revit within 30 minutes and the link
fires. **If auto-launch is wanted later**, the missing pieces are (a) a Revit
version/path lookup — `HKLM\SOFTWARE\Autodesk\Revit\<ver>\InstallationLocation`
is the usual source — (b) a decision about which version wins, and (c) extending
`MaxAge` for the launch case only, since startup plus model-open can exceed 30
minutes on a large central model.

### ❌ Also not done

- **The target row is not selected.** `planscape://issue/{id}` opens the ISSUES
  tab, not that issue. Resolving an id to a grid row means reaching into the
  BCC's grids inside an 11,900-line file; the tab navigation is the MVP.
- **Nothing generates the escaped form yet.** `EscapeSegment` exists and the
  parser handles both, but `BuildDashboardShareLink` still emits the unescaped
  project name. Changing it is a one-line follow-up; it was left alone so this
  slice did not also alter what the existing copy-link buttons put on the
  clipboard.
- **No uninstall hook.** `PlanscapeProtocol.Unregister` exists and
  `StingLink.exe --unregister` calls it, but nothing runs it automatically.

---

## 4. PENDING-HUMAN-VERIFY

Needs a machine with Revit and the plugin deployed. Nothing below has been done.

### Registration
1. Build Release and deploy (`StingTools\bin\Release` → the live plugin folder;
   grep the live `.addin` for the `<Assembly>` path first — it moves).
   Confirm **`StingLink.exe` is in the deployed folder** next to `StingTools.dll`.
   If it isn't, the copy target didn't reach the deploy step and nothing else
   here can work.
2. Start Revit. In `StingTools.log`, expect one of:
   - `planscape:// protocol registered → "…\StingLink.exe" "%1"`
   - `planscape:// protocol not registered: already registered`
   - `planscape:// protocol not registered: StingLink.exe not found beside the plugin (…)` ← the failure to look for
3. Check the key: `reg query HKCU\Software\Classes\planscape\shell\open\command`
   → the command must quote the exe path **and** `%1`, and the path must exist.
4. **Confirm nothing was written to HKLM:** `reg query HKLM\Software\Classes\planscape`
   should say the key does not exist. If it does exist, stop and report it — this
   feature is user-scope by design.

### The case that is supposed to work: Revit already running
5. With Revit open and a model loaded, use the BCC's copy-link on an issue, then
   paste the link into the Run dialog (Win+R) and press Enter.
   Expect: Revit comes to the front, the Coordination Center opens (or focuses)
   **on the ISSUES tab**, within a second or so.
6. Repeat for a deliverable link → DELIVERABLES tab.
7. Repeat for a dashboard link **whose project name matches** the open model →
   OVERVIEW tab, no prompt.
8. Dashboard link for a **different** project → a dialog naming both projects and
   asking whether to open the Coordination Center for the open one. "No" must do
   nothing at all.
9. **Minimised Revit:** minimise it, click a link. It should restore and come
   forward, not just flash in the taskbar.
10. **BCC already open on another tab:** it should switch tabs, not reopen.
11. **Rapid clicks:** click three links quickly. Exactly one navigation happens
    (the newest), and `StingTools.log` records
    `PlanscapeLink: 3 queued links; handling the newest…`.
12. **Idle cost:** leave Revit open and idle for a few minutes with no links.
    Nothing appears in the log and there is no perceptible CPU from the watcher —
    it should poll roughly once a second and find an empty directory.

### The case that is NOT built
13. Close Revit entirely, click a link. Expect a message box saying the link is
    queued and to open Revit within 30 minutes. **Revit must NOT launch.**
14. Then open Revit and a model. The queued link should fire once, shortly after
    the model finishes loading.
15. Wait more than 30 minutes with a queued link, then open Revit: the link is
    silently discarded and the inbox is empty. Confirm no jump happens.

### Edge cases
16. **Old, unescaped link:** paste `planscape://dashboard/Some Project Name/20260101-0900`.
    It should still parse — this is the compatibility case the hand-written
    parser exists for. (Note: Windows itself may mangle a link with raw spaces
    before it ever reaches the helper; if it does, that is a Windows limitation,
    and the fix is switching `BuildDashboardShareLink` to `EscapeSegment`.)
17. **Junk:** `planscape://` alone, and `planscape://nonsense` → the helper says
    it is not a Planscape link (or opens OVERVIEW for an unknown kind) and does
    not throw.
18. **Read-only inbox:** deny write on `%LOCALAPPDATA%\STING\link-inbox`, click a
    link → a clear "could not hand the link to StingTools" message naming the
    path, not a silent failure.
19. **Two Revits open**, click one link → exactly one of them navigates.
20. `StingLink.exe --unregister` then click a link → Windows offers to search for
    an app, i.e. the handler is genuinely gone. Re-register with `--register`.

---

## 5. `planscape://revit/select` — locating an ACC clash issue's objects (ACC-HARD-5, 2026-09-30)

### Why

ACC's Issues API cannot pin an issue to model objects: `linkedDocuments` (the pushpin
block) is read-only, and the issue-create "permitted attributes" list says `linkedDocument`
is not applicable ([Issues.yaml](https://github.com/autodesk-platform-services/aps-sdk-openapi/blob/main/construction/issues/Issues.yaml)).
So an issue `ACC_PullClashes` escalates from a clash cannot carry a pushpin, and the
assignee had to search the model for "object 17299 in KUT_MEP.rvt". The issue now carries
three things instead, each independently switchable in `acc_settings.json`
(`issueDeepLinks`, `issueViewerLinks`, `issueBcfAttachment` — booleans, default `true`,
strictly typed like every other key):

1. **A `planscape://revit/select` link per model** — selects and zooms to the objects in
   the open Revit model.
2. **ACC's own viewer link** for each model version — the `data.links.webView.href` Data
   Management returns for the version (`GET data/v1/projects/{p}/versions/{v}`). STING does
   not assemble ACC URLs: there is no documented URL shape, only the link the API gives.
3. **A one-topic BCF 2.1 attachment** (`.bcfzip`) naming the two elements, for BCF managers
   in Revit / Navisworks — uploaded exactly as the
   [Upload Issue Attachments tutorial](https://aps.autodesk.com/en/docs/acc/v1/tutorials/issues/upload-issue-attachment/)
   describes (topFolders → storage in the root folder → `signeds3upload` → PUT → finalise →
   `POST construction/issues/v1/projects/{p}/attachments`).

### The link

```
planscape://revit/select?doc=<model name>&uid=<UniqueId>[,<UniqueId>…][&more=N]
```

| Part | Meaning |
|---|---|
| `doc` | The ACC document name (`KUT_ARCH.rvt`). Used only to word messages; `ModelNameMatches` accepts the title without `.rvt` and a workshared local copy's `_user` suffix. |
| `uid` | Revit UniqueIds, comma-separated, URL-escaped. **Selection is decided by these alone.** |
| `more` | How many ids the minter left out to keep the link ≤ 400 characters (`PlanscapeProtocol.DefaultMaxLinkLength`). Ids are never cut mid-way; a link that cannot hold one whole id is not minted. |

A clash's two sides are usually in two models, so each model gets its own link. The issue
description stays within ACC's 1000-character limit (`AccIssueLinks.ComposeDescription`):
the triage text is shortened first, and a link that still does not fit is left out whole —
a truncated URL is a broken link.

### How the UniqueId is found (dbId → externalId)

ACC clash data carries viewer object ids (`lvid`/`rvid`), which the clash tutorial
(`bim360/v1/tutorials/mc-tutorial-clash`) says are **not stable across document versions** —
they only mean something paired with the exact version. So:

1. document scope file `documents[].urn` (the version URN) for the clash's `ldid`/`rdid`;
2. `GET bim360/modelset/v3/containers/{c}/modelsets/{m}/versions/{v}` (`v` = the test's
   `modelSetVersion`) → `documentVersions[]`: `bubbleUrn` ("the URN of the Model Derivative
   bubble") and `viewableGuid` ("the geometry node in the derivative manifest");
3. `GET modelderivative/v2/designdata/{urlsafe-b64 bubbleUrn}/metadata` → the 3D view whose
   guid is `viewableGuid` (else the master 3D view, else the first 3D view);
4. `POST …/metadata/{guid}/properties:query` with `{"$in":["objectid", …]}` and
   `fields: ["objectid","externalId"]` → `externalId`, which for a Revit object is its
   UniqueId. Model Derivative's region header is `region` (not `x-ads-region`).

Lookups are batched per document version, cached for the session, bounded (≤ 20 versions a
run, 3 attempts through `AccHttp`). **Anything that does not resolve gives no link for that
side**, a one-line reason in the description ("No Revit link for KUT_MEP object 3403: …"),
and a count in the command's report. The issue is created either way. An `externalId` that
is not shaped like a Revit UniqueId gives no link. The plugin half resolves with
`Document.GetElement(uniqueId)`, which returns nothing — never another element — for an id
the model does not hold.

### What the Revit half does (`Core/PlanscapeRevitSelect.cs`)

On the Idling tick (`PlanscapeLinkWatcher`, kind `revit`): resolves each UniqueId in the
**active** model, then in the models **linked** into it; selects everything found with
`Selection.SetReferences` (link references for linked elements); zooms with
`UIDocument.ShowElements` (host elements) or `UIView.ZoomAndCenterRectangle` over the
transformed bounding box (linked-only). Silent when everything was found; otherwise one
dialog saying which ids were not found and why that is likely. If nothing is found it says
whether the named model is the active one (elements deleted since that ACC version), open
but not active (switch and click again — `OpenAndActivateDocument` is not permitted from an
Idling handler), or not open.

### BCF attachment contents

`bcf.version`, `{topic}/markup.bcf` (TopicType `Clash`, a `Viewpoints` reference — added to
the shared `BcfEngine` in this pass, which previously wrote the viewpoint file without
referencing it), `{topic}/viewpoint.bcfv` with the elements as `Selection/Component`s:
`IfcGuid` derived from the UniqueId the way Revit's IFC export does by default (episode GUID
XOR element id, then IFC base-64) and the UniqueId itself as `AuthoringToolId`. **No camera**:
ACC gives no coordinates, and an invented camera would be a fabricated viewpoint. The topic
GUID is derived from the clash signature, so a re-send is the same topic.

### Status

| | |
|---|---|
| ✅ Tested (`StingTools.Acc.Tests/AccIssueLocateTests.cs`, loopback) | dbId→externalId through bubble URN + view guid; missing object; MD 404 / 202 → no link, issue still pushed; non-Revit externalId; cache; link format, length cap and round trip; description limit; the six attachment calls in order with the documented body; BCF structure; strict settings parsing; clash read carries document URNs + `modelSetVersion` (`completedOn`) |
| ⚠️ Build only | `PlanscapeRevitSelect` (selection / zoom / messages in Revit) |
| ❓ Unconfirmed against live APS | the document scope field name `urn` (from the `aps-clash-data-view` sample, not the reference); that `viewableGuid` equals a Model Derivative metadata guid (fallback covers it); that Revit `externalId` is always the UniqueId (true of every observed example; validated by shape); that ACC accepts `.bcfzip` as an issue attachment; whether ACC renders `planscape://` as a clickable link (if not, copy it into Win+R); MD `region` values for ACC-hosted derivatives |
