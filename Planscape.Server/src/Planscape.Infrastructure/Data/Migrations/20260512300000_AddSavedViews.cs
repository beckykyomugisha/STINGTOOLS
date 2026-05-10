using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 178b — T2-5. Saved 3D viewer states. The viewer's
/// captureViewState() returns an opaque JSON blob (camera + visibility
/// + section + active disciplines + render mode). Optionally back-
/// linked to a meeting + action item so participants can re-open
/// exact discussion context later.
/// </remarks>
public partial class AddSavedViews : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SavedViews",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ModelId = table.Column<Guid>(type: "uuid", nullable: true),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                StateJson = table.Column<string>(type: "jsonb", nullable: false),
                ThumbnailB64 = table.Column<string>(type: "text", nullable: true),
                CapturedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CapturedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LinkedMeetingId = table.Column<Guid>(type: "uuid", nullable: true),
                LinkedActionItemId = table.Column<Guid>(type: "uuid", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SavedViews", x => x.Id);
                table.ForeignKey(
                    name: "FK_SavedViews_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SavedViews_Users_CapturedByUserId",
                    column: x => x.CapturedByUserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_SavedViews_Meetings_LinkedMeetingId",
                    column: x => x.LinkedMeetingId,
                    principalTable: "Meetings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(name: "IX_SavedViews_ProjectId_CreatedAt",
            table: "SavedViews", columns: new[] { "ProjectId", "CreatedAt" });
        migrationBuilder.CreateIndex(name: "IX_SavedViews_LinkedMeetingId",
            table: "SavedViews", column: "LinkedMeetingId");
        migrationBuilder.CreateIndex(name: "IX_SavedViews_LinkedActionItemId",
            table: "SavedViews", column: "LinkedActionItemId");
        migrationBuilder.CreateIndex(name: "IX_SavedViews_CapturedByUserId",
            table: "SavedViews", column: "CapturedByUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SavedViews");
    }
}
