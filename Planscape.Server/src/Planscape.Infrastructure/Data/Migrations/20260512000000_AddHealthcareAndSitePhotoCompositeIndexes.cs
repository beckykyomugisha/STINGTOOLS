using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 178b — Composite indexes surfaced by the post-merge perf review:
///   - Healthcare Pack dashboard hot-path filters by (ProjectId, CapturedAt)
///     and (ProjectId, Pass) — without composites the queries fall back
///     to ProjectId index scans + in-memory date filtering.
///   - SitePhoto pair-lookups filter by both ProjectId and PairKey;
///     compound index avoids the second-pass ProjectId predicate.
///
/// All additive — no data shape change. Down() drops the new indexes.
/// </remarks>
public partial class AddHealthcareAndSitePhotoCompositeIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_HealthcarePressureLogs_ProjectId_CapturedAt",
            table: "HealthcarePressureLogs",
            columns: new[] { "ProjectId", "CapturedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_HealthcarePressureLogs_ProjectId_RoomBimId",
            table: "HealthcarePressureLogs",
            columns: new[] { "ProjectId", "RoomBimId" });
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareMgasVerifications_ProjectId_CapturedAt",
            table: "HealthcareMgasVerifications",
            columns: new[] { "ProjectId", "CapturedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareAntiLigatureAudits_ProjectId_CapturedAt",
            table: "HealthcareAntiLigatureAudits",
            columns: new[] { "ProjectId", "CapturedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareAntiLigatureAudits_ProjectId_Pass",
            table: "HealthcareAntiLigatureAudits",
            columns: new[] { "ProjectId", "Pass" });
        migrationBuilder.CreateIndex(
            name: "IX_HealthcareRdsSnapshots_ProjectId_RoomBimId",
            table: "HealthcareRdsSnapshots",
            columns: new[] { "ProjectId", "RoomBimId" });
        migrationBuilder.CreateIndex(
            name: "IX_SitePhotos_ProjectId_PairKey",
            table: "SitePhotos",
            columns: new[] { "ProjectId", "PairKey" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_HealthcarePressureLogs_ProjectId_CapturedAt", table: "HealthcarePressureLogs");
        migrationBuilder.DropIndex(name: "IX_HealthcarePressureLogs_ProjectId_RoomBimId",  table: "HealthcarePressureLogs");
        migrationBuilder.DropIndex(name: "IX_HealthcareMgasVerifications_ProjectId_CapturedAt", table: "HealthcareMgasVerifications");
        migrationBuilder.DropIndex(name: "IX_HealthcareAntiLigatureAudits_ProjectId_CapturedAt", table: "HealthcareAntiLigatureAudits");
        migrationBuilder.DropIndex(name: "IX_HealthcareAntiLigatureAudits_ProjectId_Pass", table: "HealthcareAntiLigatureAudits");
        migrationBuilder.DropIndex(name: "IX_HealthcareRdsSnapshots_ProjectId_RoomBimId", table: "HealthcareRdsSnapshots");
        migrationBuilder.DropIndex(name: "IX_SitePhotos_ProjectId_PairKey", table: "SitePhotos");
    }
}
