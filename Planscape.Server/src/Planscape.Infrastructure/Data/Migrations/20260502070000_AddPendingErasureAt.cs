using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddPendingErasureAt : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<DateTime>(
            name: "PendingErasureAt",
            table: "Tenants",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder mb)
        => mb.DropColumn(name: "PendingErasureAt", table: "Tenants");
}
