using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Workflow execution history — receives run records from the Revit plugin for tracking and trend analysis.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/workflows")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public WorkflowsController(PlanscapeDbContext db) => _db = db;

    /// <summary>
    /// Log a workflow execution from the Revit plugin.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult> LogRun(Guid projectId, [FromBody] LogWorkflowRunRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        var run = new WorkflowRun
        {
            ProjectId = projectId,
            PresetName = req.PresetName,
            UserName = User.FindFirst("display_name")?.Value ?? req.UserName ?? "Unknown",
            StepsPassed = req.StepsPassed,
            StepsFailed = req.StepsFailed,
            StepsSkipped = req.StepsSkipped,
            DurationMs = req.DurationMs,
            ComplianceBefore = req.ComplianceBefore,
            ComplianceAfter = req.ComplianceAfter,
            StepResultsJson = req.StepResultsJson
        };

        _db.WorkflowRuns.Add(run);

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId,
            Action = "workflow_run_logged",
            EntityType = "WorkflowRun",
            EntityId = run.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { run.PresetName, run.StepsPassed, run.StepsFailed, run.DurationMs }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetHistory), new { projectId }, new { id = run.Id, executedAt = run.ExecutedAt });
    }

    /// <summary>
    /// Get workflow execution history.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult> GetHistory(Guid projectId, [FromQuery] int limit = 50)
    {
        var tenantId = GetTenantId();
        var runs = await _db.WorkflowRuns
            .Where(w => w.ProjectId == projectId && w.Project!.TenantId == tenantId)
            .OrderByDescending(w => w.ExecutedAt)
            .Take(limit)
            .Select(w => new
            {
                w.Id, w.PresetName, w.UserName, w.StepsPassed, w.StepsFailed, w.StepsSkipped,
                w.DurationMs, w.ComplianceBefore, w.ComplianceAfter, w.ExecutedAt,
                ComplianceDelta = w.ComplianceAfter - w.ComplianceBefore
            })
            .ToListAsync();

        return Ok(runs);
    }

    /// <summary>
    /// Get compliance trend from workflow runs (compliance before/after over time).
    /// </summary>
    [HttpGet("trend")]
    public async Task<ActionResult> GetTrend(Guid projectId, [FromQuery] int days = 30)
    {
        var tenantId = GetTenantId();
        var since = DateTime.UtcNow.AddDays(-days);

        var trend = await _db.WorkflowRuns
            .Where(w => w.ProjectId == projectId && w.Project!.TenantId == tenantId && w.ExecutedAt >= since)
            .OrderBy(w => w.ExecutedAt)
            .Select(w => new
            {
                w.ExecutedAt, w.PresetName, w.ComplianceBefore, w.ComplianceAfter,
                Delta = w.ComplianceAfter - w.ComplianceBefore
            })
            .ToListAsync();

        return Ok(new
        {
            trend,
            totalRuns = trend.Count,
            avgDelta = trend.Any() ? trend.Average(t => t.Delta) : 0,
            latestCompliance = trend.LastOrDefault()?.ComplianceAfter ?? 0
        });
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record LogWorkflowRunRequest
{
    public string PresetName { get; init; } = "";
    public string? UserName { get; init; }
    public int StepsPassed { get; init; }
    public int StepsFailed { get; init; }
    public int StepsSkipped { get; init; }
    public double DurationMs { get; init; }
    public double ComplianceBefore { get; init; }
    public double ComplianceAfter { get; init; }
    public string? StepResultsJson { get; init; }
}
