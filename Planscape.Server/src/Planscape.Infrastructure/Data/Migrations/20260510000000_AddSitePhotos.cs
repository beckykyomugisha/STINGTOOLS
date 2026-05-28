using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 178 — Site photo workflow. Captures photos taken on site and
/// routes them through a 5-state audience machine
/// (Internal → PendingReview → Approved → ClientPortal → Withdrawn)
/// with a separate blur+watermark worker writing redacted derivatives
/// before any client-portal exposure. See <c>SitePhoto.cs</c> for the
/// schema rationale.
/// </remarks>
public partial class AddSitePhotos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SitePhotos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                BlurStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                WatermarkApplied = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                RedactedFilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Caption = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CapturedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                DeviceId = table.Column<string>(type: "text", nullable: true),
                Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                Latitude = table.Column<double>(type: "double precision", nullable: true),
                Longitude = table.Column<double>(type: "double precision", nullable: true),
                AccuracyM = table.Column<double>(type: "double precision", nullable: true),
                LevelCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                ZoneCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                WorkPackageId = table.Column<Guid>(type: "uuid", nullable: true),
                AnchorIssueId = table.Column<Guid>(type: "uuid", nullable: true),
                AnchorElementGuid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                ModelId = table.Column<Guid>(type: "uuid", nullable: true),
                ModelX = table.Column<double>(type: "double precision", nullable: true),
                ModelY = table.Column<double>(type: "double precision", nullable: true),
                ModelZ = table.Column<double>(type: "double precision", nullable: true),
                ClassifierConfidence = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                ClassifierSignals = table.Column<string>(type: "jsonb", nullable: true),
                PairKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RejectedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                RejectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                WithdrawnAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                WithdrawnByUserId = table.Column<Guid>(type: "uuid", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SitePhotos", x => x.Id);
                table.ForeignKey(
                    name: "FK_SitePhotos_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SitePhotos_Documents_DocumentId",
                    column: x => x.DocumentId,
                    principalTable: "Documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SitePhotos_Users_CapturedByUserId",
                    column: x => x.CapturedByUserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_SitePhotos_Users_ApprovedByUserId",
                    column: x => x.ApprovedByUserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_SitePhotos_Issues_AnchorIssueId",
                    column: x => x.AnchorIssueId,
                    principalTable: "Issues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(name: "IX_SitePhotos_ProjectId",                          table: "SitePhotos", column: "ProjectId");
        migrationBuilder.CreateIndex(name: "IX_SitePhotos_ProjectId_Audience",                 table: "SitePhotos", columns: new[] { "ProjectId", "Audience" });
        migrationBuilder.CreateIndex(name: "IX_SitePhotos_ProjectId_Reason",                   table: "SitePhotos", columns: new[] { "ProjectId", "Reason" });
        migrationBuilder.CreateIndex(name: "IX_SitePhotos_CapturedAt",                         table: "SitePhotos", column: "CapturedAt");
        migrationBuilder.CreateIndex(name: "IX_SitePhotos_PairKey",                            table: "SitePhotos", column: "PairKey");
        migrationBuilder.CreateIndex(name: "IX_SitePhotos_AnchorElementGuid",                  table: "SitePhotos", column: "AnchorElementGuid");
        migrationBuilder.CreateIndex(name: "IX_SitePhotos_DocumentId",                         table: "SitePhotos", column: "DocumentId");
        migrationBuilder.CreateIndex(name: "IX_SitePhotos_CapturedByUserId",                   table: "SitePhotos", column: "CapturedByUserId");
        migrationBuilder.CreateIndex(name: "IX_SitePhotos_ApprovedByUserId",                   table: "SitePhotos", column: "ApprovedByUserId");
        migrationBuilder.CreateIndex(name: "IX_SitePhotos_AnchorIssueId",                      table: "SitePhotos", column: "AnchorIssueId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SitePhotos");
    }
}
