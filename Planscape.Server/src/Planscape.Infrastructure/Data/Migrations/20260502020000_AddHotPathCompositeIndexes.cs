using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// S3.3 — composite indexes covering the hot read paths identified in the
/// scale review. EF's default per-FK indexes already exist (S1.1 added
/// per-table TenantId indexes); this migration adds the multi-column ones
/// the dashboards + tag sync + audit reports rely on.
///
/// Each CONCURRENTLY index would normally need its own migration to avoid
/// long ACCESS EXCLUSIVE locks; we use plain CREATE INDEX here because the
/// tables are still small at firm-1 scale. Mark this migration as a
/// maintenance-window task once any table grows past ~5 M rows.
/// </remarks>
public partial class AddHotPathCompositeIndexes : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // Tag dashboards: count compliance per (project, discipline) recently updated.
        mb.CreateIndex(
            name: "IX_TaggedElements_ProjectId_Disc_LastModifiedUtc",
            table: "TaggedElements",
            columns: new[] { "ProjectId", "Disc", "LastModifiedUtc" });

        // Issue inbox: open issues by assignee, newest first.
        mb.CreateIndex(
            name: "IX_Issues_TenantId_Status_CreatedAt",
            table: "Issues",
            columns: new[] { "TenantId", "Status", "CreatedAt" });
        mb.CreateIndex(
            name: "IX_Issues_AssigneeEmail_Status",
            table: "Issues",
            columns: new[] { "AssigneeEmail", "Status" });

        // Compliance time-series query: latest snapshot per project.
        mb.CreateIndex(
            name: "IX_ComplianceSnapshots_ProjectId_CapturedAt",
            table: "ComplianceSnapshots",
            columns: new[] { "ProjectId", "CapturedAt" },
            descending: new[] { false, true });

        // Audit log: tenant-scoped chain replay (for verify_audit_chain).
        mb.CreateIndex(
            name: "IX_AuditLogs_TenantId_Id",
            table: "AuditLogs",
            columns: new[] { "TenantId", "Id" });
        // Audit log: per-action counts ('login_failed', 'issue_created').
        mb.CreateIndex(
            name: "IX_AuditLogs_TenantId_Action_Timestamp",
            table: "AuditLogs",
            columns: new[] { "TenantId", "Action", "Timestamp" });

        // Documents: CDE-state filter on the dashboard.
        mb.CreateIndex(
            name: "IX_Documents_ProjectId_State_UpdatedAt",
            table: "Documents",
            columns: new[] { "ProjectId", "State", "UpdatedAt" });

        // Models: dashboard list view ordered by upload time.
        mb.CreateIndex(
            name: "IX_ProjectModels_ProjectId_Discipline_UploadedAt",
            table: "ProjectModels",
            columns: new[] { "ProjectId", "Discipline", "UploadedAt" });

        // Project members: 'who has access to project X' lookup.
        mb.CreateIndex(
            name: "IX_ProjectMembers_ProjectId_UserId",
            table: "ProjectMembers",
            columns: new[] { "ProjectId", "UserId" },
            unique: true);

        // Subscriptions: PaymentRouter / dunning lookups.
        mb.CreateIndex(
            name: "IX_Subscriptions_Provider_Status",
            table: "Subscriptions",
            columns: new[] { "Provider", "Status" });

        // Invoices: dashboard 'overdue' query.
        mb.CreateIndex(
            name: "IX_Invoices_TenantId_Status_DueAt",
            table: "Invoices",
            columns: new[] { "TenantId", "Status", "DueAt" });
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropIndex(name: "IX_Invoices_TenantId_Status_DueAt", table: "Invoices");
        mb.DropIndex(name: "IX_Subscriptions_Provider_Status", table: "Subscriptions");
        mb.DropIndex(name: "IX_ProjectMembers_ProjectId_UserId", table: "ProjectMembers");
        mb.DropIndex(name: "IX_ProjectModels_ProjectId_Discipline_UploadedAt", table: "ProjectModels");
        mb.DropIndex(name: "IX_Documents_ProjectId_State_UpdatedAt", table: "Documents");
        mb.DropIndex(name: "IX_AuditLogs_TenantId_Action_Timestamp", table: "AuditLogs");
        mb.DropIndex(name: "IX_AuditLogs_TenantId_Id", table: "AuditLogs");
        mb.DropIndex(name: "IX_ComplianceSnapshots_ProjectId_CapturedAt", table: "ComplianceSnapshots");
        mb.DropIndex(name: "IX_Issues_AssigneeEmail_Status", table: "Issues");
        mb.DropIndex(name: "IX_Issues_TenantId_Status_CreatedAt", table: "Issues");
        mb.DropIndex(name: "IX_TaggedElements_ProjectId_Disc_LastModifiedUtc", table: "TaggedElements");
    }
}
