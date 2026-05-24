using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// Template Manager v2 — operation result ingest. The Revit plugin's
/// dashboard publishes every OperationResult here so the server can
/// surface a cross-project template-health view.
///
/// Sister endpoint to /api/projects/{id}/compliance (point-in-time
/// compliance) but scoped to discrete Template Manager runs.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/template-ops")]
[Authorize]
[ProjectAccess]
[EnableRateLimiting("mobile")]
public class TemplateOpsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public TemplateOpsController(PlanscapeDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Push a single Template Manager OperationResult into the audit store.
    /// Fire-and-forget on the plugin side — server returns 201 with id.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> Push(Guid projectId, [FromBody] PushTemplateOpRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (req == null) return BadRequest("Body required");

        var rec = new TemplateOpRecord
        {
            TenantId         = tenantId,
            ProjectId        = projectId,
            Operation        = (req.Operation ?? "").Trim(),
            OperationLabel   = req.OperationLabel ?? "",
            Severity         = req.Severity ?? "Info",
            Headline         = req.Headline ?? "",
            SubHeadline      = req.SubHeadline,
            CompletedUtc     = req.CompletedUtc == default ? DateTime.UtcNow : req.CompletedUtc,
            DurationMs       = req.DurationMs,
            CapturedBy       = req.User ?? User.FindFirst("display_name")?.Value ?? "",
            DocumentPath     = req.DocumentPath,
            DocumentTitle    = req.DocumentTitle,
            CreatedCount     = req.CreatedCount,
            SkippedCount     = req.SkippedCount,
            FailedCount      = req.FailedCount,
            SectionCount     = req.SectionCount,
            CountersJson     = req.Counters != null ? JsonSerializer.Serialize(req.Counters) : null
        };
        _db.TemplateOpRecords.Add(rec);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetRecent), new { projectId }, new { id = rec.Id, completedUtc = rec.CompletedUtc });
    }

    /// <summary>
    /// Recent Template Manager ops for this project (newest first).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> GetRecent(Guid projectId, [FromQuery] int take = 50, [FromQuery] string? severity = null)
    {
        var tenantId = GetTenantId();
        var q = _db.TemplateOpRecords
            .Where(r => r.ProjectId == projectId && r.TenantId == tenantId);
        if (!string.IsNullOrEmpty(severity))
            q = q.Where(r => r.Severity == severity);
        var rows = await q.OrderByDescending(r => r.CompletedUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>
    /// Per-operation roll-up: counts + last-run timestamps.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult> GetSummary(Guid projectId)
    {
        var tenantId = GetTenantId();
        var rows = await _db.TemplateOpRecords
            .Where(r => r.ProjectId == projectId && r.TenantId == tenantId)
            .GroupBy(r => r.Operation)
            .Select(g => new
            {
                operation = g.Key,
                total = g.Count(),
                successes = g.Count(r => r.Severity == "Success"),
                warnings = g.Count(r => r.Severity == "Warning"),
                errors = g.Count(r => r.Severity == "Error"),
                lastRun = g.Max(r => r.CompletedUtc),
                avgDurationMs = g.Average(r => r.DurationMs)
            })
            .OrderByDescending(x => x.lastRun)
            .ToListAsync();
        return Ok(rows);
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record PushTemplateOpRequest
{
    public string? Operation { get; init; }
    public string? OperationLabel { get; init; }
    public string? Severity { get; init; }
    public string? Headline { get; init; }
    public string? SubHeadline { get; init; }
    public DateTime CompletedUtc { get; init; }
    public double DurationMs { get; init; }
    public string? User { get; init; }
    public string? DocumentPath { get; init; }
    public string? DocumentTitle { get; init; }
    public int CreatedCount { get; init; }
    public int SkippedCount { get; init; }
    public int FailedCount { get; init; }
    public int SectionCount { get; init; }
    public Dictionary<string, string>? Counters { get; init; }
}
