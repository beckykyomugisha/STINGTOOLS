// Phase 188 (Tier 3) — server endpoints for HVAC snapshots pushed
// from the desktop plugin. Mirrors the HealthcareController shape:
//   GET  /api/projects/{id}/hvac/dashboard          — per-kind RAG + latest
//   GET  /api/projects/{id}/hvac/snapshots          — list, filterable by kind
//   GET  /api/projects/{id}/hvac/snapshots/{id}     — single snapshot detail
//   POST /api/projects/{id}/hvac/snapshots          — plugin push entry
//
// All routes are tenant-scoped via TenantResolutionMiddleware so
// writes never bleed across organisations.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/hvac")]
[Authorize]
public class HvacController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public HvacController(PlanscapeDbContext db) { _db = db; }

    // ── Dashboard aggregator ─────────────────────────────────────────
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(Guid projectId)
    {
        var since = DateTime.UtcNow.AddDays(-30);

        // Latest snapshot per kind. One round-trip per kind to keep the
        // query plan trivial; the kinds list is bounded (5 entries).
        var kinds = new[] { "loads", "balance", "drift", "carbon", "sizing" };
        var latest = new Dictionary<string, HvacSnapshot?>();
        foreach (var k in kinds)
        {
            var row = await _db.Set<HvacSnapshot>()
                .Where(x => x.ProjectId == projectId && x.Kind == k)
                .OrderByDescending(x => x.CapturedAt)
                .FirstOrDefaultAsync();
            latest[k] = row;
        }

        // 30-day rolling totals (for sparkline counts).
        var counts = await _db.Set<HvacSnapshot>()
            .Where(x => x.ProjectId == projectId && x.CapturedAt >= since)
            .GroupBy(x => x.Kind)
            .Select(g => new {
                Kind = g.Key,
                Total = g.Count(),
                Pass  = g.Sum(x => x.Pass),
                Warn  = g.Sum(x => x.Warn),
                Fail  = g.Sum(x => x.Fail)
            })
            .ToListAsync();

        return Ok(new
        {
            loads = ToCard(latest["loads"]),
            balance = ToCard(latest["balance"]),
            drift = ToCard(latest["drift"]),
            carbon = ToCard(latest["carbon"]),
            sizing = ToCard(latest["sizing"]),
            last30d = counts
        });
    }

    private static object ToCard(HvacSnapshot? s) =>
        s == null
            ? new { latest = (DateTime?)null, rag = "A", inspected = 0, pass = 0, warn = 0, fail = 0,
                    totalKw = 0.0, worstValue = 0.0 }
            : new
            {
                latest     = (DateTime?)s.CapturedAt,
                rag        = s.Rag,
                inspected  = s.Inspected,
                pass       = s.Pass,
                warn       = s.Warn,
                fail       = s.Fail,
                totalKw    = s.TotalKw,
                worstValue = s.WorstValue
            };

    // ── Snapshot list ────────────────────────────────────────────────
    [HttpGet("snapshots")]
    public async Task<IActionResult> List(Guid projectId,
        [FromQuery] string? kind = null,
        [FromQuery] int take = 50)
    {
        var q = _db.Set<HvacSnapshot>().AsNoTracking()
            .Where(x => x.ProjectId == projectId);
        if (!string.IsNullOrEmpty(kind)) q = q.Where(x => x.Kind == kind);
        var rows = await q.OrderByDescending(x => x.CapturedAt)
            .Take(Math.Min(take, 500))
            .Select(x => new
            {
                x.Id, x.Kind, x.CapturedAt, x.Rag,
                x.Inspected, x.Pass, x.Warn, x.Fail,
                x.TotalKw, x.WorstValue
            })
            .ToListAsync();
        return Ok(rows);
    }

    // ── Snapshot detail (PayloadJson included) ───────────────────────
    [HttpGet("snapshots/{snapshotId:guid}")]
    public async Task<IActionResult> Get(Guid projectId, Guid snapshotId)
    {
        var row = await _db.Set<HvacSnapshot>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.Id == snapshotId);
        if (row == null) return NotFound();
        return Ok(row);
    }

    // ── Push (plugin → server) ───────────────────────────────────────
    [HttpPost("snapshots")]
    public async Task<IActionResult> Push(Guid projectId,
        [FromBody] HvacSnapshot body)
    {
        if (body == null) return BadRequest("Empty body");
        body.Id        = Guid.NewGuid();
        body.ProjectId = projectId;
        if (body.CapturedAt == default) body.CapturedAt = DateTime.UtcNow;
        if (string.IsNullOrEmpty(body.Rag))  body.Rag  = "G";
        if (string.IsNullOrEmpty(body.Kind)) body.Kind = "sizing";

        _db.Set<HvacSnapshot>().Add(body);
        await _db.SaveChangesAsync();
        return Ok(new { id = body.Id, capturedAt = body.CapturedAt });
    }
}
