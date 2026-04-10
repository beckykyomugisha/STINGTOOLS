using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public SearchController(PlanscapeDbContext db) => _db = db;

    /// <summary>
    /// Global cross-project search across tags, issues, documents, meetings.
    /// Optional type filter: tag, issue, document, meeting (comma-separated for multiple).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> Search(
        [FromQuery] string q,
        [FromQuery] string? type = null,
        [FromQuery] int limit = 25)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest(new { message = "Query must be at least 2 characters" });

        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        var term = q.ToLowerInvariant();
        limit = Math.Clamp(limit, 1, 100);

        // Parse type filter — null means search all types
        var types = string.IsNullOrWhiteSpace(type)
            ? null
            : type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Select(t => t.ToLowerInvariant()).ToHashSet();

        var results = new List<object>();

        // Search tagged elements
        if (types == null || types.Contains("tag"))
        {
            var tags = await _db.TaggedElements
                .Where(t => t.Project!.TenantId == tenantId &&
                    (t.Tag1!.ToLower().Contains(term) || t.CategoryName!.ToLower().Contains(term)))
                .Select(t => new { Type = "tag", t.Id, Label = t.Tag1, Detail = t.CategoryName, ProjectId = t.ProjectId, ProjectName = t.Project!.Name })
                .Take(limit).ToListAsync();
            results.AddRange(tags);
        }

        // Search issues
        if (types == null || types.Contains("issue"))
        {
            var issues = await _db.Issues
                .Where(i => i.Project!.TenantId == tenantId &&
                    (i.Title!.ToLower().Contains(term) || i.IssueCode!.ToLower().Contains(term) || (i.Description ?? "").ToLower().Contains(term)))
                .Select(i => new { Type = "issue", i.Id, Label = $"{i.IssueCode}: {i.Title}", Detail = i.Status, ProjectId = i.ProjectId, ProjectName = i.Project!.Name })
                .Take(limit).ToListAsync();
            results.AddRange(issues);
        }

        // Search documents
        if (types == null || types.Contains("document"))
        {
            var docs = await _db.Documents
                .Where(d => d.Project!.TenantId == tenantId &&
                    (d.FileName!.ToLower().Contains(term) || d.DocumentType!.ToLower().Contains(term)))
                .Select(d => new { Type = "document", d.Id, Label = $"{d.DocumentType}: {d.FileName}", Detail = d.CdeStatus, ProjectId = d.ProjectId, ProjectName = d.Project!.Name })
                .Take(limit).ToListAsync();
            results.AddRange(docs);
        }

        // Search meetings
        if (types == null || types.Contains("meeting"))
        {
            var meetings = await _db.Meetings
                .Where(m => m.Project!.TenantId == tenantId &&
                    (m.Title!.ToLower().Contains(term) || (m.AgendaJson ?? "").ToLower().Contains(term)))
                .Select(m => new { Type = "meeting", m.Id, Label = m.Title, Detail = m.ScheduledAt.ToString("yyyy-MM-dd"), ProjectId = m.ProjectId, ProjectName = m.Project!.Name })
                .Take(limit).ToListAsync();
            results.AddRange(meetings);
        }

        var trimmed = results.Take(limit);
        return Ok(new { query = q, count = trimmed.Count(), results = trimmed });
    }
}
