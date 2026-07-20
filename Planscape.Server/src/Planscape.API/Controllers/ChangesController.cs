using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Cursor-paged change feed — the pull half of bidirectional sync
/// (docs/MULTI_HOST_INTEGRATION_PLAN.md §1.4.1).
///
/// Push already existed: every host could send its tags to the hub. Nothing
/// could ask "what changed since I last looked?", so a host had no way to learn
/// about an edit made anywhere else. StingBridge worked around that by fetching
/// timestamps for elements it happened to be holding and applying a
/// recent-writer heuristic, which cannot see an element the local model does
/// not already have.
///
/// The cursor is <c>{ticks}_{guid}</c> of the last row returned, not a bare
/// timestamp. A bare timestamp cannot express "I have seen some of the rows at
/// this instant": bulk writes routinely share a millisecond, so resuming at
/// <c>&gt; timestamp</c> silently skips the rest of that batch, and resuming at
/// <c>&gt;= timestamp</c> loops on it forever. Ordering by (LastModifiedUtc, Id)
/// and comparing the pair makes the feed exactly-once and resumable.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/changes")]
[Authorize]
[ProjectAccess]
public class ChangesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public ChangesController(PlanscapeDbContext db) => _db = db;

    /// <summary>
    /// GET /api/projects/{projectId}/changes?since={cursor}&amp;limit={n}
    ///
    /// Returns tag changes ordered oldest-first. Omit <c>since</c> for a full
    /// backfill. Keep calling with the returned <c>nextCursor</c> until
    /// <c>hasMore</c> is false.
    /// </summary>
    /// <response code="200">A page of changes plus the cursor to resume from.</response>
    /// <response code="400">Malformed cursor.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> GetChanges(
        Guid projectId,
        [FromQuery] string? since = null,
        [FromQuery] int limit = 200)
    {
        var tenantId = GetTenantId();
        var exists = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!exists) return NotFound();

        limit = Math.Clamp(limit, 1, 1000);

        DateTime? sinceTime = null;
        Guid sinceId = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(since))
        {
            if (!TryParseCursor(since, out sinceTime, out sinceId))
                return BadRequest(new { message = "Malformed cursor. Use the nextCursor value from a previous response." });
        }

        var q = _db.TaggedElements.Where(t => t.ProjectId == projectId);

        if (sinceTime.HasValue)
        {
            // Strictly after the cursor position in (time, id) order. The Id
            // comparison is what stops same-instant rows being skipped or
            // replayed — see the class comment.
            var st = sinceTime.Value;
            q = q.Where(t => t.LastModifiedUtc != null &&
                             (t.LastModifiedUtc > st ||
                              (t.LastModifiedUtc == st && t.Id.CompareTo(sinceId) > 0)));
        }
        else
        {
            // Backfill: rows with no modification stamp cannot be ordered, so
            // they would break resumption. Excluded deliberately rather than
            // silently ordered by something else.
            q = q.Where(t => t.LastModifiedUtc != null);
        }

        // Fetch one extra to answer hasMore without a second COUNT query.
        var rows = await q
            .OrderBy(t => t.LastModifiedUtc)
            .ThenBy(t => t.Id)
            .Take(limit + 1)
            .ToListAsync();

        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var items = rows.Select(t => new
        {
            kind = "tag",
            globalId = t.UniqueId,
            lastModifiedUtc = t.LastModifiedUtc,
            payload = new
            {
                disc = t.Disc, loc = t.Loc, zone = t.Zone, lvl = t.Lvl,
                sys = t.Sys, func = t.Func, prod = t.Prod, seq = t.Seq,
                tag1 = t.Tag1,
                status = t.Status,
                categoryName = t.CategoryName,
                familyName = t.FamilyName,
            }
        }).ToList();

        // Hold the cursor at the last row actually returned. When the page is
        // empty the caller keeps its existing cursor — echoing back a "now"
        // cursor would skip anything written between the query and the response.
        var last = rows.LastOrDefault();
        var nextCursor = last != null && last.LastModifiedUtc.HasValue
            ? MakeCursor(last.LastModifiedUtc.Value, last.Id)
            : since;

        return Ok(new
        {
            items,
            nextCursor,
            hasMore,
            count = items.Count
        });
    }

    internal static string MakeCursor(DateTime lastModifiedUtc, Guid id) =>
        $"{lastModifiedUtc.Ticks}_{id}";

    internal static bool TryParseCursor(string cursor, out DateTime? time, out Guid id)
    {
        time = null;
        id = Guid.Empty;
        var parts = (cursor ?? "").Split('_', 2);
        if (parts.Length != 2) return false;
        if (!long.TryParse(parts[0], out var ticks)) return false;
        if (ticks < 0 || ticks > DateTime.MaxValue.Ticks) return false;
        if (!Guid.TryParse(parts[1], out id)) return false;
        time = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var t) ? t : Guid.Empty;
}
