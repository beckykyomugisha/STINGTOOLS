namespace Planscape.Core.Entities;

/// <summary>
/// ISO 19650 document transmittal record.
/// </summary>
public class Transmittal : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string TransmittalCode { get; set; } = ""; // TX-0001
    public string Recipient { get; set; } = "";
    public string Status { get; set; } = "DRAFT"; // DRAFT, SENT, ACKNOWLEDGED
    public string? Notes { get; set; }
    /// <summary>
    /// Legacy: JSON array of document IDs. Preserved for backwards compat.
    /// New code should use the <see cref="Documents"/> join table which
    /// captures the exact document version and CDE state at send time.
    /// </summary>
    public string? DocumentIdsJson { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }

    /// <summary>Named recipient user ID — used for targeted push notification on send.</summary>
    public Guid? RecipientUserId { get; set; }
    /// <summary>Optional SLA deadline by which the recipient must acknowledge.</summary>
    public DateTime? SlaDeadline { get; set; }
    /// <summary>When the recipient acknowledged this transmittal (ACKNOWLEDGED state).</summary>
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    /// <summary>When the recipient formally responded (RESPONDED state).</summary>
    public DateTime? RespondedAt { get; set; }
    public string? RespondedBy { get; set; }
    /// <summary>Recipient response notes/comments written during the Respond action.</summary>
    public string? ResponseNotes { get; set; }

    // Navigation
    public Project? Project { get; set; }

    /// <summary>
    /// Gap 5 — version-snapshotting join rows. Each row captures the exact
    /// DocumentVersion (and its CDE state / suitability / file path) that
    /// was physically sent with this transmittal.
    /// </summary>
    public List<TransmittalDocument> Documents { get; set; } = new();
}
