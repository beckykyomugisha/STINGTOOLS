namespace Planscape.Core.Entities;

/// <summary>
/// Links a BimIssue to a DocumentRecord (file attachment).
/// </summary>
public class IssueAttachment : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid IssueId { get; set; }
    public Guid DocumentId { get; set; }
    public DateTime AttachedAt { get; set; } = DateTime.UtcNow;
    public string AttachedBy { get; set; } = "";

    // Navigation
    public BimIssue? Issue { get; set; }
    public DocumentRecord? Document { get; set; }
}
