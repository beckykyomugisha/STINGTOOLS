# Document / deliverable alignment — gap table with verdicts

**Measured 2026-08-01 on `main` @ `a5109e3e5`.** Report only. Nothing here is implemented.

Every row carries a verdict: **rename**, **wire**, **document**, or **fix**.
**No row says "delete".** Absence of a grep hit is not evidence of no consumers — see
*Evidence standard* below.

---

## ⚠️ Corrections to the first draft of this audit

The first pass inventoried callers in `StingTools/`, `planscape-web/lib/data.ts` and (claimed)
`Planscape/`. It did **not** in fact grep the mobile app's endpoint module. Re-measured against
`Planscape/src/api/endpoints.ts`, three headline claims were wrong:

| First-draft claim | Actual |
|---|---|
| "Seven server controllers have no client on either side" | **Five**, not seven. `Deliverables` and `StageGates` have a full mobile client. |
| "The server's deliverable/stage-gate model is effectively dead data" | **False.** Mobile lists, creates, updates and transitions deliverables (`endpoints.ts:1456,1465,1478,1497,1508`) and lists / seeds / decides stage gates (`endpoints.ts:1407,1411,1420`). |
| "Approval chains are fully built and entirely unreachable" | **Half wrong.** There are **two** approval mechanisms. Mobile calls the `DocumentsController` one (`endpoints.ts:1005,1012`). The separate `ApprovalChainsController` has no caller found. |

Claims that survived re-measurement: the "deliverable" naming collision, transmittals being
write-only, the `"PM"` role gate, and the seat-count inversion.

### Evidence standard

"No caller found" here means: no hit across `StingTools/`, `planscape-web/`, `Planscape/` (mobile)
and `StingBridge/`. It does **not** cover external integrations, partner clients, or anything
calling the API directly. **Deleting an endpoint requires positive evidence of no consumers —
server access logs over a representative window — not the absence of a grep hit.** Every orphan
row below is therefore *wire* or *document*, never *delete*.

---

## 1. Live bugs — fix these, they are not cleanup

### L1 — "Manager" and "PM" are the same concept spelled twice

> **Correction.** An earlier draft said "nothing ever writes `PM`". That was wrong in an
> important way: `ProjectRole` **is** client-writable and unvalidated
> (`ProjectMembersController.cs:418` — `if (req.ProjectRole != null) member.ProjectRole = req.ProjectRole;`),
> so any API caller can set it to `"PM"`. The defect is not "impossible to write" but
> **"no first-party UI writes it, and the gates depend on it."**

The web members grid offers exactly six `ProjectRole` values
(`planscape-web/app/projects/[id]/members/page.tsx:23`):

```
['Viewer', 'Contributor', 'Coordinator', 'Manager', 'Owner', 'Admin']     ← no "PM"
```

`GET /members/roles` (`ProjectMembersController.cs:548-569`) *does* return `PM` — but that list is
the **ISO 19650 vocabulary** (`A, PM, BC, BA, AR, SE, ME, CE, QS, CA, CT, SC, FM, OM, CL, M, V, Z`)
and it feeds the **`iso19650Role`** column, a different field
(`planscape-web/lib/data.ts:428` types it `Iso19650Role[]`; the grid saves it via
`updateMemberRole(..., { iso19650Role: v })` at `page.tsx:119`).

**So the seven gates compare `ProjectRole` against a value that only ever appears in the
`Iso19650Role` vocabulary.** A user picks "PM" in the UI, it lands in `Iso19650Role`, and the gate
reads `ProjectRole` — which is `"Contributor"`. Gate closed, for everyone, silently.

### Full enumeration — every role string the server compares against

Prerequisite for choosing a canonical set. `ProjectRole` comparisons only:

| Value | Sites |
|---|---|
| `"PM"` | `DistributionGroupsController.cs:202`, `PhotoAlbumsController.cs:395`, `PhotoChecklistsController.cs:320`, `PhotoPolicyController.cs:103`, `PhotoShareController.cs:122`, `SavedViewsController.cs:136`, `SitePhotosController.cs:874,884`, `SitePhotosExtController.cs:610`, `BackgroundJobs.cs:314`, `DailyPhotoDigestJob.cs:157`, `P6LiveLinkService.cs:288` |
| `"Manager"` | `WeeklyDigestJob.cs:48`, `CdeContainersController.cs:255` |
| `"Admin"` | `WeeklyDigestJob.cs:49`, `CdeContainersController.cs:255`, `BackgroundJobs.cs:278` |
| `"Owner"` | `WeeklyDigestJob.cs:49`, `CdeContainersController.cs:255`, `BackgroundJobs.cs:278` |
| `"Coordinator"` | `WeeklyDigestJob.cs:49`, `CoordinatorWorkloadJob.cs:39` |
| `"BimManager"` | `CoordinatorWorkloadJob.cs:39` |
| `"ClientGuest"` | `DailyPhotoDigestJob.cs:126` |
| `"Author"` | `TenantAdminController.cs:51`, `QuotaGuardService.cs:76,77` |
| `"Contributor"` | (written, never compared) |

**Written by first-party paths:** `"Owner"` (`AuthController.cs:1231`), `"Manager"`
(`ProjectsController.cs:172`), `"Contributor"` (defaults — `ProjectMembersController.cs:140,171,340`),
`"Coordinator"`/`"Contributor"` (demo seed — `SeedData.cs:275,304`), plus unvalidated `req.ProjectRole`
(`:418`) and `AccessProfile.DefaultProjectRole` (`AccessProfilesController.cs:75`).

**Never written by any first-party path:** `"PM"`, `"Author"`, `"BimManager"`, `"ClientGuest"`.

Also note the viewer mirrors the same gate client-side
(`Planscape/assets/viewer/coordination-viewer.js:5677`, `me.role === 'PM'`), so the photo-reviewer
pane is hidden for the same reason.

| # | Bug | Verdict |
|---|---|---|
| **L1** | Seven+ gates compare `ProjectRole` against `"PM"`, a value no first-party UI writes into that column. `"Manager"` and `"PM"` are the same concept. | **fix** — align ONE vocabulary end to end, **with a migration for existing rows** |
| **L2** | Billing seat split inverted — see below | **fix** (needs a product decision) |
| **L3** | `req.ProjectRole` accepted unvalidated (`:418`) — the reason L1 can drift silently | **fix** — validate on write against the canonical set |

**Do not fix one gate in isolation.** Several sites already carry a tenant-level `Admin`/`Owner`
fallback and work today (`SavedViewsController.cs:131`); those without one are closed to everybody
including the project owner. A per-site patch would produce a fourth vocabulary.

**Migration required.** Existing rows carry `"Owner"`, `"Manager"`, `"Contributor"`, `"Coordinator"`
and whatever API callers wrote. Whatever canonical set is chosen, existing rows must be mapped —
otherwise the fix closes gates that currently work.

---

## 2. The naming collision

| # | Concept | Plugin | Server | Verdict |
|---|---|---|---|---|
| **N1** | **"Deliverable"** | a **document** with a revision/suitability lifecycle — `Docs/Templates/DeliverableLifecycle.cs:20`, commands at `DeliverableLifecycleCommands.cs:136,184,197,210,223,240` | a **MIDP/TIDP obligation owed at a stage gate**, states `PENDING → IN_PROGRESS → SUBMITTED → ACCEPTED / REJECTED / WAIVED` — `DeliverablesController.cs:17-27` | **rename** |

**Canonical: the server's.** It matches ISO 19650's meaning, it has the state machine, and it has a
live mobile client. The plugin's concept is a *document revision lifecycle* and should be named for
that — `DocumentIssueLifecycle` or similar — because that is exactly where it already syncs:
`POST /documents/sync-from-plugin` (`PlanscapeServerClient.cs:789`), into `DocumentRecord`, never
into `Deliverable`.

That the plugin never writes the `Deliverable` table is **correct behaviour under the corrected
reading**, not dead data: the two are different objects. What is missing is that the plugin has no
way to satisfy a MIDP obligation — see W1.

**Sub-finding.** The plugin's six lifecycle actions (Issue / ReIssue / Publish / Cancel / Supersede
/ Replace) flatten into one `action` string (`DeliverableServerSync.cs:45-57`) that nothing
server-side branches on, so **Supersede and Cancel are indistinguishable once synced**. Verdict:
**fix** — small, and it silently loses information today.

---

## 3. Endpoints with no first-party caller found

Five, not seven. None is a delete candidate on this evidence.

| # | Controller / route | Anchor | Why it might be orphaned | Verdict |
|---|---|---|---|---|
| **O1** | `ApprovalChainsController` — `/documents/{docId}/approval-chain` (+ `/decisions`) | `ApprovalChainsController.cs:39` | **Superseded in practice.** Mobile uses the parallel `DocumentsController` approvals (`:1076,1127,1203`). Two mechanisms for one job. | **document** — pick one as canonical, mark the other legacy. Do not delete without logs. |
| **O2** | `CdeContainersController` — `/cde-containers` (+ `/flat`, `PUT /{id}/documents`) | `CdeContainersController.cs` | Genuinely missing feature: no surface exposes CDE container structure, though the plugin models CDE state per document. | **wire** |
| **O3** | `DocumentRevisionsController` — `/documents/{docId}/revisions` | `DocumentRevisionsController.cs` | Plugin computes revisions locally (`RevisionScheme.cs`) and ships a flat `revision` string, so server-side revision history is never populated *or* read. | **wire** (server-side history is the point of having it) |
| **O4** | `BcfApiController` — `bcf/2.1/projects/{id}/topics/**` | `BcfApiController.cs` | The plugin implements BCF as **files** (`BcfEngine.cs`, `Clash/BcfMarkupBuilder.cs`, `Clash/BcfSnapshotter.cs`); the server implements it as **REST**. Both complete, never connected. BCF 2.1 is a published interop standard — external tools are the likely consumer. | **document** as intentional external API surface; separately decide whether the plugin should also speak REST |
| **O5** | `OpenCdeController` — `foundation/**` (OAuth2 + projects) | `OpenCdeController.cs` | Same character as O4: an interop standard whose consumers are third parties by definition. | **document** as intentional external API surface |

### Unused actions on otherwise-live controllers

| # | Route | Anchor | Verdict |
|---|---|---|---|
| **W1** | `POST /transmittals/{txId}/acknowledge`, `PUT .../respond`, `POST .../documents`, `GET .../documents` | `TransmittalsController.cs` | **wire** — highest value in this table |
| **W2** | `GET /documents/{id}/versions`, `GET .../versions/{n}/download` | `DocumentsController.cs:628,659` | **wire** |
| **W3** | `GET /documents/{id}/history` | `DocumentsController.cs:1054` | **wire** |
| **W4** | `GET /documents/validate-name` | `DocumentsController.cs:1379` | **wire** (plugin already validates ISO 19650 names locally — same rule, two implementations) |
| **W5** | `POST /documents/presign`, `POST /documents/finalize`, `POST /documents/bulk-download`, `GET /documents/changed-since` | `DocumentsController.cs:124,172,688,985` | **document** — plausible mobile/offline-sync surface; confirm against mobile before judging |

**W1 is the one with a user-visible hole.** The plugin can `send` a transmittal
(`PlanscapeServerClient.cs:1057`) and mobile can too (`endpoints.ts:583`), but **nothing anywhere
can acknowledge or respond to one**. A transmittal is write-only: the recipient loop never closes,
so "has this been received?" cannot be answered by any surface. That is a missing feature, not
spare API.

---

## 4. Same job, two implementations

| # | Concept | A | B | Verdict |
|---|---|---|---|---|
| **D1** | CDE state change | `PUT /documents/{id}/state` (`CdeTransitionRequest`) — `DocumentsController.cs:755`, used by web (`planscape-web/lib/data.ts:544`) | `POST /documents/{id}/transition` (`MobileTransitionRequest`) — `DocumentsController.cs:788`, used by plugin | **fix** — one DTO. Divergent validation is invisible until they disagree. |
| **D2** | Document approval | `DocumentsController` `/approvals` + `/approval/{id}` + `/approval-status` — used by mobile | `ApprovalChainsController` `/approval-chain` + `/decisions` — no caller found | **document** then converge (see O1) |
| **D3** | Audit trail | plugin local SHA-256 JSONL chain (`_BIM_COORD/audit_log_*.jsonl`) | server `AuditLog` via `/audit-events/batch` (`DeliverableServerSync.cs:89`) | **document** — the local chain is the tamper-evident one, the server's is the shared one; neither is authoritative and that should be stated, not silently tolerated |
| **D4** | Document numbering | plugin `_BIM_COORD/doc_sequences.json` | server `/seq`, `/seq/reserve` (called) | **fix** — numbers minted offline never reserve server-side; cross-machine collision is possible |
| **D5** | ISO 19650 name validation | plugin-local | `GET /documents/validate-name` | see W4 |

**Aligned, no action:** CDE state vocabulary `WIP / SHARED / PUBLISHED / ARCHIVE`
(`DeliverableLifecycle.cs:203-207` ↔ `DocumentRecord.cs:15`) and suitability `S0–S7 / CR / AB`
(`DocumentRecord.cs:16`).

---

## 5. Web surface gaps

`planscape-web/lib/data.ts` calls 6 document/transmittal endpoints
(`:410,419,514,544,553,563,575`). No `deliverables/`, `stage-gates/`, `cde/` or `approvals/` page
exists under `planscape-web/app/projects/[id]/` (present: clashes, documents, issues, meetings,
members, models, photos, transmittals, viewer).

Notably the **web is behind mobile** on the delivery spine: mobile has deliverables and stage-gate
screens (`Planscape/app/deliverables/`, `Planscape/app/stages/`), the web app has neither. Verdict:
**wire**, but this is a product-priority call, not a defect.

---

## Recommended order

1. **L1 + L3** — the `"PM"` gate and the role vocabulary. Live permissions hole; fix the vocabulary
   once and L1 falls out.
2. **L2** — seat-count inversion. Needs a product decision on which role consumes an author seat.
3. **W1** — close the transmittal loop. Smallest gap between "server-complete" and "user-visible".
4. **N1** — rename, before anything else is built on either meaning of "deliverable".
5. **D1** — collapse the two CDE-transition DTOs.
6. Everything else on the table's own merits.

**Nothing above is started.** Awaiting review of the verdict column.
