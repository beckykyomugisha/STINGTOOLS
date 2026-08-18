using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Coordinates;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// A host's georeferencing, normalised. Whatever the source — an IFC
/// <c>IfcMapConversion</c>, Revit's <c>ProjectPosition</c>, or an ArchiCAD
/// survey point — it reduces to these numbers.
/// </summary>
/// <param name="EastingM">Survey easting of the model's internal origin, METRES.</param>
/// <param name="NorthingM">Survey northing of the model's internal origin, METRES.</param>
/// <param name="ElevationM">Survey elevation of the model's internal origin, METRES.</param>
/// <param name="TrueNorthDeg">
/// Rotation from the CRS grid north to the model's internal north, degrees
/// clockwise-positive.
/// </param>
/// <param name="CrsCode">The source's declared CRS ("EPSG:27700"), or null.</param>
/// <param name="HasDeclaredCrs">
/// The source declared a projected CRS of its own (IfcProjectedCRS, or a Revit
/// export that carries an EPSG code). Distinct from <paramref name="CrsCode"/>
/// being non-null so a host can supply a code it is not certain about.
/// </param>
/// <param name="LengthUnit">The model's own length unit — "mm" | "m" | "ft".</param>
/// <param name="SourceLabel">
/// Which pipeline produced this: "ifc-map-conversion" | "revit-georef".
/// Stored on the transform so the UI can explain why a model moved.
/// </param>
public sealed record ModelGeoref(
    double? EastingM,
    double? NorthingM,
    double? ElevationM,
    double TrueNorthDeg,
    string? CrsCode,
    bool HasDeclaredCrs,
    string? LengthUnit,
    string SourceLabel)
{
    /// <summary>True when there is enough here to place the model at all.</summary>
    public bool HasSurveyOrigin => EastingM.HasValue && NorthingM.HasValue;
}

public interface IModelGeorefWriter
{
    /// <summary>
    /// Persist (upsert) the <see cref="ProjectModelTransform"/> implied by a
    /// host's georeferencing, grading its confidence so a trustworthy one
    /// renders without a coordinator confirming it.
    ///
    /// Never overwrites a transform a coordinator has confirmed. Returns the
    /// confidence it graded, or <see cref="TransformConfidence.None"/> when
    /// there was nothing usable to write.
    /// </summary>
    Task<TransformConfidence> WriteAsync(
        Guid projectId, Guid projectModelId, Guid tenantId,
        ModelGeoref georef, string? verdict, CancellationToken ct = default);
}

/// <summary>
/// The ONE place that turns a host's georeferencing into a stored transform.
///
/// <para><b>Why this exists.</b> Three pipelines produce automatic transforms —
/// IFC ingest, the Revit GLB upload, and <c>AutoAlignService</c>. Each used to
/// carry (or, for Revit, lack) its own copy of the translation convention, the
/// unit handling and the overwrite rules. Copies drift, and when they drift the
/// symptom is a building in the wrong place depending on which pipeline last
/// touched it — the most expensive class of bug in this system and the hardest
/// to attribute. Centralising it means a convention change is one edit, not
/// three, and the agreement is enforced by construction rather than by
/// vigilance.</para>
///
/// <para><b>The translation convention.</b> Each model is moved from its own
/// survey origin back to the project origin: <c>t = -origin</c>, metres → mm.
/// This is the convention the IFC ingest path has always used, preserved here
/// deliberately so introducing this writer changes no existing behaviour.
/// (<c>AutoAlignService</c> computes a RELATIVE transform instead — the two are
/// reconciled separately; see the P5 note in
/// <c>docs/COORDINATION_AUDIT_FINDINGS.md</c>.)</para>
/// </summary>
public sealed class ModelGeorefWriter : IModelGeorefWriter
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<ModelGeorefWriter> _logger;

    public ModelGeorefWriter(PlanscapeDbContext db, ILogger<ModelGeorefWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TransformConfidence> WriteAsync(
        Guid projectId, Guid projectModelId, Guid tenantId,
        ModelGeoref georef, string? verdict, CancellationToken ct = default)
    {
        if (!georef.HasSurveyOrigin)
        {
            // Nothing to place the model with. Deliberately writes NO transform:
            // a model at the project origin is visibly un-placed and a
            // coordinator fixes it in seconds, whereas a guessed transform looks
            // placed and costs an investigation.
            _logger.LogInformation(
                "Georef for model {ModelId}: no survey origin ({Source}) — left at project origin, no transform written.",
                projectModelId, georef.SourceLabel);
            return TransformConfidence.None;
        }

        var projectCrs = await _db.ProjectCoordinateSystems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.TenantId == tenantId, ct);

        var confidence = TransformConfidencePolicy.Evaluate(
            hasMapConversion : true,
            hasProjectedCrs  : georef.HasDeclaredCrs,
            crsMatchesProject: TransformConfidencePolicy.CrsEquivalent(
                                   georef.CrsCode, projectCrs?.CrsEpsgCode ?? projectCrs?.CrsName),
            surveyEasting    : georef.EastingM,
            surveyNorthing   : georef.NorthingM,
            verdict          : verdict);

        bool autoApply = TransformConfidencePolicy.ShouldAutoApply(confidence);
        string confidenceText = TransformConfidencePolicy.ToStorageString(confidence);

        // Survey origin (metres) → translation (mm). Negated: applying it brings
        // georeferenced model coordinates back to the project origin.
        double txMm = -georef.EastingM!.Value  * 1000.0;
        double tyMm = -georef.NorthingM!.Value * 1000.0;
        double tzMm = -(georef.ElevationM ?? 0) * 1000.0;

        var existing = await _db.ProjectModelTransforms
            .FirstOrDefaultAsync(t => t.ProjectId == projectId
                                   && t.ProjectModelId == projectModelId
                                   && t.TenantId == tenantId, ct);

        if (existing is { IsConfirmed: true })
        {
            // A coordinator has asserted this alignment against evidence the
            // survey data does not carry. An automatic writer never overrules it.
            _logger.LogInformation(
                "Georef for model {ModelId}: skipped — an existing transform is manually confirmed.",
                projectModelId);
            return confidence;
        }

        if (existing == null)
        {
            _db.ProjectModelTransforms.Add(new ProjectModelTransform
            {
                TenantId             = tenantId,
                ProjectId            = projectId,
                ProjectModelId       = projectModelId,
                TranslationX         = txMm,
                TranslationY         = tyMm,
                TranslationZ         = tzMm,
                RotationDeg          = georef.TrueNorthDeg,
                ScaleFactor          = 1.0,
                IsAutoComputed       = true,
                IsConfirmed          = false,
                AppliedAutomatically = autoApply,
                Confidence           = confidenceText,
                Source               = georef.SourceLabel,
                AppliedAt            = DateTime.UtcNow,
                Notes                = $"Auto-computed from {georef.SourceLabel} at {DateTime.UtcNow:u} (confidence {confidenceText})",
            });
        }
        else
        {
            existing.TranslationX         = txMm;
            existing.TranslationY         = tyMm;
            existing.TranslationZ         = tzMm;
            existing.RotationDeg          = georef.TrueNorthDeg;
            existing.ScaleFactor          = 1.0;
            existing.IsAutoComputed       = true;
            existing.AppliedAutomatically = autoApply;
            existing.Confidence           = confidenceText;
            existing.Source               = georef.SourceLabel;
            existing.UpdatedAt            = DateTime.UtcNow;
            existing.Notes                = $"Auto-computed from {georef.SourceLabel} at {DateTime.UtcNow:u} (confidence {confidenceText})";
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Georef for model {ModelId}: TX={TX:F1} TY={TY:F1} TZ={TZ:F1} mm Rot={Rot:F4}° source={Source} confidence={Confidence} autoApplied={AutoApplied}",
            projectModelId, txMm, tyMm, tzMm, georef.TrueNorthDeg,
            georef.SourceLabel, confidenceText, autoApply);

        return confidence;
    }
}
