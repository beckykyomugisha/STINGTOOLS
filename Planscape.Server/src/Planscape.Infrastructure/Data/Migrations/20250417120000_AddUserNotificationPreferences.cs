using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>NEW-FLEX-12 — Per-user notification preferences table.</remarks>
public partial class AddUserNotificationPreferences : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UserNotificationPreferences",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                IssuesEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                ComplianceEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                RevisionsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                MeetingsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                SlaBreachesEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "all"),
                QuietHoursStart = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                QuietHoursEnd = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserNotificationPreferences", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserNotificationPreferences_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_UserNotificationPreferences_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UserNotificationPreferences_UserId",
            table: "UserNotificationPreferences",
            column: "UserId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserNotificationPreferences_TenantId",
            table: "UserNotificationPreferences",
            column: "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UserNotificationPreferences");
    }
}
