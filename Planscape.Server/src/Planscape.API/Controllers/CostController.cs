using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>P5 — Cost tracking (Budget / Committed / Actual / Forecast).</summary>
[ApiController]
[Route("api/projects/{projectId:guid}/cost")]
[Authorize]
public class CostController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public CostController(PlanscapeDbContext db) => _db = db;

    [HttpGet("items")]
    public async Task<ActionResult> List(Guid projectId, [FromQuery] string? discipline, [FromQuery] string? kind, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var q = _db.CostItems.AsNoTracking().Where(c => c.ProjectId == projectId);
        if (!string.IsNullOrEmpty(discipline)) q = q.Where(c => c.Discipline == discipline);
        if (!string.IsNullOrEmpty(kind) && Enum.TryParse<CostKind>(kind, true, out var k))
            q = q.Where(c => c.Kind == k);
        return Ok(await q.OrderBy(c => c.Code).ToListAsync(ct));
    }

    [HttpPost("items")]
    [Authorize(Roles = "Admin,Owner,Coordinator,Manager")]
    public async Task<ActionResult> Create(Guid projectId, [FromBody] CostItem item, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        item.ProjectId = projectId;
        item.LineTotal = (decimal)item.Quantity * item.UnitRate;
        item.CreatedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        _db.CostItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new { projectId }, item);
    }

    [HttpPut("items/{itemId:guid}")]
    [Authorize(Roles = "Admin,Owner,Coordinator,Manager")]
    public async Task<ActionResult> Update(Guid projectId, Guid itemId, [FromBody] CostItem req, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var row = await _db.CostItems.FirstOrDefaultAsync(c => c.Id == itemId && c.ProjectId == projectId, ct);
        if (row == null) return NotFound();
        row.Code = req.Code;
        row.Description = req.Description;
        row.Discipline = req.Discipline;
        row.TradeBucket = req.TradeBucket;
        row.ScheduleTaskId = req.ScheduleTaskId;
        row.Unit = req.Unit;
        row.Quantity = req.Quantity;
        row.UnitRate = req.UnitRate;
        row.LineTotal = (decimal)req.Quantity * req.UnitRate;
        row.Currency = req.Currency;
        row.Kind = req.Kind;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpDelete("items/{itemId:guid}")]
    [Authorize(Roles = "Admin,Owner,Coordinator,Manager")]
    public async Task<IActionResult> Delete(Guid projectId, Guid itemId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var row = await _db.CostItems.FirstOrDefaultAsync(c => c.Id == itemId && c.ProjectId == projectId, ct);
        if (row == null) return NotFound();
        _db.CostItems.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("summary")]
    public async Task<ActionResult> Summary(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var rows = await _db.CostItems.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .GroupBy(c => new { c.Discipline, c.Kind })
            .Select(g => new
            {
                g.Key.Discipline,
                Kind  = g.Key.Kind.ToString(),
                Total = g.Sum(c => c.LineTotal),
                Count = g.Count(),
            })
            .OrderBy(r => r.Discipline).ThenBy(r => r.Kind)
            .ToListAsync(ct);
        return Ok(rows);
    }

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        if (tenantId == Guid.Empty) return false;
        return await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
    }
}
