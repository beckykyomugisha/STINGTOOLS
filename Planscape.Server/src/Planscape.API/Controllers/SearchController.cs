using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Cross-entity search across tagged elements, issues, documents, and meetings.
/// Scoped to the authenticated user's tenant.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public SearchController(PlanscapeDbContext db) => _db = db;

    /// <summary>
    /// Global search: GET /api/search?q=...&amp;type=all|elements|issues|documents&amp;projectId=...
    /// Returns up to 50 results per entity type.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> Search(
        [FromQuery] string q = "",
        [FromQuery] string type = "all",
        [FromQuery] Guid? projectId = null,
        [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest(new { message = "Query must be at least 2 characters." });

        limit = Math.Clamp(limit, 1, 50);
        var tenantId = GetTenantId();
        string lower = q.ToLower();

        var results = new
        {
            elements  = (type == "all" || type == "elements")  ? await SearchElements(tenantId, lower, projectId, limit) : null,
            issues    = (type == "all" || type == "issues")    ? await SearchIssues(tenantId, lower, projectId, limit)   : null,
            documents = (type == "all" || type == "documents") ? await SearchDocuments(tenantId, lower, projectId, limit): null
        };
        return Ok(results);
    }

    private async Task<object> SearchElements(Guid tenantId, string q, Guid? projectId, int limit)
    {
        var query = _db.TaggedElements
            .Where(e => e.Project!.TenantId == tenantId
                     && (e.Tag1.ToLower().Contains(q)
                         || e.CategoryName.ToLower().Contains(q)
                         || (e.FamilyName != null && e.FamilyName.ToLower().Contains(q))
                         || (e.Disc != null && e.Disc.ToLower().Contains(q))));

        if (projectId.HasValue) query = query.Where(e => e.ProjectId == projectId);

        return await query.OrderByDescending(e => e.SyncedAt)
            .Take(limit)
            .Select(e => new
            {
                type = "element",
                e.Id, e.Tag1, e.CategoryName, e.FamilyName, e.Disc, e.Loc, e.Lvl,
                e.ProjectId, projectName = e.Project!.Name, e.SyncedAt
            })
            .ToListAsync();
    }

    private async Task<object> SearchIssues(Guid tenantId, string q, Guid? projectId, int limit)
    {
        var query = _db.Issues
            .Where(i => i.Project!.TenantId == tenantId
                     && (i.Title.ToLower().Contains(q)
                         || i.IssueCode.ToLower().Contains(q)
                         || (i.Description != null && i.Description.ToLower().Contains(q))
                         || (i.Assignee != null && i.Assignee.ToLower().Contains(q))));

        if (projectId.HasValue) query = query.Where(i => i.ProjectId == projectId);

        return await query.OrderByDescending(i => i.CreatedAt)
            .Take(limit)
            .Select(i => new
            {
                type = "issue",
                i.Id, i.IssueCode, i.Title, i.Priority, i.Status, i.Assignee,
                i.ProjectId, projectName = i.Project!.Name, i.CreatedAt
            })
            .ToListAsync();
    }

    private async Task<object> SearchDocuments(Guid tenantId, string q, Guid? projectId, int limit)
    {
        var query = _db.Documents
            .Where(d => d.Project!.TenantId == tenantId
                     && (d.FileName.ToLower().Contains(q)
                         || (d.DocumentType != null && d.DocumentType.ToLower().Contains(q))
                         || (d.Discipline != null && d.Discipline.ToLower().Contains(q))));

        if (projectId.HasValue) query = query.Where(d => d.ProjectId == projectId);

        return await query.OrderByDescending(d => d.UploadedAt)
            .Take(limit)
            .Select(d => new
            {
                type = "document",
                d.Id, d.FileName, d.DocumentType, d.CdeStatus, d.Discipline, d.Revision,
                d.ProjectId, projectName = d.Project!.Name, d.UploadedAt
            })
            .ToListAsync();
    }

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return claim != null ? Guid.Parse(claim) : Guid.Empty;
    }
}
