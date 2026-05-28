using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 177 — adds three nullable allow-list columns to <c>ProjectMembers</c>
/// so a coordinator can be restricted per-CDE-state, per-discipline, and
/// per-suitability. Null on all three preserves the previous behaviour
/// (member sees everything in the project).
///
/// Stored as comma-separated text rather than a join table to keep the
/// JWT /me payload compact and the document query filter a single SQL
/// round-trip; the entity exposes ParseAllowList / IsAllowed helpers.
/// </remarks>
public partial class AddProjectMemberAcls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AllowedCdeStates",
            table: "ProjectMembers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AllowedDisciplines",
            table: "ProjectMembers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AllowedSuitabilities",
            table: "ProjectMembers",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AllowedCdeStates",     table: "ProjectMembers");
        migrationBuilder.DropColumn(name: "AllowedDisciplines",   table: "ProjectMembers");
        migrationBuilder.DropColumn(name: "AllowedSuitabilities", table: "ProjectMembers");
    }
}
