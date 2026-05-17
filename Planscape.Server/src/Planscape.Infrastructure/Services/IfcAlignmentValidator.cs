namespace Planscape.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed class IfcAlignmentValidator : IIfcAlignmentValidator
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<IfcAlignmentValidator> _logger;

    public IfcAlignmentValidator(PlanscapeDbContext db, ILogger<IfcAlignmentValidator> logger)
    { _db = db; _logger = logger; }

    public async Task<IfcAlignmentReport> ValidateAsync(
        string ifcPath, Guid projectId, Guid projectModelId, Guid tenantId, CancellationToken ct)
    {
        var report = new IfcAlignmentReport
        {
            TenantId = tenantId,
            ProjectId = projectId,
            ProjectModelId = projectModelId,
        };
        var findings = new List<IfcAlignmentFinding>();

        // Stream the IFC header. STEP/IFC files start with HEADER ... ENDSEC then DATA ... ENDSEC.
        // We only need a few lines to extract schema + IfcUnitAssignment + IfcGeometricRepresentationContext.
        try
        {
            using var fs = new FileStream(ifcPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            using var reader = new StreamReader(fs);

            string? line;
            int linesScanned = 0;
            bool inHeader = false, inData = false;

            // Cap the scan to first 50k lines — alignment data appears within the first 1000 typically.
            while ((line = await reader.ReadLineAsync(ct)) != null && linesScanned++ < 50_000)
            {
                if (line.StartsWith("HEADER")) inHeader = true;
                if (line.StartsWith("ENDSEC")) { inHeader = false; }
                if (line.StartsWith("DATA")) inData = true;

                // Schema version (e.g. "FILE_SCHEMA(('IFC4'));")
                if (inHeader && line.Contains("FILE_SCHEMA"))
                {
                    var m = Regex.Match(line, @"FILE_SCHEMA\s*\(\s*\(\s*'([^']+)'");
                    if (m.Success) report.SchemaVersion = m.Groups[1].Value;
                }

                if (!inData) continue;

                // IfcUnitAssignment — look for SIUNIT with LENGTHUNIT
                if (line.Contains("IFCSIUNIT") && line.Contains("LENGTHUNIT"))
                {
                    var m = Regex.Match(line, @"\.LENGTHUNIT\.,\.([A-Z]+)\.,?\.?([A-Z]*)?\.?");
                    if (m.Success)
                    {
                        var prefix = m.Groups[1].Value;       // MILLI, CENTI, etc.
                        var unit = m.Groups[2].Value;          // METRE
                        report.LengthUnit = string.IsNullOrEmpty(prefix) ? unit : $"{prefix}{unit}";
                    }
                }

                // True north — IFCDIRECTION with two values defining the project north
                if (line.Contains("IFCGEOMETRICREPRESENTATIONCONTEXT") && line.Contains("'Model'"))
                {
                    // True north is referenced by an IfcDirection — not directly inline; flag for further parsing
                    report.TrueNorthDegrees = 0.0; // Best-effort default: assume aligned to project Y
                }

                // IfcMapConversion — IFC4+ georeferencing block
                if (line.Contains("IFCMAPCONVERSION"))
                {
                    report.HasMapConversion = true;
                    // Pattern: IFCMAPCONVERSION(#ref, #refTarget, eastings, northings, orthogonal_height, x_axis_abscissa, x_axis_ordinate, scale)
                    var m = Regex.Match(line, @"IFCMAPCONVERSION\([^)]*,([0-9.\-Ee+]+),([0-9.\-Ee+]+),([0-9.\-Ee+]+)");
                    if (m.Success)
                    {
                        if (double.TryParse(m.Groups[1].Value, out var e)) report.SurveyEasting = e;
                        if (double.TryParse(m.Groups[2].Value, out var n)) report.SurveyNorthing = n;
                        if (double.TryParse(m.Groups[3].Value, out var h)) report.SurveyElevation = h;
                    }
                }

                // IfcProjectedCRS
                if (line.Contains("IFCPROJECTEDCRS"))
                {
                    report.HasProjectedCrs = true;
                    var m = Regex.Match(line, @"IFCPROJECTEDCRS\('([^']+)'");
                    if (m.Success) report.CrsName = m.Groups[1].Value;
                }

                // IfcSite GlobalId
                if (line.Contains("IFCSITE") && report.IfcSiteGuid == null)
                {
                    var m = Regex.Match(line, @"IFCSITE\('([^']+)'");
                    if (m.Success) report.IfcSiteGuid = m.Groups[1].Value;
                }
            }
        }
        catch (Exception ex)
        {
            findings.Add(new("FAIL", "PARSE_ERROR", $"Could not read IFC header: {ex.Message}",
                "Verify the file is valid STEP-format IFC (IFC2X3, IFC4, or IFC4X3)."));
            report.Verdict = "FAIL";
            report.FindingsJson = JsonSerializer.Serialize(findings);
            return report;
        }

        // Validation rules
        if (string.IsNullOrEmpty(report.SchemaVersion))
            findings.Add(new("WARN", "NO_SCHEMA", "IFC schema version not detected", "Re-export with proper FILE_SCHEMA header."));

        if (string.IsNullOrEmpty(report.LengthUnit))
            findings.Add(new("WARN", "NO_UNIT", "Length unit not declared in IfcUnitAssignment",
                "In Revit: File > Project Information ensure project units are set. In ArchiCAD: Options > Project Preferences > Working Units."));

        if (!report.HasMapConversion)
            findings.Add(new("WARN", "NO_MAP_CONVERSION", "No IfcMapConversion georeferencing found (IFC4+)",
                "In Revit: Manage > Coordinates > Acquire/Publish > Acquire Coordinates. In ArchiCAD: Options > Project Preferences > Project Location."));

        if (!report.HasProjectedCrs)
            findings.Add(new("INFO", "NO_PROJECTED_CRS", "No IfcProjectedCRS — site coordination by world coordinates not possible",
                "Declare a CRS (e.g. EPSG:27700) at export. Critical for site teams using GPS-aligned drones / total stations."));

        // Cross-model coherence check: compare against other models on the same project
        var siblings = await _db.IfcAlignmentReports.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.ProjectModelId != projectModelId)
            .ToListAsync(ct);

        if (siblings.Count > 0)
        {
            var firstSibling = siblings.First();

            if (firstSibling.LengthUnit != null && report.LengthUnit != null && firstSibling.LengthUnit != report.LengthUnit)
                findings.Add(new("FAIL", "UNIT_MISMATCH",
                    $"This model uses {report.LengthUnit} but the project's reference model uses {firstSibling.LengthUnit}. Will produce 1000× scale errors.",
                    "Re-export this model in the project's standard unit before re-uploading."));

            if (firstSibling.IfcSiteGuid != null && report.IfcSiteGuid != null && firstSibling.IfcSiteGuid != report.IfcSiteGuid)
                findings.Add(new("WARN", "SITE_GUID_MISMATCH",
                    "IfcSite GUID differs from project reference — models won't auto-align on import",
                    "Use the same shared coordinates file across all disciplines."));

            if (firstSibling.HasMapConversion && !report.HasMapConversion)
                findings.Add(new("WARN", "INCONSISTENT_GEOREF",
                    "Project reference model has IfcMapConversion but this one doesn't — coordinate alignment will fail",
                    "Acquire coordinates from the project's survey-of-record before re-exporting."));
        }

        // Determine verdict
        report.Verdict = findings.Any(f => f.Severity == "FAIL") ? "FAIL"
                       : findings.Any(f => f.Severity == "WARN") ? "WARN"
                       : "PASS";

        report.FindingsJson = JsonSerializer.Serialize(findings);

        _db.IfcAlignmentReports.Add(report);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("IFC alignment for model {ModelId}: verdict={Verdict}, findings={Count}",
            projectModelId, report.Verdict, findings.Count);

        return report;
    }
}
