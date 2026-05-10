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
        [FromQuery] int limit = 25,
        [FromQuery] string? optionSet = null,
        [FromQuery] string? option = null)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest(new { message = "Query must be at least 2 characters" });

        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        // Phase 178b — drop the in-memory `.ToLower()` pipeline and use
        // EF.Functions.ILike so the case-insensitive substring match is
        // pushed into Postgres and can hit a regular B-tree index +
        // citext / pg_trgm where one is configured. The previous
        // `.ToLower().Contains()` form forced LOWER(col) on every row,
        // defeating any index.
        var pattern = $"%{q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
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
                    (EF.Functions.ILike(t.Tag1!, pattern) || EF.Functions.ILike(t.CategoryName!, pattern)))
                .Select(t => new { Type = "tag", t.Id, Label = t.Tag1, Detail = t.CategoryName, ProjectId = t.ProjectId, ProjectName = t.Project!.Name })
                .Take(limit).ToListAsync();
            results.AddRange(tags);
        }

        // Search issues — Phase 175: optional design-option filter so the
        // mobile inbox can group queries by option set/option, ensuring
        // site queries land against the right alternative when the host
        // doc has multiple façade / fit-out / VE alternatives in flight.
        if (types == null || types.Contains("issue"))
        {
            var qIssues = _db.Issues
                .Where(i => i.Project!.TenantId == tenantId &&
                    (EF.Functions.ILike(i.Title!, pattern)
                     || EF.Functions.ILike(i.IssueCode!, pattern)
                     || (i.Description != null && EF.Functions.ILike(i.Description, pattern))));
            if (!string.IsNullOrWhiteSpace(optionSet))
                qIssues = qIssues.Where(i => i.OptionSetName == optionSet);
            if (!string.IsNullOrWhiteSpace(option))
                qIssues = qIssues.Where(i => i.OptionName == option);
            var issues = await qIssues
                .Select(i => new { Type = "issue", i.Id, Label = $"{i.IssueCode}: {i.Title}", Detail = i.Status, ProjectId = i.ProjectId, ProjectName = i.Project!.Name, OptionSet = i.OptionSetName, Option = i.OptionName })
                .Take(limit).ToListAsync();
            results.AddRange(issues);
        }

        // Search documents
        if (types == null || types.Contains("document"))
        {
            var docs = await _db.Documents
                .Where(d => d.Project!.TenantId == tenantId &&
                    (EF.Functions.ILike(d.FileName!, pattern) || EF.Functions.ILike(d.DocumentType!, pattern)))
                .Select(d => new { Type = "document", d.Id, Label = $"{d.DocumentType}: {d.FileName}", Detail = d.CdeStatus, ProjectId = d.ProjectId, ProjectName = d.Project!.Name })
                .Take(limit).ToListAsync();
            results.AddRange(docs);
        }

        // Search meetings
        if (types == null || types.Contains("meeting"))
        {
            var meetings = await _db.Meetings
                .Where(m => m.Project!.TenantId == tenantId &&
                    (EF.Functions.ILike(m.Title!, pattern)
                     || (m.AgendaJson != null && EF.Functions.ILike(m.AgendaJson, pattern))))
                .Select(m => new { Type = "meeting", m.Id, Label = m.Title, Detail = m.ScheduledAt.ToString("yyyy-MM-dd"), ProjectId = m.ProjectId, ProjectName = m.Project!.Name })
                .Take(limit).ToListAsync();
            results.AddRange(meetings);
        }

        var trimmed = results.Take(limit);
        return Ok(new { query = q, count = trimmed.Count(), results = trimmed });
    }
}
