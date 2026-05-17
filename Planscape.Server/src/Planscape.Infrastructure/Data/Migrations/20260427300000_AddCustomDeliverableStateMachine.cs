using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 145 — adds <c>Project.CustomDeliverableStateMachineJson</c> (jsonb,
/// nullable) so a tenant can override the canonical ISO 19650 6-state
/// information-state machine on a per-project basis without requiring code
/// changes. Null means use the built-in flow.
/// </remarks>
public partial class AddCustomDeliverableStateMachine : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CustomDeliverableStateMachineJson",
            table: "Projects",
            type: "jsonb",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CustomDeliverableStateMachineJson", table: "Projects");
    }
}
