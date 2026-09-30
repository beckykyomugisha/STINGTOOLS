# ACC integration strategy — StingTools × Autodesk Construction Cloud

**Written 2026-09-30 and revised at 21:30 the same day**, the evening before KUT (Kampala Uganda
Temple) starts on ACC as its CDE. It is measured against three states, and says which one each
fact belongs to:

- **Deployed DLL**: `C:\Dev\STINGTOOLS\CompiledPlugin\StingTools.dll`, built 2026-09-29 04:58,
  which is what Revit's `.addin` `<Assembly>` points at. Searching the binary finds none of
  `AccIds`, `code_verifier` (PKCE) or `AccImportIssuesCommand`, so it is **pre-fix**.
- **`main`** @ `852ec8992`.
- **Working tree** of `claude/acc-work-review-gaps-7e2ac7` (uncommitted, being changed as this
  was written): `AccIds`, `AccHttp`, `AccCredentialStore` (DPAPI), PKCE in `AccOAuthFlow`,
  `AccIssueImport` + `ACC_ImportIssues`, `AccCdeRouting`, `AccDocsMetadata`,
  `AccDisciplineResolver`, 14 new `acc_settings.json` keys.

Companion docs: [`KUT_ACC_DAY1_PLAYBOOK.md`](KUT_ACC_DAY1_PLAYBOOK.md) (what to do tomorrow),
[`KUT_OPERATING_MODEL.md`](KUT_OPERATING_MODEL.md) (how to run the work),
[`KUT_LIVE_VERIFICATION_RUNBOOK.md`](KUT_LIVE_VERIFICATION_RUNBOOK.md) (B1 = ACC proof steps).

### Conventions

- **[C]** = confirmed against APS documentation or a public APS sample (URL cited).
- **[U]** = unconfirmed: believed, inferred, or read from secondary sources. Verify before relying on it.
- **[Code]** = read from this repository's source, not run.
- APS now labels many ACC APIs **"Forma"** in page titles (for example "Forma Issues API"). The
  URLs are still `aps.autodesk.com/en/docs/acc/v1/…` and the endpoints are unchanged. **[C]**
- APS reference pages are rendered client-side; several could not be fetched directly while this
  was written, so some facts come from APS blog posts, samples and search summaries. Those are
  marked [U] where the detail matters.

---

## 1. What StingTools already does with ACC (inventory)

| Area | Files | What it does | Status |
|---|---|---|---|
| OAuth 3-legged | `V6/AccOAuthFlow.cs` | Browser sign-in, loopback `http://localhost:8910/callback`, scopes `data:read data:write data:create account:read`. Confidential client: Basic auth. **Working tree:** PKCE (S256) public client when the secret is empty | Built. **But the BCC card still demands a secret** before Sign in / Test / Find project (`BIMCoordinationCenter.cs` ~L5534/5562/5583), so PKCE cannot be reached from the UI yet: see **R10** |
| Token refresh + credential store | `V6/AccIssueSync.cs` (`EnsureAuthAsync`, `LoadCredentials`/`SaveCredentials`) | `%APPDATA%\Planscape\acc_credentials.json`: client id and secret, refresh token | Deployed/HEAD: **plain text**. Working tree: `AccCredentialStore` with DPAPI (CurrentUser) for secret, access and refresh tokens |
| Project discovery | `V6/AccProjectDiscovery.cs`, BCC card "Find my ACC project" | `GET project/v1/hubs` → `hubs/{id}/projects`. Writes the **`b.`-prefixed** id verbatim | Built. See §4 **R1** |
| Project-scoped settings | `V6/AccOperatingPolicy.cs`, `Clash/AccProjectSettingsFile.cs`, `V6/AccProjectScope.cs` | `acc_settings.json` per project. Strict parse: an unknown key makes the **whole file** Malformed | Built (IM-18). The working tree adds 14 keys (strategy R3, playbook §6) |
| Issues push/pull | `V6/AccIssueSync.cs` | Push: title, description, status, location, type/subtype. Pull: paginated, fails loudly | HEAD path is `construction/issues/v1/containers/{id}` with the id as given. **Working tree:** `…/projects/{AccIds.ForAcc(id)}` (strips `b.`), `x-ads-region`, assignee, due date, excluded statuses |
| Issue-type resolution | `AccIssueSync.EnsureIssueTypeAsync` | Picks the first type whose title contains "clash" or "coordination", else the first type. Subtype = first subtype | Does not filter out inactive types or subtypes **[Code]** |
| Model Coordination read | `V6/AccModelCoordSync.cs` | `bim360/modelset/v3` model sets → `bim360/clash/v3` tests → resources → gzipped scope JSON (clash, instance, document) | Built. HEAD uses the container id verbatim (R1). **Working tree applies `AccIds.ForAcc`** and resolves discipline from model names (`AccDisciplineResolver`, `disciplineMap`). Sub-paths come from the APS sample, not yet proved live |
| Clash triage + escalation | `Clash/AccPullClashesCommand.cs`, `V6/ClashTriageEngine.cs`, `pushed_clashes.json` | Scores clashes, writes CSV, escalates top N to Issues under a policy | Built |
| Issue-status reconcile | `Clash/AccSyncIssueStatusCommand.cs` | Un-tracks clashes whose ACC issue closed | Built |
| Docs upload | `V6/AccModelUpload.cs`, `Clash/AccUploadModelCommand.cs` (`ACC_UploadModel`, `ACC_UploadLastBundle`) | DM `storage` → signed S3 → `items`/`versions`. Adds a version when the name already exists | Built. Not proved live |
| Local "ACC bundle" | `BIMManager/PlatformLinkCommands.cs` `ACCPublishCommand` | Zips deliverables locally. **Does not publish** | Built, labelled honestly |
| Lifecycle gaps → Issues | `Commands/Twin/KutPushLifecycleGapsToAccCommand.cs` | Pushes BOQ/spec/BMS gaps as ACC Issues (idempotent sidecar) | Built. Not proved live |
| CDE mirror stub | `Core/ProjectFolderEngine.cs` `MirrorToACC` | Writes a **manifest JSON only**. Uploads nothing | Stub. Do not rely on it |
| Server connector | `Planscape.Server/.../PlatformConnectors.cs` `AccConnector` (+ working-tree `AccTokenRefresher`, `Services/Aps/`) | Server-side hubs + issues | Separate from the plugin, and not on the KUT day-1 path |
| Issues import (working tree) | `V6/AccIssueImport.cs`, `Clash/AccImportIssuesCommand.cs` (`ACC_ImportIssues`, dispatched in `StingCommandHandler` + `WorkflowEngine`) | Pulls ACC issues into the STING issue register (`issues.json`), links escalated clashes and lifecycle gaps by origin sidecar, writes a CSV, refuses when the register is unreadable | Built + unit tests (`AccIssueImportTests`). Not proved live |
| CDE routing + Docs metadata (working tree) | `V6/AccCdeRouting.cs`, `V6/AccDocsMetadata.cs` | Suitability → CDE state (`Iso19650Suitability.CdeStateFor`) → folder URN from `cdeFolders`, no fallback. ISO 19650 attribute set for ACC custom attributes | **Library + tests only: not yet called by `AccModelUpload` / `ACC_UploadModel`** |
| Federated tag compliance (2026-10-01) | `V6/AccModelProperties.cs`, `V6/AccFederatedCompliance.cs`, `Clash/AccFederatedComplianceCommand.cs` (`ACC_FederatedCompliance`, BCC ACC card "🏷 Federation Tags", KUT cycle step 3a) | Reads the 8 tag tokens + `ASS_TAG_1_TXT` from **every model in the latest version of the remembered coordination model set** through the ACC **Model Properties (Index) API** (`construction/index/v2`: `indexes:batch-status` → poll → `fields` → `queries` → gzipped NDJSON results), without opening the models. Tally per model / DISC / federation, most-missing tokens, invalid DISC/SYS/FUNC codes, and tags **duplicated across models**. Chosen over Model Derivative (per-object, paginated) and AEC Data Model (Revit 2024+ only, admin activation, AMER/EMEA/AUS). Scope `data:read`, 3-legged, View+Download on each model's folder. A model whose index fails or times out is a failed read and the command returns Failed — never "0 elements" | Built, loopback-tested. **Not proved live**: the `columns` projection shape, `_RC` category names in a non-English Revit, and index time on a large federation are [U] |
| Workflows | `Data/WORKFLOW_KUT_CoordinationCycle.json` | Steps 3 and 4 are `ACC_PullClashes` and `ACC_SyncIssueStatus`. Step 7 `ACCPublish` is local only | Built |

**Not integrated at all today:** Docs folder discovery by path and permissions, naming standards,
Reviews (approvals), Transmittals, Sheets, RFIs, Submittals, Forms, Assets, Cost, Photos, Account
Admin, Webhooks, Data Connector, AEC Data Model, Parameters service. (Custom attributes and CDE
routing exist only as untested-live libraries; see above.)

---

## 2. Capability matrix

Effort: **S** ≤ 2 days · **M** ≤ 2 weeks · **L** > 2 weeks. Value is for KUT, where the
contracted role is information management, coordination and verification, not authoring
(`project-templates/KUT/README.md`).

| # | ACC capability | What STING has today | Integration value | Effort | API / scope / token | Risks and notes |
|---|---|---|---|---|---|---|
| 1 | **Docs: hubs, projects, folders** | Discovery (hubs and projects) | **High.** Resolve the four CDE-state folders (`00_WIP`, `01_SHARED`, `02_PUBLISHED`, `03_ARCHIVE`) by path, not by URN pasted in a box | S | DM `project/v1/hubs/{hub}/projects/{p}/topFolders`, `data/v1/projects/{p}/folders/{f}/contents`. `data:read`. 3-legged [C] https://aps.autodesk.com/en/docs/data/v2/reference/http/ | Folder permissions are **not** exposed by the public API [U]. Set them in the UI |
| 2 | **Docs: upload versions** | `AccModelUpload` (one folder URN) | **High.** Upload exports into the folder that matches the file's CDE state (`cdeFolders` + `AccCdeRouting`, wired into `ACC_UploadModel` on 2026-09-30: a state with no folder is refused, never sent elsewhere) | S | DM storage/items/versions + OSS signed S3. `data:create` [C] https://aps.autodesk.com/en/docs/data/v2/tutorials/upload-file/ | Not proved live. Upload now uses 16 MB parts in batches of 25 signed URLs, retries each part, renews expired URLs and sizes each part's timeout for a slow link; a restart still re-sends the whole file |
| 3 | **Docs: custom attributes on documents** | `AccDocsMetadata` (working tree): GET definitions, optional POST definition (`docsAttributesCreateMissing`), batch-update values. Wired into `ACC_UploadModel` (on when `docsAttributes` is true) | **High.** Suitability (S0–S7, A, B), revision, originator, ISO identifier and STING document id become ACC columns | S–M | `POST projects/{p}/versions/{v}/custom-attributes:batch-update` [C] https://aps.autodesk.com/en/docs/acc/v1/reference/http/document-management-custom-attributesbatch-update-POST/ . `data:write` | Definitions are per folder. `AccDocsMetadata` cites a POST custom-attribute-definitions endpoint (https://aps.autodesk.com/en/docs/acc/v1/reference/http/document-management-custom-attribute-definitions-POST/) [U: not run]. Creating them in the UI on day 1 is safer. Drop-list values must match STING vocabulary exactly (IM-14) |
| 4 | **Docs: naming standards** | STING ISO pattern builders (`ExportCenterModels.Iso19650Full`, `SheetNumberEngine`) | **High (config) / Medium (code).** ACC enforces names on upload; STING can pre-validate against `GET naming-standards/{id}` | S | `construction/…/naming-standards/{id}` (**beta**) [C, beta] https://aps.autodesk.com/en/docs/acc/v1/overview/ | STING's default `Iso19650Full` is **9 fields** (adds `-{Suitability}-{Revision}`). The KUT BEP §4.2 is **7 fields**. They must agree, or ACC rejects the uploads (see Day-1 playbook §3.3) |
| 5 | **Reviews (approval workflows)** | STING suitability picker; `Iso19650Suitability.CdeStateFor`; Document Manager state moves | **High.** SHARED → PUBLISHED authorisation becomes an auditable ACC review, not a STING-local state flip | M | Reviews API: GET reviews/workflows, **POST workflows**, create reviews [C] https://aps.autodesk.com/en/docs/acc/v1/reference/http/reviews-createworkflow-POST ; webhooks for Reviews [C] https://aps.autodesk.com/blog/webhook-api-support-acc-reviews-released | Whether POST *review* (not just workflow) is GA was not confirmed [U]. Reviewers need Docs permissions |
| 6 | **Transmittals** | `TransmittalOrchestrator`, `CreateTransmittalOrchestratedCommand`, B06 template, `transmittals.json` | **Medium.** *Read* ACC transmittals into STING's register so there is one register | S | **Read-only**: 5 GET endpoints (transmittals, recipients, folders, documents) [C] https://aps.autodesk.com/blog/autodesk-construction-cloud-transmittals-api-general-availability | **No API to create a transmittal.** On KUT, transmittals are issued in the ACC UI (BEP §5: "issued through the CDE"). STING's docx is supporting, not the record |
| 7 | **Sheets** | Export Centre PDFs, Sheet Manager, `SheetNumbering` | **Medium–High.** Publish issued PDFs as ACC Sheets version sets for site use | M | Sheets API: upload, version sets, publish, export. 2-legged or 3-legged, **3-legged recommended** [C] https://aps.autodesk.com/en/docs/acc/v1/overview/field-guide/sheets | Sheet numbers and titles are extracted by ACC OCR/metadata. Check that they match STING's ISO numbers |
| 8 | **Issues: full fidelity** | Push/pull/import. Title, description, type (working tree: assignee, due, `ACC_ImportIssues`) | **High.** Add root cause, custom attributes (ISO tag, clash signature), **linked documents** (no pushpins: see risks), comments, watchers | M | Issues v1 `projects/{id-without-b.}/issues`, attributes, root-cause-categories, comments [C] https://aps.autodesk.com/en/docs/acc/v1/reference/http/issues-issues-POST/ ; `b.` must be removed [C via APS Postman collection] https://github.com/autodesk-platform-services/aps-acc.issues.api-postman.collection | **Pushpins cannot be created by API** (read-only) [C]. Link the model document instead and let people place pins in ACC |
| 9 | **Issues: webhooks** | Polls by `ACC_SyncIssueStatus` | Medium. Near-real-time close/reopen instead of fortnightly polling | M | `autodesk.construction.issues` system [C] https://aps.autodesk.com/blog/webhook-api-acc-issue-released | Needs a public HTTPS receiver: Planscape server. The free Render instance cold-starts in more than 180 s (MEMORY), which risks webhook timeouts |
| 10 | **RFIs** | Template Engine RFI/TQ docs; `11_ISSUES/RFI` folder | Medium. Tender and construction queries (Playbook stage 4–5) | M | RFIs API with write support (assign, transition, responses) [U: summary] https://aps.autodesk.com/en/docs/acc/v1/overview/ | Not needed in Stage 0–2. Design-stage queries use Issues |
| 11 | **Submittals** | `CSI_Assign`, `SpecLink_Reconcile` (MasterFormat primary on KUT) | Medium (construction). Spec sections in ACC can be seeded from the CSI sections STING already assigns | M | Submittals API: read + **POST items, POST specs** [C] https://autodesk-developer.zendesk.com/hc/en-us/articles/32600297046797 | Construction-stage. Submittal ownership sits with the contractor |
| 12 | **Model Coordination** | Pull clashes, triage, escalate | **High (existing).** Add: list model-set **views**, clash-group status, **write back** closed or not-an-issue | S–M | `bim360/modelset/v3`, `bim360/clash/v3`. MC container = **project id without `b.`** [C] https://github.com/Autodesk-Forge/forge-model.coordination.api-postman.collection ; sample https://github.com/autodesk-platform-services/aps-clash-data-view | Tests/resources sub-paths are the one residual left from the sample [U]. No public API creates model sets or clash tests [U]. Set them up in the UI |
| 13 | **Forms / Checklists** | Cx task library (`STING_CX_TASKS.json`), QR commissioning, healthcare checklists | Medium (Stage 3). Push STING Cx tasks as ACC Forms and pull completed forms as commissioning evidence | M | Forms API (templates, forms, form values) [U: scope of write support] https://aps.autodesk.com/en/docs/acc/v1/change_history/forms_v1_changelog | Templates are built in the UI [U] |
| 14 | **Assets** | ISO 19650 asset tag `ASS_TAG_1_TXT` (8-segment), COBie Type/Component/System maps, Cx status | **High at handover.** ACC Assets becomes the field and FM asset register, keyed by the STING tag | M–L | Assets v2: categories, status-step-sets, custom attributes, `assets:batch-create/patch` [C] https://aps.autodesk.com/en/docs/acc/v1/overview/field-guide/assets . Scopes `data:read/write/create` [C] https://aps.autodesk.com/en/docs/acc/v1/tutorials/assets/create-assets-project-settings | **No batch create for custom-attribute definitions**: one call per attribute [C]. Map: category ← COBie Type or STING SYS/PROD; `clientAssetId` ← STING tag; status set ← Cx steps; custom attributes ← COBie attributes. Decide the key once, because renumbering later breaks the FM link |
| 15 | **Cost Management** | BOQ (NRM2/CSI), `CostPlan_*`, `Var_*` (variations), `PayCert_*`, EVM | Low for KUT (IM role is not QS). High product value | L | Cost API: budgets, cost items, change orders (PCO/RFQ/RCO/OCO/SCO), main contracts, custom attributes [C partial] https://aps.autodesk.com/en/docs/acc/v1/reference/http/cost-main-contracts-GET . Limit **5000 req/min overall, 300 req/min per endpoint per app** [C] https://aps.autodesk.com/en/docs/acc/v1/overview/rate-limits/cost-management-rate-limits%20/ | Needs the ACC Cost module licence on the project. Budget codes must follow the project's segment structure |
| 16 | **Photos** | SitePhotos (PR #550 not merged) | Low now | M | Photos API [U: availability and shape] | Construction stage |
| 17 | **Account / Project Admin** | Planscape members (server) | Medium. Sync ACC project members, companies and roles into STING's responsibility matrix and distribution groups | M | Admin API. Writes need a 3-legged token, or 2-legged **with user impersonation** (`User-Id`). Pure 2-legged writes are not supported [C] https://aps.autodesk.com/blog/acc-project-admin-api-project-creation-and-user-management . **Admin API needs the region header** [C] https://aps.autodesk.com/en/docs/acc/v1/overview/field-guide/admin | Account-admin privileges. `account:read`/`account:write` |
| 18 | **Webhooks (DM)** | None | Medium. `dm.version.added` on `01_SHARED` triggers STING QA or clash on the server | M | `dm.version.added`, `dm.folder.added`, etc. [C] https://aps.autodesk.com/en/docs/webhooks/v1/reference/events/dm.version.added/ ; `region`/`x-ads-region` [C] | Public HTTPS receiver needed (see #9) |
| 19 | **Data Connector** | None | Medium. Scheduled bulk extracts (issues, RFIs, submittals, cost, admin) feed the monthly KPI report (`Owner_KpiDashboard`) without paging through every API | M | Data Connector API [C] https://aps.autodesk.com/en/docs/acc/v1/overview/ | Normally requires **account admin** [U: project-admin support]. Output is CSV in a zip. Good for reporting, bad for round-trip |
| 20 | **AEC Data Model (GraphQL)** | None. Compliance needs Revit open | **High (Q1+).** Read element properties, including STING shared parameters, from published Revit models **without Revit**: headless tag compliance, LOD and COBie checks on the server | M–L | AEC Data Model API (GraphQL) [U: works for Revit 2024+ models published to ACC. Whether it covers uploaded non-cloud RVTs is **unconfirmed**] | Strong argument **for** cloud models if the teams use them. Rate limits apply to GraphQL points [U] |
| 21 | **Parameters service** | `MR_PARAMETERS.txt` (3,330 params, GUID-stable), resolver pipeline | Medium. Publish STING parameter groups as an ACC account collection, so teams load STING params in Revit from ACC instead of a `.txt` | M | Parameters API (GA) [C] https://aps.autodesk.com/blog/parameters-api-announcing-general-availability | **GUID preservation on import is unconfirmed [U].** A GUID change breaks every tag and schedule. Keep `MR_PARAMETERS.txt` authoritative until proven |
| 22 | **Revit Cloud Worksharing (cloud models)** | **Not handled.** `StingPaths`/`ProjectFolderEngine` assume `doc.PathName` is a local path | **Critical to decide.** See §3 | S (config) / M (code) | Revit API `Document.IsModelInCloud`, `GetCloudModelPath()`, `ModelPath.GetProjectGUID()`, `Document.CloudModelGUID` [C] https://help.autodesk.com/cloudhelp/2024/CHS/Revit-API/files/Revit_API_Developers_Guide/Introduction/Application_and_Document/Revit_API_Revit_API_Developers_Guide_Introduction_Application_and_Document_CloudFiles_html.html | `PathName` is `Autodesk Docs://<project>/<model>.rvt` [C] |

### Cross-cutting API facts

| Topic | Fact | Tag |
|---|---|---|
| APS app type | "Traditional Web App" issues a secret and supports 2- and 3-legged. "Desktop, Mobile, Single-Page App" has no secret and uses PKCE (3-legged only). **Recommended for STING: the Desktop app**, because no secret sits on the workstation. Callback must match exactly, with no localhost wildcards | [C] https://get-started.aps.autodesk.com/ , https://aps.autodesk.com/en/docs/oauth/v2/developers_guide/App-types/ |
| ACC access gate | The **Account Admin must add the APS Client ID under ACC Account Admin → Custom Integrations** (tick Document Management and Account Administration). This applies to **3-legged too**. Without it, hubs or projects come back empty or 403 | [C] https://help.autodesk.com/cloudhelp/ENU/Docs-Admin/files/hub-administration/Custom_Integrations.html |
| Project id forms | DM uses `b.<guid>`. **Issues and Model Coordination use `<guid>` without `b.`** | [C] (Issues Postman collection, MC Postman collection) |
| Region | ACC data lives in US or EMEA (also AUS). Data in one region is not visible from the other. Admin API needs the region header. Webhooks take `region`/`x-ads-region`, default US | [C] https://aps.autodesk.com/en/docs/acc/v1/overview/field-guide/admin |
| Region header | `x-ads-region` values: US (default), CAN, EMEA, GBR, DEU, IND, JPN, AUS. Working tree sends it when `region` is set (`AccIds.ApplyRegion`). Whether Issues/DM/MC **require** it for non-US hubs, or route by project | Values [C]; necessity per API [U] |
| Rate limits | 429 + `Retry-After`. Cost: 5000/min overall, 300/min per endpoint per app. Increases are for ADN members only | [C] https://aps.autodesk.com/blog/rate-limiting-and-how-request-limit-increases |
| Other rate limits | Issues: 500 req/min per user per hub [C]. DM per-endpoint limits: https://aps.autodesk.com/en/docs/data/v2/developers_guide/rate-limiting/dm-rate-limits/ . MC figures not confirmed | Issues [C], MC [U] |
| Refresh token | Single-use, rotates on every refresh, **15-day** lifetime. A fortnightly cycle that is skipped once needs a fresh sign-in. Working tree records `RefreshTokenIssuedAt` for a lapse warning | [C] https://aps.autodesk.com/en/docs/oauth/v2/developers_guide/refresh_token/ |
| Issue pushpins | **Cannot be created through the API** (read-only). Linked documents are possible; the pin stays manual | [C] |
| Model Coordination auth | `bim360/modelset/v3`, `bim360/clash/v3` accept **3-legged tokens only** | [C] |
| Scopes | Requested at sign-in. Changing them needs a new consent: a token minted without `data:create` can never upload | [Code] `AccOAuthFlow.DefaultScope` comment |

---

## 3. Revit Cloud Worksharing: what it does to StingTools

**Finding [Code, not run]:** nothing in `StingTools/` references `IsModelInCloud`,
`GetCloudModelPath`, `CloudModelGUID` or the `Autodesk Docs://` scheme. Every project path goes
through `Core/StingPaths.cs` → `Core/ProjectFolderEngine.GetRootPath(doc)`, which derives the
root from `Path.GetDirectoryName(doc.PathName)`.

For a cloud model, `doc.PathName` is `Autodesk Docs://<ACC project>/<model>.rvt` [C]. Walking
`GetRootPath` (`ProjectFolderEngine.cs` ~L225–290):

| Step | For a cloud model | Result (inferred) |
|---|---|---|
| 0. ES root stamp (`StingProjectRootSchema.ResolveStampedRoot`) | Combines a relative stamp with `GetDirectoryName("Autodesk Docs://…")` → `"Autodesk Docs:\…"`, which is not a real directory | null → next step |
| 1. Persisted `ProjectSetup` (`LoadOrDetectSetup`) | Looks for `<"Autodesk Docs:\KUT">/<CODE>/_data/project_setup.json`. `Directory.GetFiles` on that path throws, and the exception is caught | null → next step |
| 2. Per-doc cache | Empty | → next step |
| 3. Global `PROJECT_FOLDER_ROOT` (from `project_config.json`) | **Used if set.** It applies to **every** project on that machine | Shared root, if configured |
| 4. `<projDir>/<CODE>` | `Directory.CreateDirectory("Autodesk Docs:\KUT\<CODE>")` is expected to fail (invalid path syntax) | → next step |
| 5. `%USERPROFILE%\Documents\<CODE>` | Created | **Per user, per machine** |

**Consequences if STING runs on a cloud model with no `PROJECT_FOLDER_ROOT`:**

1. `_data/coord` (the `_BIM_COORD` alias) lands in each user's `Documents\<CODE>`. That covers
   `acc/acc_settings.json`, `acc/pushed_clashes.json`, issue and meeting stores, the document register,
   `doc_sequences.json` and the audit log. **Every user gets a private copy.** Two people running
   `ACC_PullClashes` escalate the same clashes twice. Document numbering counters diverge. The
   SHA-256 audit chain forks.
2. `StingProjectRootSchema.EnsureStamped` computes a relative path between the bogus `rvtDir` and
   the Documents fallback, and **writes that into Project Information in the shared cloud
   model**. In a workshared model this also borrows the Project Information element [U].
3. Export routing (`OutputLocationHelper`, Export Centre) goes to the same per-user folder. That
   is recoverable, because deliverables go to ACC anyway.
4. The per-document caches are keyed on `doc.PathName`, which is stable for a cloud model, so no
   cross-project bleed is expected [Code].

**Needs one live check** before anyone relies on the table above: open a cloud model, run
`Folders_Consolidate` preview or any command that logs its root, and read the root from
`StingTools_yyyyMMdd.log`.

**Code change recommended (Q1, M effort):** a `CloudRootResolver` step inserted before step 0.
When `doc.IsModelInCloud`:

- key the root by `GetCloudModelPath().GetProjectGUID()` (the ACC project GUID) in a **machine-level
  map** (`%APPDATA%\Planscape\cloud_roots.json`: projectGuid → local folder);
- refuse to stamp ES;
- **auto-fill the ACC `projectId`** from the same GUID. That removes the discovery step and the
  `b.` ambiguity for cloud-hosted projects.

That is a per-project mapping, not a machine-global one, so it is safe with two projects open.

---

## 4. Risks and defects found during this review

| Id | Finding | Severity for tomorrow | Status |
|---|---|---|---|
| **R1** | **`b.` prefix.** "Find my ACC project" writes the DM id `b.<guid>` into ProjectId. HEAD `AccIssueSync` uses it verbatim **and** uses the path `construction/issues/v1/containers/{id}`. ACC documents `construction/issues/v1/projects/{id-without-b.}`. HEAD `AccModelCoordSync` also uses the id verbatim, but MC wants no `b.`. On a HEAD build, **every Issues and MC call is expected to 404** after discovery | **Blocker on the deployed DLL** | Working tree fixes **both** Issues (path and prefix) and MC (`AccModelCoordSync` L125/L169). Not deployed. Workaround on the old DLL: bare GUID in both boxes; MC pull then works, Issues push does not (path) |
| R2 | `EnsureIssueTypeAsync` takes the first "clash/coordination" type and `subtypes[0]`, without checking `isActive` | Medium | Configure ACC so the Coordination type's **first** subtype is "Clash", or pin `issueTypeId`/`issueSubtypeId` (working-tree keys) |
| R3 | `acc_settings.json` strictness: **an unknown key discards the whole file**. The working-tree build accepts 14 more keys (`region`, `cdeFolders`, …). A file written for the new build and read by an older one reverts **everything** to prompting | Medium | Only write keys that the **deployed** build lists (see Day-1 §6) |
| R4 | Cloud models: sidecars become per user (§3) | High if the IM runs STING against cloud models | Configuration workaround today (Day-1 §9). Code fix in Q1 |
| R5 | `ProjectFolderEngine.MirrorToACC` is a manifest-only stub whose name implies an upload | Low | Rename or remove, and use `AccModelUpload` |
| R6 | Credentials: the HEAD file holds the client secret and refresh token in plain text in `%APPDATA%` | Medium | Working-tree `AccCredentialStore` (DPAPI), plus a PKCE app, which removes the secret entirely. One named IM machine regardless |
| R7 | Export filename (9-field `Iso19650Full`) vs BEP naming (7-field) vs ACC naming-standard enforcement | Medium | Decide on Day 1 (playbook §3.3) |
| R8 | The MC `tests`/`resources` sub-paths and the upload path have never been run against a tenant | Known residual | First live pull (runbook B1 step 6) |
| R9 | Refresh-token lifetime (15 d [C]) is close to the fortnightly cadence | Low–Medium | Working-tree `RefreshTokenIssuedAt`. Operationally, run `Test / Refresh` **daily** (operating model §8) |
| **R10** | ~~PKCE unreachable from the UI~~ **Closed 2026-09-30**: the card now requires only the Client ID (plus a completed sign-in for Test / Discover) | Was a blocker for the Desktop-app registration | Deploy the branch build |
| **R11** | **Deployed DLL is 2026-09-29, pre-fix.** None of the working-tree ACC fixes reach Revit until someone builds and runs `deploy.bat` | **Blocker** for Issues push and for PKCE | Build 0/0 → tests (`StingTools.Acc.Tests`) → `deploy.bat` from the checkout to go live → grep `<Assembly>` → restart Revit. Tonight, or early tomorrow before sign-in |
| R12 | `cdeFolders` and `docsAttributes` are **parsed but not used** by the upload command. A user who sets them could believe uploads are routed | Low | Say so on the card, or wire them in (B1/B2) |

---

## 5. Prioritised roadmap

### (a) Must work tomorrow (configuration, one small UI fix and a deploy: **no new features**)

| # | Item | Type | Touches |
|---|---|---|---|
| A1 | **Relax the three ACC-card secret guards (R10)**, build 0/0, run `StingTools.Acc.Tests`, **`deploy.bat`**, and confirm `<Assembly>` (R11). The `AccIds` fix already covers Issues **and** MC in the working tree | Code (small) + deploy | `UI/BIMCoordinationCenter.cs`, `V6/AccIds.cs`, `V6/AccIssueSync.cs`, `V6/AccModelCoordSync.cs` |
| A2 | APS app: **Desktop, Mobile, Single-Page App (PKCE)** if A1 lands, otherwise Traditional Web App. Callback exactly `http://localhost:8910/callback`; API access Data Management + ACC + BIM 360. Then **ACC Custom Integration** by the Account Admin | Config | none |
| A3 | ACC project setup: CDE folders, naming standard, Coordination issue type with "Clash" first, a model set on the SHARED models, IM as project admin | Config | none |
| A4 | Sign in, discover, save project scope, then pull clashes on a set **known to have clashes** (runbook B1 step 6) | Verification | `AccPullClashesCommand` |
| A5 | Decide local vs cloud hosting for the **IM's STING host model** (recommended: local, see Day-1 §9) | Decision | `StingPaths` behaviour |
| A6 | Settle the filename pattern (7-field) in the Export Centre profile and the ACC naming standard | Config | `ExportCenterModels` profile |

### (b) First two weeks

| # | Item | Effort | Touches |
|---|---|---|---|
| B1 | **Wire the existing `AccCdeRouting` into upload** (library + key already exist): `cdeFolders` map (WIP/SHARED/PUBLISHED/ARCHIVE → folder URN), resolved from folder *paths* via `topFolders` + `contents`. `ACC_UploadLastBundle` and a new "upload Export Centre set" put each file in its suitability's folder (`Iso19650Suitability.CdeStateFor`) | S–M | `V6/AccModelUpload.cs`, `Docs/ExportCenterEngine.DeliverableFolderForSheet`, `Clash/AccUploadModelCommand.cs` |
| B2 | **Wire the existing `AccDocsMetadata` into upload**: Suitability, Revision, Originator, ISO id, STING doc_id (`docsAttributes`). Attribute definitions are created by the IM in ACC | S–M | `AccModelUpload`, `DocumentRegisterMerge.MapRegisterRow` |
| B3 | **Richer escalated issues**: assignee (company or role), due date, root cause, custom attribute `STING clash signature`, **linked document** (the clashing model's URN from the scope file; pushpins are not API-creatable) | M | `AccIssueSync.PushIssueAsync`, `AccPullClashesCommand`, `AccModelCoordSync` (keep document URNs from the scope file) |
| B4 | **Issue-type selection hardening**: skip inactive types and subtypes; choose the subtype by name ("Clash") | S | `AccIssueSync.EnsureIssueTypeAsync` |
| B5 | **ACC read of transmittals and reviews into STING's register**, so the Document Manager shows what ACC issued (read-only) | M | `DocumentManagementDialog`, `CoordStores.Transmittals`, new `V6/AccDocsSync.cs` |
| B6 | **Cloud-model guard**: when `IsModelInCloud`, log the resolved root prominently, refuse the ES root stamp, and show a BCC banner. That stops silent per-user forks before the full resolver exists | S | `ProjectFolderEngine.GetRootPath`, `StingProjectRootSchema.EnsureStamped` |
| B7 | **Pre-upload naming check** against the ACC naming standard (beta GET), so a bad name fails in STING with a reason and not in ACC | S | `ExportCenterEngine`, new call in `AccModelUpload` |

### (c) First quarter

| # | Item | Effort | Touches |
|---|---|---|---|
| C1 | **Cloud-model root resolver** keyed by ACC project GUID, with auto-fill of `projectId` (§3) | M | `ProjectFolderEngine`, `StingPaths`, `AccProjectSettingsFile` |
| C2 | **Reviews write**: start an ACC approval workflow when STING moves a set SHARED → PUBLISHED. Webhook or poll the outcome back into the suitability | M | Document Manager, `IssueDeliverableCommand`, `PublishDeliverableCommand` |
| C3 | **Sheets publishing** of Export Centre PDF sets as ACC version sets | M | `ExportCenterEngine`, new `V6/AccSheets.cs` |
| C4 | **Assets bridge**: ISO tag → `clientAssetId`, COBie Type → category, Cx steps → status set, COBie attributes → custom attributes. Idempotent upsert, and a reconcile report (STING ↔ ACC Assets) | L | `Docs/HandoverExportCommands.cs` (COBie), `COBIE_TYPE_MAP.csv`, `STING_CX_TASKS.json`, new `V6/AccAssets.cs` |
| C5 | **Server-side ACC**: token vault, webhooks (`dm.version.added` on SHARED, issues events) → Planscape server. Needs a non-sleeping instance | M–L | `Planscape.Server/.../Services/Aps/`, `AccConnector` |
| C6 | **Data Connector** extract → `Owner_KpiDashboard` monthly report (issues ageing, RFIs, reviews) | M | `Commands/Kpi/OwnerKpiDashboardCommand.cs` |
| C7 | **AEC Data Model** spike: headless tag-compliance / LOD check on published models | M | new server job, `ComplianceScan` logic reuse |
| C8 | **Account Admin** read: members, companies and roles → STING responsibility matrix and distribution groups | M | `ProjectMembersController`, `distribution_groups.json` |
| C9 | **Parameters service** spike: prove GUID-preserving publish of one `MR_PARAMETERS` group before any wider use | S–M | `tools/` generators |
| C10 | RFIs / Submittals / Forms / Cost as construction starts (Submittals seeded from `CSI_Assign` sections; Cost from BOQ and variations) | L | `Commands/Classification/CsiCommands.cs`, `BOQ/`, `Commands/Cost/` |

### Deliberately not recommended

- **Creating ACC transmittals from STING.** The API is read-only [C]. Do not build a
  browser-automation workaround.
- **Creating model sets or clash tests by API.** No public write API was found [U]. They are
  one-off UI tasks.
- **Replacing ACC's clash engine.** ACC stays the system of record. STING adds triage and the
  tolerance matrix (`WORKFLOW_KUT_CoordinationCycle.json` already states this).
