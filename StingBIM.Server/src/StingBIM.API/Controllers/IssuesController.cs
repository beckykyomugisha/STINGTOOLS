using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StingBIM.Core.Entities;
using StingBIM.Infrastructure.Data;

namespace StingBIM.API.Controllers;

[ApiController]
[Route("api/projects/{projectId}/[controller]")]
[Authorize]
public class IssuesController : ControllerBase
{
    private readonly StingBimDbContext _db;
    private static readonly Dictionary<string, int> SLAHours = new()
    {
        ["CRITICAL"] = 4, ["HIGH"] = 24, ["MEDIUM"] = 168, ["LOW"] = 336
    };

    public IssuesController(StingBimDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult> GetIssues(Guid projectId,
        [FromQuery] string? status = null, [FromQuery] string? type = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = GetTenantId();
        var query = _db.Issues.Where(i => i.ProjectId == projectId && i.Project!.TenantId == tenantId);

        if (!string.IsNullOrEmpty(status)) query = query.Where(i => i.Status == status);
        if (!string.IsNullOrEmpty(type)) query = query.Where(i => i.Type == type);

        var total = await query.CountAsync();
        var issues = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new
            {
                i.Id, i.IssueCode, i.Type, i.Title, i.Priority, i.Status,
                i.Assignee, i.Discipline, i.Revision, i.CreatedBy, i.CreatedAt, i.DueDate, i.ResolvedAt,
                IsOverdue = i.DueDate.HasValue && i.DueDate < DateTime.UtcNow && i.Status != "CLOSED" && i.Status != "RESOLVED",
                DaysOpen = (int)(DateTime.UtcNow - i.CreatedAt).TotalDays
            })
            .ToListAsync();

        return Ok(new { issues, total, page, pageSize });
    }

    [HttpPost]
    public async Task<ActionResult> CreateIssue(Guid projectId, [FromBody] CreateIssueRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        // Auto-generate collision-safe issue code — scan max numeric suffix
        var existingCodes = await _db.Issues
            .Where(i => i.ProjectId == projectId && i.Type == req.Type)
            .Select(i => i.IssueCode)
            .ToListAsync();

        int nextNum = 1;
        foreach (var code in existingCodes)
        {
            var parts = code.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[1], out int n) && n >= nextNum)
                nextNum = n + 1;
        }

        var issue = new BimIssue
        {
            ProjectId = projectId,
            IssueCode = $"{req.Type}-{nextNum:D4}",
            Type = req.Type,
            Title = req.Title,
            Description = req.Description,
            Priority = req.Priority ?? "MEDIUM",
            Assignee = req.Assignee,
            Discipline = req.Discipline,
            CreatedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            LinkedElementIds = req.LinkedElementIds,
            DueDate = ComputeSLADeadline(req.Priority ?? "MEDIUM")
        };

        _db.Issues.Add(issue);

        // Audit trail for issue creation — ISO 19650 compliance
        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId,
            Action = "issue_created",
            EntityType = "BimIssue",
            EntityId = issue.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { issue.IssueCode, issue.Type, issue.Title, issue.Priority, issue.Assignee }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetIssues), new { projectId }, issue);
    }

    [HttpPut("{issueId}")]
    public async Task<ActionResult> UpdateIssue(Guid projectId, Guid issueId, [FromBody] UpdateIssueRequest req)
    {
        var tenantId = GetTenantId();
        var issue = await _db.Issues
            .FirstOrDefaultAsync(i => i.Id == issueId && i.ProjectId == projectId && i.Project!.TenantId == tenantId);
        if (issue == null) return NotFound();

        var oldStatus = issue.Status;
        var oldPriority = issue.Priority;

        if (req.Status != null)
        {
            issue.Status = req.Status;
            if (req.Status is "RESOLVED" or "CLOSED")
                issue.ResolvedAt = DateTime.UtcNow;
        }
        if (req.Priority != null) issue.Priority = req.Priority;
        if (req.Assignee != null) issue.Assignee = req.Assignee;
        if (req.Description != null) issue.Description = req.Description;

        // Audit trail for issue update
        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId,
            Action = "issue_updated",
            EntityType = "BimIssue",
            EntityId = issueId.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                issue.IssueCode,
                statusChange = req.Status != null ? $"{oldStatus}→{req.Status}" : null,
                priorityChange = req.Priority != null ? $"{oldPriority}→{req.Priority}" : null,
                assignee = req.Assignee
            }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok(issue);
    }

    [HttpGet("sla")]
    public async Task<ActionResult> GetSLAReport(Guid projectId)
    {
        var tenantId = GetTenantId();
        var issues = await _db.Issues
            .Where(i => i.ProjectId == projectId && i.Project!.TenantId == tenantId
                && i.Status != "CLOSED" && i.Status != "RESOLVED")
            .ToListAsync();

        var violations = issues.Where(i => i.DueDate.HasValue && i.DueDate < DateTime.UtcNow).ToList();
        var byPriority = issues.GroupBy(i => i.Priority)
            .ToDictionary(g => g.Key, g => new { total = g.Count(), overdue = g.Count(i => i.DueDate < DateTime.UtcNow) });

        return Ok(new
        {
            totalOpen = issues.Count,
            violations = violations.Count,
            byPriority,
            oldestOverdue = violations.OrderBy(v => v.DueDate).FirstOrDefault()?.IssueCode,
            avgAgeHours = violations.Any() ? violations.Average(v => (DateTime.UtcNow - v.CreatedAt).TotalHours) : 0
        });
    }

    private static DateTime ComputeSLADeadline(string priority)
    {
        int hours = SLAHours.GetValueOrDefault(priority, 168);
        return DateTime.UtcNow.AddHours(hours);
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record CreateIssueRequest(string Type, string Title, string? Description, string? Priority, string? Assignee, string? Discipline, string? LinkedElementIds);
public record UpdateIssueRequest(string? Status, string? Priority, string? Assignee, string? Description);
