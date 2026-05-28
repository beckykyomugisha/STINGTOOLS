using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 151 — adds <c>Tenant.KeywordExtensionsJson</c> (jsonb,
/// nullable). Carries tenant-scoped deliverable-state-machine keyword
/// extensions sitting between platform-global appsettings and
/// per-project JSON. Null is the no-op default so existing tenants are
/// unaffected.
/// </remarks>
public partial class AddTenantKeywordExtensions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "KeywordExtensionsJson",
            table: "Tenants",
            type: "jsonb",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "KeywordExtensionsJson", table: "Tenants");
    }
}
