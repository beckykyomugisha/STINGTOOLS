# Offline-first design

Planscape's mobile app is designed for the connectivity reality of East African construction sites — variable 3G, dead zones in basements and stairwells, rural sites where the nearest tower is 5 km away. "Offline-first" means we don't degrade gracefully when the signal drops; we operate the same way whether you're online or off, and reconcile when connectivity returns.

## Why this matters

A site coordinator might walk into a basement plant room, raise three RFIs with photos, mark four NCRs as resolved, and check off two stage-gate criteria — all without ever seeing a 3G bar. The traditional cloud-only workflow would lose every one of those actions. Planscape doesn't.

This isn't a feature we bolted on; it's a design constraint that shaped the data model. Every action is structured as an idempotent, append-only operation that can be queued, replayed, and merged.

## What works offline

| Action | Offline? |
|---|---|
| View the federated 3D model | ✓ (cached after first load) |
| Browse and filter issues | ✓ |
| Create new issues with photos and voice notes | ✓ |
| Update existing issues — status, comments | ✓ |
| GPS-pin an issue to a location | ✓ |
| Scan QR-coded asset tags | ✓ (asset data cached) |
| Look up tag history (TAG7) for an asset | ✓ |
| Transition documents through CDE states | ✓ (subject to permission) |
| Check off stage-gate criteria | ✓ |

## What requires connectivity

| Action | Offline? |
|---|---|
| Initial sync of a new project | ✗ |
| Real-time chat / Crisp support | ✗ |
| Receiving push notifications | ✗ (queued by the OS, delivered on reconnect) |
| Sending a transmittal email to external recipients | ✗ (queued, sent on reconnect) |
| Federated clash detection (server-side) | ✗ |

## How the queue works

When you make a change offline, the app:

1. Writes the change to the local SQLite store (so the UI reflects it immediately).
2. Appends an action record to a JSON queue, with: action type, target entity, payload, timestamp, your user id, and a client-generated UUID.
3. Marks the entity as "pending sync" in the UI — a small amber dot next to the issue, document, or criterion.

When connectivity returns:

1. The app drains the queue in FIFO order, retrying with exponential backoff on transient failures.
2. Each successful action is removed from the queue and the amber dot turns green.
3. Each rejected action (server validation failure, conflict) routes to the [conflict triage](../concepts/cde.md) screen for human review.

## Conflict resolution

The simplest case — two people offline, both update the same issue — resolves with **last-write-wins** based on server-side timestamp. The app shows both versions side-by-side in the timeline so neither edit is lost.

For more nuanced conflicts — same document transitioned to two different states by two different people — the app routes to the conflict triage screen. There, the project's BIM Information Manager sees a diff of the conflicting actions and picks which one wins. Both actions remain in the audit log; only one becomes the new state of record.

The audit log itself is **append-only and never conflicts** — both actions are written to the chain, in the order the server received them, with the loser flagged as `superseded_by` the winner.

## Sync indicator

The status pill at the top of every screen tells you where you are:

- **Green checkmark** — fully synced, no pending actions
- **Amber spinner** — sync in progress
- **Amber dot with number** — N actions queued offline, will sync when connection returns
- **Red triangle** — connection error or unresolved conflict; tap for details

## Storage and quotas

The local SQLite store is sized at 200 MB per project by default. Photos are stored at original resolution offline and uploaded at full resolution when sync runs. If you exhaust local storage, the app rotates oldest cached model data first and retains queued action records — you never lose unwritten work.

For projects with very large photo volumes, configure **Settings → Storage → Photo compression** to compress on-device after capture.

## Revit plugin offline behaviour

The Revit plugin caches the last-known project state in `_BIM_COORD/` under your project root. Authors can keep tagging, validating, and editing without an internet connection; the plugin queues sync operations and retries when reachable. SEQ counters live in the local sidecar (`<project>.sting_seq.json`) so sequence numbers stay continuous across offline sessions.

## Next steps

- [Install the mobile app](../quickstart/mobile.md)
- [Issue an RFI](../howto/rfi.md) — try raising one offline and watching it sync
