using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// P2 — Comment thread on an issue.
///
///   GET  /api/projects/{pid}/issues/{iid}/comments           — list (newest last)
///   POST /api/projects/{pid}/issues/{iid}/comments           — add
///   PUT  /api/projects/{pid}/issues/{iid}/comments/{cid}     — edit (author only)
///   DEL  /api/projects/{pid}/issues/{iid}/comments/{cid}     — soft delete
///
/// Tenant-isolated via the issue's project. Posts a <c>CommentAdded</c>
/// real-time event to the project group + pushes to the mentioned user.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/issues/{issueId:guid}/comments")]
[Authorize]
[ProjectAccess]
public class IssueCommentsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly INotificationService _notif;
    private readonly IAuditService _audit;
    private readonly IPushNotificationService _push;

    public IssueCommentsController(
        PlanscapeDbContext db,
        IHubContext<NotificationHub> hub,
        INotificationService notif,
        IAuditService audit,
        IPushNotificationService push)
    {
        _db = db;
        _hub = hub;
        _notif = notif;
        _audit = audit;
        _push = push;
    }

    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, Guid issueId, CancellationToken ct)
    {
        if (!await IssueInTenant(projectId, issueId, ct)) return NotFound();
        var rows = await _db.IssueComments.AsNoTracking()
            .Where(c => c.IssueId == issueId && c.DeletedAt == null)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(
                c.Id, c.Body, c.AuthorName, c.AuthorUserId, c.Source,
                c.MentionedUserId, c.CreatedAt, c.EditedAt))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<CommentDto>> Add(
        Guid projectId, Guid issueId,
        [FromBody] AddCommentRequest req,
        CancellationToken ct)
    {
        if (!await IssueInTenant(projectId, issueId, ct)) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(req.Body)) return BadRequest(new { error = "body_required" });
        if (req.Body.Length > 4000)              return BadRequest(new { error = "body_too_long", max = 4000 });

        // Pull the issue so we can fan out a push to every watcher (in
        // addition to the @mentioned user). Without this watchers see the
        // SignalR `CommentAdded` event in-app but get no push when offline.
        var issue = await _db.Issues
            .AsNoTracking()
            .Where(i => i.Id == issueId && i.ProjectId == projectId)
            .Select(i => new { i.Id, i.IssueCode, i.Title, i.Priority, i.AssigneeUserId, i.WatcherUserIds })
            .FirstOrDefaultAsync(ct);
        if (issue == null) return NotFound();

        var comment = new IssueComment
        {
            IssueId         = issueId,
            Body            = req.Body.Trim(),
            AuthorUserId    = CurrentUserId(),
            AuthorName      = User.FindFirst("display_name")?.Value ?? User.Identity?.Name ?? "Unknown",
            Source          = req.Source ?? "web",
            MentionedUserId = req.MentionedUserId,
        };
        _db.IssueComments.Add(comment);
        await _db.SaveChangesAsync(ct);
        // Audit so the issue Activity timeline (which queries AuditLog by
        // EntityId == issueId across multiple EntityTypes) sees comments
        // alongside status / assignee changes. EntityId MUST be the issue
        // id (not the comment id) so the timeline join finds it.
        await _audit.LogAsync("CREATE", "IssueComment",
            issueId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new {
                commentId = comment.Id,
                preview = Truncate(comment.Body, 120),
                mentionedUserId = comment.MentionedUserId
            }));

        // Real-time push to project subscribers.
        var dto = new CommentDto(comment.Id, comment.Body, comment.AuthorName,
                                 comment.AuthorUserId, comment.Source,
                                 comment.MentionedUserId, comment.CreatedAt, null);
        _ = _hub.Clients.Group($"project-{projectId}").SendAsync("CommentAdded", new
        {
            projectId, issueId,
            comment = dto,
        }, ct);

        // ── Fan-out push targets ──────────────────────────────────────────
        // 1) Mentioned user (highest signal, distinct copy).
        // 2) Assignee, if not the author and not the mentioned user.
        // 3) Watchers, minus author / mentioned / assignee (deduped via set).
        // Each user gets exactly one push so a teammate watching their
        // own issue doesn't receive three duplicate notifications.
        var notified = new HashSet<Guid>();
        if (comment.AuthorUserId.HasValue) notified.Add(comment.AuthorUserId.Value);

        if (comment.MentionedUserId.HasValue && !notified.Contains(comment.MentionedUserId.Value))
        {
            notified.Add(comment.MentionedUserId.Value);
            var data = new Dictionary<string, object?>
            {
                ["channel"] = "issue",
                ["issueId"] = issueId.ToString(),
                ["commentId"] = comment.Id.ToString(),
            };
            _ = _notif.NotifyUserAsync(
                comment.MentionedUserId.Value,
                $"Mentioned on issue",
                $"{comment.AuthorName}: {Truncate(comment.Body, 80)}",
                data, ct);
        }

        // Build the (assignee + watcher) audience. Each gets a "new comment
        // on issue you're watching" push.
        var audience = new HashSet<Guid>();
        if (issue.AssigneeUserId.HasValue) audience.Add(issue.AssigneeUserId.Value);
        foreach (var w in BimIssue.ParseWatcherIds(issue.WatcherUserIds)) audience.Add(w);
        audience.ExceptWith(notified);          // skip author + mentioned
        foreach (var uid in audience)
        {
            _ = _push.SendToUserAsync(uid, new PushPayload
            {
                Title = $"💬 {issue.IssueCode}",
                Body = $"{comment.AuthorName}: {Truncate(comment.Body, 80)}",
                Channel = "issues",
                Data = new Dictionary<string, string>
                {
                    ["type"] = "issue_comment",
                    ["issueId"] = issueId.ToString(),
                    ["issueCode"] = issue.IssueCode,
                    ["commentId"] = comment.Id.ToString(),
                    ["projectId"] = projectId.ToString()
                }
            }, ct);
        }

        return CreatedAtAction(nameof(List), new { projectId, issueId }, dto);
    }

    [HttpPut("{commentId:guid}")]
    public async Task<ActionResult<CommentDto>> Edit(
        Guid projectId, Guid issueId, Guid commentId,
        [FromBody] EditCommentRequest req,
        CancellationToken ct)
    {
        if (!await IssueInTenant(projectId, issueId, ct)) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(req.Body))              return BadRequest(new { error = "body_required" });

        var c = await _db.IssueComments
            .FirstOrDefaultAsync(x => x.Id == commentId && x.IssueId == issueId && x.DeletedAt == null, ct);
        if (c == null) return NotFound();
        if (c.AuthorUserId != CurrentUserId()) return Forbid();

        c.Body = req.Body.Trim();
        c.EditedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UPDATE", "IssueComment",
            issueId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { commentId = c.Id, edited = true, editedAt = c.EditedAt }));

        return Ok(new CommentDto(c.Id, c.Body, c.AuthorName, c.AuthorUserId, c.Source,
                                  c.MentionedUserId, c.CreatedAt, c.EditedAt));
    }

    [HttpDelete("{commentId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid issueId, Guid commentId, CancellationToken ct)
    {
        if (!await IssueInTenant(projectId, issueId, ct)) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var c = await _db.IssueComments
            .FirstOrDefaultAsync(x => x.Id == commentId && x.IssueId == issueId && x.DeletedAt == null, ct);
        if (c == null) return NotFound();

        // Author can delete their own; Admin/Owner can delete anything.
        var userId = CurrentUserId();
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("Owner");
        if (c.AuthorUserId != userId && !isAdmin) return Forbid();

        c.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("DELETE", "IssueComment",
            issueId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { commentId = c.Id, deletedAt = c.DeletedAt, byAuthor = c.AuthorUserId == userId }));
        return NoContent();
    }

    // ── helpers ──────────────────────────────────────────────────────

    private async Task<bool> IssueInTenant(Guid projectId, Guid issueId, CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        if (tenantId == Guid.Empty) return false;
        return await _db.Issues
            .AsNoTracking()
            .AnyAsync(i => i.Id == issueId && i.ProjectId == projectId && i.Project!.TenantId == tenantId, ct);
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max - 1) + "…";
}

public record AddCommentRequest(string Body, string? Source, Guid? MentionedUserId);
public record EditCommentRequest(string Body);
public record CommentDto(
    Guid Id,
    string Body,
    string AuthorName,
    Guid? AuthorUserId,
    string? Source,
    Guid? MentionedUserId,
    DateTime CreatedAt,
    DateTime? EditedAt);
