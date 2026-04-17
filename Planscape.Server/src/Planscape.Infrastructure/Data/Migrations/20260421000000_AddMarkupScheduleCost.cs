using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>P3 + P4 + P5 — document markups, schedule tasks, cost items.</remarks>
public partial class AddMarkupScheduleCost : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── DocumentMarkups ──
        migrationBuilder.CreateTable(
            name: "DocumentMarkups",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                PreviousMarkupId = table.Column<Guid>(type: "uuid", nullable: true),
                ShapesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                PageNumber = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentMarkups", x => x.Id);
                table.ForeignKey(
                    name: "FK_DocumentMarkups_Documents_DocumentId",
                    column: x => x.DocumentId, principalTable: "Documents",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_DocumentMarkups_Users_CreatedByUserId",
                    column: x => x.CreatedByUserId, principalTable: "Users",
                    principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            });
        migrationBuilder.CreateIndex(name: "IX_DocumentMarkups_DocumentId", table: "DocumentMarkups", column: "DocumentId");

        // ── ScheduleTasks ──
        migrationBuilder.CreateTable(
            name: "ScheduleTasks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                RibaStage = table.Column<int>(type: "integer", nullable: true),
                Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                PlannedStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                PlannedFinish = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ActualStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ActualFinish = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                BaselineStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                BaselineFinish = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                PercentComplete = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                PredecessorIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                IsMilestone = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                LinkedMetric = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScheduleTasks", x => x.Id);
                table.ForeignKey(
                    name: "FK_ScheduleTasks_Projects_ProjectId",
                    column: x => x.ProjectId, principalTable: "Projects",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(name: "IX_ScheduleTasks_ProjectId", table: "ScheduleTasks", column: "ProjectId");
        migrationBuilder.CreateIndex(name: "IX_ScheduleTasks_ProjectId_Code", table: "ScheduleTasks", columns: new[] { "ProjectId", "Code" }, unique: true);

        // ── CostItems ──
        migrationBuilder.CreateTable(
            name: "CostItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                TradeBucket = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ScheduleTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "ea"),
                Quantity = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                UnitRate = table.Column<decimal>(type: "numeric(18,4)", nullable: false, defaultValue: 0m),
                LineTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "GBP"),
                Kind = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CostItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_CostItems_Projects_ProjectId",
                    column: x => x.ProjectId, principalTable: "Projects",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_CostItems_ScheduleTasks_ScheduleTaskId",
                    column: x => x.ScheduleTaskId, principalTable: "ScheduleTasks",
                    principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            });
        migrationBuilder.CreateIndex(name: "IX_CostItems_ProjectId", table: "CostItems", column: "ProjectId");
        migrationBuilder.CreateIndex(name: "IX_CostItems_ProjectId_Code", table: "CostItems", columns: new[] { "ProjectId", "Code" });
        migrationBuilder.CreateIndex(name: "IX_CostItems_ScheduleTaskId", table: "CostItems", column: "ScheduleTaskId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CostItems");
        migrationBuilder.DropTable(name: "ScheduleTasks");
        migrationBuilder.DropTable(name: "DocumentMarkups");
    }
}
