using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 180 additive — three columns supporting smart albums and the
/// per-project auto-add-on-capture rule:
///
///   * PhotoAlbums.SavedFilterJson           (jsonb)
///   * PhotoPolicies.DefaultAlbumByReasonJson (jsonb)
///   * PhotoPolicies.NdaText                  (text)
/// </remarks>
public partial class AddSmartAlbumAndAutoAddAndNdaText : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<string>(
            name: "SavedFilterJson",
            table: "PhotoAlbums",
            type: "jsonb",
            nullable: true);
        mb.AddColumn<string>(
            name: "DefaultAlbumByReasonJson",
            table: "PhotoPolicies",
            type: "jsonb",
            nullable: true);
        mb.AddColumn<string>(
            name: "NdaText",
            table: "PhotoPolicies",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn(name: "NdaText",                  table: "PhotoPolicies");
        mb.DropColumn(name: "DefaultAlbumByReasonJson", table: "PhotoPolicies");
        mb.DropColumn(name: "SavedFilterJson",          table: "PhotoAlbums");
    }
}
