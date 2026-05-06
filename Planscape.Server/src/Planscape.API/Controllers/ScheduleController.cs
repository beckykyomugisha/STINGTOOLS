using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>P4 — Project schedule tasks (Gantt + RIBA-stage milestones).</summary>
[ApiController]
[Route("api/projects/{projectId:guid}/schedule")]
[Authorize]
[ProjectAccess]
public class ScheduleController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public ScheduleController(PlanscapeDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, [FromQuery] int? stage, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var q = _db.ScheduleTasks.AsNoTracking().Where(t => t.ProjectId == projectId);
        if (stage.HasValue) q = q.Where(t => t.RibaStage == stage);
        return Ok(await q.OrderBy(t => t.PlannedStart).ToListAsync(ct));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Owner,Coordinator,Manager")]
    public async Task<ActionResult> Create(Guid projectId, [FromBody] ScheduleTask row, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        row.ProjectId = projectId;
        row.CreatedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        _db.ScheduleTasks.Add(row);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new { projectId }, row);
    }

    [HttpPut("{taskId:guid}")]
    [Authorize(Roles = "Admin,Owner,Coordinator,Manager")]
    public async Task<ActionResult> Update(Guid projectId, Guid taskId, [FromBody] ScheduleTask req, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var t = await _db.ScheduleTasks.FirstOrDefaultAsync(x => x.Id == taskId && x.ProjectId == projectId, ct);
        if (t == null) return NotFound();
        t.Code = req.Code;
        t.Name = req.Name;
        t.Description = req.Description;
        t.RibaStage = req.RibaStage;
        t.Discipline = req.Discipline;
        t.PlannedStart = req.PlannedStart;
        t.PlannedFinish = req.PlannedFinish;
        t.ActualStart = req.ActualStart;
        t.ActualFinish = req.ActualFinish;
        t.BaselineStart = req.BaselineStart;
        t.BaselineFinish = req.BaselineFinish;
        t.PercentComplete = Math.Clamp(req.PercentComplete, 0, 100);
        t.PredecessorIds = req.PredecessorIds;
        t.IsMilestone = req.IsMilestone;
        t.LinkedMetric = req.LinkedMetric;
        t.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpDelete("{taskId:guid}")]
    [Authorize(Roles = "Admin,Owner,Coordinator,Manager")]
    public async Task<IActionResult> Delete(Guid projectId, Guid taskId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var t = await _db.ScheduleTasks.FirstOrDefaultAsync(x => x.Id == taskId && x.ProjectId == projectId, ct);
        if (t == null) return NotFound();
        _db.ScheduleTasks.Remove(t);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Per-RIBA-stage roll-up used by the dashboard sparkline.</summary>
    [HttpGet("rollup")]
    public async Task<ActionResult> RollUp(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var rows = await _db.ScheduleTasks.AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .GroupBy(t => t.RibaStage ?? -1)
            .Select(g => new
            {
                Stage = g.Key,
                Count = g.Count(),
                AvgPct = g.Average(t => t.PercentComplete),
                Completed = g.Count(t => t.PercentComplete >= 99.9),
                Overdue = g.Count(t => t.PlannedFinish != null && t.PlannedFinish < DateTime.UtcNow && t.PercentComplete < 99.9),
            })
            .OrderBy(r => r.Stage)
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
