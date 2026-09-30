# KUT × ACC: Day-1 playbook for the Information Manager

**For:** Mayanja Davis (Information Manager, Planscape) on day 1 of KUT on Autodesk Construction
Cloud. **Written 2026-09-30, revised 21:30 the same day** against the working tree of
`claude/acc-work-review-gaps-7e2ac7`. This is a checklist. The reasoning is in
[`ACC_INTEGRATION_STRATEGY.md`](ACC_INTEGRATION_STRATEGY.md), the proof steps are in
[`KUT_LIVE_VERIFICATION_RUNBOOK.md`](KUT_LIVE_VERIFICATION_RUNBOOK.md) §B1, and the way of working
(repos, Cowork, snapshots, defect loop) is in [`KUT_OPERATING_MODEL.md`](KUT_OPERATING_MODEL.md).

**[U]** marks a fact that has not been confirmed. Everything else was checked against APS
documentation or the StingTools source.

> **Read §0 first.** One issue decides whether any of the rest works tomorrow.

---

## 0. Go / no-go: which StingTools build is Revit loading?

**Measured at 21:15 on 2026-09-30:**

```
grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u
→ C:\Dev\STINGTOOLS\CompiledPlugin\StingTools.dll   (built 2026-09-29 04:58)
```

That DLL contains **none** of the ACC fixes in progress: no `AccIds`, no PKCE, no
`ACC_ImportIssues`. (Checked by searching the binary for those names.) It behaves like
committed `main`:

- Discovery returns the project id in Data Management form, `b.<guid>`.
- The ACC Issues and Model Coordination APIs want the **bare `<guid>`**. On this build the Issues
  client also calls the undocumented path `…/issues/v1/containers/{id}`; the documented one is
  `…/issues/v1/projects/{id}`.
- Sign-in needs a **Client Secret**, because PKCE is not there.
- `acc_settings.json` understands only the 7 original keys (see §6).

**The branch `claude/acc-work-review-gaps-7e2ac7` fixes all of this** (plugin build 0 errors /
0 warnings, `StingTools.Acc.Tests` 279/279, 2026-09-30). `AccIds.ForAcc` is applied in **both**
`AccIssueSync` and `AccModelCoordSync`. PKCE is in `AccOAuthFlow`, and the ACC card accepts an
empty Client Secret for Sign in, Test / Refresh and Find my ACC project. `x-ads-region` is sent.
DPAPI protects the credentials (`AccCredentialStore`). `ACC_UploadModel` (and the card's upload
button, which now runs that command) routes by CDE state (`cdeFolders`) and stamps ISO 19650
attributes (`docsAttributes`). `ACC_ImportIssues` is dispatched. What remains is **deploying it**.

### Decision tree for tomorrow morning

| Situation at 08:00 | Do this |
|---|---|
| **A.** The branch is built and `deploy.bat` has been run from its checkout (or from `main` after merge), and `<Assembly>` points at it | Register a **Desktop app (PKCE)** (§1A), sign in with **no secret**. Issues push, MC pull, upload and `ACC_ImportIssues` all expected to work. Full §7 |
| **B.** New build deployed, but you already have a Traditional Web App registration | Keep it (§1B) and enter the secret; it is DPAPI-protected on this build. Everything else as A |
| **C.** Still on the 2026-09-29 DLL | Traditional Web App (§1B). Enter **Issues Project ID** and **Coord Container ID** as the **bare GUID** (strip `b.`). Plan the day around **clash pull + triage + CSV** (V1–V4). **Do not attempt V5 (issue push)** because the path is wrong on this build: escalate through `ClashBcfExport` → import into ACC. Leave Upload Folder URN as a full URN |

Registering both app types is allowed. The app type can be changed later without touching
ACC, apart from adding the second Client ID under Custom Integrations (§2).

---

## 1. APS app registration (Owner or Autodesk account holder, ~15 min)

At https://aps.autodesk.com → *My Apps* → *Create application*.

### 1A. Recommended: Desktop app with PKCE (no secret)

| Setting | Value | Why |
|---|---|---|
| Application type | **Desktop, Mobile, Single-Page App** | A public client. There is **no secret** to leak, store or rotate. StingTools signs in with PKCE (RFC 7636, S256) and refreshes with `client_id` in the body |
| Callback URL | `http://localhost:8910/callback` | **Exactly** this: `http`, `localhost` (not `127.0.0.1`), port `8910`, path `/callback`, no trailing slash. APS does not allow wildcard localhost callbacks |
| API access | **Data Management API**, **Autodesk Construction Cloud API** (newer consoles label it "Forma"), **BIM 360 API** (Model Coordination lives under `bim360/…`) | Tick what the console offers for these families [U: exact labels] |
| Scopes | Nothing to configure in the app. StingTools asks for `data:read data:write data:create account:read` at sign-in | A token minted without `data:create` can never upload. Changing scopes needs a fresh sign-in |

Requires the build in situation **A** (§0).

### 1B. Fallback: Traditional Web App (secret)

Same callback and APIs. It issues a **Client Secret**. Enter it only on the IM machine: never by
email, never in a repo, never in `kut-config`. Works on every build.

Record the Client ID(s) in `kut-config/acc-setup/` (a Client ID is not a secret).

## 2. ACC Account Admin: Custom Integration (**easy to miss; without it nothing lists**)

ACC → **Account Admin → Custom Integrations → Add custom integration** → paste the **Client ID**
→ tick **Document Management** and **Account Administration** → Save.
Source: https://help.autodesk.com/cloudhelp/ENU/Docs-Admin/files/hub-administration/Custom_Integrations.html

This is required for 3-legged sign-in as well. If it is missing, **Find my ACC project** returns
no hubs, or the KUT hub does not appear.

**Who:** whoever administers the ACC account that holds KUT (the Owner's or the lead appointed
party's). Ask for it **today**, because it depends on that person's calendar.

---

## 3. ACC project setup that StingTools depends on

### 3.1 Members and roles

| Who | ACC products | Access |
|---|---|---|
| Information Manager (the account that signs in from STING) | Docs, Model Coordination, Issues (Build/Collaborate as licensed) | **Project Admin**. Needed to create folders, attributes, the naming standard, issue types and model sets |
| Task team leads (per originator) | Docs, Model Coordination, Issues | Member, with folder permissions below |
| Client / Owner reviewers | Docs (Reviews), Issues | Member, view/review on SHARED and PUBLISHED |

Issues that STING creates appear **as created by the signed-in user**. Use the IM's named
account, which gives a clear audit trail. A shared service account needs a licensed seat and
can come later.

### 3.2 Folder structure (ISO 19650 CDE states)

Mirror StingTools' CdeFirst tree (`Core/ProjectSetup.cs` `CdeFirstFolderDefaults`), so the same
state names appear locally and in ACC:

```
Project Files/
├── 00_WIP/                    task-team private (ISO 19650 WIP, suitability S0)
│   ├── <ORIG>_<Discipline>/   one per task team, e.g. SMB_Architecture — only that team + IM
├── 01_SHARED/                 released for coordination (S1–S7)
│   ├── Models/                ← the Model Coordination model set points here
│   ├── Drawings/
│   └── Documents/
├── 02_PUBLISHED/              authorised (A1…/B1…), client-contractual
│   ├── Models/ Drawings/ Documents/ COBie/
├── 03_ARCHIVE/                superseded / historic (AB/AR)
└── 99_COORDINATION/           optional: STING clash CSV/BCF, KPI reports (IM only)
```

**Permissions:** each team has Edit on its own WIP subfolder only. Everyone has View on SHARED.
Only the IM and approvers can upload to PUBLISHED; everything else reaches it through Reviews.
Folder-level permissions are set in the UI; the API does not expose them [U].

Once the folders exist, write their paths into your local notes. Automatic state-to-folder
routing (`cdeFolders`) is **not** in the deployed build yet (see §6).

### 3.3 Naming standard (Docs → Settings → Naming standard)

The KUT BEP §4.2 fixes a **7-field** name:
`[Project]-[Originator]-[Volume]-[Level]-[Type]-[Role]-[Number]`, e.g. `KUT-SMB-ZZ-XX-M3-A-0001`.

| ACC naming-standard field | Type | Values |
|---|---|---|
| Project | Fixed | `KUT` |
| Originator | List | 3-character codes from the originator register |
| Volume | List | `00`, `01`–`06`, `ZZ`, `XX` |
| Level | List | as agreed in the BEP (`GF`, `01`…, `ZZ`, `XX`) |
| Type | List | `M3`, `DR`, `SH`, `SP`, … |
| Role | List | `A C E I M P Q S W X Y Z` |
| Number | Free text, 4 digits | `0001` |
| Delimiter | `-` | |

Apply it to `01_SHARED`, `02_PUBLISHED` and `03_ARCHIVE` (optionally WIP).

**File names are 7 fields; suitability and revision are ACC attributes.** The Export Centre's
older ISO preset (`ExportNamingPresets.Iso19650Full`) produced **9 fields**
(`…-{Role}-{SheetNumber}-{Suitability}-{Revision}`). ACC rejects those in a folder that enforces
the 7-field standard, and — because ACC matches an existing item by file name — a name that
changes at every revision makes each revision a **new item** instead of version 2 of the same one,
splitting its history, markups and reviews. So:

- The Export Centre has a **`ISO 19650 (7-field — ACC / KUT)`** preset (`{IsoName}`: the sheet's
  assembled ISO identifier when it has one, else
  `{ProjectCode}-{Originator}-{Volume}-{Level}-{Type}-{Role}-{SheetNumber}`).
- It is used **automatically** in place of the 9-field preset whenever the project's
  `acc_settings.json` configures ACC (a `projectId`, `folderUrn` or `cdeFolders`). Set
  `"fileNamingFields": 9` to opt out, or `7` to force it without ACC settings.
- An ACC upload **refuses** a file whose name ends in `-{Suitability}-{Revision}` when the
  7-field rule applies, and reads the target folder's naming standard first: a name with the
  wrong number of fields is refused before any bytes are sent. A standard STING cannot interpret
  is reported ("NOT validated"), never guessed. [U: the naming-standard response shape is not
  confirmed against a live tenant — only the delimiter and field count are enforced.]
- Suitability and revision travel as **ACC custom attributes** (§3.4).

### 3.4 Document custom attributes (Docs → folder → Settings → Attributes)

Create these on `01_SHARED` and `02_PUBLISHED` (and `00_WIP` / `03_ARCHIVE` if files are
uploaded there). STING writes to them **by name**, so use the names **exactly** — or put your own
names in `acc_settings.json` `"docsAttributeNames"` (below). There is ONE set: STING does not
create a second "suitability" column beside yours.

| Attribute (default name) | Settings key | Type | Value STING writes |
|---|---|---|---|
| `Document Number` | `documentNumber` | Text | the ISO 19650 identifier (register `doc_number`, else the file name) |
| `Suitability` | `suitability` | Drop-down **or** Text | `S0 S1 S2 S3 S4 S5 S6 S7 A1 A2 A3 A4 A5 B1 B2 B3 B4 B5 CR AB AR` (trim to what the BEP uses) |
| `Revision` | `revision` | Text | `P01`… / `C01`… — the sheet's own revision; left **unset** when it has none, never defaulted |
| `CDE State` | `cdeState` | Drop-down **or** Text | `WIP SHARED PUBLISHED ARCHIVE` — derived from the suitability |
| `Originator` | `originator` | Text | `PRJ_ORG_ORIGINATOR_CODE_TXT` |
| `STING Transmittal Id` | `transmittalId` | Text | the STING transmittal, for an ACC Publish bundle |

To use other names, e.g. an existing `Status Code` column:

```json
"docsAttributes": true,
"docsAttributeNames": { "suitability": "Status Code", "revision": "Rev" }
```

A key not given keeps its default; an unknown key or two roles sharing one name makes the whole
settings file invalid (reported by `ACC_SelfCheck`), rather than silently writing elsewhere.

With a **drop-down**, STING checks the value against the drop-down's list **before** writing: a
suitability that is not on the list writes nothing (no partial stamp) and the upload result says
which value was refused. Uploads with `"docsAttributes": true` fill these automatically; without
it, fill them by hand in ACC.

### 3.5 Issues settings (Issues → Settings)

- **Issue type `Coordination`** with subtypes in this order: **`Clash`** first, then
  `Design coordination`, `Missing information`.
  StingTools picks the first type whose title contains "clash" or "coordination", and then
  **the first subtype of that type** (`AccIssueSync.EnsureIssueTypeAsync`). Put `Clash` first, and
  do not leave an older, **inactive** type called "Coordination" in the list, because the code
  does not skip inactive types.
- Additional types: `Lifecycle gap` (for `KutPushLifecycleGapsToAcc`), `Design`, `Quality`.
- **Root causes:** Design coordination · Clash · Missing information · Standards non-compliance ·
  Client change. (StingTools does not set root causes yet. This is for the people triaging.)
- Issue custom attribute `STING signature` (text). It is used in two weeks; creating it now avoids
  a re-configuration.

### 3.6 Model Coordination

- Create a **coordination space / model set** called `KUT – Federated (SHARED)` that watches
  `01_SHARED/Models`.
- Define clash tests per the BEP clash matrix (Playbook 0.7). ACC re-runs them when models
  update.
- **Before running STING's pull, make sure at least one test has completed and shows clashes.**
  A clean set proves the plumbing but not the clash sub-paths (runbook B1).
- Model sets and clash tests are UI-only; no public write API was found [U].

---

## 4. Sign in and discover the project from StingTools

Revit → STING dock → **BIM Coordination Center → Platforms → ACC** card.

1. **Open the KUT host model first**, saved to its **local** folder (§9). The project settings
   file is resolved from the open model. With an unsaved model, nothing project-scoped can be
   saved.
2. Enter the **Client ID**. Enter a **Client Secret** only for a Traditional Web App (§1B). Then
   **💾 Save Credentials**.
3. **🔓 Sign in with Autodesk.** A browser opens; sign in as the IM. Expect a page reading
   *"Signed in to Autodesk ✓"*. The status line reads *"Signed in to Autodesk — tokens stored."*,
   and the log has `AccOAuthFlow: Autodesk sign-in succeeded (PKCE public client)` (or
   `confidential client`).
4. **🔌 Test / Refresh Token.** It must succeed; this proves the refresh token works.
5. **🔎 Find my ACC project** → pick `KUT` (shown as `<Hub> / <Project> [b.…]`). The `b.` id is
   written as is. On a situation **A/B** build it is stripped per call. On situation **C**, edit
   both ID boxes to the bare GUID now, then Save.
6. **Coord Container ID:** leave it empty (it falls back to the project id) on A/B. On C, enter
   the bare GUID.
7. **Issue Type ID:** leave it empty. It is resolved on the first push, or pinned via
   `issueTypeId` (§6).

**Credentials** live in `%APPDATA%\Planscape\acc_credentials.json`: machine- and user-scoped,
DPAPI-protected on the new build, never in the model and never committed. Container ids and the
other project values live **with the project** (next section).

## 5. Where the project settings live

The card's **PROJECT ACC SETTINGS** heading shows the exact path it is using; trust that line.
With the default layout it is:

```
<folder of the host .rvt>\<PROJECT_CODE>\_data\coord\acc\acc_settings.json
```

(`_BIM_COORD` is an alias for `_data\coord`; see `Clash/AccProjectSettingsFile.PathFor`.) Keep the
reviewed copy in `kut-config/acc/` (operating model §3).

## 6. `acc_settings.json` keys (from `AccOperatingPolicy.KnownKeys`)

Every key is optional; an absent key means *prompt / not configured*. Keys starting with `_` are
comments. **Any other unknown key makes the whole file Malformed**: every setting reverts to
prompting, and the card shows *"could NOT be read (…carries key(s) this build does not read…)"*.

| Key | Type | In the 2026-09-29 DLL? | Meaning |
|---|---|---|---|
| `projectId` | string | yes | ACC project id (`b.` accepted on the new build) |
| `coordContainerId` | string | yes | Model Coordination container; empty = `projectId` |
| `coordModelSetId` / `coordModelSetName` | string | yes | Remembered model set (the name is for messages only) |
| `publishSuitability` | string | yes | Suitability for an unattended publish. Leave unset on day 1 |
| `escalateMaxCount` + `escalateMinScore` | int + number, **both or neither** | yes | Unattended escalation policy. Leave unset on day 1 |
| `unattended` | bool | yes | `false` on day 1 |
| `hubId`, `folderUrn` | string | **no** | Hub; the single upload folder (`urn:adsk.wipprod:fs.folder:co.…`) |
| `region` | string | **no** | `US` (default, header not sent), `CAN`, `EMEA`, `GBR`, `DEU`, `IND`, `JPN`, `AUS` → `x-ads-region` |
| `issueTypeId`, `issueSubtypeId` | string | **no** | Pin the escalation type or subtype instead of name-matching (fixes R2) |
| `distToMm` | number > 0 | **no** | ACC clash distance → mm (default 1000 = metres) |
| `cdeFolders` | object `{WIP,SHARED,PUBLISHED,ARCHIVE → folder URN}` | **no** | CDE-state routing map. **No fallback:** an unmapped state is refused |
| `disciplineMap` | object `{name token → discipline}` | **no** | Model-name token → S/M/P/E/FP/A…, consulted before the ISO role field |
| `docsAttributes`, `docsAttributesCreateMissing` | bool | **no** | Stamp ISO 19650 metadata as ACC custom attributes on uploads |
| `escalateDueDays` | int ≥ 0 | **no** | Due date on escalated issues |
| `escalateAssignedTo` + `escalateAssignedToType` | string + `user`/`company`/`role`, **both or neither** | **no** | Assignee on escalated issues |
| `escalateExcludeStatuses` | string[] | **no** | ACC statuses that are not re-escalated |

As of tonight the routing and attribute libraries (`AccCdeRouting`, `AccDocsMetadata`) exist but
are **not yet called by the upload command** [Code]. `cdeFolders` and `docsAttributes` are
parsed and stored, but they do not change an upload yet.

### Example for tomorrow: situation A/B (new build)

```json
{
  "_comment": "KUT - reviewed copy in kut-config/acc/. Edit via the ACC card where it offers the setting.",
  "projectId": "b.00000000-0000-0000-0000-000000000000",
  "hubId": "b.11111111-1111-1111-1111-111111111111",
  "region": "EMEA",
  "coordModelSetId": "",
  "coordModelSetName": "",
  "unattended": false,
  "issueTypeId": "",
  "issueSubtypeId": "",
  "escalateDueDays": 14,
  "escalateExcludeStatuses": ["closed", "void"],
  "disciplineMap": { "ARC": "A", "STR": "S", "MEP": "M", "ELE": "E", "PLU": "P", "FPR": "FP" },
  "folderUrn": "urn:adsk.wipprod:fs.folder:co.SHARED_DOCUMENTS_FOLDER",
  "cdeFolders": {
    "WIP":       "urn:adsk.wipprod:fs.folder:co.WIP_FOLDER",
    "SHARED":    "urn:adsk.wipprod:fs.folder:co.SHARED_FOLDER",
    "PUBLISHED": "urn:adsk.wipprod:fs.folder:co.PUBLISHED_FOLDER",
    "ARCHIVE":   "urn:adsk.wipprod:fs.folder:co.ARCHIVE_FOLDER"
  },
  "docsAttributes": false
}
```

- Set `region` **only** after V3 confirms the hub's region. If the hub is US, omit the key.
- `coordModelSetId` is filled by answering *remember?* **Yes** after the first successful pull.
- Leave `issueTypeId` empty unless V5 picks the wrong type.
- Keep `docsAttributes` false until the upload command uses it and the attributes exist in ACC
  (§3.4).
- The `disciplineMap` tokens are examples. Use the tokens that actually appear in KUT model file
  names.

### Example for situation C (the 2026-09-29 DLL): only the old keys

```json
{
  "projectId": "00000000-0000-0000-0000-000000000000",
  "coordContainerId": "00000000-0000-0000-0000-000000000000",
  "unattended": false
}
```

Always edit through the card when it offers the setting, because the card merges. Never hand-edit
while Revit has the project open. Snapshot `_data\coord` before any hand edit.

## 7. First live verifications (in this order)

| # | Do | Expected | Failure looks like → meaning |
|---|---|---|---|
| V0 | **First, every time:** BIM Coordination Center → ACC → **🩺 Self-check** (`ACC_SelfCheck`). Read-only: it creates, uploads and changes nothing in ACC | `READY` (or `READY WITH WARNINGS` you have read). Every row says PASS / WARN / FAIL / SKIPPED with a reason and a remedy; the full report is saved as `STING_ACC_SelfCheck_*.txt` next to the other issue reports | Fix the **first** FAIL — later rows are SKIPPED because of it, not separately broken. It covers the read half of V1–V4 and V7's folder/attributes. It cannot prove write permissions (row 7.2 is always SKIPPED): V5 and V7 still have to be run |
| V1 | Sign in + Test / Refresh | 🟢 Connected, expiry about 1 h ahead | `Token exchange failed (HTTP 401/400)` → wrong secret, wrong app type, or callback not registered exactly. On a PKCE app, *"Enter Client ID and Client Secret first."* → Revit is still loading the old build (§0 situation C): use the §1B app. APS page *"redirect_uri mismatch"* → the callback differs by even one character. `Couldn't open local port 8910` → a previous sign-in is still listening; close it and retry |
| V2 | Find my ACC project | The KUT project is listed under the right hub | *"this sign-in can see no ACC projects"* → Custom Integration missing (§2) **or** the IM is not a member of the project. *"Could not list ACC projects (AuthFailed)"* → sign in again |
| V3 | Hub region | Note the hub region (US or EMEA). ACC data in one region is invisible from the other | If calls 404 on an **EMEA** hub with a correct id, suspect the region header [U]. Set `region` only once the build supports it |
| V4 | `ACC_PullClashes` (card ⬇ Pull Clashes) on the set that has clashes | The model-set picker lists `KUT – Federated (SHARED)`. Then: **non-zero clash count**, a top-10 triage list with real document names on both sides, and a CSV path. Open the CSV; its row count matches the count in the dialog. Accept *remember this model set* | `NOTHING WAS CHECKED … Failure: NotFound … Container id used: b.…` → the `b.` issue (§0); use the bare GUID. `NotFound` with a bare GUID → the clash-service sub-path residual; record the URL from `StingTools_yyyyMMdd.log` (next to the loaded DLL) for the developer. *"clash-clean, or a clash test has not completed"* after a **successful** request → the test has not finished in ACC |
| V5 | Escalate **one** clash (answer the push offer with a count of 1) | The dialog reports 1 created; **open ACC Issues in the browser and see it** (type Coordination / Clash) | Situation C: fails (path, §0), so skip it and use BCF export → ACC import. On A/B: an `issue_type` rejection → R2, so pin `issueTypeId`/`issueSubtypeId`. Created but with the wrong subtype → same fix |
| V6 | `ACC_SyncIssueStatus` | *"… reconciled …"*, and the escalation record is unchanged while the issue is still open. Close the test issue in ACC, re-run, and it is un-tracked | Any failure shows *"The escalation record was left untouched — nothing was un-tracked."*. Re-run; do not act on partial output |
| V6b | **A/B only:** snapshot `_data\coord`, then `ACC_ImportIssues` | ACC issues appear in the STING issue register. The clash escalated in V5 is **linked**, not duplicated. A CSV of created/updated/linked rows is written. Re-running creates nothing new | *"refused: issues.json unreadable"* → the register file is corrupt: restore the snapshot. `FAILED (…)` names the container, so check the `b.` handling and the region |
| V7 | `ACC_UploadModel` with a **small test PDF named to the 7-field standard** into `01_SHARED/Documents` (paste that folder's URN into Upload Folder URN) | A new item appears in ACC | `403` on storage → the token lacks `data:create`: sign in again. A naming-standard rejection → fix the name, not the standard |

Log every result, with date, build (from the addin path) and outcome, in the KUT decision log.
A verification with no written result did not happen.

---

## 8. Known limitations today

- **`ACCPublish` does not publish.** It builds a local zip. Upload is separate (`ACC_UploadModel`,
  `ACC_UploadLastBundle`), and the upload is not in any KUT workflow on purpose.
- **Uploads do not route by CDE state yet** and set **no document attributes yet**. The libraries
  exist (`AccCdeRouting`, `AccDocsMetadata`) but the upload command does not call them. You choose
  the folder and fill Suitability/Revision in ACC.
- **Escalated issues carry no assignee, due date, root cause, linked document or pushpin** on the
  2026-09-29 DLL. The new build adds assignee and due date (`escalateAssignedTo*`,
  `escalateDueDays`). Root cause, linked document and pushpin stay manual in ACC: **pushpins
  cannot be created through the API at all**.
- **No transmittal is created in ACC by StingTools.** The API is read-only. Issue transmittals in
  ACC; STING's B06 docx is the supporting record.
- **No Reviews, Sheets, RFIs, Submittals, Forms, Assets, Cost, Photos or webhooks integration.**
- **Model sets, clash tests, folders, permissions, attributes and the naming standard are set up
  in the ACC UI.**
- **The Model Coordination `tests`/`resources` sub-paths and the upload path have never run
  against a live tenant.** V4 and V7 are their first test.
- **Credentials are plain text** in `%APPDATA%` on the 2026-09-29 DLL (DPAPI on the new build).
  Either way, use one named IM machine.
- **Issues rate limit** is about 500 requests/min per user per hub. STING's volumes are far
  below it, but do not run bulk imports or escalations from two machines at once.
- **Refresh tokens expire** (15 days, and each refresh rotates the token). Run **Test / Refresh** at least weekly, and
  re-sign-in if it fails.
- **Revit cloud models are not supported by StingTools' folder resolver** (next section).

---

## 9. Revit Cloud Worksharing vs local files: recommendation

### What happens to StingTools on a cloud model

For a cloud model, `doc.PathName` is `Autodesk Docs://<project>/<model>.rvt`. StingTools resolves
every project folder from `Path.GetDirectoryName(doc.PathName)` and does not recognise cloud
models (no `IsModelInCloud` check anywhere in `StingTools/`). Reading
`ProjectFolderEngine.GetRootPath` suggests [Code, not run] that it falls through to
**`%USERPROFILE%\Documents\<PROJECT_CODE>`**, **separately on each user's machine**. So:

- each person gets their own `_BIM_COORD` (`acc_settings.json`, `pushed_clashes.json`, issue
  and meeting stores, document register, sequence counters, audit log). Two people pulling
  clashes would escalate the same clash twice, and document numbers could collide;
- StingTools may write a meaningless "project root stamp" into the shared cloud model's Project
  Information.

### Recommendation for KUT

1. **Design teams may use Revit Cloud Worksharing** (BIM Collaborate Pro) for authoring. That is
   their choice and it suits ACC. StingTools on their side is not part of the KUT contract.
2. **The IM's StingTools work runs on a locally saved host model**, for example
   `C:\KUT\KUT-PLN-ZZ-XX-M3-Z-0001.rvt`: a coordination or federation model that **links**
   the discipline models (cloud links are fine). StingTools then resolves
   `C:\KUT\KUT\_data\coord\…` exactly as designed, with one authoritative `_BIM_COORD`.
3. **Do not save the host model inside a Desktop Connector (Autodesk Docs) synced folder.**
   StingTools creates `<CODE>\_data\…` next to the model, and Desktop Connector would then
   upload all that machine state into ACC.
4. When a STING command must run against a **cloud** model itself (for example a tag audit
   inside a discipline model), use **Detach / Save As** to the local folder for read-only audits.
   For writes, agree with the team leader and have them run it in the cloud model with a
   `PROJECT_FOLDER_ROOT` set (below).
5. If cloud models must host STING **writes** before the code fix lands, set
   **`PROJECT_FOLDER_ROOT`** in the StingTools `project_config.json` on each machine to the
   **same** KUT root (a shared network path reachable by everyone). Limits: that setting is
   **global per machine**, so it only suits a machine that works on KUT only. A shared path over
   a slow link will also be slow.
6. Code fix (strategy doc §3, C1): a cloud-model resolver keyed by the ACC project GUID. It also
   lets StingTools read the ACC project id straight from the cloud model
   (`GetCloudModelPath().GetProjectGUID()`), which retires the `b.` confusion for cloud projects.

**Before relying on any of this, run one check:** open a KUT cloud model with StingTools loaded,
run any command that writes a report, and read the root it used from
`StingTools_yyyyMMdd.log`. If it is not what §9 predicts, tell the developer.
