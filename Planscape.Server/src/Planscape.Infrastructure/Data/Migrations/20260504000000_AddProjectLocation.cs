using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 169 — adds project location + cover image + pin flag so the web
/// dashboard can render the ACC-style project card grid plus the
/// Mapbox-backed project location map. All fields are nullable except
/// <c>IsPinned</c> which defaults to false so existing projects keep
/// working without a backfill.
/// </remarks>
public partial class AddProjectLocation : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<double>(
            name: "Latitude",
            table: "Projects",
            type: "double precision",
            nullable: true);

        mb.AddColumn<double>(
            name: "Longitude",
            table: "Projects",
            type: "double precision",
            nullable: true);

        mb.AddColumn<string>(
            name: "City",
            table: "Projects",
            type: "text",
            nullable: true);

        mb.AddColumn<string>(
            name: "Country",
            table: "Projects",
            type: "text",
            nullable: true);

        mb.AddColumn<string>(
            name: "CoverImageUrl",
            table: "Projects",
            type: "text",
            nullable: true);

        mb.AddColumn<bool>(
            name: "IsPinned",
            table: "Projects",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn(name: "Latitude", table: "Projects");
        mb.DropColumn(name: "Longitude", table: "Projects");
        mb.DropColumn(name: "City", table: "Projects");
        mb.DropColumn(name: "Country", table: "Projects");
        mb.DropColumn(name: "CoverImageUrl", table: "Projects");
        mb.DropColumn(name: "IsPinned", table: "Projects");
    }
}
