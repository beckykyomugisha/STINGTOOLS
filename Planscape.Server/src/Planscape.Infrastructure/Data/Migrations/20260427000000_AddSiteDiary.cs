using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 142 — daily site diary (CIOB / CMAA construction-management
/// record). Two new tables: <c>SiteDiaries</c> (header) +
/// <c>SiteDiaryAttachments</c> (link to <c>Documents</c> for photos /
/// PDFs / scans). All JSON columns use jsonb so we can index +
/// query inside <c>ManpowerByTradeJson</c> / <c>EquipmentJson</c> later
/// without a schema change.
/// </remarks>
public partial class AddSiteDiary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SiteDiaries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                DiaryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                AuthorRole = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: ""),
                Weather = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                TemperatureCelsius = table.Column<double>(type: "double precision", nullable: true),
                WindSpeedKph = table.Column<double>(type: "double precision", nullable: true),
                RainfallMm = table.Column<double>(type: "double precision", nullable: true),
                ManpowerCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                ManpowerByTradeJson = table.Column<string>(type: "jsonb", nullable: true),
                EquipmentJson = table.Column<string>(type: "jsonb", nullable: true),
                DeliveriesJson = table.Column<string>(type: "jsonb", nullable: true),
                Narrative = table.Column<string>(type: "text", nullable: true),
                ChecklistJson = table.Column<string>(type: "jsonb", nullable: true),
                VisitorsLog = table.Column<string>(type: "text", nullable: true),
                SafetyIncidents = table.Column<string>(type: "text", nullable: true),
                DelaysAndDisruption = table.Column<string>(type: "text", nullable: true),
                Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "DRAFT"),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AcknowledgedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Latitude = table.Column<double>(type: "double precision", nullable: true),
                Longitude = table.Column<double>(type: "double precision", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SiteDiaries", x => x.Id);
                table.ForeignKey(
                    name: "FK_SiteDiaries_Projects_ProjectId",
                    column: x => x.ProjectId, principalTable: "Projects",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SiteDiaries_Users_AuthorUserId",
                    column: x => x.AuthorUserId, principalTable: "Users",
                    principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            });
        migrationBuilder.CreateIndex(name: "IX_SiteDiaries_ProjectId", table: "SiteDiaries", column: "ProjectId");
        migrationBuilder.CreateIndex(name: "IX_SiteDiaries_ProjectId_DiaryDate", table: "SiteDiaries", columns: new[] { "ProjectId", "DiaryDate" });
        migrationBuilder.CreateIndex(name: "IX_SiteDiaries_Status", table: "SiteDiaries", column: "Status");

        migrationBuilder.CreateTable(
            name: "SiteDiaryAttachments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SiteDiaryId = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                AttachedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                AttachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Caption = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SiteDiaryAttachments", x => x.Id);
                table.ForeignKey(
                    name: "FK_SiteDiaryAttachments_SiteDiaries_SiteDiaryId",
                    column: x => x.SiteDiaryId, principalTable: "SiteDiaries",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SiteDiaryAttachments_Documents_DocumentId",
                    column: x => x.DocumentId, principalTable: "Documents",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(name: "IX_SiteDiaryAttachments_SiteDiaryId", table: "SiteDiaryAttachments", column: "SiteDiaryId");
        migrationBuilder.CreateIndex(name: "IX_SiteDiaryAttachments_DocumentId", table: "SiteDiaryAttachments", column: "DocumentId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SiteDiaryAttachments");
        migrationBuilder.DropTable(name: "SiteDiaries");
    }
}
