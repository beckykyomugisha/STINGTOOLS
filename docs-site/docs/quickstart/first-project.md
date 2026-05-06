# Create your first project

A Planscape project is the container for one building, one phase, or one work-package — depending on how your firm structures delivery. It includes a CDE folder structure, a team roster with roles, ISO 19650 metadata, and a connection point for the Revit plugin and the mobile app.

## 1 — Create the project

From the dashboard, click **New Project**. Fill in:

| Field | Example | Notes |
|---|---|---|
| Project name | "Kampala Hospital — Block C" | Free-text, shown in the project list |
| ISO 19650 project code | `PLNS-2026-001` | Used in tag prefixes and document numbering. Keep it short. |
| Country | Uganda | Drives default currency for invoicing |
| Discipline(s) | Architecture, Structure, MEP | Tick all that apply — affects template defaults |
| RIBA stage | Stage 3 — Spatial Coordination | Drives stage-gate criteria |
| Team members | Invite by email | They receive an invitation with role assignment |

Click **Create**. The project is provisioned in a few seconds. Behind the scenes Planscape creates the `_BIM_COORD/` folder structure required by ISO 19650 — `WIP`, `SHARED`, `PUBLISHED`, `ARCHIVED`, plus a `templates/` folder with the default 16 document templates and 5 workflow definitions.

## 2 — Connect a Revit model

On a workstation with the [Revit plugin installed](revit-plugin.md):

1. Open the `.rvt` file you want to coordinate.
2. In the STING Tools dock panel, go to **BIM → Planscape Sync**.
3. Click **Select Project** and pick the project you just created.
4. Click **Sync now**. The first sync:
    - Auto-populates the 8-token ISO 19650 tags on every taggable element using the spatial context (rooms, levels, MEP systems)
    - Writes the rich TAG7 narrative for each element
    - Pushes the tag index to the cloud
    - Caches a local sidecar (`<project>.sting_seq.json`) so SEQ numbers are continuous across sessions

For a 30k-element model the first sync typically takes 10–30 seconds. Subsequent syncs only push deltas and complete in a second or two.

## 3 — Create your first issue

From the dashboard or mobile:

1. **Issues → New Issue**.
2. Choose a type: **RFI** (request for information), **NCR** (non-conformance), or **SI** (site instruction).
3. Fill in title, description, discipline, assignee, and due date.
4. Optionally attach a photo (mobile) or paste a Revit element tag (web).

The assignee receives a push notification on mobile and an email. They open the issue, respond, and the originator is notified back. The full thread — including comments, status changes, and attachments — is hash-chained for the audit trail.

For a deeper walk-through see [Issue an RFI](../howto/rfi.md).

## 4 — Issue a transmittal

Documents in the CDE move through states (WIP → Shared → Published → Archived). When you Publish a set of documents to an external party, you issue a **transmittal** that records what was sent, to whom, and at what suitability code.

1. **Documents → select your documents → Actions → New Transmittal**.
2. Set the suitability code (S2 — Suitable for Information, S4 — Suitable for Construction, etc.).
3. Pick the recipient group (defined under **Project Settings → Distribution**).
4. Click **Issue**. Planscape auto-renders the transmittal PDF using the B06 template, attaches the documents, and emails the recipients with a secure download link — no Planscape login required for them to download.

Tracking moves through Draft → Issued → Acknowledged. SLA breaches surface in the BIM Coordination Center.

## 5 — Verify on site

Open the [mobile app](mobile.md), sign in, and your project is now visible. Site coordinators can:

- Scan a QR tag stuck to a piece of MEP plant — the app pulls up its full tag (`M-BLD1-Z01-L02-HVAC-SUP-AHU-0003`), TAG7 narrative, and any related issues
- Raise an RFI from the field with a photo
- Check off a stage-gate criterion with evidence
- All offline if the site has no signal

## Next steps

- [ISO 19650 tag format](../concepts/iso19650-tag.md) — understand the 8-segment tags Planscape generates
- [CDE state machine](../concepts/cde.md) — how documents move through WIP / Shared / Published / Archived
- [Bulk-tag elements](../howto/bulk-tag.md) — for migrating existing untagged Revit models
