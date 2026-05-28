using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Adds <c>ResolvedBy</c> (nullable text) to BimIssue so automated resolvers
/// (e.g. the P6 live-link auto-resolve path) and manual resolvers can be
/// identified independently from <c>ResolvedAt</c>.
/// </remarks>
public partial class AddBimIssueResolvedBy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ResolvedBy",
            table: "Issues",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ResolvedBy", table: "Issues");
    }
}
