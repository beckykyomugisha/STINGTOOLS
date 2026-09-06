namespace Planscape.Infrastructure.Services;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Coordinates;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

// ── Public interface ──────────────────────────────────────────────────────────

public interface IAutoAlignService
{
    /// <summary>
    /// Gap F — Compute the relative transform needed to bring targetModel into the
    /// reference model's coordinate frame, using IfcMapConversion data from
    /// IfcAlignmentReport. If a ProjectCoordinateSystem exists for the project,
    /// use its origin as the absolute reference. Otherwise use the reference model's
    /// survey origin as the reference frame.
    ///
    /// Returns the suggested transform (IsAutoComputed=true, IsConfirmed=false).
    /// Caller decides whether to persist and apply it.
    /// </summary>
    Task<AutoAlignResult> ComputeAsync(
        Guid projectId, Guid tenantId, Guid targetModelId,
        IHubContext<FederatedModelHub>? modelHub = null,
        CancellationToken ct = default,
        // #12 — re-emit ModelUpdated on NotificationHub (project-{id}) so the
        // dashboard + plugin (both on /hubs/notifications) refresh after an
        // auto-align transform; /hubs/model has no client.
        IHubContext<NotificationHub>? notificationHub = null);
}

public sealed record AutoAlignResult(
    bool    Success,
    double  TranslationX,
    double  TranslationY,
    double  TranslationZ,
    double  RotationDeg,
    double  ScaleFactor,
    string? ReferenceModelId,
    string? Message);

// ── Implementation ────────────────────────────────────────────────────────────

public sealed class AutoAlignService : IAutoAlignService
{
    private readonly PlanscapeDbContext          _db;
    private readonly IModelGeorefWriter           _georefWriter;
    private readonly ILogger<AutoAlignService>   _logger;

    public AutoAlignService(
        PlanscapeDbContext db,
        IModelGeorefWriter georefWriter,
        ILogger<AutoAlignService> logger)
    {
        _db           = db;
        _georefWriter = georefWriter;
        _logger       = logger;
    }

    public async Task<AutoAlignResult> ComputeAsync(
        Guid projectId, Guid tenantId, Guid targetModelId,
        IHubContext<FederatedModelHub>? modelHub = null,
        CancellationToken ct = default,
        IHubContext<NotificationHub>? notificationHub = null)
    {
        // ── 1. Load the target model's latest IfcAlignmentReport ──────────────
        var targetReport = await _db.IfcAlignmentReports.AsNoTracking()
            .Where(r => r.ProjectModelId == targetModelId
                     && r.ProjectId      == projectId
                     && r.TenantId       == tenantId)
            .OrderByDescending(r => r.ValidatedAt)
            .FirstOrDefaultAsync(ct);

        // ── 2. Guard: target must have IfcMapConversion data ─────────────────
        if (targetReport == null
            || !targetReport.HasMapConversion
            || targetReport.SurveyEasting  == null
            || targetReport.SurveyNorthing == null)
        {
            return Fail("Target model has no IfcMapConversion data");
        }

        // ── 2b. Guard: target must not be the designated reference model ────────
        // (ComputeAsync is a no-op and would produce a zero transform)
        var pcsCheck = await _db.Set<ProjectCoordinateSystem>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.TenantId == tenantId, ct);
        if (pcsCheck?.ReferenceModelId.HasValue == true
            && pcsCheck.ReferenceModelId.Value == targetModelId)
        {
            return Fail("Target model is the designated reference model; nothing to align.");
        }

        // ── 3. Check for ProjectCoordinateSystem ──────────────────────────────
        var pcs = await _db.Set<ProjectCoordinateSystem>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ProjectId == projectId && x.TenantId == tenantId, ct);

        // ── 4. Find reference ─────────────────────────────────────────────────
        IfcAlignmentReport? refReport   = null;
        double refEasting   = 0, refNorthing   = 0;
        double? refElevation = null, refRotDeg = null;
        string? referenceModelId = null;

        if (pcs?.ReferenceModelId.HasValue == true)
        {
            // Use the coordinator's nominated reference model
            refReport = await _db.IfcAlignmentReports.AsNoTracking()
                .Where(r => r.ProjectModelId == pcs.ReferenceModelId.Value
                         && r.ProjectId      == projectId
                         && r.TenantId       == tenantId)
                .OrderByDescending(r => r.ValidatedAt)
                .FirstOrDefaultAsync(ct);

            if (refReport?.SurveyEasting != null)
            {
                refEasting       = refReport.SurveyEasting.Value;
                refNorthing      = refReport.SurveyNorthing ?? 0;
                refElevation     = refReport.SurveyElevation;
                refRotDeg        = refReport.MapConversionRotationDeg;
                referenceModelId = pcs.ReferenceModelId.Value.ToString();
            }
        }

        if (refReport?.SurveyEasting == null)
        {
            // No designated reference model → find the most recently validated model
            // with HasMapConversion=true that is NOT the target
            refReport = await _db.IfcAlignmentReports.AsNoTracking()
                .Where(r => r.ProjectId      == projectId
                         && r.TenantId       == tenantId
                         && r.ProjectModelId != targetModelId
                         && r.HasMapConversion
                         && r.SurveyEasting  != null)
                .OrderByDescending(r => r.ValidatedAt)
                .FirstOrDefaultAsync(ct);

            if (refReport?.SurveyEasting != null)
            {
                // P5 — this is now REPORTING only, not the frame.
                //
                // "The most recently validated sibling" was a non-deterministic
                // frame: it moved every time another model was uploaded, so a
                // transform computed on Monday was expressed against a different
                // origin from one computed on Tuesday, and the two silently
                // disagreed. The writer resolves a STABLE frame instead
                // (declared benchmark → nominated reference → CRS origin), and
                // since the same frame is subtracted from every model, relative
                // placement is identical either way — only the absolute offset
                // differs, and the viewer recentres that away.
                refEasting       = refReport.SurveyEasting.Value;
                refNorthing      = refReport.SurveyNorthing ?? 0;
                refElevation     = refReport.SurveyElevation;
                refRotDeg        = refReport.MapConversionRotationDeg;
                referenceModelId = refReport.ProjectModelId.ToString();
            }
            else if (pcs?.OriginEasting != null && pcs.OriginNorthing != null)
            {
                // Fall back to the ProjectCoordinateSystem's benchmark origin
                refEasting       = pcs.OriginEasting.Value;
                refNorthing      = pcs.OriginNorthing.Value;
                refElevation     = pcs.OriginElevation;
                refRotDeg        = pcs.TrueNorthDeg;
                referenceModelId = null; // no model — PCS is the reference
            }
            else
            {
                return Fail("No reference model or project coordinate system found");
            }
        }

        // ── 5. Delegate to the ONE writer ─────────────────────────────────────
        //
        // P5 — this method used to compute and persist the transform itself,
        // with its own translation convention (reference-relative) alongside the
        // IFC ingest path's (each-model-to-origin). Two conventions for the same
        // job is how a model ends up in a different place depending on which
        // pipeline last touched it, so both now go through ModelGeorefWriter:
        // it resolves the project frame, applies the sign, grades the
        // confidence, and refuses to overwrite a confirmed transform.
        //
        // The reference model this method resolved above (steps 3-4) is exactly
        // the frame the writer resolves for itself, so the behaviour is
        // preserved — minus the mirrored sign that was in both copies.
        var georef = new ModelGeoref(
            EastingM      : targetReport.SurveyEasting,
            NorthingM     : targetReport.SurveyNorthing,
            ElevationM    : targetReport.SurveyElevation,
            TrueNorthDeg  : targetReport.MapConversionRotationDeg ?? 0,
            CrsCode       : targetReport.CrsName,
            HasDeclaredCrs: targetReport.HasProjectedCrs,
            LengthUnit    : targetReport.LengthUnit,
            SourceLabel   : "auto-align",
            MapConversionScale: targetReport.MapConversionScale);

        // The writer owns the arithmetic and hands back what it computed —
        // including when it REFUSES to write, so a refusal can still tell the
        // coordinator what the survey data says. Recomputing it here is exactly
        // how the two implementations drifted apart before P5.
        var write = await _georefWriter.WriteAsync(
            projectId, targetModelId, tenantId, georef, targetReport.Verdict, ct);

        double tx = write.TranslationXMm;
        double ty = write.TranslationYMm;
        double tz = write.TranslationZMm;
        double rotDeg = write.RotationDeg;
        double scaleFactor = write.ScaleFactor;

        // ── 6. Never overwrite a manually-confirmed transform ─────────────────
        // A coordinator who has confirmed an alignment has made a judgement the
        // survey data does not capture (a mis-stated IfcMapConversion, a model
        // deliberately parked off-site, a base point agreed on site). The IFC
        // ingest path has always respected that; this path did not, so any
        // auto-align run silently destroyed the confirmed alignment and the
        // coordinator's only signal was the model jumping.
        //
        // The rule itself lives in ModelGeorefWriter — the write it refused is
        // the authority on whether it was refused, so this branch reads the
        // writer's answer instead of re-reading the row and re-testing
        // IsConfirmed. Two copies of a precedence rule is how they drift, and
        // the drifted half here would be the one that overwrites.
        //
        // Unlike the ingest path, which skips quietly because it is a side
        // effect of an upload, this one is an explicit user action: report the
        // refusal, so the coordinator can decide deliberately (delete the
        // transform, or PUT a new one).
        if (write.RefusedAsConfirmed)
        {
            _logger.LogInformation(
                "AutoAlign skipped for model {ModelId}: an existing transform is manually confirmed (confirmed by {AppliedBy} at {AppliedAt}).",
                targetModelId, write.ConfirmedBy ?? "unknown", write.ConfirmedAt);

            return new AutoAlignResult(
                Success         : false,
                // What auto-align WOULD have applied, so the coordinator can
                // compare it against the alignment they confirmed and decide
                // deliberately. A bare refusal would be useless at exactly the
                // moment the answer matters.
                TranslationX    : tx,
                TranslationY    : ty,
                TranslationZ    : tz,
                RotationDeg     : rotDeg,
                ScaleFactor     : scaleFactor,
                ReferenceModelId: referenceModelId,
                Message         : "This model's transform was manually confirmed by a coordinator and will not be "
                                + "overwritten automatically. The transform auto-align computed is returned for "
                                + "comparison; delete the existing transform or PUT a new one to change it.");
        }

        _logger.LogInformation(
            "AutoAlign computed for model {ModelId}: TX={TX:F3} TY={TY:F3} TZ={TZ:F3} Rot={Rot:F4}° Scale={Scale:F6} ref={Ref} confidence={Confidence} autoApplied={AutoApplied}",
            targetModelId, tx, ty, tz, rotDeg, scaleFactor, referenceModelId ?? "PCS",
            TransformConfidencePolicy.ToStorageString(write.Confidence),
            TransformConfidencePolicy.ShouldAutoApply(write.Confidence));

        // Gap K — broadcast the new transform so viewer clients refresh their
        // coordinate frame without polling.
        if (modelHub != null)
        {
            try
            {
                await FederatedModelHub.NotifyUpdate(
                    modelHub,
                    projectId.ToString(),
                    new[] { targetModelId.ToString() },
                    Array.Empty<long>(),
                    "auto-align",
                    notificationHub: notificationHub);
            }
            catch (Exception hubEx)
            {
                _logger.LogWarning(hubEx,
                    "FederatedModelHub notify failed for auto-align on model {ModelId}", targetModelId);
            }
        }

        return new AutoAlignResult(
            Success         : true,
            TranslationX    : tx,
            TranslationY    : ty,
            TranslationZ    : tz,
            RotationDeg     : rotDeg,
            ScaleFactor     : scaleFactor,
            ReferenceModelId: referenceModelId,
            Message         : null);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static AutoAlignResult Fail(string message) =>
        new(false, 0, 0, 0, 0, 1.0, null, message);
}
