using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// HC-21 — Creates the HealthcareWaterLogs table for HTM 04-01 sentinel-flush
/// capture. Documentation-DDL companion to the HealthcareWaterLog entity, in the
/// same style as 20260515000000_HealthcarePack (no [Migration] attribute; dev
/// builds schema from the model via EnsureCreated, prod regenerates the set).
/// Ordered after the existing healthcare migrations.
/// </summary>
public partial class HealthcareWaterLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "HealthcareWaterLogs",
            columns: table => new
            {
                Id              = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId        = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId       = table.Column<Guid>(type: "uuid", nullable: false),
                RoomBimId       = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                RoomIfcGlobalId = table.Column<string>(type: "character varying(22)",  maxLength: 22,  nullable: true),
                RoomName        = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false, defaultValue: ""),
                OutletId        = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                FlushType       = table.Column<string>(type: "character varying(40)",  maxLength: 40,  nullable: false, defaultValue: "SENTINEL"),
                TemperatureC    = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                DurationSec     = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                CapturedAt      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CapturedBy      = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                Source          = table.Column<string>(type: "character varying(20)",  maxLength: 20,  nullable: false, defaultValue: "MANUAL"),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HealthcareWaterLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_HealthcareWaterLogs_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HealthcareWaterLogs_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_HealthcareWaterLogs_TenantId",
            table: "HealthcareWaterLogs",
            column: "TenantId");
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareWaterLogs_ProjectId",
            table: "HealthcareWaterLogs",
            column: "ProjectId");
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareWaterLogs_ProjectId_CapturedAt",
            table: "HealthcareWaterLogs",
            columns: new[] { "ProjectId", "CapturedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareWaterLogs_ProjectId_RoomBimId",
            table: "HealthcareWaterLogs",
            columns: new[] { "ProjectId", "RoomBimId" });
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareWaterLogs_ProjectId_RoomIfcGlobalId",
            table: "HealthcareWaterLogs",
            columns: new[] { "ProjectId", "RoomIfcGlobalId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "HealthcareWaterLogs");
    }
}
