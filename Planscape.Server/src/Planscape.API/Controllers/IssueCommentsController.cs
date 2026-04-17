using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

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
public class IssueCommentsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly INotificationService _notif;

    public IssueCommentsController(
        PlanscapeDbContext db,
        IHubContext<NotificationHub> hub,
        INotificationService notif)
    {
        _db = db;
        _hub = hub;
        _notif = notif;
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
        if (string.IsNullOrWhiteSpace(req.Body)) return BadRequest(new { error = "body_required" });
        if (req.Body.Length > 4000)              return BadRequest(new { error = "body_too_long", max = 4000 });

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

        // Real-time push to project subscribers.
        var dto = new CommentDto(comment.Id, comment.Body, comment.AuthorName,
                                 comment.AuthorUserId, comment.Source,
                                 comment.MentionedUserId, comment.CreatedAt, null);
        _ = _hub.Clients.Group($"project-{projectId}").SendAsync("CommentAdded", new
        {
            projectId, issueId,
            comment = dto,
        }, ct);

        // Push to the mentioned user (if any).
        if (comment.MentionedUserId.HasValue && comment.MentionedUserId != comment.AuthorUserId)
        {
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

        return CreatedAtAction(nameof(List), new { projectId, issueId }, dto);
    }

    [HttpPut("{commentId:guid}")]
    public async Task<ActionResult<CommentDto>> Edit(
        Guid projectId, Guid issueId, Guid commentId,
        [FromBody] EditCommentRequest req,
        CancellationToken ct)
    {
        if (!await IssueInTenant(projectId, issueId, ct)) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Body))              return BadRequest(new { error = "body_required" });

        var c = await _db.IssueComments
            .FirstOrDefaultAsync(x => x.Id == commentId && x.IssueId == issueId && x.DeletedAt == null, ct);
        if (c == null) return NotFound();
        if (c.AuthorUserId != CurrentUserId()) return Forbid();

        c.Body = req.Body.Trim();
        c.EditedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new CommentDto(c.Id, c.Body, c.AuthorName, c.AuthorUserId, c.Source,
                                  c.MentionedUserId, c.CreatedAt, c.EditedAt));
    }

    [HttpDelete("{commentId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid issueId, Guid commentId, CancellationToken ct)
    {
        if (!await IssueInTenant(projectId, issueId, ct)) return NotFound();
        var c = await _db.IssueComments
            .FirstOrDefaultAsync(x => x.Id == commentId && x.IssueId == issueId && x.DeletedAt == null, ct);
        if (c == null) return NotFound();

        // Author can delete their own; Admin/Owner can delete anything.
        var userId = CurrentUserId();
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("Owner");
        if (c.AuthorUserId != userId && !isAdmin) return Forbid();

        c.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
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
