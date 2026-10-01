# KUT × ACC — setup checklist and smoke test, step by step

**Written 2026-10-01** against branch `claude/acc-work-review-gaps-7e2ac7`. Every command name,
button caption, settings key and API route below was checked against the code on that date.

This guide is the **ordered to-do list**. The *why* behind each setting is in
[`KUT_ACC_DAY1_PLAYBOOK.md`](KUT_ACC_DAY1_PLAYBOOK.md), and what the APIs can and cannot do is in
[`ACC_AUTOMATION_RESEARCH_2026-10.md`](ACC_AUTOMATION_RESEARCH_2026-10.md).

How to read the guide:
- **†** marks an Autodesk UI path quoted from Autodesk's own documentation (links in the research
  doc). All other paths are STING's.
- **Never paste a client secret, token or private key into a file under the repository, an e-mail
  or a chat.** The only places they go are the BIM Coordination Center card (stored encrypted)
  and the server's environment variables.

---

## 0. Before you start (5 minutes)

- [ ] **0.1 Which StingTools build is Revit loading?** Run this in PowerShell:

```powershell
Select-String -Path "$env:APPDATA\Autodesk\Revit\Addins\*\StingTools.addin" -Pattern "<Assembly>"
```

  Expect `C:\Dev\STING_KUT_LIVE\CompiledPlugin\StingTools.dll` in each line. If it points
  elsewhere, stop and fix that first. A setting you test against the wrong DLL proves nothing.

- [ ] **0.2 Write down three things in the KUT decision log:** that path, today's date, and the
  commit deployed in `C:\Dev\STING_KUT_LIVE` (`git -C C:\Dev\STING_KUT_LIVE log -1 --oneline`).
- [ ] **0.3 Who you need on the day.**
  - The **ACC account administrator** for step 1.2.
  - A **KUT ACC Project Admin**: steps 1.3–1.6, and the server sign-in in step 4.3.
  - The **IM workstation** with Revit 2025.

---

## 1. Autodesk and ACC side (once per project)

- [ ] **1.1 APS app for the Revit plugin.**
  - Where: aps.autodesk.com → Applications → create, or open the existing one.
  - Type: **Desktop, Mobile, Single-Page App** (PKCE, no secret).
  - Callback URL: exactly `http://localhost:8910/callback`.
  - APIs: Data Management, Autodesk Construction Cloud / Forma, and BIM 360. Details are in
    playbook §1A.
  - **Record the Client ID.** A client ID is not a secret.
- [ ] **1.2 Register the app with the ACC account.**
  - Where: Hub Admin → **Custom Integrations** → **Add custom integration** → paste the APS Client ID†.
  - Without this, step 3.4 lists no projects.
- [ ] **1.3 Members.** The person signing in from STING must be a member of the KUT project. As
  **Project Admin** they can also create folders, attributes, issue types and model sets.
- [ ] **1.4 CDE folders** in Docs: one each for WIP, SHARED, PUBLISHED and ARCHIVE.
  - Open each folder in the browser and copy its id from the address bar. It looks like
    `urn:adsk.wipprod:fs.folder:co.XXXX`.
  - You need all four in step 3.6.
- [ ] **1.5 Document attributes (recommended).** In Docs, on each CDE folder, create the
  text attributes **Suitability**, **Revision**, **CDE State**, **Document Number**,
  **Originator** and **Transmittal** (playbook §3.4).
  - If you skip this, leave `docsAttributes` out in step 3.6. File names then carry the
    suitability and revision, which is the 9-field name and the safe default.
- [ ] **1.6 Model Coordination space.**
  - Where: Model Coordination → Settings → Coordination spaces → **Create coordination space**†.
  - Pick the federated SHARED folder and switch **automatic clash detection ON**. This **cannot be
    changed later**†.
  - Issue types: keep a type and subtype for clashes (e.g. *Coordination / Clash*), and one named
    *Lifecycle* if you will push lifecycle gaps (playbook §3.5).
- [ ] **1.7 Review workflow (optional).** If drawings are approved through ACC Reviews, create
  the approval workflow in ACC. Write down how its outcome labels map to suitability codes
  (e.g. *Approved* → A1); that mapping is used in step 3.6.

---

## 2. Revit plugin — sign in (once per workstation)

- [ ] **2.1** Open the KUT host model **from its local folder** and make sure it has been saved.
  Project settings live beside it.
- [ ] **2.2** Open the BIM Coordination Center:
  - STING dock → **BIM** tab → **★ OPEN COORDINATION CENTER**;
  - then the **PLATFORM** tab → **ACC** card.
- [ ] **2.3** Enter the **Client ID** from step 1.1 → **💾 Save Credentials**.
- [ ] **2.4** **🔓 Sign in with Autodesk**. A browser opens; sign in as the IM.
  - Expect: *"Signed in to Autodesk — tokens stored."*
- [ ] **2.5** **🔌 Test / Refresh Token**.
  - Expect: success, and a line saying the sign-in lapses in about **14 days if unused**.
  - The sign-in renews itself whenever a model is opened with STING.

---

## 3. Revit plugin — KUT project settings (once per project)

- [ ] **3.1** **🔎 Find my ACC project** → pick KUT. This writes `projectId`, `hubId` and `region`.
- [ ] **3.2** Leave **Coord Container ID** and **Issue Type ID** empty. Both are resolved
  automatically.
- [ ] **3.3** Read the **PROJECT ACC SETTINGS** line on the card: it is the exact file path in use,
  normally `…\<PROJECT_CODE>\_data\coord\acc\acc_settings.json`.
- [ ] **3.4 Self-check, first pass.** Click **🩺 Self-check**.
  - Expect `READY` or `READY WITH WARNINGS`.
  - Fix the **first** FAIL and run it again; later rows are skipped *because of* an earlier FAIL.
- [ ] **3.5** Note the hub **region** the self-check reports (US, EMEA, …).
- [ ] **3.6 Edit `acc_settings.json`** at the path from step 3.3. Close Revit's dialogs first,
  and keep a copy in `kut-config/acc/`.
  - **Rule: every key must be one STING knows.** An unknown key makes the whole file be ignored,
    and the card says so.
  - The minimum for KUT, with your values in place of the angle brackets:

```json
{
  "_comment": "KUT - reviewed copy in kut-config/acc/",
  "projectId": "<from 3.1>",
  "hubId": "<from 3.1>",
  "region": "<from 3.5, e.g. EMEA; omit for US>",
  "cdeFolders": {
    "WIP": "<urn from 1.4>",
    "SHARED": "<urn from 1.4>",
    "PUBLISHED": "<urn from 1.4>",
    "ARCHIVE": "<urn from 1.4>"
  },
  "docsAttributes": true,
  "reviewApprovalMap": { "Approved": "A1" },
  "retireSupersededInAcc": "ask"
}
```

  Leave out what you don't use:
  - Drop `docsAttributes` if step 1.5 was skipped. Names then stay 9-field.
  - Drop `reviewApprovalMap` without step 1.7. Its values must be S1–S7, A1–A5, B1–B5 or CR.

  Options to add only when you have decided on them (meanings are in playbook §6):
  - `startAccReviewOnPublish: {"workflowId": "…"}`
  - `escalateMaxCount` + `escalateMinScore` (set both, or neither)
  - `escalateAssignedTo` (a member's e-mail is enough)
  - `escalateDueDays`
  - `lifecycleGapEscalation: {"maxCount": n}`
  - `uploadUnattended`
  - `unattended`
- [ ] **3.7 Self-check, second pass.**
  - Row **6.1** (upload folders) must PASS for every CDE folder.
  - Row **6.2** (attributes) must PASS. It WARNS if names are 7-field while attributes are off.

---

## 4. Planscape server (only if the server sync / webhooks are used)

Do this on the Render dashboard of the **service that actually serves the API**
(`planscape-api-free`). `render.yaml` does not govern it, and **`api.planscape.build` has no DNS**.

- [ ] **4.1 Server APS app.** This is a *Traditional Web App* with a client secret. Its callback
  must be identical to `Acc__CallbackUrl` below.
- [ ] **4.2 Environment variables** (dashboard → Environment), then redeploy:

| Variable | Value |
|---|---|
| `Acc__ClientId` / `Acc__ClientSecret` | From the 4.1 app (the secret goes only here) |
| `Acc__CallbackUrl` | `https://planscape-api-free.onrender.com/api/acc/oauth/callback` |
| `Acc__Scopes` | **Leave empty.** The default now includes `data:create`, which webhooks need |
| `Autodesk__WebhookSecret` | A long random string |
| `Autodesk__WebhookCallbackUrl` | `https://planscape-api-free.onrender.com/api/webhooks/autodesk/event` |

- [ ] **4.3 Get a Planscape token, then connect ACC as a Project Admin.** Run each command in
  PowerShell on its own. `$tok` holds the token for the rest of this section.

```powershell
$tok = (Invoke-RestMethod -Method Post -Uri "https://planscape-api-free.onrender.com/api/auth/login" -ContentType "application/json" -Body (@{email="<you>"; password=(Read-Host "Planscape password")} | ConvertTo-Json)).accessToken
```

```powershell
(Invoke-RestMethod -Uri "https://planscape-api-free.onrender.com/api/acc/oauth/start?projectId=<Planscape project id>" -Headers @{Authorization="Bearer $tok"}).authorizeUrl
```

  Open the printed URL and sign in to Autodesk **as an ACC Project Admin** (Issues webhooks
  require one†).
  - **A connection made before 2026-10-01 must do this once**, to gain `data:create`.

- [ ] **4.4 Choose the hub and project.** The hub's region is now filled in automatically.

```powershell
Invoke-RestMethod -Method Put -Uri "https://planscape-api-free.onrender.com/api/projects/<Planscape project id>/acc/selection" -Headers @{Authorization="Bearer $tok"} -ContentType "application/json" -Body '{"hubId":"<hubId>","accProjectId":"<projectId>"}'
```

- [ ] **4.5 Subscribe the webhooks.**

```powershell
Invoke-RestMethod -Method Post -Uri "https://planscape-api-free.onrender.com/api/projects/<Planscape project id>/acc/webhooks/subscribe" -Headers @{Authorization="Bearer $tok"} -ContentType "application/json" -Body '{}'
```

  Expect status OK and **5 hooks**: two issue hooks, `review.closed-1.0`, and the file-version
  hooks on the top folders.

- [ ] **4.6 Nothing waiting on a reconnect.**

```powershell
Invoke-RestMethod -Uri "https://planscape-api-free.onrender.com/api/acc/reconnect-required" -Headers @{Authorization="Bearer $tok"}
```

  Expect `count: 0`.

### 4.7 Optional — Secure Service Account (unattended sync, no human sign-in)

Only after steps 4.1–4.6 work. SSA needs a **separate Server-to-Server APS app**†; one app cannot
serve both this and the browser sign-in.

1. Create the server-to-server app, then the service account and its key with the SSA API
   (*Create Service Account*, *Create Keys*†). The private key is shown **once**; store it in a
   password manager.
2. Invite the SSA robot to the KUT project with a role and Docs folder permissions†.
   - Issues need a **Forma Build** subscription on the robot†.
   - Reviews need its Data Management permissions assigned by hand†.
3. Server environment, then redeploy:
   - `Acc__Ssa__ClientId`, `Acc__Ssa__ClientSecret`
   - `Acc__Ssa__ServiceAccountId`, `Acc__Ssa__KeyId`
   - `Acc__Ssa__PrivateKeyPem` (the PEM; escaped `\n` is accepted)
4. Switch the connection to SSA. The other settings are kept, because a PUT now merges:
   First, find the connection id. It is the `id` of the row whose `platform` is ACC:

```powershell
Invoke-RestMethod -Uri "https://planscape-api-free.onrender.com/api/projects/<Planscape project id>/platform" -Headers @{Authorization="Bearer $tok"}
```


```powershell
Invoke-RestMethod -Method Put -Uri "https://planscape-api-free.onrender.com/api/projects/<Planscape project id>/platform/<connection id>" -Headers @{Authorization="Bearer $tok"} -ContentType "application/json" -Body '{"configJson":"{\"accAuthMode\":\"ssa\"}"}'
```

5. Test it. Expect success. A failure names the exact `Acc:Ssa:*` setting to fix.

```powershell
Invoke-RestMethod -Method Post -Uri "https://planscape-api-free.onrender.com/api/projects/<Planscape project id>/platform/<connection id>/test" -Headers @{Authorization="Bearer $tok"}
```

To switch back, send `{"configJson":"{\"accAuthMode\":null}"}` the same way.

---

## 5. Smoke test — run in this order, record every result

Record each line in the KUT decision log as **date · build (0.2) · test id · PASS/FAIL ·
evidence** (file path, screenshot, or log line). A test with no written result did not happen.

| # | Do | PASS looks like | If not |
|---|---|---|---|
| S1 | **🩺 Self-check** | `READY` / `READY WITH WARNINGS`; rows 6.1 and 6.2 as in 3.7 | Fix the first FAIL |
| S2 | **⬇ Pull Clashes** on the KUT model set; accept *remember this model set* | A clash count above zero; a top-10 list with real document names on both sides; a CSV whose row count matches | "NOTHING WAS CHECKED …" → read the named failure. "a clash test has not completed" → wait for ACC |
| S3 | Escalate **one** clash (answer the push offer with 1) | 1 created. **Open ACC Issues in the browser and see it**, under the clash type, with a viewer link and a BCF attachment | An issue-type rejection → pin `issueTypeId` / `issueSubtypeId` |
| S4 | **🔁 Sync Issue Status** | "… reconciled …". Close the S3 issue in ACC and run again: it is un-tracked, or held as *closed in ACC, still clashing* if the clash still exists | Any failure leaves the record untouched; run again |
| S5 | Snapshot `_data\coord`, then **📥 Import Issues** | ACC issues appear in STING; the S3 issue is **linked, not duplicated**; a second run creates nothing new | "refused: issues.json unreadable" → restore the snapshot |
| S6 | **⬆ Upload to ACC** with a small test PDF named to the KUT standard, revision P01, suitability S2 | A new item in the SHARED folder. With attributes on, its Suitability / Revision attributes are filled. Upload it again: *"not uploaded again"* | 403 on storage → sign in again (2.4). A naming rejection → fix the name |
| S7 | **Export with *Upload to ACC*** (Export Centre profile used by the fortnightly job) on one sheet | The PDF lands in the CDE folder of its suitability. The file name carries suitability + revision unless `docsAttributes` is true | — |
| S8 | **SETUP** tab → **QUICK WORKFLOWS** → *KUT fortnightly issue (8 steps)* → **Run preset**, on a **copy** of the model with one clouded change | Steps 1–5 PASS; step 6 builds the bundle; step 7 uploads only *this* run's bundle; the run log is written | Step 1 fails → a sheet's revision stamp disagrees (the report names it). Step 2 "no sheet carries a cloud" → add the cloud |
| S9 | Start or approve a review in ACC on the S6 file, then **📋 Read Reviews** → **✅ Review Decisions** → accept | A proposal for the S6 file; accepting it sets the register's suitability (e.g. A1). If a target refuses, the proposal stays **pending** and the next Accept applies only the rest | "NOT applied … revision" → the register row is at another revision |
| S10 | **📨 Read Transmittals** | ACC transmittals appear as read-only rows; a second run reports them as unchanged | A save failure now fails the command with the reason |
| S11 | *(server)* Close a review in ACC | The server log shows `Autodesk webhook review.closed-1.0: ACC review …` within a minute | No log line → check 4.5 (5 hooks) and that the webhook secret was set **before** the hooks |
| S12 | *(server)* `POST …/acc/sync` | Status OK; issues raised **and** closed in Planscape since the first sync are reported once, not sent (the default) | RECONNECT_REQUIRED → 4.3 |
| S13 | *(SSA only)* 4.7 step 5, then S12 | Success with no refresh token stored; an issue created by the sync shows the robot as its creator | The error names the `Acc:Ssa:*` setting |

**Stop rule:** if S1, S2 or S6 fails, stop and report that line. Everything after them depends
on them.

**What to send back to the developer** for any FAIL:
- the test id;
- the dialog text (a screenshot);
- the matching lines from `StingTools_yyyyMMdd.log` beside the loaded DLL (path from 0.1);
- for S11–S13, the server log lines around that time.
