using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// K1 cross-host identity — adds the nullable IFC GlobalId columns that let
/// healthcare + penetration records join the cross-host identity layer:
///   • <c>HealthcarePressureLogs.RoomIfcGlobalId</c> — room/space GlobalId,
///     queried by <c>GET .../healthcare/by-ifc/{ifcGlobalId}</c>.
///   • <c>PenetrationSignoffs.ElementIfcGlobalId</c> — penetrated host element
///     GlobalId for the golden-thread record.
/// Both are nullable so existing rows + older clients are unaffected.
///
/// NOTE on repo convention (mirrors 20260519000000_IfcIngestSubstrate): this
/// project's migrations are hand-authored without .Designer.cs companions and
/// are NOT discovered by EF's Migrate() (no [Migration] attribute). Dev / local
/// stacks build schema from OnModelCreating via RelationalDatabaseCreator.
/// CreateTables() (Program.cs), so the new columns already exist there from the
/// model. This file is the exact DDL EF Core would emit, kept so the change is
/// covered once the prod migration pipeline is repaired (backlog P3-2). The
/// PenetrationSignoffs table itself still lacks a creating migration in that
/// set — the same backlog gap — so its AddColumn assumes CreateTable lands
/// first when the pipeline is rebuilt.
/// </summary>
public partial class CrossHostIdentityFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RoomIfcGlobalId",
            table: "HealthcarePressureLogs",
            type: "character varying(22)",
            maxLength: 22,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_HealthcarePressureLogs_ProjectId_RoomIfcGlobalId",
            table: "HealthcarePressureLogs",
            columns: new[] { "ProjectId", "RoomIfcGlobalId" });

        migrationBuilder.AddColumn<string>(
            name: "ElementIfcGlobalId",
            table: "PenetrationSignoffs",
            type: "character varying(22)",
            maxLength: 22,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_PenetrationSignoffs_ProjectId_ElementIfcGlobalId",
            table: "PenetrationSignoffs",
            columns: new[] { "ProjectId", "ElementIfcGlobalId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PenetrationSignoffs_ProjectId_ElementIfcGlobalId",
            table: "PenetrationSignoffs");

        migrationBuilder.DropColumn(
            name: "ElementIfcGlobalId",
            table: "PenetrationSignoffs");

        migrationBuilder.DropIndex(
            name: "IX_HealthcarePressureLogs_ProjectId_RoomIfcGlobalId",
            table: "HealthcarePressureLogs");

        migrationBuilder.DropColumn(
            name: "RoomIfcGlobalId",
            table: "HealthcarePressureLogs");
    }
}
