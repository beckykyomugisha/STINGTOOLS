namespace Planscape.Core.Entities;

/// <summary>
/// BIM coordination meeting record with agenda, minutes, and linked action items.
/// </summary>
public class Meeting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string MeetingType { get; set; } = "BIM Coordination"; // BIM Coordination, Design Review, Client Review, Handover, Clash Resolution
    public DateTime ScheduledAt { get; set; }
    public string? Minutes { get; set; }
    public string? AgendaJson { get; set; } // JSON array of agenda items
    public string? AttendeesJson { get; set; } // JSON array of attendee names/emails
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Project? Project { get; set; }
    public ICollection<MeetingActionItem> ActionItems { get; set; } = new List<MeetingActionItem>();
}

/// <summary>
/// Action item assigned during a coordination meeting.
/// </summary>
public class MeetingActionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MeetingId { get; set; }
    public string Description { get; set; } = "";
    public string? Assignee { get; set; }
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = "OPEN"; // OPEN, IN_PROGRESS, COMPLETE, ESCALATED
    public string? LinkedIssueId { get; set; } // FK to BimIssue (optional escalation)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Meeting? Meeting { get; set; }
}
