# Phase 6B — did the capability layer actually open the gates, and who consumes it?

**Measured 2026-08-04** against `main` @ `085a98900`, on a live local stack
(Postgres + Redis + MinIO via `Planscape.Server/docker`), with real
authenticated HTTP. Every number below came from a request, not a reading of
the code. Where something was inferred rather than measured, it says so.

This document exists because [PR #540](https://github.com/beckykyomugisha/STINGTOOLS/pull/540)
is server-only: nothing visibly changed when it merged, so "did it work" could
not be answered by looking. It also answers the follow-on question — *which
clients actually call these endpoints* — because that turned out to be the more
surprising half.

---

## 1. #540 verified — 9 of 9 gated endpoints went from 403 to open

### Method

Two servers, one database, so both runs hit **identical rows**:

| | what | why it is a fair baseline |
|---|---|---|
| BEFORE | the docker `api` image, built 2026-07-31 | `git log --since=2026-07-25` shows **#540 (`d9d11095f`) is the only commit touching these controllers**, so this image is pre-#540 for the code under test |
| AFTER | `main` built from source, run on `:5099` | same DB, same JWT signing key, so tokens are interchangeable |

Fixture: a real user with tenant role **`Contributor`** (confirmed from the
login response — *not* Admin/Owner, or the probe would prove nothing), added to
a project with `ProjectRole = Manager`, `Iso19650Role = M`. The `M` is
deliberate: it is not one of `A`/`PM`/`BC`, so the **only** thing that can grant
access is the ProjectRole half of the predicate.

### Result

| Capability | Endpoint | BEFORE | AFTER |
|---|---|---|---|
| CurateProject | `POST /photo-albums` | **403** | 201 |
| CurateProject | `POST /photo-checklists` | **403** | 201 |
| CurateProject | `POST /distribution-groups` | **403** | 201 |
| CurateProject | `DELETE /saved-views/{another user's view}` | **403** | 204 |
| ApproveSitePhotos | `PUT /photo-policy` | **403** | 200 |
| ApproveSitePhotos | `POST /photo-share-links` | **403** | 200 |
| ApproveSitePhotos | `POST /photos/{id}/approve` | **403** | 200 |
| ApproveSitePhotos | `POST /photos/{id}/reject` | **403** | 200 |
| ApproveSitePhotos | `POST /photos/bulk-reclassify` | **403** | 200 |

**9 of 9 → 0 of 9.** No endpoint still 403s.

### Two controls, because "everything returns 200" is also what a broken-open gate looks like

**Negative control.** A `Contributor` on the same project, same server, is still
refused on all four endpoints tested — the gate discriminates.

**Role matrix.** Seven role combinations, curate probed with `POST
/photo-albums` and approve with `PUT /photo-policy`:

| ProjectRole | Iso19650Role | curate | approve | BEFORE (both) |
|---|---|---|---|---|
| Viewer | M | 403 | 403 | 403 / 403 |
| Contributor | M | 403 | 403 | 403 / 403 |
| Coordinator | M | **201** | 403 | 403 / 403 |
| Manager | M | **201** | **200** | 403 / 403 |
| Contributor | **PM** | **201** | **200** | 403 / 403 |
| Contributor | BC | **201** | 403 | 403 / 403 |
| Contributor | A | **201** | **200** | 403 / 403 |

Two things this pins down that a simple pass/fail would not:

1. **The `Iso19650Role = PM` row is the proof of the diagnosis.** Before #540, a
   member whose ISO role was literally `PM` — the exact person the old
   `ProjectRole == "PM"` gate was written for — was *still denied*. The gate
   matched nobody, not even its intended subject. #540 called this a
   wrong-field bug; this is that claim as a measurement.
2. **Curate and approve are genuinely different widths.** Coordinator and BC
   curate but cannot approve. The two predicates are being evaluated
   separately, not collapsed into one "is privileged" bit.

### Caveat

The BEFORE server is the pre-existing container rather than a rebuild of #540's
exact parent commit. The git history above justifies it, but it is an
**inference, not a rebuild**. If that distinction ever matters, rebuild
`d9d11095f^` and re-run.

---

## 2. Who actually calls these endpoints

The working premise had been that these controllers are clientless. **That is
true for two of them, and false for five** — mobile already consumes most of
the suite. Measured by grepping route literals across every client tree.

> ⚠️ The mobile API layer is `Planscape/src/api/endpoints.ts`, **not**
> `Planscape/app/`. `Planscape/app/site-photos/*.tsx` are screens that import
> from it. Grepping only `Planscape/app` reports zero and is wrong — that
> mistake was made and corrected while producing this table.

| Controller | planscape-web | mobile | viewer | Revit BCC |
|---|---|---|---|---|
| `PhotoAlbumsController` | — | **yes** — list/get/create/add/remove/lock | — | added by #550 |
| `PhotoChecklistsController` | — | **yes** — list/get/fulfil | — | added by #550 |
| `PhotoShareController` | — | **yes** — create | — | — |
| `PhotoPolicyController` | — | **yes** — get | — | `SitePhotosAdminSubTab` |
| `DistributionGroupsController` | — | **yes** — list/create | — | ⚠️ **local file only — see §3** |
| `SitePhotos` / `SitePhotosExt` | — | **yes** (11 screens) | capture | #550 |
| `SavedViewsController` | — | — | — | — |

### What this changes

- **`SavedViewsController` is the only fully clientless controller** of the
  seven. Nothing on any surface reads or writes it.
- **`planscape-web` consumes none of the seven.** The web app — described as the
  primary coordination surface — has no photo-suite client at all.
- Mobile's calls were **present but non-functional** for exactly the users who
  needed them: every one would have 403'd for a Manager before #540. So this
  was not "no client", it was "a client wired to a gate that never opened".
  Worth remembering when judging whether a feature is dead — a caller can exist
  and still never succeed.

---

## 3. ⚠️ `DistributionGroups` has two stores that never meet

`StingTools/Docs/Workflow/DistributionGroups.cs` is a **pure local-file store**:

```csharp
// Stored in _BIM_COORD/distribution_groups.json
return JsonConvert.DeserializeObject<List<DistributionGroup>>(File.ReadAllText(path))
File.WriteAllText(tmp, JsonConvert.SerializeObject(groups ...))
```

`File.ReadAllText` / `File.WriteAllText` and **no HTTP anywhere in the file**.
Meanwhile `DistributionGroupsController` offers full server-side CRUD, mobile
calls it, and — since #540 — a Manager can now reach it.

So the same concept has two disconnected stores: the plugin writes groups to a
JSON file beside the model that the server never sees, and mobile reads groups
from a server the plugin never contacts. Neither surface can see the other's
data, and nothing reports an error, because from each side its own store looks
fine.

This is the **same shape** as the already-known BCC "Member Directory → Save"
bug (wrote a local JSON file the server never saw, so rows silently vanished on
refresh) and as the site-photo faking that #550 removes. Three instances is a
pattern, not a coincidence: **assume any BCC panel that persists something is
writing locally until its HTTP call is shown.**

Not fixed here — it changes plugin persistence behaviour and needs a Revit
build plus a decision about migrating existing `_BIM_COORD/distribution_groups.json`
files. Filed as the next unit of work.

---

## 4. `SavedViewsController` — contract as measured

Derived by exercising a running server, not by reading the controller.
`api/projects/{projectId:guid}/saved-views`, all `[Authorize]` +
`RequireProjectMemberAsync`.

| Verb | Path | Status | Body |
|---|---|---|---|
| GET | `/` | 200 | `{items:[…], total, page, pageSize}` |
| POST | `/` | **201** | `{id, name, createdAt}` — *not* the full object |
| GET | `/{id}` | 200 | full entity (see below) |
| DELETE | `/{id}` | **204** | empty |

**List item** (heavy fields deliberately excluded):

```json
{"id":"…","name":"…","description":"…","modelId":null,
 "capturedByUserId":"…","capturedByName":"BIM Coordinator",
 "createdAt":"2026-08-04T07:58:19.963658Z",
 "linkedMeetingId":null,"linkedActionItemId":null,"hasThumbnail":false}
```

`stateJson` and `thumbnailB64` are **absent from the list** and present only on
`GET /{id}` — a client must fetch detail to restore a view.

**Errors**, both RFC 9110 ProblemDetails:
- missing `stateJson` → `400` `{"errors":{"StateJson":["The StateJson field is required."]}}`
- unknown id → `404`

**Create requires** `name` (≤120 chars) and `stateJson`; optional
`description`, `modelId`, `thumbnailB64`, `linkedMeetingId`, `linkedActionItemId`.

**Delete authority** — creator may delete their own; anyone else needs
`CanCurateProject` (verified: a Manager deleting another member's view returned
**403 before #540, 204 after**).

### Two inconsistencies a client author will hit

1. **`GET /{id}` returns the raw entity**, including `tenantId` and nulled
   navigation properties `project`, `capturedByUser`, `linkedMeeting`. The list
   returns a tidy projection. The two shapes disagree, so a client cannot use
   one type for both.
2. **`POST` returns only three fields**, so a client that wants the created row
   in list shape must re-fetch.

Neither is changed here — both alter an existing response shape.

---

## Reproducing

Scripts used are throwaway probes, not committed. To redo it:

1. `cd Planscape.Server/docker && docker compose up -d`
2. `tools/Seed-LocalSitePhotos.ps1` for a fixture project
3. Create a user via `POST /api/admin/users` with `role: Contributor`, add as a
   project member with `projectRole: Manager`, log in **as that user**
4. Probe the endpoints in §1 and record status codes

Two local-dev-DB-only changes were needed and are worth knowing about: the demo
tenant had **409 active users against a `MaxUsers` of 50**, so every
`POST /api/admin/users` returned `400` until the cap was raised; and
`AdminController.CreateUser` preserves email case while lookups are
case-sensitive, so mixed-case fixture emails fail to log in afterwards.
