using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// S3.1 — adds a unique constraint on
/// <c>TaggedElements(ProjectId, RevitElementId)</c> so the bulk-upsert
/// path's <c>INSERT ... ON CONFLICT (ProjectId, RevitElementId) DO UPDATE</c>
/// has a target. Existing duplicate rows would block creation; the migration
/// fails loudly so an operator can clean them up first.
/// </remarks>
public partial class AddTaggedElementUpsertKey : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateIndex(
            name: "UX_TaggedElements_ProjectId_RevitElementId",
            table: "TaggedElements",
            columns: new[] { "ProjectId", "RevitElementId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropIndex(name: "UX_TaggedElements_ProjectId_RevitElementId", table: "TaggedElements");
    }
}
