using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// MODEL-VIEWER — ProjectModel table + BimIssue 3D-anchor columns.
/// </remarks>
public partial class AddProjectModelsAndIssueAnchors : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Model storage table.
        migrationBuilder.CreateTable(
            name: "ProjectModels",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                Format = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                StoragePath = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                FileSizeBytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                ThumbnailPath = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                ElementMapPath = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                ElementCount = table.Column<int>(type: "integer", nullable: true),
                BoundsMinX = table.Column<double>(type: "double precision", nullable: true),
                BoundsMinY = table.Column<double>(type: "double precision", nullable: true),
                BoundsMinZ = table.Column<double>(type: "double precision", nullable: true),
                BoundsMaxX = table.Column<double>(type: "double precision", nullable: true),
                BoundsMaxY = table.Column<double>(type: "double precision", nullable: true),
                BoundsMaxZ = table.Column<double>(type: "double precision", nullable: true),
                Units = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "mm"),
                Revision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectModels", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectModels_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ProjectModels_Users_UploadedByUserId",
                    column: x => x.UploadedByUserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(name: "IX_ProjectModels_ProjectId", table: "ProjectModels", column: "ProjectId");
        migrationBuilder.CreateIndex(name: "IX_ProjectModels_ContentHash", table: "ProjectModels", column: "ContentHash");
        migrationBuilder.CreateIndex(name: "IX_ProjectModels_UploadedByUserId", table: "ProjectModels", column: "UploadedByUserId");

        // 2. BimIssue 3D anchor columns.
        migrationBuilder.AddColumn<Guid>(name: "ModelId", table: "Issues", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ModelElementGuid", table: "Issues", type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<double>(name: "ModelX", table: "Issues", type: "double precision", nullable: true);
        migrationBuilder.AddColumn<double>(name: "ModelY", table: "Issues", type: "double precision", nullable: true);
        migrationBuilder.AddColumn<double>(name: "ModelZ", table: "Issues", type: "double precision", nullable: true);

        migrationBuilder.CreateIndex(name: "IX_Issues_ModelId", table: "Issues", column: "ModelId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Issues_ModelId", table: "Issues");
        migrationBuilder.DropColumn(name: "ModelZ", table: "Issues");
        migrationBuilder.DropColumn(name: "ModelY", table: "Issues");
        migrationBuilder.DropColumn(name: "ModelX", table: "Issues");
        migrationBuilder.DropColumn(name: "ModelElementGuid", table: "Issues");
        migrationBuilder.DropColumn(name: "ModelId", table: "Issues");
        migrationBuilder.DropTable(name: "ProjectModels");
    }
}
