using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Phase 196 — Creates the <c>PenetrationSignoffs</c> table (mobile
/// commissioning sign-off for placed FRP / fire-damper / acoustic-seal
/// instances). The entity, its <c>DbSet</c>, model config and all four
/// controller endpoints already exist, and the table is present in
/// <c>PlanscapeDbContextModelSnapshot</c> — but there was no *creating*
/// migration, so <c>20260601000000_CrossHostIdentityFields</c> (which does
/// <c>AddColumn</c>/<c>CreateIndex</c> on this table) had no table to alter on a
/// from-empty run. This migration lands the CreateTable and is timestamped to
/// sort AFTER 20260515000000_HealthcarePack (sibling healthcare tables) and
/// BEFORE 20260601000000_CrossHostIdentityFields.
///
/// NOTE on repo convention (mirrors 20260515000000_HealthcarePack and
/// 20260601000000_CrossHostIdentityFields): these healthcare migrations are
/// hand-authored without .Designer.cs companions and carry no [Migration]
/// attribute, so EF's Migrate() does not discover them. Dev / local stacks
/// build schema from OnModelCreating via RelationalDatabaseCreator.CreateTables()
/// (Program.cs); this file is the exact DDL EF Core would emit, kept so the
/// change is covered once the prod migration pipeline is repaired (backlog
/// P3-2). The snapshot already contains this table, so no snapshot edit is
/// required.
///
/// Deliberately does NOT create the ElementIfcGlobalId column or its
/// (ProjectId, ElementIfcGlobalId) index — those belong to
/// 20260601000000_CrossHostIdentityFields, which runs immediately after this.
/// </summary>
public partial class CreatePenetrationSignoffs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PenetrationSignoffs",
            columns: table => new
            {
                Id                        = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId                  = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId                 = table.Column<Guid>(type: "uuid", nullable: false),
                PenetrationControlNumber  = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                PfvUuid                   = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                HostType                  = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                FireRating                = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                Certification             = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                ProductKind               = table.Column<string>(type: "text", nullable: false, defaultValue: "FIRESTOP"),
                InstallerName             = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                InstallerCompany          = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                InstalledAt               = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                InspectorName             = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                InspectedAt               = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Status                    = table.Column<string>(type: "text", nullable: false, defaultValue: "INSTALLED"),
                Notes                     = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                PhotoBlobId               = table.Column<string>(type: "text", nullable: true),
                GpsLat                    = table.Column<double>(type: "double precision", nullable: true),
                GpsLon                    = table.Column<double>(type: "double precision", nullable: true),
                CapturedAt                = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CapturedBy                = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PenetrationSignoffs", x => x.Id);
                table.ForeignKey(
                    name: "FK_PenetrationSignoffs_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PenetrationSignoffs_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Global ITenantScoped convention (OnModelCreating) indexes TenantId.
        migrationBuilder.CreateIndex(
            name: "IX_PenetrationSignoffs_TenantId",
            table: "PenetrationSignoffs",
            column: "TenantId");

        // Idempotency / lookup index backing PenetrationsController.Upsert +
        // GetByControlNumber, which filter on (ProjectId, ControlNumber[, Pfv]).
        // Non-unique: the upsert reads-then-adds and PfvUuid may be empty, so
        // uniqueness is not enforced at the DB layer.
        migrationBuilder.CreateIndex(
            name: "IX_PenetrationSignoffs_ProjectId_PenetrationControlNumber_PfvUuid",
            table: "PenetrationSignoffs",
            columns: new[] { "ProjectId", "PenetrationControlNumber", "PfvUuid" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PenetrationSignoffs");
    }
}
