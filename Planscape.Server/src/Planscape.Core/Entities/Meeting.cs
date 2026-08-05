namespace Planscape.Core.Entities;

/// <summary>
/// BIM coordination meeting with structured agenda, attendees, minutes,
/// notification list (BCC), and linked action items.
/// </summary>
public class Meeting : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = "";

    // MEETING_TYPE values: BIM Coordination | Design Review | Client Review |
    // Handover | Clash Resolution | Progress Review | RFI Review | Safety Briefing
    public string MeetingType { get; set; } = "BIM Coordination";

    public DateTime ScheduledAt { get; set; }
    public int? DurationMinutes { get; set; }

    // Physical or virtual location. Empty = TBC. Teams/Zoom URLs go in MeetingUrl.
    public string? Location { get; set; }

    // Video-conferencing join link (Teams, Zoom, Google Meet …).
    public string? MeetingUrl { get; set; }

    // SCHEDULED | IN_PROGRESS | COMPLETED | CANCELLED
    public string Status { get; set; } = "SCHEDULED";

    // Free-text minutes written during / after the meeting.
    // For rich minutes (auto-rendered from agenda items) the template engine
    // writes to _BIM_COORD/generated/ and links back via MinutesDocumentId.
    public string? Minutes { get; set; }

    // FK to a DocumentRecord created by the /export endpoint (Word minutes).
    public Guid? MinutesDocumentId { get; set; }

    // NOTIFICATION LIST (BCC equivalent) — AppUser ids who receive the
    // meeting invite and minutes but are not formal attendees. Stored as a
    // JSON array of GUID strings, same schema as BimIssue.WatcherUserIds.
    // Migration: dotnet ef migrations add AddMeetingNotifiedUserIds
    public string? NotifiedUserIds { get; set; }

    // RECURRENCE — iCal RRULE string (e.g. "FREQ=WEEKLY;BYDAY=MO;COUNT=10").
    // Null = one-off. The server does not auto-expand recurrences; the plugin
    // or a future calendar integration reads this to create the series.
    public string? RecurrenceRule { get; set; }

    // Links meetings that belong to the same recurring series.
    public Guid? SeriesId { get; set; }

    public string CreatedBy { get; set; } = "";
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Project? Project { get; set; }
    public ICollection<MeetingAttendee> Attendees { get; set; } = new List<MeetingAttendee>();
    public ICollection<MeetingAgendaItem> AgendaItems { get; set; } = new List<MeetingAgendaItem>();
    public ICollection<MeetingActionItem> ActionItems { get; set; } = new List<MeetingActionItem>();

    public static IReadOnlyList<Guid> ParseNotifiedIds(string? raw)
        => BimIssue.ParseWatcherIds(raw);

    public static string? SerializeNotifiedIds(IEnumerable<Guid>? ids)
        => BimIssue.SerializeWatcherIds(ids);
}

/// <summary>
/// A person invited to or attending a meeting.
/// Role distinguishes chair / secretary / attendee / notified-only (BCC).
/// </summary>
public class MeetingAttendee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MeetingId { get; set; }

    // Resolved AppUser id when the attendee is a registered project member.
    public Guid? UserId { get; set; }

    // Display name — always populated; may be from AppUser.DisplayName or
    // entered free-form for external attendees.
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Company { get; set; }
    public string? Discipline { get; set; }

    // CHAIR | SECRETARY | ATTENDEE | NOTIFIED (BCC — receives minutes but not
    // counted toward quorum)
    public string Role { get; set; } = "ATTENDEE";

    // INVITED | CONFIRMED | ATTENDED | ABSENT | APOLOGY
    public string AttendanceStatus { get; set; } = "INVITED";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Meeting? Meeting { get; set; }
    public AppUser? User { get; set; }
}

/// <summary>
/// One agenda item within a meeting (ordered list, outcomes recorded in-place).
/// </summary>
public class MeetingAgendaItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MeetingId { get; set; }

    public int OrderIndex { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Presenter { get; set; }

    // Filled in during / after the meeting.
    public string? Outcome { get; set; }
    public string? Decision { get; set; }

    // PENDING | DISCUSSED | DEFERRED | RESOLVED
    public string Status { get; set; } = "PENDING";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Meeting? Meeting { get; set; }
}

/// <summary>
/// Action item assigned during a coordination meeting.
/// </summary>
public class MeetingActionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MeetingId { get; set; }

    public string Description { get; set; } = "";
    public string? Notes { get; set; }

    // Display name (legacy / free-form for external assignees)
    public string? Assignee { get; set; }
    // Registered project member FK — preferred over display name.
    public Guid? AssigneeUserId { get; set; }

    public DateTime? DueDate { get; set; }

    // CRITICAL | HIGH | MEDIUM | LOW
    public string Priority { get; set; } = "MEDIUM";

    // OPEN | IN_PROGRESS | COMPLETE | ESCALATED | CLOSED
    public string Status { get; set; } = "OPEN";

    // BimIssue.Id if this action was escalated to a tracked issue.
    public string? LinkedIssueId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Meeting? Meeting { get; set; }
    public AppUser? AssigneeUser { get; set; }
}
