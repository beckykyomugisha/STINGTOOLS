namespace Planscape.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

[ApiController]
[Route("api/projects/{projectId:guid}/clashes")]
[Authorize]
public class ClashesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClashDetectionJob _job;

    public ClashesController(PlanscapeDbContext db, ITenantContext tenant, IClashDetectionJob job)
    { _db = db; _tenant = tenant; _job = job; }

    // GET /api/projects/{id}/clashes?status=NEW&severity=CRITICAL&pageSize=50
    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, [FromQuery] string? status, [FromQuery] string? severity,
        [FromQuery] string? discipline, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var q = _db.ClashRecords.AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.TenantId == _tenant.TenantId);

        if (Enum.TryParse<ClashStatus>(status, true, out var st)) q = q.Where(c => c.Status == st);
        if (Enum.TryParse<ClashSeverity>(severity, true, out var sv)) q = q.Where(c => c.Severity == sv);
        if (!string.IsNullOrEmpty(discipline))
            q = q.Where(c => c.DisciplineA == discipline || c.DisciplineB == discipline);

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(c => c.Severity).ThenByDescending(c => c.DetectedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var summary = new {
            total,
            byStatus = await q.GroupBy(c => c.Status).Select(g => new { status = g.Key.ToString(), count = g.Count() }).ToListAsync(ct),
            bySeverity = await q.GroupBy(c => c.Severity).Select(g => new { severity = g.Key.ToString(), count = g.Count() }).ToListAsync(ct),
        };

        return Ok(new { summary, page, pageSize, items = rows });
    }

    // GET /api/projects/{id}/clashes/{clashId}
    [HttpGet("{clashId:guid}")]
    public async Task<ActionResult<ClashRecord>> Get(Guid projectId, Guid clashId, CancellationToken ct)
    {
        var row = await _db.ClashRecords.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clashId && c.ProjectId == projectId && c.TenantId == _tenant.TenantId, ct);
        return row == null ? NotFound() : Ok(row);
    }

    // POST /api/projects/{id}/clashes/run — kick a new detection run
    [HttpPost("run")]
    public async Task<ActionResult<ClashDetectionResult>> Run(Guid projectId, CancellationToken ct)
    {
        var result = await _job.RunAsync(projectId, _tenant.TenantId, ct);
        return Ok(result);
    }

    // PATCH /api/projects/{id}/clashes/{clashId} — update status, assignment, resolution
    public sealed record ClashUpdateDto(string? Status, string? AssignedTo, string? ResolutionNote);

    [HttpPatch("{clashId:guid}")]
    public async Task<ActionResult<ClashRecord>> Update(Guid projectId, Guid clashId, [FromBody] ClashUpdateDto dto, CancellationToken ct)
    {
        var row = await _db.ClashRecords
            .FirstOrDefaultAsync(c => c.Id == clashId && c.ProjectId == projectId && c.TenantId == _tenant.TenantId, ct);
        if (row == null) return NotFound();

        if (dto.Status != null && Enum.TryParse<ClashStatus>(dto.Status, true, out var st))
        {
            row.Status = st;
            if (st == ClashStatus.Acknowledged) row.AcknowledgedAt = DateTime.UtcNow;
            if (st == ClashStatus.Resolved) row.ResolvedAt = DateTime.UtcNow;
            if (st == ClashStatus.Closed) row.ClosedAt = DateTime.UtcNow;
        }
        if (dto.AssignedTo != null) row.AssignedTo = dto.AssignedTo;
        if (dto.ResolutionNote != null) row.ResolutionNote = dto.ResolutionNote;

        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    // POST /api/projects/{id}/clashes/{clashId}/promote-to-issue
    [HttpPost("{clashId:guid}/promote-to-issue")]
    public async Task<ActionResult<BimIssue>> PromoteToIssue(Guid projectId, Guid clashId, CancellationToken ct)
    {
        var clash = await _db.ClashRecords
            .FirstOrDefaultAsync(c => c.Id == clashId && c.ProjectId == projectId && c.TenantId == _tenant.TenantId, ct);
        if (clash == null) return NotFound();
        if (clash.IssueId.HasValue) return BadRequest("Already promoted to issue");

        var issue = new BimIssue
        {
            TenantId = _tenant.TenantId,
            ProjectId = projectId,
            Title = $"Clash: {clash.DisciplineA ?? "?"} ↔ {clash.DisciplineB ?? "?"} — {clash.Severity}",
            Description = $"Auto-promoted from clash record {clash.Id}. Overlap volume {clash.OverlapVolumeMm3:N0} mm³, depth {clash.DistanceMm:N1} mm.",
            Type = "CLASH",
            Status = "OPEN",
            Priority = clash.Severity == ClashSeverity.Critical ? "HIGH" : clash.Severity == ClashSeverity.Major ? "MEDIUM" : "LOW",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "clash-detector",
            Discipline = clash.DisciplineA,
        };

        _db.Issues.Add(issue);
        clash.IssueId = issue.Id;
        clash.Status = ClashStatus.Acknowledged;
        clash.AcknowledgedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(issue);
    }
}
