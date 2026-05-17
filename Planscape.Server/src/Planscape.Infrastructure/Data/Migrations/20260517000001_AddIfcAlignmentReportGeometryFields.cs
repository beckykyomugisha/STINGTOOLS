using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Adds the five columns that were added to <c>IfcAlignmentReport</c> after the
/// original <c>AddIfcAlignmentReports</c> migration:
///
///   MapConversionScale        — Gap D: scale factor from IfcMapConversion (should be 1.0)
///   MapConversionRotationDeg  — Gap C: rotation angle derived from XAxisAbscissa + XAxisOrdinate
///   GeometryCentroidX/Y/Z     — Gap I: proxy centroid for between-upload drift detection
/// </summary>
public partial class AddIfcAlignmentReportGeometryFields : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<double>(
            name: "MapConversionScale",
            table: "IfcAlignmentReports",
            type: "double precision",
            nullable: true);

        mb.AddColumn<double>(
            name: "MapConversionRotationDeg",
            table: "IfcAlignmentReports",
            type: "double precision",
            nullable: true);

        mb.AddColumn<double>(
            name: "GeometryCentroidX",
            table: "IfcAlignmentReports",
            type: "double precision",
            nullable: true);

        mb.AddColumn<double>(
            name: "GeometryCentroidY",
            table: "IfcAlignmentReports",
            type: "double precision",
            nullable: true);

        mb.AddColumn<double>(
            name: "GeometryCentroidZ",
            table: "IfcAlignmentReports",
            type: "double precision",
            nullable: true);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn(name: "MapConversionScale",       table: "IfcAlignmentReports");
        mb.DropColumn(name: "MapConversionRotationDeg", table: "IfcAlignmentReports");
        mb.DropColumn(name: "GeometryCentroidX",        table: "IfcAlignmentReports");
        mb.DropColumn(name: "GeometryCentroidY",        table: "IfcAlignmentReports");
        mb.DropColumn(name: "GeometryCentroidZ",        table: "IfcAlignmentReports");
    }
}
