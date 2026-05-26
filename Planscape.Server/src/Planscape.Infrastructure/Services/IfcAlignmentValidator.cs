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

        // Gap C: entity label → (x, y) for IIfcDirection entities (needed for true north resolution)
        var directionMap = new Dictionary<int, (double X, double Y)>();
        // Gap C: entity label of the true north direction referenced from the Model RepContext
        int trueNorthRef = -1;

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

                // Gap C: Collect IFCDIRECTION entities — #102=IFCDIRECTION((0.,1.));
                if (line.Contains("IFCDIRECTION"))
                {
                    var dm = Regex.Match(line,
                        @"^#(\d+)=IFCDIRECTION\(\(([0-9.\-Ee+]+),([0-9.\-Ee+]+)");
                    if (dm.Success
                        && int.TryParse(dm.Groups[1].Value, out int dlabel)
                        && double.TryParse(dm.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dx)
                        && double.TryParse(dm.Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dy))
                    {
                        directionMap[dlabel] = (dx, dy);
                    }
                }

                // Gap C: True north — extract reference from IFCGEOMETRICREPRESENTATIONCONTEXT
                if (line.Contains("IFCGEOMETRICREPRESENTATIONCONTEXT") && line.Contains("'Model'"))
                {
                    // True north is the 6th positional argument: #NNN or $ (omitted)
                    // Pattern: IFCGEOMETRICREPRESENTATIONCONTEXT(id,'Model',dim,prec,#wcs,#trueNorth)
                    var tnm = Regex.Match(line, @"IFCGEOMETRICREPRESENTATIONCONTEXT\([^,]*,'Model',[^,]*,[^,]*,#\d+,#(\d+)");
                    if (tnm.Success && int.TryParse(tnm.Groups[1].Value, out int tnLabel))
                        trueNorthRef = tnLabel;
                    // If no explicit true north ref, angle stays null (meaning aligned with CRS Y)
                }

                // IfcMapConversion — IFC4+ georeferencing block
                if (line.Contains("IFCMAPCONVERSION"))
                {
                    report.HasMapConversion = true;
                    // Pattern: IFCMAPCONVERSION(#ref, #refTarget, eastings, northings, orthoHeight, xAbscissa, xOrdinate, scale)
                    // Arguments 1-2 are entity references (#NNN), 3-8 are numeric
                    var m = Regex.Match(line,
                        @"IFCMAPCONVERSION\([^,]*,[^,]*,([0-9.\-Ee+]+),([0-9.\-Ee+]+),([0-9.\-Ee+]+),([0-9.\-Ee+]+),([0-9.\-Ee+]+),([0-9.\-Ee+]+)");
                    if (m.Success)
                    {
                        if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var e)) report.SurveyEasting = e;
                        if (double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)) report.SurveyNorthing = n;
                        if (double.TryParse(m.Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)) report.SurveyElevation = h;
                        // XAxisAbscissa + XAxisOrdinate define the CRS→project rotation
                        if (double.TryParse(m.Groups[4].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var xa)
                            && double.TryParse(m.Groups[5].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var xo))
                        {
                            report.MapConversionRotationDeg = Math.Atan2(xo, xa) * 180.0 / Math.PI;
                            // If no explicit TrueNorth direction was found, derive it from map conversion rotation
                            report.TrueNorthDegrees ??= report.MapConversionRotationDeg;
                        }
                        // Gap D: scale factor — should be 1.0 for well-configured models
                        if (double.TryParse(m.Groups[6].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale))
                            report.MapConversionScale = scale;
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

        // Gap C: resolve true north direction reference → angle in degrees
        if (trueNorthRef >= 0 && directionMap.TryGetValue(trueNorthRef, out var tnDir))
        {
            // IFC convention: TrueNorth direction = north direction in XY plane
            // Angle from Y axis (north), clockwise positive: atan2(X, Y) gives degrees west of north
            report.TrueNorthDegrees = Math.Atan2(tnDir.X, tnDir.Y) * 180.0 / Math.PI;
        }
        // else: leave TrueNorthDegrees as whatever was set (or null if not set by the map conversion path)

        // Validation rules
        if (string.IsNullOrEmpty(report.SchemaVersion))
            findings.Add(new("WARN", "NO_SCHEMA", "IFC schema version not detected", "Re-export with proper FILE_SCHEMA header."));

        if (string.IsNullOrEmpty(report.LengthUnit))
            findings.Add(new("WARN", "NO_UNIT", "Length unit not declared in IfcUnitAssignment",
                "In Revit: File > Project Information ensure project units are set. In ArchiCAD: Options > Project Preferences > Working Units."));

        if (!report.HasMapConversion)
            findings.Add(new("WARN", "NO_MAP_CONVERSION", "No IfcMapConversion georeferencing found (IFC4+)",
                "In Revit: Manage > Coordinates > Acquire/Publish > Acquire Coordinates. In ArchiCAD: Options > Project Preferences > Project Location."));

        // Gap D: scale factor check
        if (report.MapConversionScale.HasValue && Math.Abs(report.MapConversionScale.Value - 1.0) > 0.0001)
            findings.Add(new("WARN", "MAP_CONVERSION_SCALE",
                $"IfcMapConversion.Scale = {report.MapConversionScale:F6} (not 1.0). " +
                "This applies a global scale to all coordinates — usually indicates a mm↔m unit mismatch.",
                "In Revit IFC Exporter: ensure 'Export in shared coordinates' is on and units match the project CRS. " +
                "In ArchiCAD: Options > Project Preferences > Working Units must match the IFC export unit."));

        if (!report.HasProjectedCrs)
            findings.Add(new("INFO", "NO_PROJECTED_CRS", "No IfcProjectedCRS — site coordination by world coordinates not possible",
                "Declare a CRS (e.g. EPSG:27700) at export. Critical for site teams using GPS-aligned drones / total stations."));

        // Cross-model coherence check: compare against other models on the same project
        var siblings = await _db.IfcAlignmentReports.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.ProjectModelId != projectModelId)
            .ToListAsync(ct);

        // Gap I — Coordinate drift detection: compare this upload's survey origin against
        // the most recent previous report for THIS SAME MODEL. A shift > threshold means
        // the Survey Point or User Origin moved between exports.
        const double DriftWarnMm = 50.0;  // 50 mm default threshold
        const double DriftFailMm = 500.0; // 500 mm is definitely wrong

        var previousForThisModel = await _db.IfcAlignmentReports.AsNoTracking()
            .Where(r => r.ProjectId == projectId
                     && r.ProjectModelId == projectModelId
                     && r.TenantId == tenantId
                     && r.SurveyEasting.HasValue
                     && r.SurveyNorthing.HasValue)
            .OrderByDescending(r => r.ValidatedAt)
            .FirstOrDefaultAsync(ct);

        if (previousForThisModel != null
            && report.SurveyEasting.HasValue && report.SurveyNorthing.HasValue
            && previousForThisModel.SurveyEasting.HasValue && previousForThisModel.SurveyNorthing.HasValue)
        {
            // Convert survey coordinates to mm for comparison (survey coords are in metres)
            double dEasting  = (report.SurveyEasting.Value  - previousForThisModel.SurveyEasting.Value)  * 1000.0;
            double dNorthing = (report.SurveyNorthing.Value - previousForThisModel.SurveyNorthing.Value) * 1000.0;
            double dElev     = ((report.SurveyElevation ?? 0) - (previousForThisModel.SurveyElevation ?? 0)) * 1000.0;
            double drift = Math.Sqrt(dEasting * dEasting + dNorthing * dNorthing + dElev * dElev);

            if (drift > DriftFailMm)
                findings.Add(new("FAIL", "COORD_DRIFT",
                    $"Survey origin shifted {drift:F0} mm since last upload of this model " +
                    $"(ΔE={dEasting:F0} mm, ΔN={dNorthing:F0} mm, ΔZ={dElev:F0} mm). " +
                    "This will break all existing BCF topic viewpoints and clash records for this model.",
                    "Check that the Survey Point has not been moved. If intentional, reset all BCF topics " +
                    "and retrigger clash detection after upload."));
            else if (drift > DriftWarnMm)
                findings.Add(new("WARN", "COORD_DRIFT",
                    $"Survey origin shifted {drift:F0} mm since last upload " +
                    $"(ΔE={dEasting:F0} mm, ΔN={dNorthing:F0} mm, ΔZ={dElev:F0} mm). " +
                    "Small drift may be acceptable if the survey point was refined.",
                    "Verify with the surveyor that this shift is intentional. If the survey point was " +
                    "moved accidentally, restore the original shared coordinates before re-exporting."));
        }

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

            if (firstSibling.TrueNorthDegrees.HasValue && report.TrueNorthDegrees.HasValue
                && Math.Abs(firstSibling.TrueNorthDegrees.Value - report.TrueNorthDegrees.Value) > 0.5)
                findings.Add(new("WARN", "TRUE_NORTH_MISMATCH",
                    $"True north angle {report.TrueNorthDegrees:F2}° differs from reference model " +
                    $"{firstSibling.TrueNorthDegrees:F2}° (delta = {Math.Abs(report.TrueNorthDegrees.Value - firstSibling.TrueNorthDegrees.Value):F2}°). " +
                    "Models will be rotated relative to each other in the federated view.",
                    "All models must use the same true north setting. Set in Revit via Manage > Project North, " +
                    "in ArchiCAD via Options > Project Preferences > Project North."));
        }

        // Gap 9: Revit GlobalId stability check
        // Only meaningful when: (a) the authoring tool is Revit, and (b) we have a prior upload to compare against.
        // DEFERRED: requires FederatedElement.ProjectModelId column (not yet on the entity).
        // Once that's added + migrated, restore the previous-upload diff via:
        //   var previousGuids = await _db.FederatedElements.AsNoTracking()
        //       .Where(fe => fe.ProjectId == projectId && fe.ProjectModelId != projectModelId)
        //       .Select(fe => fe.IfcGuid).Distinct().ToListAsync(ct);
        // and compute the stability % against currentFileGuids as before.
        // Until then the check is a no-op so the build can publish.
        bool isRevitModel = applicationName != null &&
                            applicationName.Contains("Revit", StringComparison.OrdinalIgnoreCase);
        _ = isRevitModel;  // suppress "unused variable" once siblings/currentFileGuids check is gone

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

        // Gap I / G3 — populate centroid proxy from survey origin (m → mm).
        // When the model is georeferenced the survey point IS the canonical
        // anchor; this lets upstream drift-detection code compare centroids
        // even before a full geometry tessellation pass has run.
        if (report.SurveyEasting.HasValue)
        {
            report.GeometryCentroidX = report.SurveyEasting.Value  * 1000.0;
            report.GeometryCentroidY = (report.SurveyNorthing ?? 0) * 1000.0;
            report.GeometryCentroidZ = (report.SurveyElevation ?? 0) * 1000.0;
        }

        // Gap C: Validate against the project's canonical coordinate system.
        // When the coordinator has declared a reference origin, each uploaded model
        // must land within 10 m of it — otherwise surveyors have misconfigured their
        // export settings.
        try
        {
            var projectCrs = await _db.ProjectCoordinateSystems.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);

            if (projectCrs != null)
            {
                if (report.SurveyEasting.HasValue && projectCrs.OriginEasting.HasValue)
                {
                    double deltaE = Math.Abs(report.SurveyEasting.Value  - projectCrs.OriginEasting.Value);
                    double deltaN = Math.Abs((report.SurveyNorthing ?? 0) - (projectCrs.OriginNorthing ?? 0));
                    double deltaElev = Math.Abs((report.SurveyElevation ?? 0) - (projectCrs.OriginElevation ?? 0));

                    if (deltaE > 10.0 || deltaN > 10.0 || deltaElev > 10.0)
                    {
                        findings.Add(new("WARN", "COORD_CRS_MISMATCH",
                            $"Model survey origin ({report.SurveyEasting:F1}, {report.SurveyNorthing:F1}) " +
                            $"differs from project canonical origin ({projectCrs.OriginEasting:F1}, {projectCrs.OriginNorthing:F1}) " +
                            $"by ΔE={deltaE:F1} m, ΔN={deltaN:F1} m, ΔElev={deltaElev:F1} m — exceeds 10 m tolerance.",
                            // IfcAlignmentReport has no Source column — fall back to sniffing
                            // the authoring tool from applicationName (already populated above).
                            $"Re-export from {(applicationName != null && applicationName.Contains("ArchiCAD", StringComparison.OrdinalIgnoreCase) ? "ArchiCAD (Options > Project Preferences > Survey Point)" : "Revit (Manage > Coordinates > Acquire Coordinates)")} " +
                            $"using the project benchmark: E={projectCrs.OriginEasting:F3} m, N={projectCrs.OriginNorthing:F3} m. " +
                            $"Reference CRS: {projectCrs.CrsName ?? projectCrs.CrsEpsgCode ?? "unspecified"}."));
                    }
                }
                else if (!report.SurveyEasting.HasValue && projectCrs.OriginEasting.HasValue)
                {
                    findings.Add(new("INFO", "COORD_CRS_NO_ORIGIN",
                        "This model has no IfcMapConversion block but the project has a declared coordinate system. " +
                        "The model cannot be automatically positioned in the federated view.",
                        $"Add georeferencing to the export: target E={projectCrs.OriginEasting:F3} m, " +
                        $"N={projectCrs.OriginNorthing:F3} m, elevation={projectCrs.OriginElevation:F3} m " +
                        $"({projectCrs.CrsName ?? projectCrs.CrsEpsgCode ?? "project CRS"})."));
                }
            }
        }
        catch (Exception crsEx)
        {
            _logger.LogWarning(crsEx, "Gap C: ProjectCoordinateSystem validation failed (non-fatal).");
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
