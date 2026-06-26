// Phase 195 (WS A6) — server endpoints for EDGE/LEED sustainability snapshots
// pushed from the desktop plugin. Mirrors HvacController:
//   GET  /api/projects/{id}/sustainability/dashboard        — latest + 30-day trend
//   GET  /api/projects/{id}/sustainability/snapshots        — list
//   GET  /api/projects/{id}/sustainability/snapshots/{id}   — single snapshot detail
//   POST /api/projects/{id}/sustainability/snapshots        — plugin push entry
//
// All routes are tenant-scoped via TenantResolutionMiddleware so writes never
// bleed across organisations.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/sustainability")]
[Authorize]
public class SustainabilityController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public SustainabilityController(PlanscapeDbContext db) { _db = db; }

    // ── Dashboard (latest snapshot + 30-day trend) ───────────────────
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(Guid projectId)
    {
        var since = DateTime.UtcNow.AddDays(-30);

        var latest = await _db.Set<SustainabilitySnapshot>()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CapturedAt)
            .FirstOrDefaultAsync();

        var trend = await _db.Set<SustainabilitySnapshot>()
            .Where(x => x.ProjectId == projectId && x.CapturedAt >= since)
            .OrderBy(x => x.CapturedAt)
            .Select(x => new
            {
                x.CapturedAt, x.EnergySavingsPct, x.WaterSavingsPct,
                x.MaterialEnergySavingsPct, x.EdgeLevel, x.EdgePassed, x.Rag
            })
            .ToListAsync();

        return Ok(new
        {
            latest = latest == null ? null : ToCard(latest),
            last30d = trend
        });
    }

    private static object ToCard(SustainabilitySnapshot s) => new
    {
        latest                   = (DateTime?)s.CapturedAt,
        rag                      = s.Rag,
        edgeLevel                = s.EdgeLevel,
        edgePassed               = s.EdgePassed,
        energySavingsPct         = s.EnergySavingsPct,
        waterSavingsPct          = s.WaterSavingsPct,
        materialEnergySavingsPct = s.MaterialEnergySavingsPct,
        gwpReductionPct          = s.GwpReductionPct,
        energyEuiKwhM2Yr         = s.EnergyEuiKwhM2Yr,
        operationalCarbonKgYr    = s.OperationalCarbonKgYr,
        floorAreaM2              = s.FloorAreaM2
    };

    // ── Snapshot list ────────────────────────────────────────────────
    [HttpGet("snapshots")]
    public async Task<IActionResult> List(Guid projectId, [FromQuery] int take = 50)
    {
        var rows = await _db.Set<SustainabilitySnapshot>().AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CapturedAt)
            .Take(Math.Min(take, 500))
            .Select(x => new
            {
                x.Id, x.CapturedAt, x.Rag, x.EdgeLevel, x.EdgePassed,
                x.EnergySavingsPct, x.WaterSavingsPct, x.MaterialEnergySavingsPct,
                x.GwpReductionPct, x.OperationalCarbonKgYr
            })
            .ToListAsync();
        return Ok(rows);
    }

    // ── Snapshot detail (PayloadJson included) ───────────────────────
    [HttpGet("snapshots/{snapshotId:guid}")]
    public async Task<IActionResult> Get(Guid projectId, Guid snapshotId)
    {
        var row = await _db.Set<SustainabilitySnapshot>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.Id == snapshotId);
        if (row == null) return NotFound();
        return Ok(row);
    }

    // ── Push (plugin → server) ───────────────────────────────────────
    [HttpPost("snapshots")]
    public async Task<IActionResult> Push(Guid projectId, [FromBody] SustainabilitySnapshot body)
    {
        if (body == null) return BadRequest("Empty body");
        body.Id        = Guid.NewGuid();
        body.ProjectId = projectId;
        if (body.CapturedAt == default) body.CapturedAt = DateTime.UtcNow;
        if (string.IsNullOrEmpty(body.Rag)) body.Rag = "G";

        _db.Set<SustainabilitySnapshot>().Add(body);
        await _db.SaveChangesAsync();
        return Ok(new { id = body.Id, capturedAt = body.CapturedAt });
    }
}
