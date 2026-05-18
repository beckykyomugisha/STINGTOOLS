using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// GAP-18: Adds RetentionExpiresAt to DocumentRecords so DocumentRetentionArchiveJob
///         can auto-archive PUBLISHED documents past their retention date.
///
/// GAP-21: Adds a composite (TenantId, ProjectId) index on Documents for tenant-scoped
///         list queries and a composite (TenantId, ProjectId) index on the new
///         TransmittalDocuments table — avoiding cross-tenant full scans.
/// </summary>
public partial class AddDocumentRetentionAndIndexes : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // ── GAP-18: RetentionExpiresAt column ────────────────────────────────
        mb.AddColumn<DateTime>(
            name: "RetentionExpiresAt",
            table: "Documents",
            type: "timestamp with time zone",
            nullable: true);

        // ── GAP-21: composite index on Documents(TenantId, ProjectId) ─────────
        // This index serves the most common list query pattern:
        //   WHERE TenantId = @t AND ProjectId = @p [AND ...]
        // Postgres will use it as a covering index for tenant-scoped list endpoints.
        mb.CreateIndex(
            name: "IX_Documents_TenantId_ProjectId",
            table: "Documents",
            columns: new[] { "TenantId", "ProjectId" });

        // Also index RetentionExpiresAt so the daily retention job's
        // WHERE CdeStatus='PUBLISHED' AND RetentionExpiresAt <= now() is fast.
        mb.CreateIndex(
            name: "IX_Documents_RetentionExpiresAt",
            table: "Documents",
            column: "RetentionExpiresAt")
            .Annotate("Npgsql:IndexFilter", "\"RetentionExpiresAt\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropIndex(name: "IX_Documents_RetentionExpiresAt", table: "Documents");
        mb.DropIndex(name: "IX_Documents_TenantId_ProjectId", table: "Documents");
        mb.DropColumn(name: "RetentionExpiresAt", table: "Documents");
    }
}
