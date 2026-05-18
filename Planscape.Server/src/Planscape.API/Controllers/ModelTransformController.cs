namespace Planscape.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

/// <summary>
/// Gap E — REST API for managing per-model coordinate transforms.
/// Route: api/projects/{projectId}/models/{modelId}/transform
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/models/{modelId:guid}/transform")]
[Authorize]
public class ModelTransformController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IIfcDeltaService _delta;
    private readonly ILogger<ModelTransformController> _logger;

    public ModelTransformController(
        PlanscapeDbContext db,
        ITenantContext tenant,
        IIfcDeltaService delta,
        ILogger<ModelTransformController> logger)
    {
        _db     = db;
        _tenant = tenant;
        _delta  = delta;
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

        if (xf == null)
        {
            return Ok(new
            {
                modelId          = modelId,
                hasTransform     = false,
                translationX     = 0.0,
                translationY     = 0.0,
                translationZ     = 0.0,
                rotationDeg      = 0.0,
                scaleFactor      = 1.0,
                isAutoComputed   = false,
                isConfirmed      = false,
                appliedBy        = (string?)null,
                appliedAt        = (DateTime?)null,
                notes            = (string?)null,
            });
        }

        return Ok(new
        {
            modelId          = modelId,
            hasTransform     = true,
            translationX     = xf.TranslationX,
            translationY     = xf.TranslationY,
            translationZ     = xf.TranslationZ,
            rotationDeg      = xf.RotationDeg,
            scaleFactor      = xf.ScaleFactor,
            isAutoComputed   = xf.IsAutoComputed,
            isConfirmed      = xf.IsConfirmed,
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
        // Validate ownership
        var projectExists = await _db.ProjectModels.AsNoTracking()
            .AnyAsync(m => m.Id        == modelId
                        && m.ProjectId == projectId
                        && m.TenantId  == _tenant.TenantId
                        && m.DeletedAt == null,
                      ct);
        if (!projectExists)
            return NotFound(new { message = "Model not found or does not belong to this project/tenant." });

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

        await _db.SaveChangesAsync(ct);

        // Re-compute SceneNode AABBs for this model
        try
        {
            var nodes = await _db.SceneNodes
                .Where(n => n.SourceModelId == modelId && n.DeletedAt == null)
                .ToListAsync(ct);

            foreach (var node in nodes)
            {
                var (mnX, mnY, mnZ, mxX, mxY, mxZ) = ApplyTransform(
                    xf,
                    node.MinX, node.MinY, node.MinZ,
                    node.MaxX, node.MaxY, node.MaxZ);

                node.MinX = mnX;
                node.MinY = mnY;
                node.MinZ = mnZ;
                node.MaxX = mxX;
                node.MaxY = mxY;
                node.MaxZ = mxZ;
            }

            if (nodes.Count > 0)
                await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "ModelTransform upserted for model {ModelId}: updated {Count} SceneNode AABBs.",
                modelId, nodes.Count);
        }
        catch (Exception ex)
        {
            // Non-fatal — transform is persisted; AABB update can be retried
            _logger.LogWarning(ex,
                "Failed to update SceneNode AABBs after transform upsert for model {ModelId}.",
                modelId);
        }

        return Ok(new
        {
            modelId          = modelId,
            hasTransform     = true,
            translationX     = xf.TranslationX,
            translationY     = xf.TranslationY,
            translationZ     = xf.TranslationZ,
            rotationDeg      = xf.RotationDeg,
            scaleFactor      = xf.ScaleFactor,
            isAutoComputed   = xf.IsAutoComputed,
            isConfirmed      = xf.IsConfirmed,
            appliedBy        = xf.AppliedBy,
            appliedAt        = xf.AppliedAt,
            notes            = xf.Notes,
        });
    }

    // ── DELETE — reset to identity (remove the transform row) ───────────────
    [HttpDelete]
    public async Task<IActionResult> Delete(Guid projectId, Guid modelId, CancellationToken ct)
    {
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

        _logger.LogInformation(
            "ModelTransform deleted for model {ModelId} in project {ProjectId}.",
            modelId, projectId);

        return NoContent();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Transform all 8 corners of the AABB using scale → Z-rotation → translation
    /// and return the new axis-aligned bounding box.
    /// </summary>
    private static (double minX, double minY, double minZ, double maxX, double maxY, double maxZ) ApplyTransform(
        ProjectModelTransform t,
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ)
    {
        var rotRad = t.RotationDeg * Math.PI / 180.0;
        var cos    = Math.Cos(rotRad);
        var sin    = Math.Sin(rotRad);

        // All 8 corners of the input AABB
        double[] xs = [minX, maxX, minX, maxX, minX, maxX, minX, maxX];
        double[] ys = [minY, minY, maxY, maxY, minY, minY, maxY, maxY];
        double[] zs = [minZ, minZ, minZ, minZ, maxZ, maxZ, maxZ, maxZ];

        var newXs = new double[8];
        var newYs = new double[8];
        var newZs = new double[8];

        for (int i = 0; i < 8; i++)
        {
            double sx = xs[i] * t.ScaleFactor;
            double sy = ys[i] * t.ScaleFactor;
            double sz = zs[i] * t.ScaleFactor;

            newXs[i] = cos * sx - sin * sy + t.TranslationX;
            newYs[i] = sin * sx + cos * sy + t.TranslationY;
            newZs[i] = sz + t.TranslationZ;
        }

        return (newXs.Min(), newYs.Min(), newZs.Min(),
                newXs.Max(), newYs.Max(), newZs.Max());
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
