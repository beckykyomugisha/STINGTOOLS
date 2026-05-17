namespace Planscape.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

/// <summary>
/// Gap 1 — CDE folder hierarchy management.
/// Each project can define a tree of information containers that mirrors
/// ACC / Aconex / BIM 360 folder structures. Documents carry a nullable
/// ContainerId FK so they can be assigned to a specific folder.
///
/// Route: api/projects/{projectId}/cde-containers
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/cde-containers")]
[Authorize]
[ProjectAccess]
public class CdeContainersController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public CdeContainersController(PlanscapeDbContext db) => _db = db;

    // ── GET tree ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the full container tree for the project, rooted at top-level
    /// containers (ParentContainerId == null). Each node includes its children
    /// so the client can render the whole tree in a single call.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> GetTree(Guid projectId, CancellationToken ct)
    {
        var tenantId = GetTenantId();

        var all = await _db.CdeContainers.AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.TenantId == tenantId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

        // Build tree in memory — the set is bounded by the number of folders
        // a team creates (typically < 200) so recursion is safe.
        var byParent = all.GroupBy(c => c.ParentContainerId)
                          .ToDictionary(g => g.Key, g => g.ToList());

        static object BuildNode(CdeContainer c, Dictionary<Guid?, List<CdeContainer>> map)
        {
            var children = map.TryGetValue(c.Id, out var ch)
                ? ch.Select(child => BuildNode(child, map)).ToList()
                : new List<object>();
            return new
            {
                c.Id, c.Name, c.ParentContainerId, c.ContainerType,
                c.Discipline, c.Description, c.SortOrder,
                c.CreatedBy, c.CreatedAt, c.UpdatedAt,
                children
            };
        }

        var roots = byParent.TryGetValue(null, out var rootList)
            ? rootList.Select(r => BuildNode(r, byParent)).ToList()
            : new List<object>();

        return Ok(new { projectId, count = all.Count, tree = roots });
    }

    // ── GET flat list ─────────────────────────────────────────────────────────

    [HttpGet("flat")]
    public async Task<ActionResult> GetFlat(Guid projectId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var items = await _db.CdeContainers.AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.TenantId == tenantId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);
        return Ok(new { count = items.Count, items });
    }

    // ── POST — create container ───────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult> Create(
        Guid projectId,
        [FromBody] CdeContainerUpsertDto dto,
        CancellationToken ct)
    {
        if (!await IsManagerOrAboveAsync(projectId))
            return Forbid();

        var tenantId = GetTenantId();

        // Validate project ownership
        var projectExists = await _db.Projects.AnyAsync(
            p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (!projectExists) return NotFound("Project not found");

        // Validate parent belongs to same project
        if (dto.ParentContainerId.HasValue)
        {
            var parentExists = await _db.CdeContainers.AnyAsync(
                c => c.Id == dto.ParentContainerId.Value
                  && c.ProjectId == projectId
                  && c.TenantId == tenantId, ct);
            if (!parentExists) return BadRequest("Parent container not found in this project");
        }

        var container = new CdeContainer
        {
            TenantId          = tenantId,
            ProjectId         = projectId,
            Name              = dto.Name.Trim(),
            ParentContainerId = dto.ParentContainerId,
            ContainerType     = dto.ContainerType,
            Discipline        = dto.Discipline,
            Description       = dto.Description,
            SortOrder         = dto.SortOrder ?? 0,
            CreatedBy         = GetUserName(),
        };

        _db.CdeContainers.Add(container);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetTree), new { projectId }, new
        {
            container.Id, container.Name, container.ParentContainerId,
            container.ContainerType, container.Discipline, container.SortOrder,
            container.CreatedAt
        });
    }

    // ── PUT — rename / move / update ─────────────────────────────────────────

    [HttpPut("{containerId:guid}")]
    public async Task<ActionResult> Update(
        Guid projectId,
        Guid containerId,
        [FromBody] CdeContainerUpsertDto dto,
        CancellationToken ct)
    {
        if (!await IsManagerOrAboveAsync(projectId))
            return Forbid();

        var tenantId = GetTenantId();
        var container = await _db.CdeContainers.FirstOrDefaultAsync(
            c => c.Id == containerId && c.ProjectId == projectId && c.TenantId == tenantId, ct);
        if (container == null) return NotFound();

        // Prevent circular reference — new parent cannot be a descendant of this node
        if (dto.ParentContainerId.HasValue && dto.ParentContainerId != container.ParentContainerId)
        {
            if (dto.ParentContainerId.Value == containerId)
                return BadRequest("A container cannot be its own parent");

            var isDescendant = await IsDescendantAsync(projectId, tenantId, dto.ParentContainerId.Value, containerId, ct);
            if (isDescendant)
                return BadRequest("Moving a container under one of its own descendants would create a cycle");
        }

        container.Name              = dto.Name.Trim();
        container.ParentContainerId = dto.ParentContainerId;
        container.ContainerType     = dto.ContainerType ?? container.ContainerType;
        container.Discipline        = dto.Discipline    ?? container.Discipline;
        container.Description       = dto.Description   ?? container.Description;
        container.SortOrder         = dto.SortOrder     ?? container.SortOrder;
        container.UpdatedAt         = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(container);
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    [HttpDelete("{containerId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid containerId, CancellationToken ct)
    {
        if (!await IsManagerOrAboveAsync(projectId))
            return Forbid();

        var tenantId = GetTenantId();
        var container = await _db.CdeContainers.FirstOrDefaultAsync(
            c => c.Id == containerId && c.ProjectId == projectId && c.TenantId == tenantId, ct);
        if (container == null) return NotFound();

        var hasChildren = await _db.CdeContainers.AnyAsync(
            c => c.ParentContainerId == containerId, ct);
        if (hasChildren)
            return BadRequest("Remove or re-parent child containers before deleting this one");

        // Unlink documents (set ContainerId to null rather than cascade-delete them)
        await _db.Documents
            .Where(d => d.ContainerId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ContainerId, (Guid?)null), ct);

        _db.CdeContainers.Remove(container);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Assign documents to a container ──────────────────────────────────────

    /// <summary>
    /// Bulk-assign a list of document IDs to this container. Pass an empty
    /// array to unlink all documents from the container.
    /// </summary>
    [HttpPut("{containerId:guid}/documents")]
    public async Task<ActionResult> AssignDocuments(
        Guid projectId,
        Guid containerId,
        [FromBody] Guid[] documentIds,
        CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var container = await _db.CdeContainers.AnyAsync(
            c => c.Id == containerId && c.ProjectId == projectId && c.TenantId == tenantId, ct);
        if (!container) return NotFound();

        if (documentIds.Length > 500)
            return BadRequest("Maximum 500 documents per batch assignment");

        // Only allow assigning documents that belong to the same project
        await _db.Documents
            .Where(d => documentIds.Contains(d.Id) && d.ProjectId == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ContainerId, containerId), ct);

        return Ok(new { assigned = documentIds.Length });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    private string GetUserName() =>
        User.FindFirst("display_name")?.Value ?? User.FindFirst("sub")?.Value ?? "System";

    private async Task<bool> IsManagerOrAboveAsync(Guid projectId)
    {
        var role = User.FindFirst("role")?.Value ?? "";
        if (role is "Admin" or "Owner") return true;
        return await _db.ProjectMembers.AnyAsync(
            m => m.ProjectId == projectId && m.IsActive
              && (m.ProjectRole == "Manager" || m.ProjectRole == "Admin" || m.ProjectRole == "Owner"));
    }

    /// <summary>
    /// Returns true if <paramref name="candidateParent"/> is a descendant of
    /// <paramref name="root"/>. Used to detect cyclic-reference attempts.
    /// </summary>
    private async Task<bool> IsDescendantAsync(
        Guid projectId, Guid tenantId, Guid candidateParent, Guid root, CancellationToken ct)
    {
        var all = await _db.CdeContainers.AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.TenantId == tenantId)
            .Select(c => new { c.Id, c.ParentContainerId })
            .ToListAsync(ct);

        var parentMap = all.ToDictionary(c => c.Id, c => c.ParentContainerId);
        var current = candidateParent;
        while (parentMap.TryGetValue(current, out var parent) && parent.HasValue)
        {
            if (parent.Value == root) return true;
            current = parent.Value;
        }
        return false;
    }
}

public record CdeContainerUpsertDto(
    string  Name,
    Guid?   ParentContainerId = null,
    string? ContainerType     = null,
    string? Discipline        = null,
    string? Description       = null,
    int?    SortOrder         = null);
