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

        // One bucket per type, merged round-robin at the end (see below) rather
        // than one flat list. Concatenating and then truncating meant a type that
        // filled the limit STARVED the ones after it: with ≥25 matching tags and
        // the default limit, a global search could not return a document, an
        // issue or a meeting at all — they were fetched and then dropped.
        var byType = new List<List<object>>();

        // Every sub-query orders before Take. Without it Postgres returns an
        // arbitrary subset of the matches — which rows you see for the same
        // query can differ between identical requests, and it is what raises
        // EF's RowLimitingOperationWithoutOrderByWarning in the logs. Newest
        // first is the useful order for search; Id is the tiebreak that makes it
        // total, so equal timestamps can't reorder between calls.

        // Search tagged elements
        if (types == null || types.Contains("tag"))
        {
            var tags = await _db.TaggedElements
                .Where(t => t.Project!.TenantId == tenantId &&
                    (EF.Functions.ILike(t.Tag1!, pattern) || EF.Functions.ILike(t.CategoryName!, pattern)))
                .OrderByDescending(t => t.SyncedAt).ThenBy(t => t.Id)
                .Select(t => new { Type = "tag", t.Id, Label = t.Tag1, Detail = t.CategoryName, ProjectId = t.ProjectId, ProjectName = t.Project!.Name })
                .Take(limit).ToListAsync();
            byType.Add(tags.Cast<object>().ToList());
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
                .OrderByDescending(i => i.UpdatedAt).ThenBy(i => i.Id)
                .Select(i => new { Type = "issue", i.Id, Label = $"{i.IssueCode}: {i.Title}", Detail = i.Status, ProjectId = i.ProjectId, ProjectName = i.Project!.Name, OptionSet = i.OptionSetName, Option = i.OptionName })
                .Take(limit).ToListAsync();
            byType.Add(issues.Cast<object>().ToList());
        }

        // Search documents
        if (types == null || types.Contains("document"))
        {
            var docs = await _db.Documents
                .Where(d => d.Project!.TenantId == tenantId &&
                    (EF.Functions.ILike(d.FileName!, pattern) || EF.Functions.ILike(d.DocumentType!, pattern)))
                .OrderByDescending(d => d.UploadedAt).ThenBy(d => d.Id)
                .Select(d => new { Type = "document", d.Id, Label = $"{d.DocumentType}: {d.FileName}", Detail = d.CdeStatus, ProjectId = d.ProjectId, ProjectName = d.Project!.Name })
                .Take(limit).ToListAsync();
            byType.Add(docs.Cast<object>().ToList());
        }

        // Search meetings
        if (types == null || types.Contains("meeting"))
        {
            var meetings = await _db.Meetings
                .Where(m => m.Project!.TenantId == tenantId &&
                    // Meeting has no AgendaJson column — fall back to Minutes text
                    // (the only narrative field on the entity).
                    (EF.Functions.ILike(m.Title!, pattern)
                     || (m.Minutes != null && EF.Functions.ILike(m.Minutes, pattern))))
                .OrderByDescending(m => m.ScheduledAt).ThenBy(m => m.Id)
                .Select(m => new { Type = "meeting", m.Id, Label = m.Title, Detail = m.ScheduledAt.ToString("yyyy-MM-dd"), ProjectId = m.ProjectId, ProjectName = m.Project!.Name })
                .Take(limit).ToListAsync();
            byType.Add(meetings.Cast<object>().ToList());
        }

        // Round-robin merge: one result from each type in turn until `limit` is
        // filled. Every type that matched anything is represented, and a type
        // with few matches donates its unused share to the others rather than
        // leaving the response short. Replaces a flat concat + Take, which gave
        // the whole budget to whichever type happened to be searched first.
        var results = Interleave(byType, limit);
        return Ok(new { query = q, count = results.Count, results });
    }

    private static List<object> Interleave(List<List<object>> buckets, int limit)
    {
        var merged = new List<object>(Math.Min(limit, buckets.Sum(b => b.Count)));
        for (var round = 0; merged.Count < limit; round++)
        {
            var placedThisRound = false;
            foreach (var bucket in buckets)
            {
                if (round >= bucket.Count) continue;
                merged.Add(bucket[round]);
                placedThisRound = true;
                if (merged.Count == limit) return merged;
            }
            if (!placedThisRound) break;   // every bucket exhausted
        }
        return merged;
    }
}
