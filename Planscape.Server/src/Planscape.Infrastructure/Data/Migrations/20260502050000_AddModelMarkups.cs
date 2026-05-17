using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddModelMarkups : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "ModelMarkups",
            columns: t => new
            {
                Id            = t.Column<Guid>("uuid", nullable: false),
                TenantId      = t.Column<Guid>("uuid", nullable: false),
                ProjectId     = t.Column<Guid>("uuid", nullable: false),
                ModelId       = t.Column<Guid>("uuid", nullable: true),
                IssueId       = t.Column<Guid>("uuid", nullable: true),
                UserId        = t.Column<Guid>("uuid", nullable: true),
                Label         = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                Color         = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "#E8912D"),
                Thickness     = t.Column<float>("real", nullable: false, defaultValue: 2f),
                PolylinesJson = t.Column<string>("jsonb", nullable: false, defaultValue: "[]"),
                CreatedAt     = t.Column<DateTime>("timestamp with time zone", nullable: false),
                DeletedAt     = t.Column<DateTime>("timestamp with time zone", nullable: true),
            },
            constraints: t => t.PrimaryKey("PK_ModelMarkups", x => x.Id));
        mb.CreateIndex("IX_ModelMarkups_ProjectId", "ModelMarkups", "ProjectId");
        mb.CreateIndex("IX_ModelMarkups_IssueId",   "ModelMarkups", "IssueId");
    }

    protected override void Down(MigrationBuilder mb) => mb.DropTable("ModelMarkups");
}
