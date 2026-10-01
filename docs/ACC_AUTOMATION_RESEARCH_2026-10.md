# ACC / APS automation research — what gives STINGTOOLS the most automation

**Written 2026-10-01** on branch `claude/acc-work-review-gaps-7e2ac7`.

This document has three inputs:
- **Code inventory** (§1): read from the code, not the docs.
- **Research** (§2): official Autodesk sources only. Every claim carries a URL; anything not confirmed on an official page is marked **UNVERIFIED**.
- **What was built** (§4): each item is linked to its commit.

APS now calls ACC **"Forma"** in its docs ("Forma Build", "Hub Admin", "Forma Data Management"). The URLs and API paths are unchanged (https://aps.autodesk.com/en/docs/acc/v1/overview/introduction/).

Nothing below was exercised against a live ACC tenant. §6 lists the live checks.

---

## 1. Inventory: every APS/ACC call today

### Plugin (Revit, `StingTools/V6` + `StingTools/Clash`)

**Auth**
- 3-legged PKCE sign-in: `AccOAuthFlow.cs:40`. Scopes: `data:read data:write data:create account:read` (`AccOAuthFlow.cs:54`).
- Token store: DPAPI-protected `%APPDATA%\Planscape\acc_credentials.json` (`AccCredentialStore.cs:56`).
- Refresh: single-use refresh tokens under a cross-process lock (`AccIssueSync.EnsureAuthDetailedAsync`).
- There is no 2-legged grant anywhere.

**Transport.** Most calls go through `AccHttp.SendAsync`, which:
- retries 429 and 503 with Retry-After;
- retries only idempotent requests otherwise;
- refreshes once on a 401;
- classifies the outcome into `AccFetchStatus` / `AccFetchResult<T>`.

| Area | Endpoints (as built) | Called from | Paging / truncation |
|---|---|---|---|
| Issues v1 | `issue-types`, `issues` (GET/POST/PATCH), `issues/{id}/comments`, `issue-attribute-definitions`, `issue-root-cause-categories`, `attachments` | `AccIssueSync`, `AccIssueAttachment` | Offset. Issue types and root causes were **one page** until AUT-2. |
| Data Management / OSS | `project/v1/hubs[/…/projects/…/topFolders]`, `data/v1/projects/{p}/storage\|items\|versions\|folders/{f}[/contents]`, `oss/v2/…/signeds3upload` + signed PUT | `AccModelUpload`, `AccReviews`, `AccProjectDiscovery`, `AccSelfCheck`, `AccClashLocate` | `links.next`. The item search had no INCOMPLETE flag until AUT-1. |
| Docs | `bim360/docs/v1/…/naming-standards/{id}`, `…/custom-attribute-definitions`, `…/custom-attributes:batch-update` | `AccModelUpload`, `AccDocsMetadata`, `AccDocsLifecycle` | Offset, 50 pages. Its own HttpClient until AUT-4. |
| Copy to archive | `data/v1/projects/{p}/items?copyFrom=` | `AccDocsLifecycle` | n/a |
| Model Coordination | `bim360/modelset/v3/…/modelsets[/…/versions/{v}\|latest]`, `bim360/clash/v3/…/tests[/…/resources]` | `AccModelCoordSync` | `continuationToken`; INCOMPLETE at 50 pages |
| Model Derivative | `modelderivative/v2/designdata/{b64}/metadata[/{guid}/properties:query]` | `AccClashLocate` | Chunked |
| Model Properties | `construction/index/v2/…/indexes:batch-status`, `indexes/{id}[/fields\|/queries…]` | `AccModelProperties` | Polled |
| Locations | `construction/locations/v2/…/trees/default/nodes` | `AccLocations` | Offset; INCOMPLETE |
| Hub Admin (project level) | `construction/admin/v1/projects/{p}[/users]` | `AccProjectDetails`, `AccProjectMembers` | Offset; INCOMPLETE |
| Reviews | `construction/reviews/v1/…/versions/{v}/approval-statuses`, `reviews/{id}/progress`, `workflows`, POST `reviews` | `AccReviews` | Limit 50 (the documented maximum); INCOMPLETE |
| Transmittals | `construction/transmittals/v1/…/transmittals[/{id}/documents]` | `AccReviews` | Read only |

### Server (`Planscape.Server`)

- **Auth:** 3-legged browser OAuth (`AccOAuthController`), scopes `Acc:Scopes`. The default was `data:read data:write` until AUT-6.
- **Token refresh:** under a Postgres advisory lock (`AccTokenRefresher`). Since AUT-7 a connection can use Secure Service Account tokens instead.
- **Calls:**
  - Issues v1 (types, create, PATCH, filtered reads) — `AccSyncService`.
  - DM hubs, projects, top folders and folder contents — `AccSyncService`, `AccWebhookService`.
  - Webhooks: tokens, hooks, list, delete — `AccWebhookService`.
- **Receiver:** `AutodeskWebhooksController` checks `x-adsk-signature: sha1hash=…`, which is correct per the APS docs.
- `ApsModelDerivativeConverter` makes no APS call. It refuses by design.

**Not called anywhere:**
- Account-level Hub Admin (users, companies, roles)
- DM folder permissions
- Issues attribute mappings
- AEC Data Model GraphQL
- Parameters service
- Data Connector
- RFIs, Submittals, Forms, Photos, Assets, Cost, Sheets, Takeoff
- Files PDF exports
- Model Coordination clash-group writes

---

## 2. Research (official sources)

### Auth for unattended use

**Token contexts.** Each endpoint documents one of three contexts: "app only", "user context required" (needs 3-legged), or "user context optional" (https://aps.autodesk.com/en/docs/oauth/v2/developers_guide/basics/).

**Refresh tokens**
- Single-use: "the original refresh token is invalidated" (https://aps.autodesk.com/blog/about-refresh-token).
- Valid for 14 days (same post; and the AEC Data Model FAQ https://aps.autodesk.com/en/docs/aecdatamodel/v1/developers_guide/faq/).
- Access-token `expires_in` is 3599 in every example. A fixed access-token lifetime is **UNVERIFIED**.
- Scopes sent on refresh must be "the same with or a subset of" the original grant, so a refresh cannot widen it (https://aps.autodesk.com/en/docs/oauth/v2/reference/http/gettoken-POST/).

**Scopes:** `data:read`, `data:write`, `data:create`, `data:search`, `account:read`, `account:write`, `bucket:*`, `user:*`, `viewables:read`, `code:all`, `openid` (https://aps.autodesk.com/en/docs/oauth/v2/developers_guide/scopes/).

**Custom Integrations** (2-legged access to a hub)
- UI path: Hub Admin → Custom Integrations → Add custom integration → APS Client ID (https://aps.autodesk.com/en/docs/acc/v1/tutorials/getting-started/manage-access-to-acc/).
- Without an SSA attached, the integration "will act as a hub administrator with immediate access to all data within your account" (https://help.autodesk.com/cloudhelp/ENU/Docs-Admin/files/hub-administration/Custom_Integrations.html).

**Secure Service Accounts (SSA)**
- Status: **GA** since 2025-09-04 (https://aps.autodesk.com/blog/update-secure-service-accounts-ssa-goes-ga).
- How it works: "SSAs use a private key to generate a JWT, which is then exchanged for a three-legged access token". The account "never needs to sign in, and can never 'lose' their refresh token" (https://aps.autodesk.com/en/docs/ssa/v1/developers_guide/overview/).
- Limits: 10 SSAs per client id by default; SSAs idle for 12 months are deactivated. "Only Server-to-Server APS apps support SSA."
- **JWT** (https://aps.autodesk.com/en/docs/ssa/v1/developers_guide/jwt-assertions/):
  - header `alg` RS256 and `kid`;
  - claims `iss` = client id, `sub` = service account id, `aud` = `https://developer.api.autodesk.com/authentication/v2/token`, `exp` 0–5 minutes ahead;
  - `scope` is an **array** of strings.
- **Exchange** (https://aps.autodesk.com/en/docs/ssa/v1/reference/http/ssa-exchange-jwt-assertion-POST/):
  - `POST /authentication/v2/token`, form-encoded, Basic `client_id:client_secret`;
  - `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`, `assertion=…`;
  - 200 returns `access_token`, `token_type`, `expires_in`.
- **Known issues** (https://aps.autodesk.com/en/docs/ssa/v1/developers_guide/known-issues/):
  - Sheets, Forms, **Issues** and Photos need a Forma Build subscription on the SSA.
  - The **Review** API needs Data Management permissions assigned to it manually.
  - Custom-attribute edits may need a Forma Files licence.
  - Revit Cloud Worksharing needs BIM Collaborate (Pro).

**Regions**
- Values: US, CAN, EMEA, GBR, DEU, IND, JPN, AUS (https://aps.autodesk.com/en/docs/acc/v1/overview/acc-regions/).
- Region selection is "x-ads-region or region" depending on the API.
- Hub Admin and Data Management document a `Region` header; Issues and Model Coordination document `x-ads-region`.
- The plugin sends `x-ads-region` everywhere it sends one. Whether Hub Admin also honours it is **UNVERIFIED**.

### API families relevant to the fortnightly cycle

**Data Management**
- Rate limits are per client id per endpoint: e.g. `POST items` 50/min, folder contents 300/min (https://aps.autodesk.com/en/docs/data/v2/developers_guide/rate-limiting/dm-rate-limits/).
- Pagination: `page[number]` / `page[limit]` with `links.next` (https://aps.autodesk.com/en/docs/data/v2/developers_guide/pagination/).

**Folder permissions**
- `POST /bim360/docs/v1/projects/:project_id/folders/:folder_id/permissions:batch-create`, plus `:batch-update`, `:batch-delete` and a `GET`.
- `data:write`. "the user needs to have CONTROL permissions" (APS reference, via the Docs API navigation).
- Response field names were not read: **UNVERIFIED**.

**Reviews**
- GET and POST `reviews`, POST `workflows`, `approval-statuses`; `limit` maximum 50.
- **No endpoint approves or rejects a step**: **UNVERIFIED** that none exists (https://aps.autodesk.com/en/docs/acc/v1/overview/field-guide/reviews/).
- Rate limits: 200 GET / 100 POST per minute.

**Transmittals:** read only. The API cannot create transmittals, add recipients or export (https://aps.autodesk.com/en/docs/acc/v1/overview/field-guide/transmittals/).

**Issues v1**
- POST is "User context required" (3-legged or SSA).
- Lookups: `issue-types` (subtypes only with `include=subtypes`), `issue-attribute-definitions`, `issue-attribute-mappings`, `issue-root-cause-categories`.
- Rate limit: 500/min per user hub (https://aps.autodesk.com/en/docs/acc/v1/overview/rate-limits/issues-rate-limits/).

**Model Coordination**
- Clash tests are **not started by the API**: "Clash Tests are executed for every model set version created by the system" (https://aps.autodesk.com/en/docs/acc/v1/overview/field-guide/model-coordination/mcfg-clash/).
- Clash groups can be assigned and closed: `POST tests/:testId/clashes:assign` and `…/clashes:close`.
- No Model Coordination webhooks exist.
- Rate limit: 600/min per user (https://aps.autodesk.com/en/docs/acc/v1/overview/rate-limits/model-coordination-rate-limits/).

**Webhooks**
- Events (https://aps.autodesk.com/en/docs/webhooks/v1/reference/events):
  - `dm.*`
  - Issues: `issue.*-1.0`, `comment.created-1.0`, `attachment.*-1.0`
  - Reviews (`autodesk.construction.reviews`): `review.created-1.0`, `review.closed-1.0`
  - Cost, Revit Cloud Worksharing (`model.sync`, `model.publish`), and others.
- **Reviews hooks** (https://aps.autodesk.com/en/docs/webhooks/v1/tutorials/create-a-hook-reviews/):
  - scope `{"project": "<uuid>"}`, 3-legged only, `data:read` + **`data:create`** to create;
  - delivery is filtered by what the creating user may see;
  - payload carries `roundNum`, `sequenceId`, `status`.
- **Issues hooks** need a Project Admin's token.
- **Signing:** `x-adsk-signature: sha1hash=<HMAC-SHA1(body, secret)>` (https://aps.autodesk.com/en/docs/webhooks/v1/tutorials/how-to-verify-payload-signature/).
- **Delivery** (https://aps.autodesk.com/en/docs/webhooks/v1/developers_guide/event-delivery-guarantees/):
  - your endpoint must answer 2xx within 7 s;
  - a failed event is retried up to 8 times over 48 h;
  - after 5 consecutive failures the hook goes inactive. `autoReactivateHook` exists, and the server sets it.

**Other APIs**
- **AEC Data Model** (GraphQL): read-only, 3-legged, published Revit 2024+ models only; must be activated per hub (https://aps.autodesk.com/en/docs/aecdatamodel/v1/developers_guide/overview/).
- **Data Connector:** `POST /data-connector/v1/accounts/:accountId/requests`, user context; scheduled daily, weekly or monthly extracts of Admin, Issues, Locations, Submittals, Cost and RFIs (https://aps.autodesk.com/en/docs/acc/v1/overview/field-guide/data-connector/).
- **RFIs, Submittals, Forms, Photos, Assets, Cost, Sheets, Takeoff:** all exist, mostly user context required (see each module's reference under https://aps.autodesk.com/en/docs/acc/v1/).
- **Rate limits generally:** 429 with `Retry-After`, which STING honours (https://aps.autodesk.com/en/docs/acc/v1/overview/rate-limits/).

---

## 3. Ranked: top 10 for the KUT fortnightly coordination cycle

The cycle runs: issue sheets → export → upload to ACC Docs → review/approve → clash (automatic on the model-set version) → escalate issues → read decisions back → transmittal record.

| # | Capability | STING part it automates | Manual step removed | Needs | Effort | Risk | State |
|---|---|---|---|---|---|---|---|
| 1 | **SSA tokens on the server** | `AccSyncService`, webhooks | A person re-signing in when the 14-day refresh chain breaks; sync stopping over a holiday | SSA + server-to-server app + robot invited to the project | M | Low (opt-in) | **Built** (AUT-7), needs setup |
| 2 | **Reviews webhook `review.closed-1.0`** | `AutodeskWebhooksController` → clients → `ACC_ReadReviews` | Polling ACC to learn that a review finished | `data:create` grant; Autodesk webhook secret and URL | S | Low | **Built** (AUT-5) |
| 3 | **`data:create` in the server grant** | `acc/webhooks/subscribe` | Hooks failing to create on a default grant | One reconnect | S | Low | **Built** (AUT-6) |
| 4 | **Complete lists or INCOMPLETE** (issue types, root causes, read-back) | Escalation, import, sync | Issues filed under the wrong type; ACC issues shown as deleted | — | S | Low | **Built** (AUT-2, AUT-3) |
| 5 | **Upload robustness** (token refresh in attribute stamps; honest item search) | `ACC_UploadModel`, Export Centre upload, retire | Re-running uploads whose ISO 19650 stamp silently failed | — | S | Low | **Built** (AUT-1, AUT-4) |
| 6 | **Folder permission pre-flight** in `ACC_SelfCheck` | Fortnightly preset steps 5–7 | Discovering a missing upload permission half way through the run | Response fields to verify | S–M | Low | Open (ACC-AUT-8) |
| 7 | **Issue attribute mappings** check before push | Escalation with `issueCustomAttributes` | 400s from attributes not mapped to the subtype | — | S | Low | Open (ACC-AUT-9) |
| 8 | **Close/assign clash groups** when the escalation closes | `ACC_SyncIssueStatus` | Closing resolved clash groups by hand in Model Coordination | Request body to verify; a policy switch | M | Medium (writes to MC) | Open (ACC-AUT-10) |
| 9 | **`Region` header on Hub Admin / DM** | `AccProjectDetails`, `AccProjectMembers`, uploads | Slow or misrouted calls for a non-US hub | Live check | S | Low | Open (ACC-AUT-11) |
| 10 | **Data Connector scheduled extract** | Owner KPI dashboard | Monthly manual exports for KPI reporting | `accountId`; account-level permission | L | Low | Open (ACC-AUT-12) |

Also noted but outside the top 10:
- Files PDF export with markups (`/construction/files/v1/projects/{id}/exports`).
- AEC Data Model for model QA without Revit (needs hub activation).
- Sheets API publishing.

---

## 4. What was built (2026-10-01)

| Id | Commit | What | Tests |
|---|---|---|---|
| AUT-1 | `968cdf2d7` | Existing-item search on a 409 reports its HTTP failure and an incomplete search | 3 loopback |
| AUT-2 | `0a94bf0d3` | Issue types and root causes read every page; INCOMPLETE at the cap | 5 loopback |
| AUT-3 | `18c0f3e05` | Server read-back at its page cap is an error, never `not_found` | 1 (fails without the fix) |
| AUT-4 | `e5619b249` | Docs attribute calls go through `AccHttp` when given credentials (401 refresh) | 3 loopback |
| AUT-5 | `357aacc77` | Subscribe to and receive `review.closed-1.0`; the undocumented event cases are labelled as such | Subscribe tests extended; receiver test fails without the fix |
| AUT-6 | `1b145b3bf` | **Scope change** (server only): default grant gains `data:create`. **Existing server connections must reconnect once.** The plugin is unchanged. | 1 |
| AUT-7 | `d691394be` | SSA token minting, opt-in per connection (`accAuthMode: "ssa"`); `Acc:Ssa:*` settings with `REPLACE_WITH_` placeholders that fail by name | 6 (per-test generated RSA key) |

These were already correct and were left alone: webhook signature verification, `autoReactivateHook`, and the Reviews page size (50).

## 5. What remains

ROADMAP ACC-AUT-8 to ACC-AUT-12 (§3), plus:
- **ACC-AUT-13:** SSA in the *plugin*, for unattended Revit runs. The private key would have to live on each workstation, so the server path is preferred.
- **ACC-AUT-14:** apply `review.closed-1.0` automatically. Today it is a notice; the decision is still proposed and accepted by a person.

## 6. Live checks (NEEDS MANUAL CHECK)

1. After reconnecting ACC on the server, `POST /api/projects/{id}/acc/webhooks/subscribe` creates 5 hooks, including `autodesk.construction.reviews/review.closed-1.0`.
2. Close a review in ACC. The web client receives `acc.review.closed`. Then run `ACC_ReadReviews` in Revit.
3. SSA:
   - set the `Acc__Ssa__*` environment variables and the connection's `accAuthMode: "ssa"`;
   - run `POST …/acc/sync`. The log shows no refresh-token use;
   - an issue created by the sync shows the robot as its creator.
4. A non-US hub: confirm Hub Admin calls work with `x-ads-region`, or switch them to `Region` (ACC-AUT-11).
