using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StingBIM.Infrastructure.Data;

namespace StingBIM.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly StingBimDbContext _db;
    public SearchController(StingBimDbContext db) => _db = db;

    /// <summary>
    /// Global cross-project search across tags, issues, documents, meetings.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> Search([FromQuery] string q, [FromQuery] int limit = 25)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest(new { message = "Query must be at least 2 characters" });

        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        var term = q.ToLowerInvariant();
        limit = Math.Clamp(limit, 1, 100);

        // Pre-fetch tenant's project IDs to avoid repeated navigation property joins
        var projectIds = await _db.Projects
            .Where(p => p.TenantId == tenantId)
            .Select(p => p.Id)
            .ToListAsync();

        if (projectIds.Count == 0)
            return Ok(new { query = q, count = 0, results = Array.Empty<object>() });

        // Search tagged elements — use Include to avoid N+1 on Project.Name
        var tags = await _db.TaggedElements
            .Include(t => t.Project)
            .Where(t => projectIds.Contains(t.ProjectId) &&
                (t.Tag1!.ToLower().Contains(term) || t.CategoryName!.ToLower().Contains(term)))
            .Select(t => new { Type = "tag", t.Id, Label = t.Tag1, Detail = t.CategoryName, ProjectId = t.ProjectId, ProjectName = t.Project!.Name })
            .Take(limit).ToListAsync();

        // Search issues
        var issues = await _db.Issues
            .Include(i => i.Project)
            .Where(i => projectIds.Contains(i.ProjectId) &&
                (i.Title!.ToLower().Contains(term) || i.IssueCode!.ToLower().Contains(term) || (i.Description ?? "").ToLower().Contains(term)))
            .Select(i => new { Type = "issue", i.Id, Label = $"{i.IssueCode}: {i.Title}", Detail = i.Status, ProjectId = i.ProjectId, ProjectName = i.Project!.Name })
            .Take(limit).ToListAsync();

        // Search documents
        var docs = await _db.Documents
            .Include(d => d.Project)
            .Where(d => projectIds.Contains(d.ProjectId) &&
                (d.FileName!.ToLower().Contains(term) || d.DocumentType!.ToLower().Contains(term)))
            .Select(d => new { Type = "document", d.Id, Label = $"{d.DocumentType}: {d.FileName}", Detail = d.CdeStatus, ProjectId = d.ProjectId, ProjectName = d.Project!.Name })
            .Take(limit).ToListAsync();

        // Search meetings
        var meetings = await _db.Meetings
            .Include(m => m.Project)
            .Where(m => projectIds.Contains(m.ProjectId) &&
                (m.Title!.ToLower().Contains(term) || (m.AgendaJson ?? "").ToLower().Contains(term)))
            .Select(m => new { Type = "meeting", m.Id, Label = m.Title, Detail = m.ScheduledAt.ToString("yyyy-MM-dd"), ProjectId = m.ProjectId, ProjectName = m.Project!.Name })
            .Take(limit).ToListAsync();

        var results = tags.Cast<object>()
            .Concat(issues).Concat(docs).Concat(meetings)
            .Take(limit);

        return Ok(new { query = q, count = results.Count(), results });
    }
}
