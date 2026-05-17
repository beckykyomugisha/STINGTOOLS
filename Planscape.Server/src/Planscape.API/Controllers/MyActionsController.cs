using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// Aggregator endpoints for "what does the current user need to do today" —
/// the BIM/Construction Manager's morning inbox view. One round-trip returns
/// every open commitment that is assigned to the caller across:
///   • <see cref="Planscape.Core.Entities.BimIssue"/> (assignee = me, status != CLOSED/RESOLVED)
///   • <see cref="Planscape.Core.Entities.MeetingActionItem"/> (assignee = my display name, status != COMPLETE)
///   • <see cref="Planscape.Core.Entities.DocumentApproval"/> (status = PENDING — anyone with permission can approve)
///   • SLA-breached <see cref="Planscape.Core.Entities.BimIssue"/> on the project (manager triage view)
///
/// Pre-Phase-142 the mobile app needed three separate round-trips and a manual
/// cross-filter to assemble this list, so the inbox tab took 3+ s to render
/// and frequently surfaced stale data. The aggregator is read-only and
/// rate-limit-bucketed under the same "mobile" policy as the rest of the
/// project routes.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/[controller]")]
[EnableRateLimiting("mobile")]
[Authorize]
[ProjectAccess]
public class MyActionsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<MyActionsController> _logger;

    public MyActionsController(PlanscapeDbContext db, ILogger<MyActionsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// One-shot inbox payload. Returns counts + the first <paramref name="limit"/>
    /// rows from each bucket (default 25 each — enough for a scroll-once view).
    /// All timestamps are UTC.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> GetMyActions(Guid projectId, [FromQuery] int limit = 25)
    {
        var tenantId = GetTenantId();
        var userId = GetUserId();
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            return Unauthorized();

        // Project membership gate — the inbox should never leak rows from a
        // project the caller can't see, even if a row was assigned to them
        // before they were removed from the team.
        var isMember = await _db.ProjectMembers.AnyAsync(m =>
            m.ProjectId == projectId && m.UserId == userId);
        if (!isMember) return Forbid();

        // Resolve the user once so we can match on UserId, Email, or DisplayName
        // without needing 3 separate round-trips. MeetingActionItem.Assignee is
        // a free-text DisplayName today; a follow-up will give it an FK.
        var me = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId);
        if (me == null) return Unauthorized();

        if (limit < 1) limit = 1;
        if (limit > 200) limit = 200;

        var openStates = new[] { "OPEN", "IN_PROGRESS" };

        // Issues assigned to me — match by FK first, fall back to email then display
        // name so legacy issues from before the AssigneeUserId migration still
        // surface for the right person.
        var myIssuesQuery = _db.Issues
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId && openStates.Contains(i.Status))
            .Where(i =>
                i.AssigneeUserId == me.Id
                || (i.AssigneeEmail != null && i.AssigneeEmail == me.Email)
                || (i.Assignee != null && i.Assignee == me.DisplayName));

        var issueCount = await myIssuesQuery.CountAsync();
        var issues = await myIssuesQuery
            .OrderBy(i => i.Priority == "CRITICAL" ? 0
                        : i.Priority == "HIGH" ? 1
                        : i.Priority == "MEDIUM" ? 2 : 3)
            .ThenBy(i => i.DueDate ?? DateTime.MaxValue)
            .Take(limit)
            .Select(i => new
            {
                i.Id, i.IssueCode, i.Type, i.Title, i.Priority, i.Status,
                i.DueDate, i.CreatedAt, i.Discipline, i.Latitude, i.Longitude,
                attachmentCount = i.Attachments.Count
            })
            .ToListAsync();

        // Meeting action items assigned to me by display name — the entity has
        // no FK yet so we match conservatively. If multiple users share a
        // display name in a tenant they will both see the row; flagged in the
        // gap docs as a follow-up.
        var myActionsQuery = _db.MeetingActionItems
            .AsNoTracking()
            .Where(a => a.Status != "COMPLETE")
            .Where(a => a.Meeting != null && a.Meeting.ProjectId == projectId)
            .Where(a => a.Assignee != null && (a.Assignee == me.DisplayName || a.Assignee == me.Email));

        var actionCount = await myActionsQuery.CountAsync();
        var actions = await myActionsQuery
            .OrderBy(a => a.DueDate ?? DateTime.MaxValue)
            .Take(limit)
            .Select(a => new
            {
                a.Id, a.MeetingId, a.Description, a.Assignee, a.DueDate, a.Status,
                a.LinkedIssueId,
                meetingTitle = a.Meeting != null ? a.Meeting.Title : "",
                meetingType = a.Meeting != null ? a.Meeting.MeetingType : ""
            })
            .ToListAsync();

        // Pending document approvals on the project — anyone with the permission
        // claim can pick one up, so we surface ALL pending approvals to all
        // members rather than gating on RequestedBy.
        //
        // Phase 177 — narrow by the caller's per-folder ACL slice so an
        // approval for a doc they can't see in the documents list never
        // appears in the inbox either. Discipline + suitability + target
        // CDE state of the transition are all checked.
        var acl = await ProjectMemberAcl.ResolveAsync(_db, projectId, User);

        var approvalsQuery = _db.DocumentApprovals
            .AsNoTracking()
            .Include(a => a.Document)
            .Where(a => a.ProjectId == projectId && a.Status == "PENDING");

        // Apply ACL discipline + suitability + transition-target filters in SQL
        // to avoid pulling rows the user can't act on.
        if (!acl.BypassesAcl)
        {
            if (acl.Disciplines is { Length: > 0 } disc)
                approvalsQuery = approvalsQuery.Where(a => a.Document != null
                    && a.Document.Discipline != null
                    && disc.Contains(a.Document.Discipline));
            if (acl.Suitabilities is { Length: > 0 } suit)
                approvalsQuery = approvalsQuery.Where(a => a.Document != null
                    && suit.Contains(a.Document.SuitabilityCode));
            if (acl.Cde is { Length: > 0 } cde)
                approvalsQuery = approvalsQuery.Where(a => a.Document != null
                    && cde.Contains(a.Document.CdeStatus));
        }

        var approvalCount = await approvalsQuery.CountAsync();
        var approvals = await approvalsQuery
            .OrderBy(a => a.RequestedAt)
            .Take(limit)
            .Select(a => new
            {
                a.Id, a.DocumentId, a.Transition, a.RequestedBy, a.RequestedAt,
                a.Comments,
                fileName = a.Document != null ? a.Document.FileName : "",
                discipline = a.Document != null ? a.Document.Discipline : null
            })
            .ToListAsync();

        // SLA-breached issues across the project (not just mine) so a manager
        // sees risk forming on the team. Cap to limit / 2 to keep the payload
        // manageable on a slow site connection.
        var slaCutoff = DateTime.UtcNow;
        var slaQuery = _db.Issues
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId
                     && openStates.Contains(i.Status)
                     && i.DueDate != null
                     && i.DueDate < slaCutoff);

        var slaCount = await slaQuery.CountAsync();
        var slaSlice = Math.Max(5, limit / 2);
        var sla = await slaQuery
            .OrderBy(i => i.DueDate)
            .Take(slaSlice)
            .Select(i => new
            {
                i.Id, i.IssueCode, i.Type, i.Title, i.Priority, i.Status,
                i.DueDate, i.Assignee, i.AssigneeUserId,
                breachHours = (int)(slaCutoff - i.DueDate!.Value).TotalHours
            })
            .ToListAsync();

        return Ok(new
        {
            generatedAt = DateTime.UtcNow,
            counts = new
            {
                issues = issueCount,
                actions = actionCount,
                approvals = approvalCount,
                slaBreached = slaCount,
                total = issueCount + actionCount + approvalCount
            },
            issues,
            actions,
            approvals,
            slaBreached = sla
        });
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    private Guid GetUserId() =>
        Guid.TryParse(
            User.FindFirst("user_id")?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id) ? id : Guid.Empty;
}
