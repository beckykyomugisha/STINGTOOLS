using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Adds SenderUserId to Transmittals so Acknowledge and Respond endpoints
/// can push back to the sender via a FK lookup rather than a fragile
/// DisplayName string match (which breaks if a user's display name changes
/// or is shared by two accounts in the same tenant).
/// </summary>
public partial class AddTransmittalSenderUserId : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<Guid>(
            name: "SenderUserId",
            table: "Transmittals",
            type: "uuid",
            nullable: true);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn(name: "SenderUserId", table: "Transmittals");
    }
}
