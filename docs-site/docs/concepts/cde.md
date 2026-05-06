# CDE state machine

A **Common Data Environment (CDE)** is the single source of truth for project information — the agreed, version-controlled place where models, drawings, specifications, and reports live. ISO 19650-3 defines the CDE workflow that Planscape implements.

## The four states

```
WIP → Shared → Published → Archived
```

| State | Meaning | Who can edit | Who can read |
|---|---|---|---|
| **WIP** (Work in Progress) | The author's working copy. Not yet ready for the team. | The author (single discipline) | The author only |
| **Shared** | Suitable for coordination with other disciplines. Not yet client-issued. | The author, after re-check | All project team members |
| **Published** | Issued to the client / appointing party at a contractually-defined suitability. | Locked. New revisions create a new Published record. | All team members + external recipients on the transmittal |
| **Archived** | Superseded by a newer revision, or moved to long-term storage on project completion. | Locked, read-only | All team members |

The transitions are intentionally one-way under normal operation. You can't move a Published document back to WIP — that would invalidate the audit trail. Instead, you create a new revision, which itself starts in WIP and graduates back through the states.

## Suitability codes

Inside the **Shared** and **Published** states, ISO 19650 uses **suitability codes** to convey what the document is fit for. Planscape adopts the standard set:

| Code | Meaning | Typical use |
|---|---|---|
| **S0** | Initial status | Just created, not yet ready |
| **S1** | Suitable for Coordination | Other disciplines should now check for clashes |
| **S2** | Suitable for Information | Reference material, not yet for review |
| **S3** | Suitable for Review and Comment | Formal review by the appointing party |
| **S4** | Suitable for Construction | Released for construction or fabrication |
| **S5** | As-Built / Record | Final record of what was built |

Suitability is a property of the document itself, set by the author at issue. It is a separate axis from the CDE state — a document can be Published at S2, then re-Published at S4 after construction-stage approval.

## How transitions work

From **Documents** in the dashboard, select a document and click **Transition**. The dialog shows the available next states based on the current state and your role:

- An author can move WIP → Shared themselves.
- Shared → Published requires an **approval workflow**. By default, the project's BIM Information Manager must approve. You can configure additional approvers (e.g. discipline lead, design manager) per document type in **Project Settings → Approval Workflows**.
- Published → Archived happens automatically when a new revision of the same document is Published, or manually at project closeout.

Every transition is recorded in the audit log with actor, timestamp, the state moved from and to, and the suitability code at the time. Approval signatures are captured as separate `DocumentApproval` records and chain-linked to the transition.

## Stage-gate approvals

For higher-stakes transitions — typically to S3 or S4 — Planscape's **stage-gate workflow** lets the appointing party (or a delegated approver) sign off the entire batch of documents in one go. You collect all the stage-deliverables in the dashboard, set the suitability, and submit for stage-gate approval. The approver receives a single notification, reviews the batch, and either signs off or rejects with comments. Sign-off is timestamped and locks the documents read-only.

See [Sign off a stage gate](../howto/stage-gate.md) for a step-by-step walk-through.

## Plugin interaction

The Revit plugin interacts with CDE state at one point: when you **Publish 3D Model** from the plugin, it pushes the current `.rvt` snapshot to the cloud and transitions the associated document record to S4 — Suitable for Construction. This is gated by an approval workflow if your project has one configured.

Authors can keep working in their `.rvt` after publishing — the published version is a frozen snapshot in the CDE; their working file goes back to S0 on the next save.

## Freeze and archive

At the end of a project, you typically **freeze** the CDE — every document is moved to Archived, the project is locked read-only, and a final audit-log export is produced. Frozen projects don't count against your active-project quota. Re-opening a frozen project requires Enterprise-plan support.

## Next steps

- [Generate a transmittal](../howto/transmittal.md) — issue a set of Published documents to an external recipient
- [Sign off a stage gate](../howto/stage-gate.md) — formally close out a RIBA stage
- [Verify the audit log](../ops/audit-verify.md) — independently check the integrity of CDE state changes
