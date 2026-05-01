using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddPinCrdtUpdates : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "PinCrdtUpdates",
            columns: t => new
            {
                Id           = t.Column<Guid>("uuid", nullable: false),
                TenantId     = t.Column<Guid>("uuid", nullable: false),
                DocKey       = t.Column<string>("character varying(120)", maxLength: 120, nullable: false),
                UpdateBase64 = t.Column<string>("text", nullable: false),
                IsSnapshot   = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                AuthorUserId = t.Column<Guid>("uuid", nullable: true),
                CreatedAt    = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t => t.PrimaryKey("PK_PinCrdtUpdates", x => x.Id));
        mb.CreateIndex("IX_PinCrdtUpdates_DocKey_CreatedAt", "PinCrdtUpdates", new[] { "DocKey", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder mb) => mb.DropTable("PinCrdtUpdates");
}
