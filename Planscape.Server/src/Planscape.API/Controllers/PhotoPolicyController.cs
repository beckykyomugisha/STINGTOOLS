using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 179 — Per-project photo workflow policy. Singleton row per
/// project (controller upserts on PUT).
///
///   GET /api/projects/{pid}/photo-policy   — current policy (or defaults)
///   PUT /api/projects/{pid}/photo-policy   — full upsert (admin only)
///
/// Mutation gate: tenant Admin / Owner OR project PM.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/photo-policy")]
[Authorize]
[ProjectAccess]
public class PhotoPolicyController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IAuditService _audit;

    public PhotoPolicyController(PlanscapeDbContext db, IAuditService audit)
    {
        _db = db; _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult> Get(Guid projectId, CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var pol = await _db.PhotoPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
        return Ok(pol ?? new PhotoPolicy { ProjectId = projectId });
    }

    [HttpPut]
    public async Task<ActionResult> Upsert(
        Guid projectId,
        [FromBody] UpdatePhotoPolicyRequest req,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();

        if (req.ApprovalChain != null && !PhotoPolicy.ValidApprovalChains.Contains(req.ApprovalChain))
            return BadRequest(new { error = "invalid_approval_chain", allowed = PhotoPolicy.ValidApprovalChains });

        var pol = await _db.PhotoPolicies.FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
        var actor = CurrentUserIdOrNull();
        if (pol == null)
        {
            pol = new PhotoPolicy
            {
                TenantId  = GetTenantId(),
                ProjectId = projectId,
                UpdatedAt = DateTime.UtcNow,
                UpdatedByUserId = actor,
            };
            _db.PhotoPolicies.Add(pol);
        }

        if (req.AllowedReasonsJson != null) pol.AllowedReasonsJson = req.AllowedReasonsJson;
        if (req.DefaultAudienceByReasonJson != null) pol.DefaultAudienceByReasonJson = req.DefaultAudienceByReasonJson;
        if (req.WatermarkLogoPath != null) pol.WatermarkLogoPath = req.WatermarkLogoPath;
        if (req.WatermarkFooterTemplate != null) pol.WatermarkFooterTemplate = req.WatermarkFooterTemplate;
        if (req.WatermarkRequired.HasValue) pol.WatermarkRequired = req.WatermarkRequired.Value;
        if (req.FaceBlurRequired.HasValue) pol.FaceBlurRequired = req.FaceBlurRequired.Value;
        if (req.PlateBlurRequired.HasValue) pol.PlateBlurRequired = req.PlateBlurRequired.Value;
        if (req.RetentionDays.HasValue) pol.RetentionDays = req.RetentionDays;
        if (req.AutoArchiveAfterHandover.HasValue) pol.AutoArchiveAfterHandover = req.AutoArchiveAfterHandover.Value;
        if (req.GeofenceWkt != null) pol.GeofenceWkt = req.GeofenceWkt;
        if (req.OffsiteAudience != null) pol.OffsiteAudience = req.OffsiteAudience;
        if (req.DigestHourLocal.HasValue) pol.DigestHourLocal = req.DigestHourLocal.Value;
        if (req.DigestDistributionGroupId.HasValue) pol.DigestDistributionGroupId = req.DigestDistributionGroupId;
        if (req.ApprovalChain != null) pol.ApprovalChain = req.ApprovalChain;
        if (req.EnforceChecklistOnShiftEnd.HasValue) pol.EnforceChecklistOnShiftEnd = req.EnforceChecklistOnShiftEnd.Value;

        pol.UpdatedAt = DateTime.UtcNow;
        pol.UpdatedByUserId = actor;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UPSERT", "PhotoPolicy", pol.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { projectId }));
        return Ok(pol);
    }

    private async Task<bool> IsCuratorAsync(Guid projectId, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value ?? "";
        if (role is "Admin" or "Owner") return true;
        var userId = CurrentUserIdOrNull();
        if (userId == null) return false;
        return await _db.ProjectMembers.AsNoTracking().AnyAsync(m =>
            m.ProjectId == projectId && m.UserId == userId.Value &&
            m.IsActive && m.ProjectRole == "PM", ct);
    }
    private Guid? CurrentUserIdOrNull()
    {
        var s = User.FindFirst("user_id")?.Value
             ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
             ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(s, out var id) ? id : null;
    }
    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record UpdatePhotoPolicyRequest(
    string? AllowedReasonsJson,
    string? DefaultAudienceByReasonJson,
    string? WatermarkLogoPath,
    string? WatermarkFooterTemplate,
    bool?   WatermarkRequired,
    bool?   FaceBlurRequired,
    bool?   PlateBlurRequired,
    int?    RetentionDays,
    bool?   AutoArchiveAfterHandover,
    string? GeofenceWkt,
    string? OffsiteAudience,
    int?    DigestHourLocal,
    Guid?   DigestDistributionGroupId,
    string? ApprovalChain,
    bool?   EnforceChecklistOnShiftEnd);
