using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// HC-06 — Creates the four Healthcare Pack tables for pressure logs,
/// MGPS verifications, anti-ligature audits, and room-data-sheet snapshots.
/// These tables are already defined in PlanscapeDbContext (Healthcare entities
/// introduced with the Healthcare Pack) but lacked a creating migration.
/// </summary>
public partial class HealthcarePack : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── HealthcarePressureLogs ──
        migrationBuilder.CreateTable(
            name: "HealthcarePressureLogs",
            columns: table => new
            {
                Id            = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId      = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId     = table.Column<Guid>(type: "uuid", nullable: false),
                RoomBimId     = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                RoomName      = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false, defaultValue: ""),
                RoomClass     = table.Column<string>(type: "character varying(80)",  maxLength: 80,  nullable: false, defaultValue: ""),
                DesignRegime  = table.Column<string>(type: "character varying(20)",  maxLength: 20,  nullable: false, defaultValue: ""),
                DesignDeltaPa = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                LiveDeltaPa   = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                InBand        = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                CapturedAt    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CapturedBy    = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                Source        = table.Column<string>(type: "character varying(20)",  maxLength: 20,  nullable: false, defaultValue: "MANUAL"),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HealthcarePressureLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_HealthcarePressureLogs_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HealthcarePressureLogs_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_HealthcarePressureLogs_TenantId",
            table: "HealthcarePressureLogs",
            column: "TenantId");
        migrationBuilder.CreateIndex(
            name: "IX_HealthcarePressureLogs_ProjectId",
            table: "HealthcarePressureLogs",
            column: "ProjectId");
        migrationBuilder.CreateIndex(
            name: "IX_HealthcarePressureLogs_ProjectId_CapturedAt",
            table: "HealthcarePressureLogs",
            columns: new[] { "ProjectId", "CapturedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_HealthcarePressureLogs_ProjectId_RoomBimId",
            table: "HealthcarePressureLogs",
            columns: new[] { "ProjectId", "RoomBimId" });

        // ── HealthcareMgasVerifications ──
        migrationBuilder.CreateTable(
            name: "HealthcareMgasVerifications",
            columns: table => new
            {
                Id                    = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId              = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId             = table.Column<Guid>(type: "uuid", nullable: false),
                Zone                  = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: ""),
                GasCode               = table.Column<string>(type: "character varying(20)",  maxLength: 20,  nullable: false, defaultValue: ""),
                VerifierName          = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                VerifierAsse6030Id    = table.Column<string>(type: "character varying(50)",  maxLength: 50,  nullable: false, defaultValue: ""),
                CertReference         = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: ""),
                CapturedAt            = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                OverallPass           = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                PassCount             = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                FailCount             = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                CheckResultsJson      = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                Notes                 = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HealthcareMgasVerifications", x => x.Id);
                table.ForeignKey(
                    name: "FK_HealthcareMgasVerifications_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HealthcareMgasVerifications_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_HealthcareMgasVerifications_TenantId",
            table: "HealthcareMgasVerifications",
            column: "TenantId");
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareMgasVerifications_ProjectId",
            table: "HealthcareMgasVerifications",
            column: "ProjectId");
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareMgasVerifications_ProjectId_CapturedAt",
            table: "HealthcareMgasVerifications",
            columns: new[] { "ProjectId", "CapturedAt" });

        // ── HealthcareAntiLigatureAudits ──
        migrationBuilder.CreateTable(
            name: "HealthcareAntiLigatureAudits",
            columns: table => new
            {
                Id          = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId    = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId   = table.Column<Guid>(type: "uuid", nullable: false),
                RoomBimId   = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                RoomName    = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false, defaultValue: ""),
                FittingType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: ""),
                Pass        = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                Notes       = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                PhotoBlobId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                GpsLat      = table.Column<double>(type: "double precision", nullable: true),
                GpsLon      = table.Column<double>(type: "double precision", nullable: true),
                CapturedAt  = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CapturedBy  = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HealthcareAntiLigatureAudits", x => x.Id);
                table.ForeignKey(
                    name: "FK_HealthcareAntiLigatureAudits_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HealthcareAntiLigatureAudits_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_HealthcareAntiLigatureAudits_TenantId",
            table: "HealthcareAntiLigatureAudits",
            column: "TenantId");
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareAntiLigatureAudits_ProjectId",
            table: "HealthcareAntiLigatureAudits",
            column: "ProjectId");
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareAntiLigatureAudits_ProjectId_CapturedAt",
            table: "HealthcareAntiLigatureAudits",
            columns: new[] { "ProjectId", "CapturedAt" });

        // ── HealthcareRdsSnapshots ──
        migrationBuilder.CreateTable(
            name: "HealthcareRdsSnapshots",
            columns: table => new
            {
                Id           = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId     = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId    = table.Column<Guid>(type: "uuid", nullable: false),
                RoomBimId    = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                RoomNumber   = table.Column<string>(type: "character varying(50)",  maxLength: 50,  nullable: false, defaultValue: ""),
                RoomName     = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false, defaultValue: ""),
                RoomClass    = table.Column<string>(type: "character varying(80)",  maxLength: 80,  nullable: false, defaultValue: ""),
                HbnRef       = table.Column<string>(type: "character varying(50)",  maxLength: 50,  nullable: false, defaultValue: ""),
                AdbCode      = table.Column<string>(type: "character varying(50)",  maxLength: 50,  nullable: false, defaultValue: ""),
                CapturedAt   = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ContextJson  = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                DocxRelPath  = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: ""),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HealthcareRdsSnapshots", x => x.Id);
                table.ForeignKey(
                    name: "FK_HealthcareRdsSnapshots_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HealthcareRdsSnapshots_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_HealthcareRdsSnapshots_TenantId",
            table: "HealthcareRdsSnapshots",
            column: "TenantId");
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareRdsSnapshots_ProjectId",
            table: "HealthcareRdsSnapshots",
            column: "ProjectId");
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareRdsSnapshots_ProjectId_RoomBimId",
            table: "HealthcareRdsSnapshots",
            columns: new[] { "ProjectId", "RoomBimId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "HealthcarePressureLogs");
        migrationBuilder.DropTable(name: "HealthcareMgasVerifications");
        migrationBuilder.DropTable(name: "HealthcareAntiLigatureAudits");
        migrationBuilder.DropTable(name: "HealthcareRdsSnapshots");
    }
}
