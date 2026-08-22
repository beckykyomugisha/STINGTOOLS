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
/// <param name="MapConversionScale">
/// The GEOREFERENCING scale declared by the source (IfcMapConversion.Scale) —
/// a survey correction, e.g. a grid-to-ground factor. Null or 1.0 means none.
/// Emphatically NOT the mesh's unit scale: that is <see cref="MeshUnits"/>,
/// applied separately at render time. Conflating the two turns a unit fix into
/// a survey error.
/// </param>
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
    string SourceLabel,
    double? MapConversionScale = null)
{
    /// <summary>True when there is enough here to place the model at all.</summary>
    public bool HasSurveyOrigin => EastingM.HasValue && NorthingM.HasValue;
}

/// <summary>
/// What the writer computed, and whether it stored it.
///
/// <para>The computed numbers are returned even when <see cref="Written"/> is
/// false. That is deliberate: when the write is refused because a coordinator
/// confirmed the transform by hand, the useful answer is "here is what the
/// survey data says, compare it with yours" — not a bare refusal. Returning
/// them from here keeps the arithmetic in ONE place; the alternative was for
/// the caller to recompute it, which is how the two implementations drifted
/// apart in the first place.</para>
/// </summary>
public sealed record GeorefWriteResult(
    TransformConfidence Confidence,
    bool Written,
    double TranslationXMm,
    double TranslationYMm,
    double TranslationZMm,
    double RotationDeg,
    double ScaleFactor,
    string FrameSource)
{
    public static GeorefWriteResult Nothing(TransformConfidence confidence = TransformConfidence.None)
        => new(confidence, false, 0, 0, 0, 0, 1.0, "none");
}

public interface IModelGeorefWriter
{
    /// <summary>
    /// Persist (upsert) the <see cref="ProjectModelTransform"/> implied by a
    /// host's georeferencing, grading its confidence so a trustworthy one
    /// renders without a coordinator confirming it.
    ///
    /// Never overwrites a transform a coordinator has confirmed — in that case
    /// the computed values are still returned, with <c>Written = false</c>.
    /// </summary>
    Task<GeorefWriteResult> WriteAsync(
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
/// <para><b>The translation convention (P5).</b> A model's geometry is authored
/// about its own internal origin, and its georeferencing states where that
/// origin sits in the survey CRS. So the transform that carries the model into
/// the shared world is
/// <c>t = (modelSurveyOrigin - projectFrameOrigin)</c>, metres → mm.</para>
///
/// <para>This corrects a sign, and the sign was the bug. Both writers previously
/// computed the NEGATION — the ingest path used <c>t = -modelOrigin</c> and
/// <c>AutoAlignService</c> used <c>t = referenceOrigin - modelOrigin</c>. Take
/// one physical point shared by two models: in model A's local frame it sits at
/// <c>S - A</c>, in model B's at <c>S - B</c>. Applying <c>t = +A</c> puts it at
/// <c>A + (S - A) = S</c> for A and at <c>S</c> for B — the same world point,
/// which is exactly what "the models overlay" means. Applying <c>t = -A</c> puts
/// it at <c>S - 2A</c>, and two models end up mirrored about the origin: an
/// east-west pair swaps sides. The existing overlay proof
/// (<c>ModelTransformMathTests</c>) could not catch this because it inverts and
/// re-applies the SAME transform, proving the math self-consistent rather than
/// proving the transform was derived correctly from survey data.</para>
///
/// <para><b>The frame origin</b> is resolved once, here, so every writer agrees:
/// the project's declared <c>ProjectCoordinateSystem</c> benchmark if there is
/// one, else the coordinator's nominated reference model's survey origin, else
/// zero (raw CRS coordinates). Subtracting it keeps world coordinates small —
/// a site at easting 432,000 m rendered about a zero frame origin puts geometry
/// 432 km out, where float precision dies — while preserving every model's true
/// offset from every other, because the same frame is subtracted from all of
/// them.</para>
/// </summary>
public sealed class ModelGeorefWriter : IModelGeorefWriter
{
    private readonly PlanscapeDbContext _db;
    private readonly ISceneNodeAabbRefresher _aabb;
    private readonly ILogger<ModelGeorefWriter> _logger;

    public ModelGeorefWriter(
        PlanscapeDbContext db,
        ISceneNodeAabbRefresher aabb,
        ILogger<ModelGeorefWriter> logger)
    {
        _db = db;
        _aabb = aabb;
        _logger = logger;
    }

    public async Task<GeorefWriteResult> WriteAsync(
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
            return GeorefWriteResult.Nothing();
        }

        var projectCrs = await _db.ProjectCoordinateSystems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.TenantId == tenantId, ct);

        var frame = await ResolveFrameOriginAsync(projectId, tenantId, projectModelId, projectCrs, ct);

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

        // P5 — the model's survey origin expressed in the project frame, metres
        // → mm. NOT negated: see the class remarks. t = +origin - frameOrigin is
        // what makes a point shared by two models land on the same world
        // coordinate; the old negation mirrored models about the origin.
        //
        // P5b — the displacement is measured on CRS grid axes, but the rendered
        // world is the project FRAME: the rotation below is frame-relative
        // (θ_model − θ_frame), so a frame declared off grid north spins the
        // geometry by −θ_frame. The translation has to live in those same axes,
        // or geometry and translation disagree and two models with different
        // survey origins stop overlaying — the error is (R(−θ_frame) − I)·
        // (originB − originA), which grows with frame rotation and separation and
        // is zero only at θ_frame = 0. So rotate the grid displacement by
        // −θ_frame too. Identity for a grid-aligned frame, which is why those
        // projects were unaffected. Z is orthogonal to the planar frame spin.
        double dxE = georef.EastingM!.Value  - frame.EastingM;    // metres, CRS grid axes
        double dyN = georef.NorthingM!.Value - frame.NorthingM;
        double frameRad = -frame.TrueNorthDeg * Math.PI / 180.0;  // −θ_frame, same CCW convention as ApplyMm
        double frameCos = Math.Cos(frameRad), frameSin = Math.Sin(frameRad);
        double txMm = (frameCos * dxE - frameSin * dyN) * 1000.0; // grid displacement, rotated into frame axes
        double tyMm = (frameSin * dxE + frameCos * dyN) * 1000.0;
        double tzMm = ((georef.ElevationM ?? 0) - frame.ElevationM) * 1000.0;

        // Rotation is likewise relative to the frame: a project whose declared
        // coordinate system is itself rotated off grid north must not have that
        // rotation applied twice.
        double rotationDeg = georef.TrueNorthDeg - frame.TrueNorthDeg;

        // The source's declared survey scale, inverted to undo it — the same
        // convention AutoAlignService uses. The IFC ingest path used to compute
        // this into a variable whose two branches both returned 1.0, so a
        // declared map-conversion scale was silently discarded.
        double scaleFactor = (georef.MapConversionScale is { } mcs && mcs != 0 && mcs != 1.0)
            ? 1.0 / mcs
            : 1.0;

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
            return new GeorefWriteResult(confidence, Written: false,
                txMm, tyMm, tzMm, rotationDeg, scaleFactor, frame.Source);
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
                RotationDeg          = rotationDeg,
                ScaleFactor          = scaleFactor,
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
            existing.RotationDeg          = rotationDeg;
            existing.ScaleFactor          = scaleFactor;
            existing.IsAutoComputed       = true;
            existing.AppliedAutomatically = autoApply;
            existing.Confidence           = confidenceText;
            existing.Source               = georef.SourceLabel;
            existing.UpdatedAt            = DateTime.UtcNow;
            existing.Notes                = $"Auto-computed from {georef.SourceLabel} at {DateTime.UtcNow:u} (confidence {confidenceText})";
        }

        await _db.SaveChangesAsync(ct);

        // P5 — the manifest AABBs describe where the chunks are in WORLD space,
        // so moving a model invalidates them. Both automatic writers used to
        // skip this (only the manual PUT did it), leaving the viewer culling
        // against bounds for a position the model no longer occupied: geometry
        // that vanishes when you look straight at it. Best-effort — the
        // transform is already committed and correct, and stale bounds are a
        // culling artefact, not data loss.
        try
        {
            await _aabb.RefreshAsync(projectId, projectModelId, tenantId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SceneNode AABB refresh failed for model {ModelId} (non-fatal — transform is stored).",
                projectModelId);
        }

        _logger.LogInformation(
            "Georef for model {ModelId}: TX={TX:F1} TY={TY:F1} TZ={TZ:F1} mm Rot={Rot:F4}° frame={Frame} source={Source} confidence={Confidence} autoApplied={AutoApplied}",
            projectModelId, txMm, tyMm, tzMm, rotationDeg, frame.Source,
            georef.SourceLabel, confidenceText, autoApply);

        return new GeorefWriteResult(confidence, Written: true,
            txMm, tyMm, tzMm, rotationDeg, scaleFactor, frame.Source);
    }

    /// <summary>
    /// The origin every model in this project is positioned relative to.
    ///
    /// <para>Resolved in one place so both automatic writers agree by
    /// construction. Order of preference, strongest evidence first:</para>
    /// <list type="number">
    /// <item>the project's declared <c>ProjectCoordinateSystem</c> benchmark —
    /// an explicit coordinator decision;</item>
    /// <item>the survey origin of the coordinator's nominated reference model —
    /// an implicit one ("everything lines up with this");</item>
    /// <item>zero, i.e. raw CRS coordinates.</item>
    /// </list>
    ///
    /// <para>Whichever is chosen, the SAME frame is subtracted from every model,
    /// so relative offsets are preserved exactly. Choosing a frame near the site
    /// only buys precision: world coordinates stay small instead of sitting
    /// hundreds of kilometres from the origin where 32-bit float stops
    /// resolving millimetres.</para>
    /// </summary>
    private async Task<FrameOrigin> ResolveFrameOriginAsync(
        Guid projectId, Guid tenantId, Guid excludeModelId,
        ProjectCoordinateSystem? projectCrs, CancellationToken ct)
    {
        if (projectCrs?.OriginEasting is { } oe && projectCrs.OriginNorthing is { } on)
        {
            return new FrameOrigin(oe, on, projectCrs.OriginElevation ?? 0,
                                   projectCrs.TrueNorthDeg, "project-coordinate-system");
        }

        if (projectCrs?.ReferenceModelId is { } refId && refId != excludeModelId)
        {
            var refReport = await _db.IfcAlignmentReports.AsNoTracking()
                .Where(r => r.ProjectModelId == refId
                         && r.ProjectId == projectId
                         && r.TenantId == tenantId
                         && r.SurveyEasting != null)
                .OrderByDescending(r => r.ValidatedAt)
                .FirstOrDefaultAsync(ct);

            if (refReport?.SurveyEasting is { } re)
            {
                return new FrameOrigin(re, refReport.SurveyNorthing ?? 0,
                                       refReport.SurveyElevation ?? 0,
                                       refReport.MapConversionRotationDeg ?? 0,
                                       "reference-model");
            }
        }

        // No declared frame: raw CRS coordinates. Correct, just large.
        return new FrameOrigin(0, 0, 0, 0, "crs-origin");
    }

    private readonly record struct FrameOrigin(
        double EastingM, double NorthingM, double ElevationM, double TrueNorthDeg, string Source);
}
