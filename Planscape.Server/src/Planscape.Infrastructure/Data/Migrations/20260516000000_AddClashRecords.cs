using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Clash detection — adds the ClashRecord table populated by
/// <c>ClashDetectionJob</c>. Each row is one detected AABB overlap
/// between two SceneNodes from different disciplines; lifecycle
/// runs NEW → ACKNOWLEDGED → RESOLVED → CLOSED (or DISMISSED).
///
/// A unique index on (ProjectId, ClashHash) gives the detection job
/// dedup-on-re-run for free. Severity/Kind/Status are stored as ints
/// because they're <c>enum</c>-backed in <c>ClashRecord</c>.
/// </remarks>
public partial class AddClashRecords : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "ClashRecords",
            columns: t => new
            {
                Id              = t.Column<Guid>("uuid", nullable: false),
                TenantId        = t.Column<Guid>("uuid", nullable: false),
                ProjectId       = t.Column<Guid>("uuid", nullable: false),
                ClashHash       = t.Column<string>("character varying(64)", maxLength: 64, nullable: false),
                Kind            = t.Column<int>("integer", nullable: false),
                Severity        = t.Column<int>("integer", nullable: false),
                Status          = t.Column<int>("integer", nullable: false),
                ModelAId        = t.Column<Guid>("uuid", nullable: false),
                ElementAGuid    = t.Column<string>("character varying(80)", maxLength: 80, nullable: false),
                ElementAName    = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                ElementAType    = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                DisciplineA     = t.Column<string>("character varying(8)", maxLength: 8, nullable: true),
                ModelBId        = t.Column<Guid>("uuid", nullable: false),
                ElementBGuid    = t.Column<string>("character varying(80)", maxLength: 80, nullable: false),
                ElementBName    = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                ElementBType    = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                DisciplineB     = t.Column<string>("character varying(8)", maxLength: 8, nullable: true),
                DistanceMm      = t.Column<double>("double precision", nullable: false),
                CentreX         = t.Column<double>("double precision", nullable: false),
                CentreY         = t.Column<double>("double precision", nullable: false),
                CentreZ         = t.Column<double>("double precision", nullable: false),
                OverlapVolumeMm3 = t.Column<double>("double precision", nullable: false),
                LevelCode       = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                ZoneCode        = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                AssignedTo      = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                ResolutionNote  = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                IssueId         = t.Column<Guid>("uuid", nullable: true),
                BcfTopicGuid    = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                DetectedAt      = t.Column<DateTime>("timestamp with time zone", nullable: false),
                AcknowledgedAt  = t.Column<DateTime>("timestamp with time zone", nullable: true),
                ResolvedAt      = t.Column<DateTime>("timestamp with time zone", nullable: true),
                ClosedAt        = t.Column<DateTime>("timestamp with time zone", nullable: true),
                DetectedByJobId = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ClashRecords", x => x.Id);
                t.ForeignKey(
                    name: "FK_ClashRecords_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_ClashRecords_Issues_IssueId",
                    column: x => x.IssueId,
                    principalTable: "Issues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        mb.CreateIndex(
            name: "IX_ClashRecords_ProjectId_Status",
            table: "ClashRecords",
            columns: new[] { "ProjectId", "Status" });

        mb.CreateIndex(
            name: "IX_ClashRecords_ProjectId_ClashHash",
            table: "ClashRecords",
            columns: new[] { "ProjectId", "ClashHash" },
            unique: true);

        mb.CreateIndex(
            name: "IX_ClashRecords_TenantId",
            table: "ClashRecords",
            column: "TenantId");

        mb.CreateIndex(
            name: "IX_ClashRecords_IssueId",
            table: "ClashRecords",
            column: "IssueId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("ClashRecords");
    }
}
