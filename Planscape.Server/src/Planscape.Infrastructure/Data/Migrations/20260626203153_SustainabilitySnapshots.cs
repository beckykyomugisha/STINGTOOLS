using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planscape.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SustainabilitySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SustainabilitySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CapturedBy = table.Column<string>(type: "text", nullable: false),
                    EnergyEuiKwhM2Yr = table.Column<double>(type: "double precision", nullable: false),
                    EnergySavingsPct = table.Column<double>(type: "double precision", nullable: false),
                    WaterLPersonDay = table.Column<double>(type: "double precision", nullable: false),
                    WaterSavingsPct = table.Column<double>(type: "double precision", nullable: false),
                    MaterialCarbonKgM2 = table.Column<double>(type: "double precision", nullable: false),
                    MaterialEnergyMjM2 = table.Column<double>(type: "double precision", nullable: false),
                    MaterialEnergySavingsPct = table.Column<double>(type: "double precision", nullable: false),
                    GwpReductionPct = table.Column<double>(type: "double precision", nullable: false),
                    EdgeLevel = table.Column<string>(type: "text", nullable: false),
                    EdgePassed = table.Column<bool>(type: "boolean", nullable: false),
                    OperationalCarbonKgYr = table.Column<double>(type: "double precision", nullable: false),
                    Occupancy = table.Column<int>(type: "integer", nullable: false),
                    FloorAreaM2 = table.Column<double>(type: "double precision", nullable: false),
                    SupplyMode = table.Column<string>(type: "text", nullable: false),
                    Country = table.Column<string>(type: "text", nullable: false),
                    ClimateZone = table.Column<string>(type: "text", nullable: false),
                    Rag = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SustainabilitySnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SustainabilitySnapshots_TenantId",
                table: "SustainabilitySnapshots",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SustainabilitySnapshots");
        }
    }
}
