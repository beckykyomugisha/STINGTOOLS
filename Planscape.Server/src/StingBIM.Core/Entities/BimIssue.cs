namespace StingBIM.Core.Entities;

/// <summary>
/// ISO 19650 BIM issue / RFI / NCR tracked across project lifecycle.
/// </summary>
public class BimIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string IssueCode { get; set; } = ""; // e.g., RFI-0001, NCR-0003, SI-0012
    public string Type { get; set; } = "RFI"; // RFI, NCR, SI, TQ, DESIGN, SAFETY, CLASH
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Priority { get; set; } = "MEDIUM"; // CRITICAL, HIGH, MEDIUM, LOW
    public string Status { get; set; } = "OPEN"; // OPEN, IN_PROGRESS, RESOLVED, CLOSED
    public string? Assignee { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? Discipline { get; set; }
    public string? Revision { get; set; }
    public string? LinkedElementIds { get; set; } // JSON array of Revit element IDs
    public string? BcfGuid { get; set; } // BCF 2.1 topic GUID

    // Navigation
    public Project? Project { get; set; }
    public List<IssueAttachment> Attachments { get; set; } = new();
}
