using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// BIM coordination meeting management.
/// Provides full CRUD for meetings, structured attendees (with BCC list),
/// agenda items, action items, push notifications to invitees, and
/// Word/iCal export.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/meetings")]
[Authorize]
[ProjectAccess]
public class MeetingsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<NotificationHub> _notifHub;
    private readonly IPushNotificationService _push;
    private readonly IAuditService _audit;
    private readonly ILogger<MeetingsController> _logger;
    private readonly IConfiguration _config;   // N2 — egress presign config
    private readonly Planscape.Core.Interfaces.INotificationService _notifications;   // N — live/scheduled notify

    public MeetingsController(
        PlanscapeDbContext db,
        IHubContext<NotificationHub> notifHub,
        IPushNotificationService push,
        IAuditService audit,
        ILogger<MeetingsController> logger,
        IConfiguration config,
        Planscape.Core.Interfaces.INotificationService notifications)
    {
        _db = db;
        _notifHub = notifHub;
        _push = push;
        _audit = audit;
        _logger = logger;
        _config = config;
        _notifications = notifications;
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult> GetMeetings(Guid projectId,
        [FromQuery] string? status = null,
        // #10 — the Revit plugin (PlanscapeServerClient.GetMeetingsAsync) sends
        // ?upcoming=true; the param was unbound so it was silently ignored and
        // all meetings (most-recent-past first) came back. Bind + honour it.
        [FromQuery] bool upcoming = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 200);
        var tenantId = GetTenantId();

        var projectExists = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!projectExists) return NotFound("Project not found");

        var q = _db.Meetings.Where(m => m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (!string.IsNullOrEmpty(status)) q = q.Where(m => m.Status == status);
        if (upcoming) q = q.Where(m => m.ScheduledAt >= DateTime.UtcNow);

        var total = await q.CountAsync();
        // Upcoming → soonest-first (ascending) matches the "next meetings" intent;
        // the default listing stays most-recent-first.
        var ordered = upcoming ? q.OrderBy(m => m.ScheduledAt) : q.OrderByDescending(m => m.ScheduledAt);
        var meetings = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new
            {
                m.Id, m.Title, m.MeetingType, m.ScheduledAt, m.DurationMinutes,
                m.Location, m.MeetingUrl, m.Status, m.CreatedBy, m.CreatedAt,
                m.RecurrenceRule, m.SeriesId,
                AttendeeCount = m.Attendees.Count,
                ActionItemCount = m.ActionItems.Count,
                OpenActions = m.ActionItems.Count(a => a.Status == "OPEN" || a.Status == "IN_PROGRESS"),
                HasMinutes = m.Minutes != null || m.MinutesDocumentId != null,
                // N5 — the live (LiveKit/SignalR) session currently backing this scheduled
                // meeting, if any. Non-null ⇒ the /app Meetings page shows an in-progress
                // badge + "Join live". Correlated by MeetingSession.MeetingId == meeting.Id.
                LiveSessionId = _db.MeetingSessions
                    .Where(s => s.MeetingId == m.Id && s.Status == "ACTIVE")
                    .Select(s => (Guid?)s.Id).FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(new { items = meetings, total, page, pageSize });
    }

    // ── Detail ───────────────────────────────────────────────────────────────

    [HttpGet("{meetingId}")]
    public async Task<ActionResult> GetMeeting(Guid projectId, Guid meetingId)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .Include(m => m.Attendees)
            .Include(m => m.AgendaItems)
            .Include(m => m.ActionItems)
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();

        // N5 — the active live session backing this meeting (in-progress badge + Join).
        var liveSessionId = await _db.MeetingSessions
            .Where(s => s.MeetingId == meetingId && s.ProjectId == projectId && s.Status == "ACTIVE")
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync();

        return Ok(new
        {
            meeting.Id, meeting.Title, meeting.MeetingType, meeting.ScheduledAt,
            meeting.DurationMinutes, meeting.Location, meeting.MeetingUrl, meeting.Status,
            meeting.Minutes, meeting.MinutesDocumentId, meeting.NotifiedUserIds,
            meeting.RecurrenceRule, meeting.SeriesId,
            meeting.CreatedBy, meeting.CreatedByUserId, meeting.CreatedAt,
            LiveSessionId = liveSessionId,
            Attendees = meeting.Attendees.OrderBy(a => a.Role).ThenBy(a => a.Name).Select(a => new
            {
                a.Id, a.UserId, a.Name, a.Email, a.Company, a.Discipline,
                a.Role, a.AttendanceStatus, a.CreatedAt
            }),
            AgendaItems = meeting.AgendaItems.OrderBy(i => i.OrderIndex).Select(i => new
            {
                i.Id, i.OrderIndex, i.Title, i.Description, i.DurationMinutes,
                i.Presenter, i.Outcome, i.Decision, i.Status, i.CreatedAt
            }),
            ActionItems = meeting.ActionItems.OrderBy(a => a.CreatedAt).Select(a => new
            {
                a.Id, a.Description, a.Notes, a.Assignee, a.AssigneeUserId,
                a.DueDate, a.Priority, a.Status, a.LinkedIssueId, a.CreatedAt,
                IsOverdue = a.DueDate.HasValue && a.DueDate < DateTime.UtcNow
                            && a.Status != "COMPLETE" && a.Status != "CLOSED"
            }),
        });
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult> CreateMeeting(Guid projectId, [FromBody] CreateMeetingRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        var creatorClaim = User.FindFirst("user_id")?.Value;
        Guid? creatorId = Guid.TryParse(creatorClaim, out var cid) ? cid : null;

        var meeting = new Meeting
        {
            ProjectId = projectId,
            Title = req.Title,
            MeetingType = req.MeetingType ?? "BIM Coordination",
            ScheduledAt = req.ScheduledAt,
            DurationMinutes = req.DurationMinutes,
            Location = req.Location,
            MeetingUrl = req.MeetingUrl,
            Status = "SCHEDULED",
            RecurrenceRule = req.RecurrenceRule,
            SeriesId = req.SeriesId,
            CreatedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            CreatedByUserId = creatorId,
        };

        // Seed attendees from the request (optional inline creation)
        if (req.Attendees != null)
        {
            foreach (var a in req.Attendees)
            {
                meeting.Attendees.Add(new MeetingAttendee
                {
                    TenantId = tenantId,
                    UserId = a.UserId,
                    Name = a.Name ?? "",
                    Email = a.Email,
                    Company = a.Company,
                    Discipline = a.Discipline,
                    Role = a.Role ?? "ATTENDEE",
                    AttendanceStatus = "INVITED",
                });
            }
        }

        // Seed agenda items
        if (req.AgendaItems != null)
        {
            int order = 0;
            foreach (var ai in req.AgendaItems)
            {
                meeting.AgendaItems.Add(new MeetingAgendaItem
                {
                    TenantId = tenantId,
                    OrderIndex = order++,
                    Title = ai.Title,
                    Description = ai.Description,
                    DurationMinutes = ai.DurationMinutes,
                    Presenter = ai.Presenter,
                });
            }
        }

        // Validate + store BCC (notified) user list
        Guid[] validNotified = Array.Empty<Guid>();
        if (req.NotifiedUserIds != null && req.NotifiedUserIds.Length > 0)
        {
            var requested = new HashSet<Guid>(req.NotifiedUserIds.Where(g => g != Guid.Empty));
            if (requested.Count > 0)
            {
                validNotified = await _db.ProjectMembers
                    .Where(m => m.ProjectId == projectId && m.IsActive && requested.Contains(m.UserId))
                    .Select(m => m.UserId)
                    .ToArrayAsync();
            }
        }
        meeting.NotifiedUserIds = Meeting.SerializeNotifiedIds(validNotified);

        _db.Meetings.Add(meeting);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CREATE", "Meeting", meeting.Id.ToString());

        // Push MeetingCreated to all subscribed project clients
        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingCreated", new
        {
            meeting.Id, meeting.Title, meeting.MeetingType, meeting.ScheduledAt,
            meeting.Location, meeting.CreatedBy, projectId
        });

        // Send push notifications to each invited user (registered members only)
        await PushMeetingInvitesAsync(meeting, projectId, creatorId, "New Meeting");
        // N — surface the scheduled meeting in-app on the dashboard (lightweight SignalR
        // refresh event to the project group; no push — invitees already got the push above).
        _ = _notifications.NotifyProjectEventAsync(projectId, "MeetingScheduled",
            new { meeting.Id, meeting.Title, meeting.ScheduledAt, meeting.MeetingType, projectId });

        return CreatedAtAction(nameof(GetMeeting), new { projectId, meetingId = meeting.Id }, meeting);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [HttpPut("{meetingId}")]
    public async Task<ActionResult> UpdateMeeting(Guid projectId, Guid meetingId, [FromBody] UpdateMeetingRequest req)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        if (req.Title != null) meeting.Title = req.Title;
        if (req.MeetingType != null) meeting.MeetingType = req.MeetingType;
        if (req.ScheduledAt.HasValue) meeting.ScheduledAt = req.ScheduledAt.Value;
        if (req.DurationMinutes.HasValue) meeting.DurationMinutes = req.DurationMinutes;
        if (req.Location != null) meeting.Location = req.Location;
        if (req.MeetingUrl != null) meeting.MeetingUrl = req.MeetingUrl;
        if (req.Status != null) meeting.Status = req.Status;
        if (req.RecurrenceRule != null) meeting.RecurrenceRule = req.RecurrenceRule;

        if (req.NotifiedUserIds != null)
        {
            Guid[] validNotified = Array.Empty<Guid>();
            if (req.NotifiedUserIds.Length > 0)
            {
                var requested = new HashSet<Guid>(req.NotifiedUserIds.Where(g => g != Guid.Empty));
                if (requested.Count > 0)
                {
                    validNotified = await _db.ProjectMembers
                        .Where(m => m.ProjectId == projectId && m.IsActive && requested.Contains(m.UserId))
                        .Select(m => m.UserId)
                        .ToArrayAsync();
                }
            }
            meeting.NotifiedUserIds = Meeting.SerializeNotifiedIds(validNotified);
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("UPDATE", "Meeting", meeting.Id.ToString());

        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingUpdated", new
        {
            meeting.Id, meeting.Title, meeting.Status, projectId, kind = "meeting_updated"
        });

        return Ok(meeting);
    }

    // ── Bulk Create ──────────────────────────────────────────────────────────

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
                DurationMinutes = req.DurationMinutes,
                Location = req.Location,
                MeetingUrl = req.MeetingUrl,
                Status = "SCHEDULED",
                RecurrenceRule = req.RecurrenceRule,
                CreatedBy = createdBy,
            };
            _db.Meetings.Add(m);
            rows.Add(m);
        }
        await _db.SaveChangesAsync();

        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingCreated", new
        {
            projectId, kind = "bulk_created", count = rows.Count,
            firstTitle = rows.FirstOrDefault()?.Title,
        });

        return Ok(new { created = rows.Count, items = rows.Select(r => new { r.Id, r.Title, r.MeetingType, r.ScheduledAt }) });
    }

    // ── Minutes ──────────────────────────────────────────────────────────────

    [HttpPut("{meetingId}/minutes")]
    public async Task<ActionResult> LogMinutes(Guid projectId, Guid meetingId, [FromBody] LogMinutesRequest req)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        meeting.Minutes = req.Minutes;
        if (req.Status != null) meeting.Status = req.Status;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("UPDATE", "Meeting", meeting.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { kind = "minutes_logged" }));

        // Notify all attendees + BCC list that minutes are ready
        var actorClaim = User.FindFirst("user_id")?.Value;
        Guid? actorId = Guid.TryParse(actorClaim, out var aid) ? aid : (Guid?)null;
        await PushMinutesReadyAsync(meeting, projectId, actorId);

        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingUpdated", new
        {
            meeting.Id, meeting.Title, projectId, kind = "minutes_logged"
        });

        return Ok(new { meeting.Id, meeting.Minutes, meeting.Status });
    }

    // ── Attendees ────────────────────────────────────────────────────────────

    [HttpGet("{meetingId}/attendees")]
    public async Task<ActionResult> GetAttendees(Guid projectId, Guid meetingId)
    {
        var tenantId = GetTenantId();
        var meetingExists = await _db.Meetings.AnyAsync(m =>
            m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (!meetingExists) return NotFound();

        var attendees = await _db.MeetingAttendees
            .Where(a => a.MeetingId == meetingId && a.TenantId == tenantId)
            .OrderBy(a => a.Role).ThenBy(a => a.Name)
            .Select(a => new
            {
                a.Id, a.UserId, a.Name, a.Email, a.Company, a.Discipline,
                a.Role, a.AttendanceStatus, a.CreatedAt
            })
            .ToListAsync();

        return Ok(attendees);
    }

    [HttpPost("{meetingId}/attendees")]
    public async Task<ActionResult> AddAttendee(Guid projectId, Guid meetingId, [FromBody] AddAttendeeRequest req)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        // Resolve user by id or email if supplied
        AppUser? user = null;
        if (req.UserId.HasValue)
        {
            user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId.Value && u.TenantId == tenantId);
        }
        else if (!string.IsNullOrWhiteSpace(req.Email))
        {
            user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email && u.TenantId == tenantId);
        }

        var attendee = new MeetingAttendee
        {
            TenantId = tenantId,
            MeetingId = meetingId,
            UserId = user?.Id ?? req.UserId,
            Name = req.Name ?? user?.DisplayName ?? req.Email ?? "Unknown",
            Email = req.Email ?? user?.Email,
            Company = req.Company,
            Discipline = req.Discipline,
            Role = req.Role ?? "ATTENDEE",
            AttendanceStatus = "INVITED",
        };

        _db.MeetingAttendees.Add(attendee);
        await _db.SaveChangesAsync();

        // Push invite notification if this is a registered user
        if (user != null)
        {
            _ = _push.SendToUserAsync(user.Id, new PushPayload
            {
                Title = $"Meeting Invitation: {meeting.Title}",
                Body = $"{meeting.ScheduledAt:ddd d MMM HH:mm}{(meeting.Location != null ? " · " + meeting.Location : "")}",
                Channel = "meetings",
                Data = new Dictionary<string, string>
                {
                    ["type"] = "meeting_invite",
                    ["meetingId"] = meeting.Id.ToString(),
                    ["projectId"] = projectId.ToString()
                }
            });
        }

        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingUpdated", new
        {
            meetingId, projectId, kind = "attendee_added", attendeeId = attendee.Id
        });

        return Ok(new { attendee.Id, attendee.UserId, attendee.Name, attendee.Email, attendee.Role, attendee.AttendanceStatus });
    }

    [HttpPut("{meetingId}/attendees/{attendeeId}")]
    public async Task<ActionResult> UpdateAttendee(Guid projectId, Guid meetingId, Guid attendeeId,
        [FromBody] UpdateAttendeeRequest req)
    {
        var tenantId = GetTenantId();
        var attendee = await _db.MeetingAttendees
            .Include(a => a.Meeting)
            .FirstOrDefaultAsync(a => a.Id == attendeeId && a.MeetingId == meetingId
                && a.Meeting!.ProjectId == projectId && a.Meeting.Project!.TenantId == tenantId);
        if (attendee == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        if (req.AttendanceStatus != null) attendee.AttendanceStatus = req.AttendanceStatus;
        if (req.Role != null) attendee.Role = req.Role;
        if (req.Company != null) attendee.Company = req.Company;
        if (req.Discipline != null) attendee.Discipline = req.Discipline;

        await _db.SaveChangesAsync();
        return Ok(new { attendee.Id, attendee.Role, attendee.AttendanceStatus });
    }

    [HttpDelete("{meetingId}/attendees/{attendeeId}")]
    public async Task<ActionResult> RemoveAttendee(Guid projectId, Guid meetingId, Guid attendeeId)
    {
        var tenantId = GetTenantId();
        var attendee = await _db.MeetingAttendees
            .Include(a => a.Meeting)
            .FirstOrDefaultAsync(a => a.Id == attendeeId && a.MeetingId == meetingId
                && a.Meeting!.ProjectId == projectId && a.Meeting.Project!.TenantId == tenantId);
        if (attendee == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        _db.MeetingAttendees.Remove(attendee);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Agenda Items ─────────────────────────────────────────────────────────

    [HttpPost("{meetingId}/agenda")]
    public async Task<ActionResult> AddAgendaItem(Guid projectId, Guid meetingId, [FromBody] AddAgendaItemRequest req)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        var maxOrder = await _db.MeetingAgendaItems
            .Where(i => i.MeetingId == meetingId)
            .MaxAsync(i => (int?)i.OrderIndex) ?? -1;

        var item = new MeetingAgendaItem
        {
            TenantId = tenantId,
            MeetingId = meetingId,
            OrderIndex = req.OrderIndex ?? (maxOrder + 1),
            Title = req.Title,
            Description = req.Description,
            DurationMinutes = req.DurationMinutes,
            Presenter = req.Presenter,
        };

        _db.MeetingAgendaItems.Add(item);
        await _db.SaveChangesAsync();

        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingUpdated", new
        {
            meetingId, projectId, kind = "agenda_added", agendaItemId = item.Id
        });

        return Ok(new { item.Id, item.OrderIndex, item.Title, item.DurationMinutes, item.Status });
    }

    [HttpPut("{meetingId}/agenda/{itemId}")]
    public async Task<ActionResult> UpdateAgendaItem(Guid projectId, Guid meetingId, Guid itemId,
        [FromBody] UpdateAgendaItemRequest req)
    {
        var tenantId = GetTenantId();
        var item = await _db.MeetingAgendaItems
            .Include(i => i.Meeting)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.MeetingId == meetingId
                && i.Meeting!.ProjectId == projectId && i.Meeting.Project!.TenantId == tenantId);
        if (item == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        if (req.Title != null) item.Title = req.Title;
        if (req.Description != null) item.Description = req.Description;
        if (req.DurationMinutes.HasValue) item.DurationMinutes = req.DurationMinutes;
        if (req.Presenter != null) item.Presenter = req.Presenter;
        if (req.Outcome != null) item.Outcome = req.Outcome;
        if (req.Decision != null) item.Decision = req.Decision;
        if (req.Status != null) item.Status = req.Status;
        if (req.OrderIndex.HasValue) item.OrderIndex = req.OrderIndex.Value;

        await _db.SaveChangesAsync();

        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingUpdated", new
        {
            meetingId, projectId, kind = "agenda_updated", agendaItemId = item.Id, item.Status
        });

        return Ok(new { item.Id, item.OrderIndex, item.Title, item.Outcome, item.Decision, item.Status });
    }

    [HttpDelete("{meetingId}/agenda/{itemId}")]
    public async Task<ActionResult> DeleteAgendaItem(Guid projectId, Guid meetingId, Guid itemId)
    {
        var tenantId = GetTenantId();
        var item = await _db.MeetingAgendaItems
            .Include(i => i.Meeting)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.MeetingId == meetingId
                && i.Meeting!.ProjectId == projectId && i.Meeting.Project!.TenantId == tenantId);
        if (item == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        _db.MeetingAgendaItems.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Action Items ─────────────────────────────────────────────────────────

    [HttpPost("{meetingId}/actions")]
    public async Task<ActionResult> AddActionItem(Guid projectId, Guid meetingId, [FromBody] AddActionItemRequest req)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        // Offline-replay dedupe (Prompt 18) — a replayed add for the same
        // X-Idempotency-Key returns the original action item, not a duplicate.
        var idemKey = Planscape.API.Services.IdempotencyGuard.KeyFrom(Request);
        if (idemKey != null)
        {
            var priorId = await Planscape.API.Services.IdempotencyGuard
                .SeenResultAsync(_db, tenantId, "meeting.action", idemKey);
            if (priorId is Guid pid)
            {
                var prior = await _db.MeetingActionItems
                    .FirstOrDefaultAsync(a => a.Id == pid && a.MeetingId == meetingId);
                if (prior != null) return Ok(prior);
            }
        }

        // Resolve assignee by UserId or email
        AppUser? assigneeUser = null;
        if (req.AssigneeUserId.HasValue)
        {
            assigneeUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.AssigneeUserId.Value && u.TenantId == tenantId);
        }
        else if (!string.IsNullOrWhiteSpace(req.AssigneeEmail))
        {
            assigneeUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.AssigneeEmail && u.TenantId == tenantId);
        }

        var item = new MeetingActionItem
        {
            TenantId = tenantId,
            MeetingId = meetingId,
            Description = req.Description,
            Notes = req.Notes,
            Assignee = assigneeUser?.DisplayName ?? req.Assignee,
            AssigneeUserId = assigneeUser?.Id ?? req.AssigneeUserId,
            DueDate = req.DueDate,
            Priority = req.Priority ?? "MEDIUM",
        };

        _db.MeetingActionItems.Add(item);
        await _db.SaveChangesAsync();

        if (idemKey != null)
            await Planscape.API.Services.IdempotencyGuard
                .RecordAsync(_db, tenantId, "meeting.action", idemKey, item.Id);

        // Push assignment notification to resolved user
        if (assigneeUser != null)
        {
            _ = _push.SendToUserAsync(assigneeUser.Id, new PushPayload
            {
                Title = $"Action Item: {(item.Description.Length > 40 ? item.Description[..40] + "…" : item.Description)}",
                Body = $"From: {meeting.Title}{(item.DueDate.HasValue ? $" · due {item.DueDate.Value:d MMM}" : "")}",
                Channel = "meetings",
                Data = new Dictionary<string, string>
                {
                    ["type"] = "action_assigned",
                    ["meetingId"] = meetingId.ToString(),
                    ["actionId"] = item.Id.ToString(),
                    ["projectId"] = projectId.ToString()
                }
            });
        }

        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingUpdated", new
        {
            meeting.Id, meeting.Title, projectId, kind = "action_added",
            actionId = item.Id, item.Assignee, item.DueDate, item.Priority
        });

        return CreatedAtAction(nameof(GetOpenActions), new { projectId }, new
        {
            item.Id, item.Description, item.Assignee, item.AssigneeUserId,
            item.DueDate, item.Priority, item.Status, MeetingId = meetingId
        });
    }

    [HttpPut("{meetingId}/actions/{actionId}")]
    public async Task<ActionResult> UpdateAction(Guid projectId, Guid meetingId, Guid actionId,
        [FromBody] UpdateActionRequest req)
    {
        var tenantId = GetTenantId();
        var action = await _db.MeetingActionItems
            .Include(a => a.Meeting)
            .FirstOrDefaultAsync(a => a.Id == actionId && a.MeetingId == meetingId
                && a.Meeting!.ProjectId == projectId && a.Meeting.Project!.TenantId == tenantId);
        if (action == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        if (req.Status != null) action.Status = req.Status;
        if (req.Priority != null) action.Priority = req.Priority;
        if (req.Notes != null) action.Notes = req.Notes;
        if (req.DueDate.HasValue) action.DueDate = req.DueDate;
        if (req.LinkedIssueId != null) action.LinkedIssueId = req.LinkedIssueId;

        // Reassign — resolve new assignee
        if (req.AssigneeUserId.HasValue || req.AssigneeEmail != null || req.Assignee != null)
        {
            AppUser? newAssignee = null;
            if (req.AssigneeUserId.HasValue)
                newAssignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.AssigneeUserId.Value && u.TenantId == tenantId);
            else if (!string.IsNullOrWhiteSpace(req.AssigneeEmail))
                newAssignee = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.AssigneeEmail && u.TenantId == tenantId);

            action.Assignee = newAssignee?.DisplayName ?? req.Assignee ?? action.Assignee;
            action.AssigneeUserId = newAssignee?.Id ?? req.AssigneeUserId ?? action.AssigneeUserId;

            if (newAssignee != null)
            {
                _ = _push.SendToUserAsync(newAssignee.Id, new PushPayload
                {
                    Title = $"Action Reassigned: {(action.Description.Length > 40 ? action.Description[..40] + "…" : action.Description)}",
                    Body = $"From: {action.Meeting?.Title ?? "meeting"}{(action.DueDate.HasValue ? $" · due {action.DueDate.Value:d MMM}" : "")}",
                    Channel = "meetings",
                    Data = new Dictionary<string, string>
                    {
                        ["type"] = "action_reassigned",
                        ["meetingId"] = meetingId.ToString(),
                        ["actionId"] = actionId.ToString(),
                        ["projectId"] = projectId.ToString()
                    }
                });
            }
        }

        await _db.SaveChangesAsync();

        // Gap 13 — notify the meeting creator/chair when an action item is completed.
        if ((req.Status == "COMPLETE" || req.Status == "CLOSED") && action.Meeting?.CreatedByUserId.HasValue == true)
        {
            _ = _push.SendToUserAsync(action.Meeting.CreatedByUserId.Value, new PushPayload
            {
                Title = "Action Item Completed",
                Body = $"{action.Assignee ?? "Someone"} completed: " +
                       (action.Description.Length > 60 ? action.Description[..60] + "…" : action.Description),
                Channel = "meetings",
                Data = new Dictionary<string, string>
                {
                    ["type"] = "action_completed",
                    ["meetingId"] = meetingId.ToString(),
                    ["actionId"] = actionId.ToString(),
                    ["projectId"] = projectId.ToString()
                }
            });
        }

        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("MeetingUpdated", new
        {
            action.MeetingId, projectId, kind = "action_updated",
            actionId = action.Id, action.Status, action.Assignee, action.Priority
        });

        return Ok(new
        {
            action.Id, action.Description, action.Assignee, action.AssigneeUserId,
            action.DueDate, action.Priority, action.Status, action.LinkedIssueId
        });
    }

    [HttpGet("actions/open")]
    public async Task<ActionResult> GetOpenActions(Guid projectId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 500);
        var tenantId = GetTenantId();

        var total = await _db.MeetingActionItems
            .CountAsync(a => a.Meeting!.ProjectId == projectId && a.Meeting.Project!.TenantId == tenantId
                && (a.Status == "OPEN" || a.Status == "IN_PROGRESS"));

        var actions = await _db.MeetingActionItems
            .Where(a => a.Meeting!.ProjectId == projectId && a.Meeting.Project!.TenantId == tenantId
                && (a.Status == "OPEN" || a.Status == "IN_PROGRESS"))
            .OrderBy(a => a.DueDate).ThenBy(a => a.Priority)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id, a.Description, a.Notes, a.Assignee, a.AssigneeUserId,
                a.DueDate, a.Priority, a.Status, a.LinkedIssueId,
                MeetingId = a.MeetingId,
                MeetingTitle = a.Meeting!.Title,
                IsOverdue = a.DueDate.HasValue && a.DueDate < DateTime.UtcNow
            })
            .ToListAsync();

        return Ok(new { items = actions, total, page, pageSize });
    }

    // ── Export ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate an ICS calendar invite for the meeting.
    /// </summary>
    [HttpGet("{meetingId}/export/ics")]
    public async Task<IActionResult> ExportIcs(Guid projectId, Guid meetingId)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .Include(m => m.Attendees)
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();

        var sb = new System.Text.StringBuilder();
        var start = meeting.ScheduledAt.ToUniversalTime();
        var end = start.AddMinutes(meeting.DurationMinutes ?? 60);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var uid = $"{meeting.Id}@planscape.app";

        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//Planscape//EN");
        sb.AppendLine("CALSCALE:GREGORIAN");
        sb.AppendLine("METHOD:REQUEST");
        sb.AppendLine("BEGIN:VEVENT");
        sb.AppendLine($"UID:{uid}");
        sb.AppendLine($"DTSTAMP:{stamp}");
        sb.AppendLine($"DTSTART:{start:yyyyMMddTHHmmssZ}");
        sb.AppendLine($"DTEND:{end:yyyyMMddTHHmmssZ}");
        sb.AppendLine($"SUMMARY:{EscapeIcs(meeting.Title)}");
        if (!string.IsNullOrEmpty(meeting.Location))
            sb.AppendLine($"LOCATION:{EscapeIcs(meeting.Location)}");
        if (!string.IsNullOrEmpty(meeting.MeetingUrl))
            sb.AppendLine($"URL:{meeting.MeetingUrl}");
        if (!string.IsNullOrEmpty(meeting.RecurrenceRule))
            sb.AppendLine($"RRULE:{meeting.RecurrenceRule}");
        foreach (var a in meeting.Attendees.Where(a => !string.IsNullOrEmpty(a.Email)))
            sb.AppendLine($"ATTENDEE;CN={EscapeIcs(a.Name)};ROLE={(a.Role == "CHAIR" ? "CHAIR" : "REQ-PARTICIPANT")}:mailto:{a.Email}");
        sb.AppendLine($"ORGANIZER;CN={EscapeIcs(meeting.CreatedBy)}:mailto:noreply@planscape.app");
        sb.AppendLine("END:VEVENT");
        sb.AppendLine("END:VCALENDAR");

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/calendar", $"meeting-{meeting.Id}.ics");
    }

    /// <summary>
    /// Generate a Word document of the meeting minutes using the template engine.
    /// Returns the DocumentRecord id and a download URL.
    /// </summary>
    [HttpPost("{meetingId}/export/minutes")]
    public async Task<ActionResult> ExportMinutes(Guid projectId, Guid meetingId)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .Include(m => m.Attendees)
            .Include(m => m.AgendaItems)
            .Include(m => m.ActionItems)
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId);
        if (meeting == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        // Build a structured summary for the minutes document record.
        // Full template rendering (MiniWord) runs server-side when the
        // meeting_minutes.docx template is available; here we persist the
        // record and return the metadata so the mobile client can display
        // a "Minutes exported" confirmation. The actual file is written by
        // the template engine (same pattern as TransmittalOrchestrator).
        var docRecord = new DocumentRecord
        {
            ProjectId = projectId,
            FileName = $"minutes_{meeting.Id:N}_{DateTime.UtcNow:yyyyMMdd}.docx",
            FilePath = $"meetings/{meeting.Id}/minutes_{DateTime.UtcNow:yyyyMMdd}.docx",
            DocumentType = "MEETING_MINUTES",
            CdeStatus = "WIP",
            SuitabilityCode = "S0",
            FileSizeBytes = 0,
            UploadedBy = User.FindFirst("display_name")?.Value ?? "Unknown"
        };
        _db.Documents.Add(docRecord);
        meeting.MinutesDocumentId = docRecord.Id;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("EXPORT", "Meeting", meeting.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { documentId = docRecord.Id, kind = "minutes_exported" }));

        return Ok(new
        {
            documentId = docRecord.Id,
            fileName = docRecord.FileName,
            message = "Minutes document record created. Template rendering will produce the .docx file.",
            meetingId
        });
    }

    // ── N5 — BCC meeting ⇄ live meeting bridge ────────────────────────────────

    /// <summary>
    /// N5 — "Join live" from a scheduled BCC meeting. Returns the ACTIVE live
    /// <see cref="MeetingSession"/> bound to this Meeting, creating one (and making
    /// the caller host) if none exists. Idempotent: a second caller joins the SAME
    /// session. Flips the meeting to IN_PROGRESS on first start. The client then
    /// opens the viewer at <c>?meeting={sessionId}</c> (web) or the native meeting
    /// screen — the same session the formal Meeting's artifacts flow back into.
    /// </summary>
    [HttpPost("{meetingId}/live-session")]
    public async Task<ActionResult> StartOrJoinLiveSession(Guid projectId, Guid meetingId, [FromBody] StartLiveSessionRequest? req, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId, ct);
        if (meeting == null) return NotFound("Meeting not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        Guid? userId = Guid.TryParse(User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value, out var uid) ? uid : null;

        var session = await _db.MeetingSessions
            .FirstOrDefaultAsync(s => s.MeetingId == meetingId && s.ProjectId == projectId && s.Status == "ACTIVE", ct);
        var isNew = false;
        if (session == null)
        {
            session = new MeetingSession
            {
                TenantId = tenantId,
                ProjectId = projectId,
                MeetingId = meetingId,
                ModelId = req?.ModelId,
                HostUserId = userId,
                Status = "ACTIVE",
                CreatedBy = User.FindFirst("display_name")?.Value ?? User.Identity?.Name ?? "",
                CreatedByUserId = userId,
            };
            _db.MeetingSessions.Add(session);
            if (userId is { } hostId)
            {
                _db.MeetingViewerParticipants.Add(new MeetingViewerParticipant
                {
                    TenantId = tenantId,
                    SessionId = session.Id,
                    UserId = hostId,
                    DisplayName = req?.DisplayName ?? User.FindFirst("display_name")?.Value ?? User.Identity?.Name ?? "Host",
                    IsHost = true,
                    IsFollowingHost = false,
                    Surface = req?.Surface ?? "web",
                });
            }
            // First Join Live flips the scheduled meeting to in-progress.
            if (meeting.Status == "SCHEDULED") meeting.Status = "IN_PROGRESS";
            await _db.SaveChangesAsync(ct);
            isNew = true;
            await _audit.LogAsync("LIVE_START", "Meeting", meeting.Id.ToString(),
                System.Text.Json.JsonSerializer.Serialize(new { sessionId = session.Id }));
            // N — notify the other project members the live meeting started (in-app + push).
            await NotifyLiveMeetingStartedAsync(projectId, session.Id, userId, ct);
        }

        return Ok(new
        {
            sessionId = session.Id,
            meetingId,
            isNew,
            status = session.Status,
            modelId = session.ModelId,
            hostUserId = session.HostUserId,
        });
    }

    /// <summary>
    /// N5 — live artifacts captured against this meeting's session(s): viewpoint /
    /// markup snapshots, attendance (from the live viewer roster), and the linked
    /// sessions themselves. Action items already live on the Meeting (added live via
    /// the meeting link). Recordings land here once N2 (LiveKit Egress) is deployed —
    /// returned empty until then.
    /// </summary>
    [HttpGet("{meetingId}/live-artifacts")]
    public async Task<ActionResult> GetLiveArtifacts(Guid projectId, Guid meetingId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var owned = await _db.Meetings.AnyAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.Project!.TenantId == tenantId, ct);
        if (!owned) return NotFound();

        var sessionIds = await _db.MeetingSessions
            .Where(s => s.MeetingId == meetingId && s.ProjectId == projectId)
            .Select(s => s.Id).ToListAsync(ct);

        var sessions = await _db.MeetingSessions
            .Where(s => s.MeetingId == meetingId && s.ProjectId == projectId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Status, s.HostUserId, s.ModelId, s.CreatedAt, s.EndedAt })
            .ToListAsync(ct);

        var snapshots = await _db.MeetingSnapshots
            .Where(x => sessionIds.Contains(x.SessionId))
            .OrderByDescending(x => x.CapturedAt)
            .Select(x => new { x.Id, x.SessionId, x.Label, x.CapturedBy, x.CapturedByUserId, x.CapturedAt })
            .ToListAsync(ct);

        var attendance = await _db.MeetingViewerParticipants
            .Where(p => sessionIds.Contains(p.SessionId))
            .GroupBy(p => new { p.UserId, p.DisplayName })
            .Select(g => new
            {
                g.Key.UserId,
                g.Key.DisplayName,
                FirstJoinedAt = g.Min(p => p.JoinedAt),
                LastSeenAt = g.Max(p => p.LastSeenAt),
            })
            .ToListAsync(ct);

        // N2 — recordings for this meeting's session(s) (LiveKit Egress → object store),
        // each with a presigned (browser-reachable) playback/download URL.
        var recRows = await _db.MeetingRecordings
            .Where(r => sessionIds.Contains(r.SessionId))
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new { r.Id, r.SessionId, r.Kind, r.Status, r.StorageKey, r.FileName, r.FileSizeBytes, r.DurationSeconds, r.StartedAt, r.EndedAt })
            .ToListAsync(ct);
        var egress = new LiveKitEgressClient(_config);
        var recordings = recRows.Select(r => new
        {
            r.Id, r.SessionId, r.Kind, r.Status, r.StorageKey, r.FileName, r.FileSizeBytes, r.DurationSeconds, r.StartedAt, r.EndedAt,
            downloadUrl = egress.GetPresignedGetUrl(r.StorageKey, TimeSpan.FromHours(6)),
        }).ToList();

        return Ok(new { meetingId, sessions, snapshots, attendance, recordings });
    }

    public class StartLiveSessionRequest
    {
        public Guid? ModelId { get; set; }
        public string? DisplayName { get; set; }
        public string? Surface { get; set; }
    }

    // N — notify project members a LIVE meeting started (in-app + push, per-user prefs),
    // excl. the starter; membership-filtered; deep link ?meeting={sessionId}. Best-effort.
    private async Task NotifyLiveMeetingStartedAsync(Guid projectId, Guid sessionId, Guid? starterUserId, CancellationToken ct)
    {
        try
        {
            var projName = await _db.Projects.Where(p => p.Id == projectId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "the project";
            var starter = User.FindFirst("display_name")?.Value ?? User.Identity?.Name ?? "Someone";
            var members = await _db.ProjectMembers.Where(m => m.ProjectId == projectId).Select(m => m.UserId).Distinct().ToListAsync(ct);
            var data = new { type = "meeting_live", meetingSessionId = sessionId, projectId, deepLink = $"?meeting={sessionId}" };
            foreach (var uid in members)
            {
                if (starterUserId.HasValue && uid == starterUserId.Value) continue;
                await _notifications.NotifyUserAsync(uid, $"{starter} started a meeting",
                    $"Join the live meeting in {projName}", data, ct);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "[meeting-notify] live notify failed"); }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task PushMeetingInvitesAsync(Meeting meeting, Guid projectId, Guid? skipUserId, string subject)
    {
        try
        {
            var userIds = new HashSet<Guid>();
            foreach (var a in meeting.Attendees.Where(a => a.UserId.HasValue)) userIds.Add(a.UserId!.Value);
            foreach (var nid in Meeting.ParseNotifiedIds(meeting.NotifiedUserIds)) userIds.Add(nid);
            if (skipUserId.HasValue) userIds.Remove(skipUserId.Value);

            foreach (var uid in userIds)
            {
                _ = _push.SendToUserAsync(uid, new PushPayload
                {
                    Title = $"{subject}: {meeting.Title}",
                    Body = $"{meeting.ScheduledAt:ddd d MMM HH:mm}{(meeting.Location != null ? " · " + meeting.Location : "")}",
                    Channel = "meetings",
                    Data = new Dictionary<string, string>
                    {
                        ["type"] = "meeting_invite",
                        ["meetingId"] = meeting.Id.ToString(),
                        ["projectId"] = projectId.ToString()
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Meeting invite push fan-out failed for meeting {MeetingId}", meeting.Id);
        }
    }

    private async Task PushMinutesReadyAsync(Meeting meeting, Guid projectId, Guid? skipUserId)
    {
        try
        {
            var attendeeIds = await _db.MeetingAttendees
                .Where(a => a.MeetingId == meeting.Id && a.UserId.HasValue)
                .Select(a => a.UserId!.Value)
                .ToListAsync();
            var notifiedIds = Meeting.ParseNotifiedIds(meeting.NotifiedUserIds);
            var all = new HashSet<Guid>(attendeeIds.Concat(notifiedIds));
            if (skipUserId.HasValue) all.Remove(skipUserId.Value);

            foreach (var uid in all)
            {
                _ = _push.SendToUserAsync(uid, new PushPayload
                {
                    Title = $"Minutes Ready: {meeting.Title}",
                    Body = "Meeting minutes have been logged and are available.",
                    Channel = "meetings",
                    Data = new Dictionary<string, string>
                    {
                        ["type"] = "minutes_ready",
                        ["meetingId"] = meeting.Id.ToString(),
                        ["projectId"] = projectId.ToString()
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Minutes-ready push fan-out failed for meeting {MeetingId}", meeting.Id);
        }
    }

    private static string EscapeIcs(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record CreateMeetingRequest(
    string Title,
    string? MeetingType,
    DateTime ScheduledAt,
    int? DurationMinutes,
    string? Location,
    string? MeetingUrl,
    string? RecurrenceRule,
    Guid? SeriesId,
    Guid[]? NotifiedUserIds,
    List<AttendeeDto>? Attendees,
    List<AgendaItemDto>? AgendaItems,
    // Legacy fields kept for backward compat with older plugin + offline queue
    string? AgendaJson,
    string? AttendeesJson);

public record UpdateMeetingRequest(
    string? Title,
    string? MeetingType,
    DateTime? ScheduledAt,
    int? DurationMinutes,
    string? Location,
    string? MeetingUrl,
    string? Status,
    string? RecurrenceRule,
    Guid[]? NotifiedUserIds);

public record LogMinutesRequest(string Minutes, string? Status);

public record AttendeeDto(
    Guid? UserId,
    string? Name,
    string? Email,
    string? Company,
    string? Discipline,
    string? Role);

public record AgendaItemDto(
    string Title,
    string? Description,
    int? DurationMinutes,
    string? Presenter,
    int? OrderIndex);

public record AddAttendeeRequest(
    Guid? UserId,
    string? Name,
    string? Email,
    string? Company,
    string? Discipline,
    string? Role);

public record UpdateAttendeeRequest(
    string? AttendanceStatus,
    string? Role,
    string? Company,
    string? Discipline);

public record AddAgendaItemRequest(
    string Title,
    string? Description,
    int? DurationMinutes,
    string? Presenter,
    int? OrderIndex);

public record UpdateAgendaItemRequest(
    string? Title,
    string? Description,
    int? DurationMinutes,
    string? Presenter,
    string? Outcome,
    string? Decision,
    string? Status,
    int? OrderIndex);

public record AddActionItemRequest(
    string Description,
    string? Notes,
    string? Assignee,
    string? AssigneeEmail,
    Guid? AssigneeUserId,
    DateTime? DueDate,
    string? Priority);

public record UpdateActionRequest(
    string? Status,
    string? Priority,
    string? Notes,
    string? Assignee,
    string? AssigneeEmail,
    Guid? AssigneeUserId,
    DateTime? DueDate,
    string? LinkedIssueId);
