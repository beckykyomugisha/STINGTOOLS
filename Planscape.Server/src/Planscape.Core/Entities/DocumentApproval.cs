namespace Planscape.Core.Entities;

/// <summary>
/// Records an approval decision for a CDE state transition per ISO 19650-2 §5.6.
/// Required before SHARED→PUBLISHED transition can complete.
/// </summary>
public class DocumentApproval : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>The CDE transition this approval covers, e.g. "SHARED->PUBLISHED".</summary>
    public string Transition { get; set; } = "";

    /// <summary>PENDING, APPROVED, REJECTED.</summary>
    public string Status { get; set; } = "PENDING";

    public string RequestedBy { get; set; } = "";
    /// <summary>User ID of the person who requested approval — used to push decision notifications.</summary>
    public Guid? RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Comments { get; set; }

    /// <summary>
    /// The document revision at the time this approval was requested.
    /// Scopes the approval gate so a reworked document (new revision) cannot
    /// be published against an approval granted for an earlier revision.
    /// </summary>
    public string? RevisionSnapshot { get; set; }

    // Navigation
    public DocumentRecord? Document { get; set; }
    public Project? Project { get; set; }
}
