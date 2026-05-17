using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 179 — Add Source column to TaggedElements:
///   * Stores the ingest origin of each element ("archicad", "revit", "ifc", …)
///   * Set by IFC ingest (IfcIngestController) and plugin sync (TagSyncController)
///   * Indexed for fast filtering by source in cost-report and compliance queries
/// </remarks>
public partial class AddTaggedElementSource : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<string>(
            name: "Source",
            table: "TaggedElements",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);

        mb.CreateIndex(
            name: "IX_TaggedElements_Source",
            table: "TaggedElements",
            column: "Source");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder mb)
    {
        mb.DropIndex(
            name: "IX_TaggedElements_Source",
            table: "TaggedElements");

        mb.DropColumn(
            name: "Source",
            table: "TaggedElements");
    }
}
