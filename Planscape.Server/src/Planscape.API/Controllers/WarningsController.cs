using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;
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
    private readonly IHubContext<NotificationHub> _hub;

    public WarningsController(PlanscapeDbContext db, IHubContext<NotificationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

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
        var prev = project.WarningCount;
        project.WarningCount = req.TotalWarnings;

        // IM-11: a report is a measurement, so it becomes a point on the warnings trend.
        // Scans run often, so an unchanged report within ReportDedupeWindow of the last
        // one adds nothing — the trend records changes, not the scan cadence.
        var now = DateTime.UtcNow;
        var lastReport = await _db.ComplianceSnapshots
            .Where(s => s.ProjectId == projectId && s.Kind == Core.Entities.ComplianceSnapshot.KindWarnings)
            .OrderByDescending(s => s.CapturedAt)
            .FirstOrDefaultAsync();
        bool duplicate = lastReport != null
            && now - lastReport.CapturedAt < ReportDedupeWindow
            && lastReport.WarningCount == req.TotalWarnings
            && lastReport.WarningHealthScore == req.HealthScore;
        if (!duplicate)
        {
            _db.ComplianceSnapshots.Add(new Core.Entities.ComplianceSnapshot
            {
                ProjectId = projectId,
                Kind = Core.Entities.ComplianceSnapshot.KindWarnings,
                CapturedAt = now,
                CapturedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
                WarningCount = req.TotalWarnings,
                WarningHealthScore = req.HealthScore,
                RagStatus = WarningRag(req.TotalWarnings, req.HealthScore),
            });
        }
        await _db.SaveChangesAsync();

        // Phase 178b — broadcast so the BCC dashboard, mobile inbox,
        // and viewer status pill all surface a warning-count change
        // without polling. Includes the delta so subscribers can
        // highlight regressions vs improvements.
        _ = _hub.Clients.Group($"project-{projectId}").SendAsync("WarningsReported", new {
            projectId,
            totalWarnings = req.TotalWarnings,
            previousWarnings = prev,
            delta = req.TotalWarnings - prev,
            healthScore = req.HealthScore,
            reportedAt = DateTime.UtcNow
        });

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
            RagStatus = WarningRag(req.WarningCount, req.HealthScore)
        };

        _db.ComplianceSnapshots.Add(snapshot);
        await _db.SaveChangesAsync();

        return Ok(new { id = snapshot.Id, capturedAt = snapshot.CapturedAt });
    }

    /// <summary>
    /// Get warning trend data (warning count + health score over time).
    /// </summary>
    [HttpGet]
    [HttpGet("trend")]
    public async Task<ActionResult> GetTrend(Guid projectId, [FromQuery] int days = 30)
    {
        var tenantId = GetTenantId();
        var since = DateTime.UtcNow.AddDays(-days);

        // IM-12: a pushed report is a measurement even at zero warnings, so it is always
        // included — a clean model must not vanish from its own trend. Other snapshots
        // count only when they carry a warning measurement: a compliance snapshot's
        // WarningCount is 0 by default whether or not warnings were measured, and
        // plotting those as zeros would invent clean scans.
        var trend = await _db.ComplianceSnapshots
            .Where(s => s.ProjectId == projectId && s.Project!.TenantId == tenantId
                && s.CapturedAt >= since
                && (s.Kind == Core.Entities.ComplianceSnapshot.KindWarnings
                    || s.WarningCount > 0 || s.WarningHealthScore > 0))
            .OrderBy(s => s.CapturedAt)
            .Select(s => new
            {
                s.CapturedAt, s.WarningCount, s.WarningHealthScore, s.CapturedBy
            })
            .ToListAsync();

        return Ok(trend);
    }

    /// <summary>Unchanged reports inside this window are not stored again.</summary>
    internal static readonly TimeSpan ReportDedupeWindow = TimeSpan.FromMinutes(15);

    internal static string WarningRag(int warnings, int health) =>
        warnings == 0 ? "GREEN" : health >= 80 ? "GREEN" : health >= 50 ? "AMBER" : "RED";

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record PushWarningReportRequest(int TotalWarnings, int HealthScore, string? ByCategoryJson, string? BySeverityJson);
public record SaveWarningBaselineRequest(int WarningCount, int HealthScore, int TotalElements, double CompliancePercent);
