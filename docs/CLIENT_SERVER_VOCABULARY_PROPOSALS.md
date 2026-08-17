# Two client/server vocabulary mismatches — proposals, not implementations

**Status: PROPOSAL. No behaviour is changed by this document.**

Both items below are the eleven-dead-gates bug on the client side, where #540 could not
reach: the client re-derives a rule the server owns, gets it wrong, and nothing fails
loudly. Both fixes need a contract decision — a third capability (#647) or an additive
response field (#633) — and both of those are propose-first by existing convention.

Everything stated as measured was measured. Where a mechanism is proven but the trigger
is not, that is said explicitly.

---

## A — #647 · project settings: `canEdit` is derived from the wrong field

### What the server actually enforces

`Planscape.Server/src/Planscape.API/Controllers/ProjectSettingsController.cs:147-150`
(`PUT /api/projects/{projectId}/settings`):

```csharp
var member = await _db.ProjectMembers
    .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId && m.IsActive);
if (member == null || (member.Iso19650Role != "K" && member.Iso19650Role != "C"))
    return Forbid();
```

### What the client predicts

`Planscape/app/project-settings/index.tsx:35` and `:59`:

```ts
const ADMIN_EDIT_ROLES = new Set(['Admin', 'Owner', 'PM', 'BIM_Manager', 'BIMManager']);
...
setCanEdit(access.bypassesAcl || ADMIN_EDIT_ROLES.has(access.projectRole ?? ''));
```

`ProjectRole` and `Iso19650Role` are **different columns with disjoint vocabularies**.
The client is not approximating the server's rule; it is reading a different field.

### Three ways this is wrong

1. **A tenant Owner gets an editable form and a guaranteed 403.** `bypassesAcl` is true
   for them, so the client enables every switch. The server has **no tenant Admin/Owner
   bypass at all** — an Owner with no `ProjectMember` row hits `member == null` and is
   refused. This is the direction the issue describes, and it is worse than described:
   the missing bypass is a server-side asymmetry, not just a client mis-prediction.

2. **A K or C member gets a read-only form for something they may do.** `Iso19650Role`
   is `K`/`C` while `ProjectRole` is typically `Contributor` — not in `ADMIN_EDIT_ROLES`,
   so the switches are disabled and a lock banner is shown to someone the server would
   have allowed.

3. **The copy already knows the rule the gate does not.** The 403 alert says *"Only BIM
   Managers (role K) and Coordinators (role C)"* — correct — while the gate three lines
   above tests `projectRole`. The footer compounds it by calling `K`/`C` a *"project
   role"*, which is the exact conflation that produced the bug.

### Proposed shape — a third capability

Named for **what it is**, never for the roles that currently satisfy it:

> **`canEditProjectAdminSettings`** — the caller may change project-level administrative
> settings, i.e. the `PUT /api/projects/{projectId}/settings` surface.

Added to the existing endpoint:

```jsonc
GET /api/projects/{projectId}/members/capabilities
{
  "projectId": "…",
  "userId": "…",
  "canCurateProject": true,
  "canApproveSitePhotos": false,
  "canEditProjectAdminSettings": false   // ← proposed
}
```

`ProjectMembersController.GetMyCapabilities` is explicit that this is a propose-first
change:

> *Deliberately two booleans, matching the two predicates that actually exist on
> `ProjectRoles`. A third does not get added inline — it goes through the same
> propose-first step these two did.*

Hence this document rather than a patch.

**Implementation note for whoever builds it.** The capability must be resolved by the
*same predicate* `UpdateSettings` enforces, extracted into one method both call. A
capability that re-implements the gate is the bug this layer exists to prevent, moved
one level up.

### The decision this needs from the owner

**Should a tenant Admin/Owner be able to edit project settings?**

Today they cannot — there is no bypass in `UpdateSettings`. Every other capability
resolves Admin/Owner without a member row. So the honest answer changes the work:

- **No, keep it K/C only** → the capability is a pure read of the existing predicate.
  Inert, no behaviour change, safe.
- **Yes, Admin/Owner too** → that is a **server authority change** to `UpdateSettings`,
  widening who may write project settings. It should be decided and shipped separately
  from adding the capability, not smuggled in as part of it.

### Interim, shippable now, no contract change

**Attempt-then-report.** Render the form enabled and let the server decide; on refusal,
show the server's own reason using the forbidden treatment from #558 (the mobile half is
#645 — reuse it, do not invent a second treatment).

This is strictly better than the current state because it is wrong in *neither*
direction: nobody is locked out of something they may do, and nobody is told an action
succeeded when it was refused. It is not as good as the capability, because the user
still discovers the refusal by attempting.

---

## B — #633 · CDE transitions: mobile predicts the approval gate and disagrees

### The two lists

| | |
|---|---|
| **Mobile** — `Planscape/app/(tabs)/documents.tsx:36` | `WIP->SHARED`, `SHARED->PUBLISHED` |
| **Server** — `DocumentsController.cs:69` | `SHARED->PUBLISHED`, `PUBLISHED->SUPERSEDED` |

They agree on exactly one entry out of three.

### What each divergence actually does

**`WIP->SHARED` — mobile says approval, server says no. This one is reachable and it
breaks the transition entirely.**

Because `requiresApproval` returns true, `handleTransition` takes the approval branch and
**never calls `transitionCDE`**. So WIP→SHARED — the most common transition in the whole
state machine — cannot be performed from mobile at all. It is not a confusing message; it
is a dead end.

Had the request reached the server it would have been rejected anyway
(`DocumentsController.cs:1089`):

```csharp
if (!ApprovalRequiredTransitions.Contains(transition))
    return BadRequest($"Transition {transition} does not require approval");
```

*Labelling: this guard clause is unambiguous on read, but I did **not** reproduce the 400
live — the routing bug below returns 404 first, so the 400 is currently unreachable. The
mechanism is proven from source; the trigger is masked.*

**`PUBLISHED->SUPERSEDED` — server requires approval, mobile does not know. Not currently
reachable.** Mobile's type is `CDEStatus = 'WIP' | 'SHARED' | 'PUBLISHED' | 'ARCHIVE'`
(`src/types/api.ts:161`) — `SUPERSEDED` does not exist on the client, so no UI can request
it. Real divergence, no current impact. Worth recording so it is not "fixed" by adding
`SUPERSEDED` to mobile without also handling its approval gate.

### A more basic bug found while measuring this

Mobile's `requestDocumentApproval` (`src/api/endpoints.ts:1005`) posts to:

```
POST /api/projects/{projectId}/documents/{documentId}/approvals
```

The server has no such route. The only approval-request route is
(`DocumentsController.cs:1076`):

```
POST /api/projects/{projectId}/documents/{docId}/approval-request
```

`git grep` over `Planscape.Server/src/**/*.cs` finds **no route containing `approvals`**.
The approval branch therefore 404s regardless of which list is right.

Corroborated live against the local API. The discriminator is the response body:

```
approvals          HTTP 404   (empty body — no route matched)
approval-request   HTTP 404   {"type":"…rfc9110#section-15.5.5","title":"Not Found",…}
```

An unmatched route returns a bare 404; a matched route returning `NotFound()` returns
ProblemDetails. *Caveat: the local API image predates #547, so its controller may not
match current source. The route-absence conclusion rests on `git grep` over current
`main`; the live probe only corroborates it.*

**This part needs no decision.** Pointing the client at the route that exists is a pure
client bug fix with no contract implication.

### The fix, and what it requires

Per the brief: **delete mobile's list. The server wins.** Not reconcile — reconciling
leaves two lists that must be kept in step, which is the same failure one edit later.

Deleting it raises the real question: *how does mobile know whether to call `transition`
or `approval-request`?* Three shapes, in order of preference.

#### Shape 1 (recommended) — the document says what can be done to it

An **additive** field on the existing document response:

```jsonc
{
  "id": "…",
  "cdeStatus": "SHARED",
  "allowedTransitions": [                          // ← proposed
    { "to": "PUBLISHED", "requiresApproval": true  },
    { "to": "WIP",       "requiresApproval": false },
    { "to": "WITHDRAWN", "requiresApproval": false }
  ]
}
```

Why this one:

- It introduces **no new rule**. It is computed from the two static dictionaries the
  controller already owns — `ValidTransitions` and `ApprovalRequiredTransitions` — so the
  server publishes what it already decides rather than growing a second source of truth.
- It kills a **second** client guess nobody has filed yet: mobile currently has no idea
  which transitions are even *valid*, only which it thinks need approval. It renders
  options from its own assumptions.
- Additive to a response the client already fetches — cheaper and lower-risk than a new
  endpoint, and no extra round-trip.

It is still a **response-shape change**, which is STOP-and-ask. Hence this document.

#### Shape 2 — attempt-then-branch, no contract change

Always call `transitionCDE`; if the server refuses *because approval is required*, offer
"Request approval". Requires the refusal to be **machine-distinguishable** — a stable
code, not prose — which is itself a response-shape question, so it only looks cheaper.

#### Shape 3 — a dedicated read

`GET /api/projects/{id}/documents/{docId}/transitions`. Cleanest separation, but a **new
endpoint** (STOP-and-ask), plus a round-trip per document. Preferred only if
`allowedTransitions` on every document in a large list is judged too heavy.

### Recommended order

1. **Fix the route** (`/approvals` → `/approval-request`). No decision needed; the
   approval branch cannot work until this lands.
2. **Decide Shape 1 vs 3.**
3. **Delete `TRANSITIONS_REQUIRING_APPROVAL`** and render from the server's answer.

Doing 3 before 2 would mean deleting the list with nothing to replace it — mobile would
have to attempt blindly, which is Shape 2 by omission rather than by choice.

---

## Summary of what is being asked

| # | Decision needed | Blocking |
|---|---|---|
| #647 | Add `canEditProjectAdminSettings` to `members/capabilities`? | Yes — third capability is propose-first by that endpoint's own contract |
| #647 | Should tenant Admin/Owner be able to edit project settings **at all**? Today they cannot. | Yes — changes server authority, decide separately |
| #633 | `allowedTransitions[]` on the document response (Shape 1) vs a new endpoint (Shape 3)? | Yes — response-shape / new-endpoint change |
| #633 | Fix `/approvals` → `/approval-request` | **No** — pure client bug, ship it |
