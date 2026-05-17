using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Phase 175 audit P1-15 — track antivirus scan state on document
/// uploads. Files uploaded via the new presigned-URL flow are
/// PENDING until the AV scanner job picks them up; multipart uploads
/// through the API stay at SKIPPED (legacy compat). Index on
/// (ProjectId, ScanStatus) so the scanner job's poll query is cheap.
/// </summary>
public partial class AddDocumentScanStatus : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<string>(
            name: "ScanStatus",
            table: "Documents",
            type: "text",
            nullable: false,
            defaultValue: "SKIPPED");

        mb.AddColumn<DateTime>(
            name: "ScanScannedAt",
            table: "Documents",
            type: "timestamp with time zone",
            nullable: true);

        mb.AddColumn<string>(
            name: "ScanThreatName",
            table: "Documents",
            type: "text",
            nullable: true);

        mb.CreateIndex(
            name: "IX_Documents_ScanStatus_Pending",
            table: "Documents",
            column: "ScanStatus",
            filter: "\"ScanStatus\" = 'PENDING'");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropIndex(name: "IX_Documents_ScanStatus_Pending", table: "Documents");
        mb.DropColumn(name: "ScanThreatName", table: "Documents");
        mb.DropColumn(name: "ScanScannedAt", table: "Documents");
        mb.DropColumn(name: "ScanStatus", table: "Documents");
    }
}
