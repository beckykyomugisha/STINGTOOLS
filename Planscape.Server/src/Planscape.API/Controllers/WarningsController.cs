using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// Warning report management — stores and queries model warning baselines and trends.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/warnings")]
[Authorize]
[ProjectAccess]
public class WarningsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public WarningsController(PlanscapeDbContext db) => _db = db;

    /// <summary>
    /// Push a warning report/baseline from the Revit plugin.
    /// </summary>
    [HttpPost("report")]
    public async Task<ActionResult> PushReport(Guid projectId, [FromBody] PushWarningReportRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        // Update project cached warning count
        project.WarningCount = req.TotalWarnings;
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTrend), new { projectId }, new
        {
            projectId,
            totalWarnings = req.TotalWarnings,
            healthScore = req.HealthScore,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Save a warning baseline snapshot for trend comparison.
    /// </summary>
    [HttpPost("baseline")]
    public async Task<ActionResult> SaveBaseline(Guid projectId, [FromBody] SaveWarningBaselineRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        // Store baseline as a compliance snapshot with warning focus
        var snapshot = new Core.Entities.ComplianceSnapshot
        {
            ProjectId = projectId,
            CapturedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            WarningCount = req.WarningCount,
            WarningHealthScore = req.HealthScore,
            TotalElements = req.TotalElements,
            TagPercent = req.CompliancePercent,
            RagStatus = req.WarningCount == 0 ? "GREEN" : req.HealthScore >= 80 ? "GREEN" : req.HealthScore >= 50 ? "AMBER" : "RED"
        };

        _db.ComplianceSnapshots.Add(snapshot);
        await _db.SaveChangesAsync();

        return Ok(new { id = snapshot.Id, capturedAt = snapshot.CapturedAt });
    }

    /// <summary>
    /// Get warning trend data (warning count + health score over time).
    /// </summary>
    [HttpGet("trend")]
    public async Task<ActionResult> GetTrend(Guid projectId, [FromQuery] int days = 30)
    {
        var tenantId = GetTenantId();
        var since = DateTime.UtcNow.AddDays(-days);

        var trend = await _db.ComplianceSnapshots
            .Where(s => s.ProjectId == projectId && s.Project!.TenantId == tenantId
                && s.CapturedAt >= since && s.WarningCount > 0)
            .OrderBy(s => s.CapturedAt)
            .Select(s => new
            {
                s.CapturedAt, s.WarningCount, s.WarningHealthScore, s.CapturedBy
            })
            .ToListAsync();

        return Ok(trend);
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record PushWarningReportRequest(int TotalWarnings, int HealthScore, string? ByCategoryJson, string? BySeverityJson);
public record SaveWarningBaselineRequest(int WarningCount, int HealthScore, int TotalElements, double CompliancePercent);
