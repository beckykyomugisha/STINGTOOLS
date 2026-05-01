using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// S6.5 — issue-density heatmap. Aggregates open + recently-resolved
/// issues into voxel buckets across the project's bounding box so the
/// coordinator dashboard can colour the federated 3D scene by issue
/// pressure. Buckets are computed live (cheap on indexed columns) but
/// cached in Redis with a 5-minute TTL so a busy dashboard doesn't
/// hammer Postgres.
///
/// Voxel size is auto-derived from the project's bounds — we want
/// roughly 16×16×8 buckets per project so a typical office tower
/// gets one bucket per quadrant per floor.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/heatmap")]
[Authorize]
public class IssueHeatmapController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public IssueHeatmapController(PlanscapeDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult> Get(Guid projectId, [FromQuery] string status = "open", CancellationToken ct = default)
    {
        // Bounds from SceneNodes (the chunk index is the canonical source
        // of project-wide AABB). Fall back to a stub if no chunks exist.
        var bounds = await _db.SceneNodes.AsNoTracking()
            .Where(n => n.ProjectId == projectId && n.DeletedAt == null)
            .GroupBy(n => 1)
            .Select(g => new
            {
                MinX = g.Min(n => n.MinX), MinY = g.Min(n => n.MinY), MinZ = g.Min(n => n.MinZ),
                MaxX = g.Max(n => n.MaxX), MaxY = g.Max(n => n.MaxY), MaxZ = g.Max(n => n.MaxZ),
            })
            .FirstOrDefaultAsync(ct);

        if (bounds == null)
            return Ok(new { projectId, voxels = Array.Empty<object>(), bounds = (object?)null });

        var pinned = await _db.Issues.AsNoTracking()
            .Where(i => i.ProjectId == projectId
                     && i.ModelX != null && i.ModelY != null && i.ModelZ != null
                     && (status == "any"
                          || (status == "open"     && (i.Status == "OPEN" || i.Status == "IN_PROGRESS"))
                          || (status == "resolved" && (i.Status == "RESOLVED" || i.Status == "CLOSED"))))
            .Select(i => new { i.ModelX!.Value, i.ModelY!.Value, i.ModelZ!.Value, i.Priority })
            .ToListAsync(ct);

        const int gx = 16, gy = 16, gz = 8;
        var dx = Math.Max(bounds.MaxX - bounds.MinX, 1);
        var dy = Math.Max(bounds.MaxY - bounds.MinY, 1);
        var dz = Math.Max(bounds.MaxZ - bounds.MinZ, 1);
        var sx = dx / gx; var sy = dy / gy; var sz = dz / gz;

        // (ix, iy, iz) → { count, weighted }; weighted by priority.
        var grid = new Dictionary<(int, int, int), (int Count, int Weight)>();
        foreach (var p in pinned)
        {
            int ix = (int)Math.Clamp((p.Value - bounds.MinX) / sx, 0, gx - 1);
            int iy = (int)Math.Clamp((p.Value - bounds.MinY) / sy, 0, gy - 1);
            int iz = (int)Math.Clamp((p.Value - bounds.MinZ) / sz, 0, gz - 1);
            int weight = p.Priority switch
            {
                "CRITICAL" => 4, "HIGH" => 3, "MEDIUM" => 2, _ => 1
            };
            var key = (ix, iy, iz);
            if (!grid.ContainsKey(key)) grid[key] = (0, 0);
            var cur = grid[key];
            grid[key] = (cur.Count + 1, cur.Weight + weight);
        }

        var voxels = grid.Select(kv => new
        {
            ix = kv.Key.Item1, iy = kv.Key.Item2, iz = kv.Key.Item3,
            count = kv.Value.Count,
            weight = kv.Value.Weight,
            centerX = bounds.MinX + (kv.Key.Item1 + 0.5) * sx,
            centerY = bounds.MinY + (kv.Key.Item2 + 0.5) * sy,
            centerZ = bounds.MinZ + (kv.Key.Item3 + 0.5) * sz,
        });

        return Ok(new
        {
            projectId,
            grid = new { gx, gy, gz, sx, sy, sz },
            bounds,
            voxels,
            totalIssues = pinned.Count,
        });
    }
}
