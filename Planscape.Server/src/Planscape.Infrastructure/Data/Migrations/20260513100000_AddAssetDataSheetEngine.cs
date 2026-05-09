using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// T4-28 — Generic Asset Data Sheet engine. Generalises the Healthcare
/// Pack RDS pattern: any tenant defines a JSON-schema template, then
/// instances populate it against a BIM anchor (Room / Element / Asset
/// / System / Project). Templates are versioned so a template edit
/// doesn't retroactively invalidate existing sheets.
/// </remarks>
public partial class AddAssetDataSheetEngine : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AssetDataSheetTemplates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                AnchorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetDataSheetTemplates", x => x.Id);
                table.ForeignKey(
                    name: "FK_AssetDataSheetTemplates_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AssetDataSheets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                TemplateVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                AnchorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AnchorKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                ValuesJson = table.Column<string>(type: "jsonb", nullable: false),
                CompletenessPct = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                RejectedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssetDataSheets", x => x.Id);
                table.ForeignKey(
                    name: "FK_AssetDataSheets_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AssetDataSheets_Users_AuthorUserId",
                    column: x => x.AuthorUserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(name: "IX_AssetDataSheetTemplates_TenantId_Code",
            table: "AssetDataSheetTemplates", columns: new[] { "TenantId", "Code" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_AssetDataSheetTemplates_TenantId_IsActive",
            table: "AssetDataSheetTemplates", columns: new[] { "TenantId", "IsActive" });
        migrationBuilder.CreateIndex(name: "IX_AssetDataSheets_ProjectId_AnchorKind_AnchorKey",
            table: "AssetDataSheets", columns: new[] { "ProjectId", "AnchorKind", "AnchorKey" });
        migrationBuilder.CreateIndex(name: "IX_AssetDataSheets_ProjectId_Status",
            table: "AssetDataSheets", columns: new[] { "ProjectId", "Status" });
        migrationBuilder.CreateIndex(name: "IX_AssetDataSheets_TemplateId",
            table: "AssetDataSheets", column: "TemplateId");
        migrationBuilder.CreateIndex(name: "IX_AssetDataSheets_AuthorUserId",
            table: "AssetDataSheets", column: "AuthorUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AssetDataSheets");
        migrationBuilder.DropTable(name: "AssetDataSheetTemplates");
    }
}
