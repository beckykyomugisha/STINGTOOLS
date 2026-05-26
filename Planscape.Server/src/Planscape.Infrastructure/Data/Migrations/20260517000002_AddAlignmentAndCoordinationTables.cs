using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Creates the five tables added by the coordinate-alignment and geometry-coordination
/// gap implementations (Phase 2 — Gaps A, B, Gaps 1–10):
///
///   ProjectCoordinateSystems  — Gap A: canonical CRS anchor per project
///   ProjectModelTransforms    — Gap B/E: per-model coordinate correction
///   IfcElementSnapshots       — Gap 5: per-element IFC delta tracking
///   ElementGlobalIdRegistries — Gap 9: cross-tool GlobalId reconciliation
///   ProjectLevels             — Gap 9/10: normalised level/storey table
/// </summary>
public partial class AddAlignmentAndCoordinationTables : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // ── ProjectCoordinateSystems ─────────────────────────────────────────
        mb.CreateTable(
            name: "ProjectCoordinateSystems",
            columns: t => new
            {
                Id               = t.Column<Guid>("uuid", nullable: false),
                TenantId         = t.Column<Guid>("uuid", nullable: false),
                ProjectId        = t.Column<Guid>("uuid", nullable: false),
                CrsEpsgCode      = t.Column<string>("character varying(20)",  maxLength: 20,  nullable: true),
                CrsName          = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                OriginEasting    = t.Column<double>("double precision", nullable: true),
                OriginNorthing   = t.Column<double>("double precision", nullable: true),
                OriginElevation  = t.Column<double>("double precision", nullable: true),
                TrueNorthDeg     = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                LengthUnit       = t.Column<string>("character varying(20)", maxLength: 20,  nullable: false, defaultValue: "mm"),
                ReferenceModelId = t.Column<Guid>("uuid", nullable: true),
                DefinedBy        = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                DefinedAt        = t.Column<DateTime>("timestamp with time zone", nullable: true),
                Notes            = t.Column<string>("text", nullable: true),
                CreatedAt        = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt        = t.Column<DateTime>("timestamp with time zone", nullable: true),
            },
            constraints: t => t.PrimaryKey("PK_ProjectCoordinateSystems", x => x.Id));

        mb.CreateIndex(
            name: "IX_ProjectCoordinateSystems_ProjectId",
            table: "ProjectCoordinateSystems",
            column: "ProjectId",
            unique: true);

        mb.CreateIndex(
            name: "IX_ProjectCoordinateSystems_TenantId",
            table: "ProjectCoordinateSystems",
            column: "TenantId");

        // ── ProjectModelTransforms ───────────────────────────────────────────
        mb.CreateTable(
            name: "ProjectModelTransforms",
            columns: t => new
            {
                Id             = t.Column<Guid>("uuid", nullable: false),
                TenantId       = t.Column<Guid>("uuid", nullable: false),
                ProjectId      = t.Column<Guid>("uuid", nullable: false),
                ProjectModelId = t.Column<Guid>("uuid", nullable: false),
                TranslationX   = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                TranslationY   = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                TranslationZ   = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                RotationDeg    = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                ScaleFactor    = t.Column<double>("double precision", nullable: false, defaultValue: 1.0),
                IsAutoComputed = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                IsConfirmed    = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                AppliedBy      = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                AppliedAt      = t.Column<DateTime>("timestamp with time zone", nullable: true),
                Notes          = t.Column<string>("text", nullable: true),
                CreatedAt      = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt      = t.Column<DateTime>("timestamp with time zone", nullable: true),
            },
            constraints: t => t.PrimaryKey("PK_ProjectModelTransforms", x => x.Id));

        mb.CreateIndex(
            name: "IX_ProjectModelTransforms_ProjectModelId",
            table: "ProjectModelTransforms",
            column: "ProjectModelId",
            unique: true);

        mb.CreateIndex(
            name: "IX_ProjectModelTransforms_ProjectId_TenantId",
            table: "ProjectModelTransforms",
            columns: new[] { "ProjectId", "TenantId" });

        // ── IfcElementSnapshots ──────────────────────────────────────────────
        mb.CreateTable(
            name: "IfcElementSnapshots",
            columns: t => new
            {
                Id              = t.Column<Guid>("uuid", nullable: false),
                TenantId        = t.Column<Guid>("uuid", nullable: false),
                ProjectId       = t.Column<Guid>("uuid", nullable: false),
                ProjectModelId  = t.Column<Guid>("uuid", nullable: false),
                IfcGuid         = t.Column<string>("character varying(44)", maxLength: 44, nullable: false),
                IfcType         = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                Name            = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                Storey          = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                Discipline      = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                MinX            = t.Column<double>("double precision", nullable: true),
                MinY            = t.Column<double>("double precision", nullable: true),
                MinZ            = t.Column<double>("double precision", nullable: true),
                MaxX            = t.Column<double>("double precision", nullable: true),
                MaxY            = t.Column<double>("double precision", nullable: true),
                MaxZ            = t.Column<double>("double precision", nullable: true),
                PropertiesHash  = t.Column<string>("character varying(64)", maxLength: 64, nullable: true),
                ChangeKind      = t.Column<string>("character varying(20)", maxLength: 20, nullable: true),
                SnapshotAt      = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UploadSequence  = t.Column<long>("bigint", nullable: false, defaultValue: 0L),
            },
            constraints: t => t.PrimaryKey("PK_IfcElementSnapshots", x => x.Id));

        mb.CreateIndex(
            name: "IX_IfcElementSnapshots_ProjectModelId_IfcGuid",
            table: "IfcElementSnapshots",
            columns: new[] { "ProjectModelId", "IfcGuid" });

        mb.CreateIndex(
            name: "IX_IfcElementSnapshots_ProjectId_TenantId",
            table: "IfcElementSnapshots",
            columns: new[] { "ProjectId", "TenantId" });

        // ── ElementGlobalIdRegistries ────────────────────────────────────────
        mb.CreateTable(
            name: "ElementGlobalIdRegistries",
            columns: t => new
            {
                Id                  = t.Column<Guid>("uuid", nullable: false),
                TenantId            = t.Column<Guid>("uuid", nullable: false),
                ProjectId           = t.Column<Guid>("uuid", nullable: false),
                IfcGlobalId         = t.Column<string>("character varying(44)", maxLength: 44, nullable: true),
                ArchiCadGuid        = t.Column<string>("character varying(44)", maxLength: 44, nullable: true),
                RevitUniqueId       = t.Column<string>("character varying(100)", maxLength: 100, nullable: true),
                TeklaGuid           = t.Column<string>("character varying(100)", maxLength: 100, nullable: true),
                Discipline          = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                IfcType             = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                ElementName         = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                NormalizedLevelName = t.Column<string>("character varying(100)", maxLength: 100, nullable: true),
                MappingStatus       = t.Column<string>("character varying(20)", maxLength: 20, nullable: true),
                MappedBy            = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                Notes               = t.Column<string>("text", nullable: true),
            },
            constraints: t => t.PrimaryKey("PK_ElementGlobalIdRegistries", x => x.Id));

        mb.CreateIndex(
            name: "IX_ElementGlobalIdRegistries_ProjectId_IfcGlobalId",
            table: "ElementGlobalIdRegistries",
            columns: new[] { "ProjectId", "IfcGlobalId" });

        mb.CreateIndex(
            name: "IX_ElementGlobalIdRegistries_TenantId",
            table: "ElementGlobalIdRegistries",
            column: "TenantId");

        // ── ProjectLevels ────────────────────────────────────────────────────
        mb.CreateTable(
            name: "ProjectLevels",
            columns: t => new
            {
                Id              = t.Column<Guid>("uuid", nullable: false),
                TenantId        = t.Column<Guid>("uuid", nullable: false),
                ProjectId       = t.Column<Guid>("uuid", nullable: false),
                NormalizedName  = t.Column<string>("character varying(100)", maxLength: 100, nullable: false),
                DisplayName     = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                ElevationM      = t.Column<double>("double precision", nullable: true),
                SortIndex       = t.Column<int>("integer", nullable: false, defaultValue: 0),
                ToolMappingsJson = t.Column<string>("jsonb", nullable: false, defaultValue: "{}"),
            },
            constraints: t => t.PrimaryKey("PK_ProjectLevels", x => x.Id));

        mb.CreateIndex(
            name: "IX_ProjectLevels_ProjectId_NormalizedName",
            table: "ProjectLevels",
            columns: new[] { "ProjectId", "NormalizedName" },
            unique: true);

        mb.CreateIndex(
            name: "IX_ProjectLevels_TenantId",
            table: "ProjectLevels",
            column: "TenantId");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "ProjectCoordinateSystems");
        mb.DropTable(name: "ProjectModelTransforms");
        mb.DropTable(name: "IfcElementSnapshots");
        mb.DropTable(name: "ElementGlobalIdRegistries");
        mb.DropTable(name: "ProjectLevels");
    }
}
