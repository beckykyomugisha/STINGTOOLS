# Document / deliverable alignment audit — plugin ↔ server ↔ web

**Measured 2026-08-01 on `main` @ `a5109e3e5`.** Report only — nothing here is implemented.

Method (reproducible):

```bash
# every server API path the Revit plugin calls
grep -rhoE '"/api/[^"]*"|\$"/api/[^"]*"' StingTools --include=*.cs | sort -u
# every server API path the web app calls
grep -rn "api/" planscape-web/lib/data.ts
# server routes
grep -oE '\[Route\("[^"]+"\)\]|\[Http[A-Za-z]+(\("[^"]*"\))?\]' \
  Planscape.Server/src/Planscape.API/Controllers/<X>Controller.cs
```

---

## Headline

The three surfaces do not disagree about document *state* — `WIP / SHARED / PUBLISHED / ARCHIVE`
matches on both sides. They disagree about **which objects exist at all**. Seven server
controllers covering the ISO 19650 delivery spine have **no first-party client on either the plugin
or the web side**, and the word "deliverable" names two unrelated things.

---

## A. Plugin capability with no server or web equivalent

| # | Plugin capability | Anchor | Server | Web | Note |
|---|---|---|---|---|---|
| A1 | Deliverable lifecycle: Issue / ReIssue / Publish / Cancel / Supersede / Replace | `StingTools/Docs/Templates/DeliverableLifecycleCommands.cs:136,184,197,210,223,240` | partial — collapses to `POST /documents/sync-from-plugin` (`PlanscapeServerClient.cs:789`) | ✗ | Six distinct ISO 19650 actions flatten into one `action` string field (`DeliverableServerSync.cs:45-57`). Nothing server-side branches on it, so Supersede and Cancel are indistinguishable after sync. |
| A2 | Document rendering (16 DOCX/XLSX templates, MiniWord + ClosedXML) | `StingTools/Docs/Templates/TemplateEngine.cs`, `MiniWordAdapter.cs`, `XlsxTemplateRenderer.cs` | ✗ | ✗ | Rendered artefacts land in `<project>/_BIM_COORD/generated/` on the authoring machine only. Nothing uploads them, so a rendered transmittal cover sheet is invisible to everyone else. |
| A3 | Append-only SHA-256 audit chain | `_BIM_COORD/audit_log_{yyyy}_{MM}.jsonl` | partial — `PushAudit` → `/audit-events/batch` (`DeliverableServerSync.cs:89`) | ✗ | The tamper-evidence chain itself is local; only individual rows are pushed, so the server holds no verifiable chain. |
| A4 | Document number sequence store | `_BIM_COORD/doc_sequences.json` | `/seq`, `/seq/reserve` (called) | ✗ | Two counters exist. Numbers minted offline never reserve server-side; collision is possible across machines. |
| A5 | Distribution groups with type/role/suitability scoring | `StingTools/Docs/Templates/TransmittalCommands.cs` (`DistributionGroups.SuggestFor`) | `DistributionGroupsController` exists, plugin never calls it | ✗ | PR #517 documents this as knowingly unported. The scoring has no server equivalent. |
| A6 | BCF authoring / round-trip | `StingTools/BIMManager/BcfEngine.cs`, `Clash/BcfMarkupBuilder.cs`, `Clash/BcfSnapshotter.cs` | `BcfApiController` (BCF 2.1 REST) — **never called by the plugin** | ✗ | The plugin writes `.bcfzip` files; the server speaks BCF 2.1 over HTTP. Two complete implementations that never meet. See C1. |
| A7 | Revision scheme / suitability progression | `StingTools/Docs/Templates/RevisionScheme.cs` | `DocumentRevisionsController` — **never called** | ✗ | Revision strings are computed in the plugin and shipped as a flat `revision` field. |

## B. Server endpoints with no client (plugin *or* web)

Verified absent from both `grep` inventories above.

| # | Controller | Route | Anchor |
|---|---|---|---|
| B1 | `DeliverablesController` | `api/projects/{id}/deliverables` + `/transition` + `/state-machine` | `DeliverablesController.cs:29` |
| B2 | `StageGatesController` | `api/projects/{id}/StageGates` (`[controller]` token — no hyphen) + `/decide` + `/seed-riba` + `/criteria/{key}/signoff` | `StageGatesController.cs:28` |
| B3 | `CdeContainersController` | `api/projects/{id}/cde-containers` (+ `/flat`, `PUT /{id}/documents`) | `CdeContainersController.cs` |
| B4 | `ApprovalChainsController` | `api/projects/{id}/documents/{docId}/approval-chain` + `/decisions` | `ApprovalChainsController.cs` |
| B5 | `DocumentRevisionsController` | `api/projects/{id}/documents/{docId}/revisions` | `DocumentRevisionsController.cs` |
| B6 | `BcfApiController` | `bcf/2.1/projects/{id}/topics/**` | `BcfApiController.cs` |
| B7 | `OpenCdeController` | `foundation/**` (OAuth2 + projects) | `OpenCdeController.cs` |
| B8 | `DocumentsController` unused actions | `POST /presign`, `POST /finalize`, `GET /{id}/versions`, `POST /bulk-download`, `GET /{id}/history`, `POST /{id}/approval-request`, `PUT /{id}/approval/{aid}`, `GET /{id}/approval-status`, `GET /validate-name`, `GET /changed-since` | `DocumentsController.cs:124,172,628,688,1054,1076,1127,1203,1379,985` |
| B9 | `TransmittalsController` unused actions | `PUT /{id}/acknowledge`, `PUT /{id}/respond`, `POST /{id}/documents`, `GET /{id}/documents` | `TransmittalsController.cs` |

**B8/B9 matter most.** The whole approval-chain feature — request, decide, query status — is
server-complete and unreachable. Same for document versioning (`/versions`) and the transmittal
*response* half: the plugin can `send` a transmittal (`PlanscapeServerClient.cs:1057`) but nothing
can `acknowledge` or `respond` to one, so a transmittal is write-only and its recipient loop never
closes.

## C. Same concept, modelled differently

| # | Concept | Plugin | Server | Consequence |
|---|---|---|---|---|
| C1 | **"Deliverable"** | A *document* with a revision/suitability lifecycle — `DeliverableLifecycle.cs:20` | A *MIDP/TIDP obligation owed at a stage gate*: `PENDING → IN_PROGRESS → SUBMITTED → ACCEPTED / REJECTED / WAIVED` — `DeliverablesController.cs:17-27` | **Two unrelated things share one word.** The plugin's deliverables sync into `DocumentRecord`, never into `Deliverable`. A reader of either codebase will assume the wrong one. This is the single most likely source of future mis-integration. |
| C2 | **CDE state change** | `POST /documents/{id}/transition` (`MobileTransitionRequest`) — `DocumentsController.cs:788` | — | Two endpoints do the same job with different DTOs: the plugin uses the one named "Mobile"; the web uses `PUT /documents/{id}/state` (`CdeTransitionRequest`, `DocumentsController.cs:755`, called at `planscape-web/lib/data.ts:544`). Divergent validation is invisible until they disagree. |
| C3 | **CDE state vocabulary** | `WIP / SHARED / PUBLISHED / ARCHIVE` — `DeliverableLifecycle.cs:203-207` | `CdeStatus` default `"WIP"`, same four — `DocumentRecord.cs:15` | ✅ Aligned. Suitability `S0–S7, CR, AB` also aligned (`DocumentRecord.cs:16`). |
| C4 | **Audit trail** | Local JSONL SHA-256 chain | `AuditLog` rows via `/audit-events/batch` | Same events, two stores, no reconciliation. The local chain is the tamper-evident one; the server's is the shared one. Neither is authoritative. |
| C5 | **Transmittal recipients** | `project_team.json` / `distribution_groups.json`, local | `Transmittal.Recipient`, a single string | PR #517/#518 both flag this. Not a list, not an FK — so "who was this issued to" cannot be queried. |

## D. Web gaps against the server it already has

The web app calls **6** document/transmittal endpoints (`planscape-web/lib/data.ts:410,419,514,544,553,563,575`)
out of ~30 available.

| # | Missing on web | Server support | Anchor |
|---|---|---|---|
| D1 | Document version history | `GET /{id}/versions` (+ per-version download) | `DocumentsController.cs:628,659` |
| D2 | Approval request / decide / status | 3 endpoints | `DocumentsController.cs:1076,1127,1203` |
| D3 | Document history timeline | `GET /{id}/history` | `DocumentsController.cs:1054` |
| D4 | Transmittal acknowledge / respond | 2 endpoints | `TransmittalsController.cs` |
| D5 | Attach documents to a transmittal | `POST /{txId}/documents` | `TransmittalsController.cs` |
| D6 | ISO 19650 name validation | `GET /validate-name` | `DocumentsController.cs:1379` |

`planscape-web/app/projects/[id]/` has **no** `deliverables/`, `stage-gates/`, `cde/` or `approvals/`
page (`ls` shows: clashes, documents, issues, meetings, members, models, photos, transmittals, viewer).

## E. Adjacent finding — a permission tier that gates document-ish features

Not document-specific, but it lands on this surface. `ProjectMember.ProjectRole == "PM"` is checked
in ~10 places (e.g. `DistributionGroupsController.cs:202`, `SavedViewsController.cs:136`,
`SitePhotosController.cs:884`) and in the viewer's photo-reviewer gate
(`Planscape/assets/viewer/coordination-viewer.js:5677`).

**No first-party path ever writes `"PM"`.** Roles actually written are `"Owner"`
(`AuthController.cs:1231`), `"Manager"` (`ProjectsController.cs:172`) and `"Contributor"`
(`ProjectMembersController.cs:140,171,340`). Some sites have a tenant-level `Admin`/`Owner` fallback;
those that don't are unreachable.

Related: `QuotaGuardService.cs:76-77` counts author seats as `ProjectRole == "Author"` (never
written → always 0) and coordinator seats as `!= "Author"` (→ every member, including the owner).
`CheckCanAddUserAsync` is called only from `OnboardingController.cs:93` and
`TenantAdminController.cs:108`; the main invite path enforces `tenant.MaxUsers` instead
(`ProjectMembersController.cs:256`).

---

## Suggested order, if this is taken forward

1. **C1 — rename.** Cheapest, highest leverage. Either the plugin's concept or the server's should
   stop being called "deliverable" before anything is built on top of either.
2. **B9/D4 — close the transmittal loop.** Send exists and works; acknowledge/respond are written
   and unreachable. Smallest gap between "server-complete" and "user-visible".
3. **B8/D2 — expose approval chains.** Fully implemented server-side, zero clients.
4. **C2 — collapse the two transition endpoints** to one DTO before their validation drifts.
5. **A6/B6 — decide whether BCF is files or REST.** Do not keep both.

Nothing above should be started until this table is reviewed.
