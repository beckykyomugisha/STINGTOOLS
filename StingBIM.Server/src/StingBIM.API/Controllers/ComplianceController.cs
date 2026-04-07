using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StingBIM.Core.Entities;
using StingBIM.Infrastructure.Data;

namespace StingBIM.API.Controllers;

/// <summary>
/// Compliance snapshot management — stores and retrieves compliance history for trend analysis.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/compliance")]
[Authorize]
public class ComplianceController : ControllerBase
{
    private readonly StingBimDbContext _db;

    public ComplianceController(StingBimDbContext db) => _db = db;

    /// <summary>
    /// Push a compliance snapshot from the Revit plugin.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> PushSnapshot(Guid projectId, [FromBody] PushComplianceRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        var snapshot = new ComplianceSnapshot
        {
            ProjectId = projectId,
            CapturedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            TotalElements = req.TotalElements,
            TaggedComplete = req.TaggedComplete,
            TaggedIncomplete = req.TaggedIncomplete,
            Untagged = req.Untagged,
            FullyResolved = req.FullyResolved,
            StaleCount = req.StaleCount,
            PlaceholderCount = req.PlaceholderCount,
            WarningCount = req.WarningCount,
            WarningHealthScore = req.WarningHealthScore,
            TagPercent = req.TagPercent,
            StrictPercent = req.StrictPercent,
            ContainerPercent = req.ContainerPercent,
            RagStatus = req.RagStatus,
            ByDisciplineJson = req.ByDisciplineJson,
            ByPhaseJson = req.ByPhaseJson,
            EmptyTokenCountsJson = req.EmptyTokenCountsJson
        };

        _db.ComplianceSnapshots.Add(snapshot);

        // Update project cached metrics
        project.CompliancePercent = req.TagPercent;
        project.ContainerCompliancePercent = req.ContainerPercent;
        project.TotalElements = req.TotalElements;
        project.TaggedElements = req.TaggedComplete + req.TaggedIncomplete;
        project.WarningCount = req.WarningCount;
        project.RagStatus = req.RagStatus;

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId,
            Action = "compliance_snapshot_pushed",
            EntityType = "ComplianceSnapshot",
            EntityId = snapshot.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { req.TagPercent, req.StrictPercent, req.ContainerPercent, req.RagStatus, req.TotalElements }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok(new { id = snapshot.Id, capturedAt = snapshot.CapturedAt });
    }

    /// <summary>
    /// Get the latest compliance snapshot.
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult> GetLatest(Guid projectId)
    {
        var tenantId = GetTenantId();
        var snapshot = await _db.ComplianceSnapshots
            .Where(s => s.ProjectId == projectId && s.Project!.TenantId == tenantId)
            .OrderByDescending(s => s.CapturedAt)
            .FirstOrDefaultAsync();

        if (snapshot == null) return NotFound("No compliance data yet");
        return Ok(snapshot);
    }

    /// <summary>
    /// Get compliance history for trend analysis.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult> GetHistory(Guid projectId,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 100)
    {
        var tenantId = GetTenantId();
        var query = _db.ComplianceSnapshots
            .Where(s => s.ProjectId == projectId && s.Project!.TenantId == tenantId);

        if (from.HasValue) query = query.Where(s => s.CapturedAt >= from.Value);
        if (to.HasValue) query = query.Where(s => s.CapturedAt <= to.Value);

        var snapshots = await query
            .OrderByDescending(s => s.CapturedAt)
            .Take(limit)
            .Select(s => new
            {
                s.CapturedAt, s.TagPercent, s.StrictPercent, s.ContainerPercent,
                s.RagStatus, s.TotalElements, s.StaleCount, s.WarningCount,
                s.WarningHealthScore, s.CapturedBy
            })
            .ToListAsync();

        return Ok(snapshots);
    }

    /// <summary>
    /// Get compliance trend (daily averages for charting).
    /// </summary>
    [HttpGet("trend")]
    public async Task<ActionResult> GetTrend(Guid projectId, [FromQuery] int days = 30)
    {
        var tenantId = GetTenantId();
        var since = DateTime.UtcNow.AddDays(-days);

        var trend = await _db.ComplianceSnapshots
            .Where(s => s.ProjectId == projectId && s.Project!.TenantId == tenantId && s.CapturedAt >= since)
            .GroupBy(s => s.CapturedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                AvgTagPercent = g.Average(s => s.TagPercent),
                AvgStrictPercent = g.Average(s => s.StrictPercent),
                AvgContainerPercent = g.Average(s => s.ContainerPercent),
                MaxWarnings = g.Max(s => s.WarningCount),
                Snapshots = g.Count()
            })
            .OrderBy(t => t.Date)
            .ToListAsync();

        return Ok(trend);
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record PushComplianceRequest
{
    public int TotalElements { get; init; }
    public int TaggedComplete { get; init; }
    public int TaggedIncomplete { get; init; }
    public int Untagged { get; init; }
    public int FullyResolved { get; init; }
    public int StaleCount { get; init; }
    public int PlaceholderCount { get; init; }
    public int WarningCount { get; init; }
    public int WarningHealthScore { get; init; }
    public double TagPercent { get; init; }
    public double StrictPercent { get; init; }
    public double ContainerPercent { get; init; }
    public string RagStatus { get; init; } = "RED";
    public string? ByDisciplineJson { get; init; }
    public string? ByPhaseJson { get; init; }
    public string? EmptyTokenCountsJson { get; init; }
}
