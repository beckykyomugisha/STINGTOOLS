using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// S1.1 — denormalises <c>TenantId</c> onto every <c>ITenantScoped</c>
/// entity that didn't already carry it. The column is added nullable,
/// backfilled from each row's parent (Project, Issue, Document, Meeting,
/// SiteDiary, StageGate as appropriate), then made NOT NULL. An index on
/// TenantId is added per-table so the global query filter doesn't trigger
/// a sequential scan.
///
/// Backfill is the only step that touches data — it's idempotent and uses
/// CTE-style updates so a second run is a no-op.
/// </remarks>
public partial class AddTenantIdToAllScopedEntities : Migration
{
    private static readonly string[] ProjectChildTables =
    {
        "TaggedElements", "Issues", "Documents", "WorkflowRuns",
        "ComplianceSnapshots", "SeqCounters", "Meetings", "Transmittals",
        "ProjectMembers", "ProjectModels", "ScheduleTasks", "CostItems",
        "SyncConflicts", "SyncWatermarks", "SiteDiaries", "StageGates",
        "IssueCustomFieldSchemas",
    };

    private static readonly (string table, string parentTable, string parentFk)[] IndirectChildTables =
    {
        ("IssueAttachments",       "Issues",          "IssueId"),
        ("IssueComments",          "Issues",          "IssueId"),
        ("DocumentMarkups",        "Documents",       "DocumentId"),
        ("DocumentVersions",       "Documents",       "DocumentId"),
        ("DocumentApprovals",      "Documents",       "DocumentId"),
        ("MeetingActionItems",     "Meetings",        "MeetingId"),
        ("SiteDiaryAttachments",   "SiteDiaries",     "SiteDiaryId"),
        ("InformationDeliverables","StageGates",      "StageGateId"),
        ("StageGateCriteria",      "StageGates",      "StageGateId"),
    };

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Add TenantId column (nullable for backfill).
        foreach (var table in ProjectChildTables)
            AddNullableTenantId(migrationBuilder, table);
        foreach (var (table, _, _) in IndirectChildTables)
            AddNullableTenantId(migrationBuilder, table);

        // 2. Backfill from parents.
        foreach (var table in ProjectChildTables)
        {
            // Skip ProjectModels — already has TenantId via earlier migration if any
            migrationBuilder.Sql(
                $"UPDATE \"{table}\" t SET \"TenantId\" = p.\"TenantId\" " +
                $"FROM \"Projects\" p WHERE t.\"ProjectId\" = p.\"Id\" AND t.\"TenantId\" IS NULL;");
        }
        foreach (var (table, parent, fk) in IndirectChildTables)
        {
            migrationBuilder.Sql(
                $"UPDATE \"{table}\" c SET \"TenantId\" = p.\"TenantId\" " +
                $"FROM \"{parent}\" p WHERE c.\"{fk}\" = p.\"Id\" AND c.\"TenantId\" IS NULL;");
        }

        // 3. Promote to NOT NULL + add index. If any rows still have a null
        //    TenantId after backfill (orphans whose parent row no longer
        //    exists), the AlterColumn call will fail loudly. The operator
        //    must inspect and clean up manually — we do NOT silently delete
        //    user data. A useful inspection query lives in the migration
        //    docs: SELECT count(*) FROM "<table>" WHERE "TenantId" IS NULL;
        foreach (var table in ProjectChildTables)
            PromoteToNotNullWithIndex(migrationBuilder, table);
        foreach (var (table, _, _) in IndirectChildTables)
            PromoteToNotNullWithIndex(migrationBuilder, table);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in ProjectChildTables)
        {
            migrationBuilder.DropIndex(name: $"IX_{table}_TenantId", table: table);
            migrationBuilder.DropColumn(name: "TenantId", table: table);
        }
        foreach (var (table, _, _) in IndirectChildTables)
        {
            migrationBuilder.DropIndex(name: $"IX_{table}_TenantId", table: table);
            migrationBuilder.DropColumn(name: "TenantId", table: table);
        }
    }

    private static void AddNullableTenantId(MigrationBuilder mb, string table)
    {
        mb.AddColumn<Guid>(
            name: "TenantId",
            table: table,
            type: "uuid",
            nullable: true);
    }

    private static void PromoteToNotNullWithIndex(MigrationBuilder mb, string table)
    {
        mb.AlterColumn<Guid>(
            name: "TenantId",
            table: table,
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
        mb.CreateIndex(
            name: $"IX_{table}_TenantId",
            table: table,
            column: "TenantId");
    }
}
