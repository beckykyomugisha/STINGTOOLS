using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Adds ContractForm to BoqVariations (Phase 184q). Distinct from Kind
/// (which is the contractual route — VO / AI / CE / RFI), this field
/// stores the contract family — JCT2024 / NEC4 / FIDIC2017Red /
/// FIDIC2017Yellow / FIDIC2017Silver / GCWorks / Bespoke — so the
/// liability map can match precisely on contract family rather than
/// inferring from Kind.
///
/// Existing rows default to JCT2024 (the UK QS commonest case).
/// </summary>
public partial class AddVariationContractForm : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<string>(
            name: "ContractForm",
            table: "BoqVariations",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "JCT2024");

        mb.CreateIndex(
            name: "IX_BoqVariations_ProjectId_ContractForm",
            table: "BoqVariations",
            columns: new[] { "ProjectId", "ContractForm" });
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropIndex(name: "IX_BoqVariations_ProjectId_ContractForm", table: "BoqVariations");
        mb.DropColumn(name: "ContractForm", table: "BoqVariations");
    }
}
