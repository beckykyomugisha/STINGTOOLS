namespace Planscape.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

// ── Public interface ──────────────────────────────────────────────────────────

public interface IFederatedCoherenceJob
{
    /// <summary>
    /// Gap G — Scan all uploaded models for a project and produce a
    /// FederatedCoherenceReport. Runs after each upload and on demand.
    /// Groups models by coordinate cluster (same survey origin + CRS + true north).
    /// Models in a different cluster from the reference are flagged.
    /// </summary>
    Task<FederatedCoherenceReport> RunAsync(Guid projectId, Guid tenantId, CancellationToken ct);
}

// ── Result types ──────────────────────────────────────────────────────────────

public sealed record FederatedCoherenceReport(
    Guid ProjectId,
    int ModelsScanned,
    int ModelsAligned,           // in the same cluster as reference
    int ModelsOutOfAlignment,    // different cluster
    int ModelsWithNoGeoref,      // no IfcMapConversion at all
    IReadOnlyList<CoherenceIssue> Issues,
    DateTime ScannedAt);

public sealed record CoherenceIssue(
    Guid   ModelId,
    string ModelName,
    string Kind,        // "NO_GEOREF" | "CLUSTER_MISMATCH" | "UNIT_MISMATCH" | "SCALE_ERROR" | "NORTH_MISMATCH"
    string Severity,    // "INFO" | "WARN" | "FAIL"
    string Message);

// ── Implementation ────────────────────────────────────────────────────────────

public sealed class FederatedCoherenceJob : IFederatedCoherenceJob
{
    private readonly PlanscapeDbContext         _db;
    private readonly ILogger<FederatedCoherenceJob> _logger;

    public FederatedCoherenceJob(PlanscapeDbContext db, ILogger<FederatedCoherenceJob> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<FederatedCoherenceReport> RunAsync(
        Guid projectId, Guid tenantId, CancellationToken ct)
    {
        // ── 1. Load all non-deleted models for the project ────────────────────
        var models = await _db.ProjectModels.AsNoTracking()
            .Where(m => m.ProjectId == projectId
                     && m.TenantId  == tenantId
                     && m.DeletedAt == null)
            .ToListAsync(ct);

        // ── 2. Load most-recent IfcAlignmentReport per model ─────────────────
        // Group by ProjectModelId, keep the row with the latest ValidatedAt.
        var allReports = await _db.IfcAlignmentReports.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.TenantId == tenantId)
            .ToListAsync(ct);

        var latestReportByModel = allReports
            .GroupBy(r => r.ProjectModelId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.ValidatedAt).First());

        // ── 3. Load ProjectCoordinateSystem if it exists ──────────────────────
        var pcs = await _db.Set<ProjectCoordinateSystem>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ProjectId == projectId && x.TenantId == tenantId, ct);

        // ── 4. Determine the reference model ──────────────────────────────────
        IfcAlignmentReport? refReport = null;

        if (pcs?.ReferenceModelId.HasValue == true
            && latestReportByModel.TryGetValue(pcs.ReferenceModelId.Value, out var pcsRef)
            && pcsRef.HasMapConversion)
        {
            refReport = pcsRef;
        }

        if (refReport == null)
        {
            // Fall back: most-recently-validated PASS model with HasMapConversion
            refReport = allReports
                .Where(r => r.HasMapConversion && r.Verdict == "PASS")
                .OrderByDescending(r => r.ValidatedAt)
                .FirstOrDefault();
        }

        if (refReport == null)
        {
            // Wider fallback: any model with HasMapConversion
            refReport = allReports
                .Where(r => r.HasMapConversion)
                .OrderByDescending(r => r.ValidatedAt)
                .FirstOrDefault();
        }

        // ── 5. Load confirmed transforms (used to suppress CLUSTER_MISMATCH) ──
        var confirmedTransformList = await _db.Set<ProjectModelTransform>()
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId
                     && t.TenantId  == tenantId
                     && t.IsConfirmed)
            .Select(t => t.ProjectModelId)
            .ToListAsync(ct);
        var confirmedTransforms = confirmedTransformList.ToHashSet();

        // ── 6. Scan each model ────────────────────────────────────────────────
        var issues  = new List<CoherenceIssue>();
        int aligned = 0, outOfAlign = 0, noGeoref = 0;

        foreach (var model in models)
        {
            if (!latestReportByModel.TryGetValue(model.Id, out var rep))
            {
                // No report at all
                issues.Add(new CoherenceIssue(
                    model.Id,
                    model.Name,
                    "NO_GEOREF",
                    "INFO",
                    "No alignment report found — model has not been validated yet."));
                noGeoref++;
                continue;
            }

            if (!rep.HasMapConversion)
            {
                issues.Add(new CoherenceIssue(
                    model.Id,
                    model.Name,
                    "NO_GEOREF",
                    "WARN",
                    "Model has no IfcMapConversion — survey origin is unknown."));
                noGeoref++;
                continue;
            }

            bool modelAligned = true;

            // Scale error — non-unity MapConversionScale causes geometry scaling
            if (rep.MapConversionScale.HasValue
                && Math.Abs(rep.MapConversionScale.Value - 1.0) > 1e-6)
            {
                issues.Add(new CoherenceIssue(
                    model.Id,
                    model.Name,
                    "SCALE_ERROR",
                    "WARN",
                    $"IfcMapConversion scale factor is {rep.MapConversionScale.Value:F6} (expected 1.0). " +
                    "This will cause proportional geometry errors when federated."));
                modelAligned = false;
            }

            // Unit mismatch vs reference
            if (refReport != null
                && rep.LengthUnit != null
                && refReport.LengthUnit != null
                && !string.Equals(rep.LengthUnit, refReport.LengthUnit, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new CoherenceIssue(
                    model.Id,
                    model.Name,
                    "UNIT_MISMATCH",
                    "FAIL",
                    $"Length unit '{rep.LengthUnit}' does not match reference unit '{refReport.LengthUnit}'. " +
                    "This causes a 1000× scale error in the federated viewer."));
                modelAligned = false;
            }

            // True north mismatch vs reference (> 0.5°)
            if (refReport != null
                && rep.TrueNorthDegrees.HasValue
                && refReport.TrueNorthDegrees.HasValue)
            {
                double northDiff = Math.Abs(
                    rep.TrueNorthDegrees.Value - refReport.TrueNorthDegrees.Value);
                // Normalise to [-180, 180]
                if (northDiff > 180.0) northDiff = 360.0 - northDiff;

                if (northDiff > 0.5)
                {
                    issues.Add(new CoherenceIssue(
                        model.Id,
                        model.Name,
                        "NORTH_MISMATCH",
                        "WARN",
                        $"True north angle {rep.TrueNorthDegrees.Value:F3}° differs from reference " +
                        $"{refReport.TrueNorthDegrees.Value:F3}° by {northDiff:F3}° (threshold 0.5°). " +
                        "Models will appear rotated against each other."));
                    modelAligned = false;
                }
            }

            // Cluster / survey origin mismatch vs reference
            // Skip if the model already has a confirmed manual transform
            if (refReport != null
                && rep.SurveyEasting.HasValue
                && refReport.SurveyEasting.HasValue
                && !confirmedTransforms.Contains(model.Id))
            {
                double eastDiff   = Math.Abs(rep.SurveyEasting.Value  - refReport.SurveyEasting.Value);
                double northDiff2 = Math.Abs((rep.SurveyNorthing ?? 0) - (refReport.SurveyNorthing ?? 0));
                double totalDrift = Math.Sqrt(eastDiff * eastDiff + northDiff2 * northDiff2); // metres

                if (totalDrift > 0.001) // > 1 mm
                {
                    issues.Add(new CoherenceIssue(
                        model.Id,
                        model.Name,
                        "CLUSTER_MISMATCH",
                        "WARN",
                        $"Survey origin is {totalDrift:F3} m from reference origin " +
                        $"(E: {rep.SurveyEasting.Value:F3}, N: {rep.SurveyNorthing ?? 0:F3}). " +
                        "Apply a coordinate transform via PUT /transform to align this model."));
                    modelAligned = false;
                }
            }

            if (modelAligned)
                aligned++;
            else
                outOfAlign++;
        }

        var report = new FederatedCoherenceReport(
            ProjectId          : projectId,
            ModelsScanned      : models.Count,
            ModelsAligned      : aligned,
            ModelsOutOfAlignment: outOfAlign,
            ModelsWithNoGeoref : noGeoref,
            Issues             : issues.AsReadOnly(),
            ScannedAt          : DateTime.UtcNow);

        _logger.LogInformation(
            "FederatedCoherenceJob for project {ProjectId}: {Scanned} scanned, " +
            "{Aligned} aligned, {OutOfAlign} misaligned, {NoGeoref} no-georef, {Issues} issues.",
            projectId,
            report.ModelsScanned,
            report.ModelsAligned,
            report.ModelsOutOfAlignment,
            report.ModelsWithNoGeoref,
            report.Issues.Count);

        return report;
    }
}
