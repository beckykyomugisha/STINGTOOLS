using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// FLEX-13 — Per-project custom-field schema + JSONB value column on BimIssue.
/// Adds a GIN index so queries against the CustomFields JSON stay fast as the
/// table grows.
/// </remarks>
public partial class AddIssueCustomFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Schema table — per-project field definitions.
        migrationBuilder.CreateTable(
            name: "IssueCustomFieldSchemas",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                FieldType = table.Column<int>(type: "integer", nullable: false),
                HelpText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                DefaultValueJson = table.Column<string>(type: "jsonb", nullable: true),
                OptionsJson = table.Column<string>(type: "jsonb", nullable: true),
                Required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IssueCustomFieldSchemas", x => x.Id);
                table.ForeignKey(
                    name: "FK_IssueCustomFieldSchemas_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IssueCustomFieldSchemas_ProjectId",
            table: "IssueCustomFieldSchemas",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_IssueCustomFieldSchemas_ProjectId_Key",
            table: "IssueCustomFieldSchemas",
            columns: new[] { "ProjectId", "Key" },
            unique: true);

        // 2. JSONB column on BimIssue + GIN index for jsonb path queries.
        migrationBuilder.AddColumn<string>(
            name: "CustomFields",
            table: "Issues",
            type: "jsonb",
            nullable: true);

        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Issues_CustomFields_gin"" ON ""Issues"" USING gin (""CustomFields"");");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Issues_CustomFields_gin"";");
        migrationBuilder.DropColumn(name: "CustomFields", table: "Issues");
        migrationBuilder.DropTable(name: "IssueCustomFieldSchemas");
    }
}
