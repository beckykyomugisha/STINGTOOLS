# STING Document Manager — Complete Guide

> **Who wrote this?** The STING Document Manager is built into the STING Tools Revit plugin.
> This guide explains how to use it. If you are new to the plugin entirely, start with the
> BIM Coordination Center guide first, then come back here.

> **Cross-references**
> - **Drawings that become deliverables**: `docs/guides/DRAWING_TYPE_MANAGER_GUIDE.md`
> - **Tagging elements before issuing**: `docs/TAGGING_PROCEDURES_GUIDE.md`
> - **BIM Management (BEP, MIDP)**: `docs/BIM_MANAGEMENT_GUIDE.md`
> - **Healthcare workflows**: `docs/HEALTHCARE_PACK_DESIGN.md`

---

## Who this guide is for

This guide is written for someone who manages files and paperwork but has never used a CDE
(Common Data Environment) before. If you have run a paper filing system, you already understand
the concepts — the software just does the same thing faster, with a record of every step.

You do not need to be a software specialist to follow this guide. Every step names the exact
button to click, the exact field to fill in, and what you will see on screen.

If you are an experienced BIM coordinator who wants a technical reference, jump straight to
Part 4 (every button) and the Quick Reference at the end.

---

## The Big Picture — Your Project's Filing System

Imagine a **very organised office filing cabinet**. Before the digital age, a project office
kept physical drawings in a filing room. The filing room had four distinct drawers:

- A **yellow drawer** labelled "DRAFTS — Do Not Distribute." Nobody outside your team
  was allowed to take anything from it. Files in here might be incomplete, wrong, or
  changing daily.
- A **blue drawer** labelled "ISSUED FOR COORDINATION." The whole project team could
  read these but not use them for construction yet. They were there so different consultants
  could coordinate their work against each other.
- A **green drawer** labelled "ISSUED FOR CONSTRUCTION." These were the authorised,
  official drawings that builders would work from. Nothing went in here without a senior
  partner's signature.
- A **grey drawer** labelled "SUPERSEDED / ARCHIVE." Old versions that had been replaced.
  You kept them so you could answer the question "what did the drawing say on 12 March?"
  but nobody built from them.

STING's Document Manager is that filing cabinet, but digital. Instead of physical drawers,
the system calls them **CDE states** (CDE stands for Common Data Environment). There are
the same four:

| The Drawer | CDE State | Colour in STING | Who can see it |
|---|---|---|---|
| Drafts | WIP — Work In Progress | Yellow | Your discipline team only |
| For coordination | SHARED | Blue | The whole project team |
| For construction | PUBLISHED | Green | Everyone, including clients |
| Old versions | ARCHIVE | Grey | Anyone (read-only) |

Every single document in the system lives in exactly one drawer at any given time. When you
"issue" a document, you are moving it from one drawer to another. That move is recorded,
dated, and signed against your login — exactly as the senior partner's signature worked in
the paper world.

The key difference from paper: **you cannot skip a drawer**. A drawing must go through
SHARED before it can reach PUBLISHED. This is the ISO 19650 rule — it exists because
"Issued for Construction" drawings that never went through a coordination review are
a building site hazard.

---

## Part 1 — Setup

### First-time setup: what happens when you first open a project

The very first time you open a Revit project file with STING Tools loaded, the system does
a silent setup task in the background. You will not see a dialog pop up. You will not be
asked to do anything. But the following happens automatically:

1. **STING checks** whether the `_BIM_COORD` folder exists inside your project's folder.
   (The project folder is wherever you saved the `.rvt` file — for example,
   `\\server\Projects\BuildingA\BIM\`)

2. If the folder does not exist, STING creates it and copies 16 template files and
   5 workflow files into it. This is called "extracting embedded resources." The files
   are baked into the plugin itself; STING is basically unpacking them for your project.

3. STING reads basic information from Revit's **Project Information** (the dialog you reach
   from Revit's Manage tab) — things like the project name, project number, and organisation
   code — and writes them into a file called `manifest.json`. This file becomes the
   "project settings" for all the document templates.

4. STING also creates a `doc_sequences.json` file, which is your auto-numbering system.
   Every document number the system ever generates for this project comes from this file.
   It starts at zero and counts up forever; you never need to manage it manually.

**How to verify the setup worked:**

1. Open Windows Explorer.
2. Navigate to the folder where your `.rvt` file is saved.
3. You should see a `_BIM_COORD` folder there.
4. Inside it, open the `templates` folder. You should see 16 `.docx` and `.xlsx` files.
5. Open the `workflows` folder. You should see 5 `.json` files.

If those folders and files are there, setup succeeded.

> **Stuck?** If you cannot find the `_BIM_COORD` folder at all, it means STING did not
> run setup. The most common cause is that Revit was already open when you loaded STING
> for the first time — the document-opened event did not fire. Close the project and
> re-open it. If it still does not appear, check the STING log file (`StingTools.log`
> in the same folder as the STING DLL) for any error messages.

> **Stuck?** If the folder is there but the `templates` subfolder is empty, the file
> extraction failed partway through. Delete the `_BIM_COORD` folder entirely and re-open
> the project. STING will re-extract from scratch.

**What if Project Information is empty?**

If your project's Project Information dialog (Manage tab → Project Information) does not
have a project number or organisation code filled in, STING will still create `manifest.json`
but the fields will be blank. Your document templates will then show blank fields where
the project name should appear. Fill in Project Information as early as possible — it feeds
every template in the system.

---

### The folder structure: what lives where and why

Here is the complete map of everything STING creates under your project folder. This section
exists so that if anything ever goes wrong, you know exactly what to look for and whether
you need to worry about it.

```
<Your Revit project folder>  (wherever your .rvt file is saved)
│
└── _BIM_COORD/                       ← the Document Manager's home base
    │
    ├── templates/                    ← the 16 reusable document templates
    │   ├── manifest.json             ← project settings for all templates
    │   ├── deliverable_standard.docx ← A01: deliverable cover sheet
    │   ├── deliverable_cancelled.docx← A02: cancellation notice
    │   ├── deliverable_superseded.docx ← A03: superseded notice
    │   ├── deliverable_replacing.docx← A04: replacing notice
    │   ├── deliverable_tabular.xlsx  ← A05: tabular deliverable list
    │   ├── transmittal.docx          ← B06: transmittal memo
    │   ├── technical_query.docx      ← B07: technical query
    │   ├── rfi.docx                  ← B08: request for information
    │   ├── technical_response.docx   ← B09: response to TQ or RFI
    │   ├── material_requisition.docx ← C10: material requisition
    │   ├── submittal_cover.docx      ← C11: submittal cover sheet
    │   ├── variation.docx            ← C12: variation / change order
    │   ├── letter_transmittal.docx   ← C13: formal letter of transmittal
    │   ├── meeting_minutes.docx      ← D14: meeting minutes
    │   ├── progress_report.docx      ← D15: progress report
    │   └── handover_certificate.docx ← D16: handover certificate
    │
    ├── workflows/                    ← the 5 built-in workflow definitions
    │   ├── transmittal_default.json  ← steps for a standard transmittal
    │   ├── rfi_default.json          ← steps for an RFI
    │   ├── tq_default.json           ← steps for a technical query
    │   ├── mr_default.json           ← steps for a material requisition
    │   └── deliverable_issue_default.json ← steps for issuing a deliverable
    │
    ├── generated/                    ← rendered documents (output)
    │   └── 20260514_PRJ-001_transmittal.docx  ← example output file
    │
    ├── healthcare/                   ← (healthcare projects only)
    │   └── mgas_verifications/       ← medical gas verification logs
    │
    ├── deliverables.json             ← the master deliverable register
    ├── transmittals.json             ← log of every transmittal created
    ├── workflow_state.json           ← currently open workflows
    ├── doc_sequences.json            ← the auto-numbering counter store
    ├── audit_log_2026_05.jsonl       ← tamper-evident audit log (one per month)
    ├── distribution_groups.json      ← email recipient groups
    ├── saved_searches.json           ← your saved document search queries
    ├── search_index/                 ← fast search database (folder of index files)
    ├── drawing_types.json            ← project drawing type overrides (if set)
    ├── panel_schedule_templates.json ← panel schedule config (electrical projects)
    ├── aec_filters.json              ← project filter overrides (if set)
    ├── bim_execution_plan.json       ← generated BEP (if BEP wizard was run)
    ├── project_dashboard.json        ← live project status snapshot
    ├── issues.json                   ← issue register
    ├── documents.json                ← document register entries
    ├── sticky_notes.json             ← element-level coordination notes index
    ├── reviews.json                  ← review tracker register
    └── reference/                   ← user-added reference PDFs and documents
        └── (any PDFs you add via "Add File" in the Briefcase)
```

**What each item is, and whether you ever need to touch it manually:**

---

**`templates/manifest.json`**

This is the project's configuration file for all 16 document templates. It contains the
project name, project number, originator code, company name, client name, and other
fields that appear on every document header.

Do you need to touch it? Yes — once. When you first set up the project, review `manifest.json`
in a text editor (Notepad works fine) and check that the project code, company name, and
client name are correct. If they came in blank from Project Information, fill them in here.

If this file gets corrupted (unreadable) or deleted, STING will recreate it from Project
Information when you re-open the project. You will lose any custom fields you added manually.

---

**`templates/*.docx` and `templates/*.xlsx`**

These are the 16 document templates. They look like normal Word documents when you open them.
They contain special `{{token}}` placeholders that STING replaces with real project data when
it renders a document.

Do you need to touch them? Only if you want to customise the look. For example, if you want
to add your company logo to the transmittal, open `transmittal.docx` in Word, add the logo
to the header, save it, and close it. STING will use your modified version from then on.

**Warning**: Do not delete the `{{token}}` placeholders. Those curly-brace codes are how STING
inserts the date, document number, recipient name, and other data. If you delete them, the
rendered document will show blank fields.

If a template file gets deleted, STING will re-extract the original factory version from
inside the plugin the next time you open the project. You will lose your customisations.

---

**`workflows/*.json`**

These define the steps and time limits for each type of workflow. For example,
`transmittal_default.json` says: "Step 1 is Draft. Step 2 is Issued (must happen within
24 hours). Step 3 is Acknowledged (must happen within 72 hours)."

Do you need to touch them? Rarely. If your project has unusual approval chains — for example,
a client who requires sign-off within 5 business days rather than the standard 72 hours —
a BIM manager can edit these files in a text editor. Otherwise, leave them alone.

---

**`generated/`**

Every time STING renders a document (creates a transmittal, issues a deliverable, etc.), it
saves the output Word file here. The filename includes the date and the document number so
you can always find it.

Do you need to touch it? No. STING writes here; you read from here if you need a copy of a
specific rendered document. Never edit files in this folder — if the document needs to change,
re-issue it through STING so the new version is tracked.

---

**`deliverables.json`**

This is your project's deliverable register. Every time you issue a deliverable through STING,
a row is added or updated here. It records the document number, current state (WIP / SHARED /
PUBLISHED / ARCHIVE), revision, suitability code, and when each transition happened.

Do you need to touch it? No. This is a system file. If it gets corrupted, you may lose the
state history of your deliverables. Back it up alongside your `.rvt` file.

---

**`transmittals.json`**

A log of every transmittal created on this project. Each entry records the transmittal number,
date, recipient, document list, and workflow state.

Do you need to touch it? No. If it gets deleted, you lose the transmittal history but can
recreate new transmittals normally.

---

**`workflow_state.json`**

A record of every workflow currently in progress. For example, if you created a transmittal
an hour ago and the recipient has not yet acknowledged it, there is an open workflow instance
in this file tracking that.

Do you need to touch it? No. If it gets corrupted, you may see an error message on the STING
dock panel. The safest recovery is to delete the file — STING will create a fresh one. In-flight
workflows will be lost (you will need to re-create them manually), but nothing else is damaged.

> **Stuck?** If you see "workflow_state.json deserialization failed" in the STING log or on
> screen, delete `workflow_state.json` from the `_BIM_COORD` folder and re-open the project.

---

**`doc_sequences.json`**

The auto-numbering counter. Every document number STING generates for this project comes from
here. It stores the highest number used for each document type.

Do you need to touch it? Almost never. The only time you might need to is if you are migrating
from another system and need the numbering to continue from a specific number rather than 001.
In that case, a BIM manager can edit the relevant counter by hand. If this file gets deleted,
the next document STING creates will start from 001 again — potentially creating duplicate
numbers if some already exist.

---

**`audit_log_YYYY_MM.jsonl`**

The tamper-evident audit trail. Every document action is recorded here. A new file is created
each calendar month (hence the `YYYY_MM` in the filename). The file uses a SHA-256 chain
(explained in detail in Part 5).

Do you need to touch it? Never. This file is read-only from your perspective. Editing it
manually breaks the tamper-evidence chain.

---

**`distribution_groups.json`**

Your named recipient lists. Each group has a name, a list of email addresses, and tags
that describe what documents it should receive.

Do you need to touch it? Only to add or edit distribution groups. See Part 7 for instructions.

---

**`saved_searches.json`**

Your personal saved searches from the document search bar. Stored per-user.

Do you need to touch it? No. If it gets deleted, you lose your saved searches but can
recreate them.

---

**`search_index/`**

A folder containing the fast search database (built using a technology called Lucene). STING
uses it to find documents in milliseconds.

Do you need to touch it? No. If it gets corrupted, STING will automatically rebuild it. If
search is returning wrong results, you can force a rebuild by deleting this folder — STING
will recreate it the next time you search.

---

### The 16 document templates explained

These are the pre-built forms that STING can fill in and produce for you automatically. Think
of them as headed notepaper with blanks that STING fills in. You choose the right form, fill
in a few extra details, and STING does the rest.

| ID | File | What it produces | When you would use it |
|---|---|---|---|
| A01 | `deliverable_standard.docx` | Deliverable cover sheet | Every time you formally issue a drawing or document. Goes on top of the package like a cover letter. |
| A02 | `deliverable_cancelled.docx` | Cancellation notice | When a document is being formally cancelled — for example, a drawing that is no longer needed. Puts the project team on notice that this document is void. |
| A03 | `deliverable_superseded.docx` | Superseded notice | When a document has been replaced by a newer revision. Explains what the new document number is. |
| A04 | `deliverable_replacing.docx` | Replacing notice | The other side of A03 — this goes with the new document, stating what it replaces. |
| A05 | `deliverable_tabular.xlsx` | Tabular deliverable list | A formatted spreadsheet listing multiple deliverables in one package. Useful for large issue packages (20+ drawings). |
| B06 | `transmittal.docx` | Transmittal memo | A short formal letter listing documents being sent. Use this every time you send drawings or documents to anyone outside your immediate team. |
| B07 | `technical_query.docx` | Technical query (TQ) | When you need a formal written answer to a design question. Creates a numbered TQ that can be tracked to resolution. |
| B08 | `rfi.docx` | Request for Information (RFI) | Similar to a TQ but typically raised from site — "the drawing says X but on site we see Y, what do we do?" |
| B09 | `technical_response.docx` | Response to a TQ or RFI | The formal written answer to a B07 or B08. Cross-referenced to the original query number automatically. |
| C10 | `material_requisition.docx` | Material requisition (MR) | A formal request to procure specific materials or equipment. Contains a list of items with quantities and specifications. |
| C11 | `submittal_cover.docx` | Submittal cover sheet | When a contractor submits product data, shop drawings, or samples for approval, this form wraps the submission. |
| C12 | `variation.docx` | Variation / change order | Documents a change to the agreed scope of work. Required by most contracts when design changes affect cost or programme. |
| C13 | `letter_transmittal.docx` | Formal letter of transmittal | A more formal version of B06 — used for stage-gate handovers or sending to the client/authority. On headed paper with a full signature block. |
| D14 | `meeting_minutes.docx` | Meeting minutes | Records attendees, agenda items, discussion, and action items from a coordination meeting. |
| D15 | `progress_report.docx` | Progress report | A periodic summary of where the project stands — deliverables issued, issues open, SLAs outstanding, compliance score. |
| D16 | `handover_certificate.docx` | Handover certificate | The formal sign-off document at project completion. Has a three-party signature block (client, lead designer, contractor). |

---

## Part 2 — The Four Drawers (CDE States)

### WIP — Work In Progress (the Yellow Drawer)

WIP is the default state for any new document. Think of it as your desk — work in progress,
visible only to you and your immediate team.

**What WIP means in practice:**
- The document may be incomplete, inconsistent, or just plain wrong. That is fine — it is
  a draft.
- Nobody outside your discipline should be working from a WIP drawing. If a structural
  engineer is co-ordinating against your M&E layout and your M&E layout is still WIP,
  they are coordinating against a draft that may change tomorrow.
- The suitability code is usually S0 (Initial).
- The revision is usually P01 (first preliminary), P02, P03 as you iterate.

**Who can see WIP?**
Only members of the owning discipline team, plus project managers and administrators.
A client, an external reviewer, or a subcontractor from another discipline should not
see your WIP.

**How long should something stay in WIP?**
As short as possible. WIP items that sit for more than 30 days without moving are flagged
as "stale" in the CDE Status screen — a signal that something is stuck.

---

### Shared — Ready for Review (the Blue Drawer)

Shared means the document is in the blue drawer — the whole project team can read it.
It is not yet authorised for construction, but it is in good enough shape that other
disciplines can coordinate their work against it.

**What Shared means in practice:**
- The M&E engineer can now coordinate their ductwork against the structural drawings
  because the structural drawings have moved to Shared.
- Reviewers can raise comments and RFIs against Shared documents.
- The revision typically bumps up when you move from WIP to Shared (e.g. P02 → P03)
  to signal that a new milestone has been reached.
- The suitability code moves to S1 (suitable for coordination), S2 (suitable for information),
  or S3 (suitable for review and comment).

**Who can move a document from WIP to Shared?**
You need at least Coordinator role. A drafter or technician in Contributor role cannot
do this — they need to ask a coordinator to promote the document.

**Who can see Shared?**
The whole project team, including subcontractors for all disciplines. Typically not
the client unless they have been given access.

---

### Published — Officially Released (the Green Drawer)

Published is the green drawer — the authoritative version used for construction, fabrication,
or handover. Think of it as the drawing stamped "FOR CONSTRUCTION" hanging on the site hut wall.

**What Published means in practice:**
- These are the drawings that builders, fabricators, and installers work from.
- Moving a document to Published requires a Manager role AND a formal approval record
  (someone with sign-off authority has clicked Approve).
- The suitability code is S4 (suitable for construction), S5 (suitable for manufacture),
  or S6 (suitable for PIM/AIM authorisation).
- The revision typically changes format from preliminary (P-series) to construction (C01, C02).

**The publishing gate:**
This is the most important safeguard in the system. A coordinator can issue a drawing for
team sharing in about 30 seconds. But getting that drawing to Published — the state where
someone will build from it — requires a manager to formally approve it. This mirrors the
real-world process where a senior engineer or director stamps a drawing for issue.

If you try to move a document to Published without an approval in place, the system will
say "Publish failed — approval required." This is not a bug. It is the system doing its job.

> **Stuck?** If you see "Publish failed — approval required" and you know the document
> has been reviewed, it means the approval record has not been created yet. Click
> "Request Approval" on the document, write a brief note to the manager explaining why
> it is ready, and they will receive a notification on their phone or email.

---

### Archive — Locked Away (the Grey Drawer)

Archive is the grey drawer — superseded revisions and end-of-project handover material.
Files in Archive are read-only. They are there so you can answer "what did the drawing
look like before the change?" — but nobody is building from them.

**What Archive means in practice:**
- When you issue revision C02 of a drawing, revision C01 moves to Archive automatically.
- At project handover, all superseded documents are moved to Archive as part of the close-out.
- Archive documents can still be downloaded and read. They just cannot be edited, promoted,
  or used as the "current" version.

**Why not just delete old versions?**
Because the Building Regulations, most construction contracts, and ISO 19650 require you
to be able to demonstrate what the design said at any given point in time. If a dispute
arises years after handover — "the wall was built 50mm out of position" — you need to
show which drawing was current on the day the wall was poured. Archive is how you do that.

---

### Moving between states: the rules

The four drawers have a strict movement sequence. You cannot skip steps.

```
WIP ──────────────────▶ SHARED ──────────────────▶ PUBLISHED ──────▶ ARCHIVE
 ▲                         │
 │   (rework)              │
 └─────────────────────────┘

Special cases:
PUBLISHED ──────────────────▶ SUPERSEDED ──────▶ ARCHIVE
                                    │
                                    └──▶ replaced by a new document ──▶ PUBLISHED
```

**The full transition table:**

| Move | What triggers it | Minimum role | Needs approval? |
|---|---|---|---|
| WIP → Shared | You are ready for the team to review | Coordinator | No |
| Shared → WIP | Document needs more work (rework) | Coordinator | No |
| Shared → Published | Ready for construction/handover | Manager | Yes — formal approval required |
| Published → Archive | Superseded by a newer version | Manager | No |
| Published → Superseded | Formally superseded (comes with an A03 notice) | Manager | No |
| Superseded → Archive | After the replacing document is published | Automatic | — |

**What happens when a document is superseded and replaced:**

1. You create a new document (the replacement).
2. You run SupersedeDeliverable on the old document — STING generates an A03 notice.
3. STING also generates an A04 notice on the new document showing what it replaces.
4. When the new document is Published, the old document moves to Archive.
5. The A03 and A04 notices are stored alongside the documents in the generated folder.

---

## Part 3 — Daily Workflows

### Issuing a deliverable (step by step)

A deliverable is any document you formally issue — a drawing, a report, a specification.
Here is the complete process from start to finish.

**Before you start:**
- Your drawing or document must be in a state you are happy to issue. For most Revit
  drawings, this means sheets are complete, notes are checked, revision triangle is placed.
- Project Information in Revit must have the project number and organisation code filled in.

**Step 1: Open the Document Management Center**

In the STING dock panel, click the **BIM** tab. Then click the **Document Management Center**
button (it shows a filing-cabinet icon). A large dialog with 8 tabs along the top will appear.

Alternatively, in the STING dock panel's BIM tab, look for the **Deliverables** section and
click **Issue Deliverable** directly if you just want to issue a single document quickly.

**Step 2: Fill in the issue form**

A dialog will appear asking for:
- **Document number**: STING will suggest the next number automatically from `doc_sequences.json`.
  You can override it if needed, but use the suggested number unless you have a specific reason
  not to.
- **Title**: A plain-English description of the document (e.g. "Ground Floor MEP Services Plan").
- **Discipline**: Pick from the dropdown — A (Architectural), S (Structural), M (Mechanical),
  E (Electrical), P (Plumbing), FP (Fire Protection), LV (Low Voltage), G (General).
- **Suitability**: Pick the code that describes what this issue is for. S1 for coordination,
  S3 for review and comment, S4 for construction. If unsure, ask your project manager.
- **Revision**: The system will suggest the next revision letter (P01, P02, etc.). Confirm
  or override.
- **Issue purpose**: A short note about why this is being issued.
- **Recipient / distribution group**: Who should receive this (see Part 7 for distribution groups).

**Step 3: Click "Issue"**

When you click Issue, STING does the following automatically — you will see a progress bar:

1. Mints the document number (increments the counter in `doc_sequences.json`).
2. Renders the A01 deliverable cover sheet from `templates/deliverable_standard.docx`,
   filling in all the token fields.
3. Saves the rendered `.docx` file into the `generated/` folder with a date-stamped filename.
4. Writes a new row (or updates the existing row) in `deliverables.json`.
5. Starts the `deliverable_issue_default` workflow — which begins timing the SLA for
   the recipient to acknowledge receipt.
6. Writes a row to the audit log.
7. Mirrors everything to the cloud server (if you are connected).

**Step 4: Check the output**

After STING finishes, it shows you a summary: the document number, the path to the rendered
DOCX, and whether the server sync succeeded.

Open the `generated/` folder to find your rendered cover sheet. It is a normal Word document
that you can print, email, or attach to a transmittal.

**What CDE state is the document in now?**

After Issue, the document is in **WIP**. Issuing creates the document and assigns it a number,
but it does not yet move it to Shared or Published. Those are separate steps:

- To move to Shared: click **Publish to Shared** (or the appropriate button in the
  Document Management Center). This requires Coordinator role.
- To move to Published: the document must first be in Shared, an approval must be created
  and approved by a Manager, and then you click **Publish for Construction**.

---

### Re-issuing (when something changes)

If a document has already been issued and something changes — a design update, a correction,
or a client comment addressed — you re-issue it with a new revision number.

**Step 1: Find the document in the deliverable register.**
In the Document Management Center, click the **DOCS/CDE** tab. You will see a list of
all documents. Find the one you want to re-issue.

**Step 2: Click Re-Issue Deliverable.**
This opens the same form as Issue, but pre-filled with the existing details. The revision
number is already incremented (e.g. P01 → P02).

**Step 3: Review and confirm.**
Check that the issue purpose explains what changed (e.g. "Coordination revision — duct route
amended to avoid structural beam at G.03").

**Step 4: Click Re-Issue.**
STING repeats the same steps as Issue — renders a new A01 cover sheet with the new revision
number, saves it, updates `deliverables.json`, continues the workflow (or starts a new one),
and writes the audit row.

The old revision is automatically moved to Archive. The new revision becomes the current one.

---

### Creating a transmittal (step by step)

A transmittal is a formal cover letter that lists the documents you are sending and why.
In paper days, every package of drawings came with a transmittal slip listing what was in
the envelope. STING generates these automatically.

**Scenario**: You have produced five drawings and need to send them to the structural engineer
for coordination review.

**Step 1: Gather the drawings.**

Make sure all five drawings have been issued in STING (they should have document numbers).
If they have not been issued yet, do that first (see "Issuing a deliverable" above).

**Step 2: Open Create Transmittal.**

In the STING dock panel BIM tab, click **Create Transmittal (Orchestrated)**. This is the
full version — it generates the document, starts the workflow, and records everything in one
step.

> **Note**: There is also a simpler **Create Transmittal** button. That is the quick version
> for informal internal transmittals. For anything going to a consultant or client, use the
> Orchestrated version.

**Step 3: Fill in the transmittal form.**

The dialog asks for:
- **Transmittal number**: Auto-generated from `doc_sequences.json`. Do not change unless
  you have a specific reason.
- **Date**: Today's date, pre-filled.
- **Recipient name and organisation**: The structural engineer's name and company.
- **Recipient email**: Used if the system is configured to send notifications.
- **Purpose**: A short description — e.g. "Issued for coordination — MEP services coordination
  drawings, first issue."
- **Document list**: A table where you add each drawing. Click "Add Document" for each one,
  pick it from the deliverable register, and its number, title, revision, and suitability
  will auto-fill.
- **Distribution group**: If you have a "Structural Team" group set up (see Part 7), pick it
  here and the recipient list auto-fills.

**Step 4: Click "Create Transmittal."**

STING does the following:
1. Mints the transmittal number.
2. Renders the B06 transmittal memo from the template, filling in all details including the
   document table.
3. Saves the rendered `.docx` to the `generated/` folder.
4. Adds a row to `transmittals.json`.
5. Starts the `transmittal_default` workflow — SLA timer begins (24 hours for you to issue
   it, then 72 hours for the recipient to acknowledge).
6. Writes the audit row.
7. Syncs to the server.

**Step 5: Send the transmittal.**

STING generates the document but does not send emails itself (STING is a Revit plugin, not
an email client). Open the generated `.docx` file from the `generated/` folder, save it
as a PDF if needed, and attach it along with the drawings to your normal email.

> **Stuck?** If you cannot find the generated file, check the `generated/` folder inside
> `_BIM_COORD/`. The filename starts with today's date (YYYYMMDD) followed by the transmittal
> number and "transmittal".

**Step 6: Track acknowledgement.**

The workflow is now waiting for the recipient to acknowledge receipt. In a fully configured
system with the Planscape server and mobile app, the recipient gets a notification and can
acknowledge from their phone. If you are using STING without the server, you track this
manually — when the recipient confirms receipt, open the BIM Coordination Center and close
the workflow step.

---

### Raising an RFI

An RFI (Request for Information) is a formal question from site or from a consultant that
needs a formal answer. The question and its answer are both recorded.

**Step 1: Click "Raise Issue" or navigate to the ISSUES tab** in the Document Management Center.

**Step 2: Fill in the RFI form:**
- Issue type: RFI
- Subject: A clear one-line description of the question.
- Body: The full question in enough detail that the person answering has everything they need.
- Discipline: Which discipline the question is about.
- Priority: Low / Medium / High / Critical. Be honest — "Critical" should mean "work is stopped."
- Assignee: Who should answer it.

**Step 3: Click "Raise."**

STING renders an B08 RFI document from the template, assigns a number, starts the
`rfi_default` workflow (SLA: 48 hours for first response), and records everything.

**Step 4: Receive and record the response.**

When the answer comes back, click **Create Technical Response** to generate a B09 document
cross-referencing the original RFI number. The workflow moves to Responded state.

**Step 5: Close the RFI.**

Once the response is accepted and any drawing changes are made, click to close the RFI.
The workflow reaches Closed state and the SLA timer stops.

---

### Recording meeting minutes

**Step 1: Before the meeting**, click **Create Meeting** in the Document Management Center's
COORDINATION tab. Give it a name, date, and location.

**Step 2: During the meeting**, add agenda items and discussion notes in the meeting record.
STING is not a live meeting tool — you can take notes in Word or paper and enter them after.

**Step 3: After the meeting**, open the meeting record, fill in:
- Attendees (who was there)
- Agenda items and discussion
- Action items — each action has an owner and a due date

**Step 4: Generate the minutes.**

Click **Generate Minutes**. STING renders a D14 meeting minutes document from the template,
including all the above.

**Step 5: Distribute.**

Create a transmittal (B06) wrapping the minutes and send via email.

---

### Running a progress report

At the end of each reporting period (weekly or monthly), a progress report summarises where
the project stands.

In the Document Management Center, click **Progress Report** in the HANDOVER tab (or from
the BIM tab of the dock panel). STING renders a D15 progress report from the template,
automatically pulling:
- Number of deliverables in each CDE state
- Open issues count and overdue count
- SLA breaches this period
- Compliance score from the tag system
- Upcoming milestones

Review the rendered document, save as PDF, attach to a B06 transmittal, and distribute.

---

## Part 4 — The Document Management Center (every button)

The Document Management Center (DMC) is the main window for all document management in STING.
Open it from the BIM tab of the dock panel by clicking **Document Management Center**.

It opens as a large dialog with **8 tabs** along the top. Here is every button on every tab.

---

### Tab 1 — FILE/BULK

This tab handles batch operations — doing the same thing to many documents at once.

| Button | What it does | When to use it |
|---|---|---|
| **Issue Deliverable** | Creates and issues a single deliverable. Opens a form to fill in document details. | Every time you formally issue one document. |
| **Re-Issue Deliverable** | Re-issues an existing deliverable with a new revision. Pre-fills details from the existing record. | When a document needs a revision issued. |
| **Publish Deliverable** | Moves a Shared document to Published. Requires Manager role and an existing approval record. | When a document is ready for construction issue. |
| **Cancel Deliverable** | Formally cancels a document. Generates an A02 cancellation notice. The document moves to Archive. | When a document is no longer needed — for example, a room was removed from the project. |
| **Supersede Deliverable** | Formally supersedes a document. Generates an A03 notice. Use when issuing a replacement. | When you are replacing a document with a new version and want a formal record of the supersession. |
| **Replace Deliverable** | Marks the new document as replacing the old one. Generates an A04 notice cross-referencing the superseded document. | Use alongside Supersede — this goes on the new document. |
| **Bulk Issue Deliverables** | Issues all selected deliverables simultaneously. All succeed or all fail together (atomic). | At a stage gate when you are issuing a large package of drawings at once. |
| **Import Documents** | Manually registers documents that exist outside STING (e.g. external consultant PDFs) in the document register. | When you receive drawings from another party and want to track them in the same register. |
| **Export Register** | Exports the complete document register to a CSV spreadsheet. | For reporting, for sharing with parties who do not have access to STING. |

---

### Tab 2 — DOCS/CDE

This is the main browser for all documents. Think of it as the view into all four drawers.

| Control / Button | What it does |
|---|---|
| **Drawer chips (WIP / SHARED / PUBLISHED / ARCHIVE)** | Click a chip to filter the document list to that drawer only. A chip appears greyed out if you do not have access to that drawer. |
| **Discipline filter** | Dropdown to filter by discipline (A, S, M, E, P, etc.). |
| **Suitability filter** | Dropdown to filter by suitability code (S0 through S7). |
| **Search bar** | Free-text search across document names, titles, and authors. |
| **Document list** | Shows all documents matching the current filters. Click a row to see its detail in the right panel. |
| **Detail panel (right)** | Shows all fields for the selected document: number, title, discipline, revision, suitability, CDE state, who last changed it, and when. |
| **Version history** | In the detail panel, shows every past revision of the selected document. Click a row to download that specific version. |
| **Transition buttons** | In the detail panel: WIP→Shared, Shared→WIP (rework), Shared→Published, Published→Archive. Only the valid next transitions for the selected document and your role are enabled. |
| **Request Approval** | Creates a formal approval request directed at a Manager. Appears when you try to move to Published without an existing approval. |
| **Download** | Downloads the current version of the selected document. |
| **Add Version** | Uploads a new version of an existing document. |
| **CDE Status** | Opens the CDE health-check screen: how many documents are in each drawer, how long they have been there, and any stale warnings. |
| **Validate Naming** | Checks whether a filename matches the ISO 19650 naming convention before you upload it. Shows which segments are wrong and what they should be. |

---

### Tab 3 — ISSUES

Issues are formal tracked problems — RFIs from site, coordination clashes, design queries.

| Button | What it does |
|---|---|
| **Raise Issue** | Opens the issue creation form. Fill in type (RFI, NCR, SI, Coordination), subject, body, discipline, priority, and assignee. |
| **Issue Dashboard** | A summary view: total issues, open by priority, overdue by assignee. Shows which disciplines are generating the most issues. |
| **Update Issue** | Edits the status, comment, or assignee on an existing issue. |
| **Select Issue Elements** | Selects the Revit elements linked to the selected issue directly in the model. |
| **BCF Export** | Exports all issues as a BCF 2.1 zip file — the standard format for sharing issues with consultants using other software (Navisworks, Solibri). |
| **BCF Import** | Imports issues from a BCF zip received from another party. |
| **Close Issue** | Formally closes an issue. Records the resolution. |
| **Issue filter chips** | Filter by status (Open, In Progress, Resolved, Closed), by discipline, by priority, by assignee. |
| **Link to Document** | Links the selected issue to a document in the document register — for example, an RFI that led to a drawing change. |
| **Issue list** | All issues matching the current filter. Click to see detail. |

---

### Tab 4 — REVISIONS

This tab manages drawing revisions across the project.

| Button | What it does |
|---|---|
| **Create Revision** | Creates a new revision entry in Revit (the Revit Revisions table). |
| **Revision Dashboard** | Summary of all revisions: which drawings are at which revision, how many revisions have been issued. |
| **Auto Revision Cloud** | Automatically creates revision clouds on drawing sheets based on elements that have changed since the last issue. |
| **Revision Schedule** | Creates a Revit schedule showing all elements and their current revision. |
| **Track Element Revisions** | Stamps a parameter on each element recording which revision it was current at. |
| **Revision Compare** | Shows a side-by-side comparison of two revisions of the same document — what changed. |
| **Issue Sheets for Revision** | Issues all sheets associated with the selected revision as a batch. |
| **Revision Naming Enforce** | Checks that revision naming conventions (P01, P02, C01, etc.) are being followed consistently. |
| **Revision Tag Integration** | Links revision triangles on drawings to STING's revision tracking parameters. |
| **Export Revisions** | Exports the full revision history to a spreadsheet. |
| **Bulk Revision Stamp** | Stamps a revision code on multiple selected elements at once. |
| **Auto Revision on Tag Change** | Marks elements for revision automatically when their STING tag parameters change. |

---

### Tab 5 — COORDINATION

Coordination is the process of checking that all disciplines' designs work together without
clashing.

| Button | What it does |
|---|---|
| **Review Tracker** | Opens the coordinated-review register. Shows which documents are under review, by whom, and whether they are overdue. |
| **MIDP Tracker** | Opens the Master Information Delivery Plan view — the list of every planned deliverable for the project with its current status and target date. |
| **Clash Detection** | Runs a basic in-Revit clash check between disciplines and reports clashes as issues. |
| **Platform Sync** | Synchronises with an external BIM platform (Autodesk Construction Cloud, Procore, etc.) — pushes issues and document records outward. |
| **BCF Export / Import** | As in the Issues tab — issue interchange in BCF format. |
| **BIM Coordination Center** | Opens the full BIM Coordination Center dialog (the 13-tab version) for deeper coordination work. |
| **LAN Collaboration** | Enables local-network collaboration for teams working without internet access. |
| **Workset Audit** | Checks worksets are set up correctly for collaborative working in a workshared model. |
| **Link Manager** | Manages Revit link files (linked models from other disciplines). |
| **Create Meeting** | Creates a meeting record and ultimately generates D14 minutes. |
| **Meeting list** | Lists all meetings recorded for the project. Click to open a meeting record. |

---

### Tab 6 — HANDOVER

Handover is what happens at project completion when you hand over all the information to
the client and FM team.

| Button | What it does |
|---|---|
| **Document Briefcase** | Generates the 8-file portable handover package (project info, tag register, compliance report, parameter audit, model statistics, sheet index, discipline breakdown, MIDP register). Saves to a timestamped folder. |
| **COBie Export** | Exports a full COBie V2.4 workbook (19 worksheets) for the FM team. This is the structured data handover that feeds asset management systems. |
| **COBie Import** | Imports back a COBie workbook to update model parameters from FM team corrections. |
| **FM Handover Manual** | Generates a readable operations and maintenance manual referencing all the project's documents. |
| **O&M Manual** | Similar to FM Handover Manual — a more technically detailed operations manual. |
| **Asset Health Report** | A snapshot of all assets' current condition and maintenance status. |
| **Space Handover Report** | A room-by-room report of what is being handed over, suitable for signing by all parties. |
| **Export Maintenance Schedule** | Exports the SFG20 / BS 8210 maintenance schedule for the FM team. |
| **COBie Type Map** | Opens the browser for the 70+ equipment category mappings — use this to fix any unmapped equipment before running COBie Export. |
| **COBie Picklists** | Shows and lets you edit the controlled vocabularies used in the COBie workbook. |

---

### Tab 7 — NOTES/BEP

Notes covers sticky notes (element-level coordination markup) and the Briefcase viewer.
BEP covers the BIM Execution Plan.

| Button | What it does |
|---|---|
| **Add Note** | Adds a timestamped sticky note to the selected Revit element(s). |
| **View Notes** | Shows all sticky notes on the selected element(s) in a list. |
| **Clear Notes** | Removes sticky notes from the selected element(s). |
| **Bulk Delete Notes** | Removes all sticky notes from the entire project. Use with care — this is not undoable. |
| **Export Notes** | Exports all sticky notes to a CSV, with element ID, category, tag, author, date, and note text. |
| **Select Noted Elements** | Selects all elements in the project that have a sticky note. |
| **Note Dashboard** | Shows aggregate sticky-note counts by author, discipline, and category. |
| **Note Search** | Free-text search across all sticky notes. |
| **Note Categories** | Per-category note counts. |
| **Briefcase Viewer** | Opens the in-Revit document browser — read BEP, transmittals, and reference documents without leaving Revit. |
| **Add to Briefcase** | Adds a PDF, DOCX, or XLSX to the project's reference set (the `_BIM_COORD/reference/` folder). |
| **Read Briefcase Item** | Opens a specific item in the briefcase viewer. |
| **Create BEP** | Launches the BEP (BIM Execution Plan) wizard — generates a project BEP from a template. |
| **Update BEP** | Opens the BEP for editing and update. |
| **Export BEP** | Exports the BEP as a document. |
| **Generate BEP** | Generates a full BEP document from the BEP data. |

---

### Tab 8 — BIM Execution Plan (BEP)

The BIM tab also includes BIM management commands covered in depth in `BIM_MANAGEMENT_GUIDE.md`.
The key document-management buttons on this tab:

| Button | What it does |
|---|---|
| **Full Compliance Dashboard** | A complete project-wide health summary: tag compliance, document completeness, issue counts, workflow status. |
| **Model Health Dashboard** | Focused on model quality: warning counts, unused views, parameter completeness. |
| **ISO 19650 Reference** | Opens the in-Revit quick reference for ISO 19650 codes and conventions. |
| **Bulk BIM Export** | Exports multiple formats at once: IFC, COBie, BCF, and PDF drawings as a single batch. |
| **Stage Compliance Gate** | Checks whether all required deliverables for the current RIBA stage are in the correct CDE state. Use at every stage gate. |

---

### The standalone document commands (outside the DMC)

These buttons appear directly on the BIM tab of the STING dock panel and do not require
opening the Document Management Center:

| Button | What it does |
|---|---|
| **Issue Deliverable** | Quick issue — same as the FILE/BULK tab button but one click faster. |
| **Re-Issue Deliverable** | Quick re-issue. |
| **Publish Deliverable** | Move to Published. Requires approval in place. |
| **Cancel Deliverable** | Formally cancel. Generates A02 notice. |
| **Supersede Deliverable** | Formally supersede. Generates A03 notice. |
| **Replace Deliverable** | Mark as replacing another document. Generates A04 notice. |
| **Create Transmittal (Orchestrated)** | Full pipeline — number, render, workflow, audit. |
| **Bulk Issue Deliverables** | Issue all selected deliverables together. |

---

## Part 5 — The Audit Trail

### Why every action is logged

Imagine a senior partner in a paper-based office. Every time a drawing leaves the office,
they write in a ledger: the date, what was sent, who it went to, and their initials.
That ledger is the audit trail.

STING keeps exactly this kind of ledger, but digitally. Every document action is recorded:
- When a document was issued
- Who issued it
- When it moved from WIP to Shared
- When an approval was granted (or denied) and by whom
- When it was published
- When it was superseded
- When a transmittal was created and who received it

This log is stored in `audit_log_YYYY_MM.jsonl` (one file per calendar month). The `.jsonl`
format means each line is a separate JSON record — readable in any text editor.

**Why does this matter?**

If a dispute arises about whether a drawing was issued before a certain date, the audit log
shows the exact timestamp and who clicked Issue. If a building inspector asks "when did you
change that wall specification?", the audit log shows the supersession event. If an insurance
claim hinges on whether a handover certificate was signed, the audit log shows who approved it.

This is not optional paperwork — it is the digital equivalent of the senior partner's ledger.

---

### Reading the audit log

Open `_BIM_COORD/audit_log_2026_05.jsonl` (or whichever month you need) in Notepad.
Each line looks something like this:

```json
{"timestamp":"2026-05-14T09:23:41Z","action":"deliverable.issue","user":"j.smith","documentNumber":"PRJ-XYZ-ZZ-ZZ-DR-M-0001","revision":"P01","previousState":null,"newState":"WIP","hash":"a3f5...","prevHash":"8c2d..."}
```

In plain English: On 14 May 2026 at 09:23, Jane Smith issued document PRJ-XYZ-ZZ-ZZ-DR-M-0001
at revision P01. The document moved from nothing (first issue) to WIP state.

**The fields:**
- `timestamp`: When the action happened (UTC time).
- `action`: What happened — examples: `deliverable.issue`, `deliverable.publish`,
  `workflow.transition`, `transmittal.create`, `approval.grant`.
- `user`: Who did it (their login name).
- `documentNumber`: Which document.
- `revision`: Which revision.
- `previousState` / `newState`: Which drawer the document moved from and to.
- `hash`: A unique fingerprint of this log entry.
- `prevHash`: The fingerprint of the entry before this one.

---

### What the SHA-256 tamper-evidence chain means

The `hash` and `prevHash` fields are the key to tamper-evidence. Here is how it works
in plain English:

Imagine each audit log entry is a link in a chain. Each link is stamped with a seal,
and that seal includes a picture of the seal on the previous link. If you break open
the middle of the chain and swap a link for a different one, the seal on that new link
will not match the seal on the next link — because the next link's seal includes a
picture of the original link's seal, not the replacement.

In digital terms: the `hash` of each entry is calculated from the content of that entry
PLUS the `hash` of the previous entry. If you change anything in any entry — even a
single character — the hash no longer matches, and every entry after it also fails to
verify. The chain is broken and detectable.

This means: if anyone edits the audit log to hide a document action, STING can detect it.

**How STING verifies the chain:**

STING includes a VerifyChain function that walks through every entry in the audit log and
checks that each hash is correct. If any entry fails verification, STING reports exactly
which entry is suspect.

For day-to-day use, you do not need to verify the chain manually. The server does this
automatically as part of its periodic audit. If you need to verify it yourself (for an
inspection or ISO 27001 audit), run `POST /api/admin/audit/verify` on the Planscape server
and it returns a pass/fail result with the first failed entry if any.

**Do not edit the audit log files manually.** Even correcting a typo will break the chain.
If an entry is wrong (for example, the wrong username was logged), the correct response is
to add a new entry recording the correction, not to edit the original.

---

## Part 6 — Workflows and SLAs

### What a workflow is

When you create a transmittal or issue an RFI, it is not just a document — it is a process.
Someone needs to receive it, acknowledge it, review it, and close it. Those steps need to
happen in a specific order, and some of them have time limits.

A workflow is a description of those steps. Think of it as a checklist with a clock on it:
"Step 1: Draft. Step 2: Issue within 24 hours. Step 3: Recipient acknowledges within 72 hours."

STING ships five built-in workflows that cover the most common processes. Each workflow is
defined in a JSON file in the `_BIM_COORD/workflows/` folder. When you create a transmittal,
STING starts an instance of the relevant workflow — it begins tracking which step you are on
and whether the time limits are being met.

---

### The 5 built-in workflows

**`transmittal_default`** — Standard transmittal

| Step | Name | What it means | Time limit |
|---|---|---|---|
| 1 | Draft | Transmittal created but not yet sent | 24 hours |
| 2 | Issued | Transmittal sent to recipient | — |
| 3 | Acknowledged | Recipient has confirmed receipt | 72 hours after issue |
| Done | Complete | Transmittal closed | — |

If you create a transmittal and do not send it within 24 hours, the SLA for the Draft step
is breached. STING will show this on the dashboard and send a notification.

If the recipient does not acknowledge within 72 hours of you issuing it, the Acknowledged
SLA is breached.

---

**`rfi_default`** — Request for Information

| Step | Name | Time limit |
|---|---|---|
| 1 | Open | 24 hours (to assign) |
| 2 | Reviewed | 48 hours (to review) |
| 3 | Responded | 168 hours / 7 days (to respond) |
| 4 | Closed | — |

---

**`tq_default`** — Technical Query

| Step | Name | Time limit |
|---|---|---|
| 1 | Open | — |
| 2 | Answered | 48 hours |
| Done | Closed | — |

---

**`mr_default`** — Material Requisition

| Step | Name | Time limit |
|---|---|---|
| 1 | Draft | — |
| 2 | Submitted | — |
| 3 | Approved or Rejected | Varies by project |
| Done | Complete | — |

---

**`deliverable_issue_default`** — Deliverable lifecycle

This workflow tracks the lifecycle of a formal deliverable through the four CDE drawers.
Its states mirror the CDE states:

| Step | Matches CDE State |
|---|---|
| WIP | WIP |
| Shared | SHARED |
| Published | PUBLISHED |
| Archived | ARCHIVE |

Unlike the other workflows, this one does not have fixed SLA times by default — the timing
is governed by the project programme.

---

### What happens when an SLA is breached

An SLA (Service Level Agreement) is a promise that a step will be completed within a certain
time. When that time passes without the step being completed, the SLA is breached.

When a breach occurs, STING does the following:

1. **On the BIM Coordination Center dashboard**: The overdue workflow step appears in red
   in the "Open Workflows" section. The document number, step name, and how long overdue
   it is are all shown.

2. **On the mobile app**: If you have the Planscape mobile app and are assigned to the
   workflow, you receive a push notification: "Transmittal T-0042 is overdue — Acknowledged
   step is 18 hours past deadline."

3. **In the audit log**: The SLA breach is recorded as an audit event.

4. **Via email**: If the Planscape server is configured with SMTP email, a reminder email
   is sent to the assignee.

SLA breaches do not automatically fail the workflow — the step can still be completed after
the deadline. The breach is a warning and a record, not a hard block. However, if your
contract specifies SLA compliance requirements, the audit log provides evidence of any breaches.

**What to do when an SLA is breached:**

If you see a breach notification, the correct response is:
1. Complete the overdue step as quickly as possible.
2. Add a comment explaining the delay.
3. If the delay was the other party's fault (e.g. the client did not respond), record that
   in the comment so the audit log reflects the actual cause.

---

## Part 7 — Distribution Groups

### What a distribution group is

A distribution group is a named list of people who should receive a particular category of
documents. Instead of typing the same list of email addresses every time you create a transmittal,
you create a group once and then pick it from a dropdown.

For example, you might create:
- **"Project Team"** — everyone on the project (lead designers, coordinator, PM)
- **"Structural Reviewers"** — the structural engineering team
- **"M&E Subcontractors"** — the mechanical and electrical subcontractors
- **"Client Distribution — Issued for Construction"** — client and quantity surveyor,
  for S4 drawings only

Distribution groups are stored in `_BIM_COORD/distribution_groups.json`.

---

### How STING matches recipients to documents

When you create a transmittal, STING looks at the document's properties (type, discipline,
suitability code) and scores all your distribution groups to find the best match. The group
with the highest score is suggested automatically, but you can override it.

The scoring works like this:

| Match | Score |
|---|---|
| Document type matches group's type tag | +3 |
| Document discipline matches group's discipline tag | +3 |
| Document suitability matches group's suitability tag | +2 |
| Group has a matching project code | +1 |

The group with the highest total score is suggested. If two groups tie, the one that was
used most recently is preferred.

**In plain English**: if you are issuing a Mechanical drawing at S4 (for construction),
and you have a group tagged with "Discipline: M, Suitability: S4", that group will be
suggested automatically. You just click Confirm rather than re-typing the recipient list.

---

### Setting up distribution groups

**Method 1 — via the Document Management Center (recommended):**

1. Open the Document Management Center (BIM tab → Document Management Center).
2. Click on the **NOTES/BEP** tab.
3. Scroll down to the **Distribution Groups** section.
4. Click **Add Group**.
5. Fill in:
   - **Name**: Something clear, like "Structural Team" or "Client — S4 Only"
   - **Members**: Click Add Member and type each person's name and email address.
   - **Tags**: What this group should receive. Click the dropdown and select the applicable
     types (Drawing), disciplines (M, E, P etc.), and suitability codes (S3, S4, etc.).
     Leave a tag blank to mean "all values" — so a group tagged only with S4 will receive
     all disciplines' S4 documents.
6. Click **Save Group**.

**Method 2 — by editing `distribution_groups.json` directly:**

For advanced users, open `_BIM_COORD/distribution_groups.json` in a text editor.
The file is an array of group objects. Each object looks like this:

```json
{
  "name": "Structural Team",
  "members": [
    { "name": "Alice Brown", "email": "alice@structuralengineers.com" },
    { "name": "Bob Green", "email": "bob@structuralengineers.com" }
  ],
  "tags": {
    "type": "Drawing",
    "discipline": "S",
    "suitability": ["S3", "S4"]
  }
}
```

Save the file. STING picks up changes the next time you create a transmittal.

> **Stuck?** If you save `distribution_groups.json` and STING does not seem to use the
> new group, check that the file is valid JSON (no missing commas or quotes). Open it in
> a web browser — Chrome will show a red error if the JSON is malformed. Fix any errors
> and try again.

---

### Tips for distribution group management

1. **Create groups at the start of the project** — not when you first need them. During
   the first project kick-off meeting, while you have all the consultant contact details
   in front of you, set up the groups. Saves time every single transmittal for the rest
   of the project.

2. **Keep groups specific** — a group tagged "all disciplines, all suitabilities" will
   always be suggested, even when it is not appropriate. Specific tags give better
   auto-suggestions.

3. **Update groups when consultants change** — if the structural engineer is replaced
   mid-project, update the group. All future transmittals will go to the right person;
   past ones remain in the audit log going to the old contact.

4. **Use the suitability tag carefully** — a client should typically only be in groups
   tagged S4 or above. They should not receive S1 or S2 coordination drawings that are
   not yet fit for client review.

---

## Part 8 — Troubleshooting

The following table covers the most common problems and how to fix them.

| Problem | What caused it | How to fix it |
|---|---|---|
| **`_BIM_COORD` folder does not exist** | STING did not run setup on this project. | Close the project, wait 5 seconds, re-open it. If it still does not appear, check `StingTools.log` for errors. |
| **Template files missing from `templates/`** | Setup ran partially and failed during file extraction. | Delete the entire `_BIM_COORD` folder and re-open the project. STING will re-extract everything. |
| **`manifest.json` contains blank project name** | Project Information in Revit was empty when setup ran. | Fill in Manage → Project Information in Revit, then delete `_BIM_COORD/templates/manifest.json` and re-open the project. |
| **Rendered document shows `{{token}}` instead of real data** | A template was edited and a token was accidentally deleted. | Open the template (e.g. `transmittal.docx`) in Word. The missing token should be typed back in exactly as it appeared — curly braces included. |
| **"Publish failed — approval required"** | The document is in Shared but no Manager has approved the SHARED→PUBLISHED transition. | Click "Request Approval" on the document, write a note to the manager, and wait for them to approve. |
| **Transmittal rendered but not in `generated/`** | Output folder path changed or a permissions error. | Check `StingTools.log` for the actual path used. Try running Create Transmittal again — the second attempt usually succeeds. |
| **"workflow_state.json deserialization failed"** | The workflow state file was corrupted (manual edit, crash during write, or schema version mismatch). | Delete `_BIM_COORD/workflow_state.json`. STING creates a fresh one. In-flight workflows are lost. |
| **Document appears in wrong CDE state** | A transition was made by someone with wrong role permissions, or the server sync failed in an inconsistent state. | Check the audit log for the last transition event. If wrong, use the CDE transition buttons to move the document to the correct state — this creates a new audit entry. |
| **Audit chain broken** | Someone edited an `audit_log_*.jsonl` file manually, or a crash corrupted the last entry. | The chain is broken but the data is not lost. Do not edit the file further. Run audit chain verify on the server. The first bad entry will be identified. For regulatory audits, the break itself must be disclosed. |
| **Deliverables not appearing on server** | The sync failed. | Click the Sync button on the STING dock panel. This forces a reconcile pass. Check `StingTools.log` for "DeliverableServerSync failed" if it keeps failing. |
| **Distribution group not suggested** | The group's tags do not match the document being transmitted. | Review the group's tags in the Document Management Center. Make sure discipline and suitability tags match. |
| **COBie export missing equipment** | Equipment categories are not mapped in `COBIE_TYPE_MAP.csv`. | Open COBie Type Map in the HANDOVER tab. Find the missing category and add a mapping. |
| **"Search returns no results"** | The search index is empty or corrupted. | Delete the `_BIM_COORD/search_index/` folder. STING rebuilds the index automatically on next search. |
| **"Document number skipped — gap in sequence"** | A document was partially created and then cancelled before the number was written to `deliverables.json`, or the counter was manually edited. | The gap in numbering is not a functional problem. Do not try to reuse the skipped number — it may have been reserved. Continue from the current counter. |
| **Cannot move to Published — user does not have Manager role** | The person trying to publish has Coordinator role but not Manager. | Ask the project manager to either promote the user's role or run the transition themselves. |
| **RFI shows as overdue but recipient says they responded** | The workflow step was not closed in STING when the response was received. | Open the RFI in the Issues tab, find the overdue step, and mark it as complete. Add a comment noting the actual response date. |
| **Mobile app shows documents user should not see** | Old app version without ACL filters, or the member record has not been updated. | Force-quit and re-open the app. Sign out and sign back in. The app re-fetches the ACL on login. |
| **Briefcase viewer shows "no items"** | The underlying files in `_BIM_COORD/` have not been generated yet. | Run BEP wizard to create `bim_execution_plan.json`. Generate the dashboard from the BCC Overview tab. The viewer shows what exists — generate the items first. |

---

## Quick Reference

### All document commands (complete table)

| Command tag | What it does | Where to find it |
|---|---|---|
| `IssueDeliverable` | Issue a single deliverable (A01 cover sheet) | BIM tab, FILE/BULK tab |
| `ReIssueDeliverable` | Re-issue with new revision | BIM tab, FILE/BULK tab |
| `PublishDeliverable` | Move to Published (requires approval) | BIM tab, FILE/BULK tab |
| `CancelDeliverable` | Formally cancel (A02 notice) | BIM tab, FILE/BULK tab |
| `SupersedeDeliverable` | Formally supersede (A03 notice) | BIM tab, FILE/BULK tab |
| `ReplaceDeliverable` | Mark as replacing another (A04 notice) | BIM tab, FILE/BULK tab |
| `BulkIssueDeliverables` | Issue all selected deliverables together | BIM tab, FILE/BULK tab |
| `CreateTransmittalOrchestrated` | Full transmittal pipeline | BIM tab |
| `CreateTransmittal` | Quick transmittal (informal) | BIM tab |
| `DocumentRegister` | Open document register | BIM tab, DOCS/CDE tab |
| `AddDocument` | Manual register entry | DOCS/CDE tab |
| `CDEStatus` | CDE drawer health check | DOCS/CDE tab |
| `ValidateDocNaming` | ISO 19650 naming check | DOCS/CDE tab |
| `RaiseIssue` | Create an issue (RFI, NCR, SI) | ISSUES tab |
| `IssueDashboard` | Issue summary dashboard | ISSUES tab |
| `UpdateIssue` | Edit an issue | ISSUES tab |
| `SelectIssueElements` | Select Revit elements linked to issue | ISSUES tab |
| `BCFExport` | Export issues as BCF file | ISSUES tab |
| `BCFImport` | Import BCF issues | ISSUES tab |
| `CreateRevision` | Create Revit revision entry | REVISIONS tab |
| `RevisionDashboard` | Revision summary | REVISIONS tab |
| `AutoRevisionCloud` | Auto-generate revision clouds | REVISIONS tab |
| `RevisionCompare` | Compare two revisions | REVISIONS tab |
| `ReviewTracker` | Open review register | COORDINATION tab |
| `MidpTracker` | Open MIDP delivery plan | COORDINATION tab |
| `ClashDetection` | Run in-Revit clash check | COORDINATION tab |
| `DocumentBriefcase` | Generate 8-file handover package | HANDOVER tab |
| `COBieExport` | Export COBie V2.4 workbook | HANDOVER tab |
| `COBieImport` | Import COBie workbook | HANDOVER tab |
| `HandoverManual` | Generate FM handover manual | HANDOVER tab |
| `ElementStickyNote` | Add/view/clear sticky note | NOTES/BEP tab |
| `ExportStickyNotes` | Export all notes to CSV | NOTES/BEP tab |
| `SelectStickyElements` | Select all noted elements | NOTES/BEP tab |
| `StickyNoteDashboard` | Sticky note aggregate view | NOTES/BEP tab |
| `StickyNoteSearch` | Search all notes | NOTES/BEP tab |
| `BriefcaseView` | Open in-Revit document viewer | NOTES/BEP tab |
| `BriefcaseAddFile` | Add file to briefcase | NOTES/BEP tab |
| `CreateBEP` | Launch BEP wizard | NOTES/BEP tab |
| `FullComplianceDashboard` | Project-wide health summary | BIM tab |
| `ModelHealthDashboard` | Model quality dashboard | BIM tab |
| `StageComplianceGate` | Stage gate deliverable check | BIM tab |
| `BulkBIMExport` | Export IFC, COBie, BCF, PDF together | BIM tab |

---

### CDE state transitions table

| From | To | Trigger | Role needed | Approval? |
|---|---|---|---|---|
| (new) | WIP | Issue Deliverable | Contributor | No |
| WIP | SHARED | Publish to Shared | Coordinator | No |
| SHARED | WIP | Rework | Coordinator | No |
| SHARED | PUBLISHED | Publish for Construction | Manager | Yes |
| PUBLISHED | ARCHIVE | Document superseded | Manager | No |
| PUBLISHED | SUPERSEDED | Supersede Deliverable | Manager | No |
| SUPERSEDED | ARCHIVE | Automatic after replacement published | System | — |

---

### Template ID reference table

| ID | Template file | Produces | Used by command |
|---|---|---|---|
| A01 | `deliverable_standard.docx` | Deliverable cover sheet | `IssueDeliverable`, `ReIssueDeliverable` |
| A02 | `deliverable_cancelled.docx` | Cancellation notice | `CancelDeliverable` |
| A03 | `deliverable_superseded.docx` | Superseded notice | `SupersedeDeliverable` |
| A04 | `deliverable_replacing.docx` | Replacing notice | `ReplaceDeliverable` |
| A05 | `deliverable_tabular.xlsx` | Tabular deliverable list | `BulkIssueDeliverables` |
| B06 | `transmittal.docx` | Transmittal memo | `CreateTransmittalOrchestrated` |
| B07 | `technical_query.docx` | Technical query | Raise Issue (TQ type) |
| B08 | `rfi.docx` | Request for Information | `RaiseIssue` (RFI type) |
| B09 | `technical_response.docx` | Response to TQ or RFI | Close Issue with response |
| C10 | `material_requisition.docx` | Material requisition | Raise Issue (MR type) |
| C11 | `submittal_cover.docx` | Submittal cover sheet | `IssueDeliverable` (submittal) |
| C12 | `variation.docx` | Variation / change order | Issue change order |
| C13 | `letter_transmittal.docx` | Formal letter of transmittal | `CreateTransmittalOrchestrated` (formal) |
| D14 | `meeting_minutes.docx` | Meeting minutes | Create Meeting → Generate Minutes |
| D15 | `progress_report.docx` | Progress report | Progress Report command |
| D16 | `handover_certificate.docx` | Handover certificate | `HandoverManual` / end-of-project |

---

### Suitability code reference

| Code | Full name | Typical CDE state | Use |
|---|---|---|---|
| S0 | Initial | WIP | First draft, internal only |
| S1 | Suitable for coordination | SHARED | Ready for other disciplines to coordinate against |
| S2 | Suitable for information | SHARED | For client information — not for construction |
| S3 | Suitable for review and comment | SHARED | Formal review period — comments invited |
| S4 | Suitable for construction | PUBLISHED | Builders and fabricators work from this |
| S5 | Suitable for manufacture | PUBLISHED | Shop drawings, fabrication |
| S6 | Suitable for PIM/AIM authorisation | PUBLISHED | Asset information model — FM handover |
| S7 | As-built | ARCHIVE | Final record of what was built |

---

### Role hierarchy

```
Viewer      Can read Published and Archive only
Contributor Can read WIP, Shared, Published, Archive (own discipline)
Coordinator Can promote WIP→Shared and Shared→WIP. Can issue, re-issue.
Manager     Can promote Shared→Published (requires approval). Can approve.
Admin       Full access to all settings, members, and access profiles.
Owner       Full control including billing and tenant management.
```

---

## Cross-references

The STING Document Manager does not operate in isolation. Other parts of the system produce
the documents that flow through it:

- **DRAWING_TYPE_MANAGER_GUIDE** (`docs/guides/DRAWING_TYPE_MANAGER_GUIDE.md`): Drawing
  types produce the Revit sheets and views that become deliverables. The Drawing Type
  Manager determines which title block, scale, and sheet layout a drawing uses before
  it is issued through the Document Manager.

- **TAGGING_PROCEDURES_GUIDE** (`docs/TAGGING_PROCEDURES_GUIDE.md`): Every element that
  appears on an issued drawing needs a complete ISO 19650 tag before the compliance
  gate allows issue. Read this guide to understand how tagging works before you try
  to issue documents.

- **BIM_MANAGEMENT_GUIDE** (`docs/BIM_MANAGEMENT_GUIDE.md`): The BEP, MIDP, and stage
  gate processes that govern when documents are issued. The Document Manager is the
  mechanism; BIM Management is the plan.

- **HEALTHCARE_PACK_DESIGN** (`docs/HEALTHCARE_PACK_DESIGN.md`): Healthcare projects
  have additional document workflows (MGPS verification, RDS issue, HTM maintenance
  records) that flow through the same document system.

- **AEC_FILTER_LIBRARY** (`docs/AEC_FILTER_LIBRARY.md`): The view style packs and
  filters that control how drawings look before they are issued. Drawings that look
  wrong should be fixed at the filter/template level before issuing.
