using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>FLEX-03 — Per-tenant branding (logo, colors, product name, email overrides).</remarks>
public partial class AddTenantBranding : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantBrandings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                AccentColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                HeaderColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                SupportEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                EmailFromName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                EmailFromAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                EmailSignature = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                DefaultLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TenantBrandings", x => x.Id);
                table.ForeignKey(
                    name: "FK_TenantBrandings_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TenantBrandings_TenantId",
            table: "TenantBrandings",
            column: "TenantId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TenantBrandings");
    }
}
