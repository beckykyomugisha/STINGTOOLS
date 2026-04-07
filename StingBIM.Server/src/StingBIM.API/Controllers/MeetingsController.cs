using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StingBIM.Core.Entities;
using StingBIM.Infrastructure.Data;

namespace StingBIM.API.Controllers;

/// <summary>
/// BIM coordination meeting management with agenda, minutes, and action items.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/meetings")]
[Authorize]
public class MeetingsController : ControllerBase
{
    private readonly StingBimDbContext _db;

    public MeetingsController(StingBimDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult> GetMeetings(Guid projectId)
    {
        var tenantId = GetTenantId();
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

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId,
            Action = "meeting_created",
            EntityType = "Meeting",
            EntityId = meeting.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { meeting.Title, meeting.MeetingType, meeting.ScheduledAt }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetMeetings), new { projectId }, meeting);
    }

    [HttpPut("{meetingId}/minutes")]
    public async Task<ActionResult> LogMinutes(Guid projectId, Guid meetingId, [FromBody] LogMinutesRequest req)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();

        meeting.Minutes = req.Minutes;

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId,
            Action = "meeting_minutes_logged",
            EntityType = "Meeting",
            EntityId = meetingId.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { meeting.Title, minutesLength = req.Minutes?.Length ?? 0 }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok(meeting);
    }

    [HttpPost("{meetingId}/actions")]
    public async Task<ActionResult> AddActionItem(Guid projectId, Guid meetingId, [FromBody] AddActionItemRequest req)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();

        var item = new MeetingActionItem
        {
            MeetingId = meetingId,
            Description = req.Description,
            Assignee = req.Assignee,
            DueDate = req.DueDate
        };

        _db.MeetingActionItems.Add(item);

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId,
            Action = "action_item_created",
            EntityType = "MeetingActionItem",
            EntityId = item.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { item.Description, item.Assignee, item.DueDate, meetingId }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpPut("{meetingId}/actions/{actionId}")]
    public async Task<ActionResult> UpdateAction(Guid projectId, Guid meetingId, Guid actionId, [FromBody] UpdateActionRequest req)
    {
        var action = await _db.MeetingActionItems
            .FirstOrDefaultAsync(a => a.Id == actionId && a.MeetingId == meetingId);
        if (action == null) return NotFound();

        var oldStatus = action.Status;
        if (req.Status != null) action.Status = req.Status;
        if (req.Assignee != null) action.Assignee = req.Assignee;
        if (req.LinkedIssueId != null) action.LinkedIssueId = req.LinkedIssueId;

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = GetTenantId(),
            ProjectId = projectId,
            UserId = userId,
            Action = "action_item_updated",
            EntityType = "MeetingActionItem",
            EntityId = actionId.ToString(),
            DetailsJson = JsonSerializer.Serialize(new
            {
                statusChange = req.Status != null ? $"{oldStatus}→{req.Status}" : null,
                assignee = req.Assignee,
                linkedIssueId = req.LinkedIssueId
            }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
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
