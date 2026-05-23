using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 178 — flag for ProjectModel rows whose StoragePath points at a file
/// that's no longer on disk. Set when the /file endpoint discovers the gap
/// (e.g. after a container rebuild without the persistent volume), cleared
/// on the next successful republish. Lets the viewer surface an actionable
/// "Republish from Revit" CTA instead of a generic 404.
/// </remarks>
public partial class AddProjectModelStorageMissingAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "StorageMissingAt",
            table: "ProjectModels",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StorageMissingAt", table: "ProjectModels");
    }
}
