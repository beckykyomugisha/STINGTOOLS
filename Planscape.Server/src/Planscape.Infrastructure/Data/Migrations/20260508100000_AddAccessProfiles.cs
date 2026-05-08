using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 177-D — tenant-scoped named ACL presets so a PM can pick a profile
/// from a single dropdown when inviting a member instead of ticking three
/// orthogonal allow-lists. See <see cref="Planscape.Core.Entities.AccessProfile"/>.
/// </remarks>
public partial class AddAccessProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AccessProfiles",
            columns: table => new
            {
                Id                   = table.Column<System.Guid>(type: "uuid", nullable: false),
                TenantId             = table.Column<System.Guid>(type: "uuid", nullable: false),
                Name                 = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Description          = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                AllowedCdeStates     = table.Column<string>(type: "text", nullable: true),
                AllowedDisciplines   = table.Column<string>(type: "text", nullable: true),
                AllowedSuitabilities = table.Column<string>(type: "text", nullable: true),
                DefaultProjectRole   = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DefaultIso19650Role  = table.Column<string>(type: "character varying(8)",  maxLength:  8, nullable: false),
                IsActive             = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt            = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy            = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccessProfiles", x => x.Id);
                table.ForeignKey(
                    name: "FK_AccessProfiles_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AccessProfiles_TenantId_Name",
            table: "AccessProfiles",
            columns: new[] { "TenantId", "Name" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AccessProfiles");
    }
}
