using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Clash automation glue layer — adds the ClashAutomationRules table
/// consumed by <c>ClashAutomationService.ProcessNewClashesAsync</c>.
///
/// Each row is a per-project rule with optional match criteria
/// (severity, discipline, kind, overlap volume, level) plus actions
/// (auto-issue, push notify, webhook fire). Rules fire in
/// <c>Priority</c> order (lower = higher priority); the first matching
/// auto-issue rule wins, but all matching notification + webhook rules
/// fire.
///
/// MinSeverity and Kind are stored as nullable ints because they're
/// nullable <c>enum</c>-backed fields on the entity.
/// </remarks>
public partial class AddClashAutomationRules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "ClashAutomationRules",
            columns: t => new
            {
                Id                  = t.Column<Guid>("uuid", nullable: false),
                TenantId            = t.Column<Guid>("uuid", nullable: false),
                ProjectId           = t.Column<Guid>("uuid", nullable: false),
                Name                = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
                Enabled             = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                Priority            = t.Column<int>("integer", nullable: false, defaultValue: 100),
                MinSeverity         = t.Column<int>("integer", nullable: true),
                DisciplineA         = t.Column<string>("character varying(8)", maxLength: 8, nullable: true),
                DisciplineB         = t.Column<string>("character varying(8)", maxLength: 8, nullable: true),
                Kind                = t.Column<int>("integer", nullable: true),
                MinOverlapVolumeMm3 = t.Column<double>("double precision", nullable: true),
                LevelCode           = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                AutoCreateIssue     = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                AutoAssignTo        = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                IssuePriority       = t.Column<string>("character varying(20)", maxLength: 20, nullable: true),
                NotifyPush          = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                NotifyUsers         = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                FireWebhook         = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                CreatedAt           = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt           = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy           = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ClashAutomationRules", x => x.Id);
                t.ForeignKey(
                    name: "FK_ClashAutomationRules_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex(
            name: "IX_ClashAutomationRules_ProjectId_Priority",
            table: "ClashAutomationRules",
            columns: new[] { "ProjectId", "Priority" });

        mb.CreateIndex(
            name: "IX_ClashAutomationRules_TenantId",
            table: "ClashAutomationRules",
            column: "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("ClashAutomationRules");
    }
}
