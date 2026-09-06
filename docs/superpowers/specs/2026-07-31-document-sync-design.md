# Document sync design — cloud documents on an Author's local disk via StingTools/BCC

**Status:** Approved by Sting, 2026-07-31. Not yet implemented — no code exists for anything in
this document. Next step is an implementation plan (writing-plans), not this doc.

## Problem

An Author works in Revit against local files. Coordinators publish documents (drawings, PDFs,
specs) to the cloud (Planscape.Server, via `DocumentsController`). Today the only way those
documents reach an Author's machine is a manual download through the web app. The ask: a
coordinator's upload should reach a local folder on the Author's machine automatically, integrated
with StingTools/BCC, without reinventing what a general-purpose file-sync tool already does badly
for this exact problem.

## Prior art consulted

Autodesk Desktop Connector (ACC/BIM 360's own answer to this problem) — a virtual mapped drive,
selectively synced per project. Its recurring real-world failure, even in a mature Autodesk
product: files get stuck "checked out" in the cloud after the local app closes, sometimes
requiring manual unlock, and a conflicting cloud revision landing mid-edit can silently send the
local copy to the Recycle Bin rather than preserving it visibly.
([Autodesk product page](https://www.autodesk.com/products/desktop-connector/overview),
[lock issue reports](https://forums.autodesk.com/t5/bim-360-support-forum/desktop-connector-v16-issue-with-locking-revit-files/td-p/11623259))

The lesson taken from that: don't use OS-level file locking as the source of truth for who owns an
edit. This repo already has something better to use instead (see below).

## What already exists and this design builds on, not around

- **A full ISO 19650 CDE state machine**, server-side, already implemented:
  `DocumentRecord.CdeStatus` (`WIP` / `SHARED` / `PUBLISHED` / `ARCHIVE`), `SuitabilityCode`
  (S0–S7, CR, AB), per-revision state history (`DocumentRevision.CdeStateAtRevision`), and
  per-member visibility scoping (`ProjectMember.AllowedCdeStates`,
  `SuitabilityTransitionRule`). This is the locking/ownership/visibility model for sync — no new
  permission system gets invented.
- **`StingTools.LinkHandler` → `StingLink.exe`** (landed today, commit `3e0a60f6e`,
  `docs/PLANSCAPE_PROTOCOL.md`): a standalone, non-Revit .NET 8 exe that Windows launches on
  `planscape://` activation. It drops the incoming URL into `%LOCALAPPDATA%\STING\link-inbox` and
  exits — it does not stay running. Draining the inbox happens inside StingTools via
  `PlanscapeLinkWatcher`, an `IIdlingJob`, which only runs while Revit is open.
  **This does not solve "sync while Revit is closed"** — it was never meant to; it solves
  protocol-link activation, which is a different problem with a different lifetime requirement.
  Leave it exactly as built. Do not extend or repurpose it into the sync engine.
- **SignalR is proven working** in this codebase (`MeetingHub`, used all through Track B). Reuse
  the pattern, not the hub.

## Architecture

Two small, single-purpose standalone processes, not one:

1. **`StingLink.exe`** (exists, unchanged) — protocol link activation only, short-lived, drop-file
   handoff to Revit's idling loop.
2. **Planscape Companion** (new) — a standalone .NET tray app, starts on Windows login, stays
   running for the whole session regardless of whether Revit is open. Owns exactly one job: the
   document sync engine. StingTools/BCC talks to it over a local loopback HTTP endpoint (or named
   pipe) for status display and the manual "Sync now" trigger — BCC never runs sync logic itself,
   it only asks the Companion to do things and reads its status.

Rationale for keeping these separate rather than merging: `StingLink.exe` is deliberately minimal
and short-lived by design (documented reasoning: a pipe's only job would be handing work to an
idling job that already exists, so a file inbox was simpler). Retrofitting it into an always-running
service would fight that design rather than extend it. Two small processes, each doing one thing,
matches this codebase's existing taste better than one merged one.

## Sync model

**One-way, read-down only.** No bidirectional sync, no client-authored uploads through this
pipeline (uploads already go through the existing web/API path). Gated entirely by the CDE state
machine already described above:

- `PUBLISHED` / `SHARED` documents an Author can see (per `AllowedCdeStates`) sync down as
  **read-only reference copies**. No lock needed — nobody edits a reference copy, so there is
  nothing to conflict over.
- `WIP` documents specifically checked out to an Author sync down as the editable working copy.
  "Checked out" is a fact recorded in the CDE state machine (already has an owner, already has an
  audit trail) — **never an OS-level file lock**. This is the specific mechanism that breaks in
  Desktop Connector; it is not reproduced here.

## Trigger

Push, not polling, as the normal path:

- A `DocumentSyncHub` (SignalR, same pattern as `MeetingHub`) pushes a "document changed" event to
  every connected Companion instance for that project the moment a coordinator changes a document's
  state or a new revision is published.
- Polling exists only as the **reconnect fallback**: `GET /api/projects/{id}/documents/changed-since
  ?since={lastKnownSyncUtc}` — a delta query, never a full re-scan, used once when the Companion
  reconnects after being offline (laptop closed, VPN down, etc.).
- A project newly linked on a machine triggers a one-time initial sync using the same
  changed-since-style call with `since` unset (i.e. "everything currently visible").
- Manual "Sync now" (see Flexibility below) calls the identical sync routine on demand.

All four triggers feed one code path; only what invokes it differs.

## Scope pulled per project

**Latest revision only**, filtered by `AllowedCdeStates` — not full history, by default. Full
revision history for a specific document is an explicit, per-document "download full history"
action a user takes deliberately, never a default that silently grows on every machine.

## Flexibility: per-project auto/manual toggle

One toggle per project (not per-document — too fine-grained to manage day to day):
**"Auto-sync this project"**, on by default. Off means the Companion still knows the project is
linked but does nothing until the user explicitly triggers "Sync now" from BCC or the Companion's
tray icon. Same underlying sync routine either way; the toggle only gates whether the push
trigger is allowed to fire automatically.

## Conflict / external-edit safety net

A read-only reference copy that gets superseded by a newer revision while something external
(Acrobat, Word, anything that isn't Revit/BCC) might have it open: rename the outgoing file to
`{Name} (superseded {date}).{ext}` rather than overwriting or deleting it outright, auto-purge
those renamed copies after 7 days. Cheap, self-cleaning, avoids the "file vanished while I had it
open" complaint without building real conflict-resolution UI for a case that, by construction
(read-only reference copies are never edited locally), isn't a true edit conflict.

## Local folder convention

`%USERPROFILE%\Planscape\{ProjectCode}\`, overridable per project. Deliberately not buried in
`%APPDATA%` — Authors need to find and open these files from Explorer or Revit's own file-open
dialog. The override mechanism follows the same pattern already established by
`PlanscapeServerClient.MachineSettingsPath` (a small JSON settings file, not a new convention).

## Checkout-state visibility

- **In BCC**: a badge on the relevant Documents grid row, reusing the same visual pattern already
  used for the "Live" meeting badge — no new UI language introduced.
- **In the Companion tray icon**: a lightweight count (e.g. "3 checked out") for at-a-glance status
  even when Revit is closed, click-to-expand to a short list. Not a second full UI surface to
  maintain — status only, no editing happens here.

## Explicitly out of scope for this design

- Bidirectional sync / local edits flowing back to the cloud through this pipeline.
- Any OS-level file locking.
- Extending or repurposing `StingLink.exe` — it stays exactly as built for protocol links only.
- Per-document (as opposed to per-project) auto/manual granularity.
- A Windows Service running independent of any logged-in user (this is a per-user, start-on-login
  tray app, not a machine-wide service) — revisit only if a genuine multi-user-per-machine need
  shows up later.
- Full revision history sync by default.

## Open items for the implementation plan to resolve, not this spec

- Exact Companion↔BCC IPC transport (loopback HTTP vs named pipe) — pick whichever is less new
  code given what StingLink.exe's inbox mechanism already proved out.
- Whether the Companion needs its own installer step or piggybacks on the existing StingTools
  plugin installer/first-run flow.
- Telemetry/error visibility when a sync fails silently in the background (a toast the next time
  BCC opens? a persistent tray icon state? — needs a decision, not a default).
