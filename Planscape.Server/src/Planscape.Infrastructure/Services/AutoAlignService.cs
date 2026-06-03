namespace Planscape.Infrastructure.Services;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<AutoAlignService>   _logger;

    public AutoAlignService(PlanscapeDbContext db, ILogger<AutoAlignService> logger)
    {
        _db     = db;
        _logger = logger;
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

        // ── 5. Compute relative transform ─────────────────────────────────────
        //   To bring the target INTO the reference frame we subtract the target
        //   survey origin and add the reference survey origin.
        //   Values are in metres; multiply by 1000 to get mm (project default).
        double targetEasting   = targetReport.SurveyEasting!.Value;
        double targetNorthing  = targetReport.SurveyNorthing ?? 0;
        double targetElevation = targetReport.SurveyElevation ?? 0;
        double targetRotDeg    = targetReport.MapConversionRotationDeg ?? 0;

        double tx = (refEasting  - targetEasting)  * 1000.0;
        double ty = (refNorthing - targetNorthing) * 1000.0;
        double tz = ((refElevation ?? 0) - targetElevation)  * 1000.0;

        double rotDeg = (refRotDeg ?? 0) - targetRotDeg;

        double scaleFactor = (targetReport.MapConversionScale.HasValue
                              && targetReport.MapConversionScale.Value != 0
                              && targetReport.MapConversionScale.Value != 1.0)
                           ? 1.0 / targetReport.MapConversionScale.Value
                           : 1.0;

        // ── 6. Persist (overwrite if exists) ──────────────────────────────────
        var existing = await _db.Set<ProjectModelTransform>()
            .FirstOrDefaultAsync(
                t => t.ProjectModelId == targetModelId
                  && t.ProjectId      == projectId
                  && t.TenantId       == tenantId,
                ct);

        if (existing == null)
        {
            existing = new ProjectModelTransform
            {
                TenantId       = tenantId,
                ProjectId      = projectId,
                ProjectModelId = targetModelId,
                CreatedAt      = DateTime.UtcNow,
            };
            _db.Set<ProjectModelTransform>().Add(existing);
        }
        else
        {
            existing.UpdatedAt = DateTime.UtcNow;
        }

        existing.TranslationX   = tx;
        existing.TranslationY   = ty;
        existing.TranslationZ   = tz;
        existing.RotationDeg    = rotDeg;
        existing.ScaleFactor    = scaleFactor;
        existing.IsAutoComputed = true;
        existing.IsConfirmed    = false;
        existing.AppliedBy      = "auto-align-service";
        existing.AppliedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AutoAlign computed for model {ModelId}: TX={TX:F3} TY={TY:F3} TZ={TZ:F3} Rot={Rot:F4}° Scale={Scale:F6} ref={Ref}",
            targetModelId, tx, ty, tz, rotDeg, scaleFactor, referenceModelId ?? "PCS");

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
