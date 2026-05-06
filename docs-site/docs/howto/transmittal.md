# Generate a transmittal

A **transmittal** is a formal record that accompanies a document issue — it tells the recipient what they're being sent, at what suitability, on what date, by whom, and for what purpose. ISO 19650 requires a transmittal for every formal issue between parties; Planscape generates them automatically using the B06 template.

## When to use a transmittal

- Issuing a Stage 3 design package to the appointing party for review (S3)
- Releasing construction drawings to the contractor (S4)
- Forwarding consultants' submissions to the client
- Any cross-party document exchange that needs a paper trail

For internal team-to-team document moves within the same project, the CDE state transition (WIP → Shared) is the audit record — no transmittal needed.

## Step 1 — Select documents

From the **Documents** tab:

1. Filter to the documents you're issuing. Use the discipline, suitability, and revision filters to narrow down.
2. Tick the documents in the list (multi-select with shift-click for ranges).
3. Click **Actions → New Transmittal**.

## Step 2 — Fill the transmittal form

The transmittal dialog asks:

| Field | Notes |
|---|---|
| **Transmittal number** | Auto-generated using the project's transmittal number pattern (e.g. `TM-PLNS-2026-001-014`). Editable if you need to override. |
| **Suitability code** | The code being applied to all documents in this batch — S1, S2, S3, S4, or S5. The selected documents transition to this code on issue. |
| **Recipient group** | Pick from the project's distribution groups (defined under **Project Settings → Distribution**), or enter ad-hoc emails. |
| **Purpose of issue** | Free-text reason — "For Stage 3 review", "Construction issue", "RFI response". Appears prominently on the transmittal. |
| **Cover note** | Optional longer message. Supports basic markdown. |
| **Due date for response** | Optional. Used for tracking — appears on the recipient's reminder email. |

Click **Issue**.

## Step 3 — Automatic rendering and dispatch

Once you click Issue, Planscape:

1. Transitions every selected document to the chosen suitability — recorded in the audit log.
2. Renders the transmittal PDF using the B06 template — a one-page cover with the transmittal number, parties, suitability, document list, and signature block.
3. Bundles the cover PDF + the documents into a ZIP package.
4. Emails each recipient a secure download link. Recipients do not need a Planscape account — the link is signed and expires after 14 days.
5. Logs the dispatch and starts the **transmittal_default** workflow.

## Step 4 — Track acknowledgements

The transmittal moves through three states:

- **Draft** — created, not yet issued. (Skipped if you click Issue immediately.)
- **Issued** — sent, awaiting recipient acknowledgement
- **Acknowledged** — at least one recipient has clicked the download link or replied to the dispatch email

The transmittal list view shows a status pill for each one. Hover for the recipient breakdown — who's downloaded, who hasn't, who has replied with comments.

If a 24/72/∞-hour SLA is configured (default in `transmittal_default`), unacknowledged transmittals trigger reminder emails at 24 and 72 hours.

## Re-issue a transmittal

If the recipient asks for a revised set, or you spot an error post-issue:

1. Open the transmittal.
2. **Actions → Re-issue**.
3. Add or remove documents in the dialog.
4. Set the new suitability code (often the same; sometimes bumped — e.g. S3 → S4 after review).
5. Click **Issue**.

The new transmittal cross-links to the original (`TM-…-014-R1`). Both records remain in the system; the audit chain captures the relationship.

## Bulk transmittals

For large issues — say, all 200 stage-3 deliverables to the client at once — the Bulk Issue command on the Documents tab handles every selected document in a single transmittal record. The B06 PDF lists all 200 documents in a tabular appendix. The dispatch ZIP can be a single archive (default, up to 2 GB) or split into multiple archives (configurable in **Project Settings → Transmittal**).

## Next steps

- [CDE state machine](../concepts/cde.md) — understand how suitability codes interact with WIP/Shared/Published
- [Sign off a stage gate](stage-gate.md) — formally close out the stage for which the transmittal was issued
