# Sign off a stage gate

A **stage gate** is a formal milestone in the project lifecycle — typically aligned with a RIBA stage in the UK or an equivalent stage in your jurisdiction. Sign-off means the appointing party (or their delegated approver) confirms the deliverables for that stage are complete, the design is coordinated, and the project can move to the next stage.

Planscape models stage gates as a structured workflow with criteria, evidence, approval, and an audit-locked sign-off record.

## What is a stage gate

Each project is configured with a sequence of RIBA stages (or your firm's equivalent). For each stage, Planscape ships a default **criteria list** — the things that must be true before the gate can be signed off. Examples for **RIBA Stage 3 — Spatial Coordination**:

- All discipline models synced and federated
- Clash detection run, all major clusters resolved or assigned
- All RFIs raised in the stage closed or carried forward with sign-off
- Stage 3 design package issued at S3 suitability
- Cost plan signed off by the QS

You can add, remove, or modify criteria per project in **Project Settings → Stage Gates**.

## Step 1 — Open the stage

From the dashboard, **Stages → [Current RIBA Stage]**. The stage page lists all criteria with their current status:

- ✓ Green — complete with evidence attached
- ◷ Amber — in progress
- ✗ Red — not yet started or blocking

You also see the linked **MIDP deliverables** for the stage — the documents that must be Published before the gate can be signed.

## Step 2 — Mark criteria complete

For each criterion, click into it. The detail panel shows:

- The criterion description and acceptance condition
- A field to attach evidence (a document, a clash report, a cost-plan PDF)
- A note field for the rationale
- A **Mark complete** button

Attach evidence and click **Mark complete**. The criterion turns green. Until you submit for approval, criteria can be unticked or evidence replaced.

For criteria that are computed automatically (e.g. "All RFIs closed"), Planscape ticks them when the underlying condition is met. You don't need to do anything.

## Step 3 — Submit for approval

When all criteria are green, the **Submit for Approval** button activates. Click it. The dialog asks:

- **Approver(s)** — defaults to the project's BIM Information Manager + the appointing party's representative. Add or remove as needed.
- **Submission note** — your statement to the approvers.
- **Lock model snapshot** — recommended. Captures a frozen copy of every model and document in their current state, attached to the gate record. The audit trail then references this snapshot.

Click **Submit**. The approvers receive a push notification and email.

## Step 4 — Approvers review

Each approver opens the gate from their inbox. They see:

- The full criteria list with evidence links — they can drill into each one
- The submission note from the originator
- A timeline of recent activity
- A snapshot of the federated model (read-only)
- **Approve** / **Reject with comments** buttons

If they reject, the gate goes back to the originator with the comments — typical reasons are missing evidence, criteria they don't accept as complete, or a design concern they want addressed first. The originator addresses the comments and re-submits.

If they approve, the gate moves to **Signed off** state.

## Step 5 — After sign-off

Once the gate is signed off:

- The stage record is **locked read-only**. Criteria evidence cannot be edited; the snapshot is immutable.
- The audit log captures the sign-off with timestamp, approver identity, and a hash of the criteria-evidence-snapshot bundle. This record is verifiable as part of [the audit log](../ops/audit-verify.md).
- The project advances to the next stage. The next stage's criteria become visible and trackable.
- A signed-off PDF certificate is generated and attached to the project document register — useful as the evidence of stage completion in client reporting.

## Stage history

The **Stages** tab shows the whole lifecycle: which stages are signed off (with timestamp + approver), which is in progress, which are upcoming. Each signed-off gate has a link to the locked snapshot — useful for "what did we commit to at Stage 3?" questions later in the project.

## Reopening a signed-off gate

In the rare case where a signed-off gate needs to be re-opened — typically because of a discovered defect or a contract change — only project admins can do so. **Stages → [Stage] → Actions → Reopen**. Reopening creates a new gate record (the original stays, locked); the new record starts in Draft and follows the normal flow back through approval.

The audit log captures the reopening with reason. This is intentionally a heavyweight action — it should be rare.

## Next steps

- [Verify the audit log](../ops/audit-verify.md) — independently validate stage-gate sign-offs
- [CDE state machine](../concepts/cde.md) — understand the suitability codes that gate transitions use
