using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Healthcare Pack H-22 — server endpoints for pressure logs, MGPS
/// verification records, anti-ligature audits and RDS snapshots.
///
/// All routes are tenant-scoped via the existing TenantResolutionMiddleware
/// so writes never bleed across organisations.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/healthcare")]
[Authorize]
public class HealthcareController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public HealthcareController(PlanscapeDbContext db) { _db = db; }

    // ── Dashboard aggregator ─────────────────────────────────────────
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(Guid projectId)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        // Two GroupBy queries cover the four count-style aggregates that
        // came from independent CountAsync calls, halving round-trips
        // and letting Postgres aggregate in-place. The MGPS "latest"
        // and RDS count stay separate (they need different orderings /
        // semantics). 6 sequential queries → 3.
        var pressureCounts = await _db.Set<HealthcarePressureLog>()
            .Where(x => x.ProjectId == projectId && x.CapturedAt >= since)
            .GroupBy(x => 1)
            .Select(g => new {
                Total = g.Count(),
                Fail  = g.Count(x => !x.InBand)
            })
            .FirstOrDefaultAsync();
        var ligCounts = await _db.Set<HealthcareAntiLigatureAudit>()
            .Where(x => x.ProjectId == projectId)
            .GroupBy(x => 1)
            .Select(g => new {
                Total = g.Count(),
                Fail  = g.Count(x => !x.Pass)
            })
            .FirstOrDefaultAsync();
        var mgasLatest = await _db.Set<HealthcareMgasVerification>()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CapturedAt)
            .FirstOrDefaultAsync();
        var rdsCount = await _db.Set<HealthcareRdsSnapshot>()
            .Where(x => x.ProjectId == projectId).CountAsync();
        var pressure = pressureCounts?.Total ?? 0;
        var pressureFail = pressureCounts?.Fail ?? 0;
        var ligTotal = ligCounts?.Total ?? 0;
        var ligFail  = ligCounts?.Fail ?? 0;
        return Ok(new {
            pressure = new { totalLast7d = pressure, breachLast7d = pressureFail,
                              rag = pressureFail > 0 ? "R" : "G" },
            mgas = mgasLatest == null
                ? new { latest = (DateTime?)null, pass = false, rag = "A" }
                : new { latest = (DateTime?)mgasLatest.CapturedAt, pass = mgasLatest.OverallPass,
                        rag = mgasLatest.OverallPass ? "G" : "R" },
            antiLigature = new { totalAudits = ligTotal, failed = ligFail,
                                  rag = ligFail > 0 ? "A" : "G" },
            rdsCount
        });
    }

    // ── Pressure log ─────────────────────────────────────────────────
    [HttpPost("pressure-log")]
    public async Task<IActionResult> PostPressureLog(Guid projectId,
        [FromBody] HealthcarePressureLog body)
    {
        body.ProjectId = projectId;
        _db.Set<HealthcarePressureLog>().Add(body);
        await _db.SaveChangesAsync();
        return Created($"api/projects/{projectId}/healthcare/pressure-log/{body.Id}", body);
    }

    [HttpGet("pressure-log")]
    public async Task<IActionResult> GetPressureLog(Guid projectId,
        [FromQuery] DateTime? since = null, [FromQuery] string? roomBimId = null,
        [FromQuery] string? roomIfcGlobalId = null)
    {
        var q = _db.Set<HealthcarePressureLog>().Where(x => x.ProjectId == projectId);
        if (since.HasValue) q = q.Where(x => x.CapturedAt >= since.Value);
        if (!string.IsNullOrEmpty(roomBimId)) q = q.Where(x => x.RoomBimId == roomBimId);
        if (!string.IsNullOrEmpty(roomIfcGlobalId)) q = q.Where(x => x.RoomIfcGlobalId == roomIfcGlobalId);
        var rows = await q.OrderByDescending(x => x.CapturedAt).Take(500).ToListAsync();
        return Ok(rows);
    }

    // ── MGPS verification ────────────────────────────────────────────
    [HttpPost("mgas-verification")]
    public async Task<IActionResult> PostMgasVerification(Guid projectId,
        [FromBody] HealthcareMgasVerification body)
    {
        body.ProjectId = projectId;
        _db.Set<HealthcareMgasVerification>().Add(body);
        await _db.SaveChangesAsync();
        return Created($"api/projects/{projectId}/healthcare/mgas-verification/{body.Id}", body);
    }

    [HttpGet("mgas-verification")]
    public async Task<IActionResult> GetMgasVerification(Guid projectId,
        [FromQuery] string? zone = null, [FromQuery] string? gasCode = null)
    {
        var q = _db.Set<HealthcareMgasVerification>().Where(x => x.ProjectId == projectId);
        if (!string.IsNullOrEmpty(zone)) q = q.Where(x => x.Zone == zone);
        if (!string.IsNullOrEmpty(gasCode)) q = q.Where(x => x.GasCode == gasCode);
        var rows = await q.OrderByDescending(x => x.CapturedAt).Take(200).ToListAsync();
        return Ok(rows);
    }

    // ── Anti-ligature audit ──────────────────────────────────────────
    [HttpPost("anti-ligature-audit")]
    public async Task<IActionResult> PostLigAudit(Guid projectId,
        [FromBody] HealthcareAntiLigatureAudit body)
    {
        body.ProjectId = projectId;
        _db.Set<HealthcareAntiLigatureAudit>().Add(body);
        await _db.SaveChangesAsync();
        return Created($"api/projects/{projectId}/healthcare/anti-ligature-audit/{body.Id}", body);
    }

    [HttpGet("anti-ligature-audit")]
    public async Task<IActionResult> GetLigAudit(Guid projectId)
    {
        var rows = await _db.Set<HealthcareAntiLigatureAudit>()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CapturedAt).Take(500).ToListAsync();
        return Ok(rows);
    }

    // ── Room Data Sheet snapshots ────────────────────────────────────
    [HttpPost("rds")]
    public async Task<IActionResult> PostRds(Guid projectId,
        [FromBody] HealthcareRdsSnapshot body)
    {
        body.ProjectId = projectId;
        _db.Set<HealthcareRdsSnapshot>().Add(body);
        await _db.SaveChangesAsync();
        return Created($"api/projects/{projectId}/healthcare/rds/{body.Id}", body);
    }

    [HttpGet("rds/{roomBimId}")]
    public async Task<IActionResult> GetRds(Guid projectId, string roomBimId)
    {
        var snap = await _db.Set<HealthcareRdsSnapshot>()
            .Where(x => x.ProjectId == projectId && x.RoomBimId == roomBimId)
            .OrderByDescending(x => x.CapturedAt)
            .FirstOrDefaultAsync();
        if (snap == null) return NotFound();
        return Ok(snap);
    }

    // ── Cross-reference: all healthcare data for an IFC element ──────
    /// <summary>
    /// GET .../healthcare/by-ifc/{ifcGlobalId}
    /// Cross-host lookup: given the canonical IFC GlobalId of a room/space
    /// (the same key carried by <c>ExternalElementMapping</c>), return every
    /// healthcare record linked to it. Pressure logs join directly via
    /// <c>RoomIfcGlobalId</c>; RDS snapshots and anti-ligature audits are
    /// resolved through the room's BIM id(s) discovered on those logs, so a
    /// caller holding only the IFC GlobalId (e.g. a Blender/ArchiCAD viewer)
    /// gets the full room picture without knowing the STING RoomBimId.
    /// </summary>
    [HttpGet("by-ifc/{ifcGlobalId}")]
    public async Task<IActionResult> GetByIfcGlobalId(Guid projectId, string ifcGlobalId)
    {
        if (string.IsNullOrWhiteSpace(ifcGlobalId)) return BadRequest("ifcGlobalId is required");

        var pressureLogs = await _db.Set<HealthcarePressureLog>()
            .Where(x => x.ProjectId == projectId && x.RoomIfcGlobalId == ifcGlobalId)
            .OrderByDescending(x => x.CapturedAt)
            .Take(500)
            .ToListAsync();

        // Resolve the room's BIM id(s) from the matched logs so we can pull the
        // sibling records that key on RoomBimId rather than the IFC GlobalId.
        var roomBimIds = pressureLogs
            .Select(x => x.RoomBimId)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();

        var rds = roomBimIds.Count == 0
            ? new List<HealthcareRdsSnapshot>()
            : await _db.Set<HealthcareRdsSnapshot>()
                .Where(x => x.ProjectId == projectId && roomBimIds.Contains(x.RoomBimId))
                .OrderByDescending(x => x.CapturedAt)
                .ToListAsync();

        var antiLigature = roomBimIds.Count == 0
            ? new List<HealthcareAntiLigatureAudit>()
            : await _db.Set<HealthcareAntiLigatureAudit>()
                .Where(x => x.ProjectId == projectId && roomBimIds.Contains(x.RoomBimId))
                .OrderByDescending(x => x.CapturedAt)
                .ToListAsync();

        return Ok(new
        {
            ifcGlobalId,
            roomBimIds,
            pressureLogs,
            rds,
            antiLigature,
            counts = new
            {
                pressureLogs = pressureLogs.Count,
                rds = rds.Count,
                antiLigature = antiLigature.Count,
            },
        });
    }
}
