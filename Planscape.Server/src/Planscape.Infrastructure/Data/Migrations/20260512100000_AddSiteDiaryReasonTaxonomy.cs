using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 178b — Extend SiteDiary with the SitePhoto Reason taxonomy
/// (Progress | Issue | Defect | Safety | AsBuilt | Reference) plus a
/// link back to any auto-created BimIssue at submit time. Diaries with
/// Reason = Defect mint an NCR; Reason = Safety mints a high-priority
/// SAFETY issue with a 4h SLA. Default "Reference" preserves prior
/// behaviour for older mobile builds that don't post a Reason field.
/// </remarks>
public partial class AddSiteDiaryReasonTaxonomy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Reason",
            table: "SiteDiaries",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Reference");
        migrationBuilder.AddColumn<Guid>(
            name: "AutoCreatedIssueId",
            table: "SiteDiaries",
            type: "uuid",
            nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_SiteDiaries_AutoCreatedIssueId",
            table: "SiteDiaries",
            column: "AutoCreatedIssueId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_SiteDiaries_AutoCreatedIssueId", table: "SiteDiaries");
        migrationBuilder.DropColumn(name: "AutoCreatedIssueId", table: "SiteDiaries");
        migrationBuilder.DropColumn(name: "Reason", table: "SiteDiaries");
    }
}
