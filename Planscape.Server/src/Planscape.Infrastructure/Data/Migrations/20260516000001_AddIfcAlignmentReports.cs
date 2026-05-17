using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// IFC alignment / georeferencing validation reports.
///
/// One row per (ProjectModel, validation pass) — written by
/// <c>IfcAlignmentValidator</c> at upload time. Captures whether the
/// uploaded IFC has consistent project units / true-north / map
/// conversion / projected CRS / IfcSite GUID relative to the project's
/// reference model. Surfaces the cross-software coordination drift
/// (ArchiCAD vs Revit) that is the single most common cause of
/// federation failure.
///
/// FindingsJson is the serialised list of <c>IfcAlignmentFinding</c>
/// records (severity / code / message / fixHint) so the viewer can
/// render a checklist with actionable fix hints.
/// </remarks>
public partial class AddIfcAlignmentReports : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "IfcAlignmentReports",
            columns: t => new
            {
                Id               = t.Column<Guid>("uuid", nullable: false),
                TenantId         = t.Column<Guid>("uuid", nullable: false),
                ProjectId        = t.Column<Guid>("uuid", nullable: false),
                ProjectModelId   = t.Column<Guid>("uuid", nullable: false),
                SchemaVersion    = t.Column<string>("character varying(20)", maxLength: 20, nullable: true),
                IfcSiteGuid      = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                LengthUnit       = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                TrueNorthDegrees = t.Column<double>("double precision", nullable: true),
                SurveyEasting    = t.Column<double>("double precision", nullable: true),
                SurveyNorthing   = t.Column<double>("double precision", nullable: true),
                SurveyElevation  = t.Column<double>("double precision", nullable: true),
                HasMapConversion = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                HasProjectedCrs  = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                CrsName          = t.Column<string>("character varying(120)", maxLength: 120, nullable: true),
                Verdict          = t.Column<string>("character varying(10)", maxLength: 10, nullable: false, defaultValue: "WARN"),
                FindingsJson     = t.Column<string>("jsonb", nullable: false, defaultValue: "[]"),
                ValidatedAt      = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t => t.PrimaryKey("PK_IfcAlignmentReports", x => x.Id));

        mb.CreateIndex(
            name: "IX_IfcAlignmentReports_ProjectId_ProjectModelId",
            table: "IfcAlignmentReports",
            columns: new[] { "ProjectId", "ProjectModelId" });

        mb.CreateIndex(
            name: "IX_IfcAlignmentReports_TenantId",
            table: "IfcAlignmentReports",
            column: "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder mb)
        => mb.DropTable(name: "IfcAlignmentReports");
}
