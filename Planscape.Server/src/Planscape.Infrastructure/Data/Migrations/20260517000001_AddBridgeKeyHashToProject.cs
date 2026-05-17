using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Adds <c>BridgeKeyHash</c> to the Projects table.
///
/// This column stores the BCrypt hash (work factor 11) of the
/// StingBridge key issued via GET /api/archicad/{id}/keygen.
/// A NULL value means no bridge has been registered yet — all
/// POST /api/archicad/{id}/push and /status calls are rejected
/// until keygen is called by an authenticated project member.
///
/// The plaintext key is never persisted; only the BCrypt hash lives here.
/// </remarks>
public partial class AddBridgeKeyHashToProject : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BridgeKeyHash",
            table: "Projects",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BridgeKeyHash",
            table: "Projects");
    }
}
