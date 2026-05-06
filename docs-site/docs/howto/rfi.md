# Issue an RFI

A **Request for Information (RFI)** is a formal question raised when the construction team needs clarification from the design team — often because something on site doesn't match the model, or because a detail isn't fully resolved. Planscape tracks RFIs through to closure with SLA monitoring, comment threads, and an audit trail.

## Raise an RFI from the web dashboard

1. **Issues → New Issue**.
2. Set **Type: RFI**.
3. Fill the required fields:
    - **Title** — a short statement of the question (e.g. "Beam B12-L02 conflicts with supply duct")
    - **Description** — the full context. Be specific: what you observed, what you expected, what you'd need to know to proceed.
    - **Discipline** — drives auto-routing. An MEP RFI defaults to the MEP lead.
    - **Assignee** — who's responsible for answering. Defaults to the discipline lead.
    - **Due date** — typically 5 working days, but configurable per project in **Project Settings → SLA**.
4. Optionally:
    - Attach a photo (drag and drop, or click the camera icon)
    - Paste a Revit element tag to link the RFI to a specific element (e.g. `S-BLD1-Z01-L02-STR-BEAM-0042`)
5. Click **Raise RFI**.

The assignee receives a push notification on mobile and an email immediately. The RFI appears in their inbox queue.

## Raise an RFI from the mobile app

On site, the workflow is faster:

1. Tap **+** in the issues tab.
2. Tap **RFI**.
3. Take a photo (or pick from gallery) — required for site-raised RFIs by default.
4. Type the title and description. Long-press the mic button to dictate.
5. The app GPS-stamps the location automatically (if you granted permission).
6. Optionally tap **Scan QR** to link the RFI to a specific tagged element.
7. Tap **Raise**.

If you're offline the RFI sits in the action queue with an amber dot. It uploads automatically when you regain signal — usually before you've left the basement.

## Respond to an RFI

The assignee opens the RFI from their inbox or notification:

1. Read the description and any attachments.
2. Add a response in the **Comments** section. Markdown is supported for code references and lists.
3. Optionally attach a sketch or revised drawing.
4. Change status from **Open** to **Answered**.

The originator is notified. They review the answer and either:

- Accept and close (Answered → Closed)
- Reject with a follow-up comment (back to Open, the assignee is re-notified)
- Escalate to a more senior approver (status → Escalated; the project manager is added)

## SLA tracking

Each RFI has a due date. The **BIM Coordination Center** dashboard shows:

- RFIs due in the next 48 hours — amber
- RFIs overdue — red
- RFIs answered within SLA in the last 30 days — a percentage chip

If you have an Enterprise plan, breaching SLAs trigger an automatic escalation email to the project manager. Configure thresholds in **Project Settings → SLA Escalation**.

## Generate the RFI document

Some appointing parties require a formal PDF for every RFI. From the RFI detail page, **Actions → Generate Document** renders the B08 RFI template — a single-page A4 PDF with the question, response, parties, dates, and an audit-log signature. The PDF is saved to the document register and can be issued as a transmittal.

## Bulk operations

For projects with many open RFIs, the **Issues** tab supports bulk operations:

- Select multiple RFIs → **Bulk Update Status** to close a batch
- Filter by assignee, discipline, level, or tag prefix to find what's relevant
- Export to CSV for offline review
- Import from BCF 2.1 (when migrating from another platform)

## Next steps

- [Generate a transmittal](transmittal.md) — bundle issued RFIs and other documents into a formal issue
- [Run clash detection](clash.md) — proactively raise RFIs from clash clusters
