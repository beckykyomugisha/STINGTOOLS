using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Adds reason / liability / reasonDetail / eotDays to BoqVariations so
/// QS reporting can pivot variations by why-they-arose and who-pays.
/// Phase 184o — mirrors the plugin-side VariationReason +
/// VariationLiability enums introduced in StingTools.Core.Variation.
/// </summary>
public partial class AddVariationReason : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<string>(
            name: "Reason",
            table: "BoqVariations",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Other");

        mb.AddColumn<string>(
            name: "Liability",
            table: "BoqVariations",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Employer");

        mb.AddColumn<string>(
            name: "ReasonDetail",
            table: "BoqVariations",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);

        mb.AddColumn<int>(
            name: "EotDays",
            table: "BoqVariations",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        mb.CreateIndex(
            name: "IX_BoqVariations_ProjectId_Reason",
            table: "BoqVariations",
            columns: new[] { "ProjectId", "Reason" });

        mb.CreateIndex(
            name: "IX_BoqVariations_ProjectId_Liability",
            table: "BoqVariations",
            columns: new[] { "ProjectId", "Liability" });
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropIndex(name: "IX_BoqVariations_ProjectId_Liability", table: "BoqVariations");
        mb.DropIndex(name: "IX_BoqVariations_ProjectId_Reason", table: "BoqVariations");
        mb.DropColumn(name: "EotDays", table: "BoqVariations");
        mb.DropColumn(name: "ReasonDetail", table: "BoqVariations");
        mb.DropColumn(name: "Liability", table: "BoqVariations");
        mb.DropColumn(name: "Reason", table: "BoqVariations");
    }
}
