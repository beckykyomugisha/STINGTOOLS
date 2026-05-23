using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 179.2 — Per-user, per-photo NDA acceptance log. Required for
/// the <c>PhotoAccessRule.RequiresNdaAcceptance</c> gate (Phase 179.1
/// shipped the column but didn't enforce it at fetch time).
///
/// Composite PK on (PhotoId, UserId) makes re-acceptance idempotent:
/// the controller catches the unique-violation and returns the
/// existing row.
/// </remarks>
public partial class AddPhotoNdaAcceptances : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "PhotoNdaAcceptances",
            columns: t => new
            {
                PhotoId    = t.Column<Guid>("uuid", nullable: false),
                UserId     = t.Column<Guid>("uuid", nullable: false),
                AcceptedAt = t.Column<DateTime>("timestamp with time zone", nullable: false),
                IpAddress  = t.Column<string>("character varying(64)", maxLength: 64, nullable: true),
                UserAgent  = t.Column<string>("character varying(500)", maxLength: 500, nullable: true),
                AcceptedTextSha256 = t.Column<string>("character varying(64)", maxLength: 64, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PhotoNdaAcceptances", x => new { x.PhotoId, x.UserId });
                t.ForeignKey("FK_PhotoNdaAcceptances_SitePhotos_PhotoId",
                    x => x.PhotoId, "SitePhotos", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PhotoNdaAcceptances_Users_UserId",
                    x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
            });
        mb.CreateIndex("IX_PhotoNdaAcceptances_UserId", "PhotoNdaAcceptances", "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("PhotoNdaAcceptances");
    }
}
