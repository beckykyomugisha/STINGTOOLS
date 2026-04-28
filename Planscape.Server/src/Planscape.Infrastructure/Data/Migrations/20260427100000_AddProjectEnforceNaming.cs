using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 143 — adds <c>Project.EnforceIso19650Naming</c> so a BIM Manager
/// can flip per-project enforcement of the ISO 19650 naming convention on
/// document upload. Defaults to false to keep existing projects un-affected.
/// </remarks>
public partial class AddProjectEnforceNaming : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "EnforceIso19650Naming",
            table: "Projects",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "EnforceIso19650Naming", table: "Projects");
    }
}
