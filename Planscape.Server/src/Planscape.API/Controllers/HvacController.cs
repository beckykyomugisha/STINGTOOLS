using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// HVAC engine results — load snapshots, NC predictions, refrigerant
/// pipe sizing records. The desktop plugin pushes results from
/// BlockLoad / NcPredict / RefrigerantSize commands so the design
/// history is auditable + visible in the mobile app.
///
/// All routes tenant-scoped via the existing middleware.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/hvac")]
[Authorize]
public class HvacController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public HvacController(PlanscapeDbContext db) { _db = db; }

    // ── Dashboard ─────────────────────────────────────────────────

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(Guid projectId)
    {
        var loadLatest = await _db.Set<HvacLoadSnapshot>()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CapturedAt)
            .FirstOrDefaultAsync();
        var ncLatestCount = await _db.Set<HvacNcSnapshot>()
            .Where(x => x.ProjectId == projectId &&
                        x.CapturedAt >= DateTime.UtcNow.AddDays(-30))
            .GroupBy(x => 1)
            .Select(g => new {
                Total       = g.Count(),
                OverTarget  = g.Count(x => x.PredictedNc > x.TargetNc)
            })
            .FirstOrDefaultAsync();
        var refrigCount = await _db.Set<HvacRefrigerantSizing>()
            .Where(x => x.ProjectId == projectId).CountAsync();
        var refrigWarnCount = await _db.Set<HvacRefrigerantSizing>()
            .Where(x => x.ProjectId == projectId && !x.Ok).CountAsync();

        return Ok(new {
            loads = loadLatest == null
                ? new { latest = (DateTime?)null, blockKw = 0.0, diversity = 0.0, rag = "A" }
                : new {
                    latest = (DateTime?)loadLatest.CapturedAt,
                    blockKw = loadLatest.BlockSensibleW / 1000.0,
                    diversity = loadLatest.DiversityFactor,
                    climate = loadLatest.ClimateSiteLabel,
                    rag = "G"
                  },
            nc = new {
                totalLast30d = ncLatestCount?.Total ?? 0,
                overTarget   = ncLatestCount?.OverTarget ?? 0,
                rag = (ncLatestCount?.OverTarget ?? 0) > 0 ? "A" : "G"
            },
            refrigerant = new {
                totalSizings = refrigCount,
                failed       = refrigWarnCount,
                rag = refrigWarnCount > 0 ? "R" : "G"
            }
        });
    }

    // ── Block-load snapshots ──────────────────────────────────────

    [HttpPost("loads")]
    public async Task<IActionResult> PostLoadSnapshot(Guid projectId,
        [FromBody] HvacLoadSnapshot body)
    {
        body.ProjectId = projectId;
        if (body.CapturedAt == default) body.CapturedAt = DateTime.UtcNow;
        _db.Set<HvacLoadSnapshot>().Add(body);
        await _db.SaveChangesAsync();
        return Created($"api/projects/{projectId}/hvac/loads/{body.Id}", body);
    }

    [HttpPost("loads/bulk")]
    public async Task<IActionResult> PostLoadSnapshotsBulk(Guid projectId,
        [FromBody] List<HvacLoadSnapshot> bodies)
    {
        if (bodies == null || bodies.Count == 0) return Ok(new { count = 0 });
        foreach (var b in bodies)
        {
            b.ProjectId = projectId;
            if (b.CapturedAt == default) b.CapturedAt = DateTime.UtcNow;
        }
        _db.Set<HvacLoadSnapshot>().AddRange(bodies);
        await _db.SaveChangesAsync();
        return Ok(new { count = bodies.Count });
    }

    [HttpGet("loads")]
    public async Task<IActionResult> GetLoads(Guid projectId,
        [FromQuery] string? systemId = null,
        [FromQuery] DateTime? since = null)
    {
        var q = _db.Set<HvacLoadSnapshot>().Where(x => x.ProjectId == projectId);
        if (!string.IsNullOrEmpty(systemId)) q = q.Where(x => x.SystemId == systemId);
        if (since.HasValue) q = q.Where(x => x.CapturedAt >= since.Value);
        var rows = await q.OrderByDescending(x => x.CapturedAt).Take(500).ToListAsync();
        return Ok(rows);
    }

    // ── NC predictions ────────────────────────────────────────────

    [HttpPost("nc")]
    public async Task<IActionResult> PostNcSnapshot(Guid projectId,
        [FromBody] HvacNcSnapshot body)
    {
        body.ProjectId = projectId;
        if (body.CapturedAt == default) body.CapturedAt = DateTime.UtcNow;
        _db.Set<HvacNcSnapshot>().Add(body);
        await _db.SaveChangesAsync();
        return Created($"api/projects/{projectId}/hvac/nc/{body.Id}", body);
    }

    [HttpGet("nc")]
    public async Task<IActionResult> GetNc(Guid projectId,
        [FromQuery] DateTime? since = null,
        [FromQuery] bool overTargetOnly = false)
    {
        var q = _db.Set<HvacNcSnapshot>().Where(x => x.ProjectId == projectId);
        if (since.HasValue) q = q.Where(x => x.CapturedAt >= since.Value);
        if (overTargetOnly) q = q.Where(x => x.PredictedNc > x.TargetNc);
        var rows = await q.OrderByDescending(x => x.CapturedAt).Take(500).ToListAsync();
        return Ok(rows);
    }

    // ── Refrigerant sizings ───────────────────────────────────────

    [HttpPost("refrigerant")]
    public async Task<IActionResult> PostRefrigerantSizing(Guid projectId,
        [FromBody] HvacRefrigerantSizing body)
    {
        body.ProjectId = projectId;
        if (body.CapturedAt == default) body.CapturedAt = DateTime.UtcNow;
        _db.Set<HvacRefrigerantSizing>().Add(body);
        await _db.SaveChangesAsync();
        return Created($"api/projects/{projectId}/hvac/refrigerant/{body.Id}", body);
    }

    [HttpGet("refrigerant")]
    public async Task<IActionResult> GetRefrigerantSizings(Guid projectId,
        [FromQuery] string? refrigerantId = null)
    {
        var q = _db.Set<HvacRefrigerantSizing>().Where(x => x.ProjectId == projectId);
        if (!string.IsNullOrEmpty(refrigerantId))
            q = q.Where(x => x.RefrigerantId == refrigerantId);
        var rows = await q.OrderByDescending(x => x.CapturedAt).Take(500).ToListAsync();
        return Ok(rows);
    }
}
