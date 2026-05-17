using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 154 — adds <c>Tenant.BimManagerIso19650RolesJson</c> (jsonb,
/// nullable). Carries a tenant-scoped override of the deployment-wide
/// BIM-Manager grant list. Null is the no-op default (fall back to
/// appsettings <c>Authorization:BimManagerIso19650Roles</c> → <c>["K"]</c>).
/// </remarks>
public partial class AddTenantBimManagerRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BimManagerIso19650RolesJson",
            table: "Tenants",
            type: "jsonb",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "BimManagerIso19650RolesJson", table: "Tenants");
    }
}
