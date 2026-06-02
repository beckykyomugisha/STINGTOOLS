# Mobile deferred features (Prompt 18)

Prompt 18 drove `Planscape/` `tsc --noEmit` from 12 → **0** so the mobile
typecheck could become a CI gate (`.github/workflows/contract-drift.yml` →
`mobile-typecheck`). Most of the 12 errors were mobile code calling an
*imagined* API; those were **aligned** to the real API or **built** for real
(per the product decision recorded in the Prompt-18 conversation):

| Feature | Route taken |
|---|---|
| Offline replay: comment / diary / stage-signoff | **Aligned** to the real endpoints (`addIssueComment`, `createSiteDiary`/`updateSiteDiary`, `signOffStageCriterion(…, met: bool)`) |
| Offline replay: idempotency (issue create/update, meeting-action add) | **Built** — `X-Idempotency-Key` header on the client args + server-side dedupe (`IdempotencyRecord` table + `IdempotencyGuard`) |
| 3D zoom-to-element (`ModelViewerHandle.selectAndZoom`) | **Built** — handle method + WebView viewer handler (find mesh by GUID → frame camera → highlight) |
| Healthcare live pressure (`/hubs/healthcare`) | **Built** — mobile `realtimeClient` per-hub connector + wired `HealthcareController.PostPressureLog` to call the (already-existing) `HealthcareHub.BroadcastPressureReading` |

The remaining items below were **deferred** (route 3): the offline-replay
handlers were dead code (no screen enqueued them), so removing them is **not a
user-visible regression** today — there is no shipping offline behaviour to
lose. Re-add when the underlying feature is built.

---

## 1. Offline 3D-pin replay — `PIN_PLACE` / `PIN_DELETE`

- **Removed from:** the `OfflineAction['type']` union (`src/types/api.ts`) and
  the replay switch (`src/utils/offlineQueue.ts`). See the `TODO(offline-pins)`
  markers.
- **Why deferred:** there is **no issue-pin CRUD endpoint anywhere** — neither
  a mobile `placeIssuePin`/`deleteIssuePin` client function nor a server route.
  No screen enqueued these actions, so nothing in today's UI relied on them.
- **To build:**
  1. Server: add pin CRUD endpoints (e.g. `POST/DELETE
     /api/projects/{projectId}/issues/{issueId}/pins`) + a `Pin` entity.
  2. Client: add `placeIssuePin` / `deleteIssuePin` to `src/api/endpoints.ts`.
  3. Re-add `'PIN_PLACE' | 'PIN_DELETE'` to the `OfflineAction` union and the
     two replay cases in `offlineQueue.ts`.
  4. Enqueue the actions from the 3D pin-placement UI (the viewer already emits
     `placeIssue` / `pinTap` — wire those to `enqueue('PIN_PLACE', …)` when
     offline). Use the `X-Idempotency-Key` pattern so replay is dedupe-safe.

## 2. Standalone offline checklist fulfilment — `FULFIL_CHECKLIST_ITEM`

- **Removed from:** the `offlineQueue.ts` replay switch (it was never a member
  of the `OfflineAction` union — its presence was the original TS2678 error).
  See the `TODO(offline-checklist)` marker.
- **Still works today:** checklist fulfilment that follows a queued
  `CAPTURE_SITE_PHOTO` is performed inline in the `CAPTURE_SITE_PHOTO` replay
  case (it calls the real `fulfilChecklistItem` endpoint after the photo
  lands). Only the *standalone* "fulfil an existing item offline" path is
  deferred.
- **To build:** add `'FULFIL_CHECKLIST_ITEM'` to the `OfflineAction` union, a
  dedicated replay case (the `fulfilChecklistItem` endpoint already exists), and
  an enqueue site on the checklist screen for the offline case.

---

## Operational notes (built features)

- **Idempotency EF migration.** `20260602000000_IdempotencyRecords` is
  hand-authored without a `.Designer.cs` and **carries no `[Migration]`
  attribute**, matching this repo's convention (see
  `20260601000000_CrossHostIdentityFields`). Dev/local stacks build the schema
  from `OnModelCreating` via `RelationalDatabaseCreator.CreateTables()`; the
  prod migration pipeline (backlog **P3-2**) must apply it. The
  `PlanscapeDbContextModelSnapshot` is intentionally left stale per that same
  backlog item — `dotnet ef migrations add` against this project re-scaffolds
  unrelated pending drift, so migrations here are written by hand.
- **Comment offline replay is at-least-once.** `POST_COMMENT` was aligned to
  `addIssueComment`, which does not carry an idempotency key (idempotency was
  scoped to issue create/update + meeting-action add). A replayed comment can
  therefore post twice on a flaky reconnect. Extend `X-Idempotency-Key` +
  server dedupe to the comments endpoint if duplicate comments become an issue.
