using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 178b — T2-13. Per-user opt-in for the daily site-photo digest
/// emails (and the approver-nudge variant). Default ON to preserve
/// existing behaviour for users who already get the digest; users
/// who toggle it off via Settings → Email preferences are skipped
/// in DailyPhotoDigestJob.
/// </remarks>
public partial class AddEmailDigestOptIn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "EmailDigestEnabled",
            table: "UserNotificationPreferences",
            type: "boolean",
            nullable: false,
            defaultValue: true);
        migrationBuilder.AddColumn<int>(
            name: "EmailDigestHourUtc",
            table: "UserNotificationPreferences",
            type: "integer",
            nullable: false,
            defaultValue: 17);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "EmailDigestEnabled",  table: "UserNotificationPreferences");
        migrationBuilder.DropColumn(name: "EmailDigestHourUtc", table: "UserNotificationPreferences");
    }
}
