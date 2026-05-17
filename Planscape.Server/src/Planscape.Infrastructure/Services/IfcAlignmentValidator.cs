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
        // Gap 9: Revit GlobalId stability — track application and GlobalIds found in this file
        string? applicationName = null;
        var currentFileGuids = new HashSet<string>(StringComparer.Ordinal);

        // Gap 10: Analytical model detection counters
        int polylineCount = 0;
        int wallCount = 0;
        string? analyticalAppShortName = null;

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

                // Gap 9 + 10: Detect authoring application
                // IFCAPPLICATION pattern: IFCAPPLICATION(#org,'version','full name','short name')
                if (applicationName == null && line.Contains("IFCAPPLICATION"))
                {
                    var m = Regex.Match(line, @"IFCAPPLICATION\([^,]*,'[^']*','([^']+)'");
                    if (m.Success) applicationName = m.Groups[1].Value;

                    // Gap 10: Check for analytical authoring tools
                    if (applicationName != null)
                    {
                        if (applicationName.Contains("ETABS", StringComparison.OrdinalIgnoreCase))
                            analyticalAppShortName = "ETABS";
                        else if (applicationName.Contains("SAP2000", StringComparison.OrdinalIgnoreCase))
                            analyticalAppShortName = "SAP2000";
                        else if (applicationName.Contains("CSi", StringComparison.OrdinalIgnoreCase))
                            analyticalAppShortName = "CSi";
                        else if (applicationName.Contains("SAFE", StringComparison.OrdinalIgnoreCase))
                            analyticalAppShortName = "SAFE";
                        else if (applicationName.Contains("RAM ", StringComparison.OrdinalIgnoreCase))
                            analyticalAppShortName = "RAM Structural";
                    }
                }

                // Gap 9: Collect 22-char IFC GlobalIds (base64-like) from the DATA section.
                // IFC GlobalIds appear as quoted 22-char alphanumeric strings at the start of entity records.
                // We harvest them from any IFC entity line (e.g. #123=IFCWALL('AbCdEfGhIjKlMnOpQrStUv',...)).
                {
                    var gm = Regex.Match(line, @"=IFC[A-Z]+\('([0-9A-Za-z_$]{22})'");
                    if (gm.Success) currentFileGuids.Add(gm.Groups[1].Value);
                }

                // Gap 10: Count IFCPOLYLINE and wall/slab entity lines for stick-model detection
                if (line.Contains("IFCPOLYLINE")) polylineCount++;
                if (line.Contains("IFCWALLSTANDARDCASE") || line.Contains("IFCWALL(") || line.Contains("IFCSLAB"))
                    wallCount++;
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

        // Gap 9: Revit GlobalId stability check
        // Only meaningful when: (a) the authoring tool is Revit, and (b) we have a prior upload to compare against.
        bool isRevitModel = applicationName != null &&
                            applicationName.Contains("Revit", StringComparison.OrdinalIgnoreCase);

        if (isRevitModel && siblings.Count > 0 && currentFileGuids.Count > 0)
        {
            // Fetch the distinct IfcGuids that were recorded for OTHER uploads of this project model
            // (previous versions / other disciplines that have been processed before).
            var previousGuids = await _db.FederatedElements
                .AsNoTracking()
                .Where(fe => fe.ProjectId == projectId && fe.ProjectModelId != projectModelId)
                .Select(fe => fe.IfcGuid)
                .Distinct()
                .ToListAsync(ct);

            if (previousGuids.Count > 0)
            {
                int missingCount = previousGuids.Count(g => !currentFileGuids.Contains(g));
                double pctMissing = (double)missingCount / previousGuids.Count * 100.0;

                if (pctMissing > 15.0)
                {
                    findings.Add(new("WARN", "REVIT_GLOBALID_INSTABILITY",
                        $"Approximately {pctMissing:F0}% of element GlobalIds changed since the last upload. " +
                        "This breaks BCF round-trips, clash history, and issue element links.",
                        "In Revit: Manage > Project Information > set STING_IFC_GUID or use the IFC Exporter with " +
                        "'Export IFC GUIDs' shared parameter. Ensure all team members use the same IFC export settings."));
                }
            }
        }

        // Gap 10: Analytical/structural analysis model detection
        // ETABS, SAP2000, CSi, SAFE, RAM Structural produce stick-geometry IFC unsuitable for coordination.
        if (analyticalAppShortName != null)
        {
            findings.Add(new("WARN", "ANALYTICAL_MODEL_DETECTED",
                $"This IFC appears to be an analytical/structural model from {analyticalAppShortName}. " +
                "Analytical stick geometry will not coordinate correctly with architectural models.",
                "Export a physical (solid geometry) model for coordination, not the analytical model. " +
                "In ETABS: File > Export > IFC > select 'Physical Model' export option."));
        }
        else if (polylineCount > 0 && wallCount == 0 && polylineCount > 50)
        {
            // Heuristic: heavy polyline usage with no wall/slab entities suggests a stick model
            // even when the application name is generic or absent.
            findings.Add(new("WARN", "POSSIBLE_STICK_MODEL",
                $"This IFC contains {polylineCount} IFCPOLYLINE entities but no wall or slab elements. " +
                "This pattern is typical of analytical stick models that cannot be used for spatial coordination.",
                "Verify this is a physical-geometry IFC. Analytical models should be replaced with a " +
                "solid-body export before uploading for coordination."));
        }
        else if (analyticalAppShortName == null && wallCount > 0 && polylineCount > wallCount * 5)
        {
            // Mixed model with disproportionate polyline count — flag as suspicious
            findings.Add(new("INFO", "HIGH_POLYLINE_RATIO",
                $"This IFC has a high ratio of curve geometry ({polylineCount} polylines) relative to " +
                $"solid elements ({wallCount} walls/slabs). Confirm this is a full physical model.",
                "If this is a combined physical+analytical export, re-export physical elements only."));
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
