using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>P2 — IssueComment table for per-issue discussion threads.</remarks>
public partial class AddIssueComments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IssueComments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                MentionedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IssueComments", x => x.Id);
                table.ForeignKey(
                    name: "FK_IssueComments_Issues_IssueId",
                    column: x => x.IssueId,
                    principalTable: "Issues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_IssueComments_Users_AuthorUserId",
                    column: x => x.AuthorUserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(name: "IX_IssueComments_IssueId", table: "IssueComments", column: "IssueId");
        migrationBuilder.CreateIndex(name: "IX_IssueComments_CreatedAt", table: "IssueComments", column: "CreatedAt");
        migrationBuilder.CreateIndex(name: "IX_IssueComments_AuthorUserId", table: "IssueComments", column: "AuthorUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "IssueComments");
    }
}
