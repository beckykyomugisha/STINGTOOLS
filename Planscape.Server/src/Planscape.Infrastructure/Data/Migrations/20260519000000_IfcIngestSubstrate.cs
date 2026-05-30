using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Phase 186 — IFC ingest substrate. Creates the <c>ExternalElementMappings</c>
/// table (cross-host element identity: IFC GlobalId ↔ host-element-id, populated
/// by <c>IfcController.IngestData</c> on every POST /api/projects/{id}/ifc/data),
/// and converts the two <c>TaggedElements</c> uniqueness constraints to the
/// FILTERED form that lets Revit (RevitElementId &gt; 0) and non-Revit hosts
/// (UniqueId = IFC GlobalId) coexist in one table.
///
/// These were added to PlanscapeDbContext in Phase 186 but never had a creating
/// migration, so POST /ifc/data 500s in any environment that applies migrations
/// rather than EnsureCreated/CreateTables. This migration closes that gap.
///
/// NOTE on repo convention: this project's migrations are hand-authored without
/// .Designer.cs companions and are not discovered by EF's Migrate() (no
/// [Migration] attribute). Dev/local builds create schema from OnModelCreating
/// via RelationalDatabaseCreator.CreateTables() (Program.cs). This file matches
/// the existing hand-authored convention so the IFC ingest substrate is covered
/// once the prod migration pipeline is repaired (backlog P3-2). DDL here is the
/// exact shape EF Core would emit for the Phase 186 model changes.
/// </summary>
public partial class IfcIngestSubstrate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── TaggedElements: convert uniqueness to filtered indexes ──
        // Old: unique (ProjectId, RevitElementId) — unfiltered, from InitialCreate.
        // New: same key but only when RevitElementId > 0, plus a new filtered
        // unique on (ProjectId, UniqueId) for IFC-GlobalId-keyed non-Revit hosts.
        migrationBuilder.DropIndex(
            name: "IX_TaggedElements_ProjectId_RevitElementId",
            table: "TaggedElements");

        migrationBuilder.CreateIndex(
            name: "IX_TaggedElements_ProjectId_RevitElementId",
            table: "TaggedElements",
            columns: new[] { "ProjectId", "RevitElementId" },
            unique: true,
            filter: "\"RevitElementId\" > 0");

        migrationBuilder.CreateIndex(
            name: "IX_TaggedElements_ProjectId_UniqueId",
            table: "TaggedElements",
            columns: new[] { "ProjectId", "UniqueId" },
            unique: true,
            filter: "\"UniqueId\" <> ''");

        // ── ExternalElementMappings ──
        migrationBuilder.CreateTable(
            name: "ExternalElementMappings",
            columns: table => new
            {
                Id               = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId         = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId        = table.Column<Guid>(type: "uuid", nullable: false),
                IfcGlobalId      = table.Column<string>(type: "character varying(22)",  maxLength: 22,  nullable: false),
                Host             = table.Column<string>(type: "character varying(20)",  maxLength: 20,  nullable: false),
                HostElementId    = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                HostDocumentGuid = table.Column<string>(type: "character varying(64)",  maxLength: 64,  nullable: true),
                HostDisplayLabel = table.Column<string>(type: "text", nullable: true),
                FirstSeenUtc     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenUtc      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IngestionCount   = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExternalElementMappings", x => x.Id);
                table.ForeignKey(
                    name: "FK_ExternalElementMappings_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ExternalElementMappings_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // FK index for TenantId (ProjectId is covered by the composite indexes below,
        // which all lead with ProjectId, so EF does not emit a standalone ProjectId index).
        migrationBuilder.CreateIndex(
            name: "IX_ExternalElementMappings_TenantId",
            table: "ExternalElementMappings",
            column: "TenantId");

        // Composite uniqueness: the same IFC GlobalId may appear in multiple
        // federated host documents, each with its own host-element-id.
        migrationBuilder.CreateIndex(
            name: "IX_ExternalElementMappings_ProjectId_IfcGlobalId_Host_HostDocumentGuid",
            table: "ExternalElementMappings",
            columns: new[] { "ProjectId", "IfcGlobalId", "Host", "HostDocumentGuid" },
            unique: true);

        // Cross-host lookup: issue on IfcGlobalId X → all host mappings.
        migrationBuilder.CreateIndex(
            name: "IX_ExternalElementMappings_ProjectId_IfcGlobalId",
            table: "ExternalElementMappings",
            columns: new[] { "ProjectId", "IfcGlobalId" });

        // Reverse lookup: host-element-id → IfcGlobalId.
        migrationBuilder.CreateIndex(
            name: "IX_ExternalElementMappings_ProjectId_Host_HostElementId",
            table: "ExternalElementMappings",
            columns: new[] { "ProjectId", "Host", "HostElementId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ExternalElementMappings");

        migrationBuilder.DropIndex(
            name: "IX_TaggedElements_ProjectId_UniqueId",
            table: "TaggedElements");

        migrationBuilder.DropIndex(
            name: "IX_TaggedElements_ProjectId_RevitElementId",
            table: "TaggedElements");

        // Restore the original unfiltered unique index.
        migrationBuilder.CreateIndex(
            name: "IX_TaggedElements_ProjectId_RevitElementId",
            table: "TaggedElements",
            columns: new[] { "ProjectId", "RevitElementId" },
            unique: true);
    }
}
