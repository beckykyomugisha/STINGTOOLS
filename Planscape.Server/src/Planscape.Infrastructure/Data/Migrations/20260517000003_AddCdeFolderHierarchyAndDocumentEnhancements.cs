using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Implements five CDE gaps identified in the document-manager review:
///
///   Gap 1 — CDE folder hierarchy: creates CdeContainers (self-referencing) and
///            adds ContainerId FK to DocumentRecords.
///
///   Gap 2 — Per-member ACLs (already shipped in 20260508000000_AddProjectMemberAcls).
///
///   Gap 3 — Suitability code whitelist (server-side validation, no schema change).
///
///   Gap 4 — E-signature on S4 publication: creates DocumentSignatures and adds
///            PublishedByUserId/PublishedByName/PublishedAt columns to DocumentRecords.
///
///   Gap 5 — Transmittal version snapshot: creates TransmittalDocuments join table
///            that captures exact DocumentVersionId + CDE state at send time.
/// </summary>
public partial class AddCdeFolderHierarchyAndDocumentEnhancements : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // ── Gap 1: CdeContainers ─────────────────────────────────────────────
        mb.CreateTable(
            name: "CdeContainers",
            columns: t => new
            {
                Id                = t.Column<Guid>("uuid",                        nullable: false),
                TenantId          = t.Column<Guid>("uuid",                        nullable: false),
                ProjectId         = t.Column<Guid>("uuid",                        nullable: false),
                Name              = t.Column<string>("character varying(200)",    maxLength: 200, nullable: false),
                ParentContainerId = t.Column<Guid>("uuid",                        nullable: true),
                ContainerType     = t.Column<string>("character varying(50)",     maxLength: 50,  nullable: true),
                Discipline        = t.Column<string>("character varying(20)",     maxLength: 20,  nullable: true),
                Description       = t.Column<string>("character varying(1000)",   maxLength: 1000, nullable: true),
                SortOrder         = t.Column<int>("integer",                      nullable: false, defaultValue: 0),
                CreatedBy         = t.Column<string>("character varying(200)",    maxLength: 200, nullable: false, defaultValue: ""),
                CreatedAt         = t.Column<DateTime>("timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                UpdatedAt         = t.Column<DateTime>("timestamp with time zone", nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_CdeContainers", x => x.Id);
                t.ForeignKey(
                    name: "FK_CdeContainers_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_CdeContainers_CdeContainers_ParentContainerId",
                    column: x => x.ParentContainerId,
                    principalTable: "CdeContainers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        mb.CreateIndex(
            name: "IX_CdeContainers_TenantId_ProjectId",
            table: "CdeContainers",
            columns: new[] { "TenantId", "ProjectId" });

        mb.CreateIndex(
            name: "IX_CdeContainers_ParentContainerId",
            table: "CdeContainers",
            column: "ParentContainerId");

        // ── Gap 1: ContainerId on DocumentRecords ────────────────────────────
        mb.AddColumn<Guid>(
            name: "ContainerId",
            table: "Documents",
            type: "uuid",
            nullable: true);

        mb.CreateIndex(
            name: "IX_Documents_ContainerId",
            table: "Documents",
            column: "ContainerId");

        mb.AddForeignKey(
            name: "FK_Documents_CdeContainers_ContainerId",
            table: "Documents",
            column: "ContainerId",
            principalTable: "CdeContainers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        // ── Gap 4: Publication stamp columns on DocumentRecords ──────────────
        mb.AddColumn<string>(
            name: "PublishedByUserId",
            table: "Documents",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        mb.AddColumn<string>(
            name: "PublishedByName",
            table: "Documents",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        mb.AddColumn<DateTime>(
            name: "PublishedAt",
            table: "Documents",
            type: "timestamp with time zone",
            nullable: true);

        // ── Gap 4: DocumentSignatures ────────────────────────────────────────
        mb.CreateTable(
            name: "DocumentSignatures",
            columns: t => new
            {
                Id                  = t.Column<Guid>("uuid",                        nullable: false),
                TenantId            = t.Column<Guid>("uuid",                        nullable: false),
                ProjectId           = t.Column<Guid>("uuid",                        nullable: false),
                DocumentId          = t.Column<Guid>("uuid",                        nullable: false),
                SignedByUserId      = t.Column<string>("character varying(100)",    maxLength: 100, nullable: false, defaultValue: ""),
                SignedByName        = t.Column<string>("character varying(200)",    maxLength: 200, nullable: false, defaultValue: ""),
                SignedAt            = t.Column<DateTime>("timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                SignatureNote       = t.Column<string>("character varying(1000)",   maxLength: 1000, nullable: true),
                WatermarkedFilePath = t.Column<string>("text",                      nullable: true),
                WatermarkStatus     = t.Column<string>("character varying(20)",     maxLength: 20, nullable: false, defaultValue: "PENDING"),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_DocumentSignatures", x => x.Id);
                t.ForeignKey(
                    name: "FK_DocumentSignatures_Documents_DocumentId",
                    column: x => x.DocumentId,
                    principalTable: "Documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex(
            name: "IX_DocumentSignatures_TenantId_DocumentId",
            table: "DocumentSignatures",
            columns: new[] { "TenantId", "DocumentId" });

        mb.CreateIndex(
            name: "IX_DocumentSignatures_WatermarkStatus",
            table: "DocumentSignatures",
            column: "WatermarkStatus")
            // Partial index — only un-processed rows; keeps the scanner fast.
            .Annotation("Npgsql:IndexFilter", "\"WatermarkStatus\" = 'PENDING'");

        // ── Gap 5: TransmittalDocuments ──────────────────────────────────────
        mb.CreateTable(
            name: "TransmittalDocuments",
            columns: t => new
            {
                Id                       = t.Column<Guid>("uuid",                        nullable: false),
                TransmittalId            = t.Column<Guid>("uuid",                        nullable: false),
                DocumentId               = t.Column<Guid>("uuid",                        nullable: false),
                DocumentVersionId        = t.Column<Guid>("uuid",                        nullable: true),
                CdeStateAtTransmittal    = t.Column<string>("character varying(30)",     maxLength: 30, nullable: true),
                SuitabilityAtTransmittal = t.Column<string>("character varying(10)",     maxLength: 10, nullable: true),
                FilePathAtTransmittal    = t.Column<string>("text",                      nullable: true),
                AddedAt                  = t.Column<DateTime>("timestamp with time zone", nullable: false, defaultValueSql: "now()"),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_TransmittalDocuments", x => x.Id);
                t.ForeignKey(
                    name: "FK_TransmittalDocuments_Transmittals_TransmittalId",
                    column: x => x.TransmittalId,
                    principalTable: "Transmittals",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_TransmittalDocuments_Documents_DocumentId",
                    column: x => x.DocumentId,
                    principalTable: "Documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                t.ForeignKey(
                    name: "FK_TransmittalDocuments_DocumentVersions_DocumentVersionId",
                    column: x => x.DocumentVersionId,
                    principalTable: "DocumentVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        mb.CreateIndex(
            name: "IX_TransmittalDocuments_TransmittalId",
            table: "TransmittalDocuments",
            column: "TransmittalId");

        mb.CreateIndex(
            name: "IX_TransmittalDocuments_DocumentId",
            table: "TransmittalDocuments",
            column: "DocumentId");

        mb.CreateIndex(
            name: "IX_TransmittalDocuments_TransmittalId_DocumentId",
            table: "TransmittalDocuments",
            columns: new[] { "TransmittalId", "DocumentId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "TransmittalDocuments");

        mb.DropIndex(name: "IX_DocumentSignatures_WatermarkStatus", table: "DocumentSignatures");
        mb.DropIndex(name: "IX_DocumentSignatures_TenantId_DocumentId", table: "DocumentSignatures");
        mb.DropTable(name: "DocumentSignatures");

        mb.DropForeignKey(name: "FK_Documents_CdeContainers_ContainerId", table: "Documents");
        mb.DropIndex(name: "IX_Documents_ContainerId", table: "Documents");
        mb.DropColumn(name: "PublishedAt",       table: "Documents");
        mb.DropColumn(name: "PublishedByName",   table: "Documents");
        mb.DropColumn(name: "PublishedByUserId", table: "Documents");
        mb.DropColumn(name: "ContainerId",       table: "Documents");

        mb.DropIndex(name: "IX_CdeContainers_ParentContainerId",    table: "CdeContainers");
        mb.DropIndex(name: "IX_CdeContainers_TenantId_ProjectId",   table: "CdeContainers");
        mb.DropTable(name: "CdeContainers");
    }
}
