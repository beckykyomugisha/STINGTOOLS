> **This document has been moved.** The up-to-date version is at `docs/guides/DOCUMENT_MANAGER_GUIDE.md`. The content below is kept for historical reference only.

---

# Document Manager — Plain-English Guide

A practical guide to managing project documents across **Revit (the plugin)**, **the cloud server (Planscape)**, and **the mobile app**, with recommended workflows for the most common roles.

If you only read one section, read [The Three-Minute Picture](#the-three-minute-picture) and [Best-Practice Workflows](#best-practice-workflows).

---

## The three-minute picture

Think of the document manager as **one cabinet with four drawers**, opened from **three different doors**.

**Four drawers** (these are the ISO 19650 CDE states — every document lives in exactly one at a time):

| Drawer | Code | What goes in it |
|---|---|---|
| 🟡 **Work In Progress** | WIP | Drafts. Only the discipline that owns the file should see them. |
| 🔵 **Shared** | SHARED | Issued for coordination across the team. Other disciplines can review. |
| 🟢 **Published** | PUBLISHED | Issued for use — for construction, tender, fabrication, handover. Authoritative. |
| ⚫ **Archive** | ARCHIVE | Retired. Read-only history. |

**Three doors:**

| Door | Who uses it | Best for |
|---|---|---|
| **Revit plugin** (BCC + Document Management dialog) | BIM coordinators, designers | Producing deliverables, transmittals, publishing from Revit |
| **Web/desktop server** (Planscape) | Project managers, reviewers | Approvals, dashboards, audit |
| **Mobile app** | Site teams, clients, approvers | Look-up, photo upload, on-site approvals |

The **server is the single source of truth.** The plugin and mobile are views over it. When the network is down, the plugin keeps working locally and re-syncs when it reconnects.

---

## What's actually stored

Every document has these pieces, regardless of which door you're using:

- **File name** — typically the ISO 19650 8-segment name (e.g. `PRJ-XYZ-ZZ-ZZ-DR-A-0001`).
- **Discipline** — A (Arch), S (Structural), M (Mechanical), E (Electrical), P (Plumbing), FP (Fire), LV (Low Voltage), G (General).
- **CDE state** — one of WIP / SHARED / PUBLISHED / ARCHIVE (the drawer it's in).
- **Suitability code** — S0 (initial), S1 (suitable for coordination), S2 (info), S3 (review/comment), S4 (construction), S5 (manufacture), S6 (PIM/AIM), S7 (archive).
- **Revision** — `P01`, `P02`, … (preliminary), `C01`, `C02`, … (construction).
- **Status history** — every transition, who did it, when, why.
- **Approvals** — who signed it off (where required by ISO 19650-2 §5.6).
- **Versions** — every upload of the same filename creates a new `DocumentVersion` row, so you can roll back or compare.
- **The file itself** (PDF / DOCX / DWG / IFC / RVT / etc.).

In the plugin you'll also see a **deliverables register** (`deliverables.json`) and **rendered output** (`generated/*.docx` for transmittals, RFIs, MR forms, etc.). These mirror to the server automatically.

**Two sibling features attached to documents:**
- **Sticky notes** on individual model elements — attached coordination markup that travels with the element (covered in detail below).
- **Briefcase** — a curated reference library of read-only project documents you can browse without leaving Revit (covered below).

---

## The four drawers in detail

### 🟡 WIP — Work In Progress

- **Default state** for any new file.
- Only the **owning discipline** should see it. (A subcontractor M&E coordinator should never see the architect's WIP.)
- Suitability is usually **S0**.
- Revision is usually **P01**, **P02** while drafting.
- **No coordination assumed** — files may be incomplete, broken, contradictory.

### 🔵 SHARED — Issued for coordination

- The team as a whole can review, mark up, raise RFIs.
- Suitability moves to **S1, S2, or S3** depending on the issue purpose.
- A revision typically bumps (P02 → P03) when promoted from WIP.
- Promotion **WIP → SHARED** requires **Coordinator role** at minimum.

### 🟢 PUBLISHED — Issued for use

- Construction / fabrication / handover. **This is the version that will be built from.**
- Suitability moves to **S4** (construction), **S5** (manufacture), **S6** (PIM/AIM).
- Revision typically bumps to a construction code (`C01`).
- Promotion **SHARED → PUBLISHED** requires:
  - **Manager role** at minimum, AND
  - **An approved DocumentApproval record** (someone with sign-off authority has clicked "Approve" on the approval request).
- This is the **publishing gate.** A coordinator can issue a draft for sharing in seconds, but a draft for construction needs a Manager to bless it. That's the ISO 19650-2 §5.6 rule the system enforces.

### ⚫ ARCHIVE — Retired

- Read-only history. Files don't disappear; they're moved out of active use.
- Used for superseded revisions and end-of-project handover.

### One-way flow

```
WIP ──▶ SHARED ──▶ PUBLISHED ──▶ ARCHIVE / SUPERSEDED
         ▲    │
         │    └──▶ (back to WIP for rework)
```

You can't skip drawers. To get from WIP to PUBLISHED, the file goes through SHARED first.

---

## Permissions — who sees what

The system uses **three permission axes** stacked on top of project membership. Each axis has a per-member allow-list:

| Axis | Example values | Effect |
|---|---|---|
| **CDE state** | WIP, SHARED, PUBLISHED, ARCHIVE | Which drawers the user can open. |
| **Discipline** | A, S, M, E, P | Which trades they can see. |
| **Suitability** | S0–S7 | Which suitability bands they can see. |

If an axis is left blank, the user gets **everything on that axis** (the default before Phase 177 — preserved for old projects).

### Worked examples

| Person | Allowed CDE | Allowed Discipline | Allowed Suit | What they see |
|---|---|---|---|---|
| Lead BIM coordinator (in-house) | (all) | (all) | (all) | Everything. Full project. |
| Discipline lead (M&E sub) | WIP, SHARED, PUBLISHED | M, E | (all) | Their own discipline only, but at every CDE state |
| Reviewer / external auditor | SHARED, PUBLISHED | (all) | S2, S3, S4 | Issued items only — no drafts |
| Site engineer | PUBLISHED | (all) | S4, S5 | Current construction issue only |
| Client representative | PUBLISHED, ARCHIVE | (all) | S4, S6 | Final issued + handover bundle |

The server enforces this on **every** read and write — list, download, version history, approval, transition. A document the user can't see doesn't even appear in their list (we return 404, not 403, so existence isn't leaked).

### Access profiles — pick a preset, don't tick boxes

Rather than configuring three multi-selects per invitee, the system has **named access profiles** at the tenant level. Examples a project manager might create:

- **"Trade subcontractor — M only"** → CDE: WIP+SHARED+PUBLISHED · Disc: M · Suit: (all)
- **"Reviewer"** → CDE: SHARED+PUBLISHED · Disc: (all) · Suit: S2+
- **"Client read-only"** → CDE: PUBLISHED+ARCHIVE · Disc: (all) · Suit: S4+

When inviting a new member, pick a profile from the dropdown — the three allow-lists get stamped on the member row in one click. You can still override individual axes if a person needs an exception.

Profiles are **snapshots**. Editing the profile later doesn't retroactively rewrite existing members (that would be a surprising audit hazard). To re-apply a changed profile, edit the affected members manually.

---

## ISO 19650 file naming — the 8-segment tag

Every file follows the `PRJ-ORG-LVL-LOC-TYPE-DISC-NUMBER-REV` shape:

```
   PRJ-ORG-ZZ-ZZ-DR-A-0001-P03
   │   │   │  │  │  │ │    └── Revision (P=preliminary, C=construction)
   │   │   │  │  │  │ └─────── 4-digit sequence number
   │   │   │  │  │  └───────── Discipline (A/S/M/E/P/FP/LV/G)
   │   │   │  │  └──────────── Type (DR=drawing, SP=spec, MR=material req, etc.)
   │   │   │  └─────────────── Location code (ZZ if whole-building)
   │   │   └────────────────── Level code (ZZ if not level-specific)
   │   └────────────────────── Originator code (e.g. company short code)
   └────────────────────────── Project code
```

The plugin **enforces this convention on upload** if the project's "Enforce ISO 19650 naming" flag is on (PM toggles it under project settings). If a file doesn't match, the upload is rejected with a structured error explaining which segment is wrong.

---

## The four production paths

There are four end-to-end flows for getting a document out the door. Pick the one that matches what you're producing.

### Path 1 — Manual upload (any file)

Use when you have an existing PDF/DWG/DOCX from outside the BIM tooling that needs to land on the CDE.

1. **Mobile or web** → Documents → Upload → fill in discipline / type / revision.
2. The file lands in **WIP** with suitability **S0**.
3. Promote through SHARED → PUBLISHED as the document matures (each step needs the right role).

### Path 2 — Drawing production (Revit)

The bread-and-butter flow for design teams.

1. In Revit, produce sheets via the Sheet Manager + Drawing Template Manager.
2. Open the **BIM Coordination Center** → **Deliverables** tab.
3. Click **Issue Deliverable**: the plugin renders an A01 cover sheet (DOCX), bumps the revision, writes a row to `deliverables.json`, mirrors to the server.
4. Click **Publish Stage 3** when ready for construction issue: the plugin moves the deliverable to PUBLISHED + S4. The server requires a Manager role plus an approved DocumentApproval.

### Path 3 — Transmittals + RFIs + MRs (template engine)

For produced documents like transmittal memos, RFIs, technical queries, material requisitions, meeting minutes.

1. **BIM Coordination Center** → choose the template (B06 Transmittal, B08 RFI, C10 Material Requisition, D14 Meeting Minutes, etc.).
2. Fill in the form. The plugin:
   - Mints a unique document number from the project's `doc_sequences.json`.
   - Renders the DOCX from the template.
   - Starts a workflow instance (e.g. transmittal Draft → Issued → Acknowledged with SLAs).
   - Mirrors the lifecycle event + audit row to the server.
3. The recipient gets the rendered file + a workflow notification.

### Path 4 — On-site capture (mobile)

For site-walks, photo evidence, defect reports.

1. **Mobile app** → Issues → Create → take photos → assign discipline / priority / location.
2. The file lands as an **issue attachment** in WIP.
3. Promote to SHARED if it needs to feed an RFI, or PUBLISHED if it's a formal handover photo.

**Geofencing**: if the project has a boundary polygon configured, the server checks the device's GPS coordinates on upload and rejects any "site photo" taken outside the geofence. This stops people from claiming office desk-photos as site evidence.

**Antivirus scanning**: every uploaded file is queued for scanning. Until the scan completes, downloads return HTTP 423 (Locked) — a clean retry. If a virus is found, downloads return 451 with the threat name and the file is quarantined.

---

## Sticky notes — per-element coordination markup

Sticky notes are **comments stuck to individual Revit elements**. They're not file documents — they're parameter values on the element itself (`STING_STICKY_NOTE_TXT` + `STING_NOTE_AUTHOR_TXT` + `STING_NOTE_DATE_TXT`) plus a project-level index in `_BIM_COORD/sticky_notes.json`.

Use them for **coordination markup that travels with the model**: "check this clash with M&E", "confirm structural load", "QA review required before issue".

### What you can do

| Action | Where | What it does |
|---|---|---|
| **Add note** | Select element(s) → Sticky Notes → Add | Writes a timestamped note to each selected element. Multiple notes pipe-separated (`note1 \| note2`). |
| **View notes** | Select element(s) → Sticky Notes → View | Shows all notes on the selection in a TaskDialog. |
| **Clear notes** | Select element(s) → Sticky Notes → Clear | Wipes the note + author + date parameters. |
| **Quick Note** (dialog) | Document Management dialog → Notes & Briefcase | Single-element inline note from the WPF dialog. |
| **Bulk Delete** | Document Management dialog → Bulk Delete | Removes every sticky note in the project (use with care — irreversible). |
| **Export to CSV** | Sticky Notes → Export | All notes across the project, with element id, category, tag, author, date, text. Lands under the chosen output directory. |
| **Select sticky elements** | Sticky Notes → Select | Selects every element in the project that has a note. Useful for "show me everything that's flagged". |
| **Categories breakdown** | Sticky Note Categories | Counts notes per discipline/category — quickly answers "where are all the open coordination items?". |
| **Sticky Dashboard** | Sticky Note Dashboard | Aggregate view: total notes, by author, by date, by discipline. |
| **Search** | Sticky Note Search | Free-text search across all notes; jumps to matching elements. |

### Best uses

- **Pre-issue QA pass** — coordinator walks the model, drops "QA REVIEW REQUIRED" notes (verification checkbox preset) on flagged elements. Discipline lead clears them as each is fixed. Issue gate: 0 sticky notes outstanding.
- **Cross-discipline coordination** — M&E coordinator drops "check clash with structural beam" on a duct. Structural lead sees the note when they next select the element. No email thread needed.
- **Site-to-office feedback loop** — site engineer photos a defect, mobile creates an issue. Office BIM coordinator finds the linked element in Revit, attaches a sticky note for the design team.
- **Meeting action capture** — during a coordination meeting, drop notes on the elements discussed. Export to CSV at end of meeting → distribute as minutes annex.

### Sticky notes ≠ documents

A sticky note is **on an element**, not in a CDE drawer. It doesn't have suitability or a publishing gate. It's coordination metadata. Don't confuse it with the deliverable register.

---

## Briefcase — read-only project reference library

The briefcase has **two distinct features that share the name** — pay attention to which one you want:

### 1. Briefcase Viewer (`Briefcase` / `View Briefcase`)

An **in-Revit document reader** for project-reference files. Lets you check the BEP, read a transmittal, or browse the COBie spreadsheet **without leaving Revit and without breaking your modeling flow**.

It auto-discovers and indexes:

| Source | Item type | Where it comes from |
|---|---|---|
| BIM Execution Plan | BEP | `_BIM_COORD/bim_execution_plan.json` (generated by BEP wizard) |
| Project dashboard | Dashboard | `_BIM_COORD/project_dashboard.json` |
| Issue register | Issues | `_BIM_COORD/issues.json` (BCF-compatible) |
| Document register | DocReg | `_BIM_COORD/documents.json` |
| Transmittals | Transmittal | `_BIM_COORD/transmittals.json` |
| Review register | Reviews | `_BIM_COORD/reviews.json` |
| COBie outputs | COBie | `_BIM_COORD/cobie_*.csv` and `*.xlsx` |
| User-added files | Reference | Any PDF / spreadsheet dropped into `_BIM_COORD/reference/` |
| STING reference docs | Reference | Tag Guide V3, Parameter Registry from the plugin's data folder |

**Add file**: click "Add File" to copy any PDF, DOCX, or XLSX into the project's reference folder so the whole team has it without leaving Revit.

**Read mode**: opens the file inline (text + tabular preview, capped at the first 200 lines for safety) or launches the OS default app for the full document.

### 2. Document Briefcase generator (`DocumentBriefcase`)

A **portable handover package** — one click, produces an 8-file folder you can zip and email. Different purpose entirely.

When clicked, exports to `_data/17_BRIEFCASE_<code>/<modelName>_Briefcase_<timestamp>/`:

| # | File | Purpose |
|---|---|---|
| 1 | Project Information Summary | Project code, client, organisation, phase, classification |
| 2 | Tag Register | Every tagged element with full 8-segment tag + element id + category + level |
| 3 | Compliance Report | ISO 19650 compliance scores per discipline + per phase |
| 4 | Parameter Audit | Tag completeness — which tokens are missing on which categories |
| 5 | Model Statistics | Element counts by category, view counts, sheet counts, family counts |
| 6 | Sheet Index | Every sheet number + name + revision + discipline |
| 7 | Discipline Breakdown | Counts per discipline (A/S/M/E/P) |
| 8 | MIDP Register | Master Information Delivery Plan: every deliverable with status |

**When to use which:**

- **Briefcase Viewer**: daily — quick lookup of the BEP or a transmittal while you're modeling.
- **Document Briefcase generator**: at stage gates and project end — for handover to the client or to a coordinating party. Time-stamped folder, zip it, attach it to a transmittal.

---

## Document Register — your project's master file list

The **Document Register** is the read-only project-wide index of every document tracked by the system. Open from the BIM Coordination Center → **Doc Register** button.

What it shows (per row):

- File name (ISO 19650 8-segment)
- Document type (drawing, specification, model, schedule, etc.)
- Discipline
- Originator
- Current revision
- CDE state (WIP / SHARED / PUBLISHED / ARCHIVE) — colour-coded
- Suitability code
- Last update date
- Author / responsible

**Filters**: discipline, type, CDE state, suitability — narrows the view to "all M&E drawings issued for construction" or "everything S3 awaiting review".

**Drawing Register Schedule**: a Revit-native variant lives as `STING - Drawing Register` schedule, populated automatically from sheet parameters. Useful for embedding the register on a sheet itself (e.g. a transmittal cover sheet).

**Add Document**: manual register entry for files that don't go through upload — e.g. an external consultant's PDF received via email.

**Validate Naming**: dry-run check against the ISO 19650 pattern before upload. Returns structured per-segment errors.

---

## CDE Status — health check per drawer

The **CDE Status** screen (BCC button) is a one-screen summary:

```
WIP        ████████████░░░  240 docs  [last update 14 min ago]
SHARED     ████░░░░░░░░░░░   75 docs  [last update 2 h ago]
PUBLISHED  ██░░░░░░░░░░░░░   38 docs  [last update yesterday]
ARCHIVE    █░░░░░░░░░░░░░░   12 docs
```

Plus per-discipline breakdown, suitability mix, and "stale" detection (docs sitting in WIP > 30 days, SHARED > 14 days). Click any row to drill into the affected docs.

**Use this to spot bottlenecks**: a queue building up in SHARED means approvals are stuck; a long WIP tail means drafts aren't getting reviewed.

---

## Review Tracker — coordinated-review state

For documents in SHARED awaiting review/comment, the **Review Tracker** maintains a register of who's reviewing what:

- **Per-document review status** — Open / In Review / Commented / Approved / Rejected.
- **Reviewer assignments** — typically one per discipline (e.g. M&E coordinator reviews structural drawings for clash impact).
- **Comment threads** — captured against the document, exported with the transmittal acknowledgement.
- **SLA timers** — review windows (e.g. 5 working days for S2, 10 for S3) automatically tracked; overdue reviews surface on the BCC dashboard.

The review register is a sibling to the document register — the document is the **artefact**, the review is the **process** around it.

---

## MIDP Tracker — the delivery plan

**MIDP** = Master Information Delivery Plan (ISO 19650-2 §5.4). The plan that says: "by milestone X, the team will deliver these N documents at suitability Y."

The MIDP Tracker (BCC button or `MidpTracker` command) shows:

- **Total deliverables** — every row in `deliverables.json` plus published sheets.
- **Sheets published vs total** — `38/240` style ratio.
- **Linked models** — models referenced in the federation.
- **Suitability breakdown** — how many at each S-code.
- **By discipline** — per-discipline counts.
- **By status** — WIP / SHARED / PUBLISHED / ARCHIVE / SUPERSEDED.

This is the **report you send to the client** at each gate review. The Document Briefcase exports a copy as file #8.

---

## Distribution Groups — recipient management

Transmittals and issued deliverables go to people. Rather than re-typing the recipient list every time, **distribution groups** let you save named recipient lists:

- **By type/role/suitability**: "Client Distribution — Drawing — Architect — S4" automatically suggests itself when you create a transmittal matching those criteria.
- **Project Team list** — typically one default group covering the full team (lead designers + coordinator + PM).
- **Discipline-specific** — "Structural Reviewers", "M&E Subs", etc.
- **Client distribution** — usually for S4+ only.

Stored in `_BIM_COORD/distribution_groups.json`. Edit via the Document Management dialog → Distribution Groups tab. The transmittal orchestrator suggests the best-match group automatically based on the document's type / role / suitability triple.

---

## Document search — find anything

A **Lucene full-text index** (under `_BIM_COORD/search_index/`) covers the document register + deliverables + transmittals + sticky notes. Open from the dock-panel search bar or from the Document Management dialog.

**Query operators**:

- `discipline:M` — only mechanical
- `cde:SHARED` — only docs in SHARED
- `suitability:S3` — only S3
- `revision:P03` — exact revision
- `before:2025-04-01` / `after:2025-04-01` — date range
- Free text — searched across file name + description + author + comments

**Saved searches**: stash a frequently-used query under a name. Stored per-user in `saved_searches.json`. The Sticky Note Search command and the Quick Search bar both use the saved search list.

**Cross-project global search**: `/api/search?q=...` on the server hits all projects the user has access to (subject to ACL slice).

---

## Versions and revision compare

Every upload of the same filename creates a new `DocumentVersion` row. The current `DocumentRecord` always points at the latest; older versions are kept indefinitely (subject to retention policy).

| Action | Result |
|---|---|
| **View versions** | `/documents/{id}/versions` lists every version with size, hash, uploader, timestamp |
| **Download specific version** | `/documents/{id}/versions/{n}/download` gets the historical bytes |
| **Revision Compare** | Side-by-side diff of two versions for tagged Revit elements (BCC → Revisions tab) |
| **Track Element Revisions** | Stamps `STING_REVISION_TXT` on every element so a schedule can show what changed between issues |
| **Revision Cloud Auto-Create** | Generates revision clouds on changed sheet regions automatically |

ACL applies to versions — a user denied on the head document can't read its history either.

---

## COBie — facilities-management handover

COBie (Construction Operations Building Information Exchange, BS 1192-4:2014) is the structured handover format for FM data. The plugin has full COBie support:

| Action | Class | Purpose |
|---|---|---|
| **COBie Export** | `COBieExportCommand` | Exports a full COBie 2.4 workbook (19 worksheets — Facility, Floor, Space, Zone, Type, Component, System, Assembly, Connection, Spare, Resource, Job, Document, Attribute, Coordinate, Issue, Impact, Contact + Picklists) as XLSX |
| **COBie Import** | `COBieImportCommand` | Reads back an external COBie workbook to populate / update the model parameters |
| **COBie Type Map** | `COBieDataCommands` | Browse the 70+ equipment-type mappings (HVAC, plumbing, electrical, fire, etc.) |
| **COBie Picklists** | `COBieDataCommands` | View / edit the controlled vocabularies (124 entries) |
| **COBie Job Templates** | `COBieDataCommands` | SFG20 / BS 8210 maintenance job templates (47 entries) |
| **COBie Spare Parts** | `COBieDataCommands` | Spare-part templates per equipment type (38 entries) |

**22 project-type presets** ship with the plugin (Office, School, Hospital, Hotel, Retail, Datacenter, etc.) — pick the closest match, the COBie output structure pre-fills with the relevant equipment categories.

**When to run**: at handover (S6 or S7 stage). The output goes to the FM team and feeds the Asset Information Model.

---

## BCF — issue interchange

**BCF** (BIM Collaboration Format) is the cross-tool standard for issue interchange. The plugin can:

- **BCF Export** — every issue in the project becomes a BCF 2.1 topic with viewpoint, screenshot, and comment thread. Output is a `.bcfzip` file for sharing with consultants on different software.
- **BCF Import** — read incoming BCF zips and create matching issues in the project.

Useful for round-tripping issues with non-Revit consultants (Navisworks, Solibri, Tekla).

---

## Workflow engine — the timed lifecycle

Every transmittal / RFI / TQ / MR creates a **workflow instance** with timed states. The engine ships 5 default workflows:

| Workflow | States | SLAs |
|---|---|---|
| `transmittal_default` | Draft → Issued → Acknowledged | 24 / 72 / ∞ hr |
| `rfi_default` | Open → Reviewed → Responded → Closed | 24 / 48 / 168 / ∞ hr |
| `tq_default` | Open → Answered | 48 / ∞ hr |
| `mr_default` | Draft → Submitted → Approved/Rejected | varies |
| `deliverable_issue_default` | WIP → Shared → Published → Archived | matches CDE |

**SLA breaches** auto-surface on the My Actions inbox + the BCC dashboard. The workflow engine runs a periodic SLA scanner that emits notifications when a step is overdue.

**Custom workflows**: PMs can author project-specific workflows in JSON, drop them in `_BIM_COORD/workflows/`, and the engine picks them up. Useful for project-specific approval chains (e.g. design-review-board sign-off requiring 3 of 5 reviewers).

---

## Audit log — tamper-evident history

Every document operation writes a row to **two places**:

1. **Local JSONL chain** at `_BIM_COORD/audit_log_YYYY_MM.jsonl` — SHA-256 chained, each row references the previous row's hash. Tamper a row and `VerifyChain` fails on the next call.
2. **Server `AuditLogs` table** — same SHA-256 chaining mechanism server-side, ingested via `POST /audit-events/batch` with action prefix forced to `plugin.` so a malicious client can't spoof admin events.

**What's logged**: every CDE transition, every approval decision, every member ACL change, every transmittal send, every workflow state change, every document upload + delete. **Not** logged: read operations (would 100× the volume).

**Retention**: server side is partitioned monthly; cold partitions detach to object storage. Local JSONL grows by month — old months can be archived but never edited.

**Verification command** on the server: `/api/admin/audit/verify` walks the chain and reports any break. Required for SOC2 / ISO 27001 audits.

---

These are the streamlined recipes that work well in practice. Adapt them to your project structure.

### 🧑‍💼 BIM Coordinator — daily routine

**Morning (5 min):**
1. Open the **BIM Coordination Center** → **Overview** tab.
2. Check the compliance gauge + warnings count + open issues count.
3. If any pending approvals are sitting on you — go to the mobile **Inbox → Document Approvals** screen, approve/reject from there (it's faster than the desktop dialog).

**During the day:**
- Use **Tag & Combine** in Revit to keep ISO 19650 tags up to date as you place elements.
- Run the **Auto-Tag** updater on geometry-changed elements to mark stales.
- Issue deliverables from the BCC Deliverables tab as work matures.

**End of day:**
- Hit the **Sync** button on the dock panel (or let the 5-min auto-tick do it).
- The reconcile pass will push any deliverables that drifted while you were offline.

### 🏗️ Discipline lead — preparing a package

**Plan ahead** of issue date:
1. Confirm your file naming matches the ISO 19650 convention (use `validate-name` on the server, or just upload — the server will reject and tell you what's wrong).
2. Confirm your CDE access includes WIP + SHARED for your discipline (look at **Documents tab** chip strip — if SHARED isn't there, ping your PM).

**Issue cycle:**
1. Render sheets in Revit.
2. **BIM Coordination Center → Deliverables → Issue Deliverable**.
3. Plugin renders the cover sheet, bumps revision, mirrors to server. Document is now in WIP.
4. When the package is ready for the team: **Publish Stage 1** (S2) or **Publish Stage 2** (S3) — moves it to SHARED.
5. When the team has signed off: **Publish Stage 3** — server requires Manager approval first.

### ✅ Project Manager — approvals + governance

**Once a day** (or when push notification fires):
1. **Mobile or web** → **Inbox → Document Approvals**.
2. Each pending approval shows: file name, transition (e.g. SHARED→PUBLISHED), discipline, who requested it, when, their comment.
3. Approve or reject inline. Comment optional.
4. Approved? The originating coordinator can now run the SHARED→PUBLISHED transition from their plugin or the documents tab.

**Weekly:**
- Review the **Project Members** tab in the BCC: are anyone's allow-lists drifting from what they should be? (E.g. a sub-contractor whose package finished should be moved to a "client read-only" profile.)
- Check the **audit log** for any unusual transitions (server `/api/admin/audit`).

**End of stage:**
- Run **Bulk Issue Deliverables** for the whole package.
- Generate a transmittal (template B06) listing the package.

### 👷 Trade subcontractor — limited slice

You'll see only your discipline (e.g. M for mechanical), all four CDE states for that discipline.

1. **Documents tab** shows your discipline only — chips for WIP / SHARED / PUBLISHED / ARCHIVE.
2. Use **Issue Deliverable** when your draft is ready for the lead coordinator.
3. After the lead promotes to SHARED and PM approves to PUBLISHED, you'll see the new state via SignalR push within seconds.

### 🏢 Client / external reviewer — read-only

You'll see PUBLISHED + ARCHIVE only.

1. **Mobile app** → Documents tab — only published+archived material renders.
2. PDFs / DWGs are downloadable; rendered transmittals and meeting minutes show in a viewer.
3. No upload, no transitions — the system silently filters everything else out.

### 🔍 QA reviewer — pre-issue model walkthrough

Before any issue, the QA reviewer does a model walkthrough. Sticky notes are the workflow.

1. **Open the briefcase viewer** (`BriefcaseView`) — confirm the BEP, tag guide, and current dashboard are accessible.
2. **Walk the model** view by view. For each flag, select element(s) → **Sticky Note → Add** → tick "QA Review Required" or write a custom note.
3. When done, run **Sticky Note Dashboard** — the count is your "blocked items" total.
4. **Sticky Note Search** with `discipline:M` (or whichever) hands the list to each discipline lead.
5. As issues are fixed, **Sticky Note → Clear** drops the count.
6. **Pre-issue gate**: 0 outstanding sticky notes → ready to issue.
7. **Export Sticky Notes** to CSV at start and end of the cycle as a delta record (paste into the transmittal acknowledgement so the team has a record of what was caught).

### 🏛️ Stage gate handover — the briefcase ritual

At the end of each RIBA stage:

1. **MIDP Tracker** — confirm every planned deliverable for the gate is at SHARED or PUBLISHED.
2. **CDE Status** — any docs stuck in WIP > 14 days at gate? Decide: drop them or escalate.
3. **Bulk Issue Deliverables** for every gate deliverable simultaneously (one transaction group, atomic).
4. **DocumentBriefcase** generates the 8-file handover package. Folder is timestamped — keep it.
5. **CreateTransmittalOrchestrated** with template C13 (formal letter of transmittal) referencing the briefcase folder.
6. **Distribution group** auto-suggested by the orchestrator — verify before sending.
7. **Workflow** auto-starts SLA timer; recipients get push + email; acknowledgements come back to the inbox.
8. **Audit chain** records the entire sequence, replayable at the next gate.

### 🛠️ FM handover — the COBie ritual

For project end-of-life handover to the FM team:

1. Run **COBie Export** at draft (still in WIP / SHARED state) — review the workbook for missing data.
2. Open **COBie Type Map** to fix any unmapped equipment categories.
3. Open **COBie Picklists** to verify controlled vocabularies match the FM team's Asset Information Requirements (AIR).
4. Re-run **COBie Export** until clean.
5. **Document Briefcase** + **COBie XLSX** + **As-built drawings** (PUBLISHED + S6) bundled into a final transmittal.
6. **HandoverManual** command generates the FM operations manual referencing all of the above.
7. CDE state moves to ARCHIVE (or stays at PUBLISHED + S7 if your project's retention policy keeps it warm).

---

## Streamlining tips

These are small habits and configurations that compound to save real time.

### 0. Sticky-note discipline as a pre-issue gate

Treat **0 outstanding sticky notes** as a hard pre-issue requirement. The QA reviewer drops a sticky note on every flag they raise; the discipline lead clears each as they fix it. Use **Sticky Note Dashboard** at the start of each issue cycle to see how many are open per discipline, and **Sticky Note Search** to jump straight to elements when reviewing. This habit alone catches more issues than any other QA mechanism.

### 1. Use access profiles religiously

Don't tick three multi-selects every time you invite someone. Define 5–7 profiles upfront for the project (Coordinator, Discipline lead, Sub, Reviewer, Client, Auditor) and pick one per invite. You'll save 30 seconds per invite × every invite for the project life.

### 2. Let the auto-tag updater carry the load

Turn on the **Auto-Tag IUpdater** in the dock panel. Every time you place a new element, it gets the 7-token tag automatically. Every time geometry changes, the element gets marked stale. You'll never have to remember to re-tag.

### 3. Issue early, publish late

Promote to SHARED **as soon as** the file is reviewable, even if it's not final — let the team coordinate against it. Publish only when truly construction-ready. The cost of a too-early publish is rework; the cost of a too-late share is missed coordination.

### 4. Use templates for transmittals — never hand-write them

The template engine has 16 standard documents (A01–A04 deliverables, B06–B09 transmittals/RFIs/TQs/responses, C10–C13 materials/submittals/variations/letters, D14–D16 meetings/progress/handover). Pick the right one and let the plugin fill the tokens. The doc number, project code, dates, distribution list, signature block all populate from project info.

### 5. Trust the workflow engine for SLAs

When you create a transmittal, the workflow starts a timer (24 hr / 72 hr / unlimited). The server tracks who's overdue and surfaces it on the My Actions inbox. Don't rebuild this in a spreadsheet.

### 6. Approve from your phone

The on-site approvals inbox is **faster** than opening the BCC dialog on a workstation. If you're a PM, install the app and turn on push.

### 7. Use the conflict triage

If two people edit the same row offline (rare but possible with the plugin's offline buffer), the conflict triage screen lets you pick which wins. Don't bypass it — that's how silent data corruption happens.

### 8. Promote through CDE — don't move files

Never drag a file out of `01_WIP` into `02_SHARED` on disk. The folders are **output**, not the source of truth. Use the transition button. The server records the audit row, the SignalR event fires, the audit log chains. Direct file moves bypass all that.

### 9. Review compliance before a big issue

The compliance scan (cached, 30-second TTL) tells you what % of elements have complete tags. Don't issue at < 80% — you're shipping incomplete data.

### 10. Keep your audit chain healthy

The plugin's local audit log is SHA-256 chained. **Don't manually edit `audit_log_*.jsonl`** — it'll break the chain and the next `VerifyChain` call will fail. The server gets a copy via `audit-events/batch` so cross-machine queries work, but the local chain is the tamper-evidence anchor.

### 11. Use the briefcase viewer, not Explorer

When you need to check the BEP or read a transmittal mid-modeling, **use the in-Revit briefcase viewer** rather than alt-tabbing to Windows Explorer. It's faster, scoped to the project, and read-only so you can't accidentally edit the file.

### 12. Build distribution groups before you need them

At project kickoff, create the 4–6 distribution groups you'll re-use: "Project Team", "Architects only", "Structural reviewers", "M&E subs", "Client distribution", "Authority Having Jurisdiction". The transmittal orchestrator will then auto-suggest the right group based on the document type / discipline / suitability, and you stop re-typing recipient lists for the rest of the project.

### 13. Generate a Document Briefcase at every stage gate

Don't wait for project end. At each RIBA stage (or equivalent), run **DocumentBriefcase** — the timestamped folder is your stage-gate handover artefact. Zip it, attach to a transmittal, distribute. End-of-project handover then becomes "we have ten of these, here's the latest" rather than a panicked snapshot.

### 14. Save your common searches

If you find yourself repeatedly typing `cde:SHARED discipline:M after:last-month`, save it as "M&E in review this month". Saved searches stash under your user profile and load in one click. The Sticky Note Search tab uses the same store, so QA-flag queries stick around too.

### 15. Run COBie export early, not just at handover

Run a draft COBie export at S2 (review) — most issues with type-mapping or missing FM data show up as red rows. Fixing them at S2 is cheap; fixing them at handover is expensive (the FM team is waiting and you've already moved on).

---

## Common pitfalls

### "I can't see a document I know exists."

You don't have access to its CDE state, discipline, or suitability. Check **Documents tab → chip strip** — if a chip is missing, that whole drawer is hidden. Ask the PM to extend your access profile.

### "Publish failed — approval required."

Someone with Manager role hasn't approved the SHARED→PUBLISHED transition. Click **Request Approval** on the document detail screen, fill in why, and a Manager will see it on their approvals inbox.

### "Plugin says synced but server doesn't see it."

The fire-and-forget mirror failed silently. Two recovery paths:
1. Hit the dock panel **Sync** button — this runs `ReconcileAsync` immediately, which scans `deliverables.json` for unsynced rows and pushes them.
2. Wait for the next 5-min tick — same effect, automatic.

If both fail, check `StingTools.log` for `DeliverableServerSync … failed` lines. Server unreachable, expired token, or rate-limited are the usual culprits.

### "Two people edited the same deliverable while offline."

Open the **Conflicts** screen on mobile. It shows the diff and lets you pick a winner. The unchosen side becomes a superseded revision in the audit log.

### "Why does my mobile app keep showing PUBLISHED docs I'm not supposed to see?"

It shouldn't. If it does, you either have an old build (without ACL filters) or your member row hasn't been updated. Force-quit the app, sign out and back in — it re-fetches `/members/me` and re-renders the chip strip.

### "I tried to bypass the approval gate by editing the JSON."

That works locally but the next sync hits the server, which still enforces the gate, and your row is rejected. Don't bother — the gate exists for a reason (ISO 19650-2 §5.6).

### "The mobile app is missing a tab I see on web."

Mobile is intentionally a subset — focused on field work. If you need full document management, use the BCC in Revit or the web dashboard.

### "Sticky notes don't appear on the same element after I copy it."

Sticky notes live on the **instance** parameter, not the type. Copying an element copies the parameter value (so the note travels with the copy). Mirroring or array-creating an element from the family editor does not. If you've copied an element from a different project, the note doesn't come along — sticky notes are per-document.

### "Briefcase viewer says 'no items'."

You haven't generated the underlying files yet. The viewer surfaces what exists in `_BIM_COORD/`. Run BEP wizard to populate `bim_execution_plan.json`, generate the dashboard from the BCC Overview tab, etc. The viewer is a window — you have to put things in the room first.

### "Document Briefcase generator says it can't write to the output folder."

Likely cause: the model is on a network share and the project root isn't writable, or `_data/` was created with restrictive ACLs. The briefcase falls back to `OutputLocationHelper`'s default (typically `Documents\STING_Output\`) — check the log line for the actual path used.

### "COBie export looks empty for half my equipment."

The equipment categories aren't mapped in `COBIE_TYPE_MAP.csv`. Open **COBie Type Map** to see what's mapped; add rows for missing categories before re-running. Common gap: custom families with non-standard category assignments.

### "My saved search returns documents I shouldn't see."

The search index respects ACL at query time — every result is re-checked against the caller's slice before being returned. If you genuinely see a doc you shouldn't, file it as a bug; it's not supposed to happen.

### "My distribution group keeps suggesting the wrong recipients."

The matcher scores groups by `(type, role, suitability)` triple. If your transmittal is for a deliverable with discipline=M and suitability=S4, it'll prefer a group whose tags include all three. Tighten the group's filters (or untag groups that shouldn't match) — `_BIM_COORD/distribution_groups.json` is the master.

### "MIDP totals don't match my schedule."

The MIDP register counts every row in `deliverables.json` plus published sheets. If your schedule is off, run **Drawing Register Sync** to re-pull from the model — the MIDP is downstream of the document register, which is downstream of the model.

### "I see 'workflow_state.json deserialization failed'."

The schema versioning gate caught a stale file. Don't edit it manually. Either delete the file (workflows reset to default — old in-flight ones lost) or run the Workflow Migrate command to upgrade in place.

---

## Quick reference

### Roles, in order of authority

```
Viewer < Contributor < Coordinator < Manager < Admin < Owner
                                              SecurityOfficer (read-only audit)
```

### Required role per transition

| Transition | Minimum role | Approval required? |
|---|---|---|
| WIP → SHARED | Coordinator | No |
| SHARED → WIP (rework) | Coordinator | No |
| **SHARED → PUBLISHED** | **Manager** | **Yes (DocumentApproval)** |
| PUBLISHED → ARCHIVE | Manager | No |
| PUBLISHED → SUPERSEDED | Manager | No |

### Suitability codes (UK BS EN ISO 19650-2)

| Code | Meaning | Typical CDE state |
|---|---|---|
| S0 | Initial | WIP |
| S1 | Suitable for coordination | SHARED |
| S2 | Suitable for information | SHARED |
| S3 | Suitable for review/comment | SHARED |
| S4 | Suitable for construction | PUBLISHED |
| S5 | Suitable for manufacture | PUBLISHED |
| S6 | Suitable for PIM/AIM authorisation | PUBLISHED |
| S7 | As-built | ARCHIVE |

### Where things live on disk (per project)

```
<project>/
├── _data/                           ← consolidated metadata root
│   └── _BIM_COORD/
│       ├── manifest.json            ← project info + doc engine config
│       ├── deliverables.json        ← deliverable register
│       ├── doc_sequences.json       ← per-type sequence counters
│       ├── transmittals.json        ← transmittal log
│       ├── reviews.json             ← review tracker register
│       ├── issues.json              ← issue register (BCF-compatible)
│       ├── documents.json           ← document register entries
│       ├── sticky_notes.json        ← project-level sticky-note index
│       ├── workflow_state.json      ← active workflow instances
│       ├── audit_log_YYYY_MM.jsonl  ← SHA-256 chained audit (one per month)
│       ├── distribution_groups.json ← named recipient groups
│       ├── saved_searches.json      ← per-user saved queries
│       ├── bim_execution_plan.json  ← BEP (briefcase viewer source)
│       ├── project_dashboard.json   ← live project-status snapshot
│       ├── search_index/            ← Lucene full-text index
│       ├── templates/               ← rendered template sources
│       │   └── *.docx / *.xlsx
│       ├── workflows/               ← workflow definition JSONs
│       ├── reference/               ← user-added reference PDFs (briefcase)
│       └── generated/               ← rendered output
│           └── YYYYMMDD_{number}_{template}.docx
├── 17_BRIEFCASE_<code>/             ← Document Briefcase exports
│   └── <model>_Briefcase_<timestamp>/
│       ├── 01_ProjectInfo.csv
│       ├── 02_TagRegister.csv
│       ├── 03_ComplianceReport.csv
│       ├── 04_ParameterAudit.csv
│       ├── 05_ModelStatistics.csv
│       ├── 06_SheetIndex.csv
│       ├── 07_DisciplineBreakdown.csv
│       └── 08_MIDPRegister.csv
├── 01_WIP/                          ← rendered output cache (per CDE state)
├── 02_SHARED/
├── 03_PUBLISHED/
└── 04_ARCHIVE/
```

The **server is the system of record** for state. The on-disk folders are just where the rendered files land for distribution.

### All document-related Revit commands at a glance

| Command tag | What it does |
|---|---|
| `DocumentRegister` | Open the project document register |
| `AddDocument` | Manual register entry |
| `DocumentBriefcase` | Generate the 8-file portable handover package |
| `Briefcase` / `BriefcaseView` | Open the in-Revit reference document viewer |
| `BriefcaseRead` | Read a specific briefcase item |
| `BriefcaseAddFile` | Add a PDF/DOCX/XLSX to the briefcase reference set |
| `CDEStatus` | One-screen CDE drawer health check |
| `MidpTracker` | Master Information Delivery Plan summary |
| `ReviewTracker` | Coordinated-review register |
| `CreateTransmittal` | Quick transmittal (uses orchestrator) |
| `CreateTransmittalOrchestrated` | Full pipeline: number → render → workflow → audit |
| `BulkIssueDeliverables` | Issue every selected deliverable in one transaction group |
| `IssueDeliverable` / `ReIssueDeliverable` / `PublishDeliverable` / `CancelDeliverable` / `SupersedeDeliverable` / `ReplaceDeliverable` | Lifecycle state machine commands |
| `ValidateDocNaming` | ISO 19650 naming dry-run |
| `BriefcaseAddFile` | Drop a reference PDF into the project briefcase |
| `ElementStickyNote` | Add / view / clear sticky note on selected elements |
| `ExportStickyNotes` | All notes across project → CSV |
| `SelectStickyElements` | Select every element with a note |
| `StickyNoteCategories` | Per-category note counts |
| `StickyNoteDashboard` | Sticky-note aggregate view |
| `StickyNoteSearch` | Free-text search across all notes |
| `COBieExport` / `COBieImport` | Full COBie 2.4 round-trip |
| `BCFExport` / `BCFImport` | BCF 2.1 issue interchange |
| `RaiseIssue` / `IssueDashboard` / `UpdateIssue` | Issue tracker (related to documents via issue attachments) |
| `CreateRevision` / `RevisionDashboard` / `RevisionCompare` | Revision management |
| `ModelHealthDashboard` / `FullComplianceDashboard` | Project-wide health metrics (briefcase items) |
| `BriefcaseView` button on dock panel | Quick-launch briefcase viewer |

### Where things live on the server

| Table | Purpose |
|---|---|
| `Documents` | The current state of every document. CDE / suitability / revision / file path. |
| `DocumentVersions` | Per-version history. |
| `DocumentApprovals` | Approval requests + decisions, ISO 19650-2 §5.6. |
| `ProjectMembers` | Project membership + role + per-folder ACL allow-lists. |
| `AccessProfiles` | Tenant-level named ACL presets. |
| `AuditLogs` | Tamper-evident audit (SHA-256 chained server-side too). |
| `Transmittals` / `Meetings` / `WorkflowRuns` | Lifecycle artefacts mirrored from the plugin. |

---

## What this guide doesn't cover

- **Family library + tag schema setup** — see `TAGGING_PROCEDURES_GUIDE.md`.
- **Drawing template profiles + view style packs** — see `AEC_PRODUCTION_SET_STRATEGY.md`.
- **CDE filter library** — see `AEC_FILTER_LIBRARY.md`.
- **Per-tenant filter / role customisation** — admin / DBA territory.
- **Off-network NTFS folder ACL mirroring** — separate desktop sync agent, not part of the document manager.
- **Detailed BIM Execution Plan (BEP) authoring** — see `BIM_MANAGEMENT_GUIDE.md`.

---

## Further reading

- [ISO 19650-2:2018 — Information management using BIM, delivery phase](https://www.iso.org/standard/68080.html) (the standard the CDE state machine implements)
- [UK BIM Framework — Guidance Part D: Information Production Methods](https://www.ukbimframework.org/) (the suitability code definitions in plain English)
- The in-repo guides linked above for the technical layers behind the document manager.
