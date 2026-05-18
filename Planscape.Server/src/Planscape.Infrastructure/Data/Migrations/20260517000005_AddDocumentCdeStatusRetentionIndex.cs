using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Adds a compound partial index on Documents(CdeStatus, RetentionExpiresAt)
/// to speed up the DocumentRetentionArchiveJob's cross-tenant query:
///
///   WHERE CdeStatus = 'PUBLISHED'
///     AND RetentionExpiresAt IS NOT NULL
///     AND RetentionExpiresAt &lt;= now()
///
/// The existing IX_Documents_RetentionExpiresAt (single-column partial) works
/// for the job but Postgres must filter by CdeStatus in a second pass. The new
/// compound index collapses both predicates into a single index range seek and
/// is especially valuable once the Documents table grows large enough that the
/// heap access for CdeStatus becomes measurable.
/// </summary>
public partial class AddDocumentCdeStatusRetentionIndex : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateIndex(
            name: "IX_Documents_CdeStatus_RetentionExpiresAt",
            table: "Documents",
            columns: new[] { "CdeStatus", "RetentionExpiresAt" })
            .Annotate("Npgsql:IndexFilter",
                "\"RetentionExpiresAt\" IS NOT NULL AND \"CdeStatus\" = 'PUBLISHED'");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropIndex(
            name: "IX_Documents_CdeStatus_RetentionExpiresAt",
            table: "Documents");
    }
}
