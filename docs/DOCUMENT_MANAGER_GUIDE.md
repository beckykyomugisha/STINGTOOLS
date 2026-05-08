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
- **The file itself** (PDF / DOCX / DWG / IFC / RVT / etc.).

In the plugin you'll also see a **deliverables register** (`deliverables.json`) and **rendered output** (`generated/*.docx` for transmittals, RFIs, MR forms, etc.). These mirror to the server automatically.

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

---

## Best-practice workflows

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

---

## Streamlining tips

These are small habits and configurations that compound to save real time.

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
│       ├── workflow_state.json      ← active workflow instances
│       ├── audit_log_YYYY_MM.jsonl  ← SHA-256 chained audit
│       ├── distribution_groups.json ← named recipient groups
│       ├── saved_searches.json      ← per-user saved queries
│       ├── search_index/            ← Lucene index
│       ├── templates/               ← rendered template sources
│       │   └── *.docx / *.xlsx
│       ├── workflows/               ← workflow definition JSONs
│       └── generated/               ← rendered output
│           └── YYYYMMDD_{number}_{template}.docx
├── 01_WIP/                          ← rendered output cache
├── 02_SHARED/
├── 03_PUBLISHED/
└── 04_ARCHIVE/
```

The **server is the system of record** for state. The on-disk folders are just where the rendered files land for distribution.

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
