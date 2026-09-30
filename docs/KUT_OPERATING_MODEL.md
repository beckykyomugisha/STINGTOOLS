# KUT operating model: code, project data, ACC and AI agents

**Written 2026-09-30**, the day before KUT (Kampala Uganda Temple) starts on Autodesk
Construction Cloud (ACC). This document answers one question:

> *What procedure makes the KUT work go smoothly? Should Cowork (Claude's desktop agent working
> in a local folder) be used on the KUT project folder that StingTools creates? Should the
> StingTools repository manage that folder directly, since some tools are not yet reliable? What
> is the most flexible and sustainable option?*

Companion docs: [`ACC_INTEGRATION_STRATEGY.md`](ACC_INTEGRATION_STRATEGY.md) (what ACC can do and
what STING does with it) and [`KUT_ACC_DAY1_PLAYBOOK.md`](KUT_ACC_DAY1_PLAYBOOK.md) (tomorrow's
checklist).

---

## 0. The recommendation in three sentences

**Keep three stores apart and give each one owner.** ACC is the CDE of record for every
deliverable. The local KUT project folder is StingTools' working state, and only the plugin and
the Information Manager write to it. The StingTools repo holds code only, and never project data.

**Put the KUT *configuration*, not the KUT *data*, under version control** in a separate private
repo (`kut-config`). Deploy it into the project folder with a script. Fix a defective tool through
the normal defect loop (issue, worktree branch, build, `deploy.bat`, verify on a **copy** of the
KUT data, merge). Never patch around it in the live folder.

**Use Cowork (or Claude Code) as a reader and a drafter, not an operator of the live project.** It
can read reports, logs and registers and draft documents, with **read-only access to the live
folder**. It can write only to a scratch or drafts folder, or to the config repo through a pull
request. It cannot drive Revit, and it must never edit files that the plugin owns.

---

## 1. Three stores, three owners

| Store | What lives there | Owner / only writer | System of record for |
|---|---|---|---|
| **ACC (Autodesk Docs + Issues + Model Coordination)** | Models, drawings and documents in WIP / SHARED / PUBLISHED / ARCHIVE; issues; clash tests; transmittals; reviews | Task teams (their WIP) and the IM (SHARED → PUBLISHED through reviews) | **Every contractual information container.** ISO 19650-1 §12 CDE |
| **Local KUT project folder** `C:\KUT\…` | IM host model, `<CODE>\_data\coord\` (the `_BIM_COORD` alias), exports, reports, clash CSVs, registers, sequence counters, audit log | **The StingTools plugin**, plus the IM through documented settings only | STING's working state: issue register, document numbering counters, clash escalation ledger, audit chain |
| **StingTools repo** `C:\Dev\STINGTOOLS` (+ worktrees) | Source code, corporate baseline data (`StingTools/Data/`), `project-templates/KUT/` (the **template** overlay pack) | Developers and coding agents through PRs | The behaviour of the tools |
| **`kut-config` private repo** (new) | KUT-specific **configuration** only: overlay JSON, `acc_settings.json` (no secrets), Export Centre profiles, workflow choices, decision log, runbook notes | IM (and a developer by PR) | *Why* the project is configured the way it is |

### Should project data ever go into the StingTools repo? No.

1. **Confidentiality.** KUT is a religious-client project with owner standards and contractual
   documents. The StingTools repo is shared across many agents and worktrees and is pushed to
   GitHub. One accidental `git add -A` in a worktree would publish client data. The repo's
   `.gitignore` was not designed for this and must not be relied on to keep it out.
2. **Size and churn.** RVT files, exports and zips are hundreds of MB. Registers and logs change
   daily. Git history would grow without bound, and every worktree checkout would copy it.
3. **ISO 19650.** The CDE is the single source of truth, with an audit trail of status changes. A
   second copy in git is an uncontrolled parallel CDE. It invites "which one is current?" disputes,
   which ISO 19650 exists to prevent.
4. **Multi-agent worktrees.** Many agents work in `C:\Dev\STINGTOOLS\.claude\worktrees\*` in
   parallel and run `git reset`, `git clean` and branch switches (see MEMORY: *worktree
   isolation*). Project data inside that tree could be deleted or reverted by an agent working on
   something unrelated.
5. **Lifecycle mismatch.** Code is released per PR. Project data is released per ISO 19650
   suitability and revision. Tying them together means a code revert can roll back a register.

`project-templates/KUT/` in the repo is correct as it stands. It is a **template**, not live data,
and it holds no client documents. Keep it there as the starting point, and keep the **live** copy
in `kut-config`.

---

## 2. Folder topology

```
                         ┌──────────────────────────────────────────────┐
                         │  ACC  (CDE of record, Autodesk-hosted)        │
                         │  Project Files/                               │
                         │   00_WIP/<ORIG>_<Disc>/   task teams          │
                         │   01_SHARED/Models|Drawings|Documents         │
                         │   02_PUBLISHED/…   (via Reviews only)         │
                         │   03_ARCHIVE/                                 │
                         │  Issues · Model Coordination · Transmittals   │
                         └──────────▲───────────────────────┬───────────┘
          upload (ACC_UploadModel,  │                       │ clash pull, issue pull
          ACC_UploadLastBundle),    │  APS REST (3-legged,  │ (ACC_PullClashes,
          issue push                │  IM's own sign-in)    │  ACC_ImportIssues,
                                    │                       │  ACC_SyncIssueStatus)
┌───────────────────────────────────┴───────────────────────▼─────────────────────┐
│  IM WORKSTATION                                                                  │
│                                                                                  │
│  C:\KUT\                        ← LOCAL, NOT a Desktop Connector synced folder    │
│   ├─ KUT-ZZZ-ZZ-XX-M3-Z-0001.rvt   IM host / federation model (links cloud models)│
│   └─ KUT\                       ← <PROJECT_CODE> root StingTools resolves         │
│       ├─ _data\coord\           ← plugin-owned state (_BIM_COORD alias)           │
│       │    ├─ acc\acc_settings.json   (IM edits via ACC card; keys documented)    │
│       │    ├─ acc\pushed_clashes.json (plugin-owned: NEVER hand-edit)             │
│       │    ├─ issues.json, doc_sequences.json, audit_log_*.jsonl (plugin-owned)   │
│       │    └─ overlay JSON (owner_standards, lod_matrix, tag_schemes …) ← kut-config│
│       ├─ 00_WIP… 02_PUBLISHED…  local exports (staging for ACC upload)            │
│       └─ reports / clash CSVs                                                    │
│                                                                                  │
│  C:\KUT-snapshots\yyyymmdd-hhmm\_coord.zip   ← pre-write snapshots (§6)           │
│  C:\KUT-sandbox\                ← COPY of C:\KUT for testing new builds (§5)       │
│  C:\KUT-drafts\                 ← the ONLY folder Cowork may write to (§4)         │
│                                                                                  │
│  %APPDATA%\Planscape\acc_credentials.json   ← machine secret store (DPAPI in the   │
│                                               new build). NEVER copied or committed│
│  %APPDATA%\Autodesk\Revit\Addins\20xx\StingTools.addin → <Assembly> = live DLL    │
│                                                                                  │
│  C:\Dev\kut-config\   (private git repo) ──deploy script──► C:\KUT\KUT\_data\coord │
│  C:\Dev\STINGTOOLS\   (code repo + worktrees) ──deploy.bat──► CompiledPlugin\      │
└──────────────────────────────────────────────────────────────────────────────────┘
```

Two rules make this safe:

- **Nothing flows from the code repo into `C:\KUT` except the built DLL and the corporate
  baseline data** that `deploy.bat` stages. Nothing flows from `C:\KUT` into the code repo.
- **Nothing flows from `C:\KUT` into `kut-config` automatically.** The IM decides which
  configuration change is intentional and commits it.

### Why the host model is local, not a cloud model

`StingPaths` resolves every project folder from `Path.GetDirectoryName(doc.PathName)`. For a Revit
Cloud Worksharing model, `PathName` is `Autodesk Docs://…`. From reading the code (not from a live
run), the resolver then falls through to `%USERPROFILE%\Documents\<CODE>` on each machine
separately. The result is **one `_BIM_COORD` per user**: counters diverge, the clash ledger forks
and the audit chain splits. See strategy §3 and playbook §9. Until a cloud-root resolver ships,
the IM's STING work runs on a **locally saved host model that links the cloud models**. Design
teams can still use cloud worksharing for authoring.

---

## 3. `kut-config`: what to version-control

Create a **private** repo (GitHub private or Azure DevOps), separate from StingTools.

### Include

| Path | Content | Deployed to |
|---|---|---|
| `overlay/` | `owner_standards.json`, `lod_matrix.json`, `tag_schemes.json`, `project_config.json`, `fohlio_map.json`, `sting_classification.json`, `boq_rate_policy.json`, `classification_policy.json`, `manifest.json` (seeded from `project-templates/KUT/_BIM_COORD/`) | `C:\KUT\KUT\_data\coord\` |
| `acc/acc_settings.json` | Project ACC settings: ids, model set, escalation policy, `cdeFolders`. **No secrets. The file holds none by design** | `…\_data\coord\acc\` |
| `export/` | Export Centre KUT profile (7-field naming pattern), sheet-set definitions | via the Export Centre import, or a documented path |
| `workflows/` | Which `WORKFLOW_KUT_*.json` presets are in use, plus any project override | reference |
| `acc-setup/` | Written record of the ACC setup: folder tree, naming standard, attributes, issue types, model sets, clash matrix, member roles. ACC settings cannot be exported, so this **is** the record | none (documentation) |
| `decisions/DECISION_LOG.md` | Dated decisions: naming pattern, local vs cloud host, escalation thresholds, and each live-verification result with the build path | none |
| `scripts/deploy-config.ps1` | Copies `overlay/` and `acc/` into the live folder **after** snapshotting it (§6), then prints a diff | none |
| `scripts/snapshot.ps1` | Zips `_data\coord` into `C:\KUT-snapshots\` | none |
| `BUILD_PIN.txt` | The StingTools commit or tag this config was last verified against | none |

### `.gitignore` for `kut-config`

```gitignore
# Never: secrets or machine state
acc_credentials.json
*.pem
*.lic
.env*
# Never: project data (it lives in ACC and C:\KUT)
*.rvt
*.rfa
*.rws
*.nwc
*.nwd
*.ifc
*.pdf
*.dwg
*.zip
*.xlsx
# Never: plugin-owned runtime state
pushed_clashes.json
issues.json
meetings.json
doc_sequences.json
deliverables.json
transmittals.json
workflow_state.json
audit_log_*.jsonl
search_index/
*.log
```

The credentials file lives at `%APPDATA%\Planscape\acc_credentials.json`, outside any repo. It
must **never** be copied into one, even encrypted. With the in-flight build it is DPAPI-protected
under the Windows user, so a copy is useless on another account anyway. A lost machine means
signing in again, not restoring a file.

**Pin config to a build.** A configuration file written for a newer build can break an older
one. For example, `acc_settings.json` rejects **the whole file** when it sees an unknown key
(`AccOperatingPolicy.KnownKeys`). Record the build in `BUILD_PIN.txt`, and do not deploy config
that uses new keys until the matching build is live (check the `.addin` `<Assembly>` path).

---

## 4. Where Cowork and Claude Code fit, and where they do not

Facts about Cowork used here, from Anthropic's help centre (verify there for your plan):

- Cowork works on folders you connect in Claude Desktop, and can read, create, edit and delete
  files inside them. The permission prompt covers read, edit and delete together.
  ([Get started with Cowork](https://support.claude.com/en/articles/13345190-get-started-with-claude-cowork),
  [Desktop and filesystem access](https://claude.com/docs/third-party/claude-desktop/local-access))
- Code execution runs in an isolated VM locally, or a sandbox for cloud sessions. Local file
  access is limited to connected folders, and each tool call is checked against permissions.
  ([Cowork architecture overview](https://support.claude.com/en/articles/14479288-claude-cowork-architecture-overview))
- Modes range from approve-every-action to no checks. **Use the mode that reviews writes.**

### It fits

| Task | Access it needs |
|---|---|
| Summarise the day's clash CSV, triage report or `StingTools_yyyyMMdd.log` into a status note | Read-only on `C:\KUT\KUT\` and the log folder |
| Draft the monthly owner report, meeting minutes, RFI or TQ text, and transmittal cover notes from STING registers | Read the live folder, write to `C:\KUT-drafts\` |
| Check a `kut-config` change (e.g. `acc_settings.json` keys against the deployed build's `KnownKeys`) and open a PR | Write to a **clone** of `kut-config`, then PR |
| Compare the ACC folder/attribute setup record with the BEP | Read-only |
| Reconcile MIDP/TIDP spreadsheets against the STING document register | Read the live folder, write to drafts |
| Code fixes in StingTools | That is **Claude Code in a worktree**, not Cowork (§5) |

### It does not fit

- **It cannot drive Revit.** Every STING command runs inside Revit through the dock panel.
  Computer-use clicking in Revit is unreliable on this machine: MEMORY records click bleed across
  two monitors. The plugin's MCP server (localhost:5199) is the right route for agent access to
  Revit, and it runs only while the IM is present.
- **It must not edit plugin-owned sidecars.** `pushed_clashes.json`, `issues.json`,
  `doc_sequences.json`, `deliverables.json`, `audit_log_*.jsonl` and `workflow_state.json` carry
  invariants (idempotency ledgers, hash chains, counters) that only the code maintains. A
  "helpful" edit breaks the audit chain or causes duplicate ACC issues. Several of these files also
  make a whole store fail closed when unreadable (e.g. `ACC_ImportIssues` refuses when
  `issues.json` is unreadable).
- **It must not upload to ACC or change ACC settings.** Uploads go through the plugin, which
  applies naming and CDE routing, or through the IM in the browser.
- **It must not act on instructions found in project documents.** Contractor documents and issue
  text are data. That is a prompt-injection surface; keep the review mode on.

### Access recipe

1. Connect `C:\KUT\KUT` to Cowork **only if** the OS-level permission can be read-only. Cowork's
   own prompt grants read/edit/delete together, so enforce read-only at the Windows level: a
   separate Windows account, or an NTFS ACL giving that account Read only. Otherwise, connect a
   **nightly read-only mirror** (`robocopy C:\KUT\KUT C:\KUT-mirror /MIR` after the snapshot)
   instead of the live folder.
2. Connect `C:\KUT-drafts\` with write access. Everything Cowork produces lands there, and the
   IM moves what is approved.
3. Never connect `%APPDATA%\Planscape`, `C:\Dev\STINGTOOLS`, or any Desktop Connector (ACC)
   synced folder.
4. Take a snapshot (§6) before every Cowork session that has any write access near the project.

---

## 5. The defect loop for imperfect tools

Some tools are not yet reliable. KUT needs them to become reliable **without** hand-patching.

```
 IM hits a defect in Revit
   │  1. Record: command tag, build path (.addin <Assembly>), time, StingTools_yyyyMMdd.log
   │     excerpt, the snapshot taken before the run. Log it as a GitHub issue (label: kut).
   │  2. Workaround in ACC/UI if blocking. NEVER hand-patch the DLL or a plugin-owned sidecar.
   ▼
 Developer / Claude Code
   3. New worktree from origin/main (never stack on an unmerged branch)
   4. Reproduce with a Revit-free test where possible (StingTools.Acc.Tests etc.), RED first
   5. Fix, then build (dotnet build 0/0) and tests green
   6. deploy.bat FROM THAT WORKTREE, then restart Revit, then grep the .addin <Assembly>
   ▼
 Verify on a COPY
   7. robocopy C:\KUT → C:\KUT-sandbox, open the sandbox host model, run the command.
      For ACC writes, point acc_settings at a TEST ACC project, or a test folder and a
      "STING-TEST" issue type, never the live one.
   8. Record RED/GREEN evidence (before/after output) in the issue.
   ▼
 Release
   9. PR → merge → deploy.bat from an up-to-date main checkout → grep <Assembly> →
      update kut-config/BUILD_PIN.txt → note in DECISION_LOG.md
  10. Run the command on live KUT once, with a fresh snapshot first.
```

**Hard rules**

- **Never hand-patch the deployed DLL**, and never leave the live add-in pointing at an
  experimental worktree overnight. `deploy.bat` repoints the manifest to whichever checkout ran it
  last. The KUT machine must end each day on a **release build from `main`**.
- **Never hand-edit plugin-owned sidecars** beyond documented keys. The documented, hand-editable
  set is: `acc_settings.json` (keys in `AccOperatingPolicy.KnownKeys`, preferably through the ACC
  card) and the overlay JSON files listed in `project-templates/KUT/_BIM_COORD/manifest.json`.
  Everything else is plugin-owned.
- **One deploy slot.** Parallel agents building elsewhere is fine. Only the IM, or a developer on
  the IM's instruction, runs `deploy.bat` on the KUT machine.
- **A "no effect" is not a result.** Check the `<Assembly>` path and the log before concluding
  that a fix failed or worked (CLAUDE.md: *"What the repo says is not what is serving"*).

---

## 6. Snapshots before write commands

STING write commands can change many files at once: ACC escalation, `ACC_ImportIssues`, issue
deliverable, transmittal, renumber, `Folders_Consolidate`, `LOD_Stamp`, workflow runs.

- **Before every write command or workflow run:** `scripts/snapshot.ps1`, which zips
  `C:\KUT\KUT\_data\coord` to `C:\KUT-snapshots\yyyyMMdd-HHmm_<command>.zip`. It takes seconds,
  because this folder is small.
- **Keep** every snapshot for 14 days, then one per week for the project's life. Also copy them
  off the machine (company NAS or OneDrive, **not** the ACC project).
- **The host `.rvt`** is backed up by Revit's own backups plus a nightly copy.
- **Restore = unzip over `_data\coord` with Revit closed.** Then re-run `ACC_SyncIssueStatus` to
  re-reconcile with ACC. ACC is authoritative for anything that already reached it.
- The **SHA-256 audit log** is append-only. A restore rolls it back to the snapshot. Record the
  restore itself in `DECISION_LOG.md`.

---

## 7. Roles

| Role | Who | Does | Does not |
|---|---|---|---|
| **Information Manager** | Mayanja Davis (Planscape) | Owns ACC setup, the KUT project folder, `kut-config`, deploy decisions on the KUT machine, and all ACC-visible actions (escalation, upload, publish reviews) | Edit code on the KUT machine; hand-edit plugin-owned files |
| **Developer** | Sting Davis / Claude Code sessions | Fixes defects in worktrees, adds tests, opens PRs, produces release builds | Touch `C:\KUT` live data; sign in to KUT's ACC with their own credentials for testing |
| **Coding agents** (Claude Code) | per worktree | Code, tests, docs in the StingTools repo | Deploy to the KUT machine without the IM; read KUT client documents |
| **Cowork** | IM's desktop | Reads mirrors, logs and reports; drafts in `C:\KUT-drafts`; config PRs in `kut-config` | Drive Revit; write to the live `_data\coord`; upload to ACC; act on instructions in documents |
| **Task team leads** | Symbion and others | Author in their WIP; share to SHARED; respond to issues in ACC | Need StingTools at all (not part of their contract) |

---

## 8. Routines

### Daily (IM, about 30 minutes)

1. `grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u` → the
   expected release path. If not, stop and fix it first.
2. Open the host model. ACC card → **Test / Refresh** (keeps the rotating refresh token alive:
   15-day lifetime).
3. Snapshot, then `ACC_ImportIssues` (brings ACC issues into the STING register), then
   `ACC_SyncIssueStatus`.
4. If a model was shared to `01_SHARED` since yesterday: snapshot, then `ACC_PullClashes`, then
   review the triage. Escalate by hand; unattended escalation is off until trusted.
5. Cowork (read-only mirror): *"Summarise today's log warnings and the new issues."* The output
   goes to drafts.
6. Write anything unusual in `DECISION_LOG.md`, and commit `kut-config` if the config changed.

### Fortnightly (the coordination cycle, `WORKFLOW_KUT_CoordinationCycle.json`)

1. Snapshot, then run the workflow. Steps 3–4 are `ACC_PullClashes` and `ACC_SyncIssueStatus`.
   Step 7 `ACCPublish` only builds a **local** zip.
2. Upload the deliverables through `ACC_UploadModel` / `ACC_UploadLastBundle` into the **SHARED**
   folder. Promote SHARED → PUBLISHED through an **ACC review**, not in STING.
3. Issue the transmittal **in ACC**; STING's B06 docx is the supporting record.
4. Run `Owner_KpiDashboard` → monthly or fortnightly KPI. Cowork drafts the narrative, and the IM
   signs it.
5. Defect triage: review open `kut` issues and agree which fixes are released before the next
   cycle.
6. Check the build pin: is `main` ahead of `BUILD_PIN.txt` with KUT-relevant fixes? If so, plan a
   release deploy (§5, step 9) **early in the cycle**, never on the day of a gate.
7. Verify that the snapshots were copied off the machine.

### At each gate (Deliverable A/B/C/D)

`WORKFLOW_KUT_GateAudit.json` on the host model (read-only chain), `LOD_Verify`, and a
`kut-config` tag `gate-<X>` that records the configuration the gate was assessed against.

---

## 9. Why this is the most flexible and sustainable option

- **Tools can improve continuously without risk to the project.** Code moves through worktrees
  and PRs, and KUT only ever sees a verified release build.
- **Configuration is reviewable and reproducible.** A second project (or a rebuilt machine) is
  `git clone kut-config` plus `deploy-config.ps1`.
- **Data stays where ISO 19650 and the contract expect it.** ACC holds the record; the local
  folder is a working cache that can be rebuilt, protected by snapshots.
- **AI agents add leverage without write risk.** Drafting and analysis are where Cowork is strong,
  and neither needs write access to the live state.
- **It degrades well.** If ACC integration fails on a given day, the IM works in the ACC browser
  UI and STING's local outputs (CSV, BCF, docx) still exist. Nothing depends on a single
  imperfect tool.

### Options considered and rejected

| Option | Why not |
|---|---|
| Manage the KUT folder inside the StingTools repo | Confidentiality, size, a parallel CDE, and agents resetting worktrees (§1) |
| Let Cowork operate the live project folder with write access | It cannot run the plugin, and it can corrupt plugin invariants. Its value is in drafting, not operating |
| Put `_data\coord` in a Desktop Connector (ACC-synced) folder "for backup" | It syncs machine state into the CDE, invites sync conflicts on JSON ledgers, and makes the ACC record noisy. Use snapshots instead |
| Run STING directly on cloud models now | Per-user `_BIM_COORD` forks (§2) until the cloud-root resolver ships |
| Hand-patch tools in place "just for KUT" | Unreviewed, unreproducible, and lost at the next deploy |
