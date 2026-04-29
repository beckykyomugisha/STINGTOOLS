using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// BIM coordination meeting management with agenda, minutes, and action items.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/meetings")]
[Authorize]
public class MeetingsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    // GAP-FIX-SIGNALR — broadcast MeetingCreated / MeetingUpdated to project
    // subscribers. New events; matching subscriptions added on both clients
    // (PlanscapeRealtimeClient.cs + src/services/realtimeClient.ts).
    private readonly IHubContext<NotificationHub> _notifHub;

    public MeetingsController(PlanscapeDbContext db, IHubContext<NotificationHub> notifHub)
    {
        _db = db;
        _notifHub = notifHub;
    }

    [HttpGet]
    public async Task<ActionResult> GetMeetings(Guid projectId)
    {
        var tenantId = GetTenantId();

        // Tenant isolation: 404 if the project does not belong to this tenant
        var projectExists = await _db.Projects
            .AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!projectExists) return NotFound("Project not found");

        var meetings = await _db.Meetings
            .Where(m => m.ProjectId == projectId && m.Project!.TenantId == tenantId)
            .OrderByDescending(m => m.ScheduledAt)
            .Select(m => new
            {
                m.Id, m.Title, m.MeetingType, m.ScheduledAt, m.CreatedBy, m.CreatedAt,
                ActionItemCount = m.ActionItems.Count,
                OpenActions = m.ActionItems.Count(a => a.Status == "OPEN" || a.Status == "IN_PROGRESS")
            })
            .ToListAsync();

        return Ok(meetings);
    }

    [HttpPost]
    public async Task<ActionResult> CreateMeeting(Guid projectId, [FromBody] CreateMeetingRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        var meeting = new Meeting
        {
            ProjectId = projectId,
            Title = req.Title,
            MeetingType = req.MeetingType ?? "BIM Coordination",
            ScheduledAt = req.ScheduledAt,
            AgendaJson = req.AgendaJson,
            AttendeesJson = req.AttendeesJson,
            CreatedBy = User.FindFirst("display_name")?.Value ?? "Unknown"
        };

        _db.Meetings.Add(meeting);
        await _db.SaveChangesAsync();

        // GAP-FIX-SIGNALR — broadcast MeetingCreated to project subscribers.
        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingCreated", new
        {
            meeting.Id, meeting.Title, meeting.MeetingType, meeting.ScheduledAt,
            meeting.CreatedBy, projectId
        });

        return CreatedAtAction(nameof(GetMeetings), new { projectId }, meeting);
    }

    /// <summary>
    /// Phase 142 — bulk-create endpoint so the offline queue and the
    /// plugin's PlanscapeServerClient can flush a backlog in one round-trip.
    /// Caps at 200 per call.
    /// </summary>
    [HttpPost("bulk")]
    public async Task<ActionResult> BulkCreate(Guid projectId, [FromBody] List<CreateMeetingRequest> reqs)
    {
        if (reqs == null) return BadRequest("Body must be a JSON array");
        if (reqs.Count == 0) return Ok(new { created = 0, items = Array.Empty<object>() });
        if (reqs.Count > 200) return BadRequest("Maximum 200 meetings per bulk operation");

        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        var createdBy = User.FindFirst("display_name")?.Value ?? "Unknown";
        var rows = new List<Meeting>(reqs.Count);
        foreach (var req in reqs)
        {
            var m = new Meeting
            {
                ProjectId = projectId,
                Title = req.Title,
                MeetingType = req.MeetingType ?? "BIM Coordination",
                ScheduledAt = req.ScheduledAt,
                AgendaJson = req.AgendaJson,
                AttendeesJson = req.AttendeesJson,
                CreatedBy = createdBy
            };
            _db.Meetings.Add(m);
            rows.Add(m);
        }
        await _db.SaveChangesAsync();

        // GAP-FIX-SIGNALR — single bulk-event with the count, not one per row.
        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingCreated", new
        {
            projectId,
            kind = "bulk_created",
            count = rows.Count,
            firstTitle = rows.FirstOrDefault()?.Title,
        });

        return Ok(new
        {
            created = rows.Count,
            items = rows.Select(r => new { r.Id, r.Title, r.MeetingType, r.ScheduledAt })
        });
    }

    [HttpPut("{meetingId}/minutes")]
    public async Task<ActionResult> LogMinutes(Guid projectId, Guid meetingId, [FromBody] LogMinutesRequest req)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        meeting.Minutes = req.Minutes;
        await _db.SaveChangesAsync();

        // GAP-FIX-SIGNALR — broadcast MeetingUpdated when minutes are logged.
        // Mobile inbox surfaces "minutes ready" indicator off this event.
        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingUpdated", new
        {
            meeting.Id, meeting.Title, projectId, kind = "minutes_logged"
        });

        return Ok(meeting);
    }

    [HttpPost("{meetingId}/actions")]
    public async Task<ActionResult> AddActionItem(Guid projectId, Guid meetingId, [FromBody] AddActionItemRequest req)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        var item = new MeetingActionItem
        {
            MeetingId = meetingId,
            Description = req.Description,
            Assignee = req.Assignee,
            DueDate = req.DueDate
        };

        _db.MeetingActionItems.Add(item);
        await _db.SaveChangesAsync();

        // GAP-FIX-SIGNALR — action items are the most actionable child of a
        // meeting; broadcast so any My-Actions screen open in the mobile app
        // refreshes without a poll.
        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingUpdated", new
        {
            meeting.Id, meeting.Title, projectId, kind = "action_added",
            actionId = item.Id, item.Assignee, item.DueDate
        });

        return CreatedAtAction(nameof(GetOpenActions), new { projectId }, item);
    }

    [HttpPut("{meetingId}/actions/{actionId}")]
    public async Task<ActionResult> UpdateAction(Guid projectId, Guid meetingId, Guid actionId, [FromBody] UpdateActionRequest req)
    {
        // S3 — also fixes a pre-existing tenant-scoping gap: this endpoint
        // had no Project.TenantId check, so a user could mutate any action
        // item in any tenant just by knowing the GUIDs.
        var tenantId = GetTenantId();
        var action = await _db.MeetingActionItems
            .Include(a => a.Meeting)
            .FirstOrDefaultAsync(a => a.Id == actionId
                                   && a.MeetingId == meetingId
                                   && a.Meeting!.ProjectId == projectId
                                   && a.Meeting.Project!.TenantId == tenantId);
        if (action == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        if (req.Status != null) action.Status = req.Status;
        if (req.Assignee != null) action.Assignee = req.Assignee;
        if (req.LinkedIssueId != null) action.LinkedIssueId = req.LinkedIssueId;

        await _db.SaveChangesAsync();

        // GAP-FIX-SIGNALR — action item updated (status / assignee / linked issue).
        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingUpdated", new
        {
            action.MeetingId, projectId, kind = "action_updated",
            actionId = action.Id, action.Status, action.Assignee
        });

        return Ok(action);
    }

    [HttpGet("actions/open")]
    public async Task<ActionResult> GetOpenActions(Guid projectId)
    {
        var tenantId = GetTenantId();
        var actions = await _db.MeetingActionItems
            .Where(a => a.Meeting!.ProjectId == projectId && a.Meeting.Project!.TenantId == tenantId
                && (a.Status == "OPEN" || a.Status == "IN_PROGRESS"))
            .OrderBy(a => a.DueDate)
            .Select(a => new
            {
                a.Id, a.Description, a.Assignee, a.DueDate, a.Status, a.LinkedIssueId,
                // Phase 96 — mobile needs MeetingId to call PUT .../actions/{id}
                // without re-fetching the parent meeting. Previously only
                // MeetingTitle was projected which meant the mobile tick-off
                // action silently failed because the route required a meetingId.
                MeetingId = a.MeetingId,
                MeetingTitle = a.Meeting!.Title,
                IsOverdue = a.DueDate.HasValue && a.DueDate < DateTime.UtcNow
            })
            .ToListAsync();

        return Ok(actions);
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record CreateMeetingRequest(string Title, string? MeetingType, DateTime ScheduledAt, string? AgendaJson, string? AttendeesJson);
public record LogMinutesRequest(string Minutes);
public record AddActionItemRequest(string Description, string? Assignee, DateTime? DueDate);
public record UpdateActionRequest(string? Status, string? Assignee, string? LinkedIssueId);
