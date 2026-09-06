namespace Planscape.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Coordinates;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

/// <summary>
/// Gap E — REST API for managing per-model coordinate transforms.
/// Route: api/projects/{projectId}/models/{modelId}/transform
/// </summary>
/// <remarks>
/// Authorization mirrors <see cref="ModelsController"/> / <see cref="IfcIngestController"/>:
/// <c>[ProjectAccess]</c> is the read gate (404 for a project the caller cannot
/// see) and <see cref="ControllerProjectMembershipExtensions.RequireProjectMemberAsync"/>
/// is the write gate (403 for a caller who can see the project but is not a
/// member). Tenant scope alone is NOT sufficient — without these, a member of
/// any project in the tenant could read and overwrite every other project's
/// model transforms.
/// </remarks>
[ApiController]
[Route("api/projects/{projectId:guid}/models/{modelId:guid}/transform")]
[Authorize]
[ProjectAccess]
public class ModelTransformController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IIfcDeltaService _delta;
    private readonly ISceneNodeAabbRefresher _aabb;
    private readonly ILogger<ModelTransformController> _logger;

    public ModelTransformController(
        PlanscapeDbContext db,
        ITenantContext tenant,
        IIfcDeltaService delta,
        ISceneNodeAabbRefresher aabb,
        ILogger<ModelTransformController> logger)
    {
        _db     = db;
        _tenant = tenant;
        _delta  = delta;
        _aabb   = aabb;
        _logger = logger;
    }

    // ── GET — return current transform or identity (200 always) ─────────────
    [HttpGet]
    public async Task<ActionResult> Get(Guid projectId, Guid modelId, CancellationToken ct)
    {
        var xf = await _db.Set<ProjectModelTransform>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.ProjectModelId == modelId
                  && t.ProjectId      == projectId
                  && t.TenantId       == _tenant.TenantId,
                ct);

        // P3 — the mesh's own length unit travels with the transform because
        // this is the one payload the viewer already fetches per model, and the
        // two are multiplied at render time anyway. It is reported even when
        // there is NO transform: a model whose mesh is in millimetres needs
        // rescaling whether or not it is georeferenced, and before this the
        // viewer never read ProjectModel.Units at all, so such a model rendered
        // 1000x too large. Alone that is invisible (the camera fits to bounds);
        // federated with a metre model it is a thousand-fold mismatch.
        var meshUnits = await _db.ProjectModels.AsNoTracking()
            .Where(m => m.Id == modelId && m.ProjectId == projectId && m.TenantId == _tenant.TenantId)
            .Select(m => m.Units)
            .FirstOrDefaultAsync(ct);
        double meshUnitScale = MeshUnits.ToMetres(meshUnits);

        if (xf == null)
        {
            return Ok(new
            {
                modelId          = modelId,
                hasTransform     = false,
                meshUnits        = meshUnits,
                meshUnitScale    = meshUnitScale,
                translationX     = 0.0,
                translationY     = 0.0,
                translationZ     = 0.0,
                rotationDeg      = 0.0,
                scaleFactor      = 1.0,
                isAutoComputed   = false,
                isConfirmed      = false,
                appliedAutomatically = false,
                confidence       = (string?)null,
                source           = (string?)null,
                appliedBy        = (string?)null,
                appliedAt        = (DateTime?)null,
                notes            = (string?)null,
            });
        }

        return Ok(new
        {
            modelId          = modelId,
            hasTransform     = true,
            meshUnits        = meshUnits,
            meshUnitScale    = meshUnitScale,
            translationX     = xf.TranslationX,
            translationY     = xf.TranslationY,
            translationZ     = xf.TranslationZ,
            rotationDeg      = xf.RotationDeg,
            scaleFactor      = xf.ScaleFactor,
            isAutoComputed   = xf.IsAutoComputed,
            isConfirmed      = xf.IsConfirmed,
            appliedAutomatically = xf.AppliedAutomatically,
            confidence       = xf.Confidence,
            source           = xf.Source,
            appliedBy        = xf.AppliedBy,
            appliedAt        = xf.AppliedAt,
            notes            = xf.Notes,
        });
    }

    // ── PUT — upsert transform ───────────────────────────────────────────────
    [HttpPut]
    public async Task<ActionResult> Upsert(
        Guid projectId,
        Guid modelId,
        [FromBody] TransformUpsertDto dto,
        CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;

        // Validate ownership. Selecting Units at the same time serves the
        // response below, which echoes the mesh unit so a client that PUTs a
        // transform sees the same shape GET returns.
        var model = await _db.ProjectModels.AsNoTracking()
            .Where(m => m.Id        == modelId
                     && m.ProjectId == projectId
                     && m.TenantId  == _tenant.TenantId
                     && m.DeletedAt == null)
            .Select(m => new { m.Units })
            .FirstOrDefaultAsync(ct);
        if (model == null)
            return NotFound(new { message = "Model not found or does not belong to this project/tenant." });

        var meshUnits = model.Units;
        double meshUnitScale = MeshUnits.ToMetres(meshUnits);

        if (dto.ScaleFactor <= 0)
            return BadRequest(new { message = "ScaleFactor must be greater than zero." });

        // Look up or create the transform row
        var xf = await _db.Set<ProjectModelTransform>()
            .FirstOrDefaultAsync(
                t => t.ProjectModelId == modelId
                  && t.ProjectId      == projectId
                  && t.TenantId       == _tenant.TenantId,
                ct);

        if (xf == null)
        {
            xf = new ProjectModelTransform
            {
                TenantId       = _tenant.TenantId,
                ProjectId      = projectId,
                ProjectModelId = modelId,
                CreatedAt      = DateTime.UtcNow,
            };
            _db.Set<ProjectModelTransform>().Add(xf);
        }
        else
        {
            xf.UpdatedAt = DateTime.UtcNow;
        }

        xf.TranslationX   = dto.TranslationX;
        xf.TranslationY   = dto.TranslationY;
        xf.TranslationZ   = dto.TranslationZ;
        xf.RotationDeg    = dto.RotationDeg;
        xf.ScaleFactor    = dto.ScaleFactor;
        xf.IsConfirmed    = dto.IsConfirmed;
        xf.Notes          = dto.Notes;
        xf.IsAutoComputed = false;
        xf.AppliedBy      = User.Identity?.Name;
        xf.AppliedAt      = DateTime.UtcNow;

        // B1 — a hand-entered transform is never an "auto-applied" one, whatever
        // the row held before. Clearing the flag here is what makes the
        // precedence rule total: after a manual PUT the row is purely the
        // coordinator's, and the automatic writers will not touch it again while
        // IsConfirmed stands. A manual transform saved with IsConfirmed=false
        // stays a draft and does not render — unchanged from previous behaviour.
        xf.AppliedAutomatically = false;
        xf.Confidence           = null;
        xf.Source               = "manual";

        await _db.SaveChangesAsync(ct);

        // P5 — recompute the chunks' world-space AABBs through the shared,
        // idempotent refresher. The inline version that used to live here read
        // the STORED (already-transformed) box and transformed it again, so two
        // PUTs compounded; and it ran ONLY here, so both automatic writers left
        // the manifest describing where chunks used to be.
        try
        {
            var updated = await _aabb.RefreshAsync(projectId, modelId, _tenant.TenantId, ct);
            _logger.LogInformation(
                "ModelTransform upserted for model {ModelId}: refreshed {Count} SceneNode AABBs.",
                modelId, updated);
        }
        catch (Exception ex)
        {
            // Non-fatal — the transform is persisted and correct; stale bounds
            // are a culling artefact, not data loss, and the next write retries.
            _logger.LogWarning(ex,
                "Failed to refresh SceneNode AABBs after transform upsert for model {ModelId}.",
                modelId);
        }

        return Ok(new
        {
            modelId          = modelId,
            hasTransform     = true,
            meshUnits        = meshUnits,
            meshUnitScale    = meshUnitScale,
            translationX     = xf.TranslationX,
            translationY     = xf.TranslationY,
            translationZ     = xf.TranslationZ,
            rotationDeg      = xf.RotationDeg,
            scaleFactor      = xf.ScaleFactor,
            isAutoComputed   = xf.IsAutoComputed,
            isConfirmed      = xf.IsConfirmed,
            appliedAutomatically = xf.AppliedAutomatically,
            confidence       = xf.Confidence,
            source           = xf.Source,
            appliedBy        = xf.AppliedBy,
            appliedAt        = xf.AppliedAt,
            notes            = xf.Notes,
        });
    }

    // ── DELETE — reset to identity (remove the transform row) ───────────────
    [HttpDelete]
    public async Task<IActionResult> Delete(Guid projectId, Guid modelId, CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;

        var xf = await _db.Set<ProjectModelTransform>()
            .FirstOrDefaultAsync(
                t => t.ProjectModelId == modelId
                  && t.ProjectId      == projectId
                  && t.TenantId       == _tenant.TenantId,
                ct);

        if (xf == null)
            return NoContent(); // idempotent

        _db.Set<ProjectModelTransform>().Remove(xf);
        await _db.SaveChangesAsync(ct);

        // P5 — resetting to identity moves the model too, so the world-space
        // chunk bounds are just as stale as after a write. With the transform
        // row gone the refresher falls back to the identity and the world box
        // returns to the local one — which only works because the local box is
        // preserved separately; the old in-place version had destroyed it.
        try
        {
            await _aabb.RefreshAsync(projectId, modelId, _tenant.TenantId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to refresh SceneNode AABBs after transform delete for model {ModelId}.",
                modelId);
        }

        _logger.LogInformation(
            "ModelTransform deleted for model {ModelId} in project {ProjectId}.",
            modelId, projectId);

        return NoContent();
    }

}

/// <summary>Body DTO for PUT /transform.</summary>
public sealed record TransformUpsertDto(
    double TranslationX,
    double TranslationY,
    double TranslationZ,
    double RotationDeg,
    double ScaleFactor,
    bool   IsConfirmed,
    string? Notes);
