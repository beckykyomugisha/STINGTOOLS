using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Adds <c>WatcherUserIds</c> (JSON array of AppUser ids) to BimIssue so the
/// viewer + BCC + mobile can record a notify list per issue. Stored as text
/// (mirroring LinkedElementIds) rather than a join table because lookups are
/// always &quot;list watchers for one issue&quot; or &quot;notify these N user ids&quot;
/// — both bounded operations.
/// </remarks>
public partial class AddIssueWatchers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "WatcherUserIds",
            table: "Issues",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "WatcherUserIds", table: "Issues");
    }
}
