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
/// Phase 179 — Distribution group CRUD + membership management.
///
///   GET    /api/projects/{pid}/distribution-groups                 — list
///   POST   /api/projects/{pid}/distribution-groups                 — create
///   GET    /api/projects/{pid}/distribution-groups/{gid}           — single + members
///   PUT    /api/projects/{pid}/distribution-groups/{gid}           — rename / re-flag
///   DELETE /api/projects/{pid}/distribution-groups/{gid}           — drop row
///   POST   /api/projects/{pid}/distribution-groups/{gid}/members   — add member (user|email)
///   DELETE /api/projects/{pid}/distribution-groups/{gid}/members/{mid}  — remove
///
/// Mutation gate: tenant Admin / Owner OR project PM.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/distribution-groups")]
[Authorize]
[ProjectAccess]
public class DistributionGroupsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IAuditService _audit;

    public DistributionGroupsController(PlanscapeDbContext db, IAuditService audit)
    {
        _db = db; _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var groups = await _db.DistributionGroups.AsNoTracking()
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.Name)
            .ToListAsync(ct);
        var counts = await _db.DistributionGroupMembers.AsNoTracking()
            .Where(m => groups.Select(g => g.Id).Contains(m.DistributionGroupId))
            .GroupBy(m => m.DistributionGroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countMap = counts.ToDictionary(c => c.GroupId, c => c.Count);
        return Ok(groups.Select(g => new {
            g.Id, g.ProjectId, g.Name, g.Description, g.Kind,
            g.IncludeInDailyDigest, g.ForceRedacted, g.CreatedAt,
            MemberCount = countMap.GetValueOrDefault(g.Id, 0),
        }));
    }

    [HttpPost]
    public async Task<ActionResult> Create(
        Guid projectId,
        [FromBody] CreateDistributionGroupRequest req,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "name_required" });
        if (!DistributionGroup.ValidKinds.Contains(req.Kind ?? "Internal"))
            return BadRequest(new { error = "invalid_kind", allowed = DistributionGroup.ValidKinds });

        var dup = await _db.DistributionGroups.AsNoTracking()
            .AnyAsync(g => g.ProjectId == projectId && g.Name == req.Name.Trim(), ct);
        if (dup) return Conflict(new { error = "name_in_use" });

        var grp = new DistributionGroup
        {
            TenantId = GetTenantId(),
            ProjectId = projectId,
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
            Kind = req.Kind ?? "Internal",
            IncludeInDailyDigest = req.IncludeInDailyDigest ?? false,
            ForceRedacted = req.ForceRedacted ?? (req.Kind == "Client"),
            CreatedByUserId = CurrentUserIdOrNull(),
        };
        _db.DistributionGroups.Add(grp);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CREATE", "DistributionGroup", grp.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { projectId, grp.Name, grp.Kind }));
        return CreatedAtAction(nameof(GetOne), new { projectId, groupId = grp.Id }, grp);
    }

    [HttpGet("{groupId:guid}")]
    public async Task<ActionResult> GetOne(Guid projectId, Guid groupId, CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var grp = await _db.DistributionGroups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId && g.ProjectId == projectId, ct);
        if (grp == null) return NotFound();
        var members = await (
            from m in _db.DistributionGroupMembers.AsNoTracking()
            where m.DistributionGroupId == groupId
            join u in _db.Users.AsNoTracking() on m.UserId equals u.Id into ug
            from u in ug.DefaultIfEmpty()
            select new {
                m.Id, m.UserId, m.ExternalEmail,
                Display = m.DisplayName ?? (u != null ? u.DisplayName : null),
                Email   = m.ExternalEmail ?? (u != null ? u.Email : null),
                m.DisciplineFilter, m.AddedAt
            })
            .OrderBy(m => m.Display).ToListAsync(ct);
        return Ok(new { group = grp, members });
    }

    [HttpPut("{groupId:guid}")]
    public async Task<ActionResult> Update(
        Guid projectId, Guid groupId,
        [FromBody] UpdateDistributionGroupRequest req,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        var grp = await _db.DistributionGroups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.ProjectId == projectId, ct);
        if (grp == null) return NotFound();

        if (req.Name != null) grp.Name = req.Name.Trim();
        if (req.Description != null) grp.Description = req.Description.Trim();
        if (req.Kind != null)
        {
            if (!DistributionGroup.ValidKinds.Contains(req.Kind))
                return BadRequest(new { error = "invalid_kind", allowed = DistributionGroup.ValidKinds });
            grp.Kind = req.Kind;
        }
        if (req.IncludeInDailyDigest.HasValue) grp.IncludeInDailyDigest = req.IncludeInDailyDigest.Value;
        if (req.ForceRedacted.HasValue) grp.ForceRedacted = req.ForceRedacted.Value;
        await _db.SaveChangesAsync(ct);
        return Ok(grp);
    }

    [HttpDelete("{groupId:guid}")]
    public async Task<ActionResult> Delete(Guid projectId, Guid groupId, CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        var grp = await _db.DistributionGroups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.ProjectId == projectId, ct);
        if (grp == null) return NotFound();
        _db.DistributionGroups.Remove(grp);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("DELETE", "DistributionGroup", grp.Id.ToString(), "{}");
        return NoContent();
    }

    [HttpPost("{groupId:guid}/members")]
    public async Task<ActionResult> AddMember(
        Guid projectId, Guid groupId,
        [FromBody] AddDistributionMemberRequest req,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        var grp = await _db.DistributionGroups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.ProjectId == projectId, ct);
        if (grp == null) return NotFound();
        if (req.UserId == null && string.IsNullOrWhiteSpace(req.ExternalEmail))
            return BadRequest(new { error = "user_or_email_required" });

        var member = new DistributionGroupMember
        {
            DistributionGroupId = groupId,
            UserId             = req.UserId,
            ExternalEmail      = req.ExternalEmail?.Trim().ToLowerInvariant(),
            DisplayName        = req.DisplayName?.Trim(),
            DisciplineFilter   = req.DisciplineFilter?.Trim(),
            AddedByUserId      = CurrentUserIdOrNull(),
        };
        _db.DistributionGroupMembers.Add(member);
        await _db.SaveChangesAsync(ct);
        return Ok(member);
    }

    [HttpDelete("{groupId:guid}/members/{memberId:guid}")]
    public async Task<ActionResult> RemoveMember(
        Guid projectId, Guid groupId, Guid memberId,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        var member = await _db.DistributionGroupMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.DistributionGroupId == groupId, ct);
        if (member == null) return NotFound();
        _db.DistributionGroupMembers.Remove(member);
        await _db.SaveChangesAsync(ct);
        return NoContent();
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

public record CreateDistributionGroupRequest(
    string  Name,
    string? Description,
    string? Kind,                  // Client | Internal | Mixed
    bool?   IncludeInDailyDigest,
    bool?   ForceRedacted);

public record UpdateDistributionGroupRequest(
    string? Name,
    string? Description,
    string? Kind,
    bool?   IncludeInDailyDigest,
    bool?   ForceRedacted);

public record AddDistributionMemberRequest(
    Guid?   UserId,
    string? ExternalEmail,
    string? DisplayName,
    string? DisciplineFilter);
