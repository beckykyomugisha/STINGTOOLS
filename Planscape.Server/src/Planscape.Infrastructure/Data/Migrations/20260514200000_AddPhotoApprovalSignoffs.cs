using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 180 — Multi-step approval-chain signoffs for photos governed
/// by a <see cref="PhotoPolicy.ApprovalChain"/> of TwoStepSafety or
/// TwoStepAll. Single-step chains never write to this table; the
/// existing <c>SitePhoto.ApprovedAt</c> column is still the
/// authoritative "fully approved" marker. Unique (PhotoId, Stage)
/// prevents the same approver re-signing the same stage.
/// </remarks>
public partial class AddPhotoApprovalSignoffs : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "PhotoApprovalSignoffs",
            columns: t => new
            {
                Id              = t.Column<Guid>("uuid", nullable: false),
                PhotoId         = t.Column<Guid>("uuid", nullable: false),
                Stage           = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
                SignedAt        = t.Column<DateTime>("timestamp with time zone", nullable: false),
                SignedByUserId  = t.Column<Guid>("uuid", nullable: true),
                Caption         = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Notes           = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PhotoApprovalSignoffs", x => x.Id);
                t.ForeignKey("FK_PhotoApprovalSignoffs_SitePhotos_PhotoId",
                    x => x.PhotoId, "SitePhotos", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PhotoApprovalSignoffs_Users_SignedByUserId",
                    x => x.SignedByUserId, "Users", "Id", onDelete: ReferentialAction.SetNull);
            });
        mb.CreateIndex("IX_PhotoApprovalSignoffs_PhotoId", "PhotoApprovalSignoffs", "PhotoId");
        mb.CreateIndex("IX_PhotoApprovalSignoffs_PhotoId_Stage",
            "PhotoApprovalSignoffs", new[] { "PhotoId", "Stage" }, unique: true);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("PhotoApprovalSignoffs");
    }
}
