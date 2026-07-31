# Document sync — implementation plan

**Design:** [`docs/superpowers/specs/2026-07-31-document-sync-design.md`](../specs/2026-07-31-document-sync-design.md)
(approved by Sting, 2026-07-31). This plan implements it; it does not revisit it.

**Branch:** `claude/livekit-corporate-ui-research-3233e0` · PR #503.

**Baseline before any of this work:** `Planscape.Tests` — **493 passed, 0 failed, 9 skipped
(502 total)**. Every slice below re-runs the full suite against that number.

---

## 1. Resolving the spec's three open items

The design deferred three decisions to this plan by name. Each is settled here, with the
reasoning, because the reasoning is what a future session needs when the decision looks wrong.

### 1a. Companion↔BCC IPC transport → **named pipe**

BCC needs exactly two things: read status, and trigger "sync now". Nothing else. Three candidates
were considered against that:

| Option | Verdict |
|---|---|
| **Loopback HTTP via `HttpListener`** | **Rejected — needs elevation.** Binding a fixed `http://127.0.0.1:{port}/` prefix as a non-admin requires a pre-existing URL ACL (`netsh http add urlacl`). That is machine-wide state and an elevation prompt, which this feature's whole posture rules out. |
| **Loopback HTTP via Kestrel** | **Rejected — cost.** Avoids the URL-ACL problem (raw socket bind), but pulls ASP.NET Core hosting into a tray app to serve two verbs. A listening TCP port is also reachable by *any* local process, including a browser tab, so it would need its own bearer token — more code than the thing it is protecting. |
| **File drop + status file** (the StingLink precedent) | **Rejected, narrowly — see below.** |
| **Named pipe** | **Chosen.** |

**Why not the file-inbox precedent, given StingLink.exe used it.** That precedent was reasoned
about a *specific* asymmetry: StingLink is short-lived and its receiver (Revit) had no listener,
so a pipe's only job would have been handing work to an `IIdlingJob` that already existed. Neither
half of that holds here. The Companion is long-lived and has a natural place to host an accept
loop, and BCC is asking a *question* rather than dropping a job.

The deciding factor is the failure mode. With a file drop, "the Companion isn't running" and "the
Companion is running but busy" look identical at click time — BCC would have to write, then poll a
status file, then decide how stale is too stale. That staleness heuristic is a fuzzy judgement a
pipe makes exact: a connect that fails in 200 ms is a definite "not running", which is precisely
the answer a user clicking "Sync now" into the void needs to see.

Concretely: `System.IO.Pipes.NamedPipeServerStream`, one line of JSON per request, one per
response. No package dependency, no port, no firewall prompt, and Windows ACLs the pipe to the
creating user by default — matching the per-user, no-elevation posture of the rest of the feature.

Pipe name: `planscape-companion` (per-user by ACL, so no user suffix is needed for isolation).

### 1b. Installer → **piggyback on StingTools, with the Companion self-registering**

No new installer. The Companion ships in the plugin output directory exactly as `StingLink.exe`
does (same `CopyStingLinkHelper`-style MSBuild target), and autostart is an **HKCU
`…\CurrentVersion\Run` value** — the same user-scope, no-elevation, idempotent, re-checked-every-start
pattern `RegisterPlanscapeProtocol` already established for the protocol handler.

Split of responsibility, chosen to keep this session's changes out of the Revit plugin entirely:

- **The Companion registers its own autostart** on first run (`--install-autostart`, and
  automatically on a normal first launch). This is Slice B.
- **`StingToolsApp.OnStartup` launching it if absent** is deferred to Slice D. It is the piece
  that makes the whole thing zero-touch for a user who only ever opens Revit, and it is one
  `Process.Start` next to the existing `RegisterPlanscapeProtocol()` call.

**Honest caveat, written down rather than discovered later:** until Slice D lands, the Companion
must be started once by hand. And even after Slice D, a machine that never opens Revit never gets
a Companion. That is acceptable — the Companion exists to serve an Author, and an Author runs
Revit. A Coordinator-only machine needing this is the point at which a real installer earns its
keep, not before.

### 1c. Background failure visibility → **tray state + pull-on-demand, never a toast**

A background sync can fail in two materially different ways, and conflating them is what makes
this kind of notification useless:

| Class | Examples | Treatment |
|---|---|---|
| **Offline** — expected, self-healing | laptop closed, VPN down, server restarting | **Silent.** Tray icon shows a muted/disconnected glyph. No notification, ever. The reconnect delta fixes it with nobody involved. |
| **Error** — needs a human | auth rejected, target folder unwritable, disk full, 403 on every document | **Tray icon changes state**, tooltip names the failure, and the status is available to BCC over the pipe. |

**No Windows toast.** The most frequent failure by far is the offline case, which is expected and
resolves itself; toasting it trains the user to mute the app, at which point the *real* errors go
unseen too. Pull-on-demand (BCC reads status when it opens; the user hovers the tray icon) cannot
spam by construction.

The tray icon is already in the design for checked-out counts, so an error state on that same icon
introduces no new UI language — the same instinct the spec applies to the BCC badge.

Persisted shape (survives a Companion restart, so a failure at 5 pm is still visible at 9 am):

```
SyncStatus { State: Idle | Syncing | Offline | Error,
             LastSuccessUtc, LastError, LastErrorUtc, ConsecutiveFailures }
```

---

## 2. Slices

Each is independently committable and provable. **This session: A, B, C.** D and E are explicitly
a later pass — see §3.

### Slice A — server-side foundation *(this session)*

1. **`DocumentSyncHub`** (`Planscape.Infrastructure/SignalR/DocumentSyncHub.cs`), modelled on
   `MeetingHub`:
   - `JoinProject(projectId)` / `LeaveProject(projectId)`, group `docsync:{projectId}`.
   - Gated by `HubTenantGuard.OwnsProjectAsync` — the same guard, for the same reason: a hub
     connection has no `HttpContext`, so the DbContext tenant filter resolves to an empty
     `TenantId` and cannot be relied on. A per-connection authorised set caches the check.
   - `NotifyDocumentChanged(IHubContext<DocumentSyncHub>, projectId, payload)` static push.
   - Mapped at `/hubs/document-sync` in `Program.cs`.
2. **Call sites** — both places a document actually changes:
   - `DocumentsController.PerformCdeTransitionAsync` (the single shared core for web PUT, mobile
     POST and plugin sync — it already mints a `DocumentRevision` and broadcasts on
     `NotificationHub`).
   - `DocumentRevisionsController` manual revision creation.
3. **`GET /api/projects/{id}/documents/changed-since?since={iso}`** on `DocumentsController`:
   - Delta on `UpdatedAt ?? UploadedAt`; `since` omitted ⇒ everything currently visible.
   - Scoped by `ProjectMemberAcl` (the existing `AllowedCdeStates` / discipline / suitability
     ACL) — the sync surface must not be wider than the documents list.
   - **Latest-revision-only metadata**, not history.
4. **Tests** to the `LiveKitRoomTenancyTests` bar — real assertions, and specifically the
   cross-tenant refusal, which is the one that matters.

### Slice B — Planscape Companion skeleton *(this session)*

New `Planscape.Companion/` project — a sibling of `StingTools.LinkHandler`, **not** a modification
of it. `StingLink.exe` is finished and out of scope per the spec's own reasoning.

- WinExe, `net8.0-windows`, `UseWindowsForms` for `NotifyIcon` only.
- SignalR client with backoff reconnect; on reconnect, immediately call `changed-since` per linked
  project with that project's last-known sync timestamp.
- Named-pipe server (§1a): `status`, `sync-now`, `ping`.
- Settings at `%APPDATA%\StingTools\planscape_sync.json`, Newtonsoft, merge-into-existing-JObject
  — the same shape and directory as `PlanscapeServerClient.MachineSettingsPath`.
- HKCU `Run` autostart self-registration.
- **`--diagnose`**: a headless one-shot mode (no tray) that proves the thing works from a terminal
  and doubles as a support tool.

### Slice C — core sync engine *(this session)*

Inside the Companion. One code path; four triggers (SignalR push, reconnect delta, initial link,
manual "Sync now").

- Target `%USERPROFILE%\Planscape\{ProjectCode}\`, per-project override in the settings file.
- **No OS-level locking.** No `FileShare.None`, no lock files as truth. The CDE state is the only
  ownership record. Non-WIP copies get the `ReadOnly` *file attribute* — a removable hint that a
  reference copy is not for editing, explicitly **not** a lock and not a source of truth.
- Superseded safety net: rename outgoing to `{Name} (superseded {yyyy-MM-dd}).{ext}`, purge those
  older than 7 days on startup and on each tick.
- Latest revision only, ACL-scoped. No full history.

### Slice D — StingTools/BCC integration *(NEXT SESSION — not started)*

- Pipe client in the plugin; BCC reads status, triggers "Sync now".
- `StingToolsApp.OnStartup` launches the Companion if it is installed and not running (§1b).
- Per-project auto/manual toggle (on by default).
- BCC Documents-grid badge, reusing the "Live" meeting badge pattern.

### Slice E — Companion tray polish *(NEXT SESSION — not started)*

- Checked-out count on the icon, click-to-expand list.
- Error-state glyph + tooltip wording (§1c decides the behaviour; this builds it).
- Per-document "download full history" opt-in action.

---

## 3. Scope boundary for this session

**In:** A, B, C. **Out, deliberately:** D, E, and everything the spec already rules out
(bidirectional sync, OS-level file locking, per-document toggles, full history by default, a
Windows Service, any change to `StingLink.exe`).

---

## 4. Status — what actually shipped

Updated as each slice lands. Anything not marked DONE was not built.

| Slice | Status | Proof |
|---|---|---|
| A — server foundation | **DONE** | `dotnet build` 0 errors · `dotnet test` **515 / 0 failed / 9 skipped** (baseline 493/0/9 — **22 new**, no regressions) |
| B — Companion skeleton | **DONE** | builds 0 warnings / 0 errors · **run**: tray started, named pipe answered three real requests from a separate process, "not running" detected in 567 ms |
| C — sync engine | **DONE, and proven end-to-end against a live server** | see §4a |
| D — BCC integration | **DONE** | plugin + Companion build **0 warnings / 0 errors**; `dotnet test` **515 / 0 / 9** (unchanged from Slice A — no regressions); the per-project toggle proven end-to-end against a live server — see §4c |
| E — tray polish | IN PROGRESS | — |

### 4a. What was actually run

The API was run **from this branch's source** on `:5099` against the docker
PostgreSQL (the running `docker-api-1` container is an older image — it 404s on
`changed-since`, which is how the version gap was spotted). A real personal access
token, a real uploaded document, a real Companion process.

| Behaviour | Observed |
|---|---|
| Initial sync | `1 downloaded`; SHA-256 on disk **matches the server's `contentHash` exactly** |
| Idempotent re-run | `0 downloaded`, delta mark advanced, nothing re-fetched |
| Read-only hint | WIP copy writable; after WIP→SHARED the copy is `ReadOnly`; the superseded copy is **not** (so it can still be moved) |
| It is a hint, not a lock | cleared the attribute by hand and appended to the file — nothing fought back |
| Supersede | old copy → `A-101-Floor-Plan (superseded 2026-07-31).pdf` with the **P01 bytes intact**, new bytes live |
| Same-day collision | resolved as `(superseded 2026-07-31 #2).pdf`, `#3` |
| **SignalR push** | with only the tray running, a transition on another connection produced `push: cde_transition` → `sync (Push)` → supersede + download. **Nobody touched the Companion.** |
| Purge | 9-day-old *superseded* file deleted; a recent superseded file **and** a 9-day-old *non-superseded* user file both untouched |
| Foreign project | reports "not visible to this account" rather than failing opaquely |
| IPC | `ping` returned the matching pid; `status` returned live state; unknown command refused |

**Two silent-failure bugs were found by running it, not by building it** — both
recorded here because they are the class of bug this feature is most exposed to:

1. The Companion registered `On<JObject>`. SignalR's default protocol is
   System.Text.Json, which cannot materialise a Newtonsoft `JObject`, so **the
   handler never fired** — connection healthy, server logging a successful push,
   client doing nothing at all. Fixed with a concrete payload type.
2. `DocumentSyncHub` refused joins silently *and* logged nothing server-side, so a
   tenant mismatch would have been undiagnosable. It now logs both the refusal
   (with the connection's tenant) and the grant, and the controller logs when the
   hub context is missing.

**Test artefacts were cleaned up:** the settings, status and log files this session
created under `%APPDATA%\StingTools\` were removed (they did not exist before), and
**no autostart registry entry was created** — confirmed absent afterwards.

### 4c. Slice D — what was actually run

The API was again run from this branch's source on `:5099` against the docker
PostgreSQL, with a real PAT and a real document.

| Behaviour | Observed |
|---|---|
| Schema column | `ALTER TABLE "Projects" ADD COLUMN IF NOT EXISTS "DocumentSyncAutoEnabled"` applied by the existing idempotent patcher — `[schema-patch] done — 21 ok, 0 failed`, and `\d "Projects"` shows `DocumentSyncAutoEnabled | boolean | not null | true` |
| Default | a project that predates the column reads `documentSyncAutoEnabled = True` on the list payload |
| **Toggle OFF** | `PUT /api/projects/{id} {"documentSyncAutoEnabled":false}` → a subsequent CDE transition pushed `(auto-sync off)` and the Companion logged the push and **stopped**. The file on disk stayed at the previous revision. |
| **Manual overrides the toggle** | with auto-sync still OFF, `sync-now` over the pipe returned `{"ok":true,"started":true}` and downloaded the new revision, superseding the old copy — exactly what the design says manual should do |
| **Toggle ON** | the next transition pushed `(auto-sync on)` and synced by itself |
| Companion ships with the plugin | `Planscape.Companion.{exe,dll,runtimeconfig.json,deps.json}` + the SignalR client closure land in `StingTools/bin/Debug`, and the copied exe **runs from there** (`--status` succeeded), proving the dependency set is complete |
| Shared pipe client | `--ping` (which calls the same `CompanionIpcClient` compiled into StingTools) reports `running: False` with the Companion stopped and full live status with it running |

**One cross-project build constraint worth knowing:** `CompanionIpcContract.cs` is
compiled into two projects with different settings — the Companion enables
`ImplicitUsings` and nullable reference types, StingTools disables both. The file
therefore declares every `using` explicitly and opens with `#nullable enable`.
Without that it builds on one side and fails (or warns) on the other, which is
how it was found.

### 4b. Known gap found while building, not a regression

`ProjectMemberAcl.ResolveAsync` currently **hard-codes its three allow-list columns
to `null`** (a deliberate migration-safety choice — see its own comment), so no
CDE/discipline/suitability narrowing happens for anyone today, on the documents
list or on `changed-since`. `changed-since` routes through the same helper, so it
cannot be *wider* than the list — and the test asserts exactly that subset
invariant rather than a filtering behaviour that does not currently exist. When
those columns are read for real, both endpoints narrow together. Flagged so nobody
reads "respects `AllowedCdeStates`" as "filters today".

### 4d. Second inert-plumbing finding (Slice D), same shape as §4b

`CoordData.Deliverables` — the list behind BCC's DELIVERABLE REGISTER — **is never
populated**. It is declared, read by the KPI cards and the grid, and appended to by
the inline "add row" button, but nothing in `BuildCoordData` loads
`_BIM_COORD/deliverables.json` into it. The register therefore renders empty in the
normal flow, and the new **Local** sync badge column, though correct, has nothing to
badge until rows exist.

Not fixed here, deliberately: wiring the register to the deliverables file is a
feature in its own right with its own schema-mapping decisions, and bundling it into
a badge change would be exactly the drive-by this pass was told to avoid. It is
recorded next to §4b because the two are the same kind of thing — correct code
sitting on top of plumbing that was never connected.

**A related design mismatch, decided rather than drifted:** the spec asks the badge
to reuse "the same visual pattern already used for the Live meeting badge". That
badge does not exist in BCC — it is in the *web* app's project overview. Rather than
invent a new look for a WPF window, the badge reuses BCC's own existing chip idiom
(the rounded `Border` + small bold white text of the discipline legend directly
above the register). Same instinct the spec had — no new UI language — applied to
the language this window actually speaks.

---

## 5. PENDING-HUMAN-VERIFY

**Updated after the live run.** Items 1, 3 and 5–8 below were exercised in §4a and are marked
accordingly. What remains genuinely untested is everything needing a **second machine, a second
user, or a real network interruption** — none of which exist in this environment.

- ✅ **Exercised in §4a:** the push path, the delta with and without `since`, the initial-link
  sync, supersede, purge, the read-only hint, and the IPC surface.
- ❌ **Still open:** everything in items 2, 4, 6, 8, 9–13 below.

The single most important one still open is **item 2** (cross-tenant isolation over the wire).
It is unit-tested with a fake hub context, and the server now logs a refusal, but no second firm's
connection has ever been pointed at another firm's project on a live hub.

### Server (Slice A)
1. With the docker stack up (`cd Planscape.Server/docker && docker compose up -d`), sign in and
   `PUT /api/projects/{id}/documents/{docId}/state` to move a document WIP→SHARED. A client
   subscribed to `/hubs/document-sync` and joined to that project must receive `DocumentChanged`
   with `kind: "cde_transition"`.
2. **Cross-tenant refusal, the one that matters:** connect as a user in firm B, call
   `JoinProject` with firm A's project GUID, then have firm A transition a document. Firm B must
   receive **nothing**. (Unit-tested with a fake hub context; this is the over-the-wire confirmation.)
3. `GET …/documents/changed-since` with no `since` returns everything visible; with `since` set to
   just after an upload returns only what changed after it.
4. As a member whose `AllowedCdeStates` excludes `PUBLISHED`, confirm `changed-since` omits
   published documents — the sync surface must not be wider than the documents list.

### Companion (Slices B + C)
5. Start `Planscape.Companion.exe` with no arguments — a tray icon appears; it does not steal focus.
6. `--install-autostart`, sign out and back in, confirm it starts. Then `--uninstall-autostart` and
   confirm it stops starting. **Confirm nothing was written to HKLM** (`reg query
   HKLM\Software\Microsoft\Windows\CurrentVersion\Run` must not mention Planscape).
7. Point it at a live server, link a project, and confirm files land in
   `%USERPROFILE%\Planscape\{ProjectCode}\`.
8. **The real test of the push path:** with the Companion running on machine A, publish a document
   from the web app as a coordinator on machine B. The file must appear on A without anyone
   touching it.
9. Pull the network cable mid-session, publish two documents, restore the network. On reconnect
   both must arrive via the `changed-since` delta — **and the tray icon must have shown the quiet
   offline state throughout, with no toast** (§1c).
10. Open a synced PDF in Acrobat, publish a newer revision. The open file must be renamed to
    `{Name} (superseded {date}).pdf` rather than vanishing, and Acrobat must not error.
11. Set a machine clock forward 8 days and restart the Companion — superseded copies are purged,
    live files are not.
12. Confirm a `PUBLISHED` reference copy is read-only in Explorer, and that clearing the read-only
    attribute by hand is *not* fought over — it is a hint, not a lock.
13. Break the target folder (deny write) and force a sync: the tray must enter the **Error** state
    with a tooltip naming the folder, and recover on its own once the permission is restored.

### BCC / StingTools (Slice D) — none of this is verifiable without Revit

There is no Revit session in this environment, so **nothing below has been seen**.
The pipe client underneath it all IS proven headlessly (`--ping`, §4c); what is
unverified is the Revit-side wiring and how any of it looks.

14. **The Companion starts itself.** With `Planscape.Companion.exe` beside
    `StingTools.dll` and no Companion running, launch Revit. `StingTools.log` must
    say `Planscape Companion started (pid …)`. Launch Revit again with it already
    running — the log must say `already running` and **no second process** appears
    in Task Manager.
15. **Missing Companion is reported, not worked around.** Delete
    `Planscape.Companion.exe` from the plugin folder and start Revit: the log must
    say `not installed (…); document sync will not run`, Revit must start normally,
    and nothing should be launched from any other directory.
16. **The sync strip.** Open BCC → DELIVERABLES. The strip above the register
    shows a grey dot and "the Planscape Companion is not running" when it is
    stopped; a green dot and the last-sync time when it is running and idle.
    **Confirm the not-running case does not read as an error** — no red, no
    warning icon (plan §1c).
17. **Sync now.** With the Companion running, click *Sync now* → the strip says a
    sync was requested and `companion.log` shows a `Manual` pass. With it stopped,
    the button is disabled rather than silently doing nothing.
18. **Offline is not red.** Stop the API, leave the Companion running, click
    *Refresh* → the dot must be **grey**, not red. Then break something real (a
    revoked token) → the dot must be **red** and the strip must name the error.
19. **The Local badge.** See the caveat in §4d first — the register is empty in the
    normal flow, so this most likely shows nothing at all. If rows are present
    (added by hand in the editable register), a document synced as WIP shows a
    green `WORKING` chip and a SHARED/PUBLISHED one a grey `REF` chip, with the
    tooltip explaining the read-only hint. A row with no local copy shows an empty
    cell, not an empty chip.
20. **The auto-sync toggle in the web app.** `/projects/{id}` shows the "Document
    sync" card; unticking it says *Auto-sync paused*, and the copy makes clear that
    linked machines keep the project. Reload and confirm it stuck. This is
    server-verified in §4c but has never been **seen** rendering.
