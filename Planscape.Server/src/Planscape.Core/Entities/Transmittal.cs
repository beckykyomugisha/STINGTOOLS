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
    public string? DocumentIdsJson { get; set; } // JSON array of document IDs
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }

    // Navigation
    public Project? Project { get; set; }
}
