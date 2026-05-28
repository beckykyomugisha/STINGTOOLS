using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>Pillar B (6A) — twin alert inbox: list, acknowledge, resolve.</summary>
[ApiController]
[Route("api/projects/{projectId:guid}/twins/alerts")]
[Authorize]
public class TwinAlertsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public TwinAlertsController(PlanscapeDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<object>> List(
        Guid projectId, [FromQuery] string? status = "OPEN", [FromQuery] int max = 200, CancellationToken ct = default)
    {
        max = Math.Clamp(max, 1, 1000);
        var q = _db.TwinAlerts.Where(a => a.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);
        var rows = await q.OrderByDescending(a => a.FiredAt).Take(max).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("{alertId:guid}/ack")]
    public async Task<IActionResult> Ack(Guid projectId, Guid alertId, CancellationToken ct)
    {
        var a = await _db.TwinAlerts.FirstOrDefaultAsync(x => x.Id == alertId && x.ProjectId == projectId, ct);
        if (a is null) return NotFound();
        if (a.Status == "OPEN") { a.Status = "ACKNOWLEDGED"; a.AcknowledgedAt = DateTime.UtcNow; a.AcknowledgedByUserId = GetUserId(); }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{alertId:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid projectId, Guid alertId, CancellationToken ct)
    {
        var a = await _db.TwinAlerts.FirstOrDefaultAsync(x => x.Id == alertId && x.ProjectId == projectId, ct);
        if (a is null) return NotFound();
        a.Status = "RESOLVED"; a.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Guid? GetUserId()
    {
        var c = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return c != null && Guid.TryParse(c, out var id) ? id : null;
    }
}
