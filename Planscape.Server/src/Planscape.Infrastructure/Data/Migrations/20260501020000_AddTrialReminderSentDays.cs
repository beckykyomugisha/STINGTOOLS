using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// S1.6 — adds <c>Tenants.TrialReminderSentDays</c> (int, default 0). Bitmask
/// of which trial-expiry reminders have already been sent (bit 4 = 7-day
/// notice, bit 2 = 3-day, bit 1 = 1-day). Used by
/// <see cref="Planscape.Infrastructure.Services.TrialStateMachineJob"/>
/// to ensure each reminder fires exactly once per trial.
/// </remarks>
public partial class AddTrialReminderSentDays : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<int>(
            name: "TrialReminderSentDays",
            table: "Tenants",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn(name: "TrialReminderSentDays", table: "Tenants");
    }
}
