using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// S1.3 — adds the new East-Africa billing fields to <c>Tenants</c>:
/// <list type="bullet">
///   <item><c>Plan</c> (int, default 0 = Trial)</item>
///   <item><c>Currency</c> (text, default 'USD')</item>
///   <item><c>BillingCycle</c> (int, default 0 = Monthly)</item>
/// </list>
/// Backfills <c>Plan</c> from the legacy <c>Tier</c> column using the same
/// mapping as <c>BillingPlanLimits.FromLegacyTier</c>:
/// Starter→Trial · Professional→Studio · Premium→Practice · Enterprise→Enterprise.
/// </remarks>
public partial class AddBillingPlanToTenant : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<int>(
            name: "Plan",
            table: "Tenants",
            type: "integer",
            nullable: false,
            defaultValue: 0);
        mb.AddColumn<string>(
            name: "Currency",
            table: "Tenants",
            type: "character varying(3)",
            maxLength: 3,
            nullable: false,
            defaultValue: "USD");
        mb.AddColumn<int>(
            name: "BillingCycle",
            table: "Tenants",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        // Backfill Plan from legacy Tier (Starter=0, Professional=1, Premium=2, Enterprise=3
        //                                  → Trial=0,  Studio=1,        Practice=2, Enterprise=4)
        mb.Sql(@"UPDATE ""Tenants"" SET ""Plan"" = CASE ""Tier""
                    WHEN 0 THEN 0  -- Starter      → Trial
                    WHEN 1 THEN 1  -- Professional → Studio
                    WHEN 2 THEN 2  -- Premium      → Practice
                    WHEN 3 THEN 4  -- Enterprise   → Enterprise
                    ELSE 0
                 END;");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn(name: "BillingCycle", table: "Tenants");
        mb.DropColumn(name: "Currency",     table: "Tenants");
        mb.DropColumn(name: "Plan",         table: "Tenants");
    }
}
