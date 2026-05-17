using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Adds UpdatedAt to BimIssue (Issues table) for INT-10 incremental pull.
/// Run: dotnet ef database update AddBimIssueUpdatedAt
/// </remarks>
public partial class AddBimIssueUpdatedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAt",
            table: "Issues",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "now()");

        migrationBuilder.CreateIndex(
            name: "IX_Issues_UpdatedAt",
            table: "Issues",
            column: "UpdatedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Issues_UpdatedAt", table: "Issues");
        migrationBuilder.DropColumn(name: "UpdatedAt", table: "Issues");
    }
}
