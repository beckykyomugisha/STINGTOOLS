using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Adds columns for all 14 coordination workflow gaps:
///
/// DocumentApprovals:
///   - RevisionSnapshot   — scopes approval validity to the document revision at
///                          request time, preventing a stale approval from satisfying
///                          the gate after a rework bump (Gap 2).
///   - RequestedByUserId  — enables push notification to the requestor on decision (Gap 4).
///
/// Transmittals:
///   - RecipientUserId    — targeted push on send (Gap 6).
///   - SlaDeadline        — SLA tracking for acknowledge/respond lifecycle (Gap 7).
///   - AcknowledgedAt/By  — ACKNOWLEDGED state (Gap 7).
///   - RespondedAt/By     — RESPONDED state (Gap 7).
///   - ResponseNotes      — recipient response comments (Gap 7).
///
/// WorkflowRuns:
///   - LinkedEntityJson   — links a run to affected document/issue/transmittal IDs (Gaps 10 + 14).
/// </summary>
public partial class AddCoordinationWorkflowGaps : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // ── DocumentApprovals ────────────────────────────────────────────────

        mb.AddColumn<Guid>(
            name: "RequestedByUserId",
            table: "DocumentApprovals",
            type: "uuid",
            nullable: true);

        mb.AddColumn<string>(
            name: "RevisionSnapshot",
            table: "DocumentApprovals",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        // Index makes CheckApprovalGate queries efficient: look up by docId + transition + status.
        mb.CreateIndex(
            name: "IX_DocumentApprovals_DocumentId_Transition_Status",
            table: "DocumentApprovals",
            columns: new[] { "DocumentId", "Transition", "Status" });

        // ── Transmittals ──────────────────────────────────────────────────────

        mb.AddColumn<Guid>(
            name: "RecipientUserId",
            table: "Transmittals",
            type: "uuid",
            nullable: true);

        mb.AddColumn<DateTime>(
            name: "SlaDeadline",
            table: "Transmittals",
            type: "timestamp with time zone",
            nullable: true);

        mb.AddColumn<DateTime>(
            name: "AcknowledgedAt",
            table: "Transmittals",
            type: "timestamp with time zone",
            nullable: true);

        mb.AddColumn<string>(
            name: "AcknowledgedBy",
            table: "Transmittals",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        mb.AddColumn<DateTime>(
            name: "RespondedAt",
            table: "Transmittals",
            type: "timestamp with time zone",
            nullable: true);

        mb.AddColumn<string>(
            name: "RespondedBy",
            table: "Transmittals",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        mb.AddColumn<string>(
            name: "ResponseNotes",
            table: "Transmittals",
            type: "text",
            nullable: true);

        // ── WorkflowRuns ───────────────────────────────────────────────────────

        mb.AddColumn<string>(
            name: "LinkedEntityJson",
            table: "WorkflowRuns",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn(name: "RequestedByUserId", table: "DocumentApprovals");
        mb.DropColumn(name: "RevisionSnapshot", table: "DocumentApprovals");
        mb.DropIndex(name: "IX_DocumentApprovals_DocumentId_Transition_Status", table: "DocumentApprovals");

        mb.DropColumn(name: "RecipientUserId", table: "Transmittals");
        mb.DropColumn(name: "SlaDeadline", table: "Transmittals");
        mb.DropColumn(name: "AcknowledgedAt", table: "Transmittals");
        mb.DropColumn(name: "AcknowledgedBy", table: "Transmittals");
        mb.DropColumn(name: "RespondedAt", table: "Transmittals");
        mb.DropColumn(name: "RespondedBy", table: "Transmittals");
        mb.DropColumn(name: "ResponseNotes", table: "Transmittals");

        mb.DropColumn(name: "LinkedEntityJson", table: "WorkflowRuns");
    }
}
