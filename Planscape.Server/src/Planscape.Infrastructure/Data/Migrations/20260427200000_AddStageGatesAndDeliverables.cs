using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 144 — RIBA Plan of Work stage gates + MIDP / IE deliverables.
/// Two new tables:
///   StageGates              — one row per (project, stage_code).
///   InformationDeliverables — one row per MIDP/IE item, optional FK
///                             back to a StageGate row.
/// </remarks>
public partial class AddStageGatesAndDeliverables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StageGates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                StageCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: ""),
                StageName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                PlannedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ActualDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "NOT_STARTED"),
                Description = table.Column<string>(type: "text", nullable: true),
                CriteriaJson = table.Column<string>(type: "jsonb", nullable: true),
                DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StageGates", x => x.Id);
                table.ForeignKey(
                    name: "FK_StageGates_Projects_ProjectId",
                    column: x => x.ProjectId, principalTable: "Projects",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(name: "IX_StageGates_ProjectId", table: "StageGates", column: "ProjectId");
        migrationBuilder.CreateIndex(name: "IX_StageGates_ProjectId_StageCode", table: "StageGates", columns: new[] { "ProjectId", "StageCode" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_StageGates_Status", table: "StageGates", column: "Status");

        migrationBuilder.CreateTable(
            name: "InformationDeliverables",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                StageGateId = table.Column<Guid>(type: "uuid", nullable: true),
                Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: ""),
                Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false, defaultValue: ""),
                Description = table.Column<string>(type: "text", nullable: true),
                Type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "DR"),
                OwnerRole = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: ""),
                Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                SuitabilityTarget = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "PENDING"),
                SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SubmittedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AcceptedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                RejectionReason = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InformationDeliverables", x => x.Id);
                table.ForeignKey(
                    name: "FK_InformationDeliverables_Projects_ProjectId",
                    column: x => x.ProjectId, principalTable: "Projects",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_InformationDeliverables_StageGates_StageGateId",
                    column: x => x.StageGateId, principalTable: "StageGates",
                    principalColumn: "Id", onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_InformationDeliverables_Documents_DocumentId",
                    column: x => x.DocumentId, principalTable: "Documents",
                    principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            });
        migrationBuilder.CreateIndex(name: "IX_InformationDeliverables_ProjectId", table: "InformationDeliverables", column: "ProjectId");
        migrationBuilder.CreateIndex(name: "IX_InformationDeliverables_ProjectId_Code", table: "InformationDeliverables", columns: new[] { "ProjectId", "Code" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_InformationDeliverables_StageGateId", table: "InformationDeliverables", column: "StageGateId");
        migrationBuilder.CreateIndex(name: "IX_InformationDeliverables_Status", table: "InformationDeliverables", column: "Status");
        migrationBuilder.CreateIndex(name: "IX_InformationDeliverables_DueDate", table: "InformationDeliverables", column: "DueDate");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "InformationDeliverables");
        migrationBuilder.DropTable(name: "StageGates");
    }
}
