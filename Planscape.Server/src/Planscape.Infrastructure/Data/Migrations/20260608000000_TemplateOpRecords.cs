using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// P3-2 reconcile — adds the creating DDL for <c>TemplateOpRecords</c>, the
/// Template Manager operation log pushed by the Revit plugin via
/// <c>POST /api/projects/{projectId}/template-ops</c>. The entity + DbSet +
/// ModelSnapshot already carried it, but no migration ever created the table,
/// so pre-existing DBs drifted (SchemaDriftChecker reported
/// <c>MISSING TABLE : TemplateOpRecords</c>). The running fix is the idempotent
/// statement in <c>PatchDevSchemaAsync</c>; this file completes the migration
/// set with the exact DDL EF Core would emit.
///
/// NOTE on repo convention (mirrors 20260601000000_CrossHostIdentityFields):
/// this project's migrations are hand-authored without .Designer.cs companions
/// and are NOT discovered by EF's Migrate() (no [Migration] attribute). Dev /
/// local stacks build schema from OnModelCreating via
/// RelationalDatabaseCreator.CreateTables() + PatchDevSchemaAsync; this file is
/// kept so the change is covered once the prod migration pipeline is repaired.
/// </summary>
public partial class TemplateOpRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TemplateOpRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OperationLabel = table.Column<string>(type: "text", nullable: false),
                Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Headline = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                SubHeadline = table.Column<string>(type: "text", nullable: true),
                CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DurationMs = table.Column<double>(type: "double precision", nullable: false),
                CapturedBy = table.Column<string>(type: "text", nullable: false),
                DocumentPath = table.Column<string>(type: "text", nullable: true),
                DocumentTitle = table.Column<string>(type: "text", nullable: true),
                CreatedCount = table.Column<int>(type: "integer", nullable: false),
                SkippedCount = table.Column<int>(type: "integer", nullable: false),
                FailedCount = table.Column<int>(type: "integer", nullable: false),
                SectionCount = table.Column<int>(type: "integer", nullable: false),
                CountersJson = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TemplateOpRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TemplateOpRecords_TenantId",
            table: "TemplateOpRecords",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_TemplateOpRecords_ProjectId_CompletedUtc",
            table: "TemplateOpRecords",
            columns: new[] { "ProjectId", "CompletedUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_TemplateOpRecords_ProjectId_Operation",
            table: "TemplateOpRecords",
            columns: new[] { "ProjectId", "Operation" });

        migrationBuilder.CreateIndex(
            name: "IX_TemplateOpRecords_TenantId_CompletedUtc",
            table: "TemplateOpRecords",
            columns: new[] { "TenantId", "CompletedUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TemplateOpRecords");
    }
}
