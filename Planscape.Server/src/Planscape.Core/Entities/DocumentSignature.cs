namespace Planscape.Core.Entities;

/// <summary>
/// Records the e-signature event when a document is published (SHARED→PUBLISHED).
/// The BIM manager's name, timestamp, and approval note are persisted here;
/// a Hangfire background job then stamps the physical PDF with a watermark.
/// </summary>
public class DocumentSignature : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid DocumentId { get; set; }

    public string SignedByUserId { get; set; } = "";
    public string SignedByName { get; set; } = "";
    public DateTime SignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional note attached to the signature (approval comments, standard clause, etc.).
    /// </summary>
    public string? SignatureNote { get; set; }

    /// <summary>
    /// Storage path of the watermarked file once the PDF stamp job completes.
    /// Null until the job runs successfully.
    /// </summary>
    public string? WatermarkedFilePath { get; set; }

    /// <summary>
    /// PENDING → APPLIED (watermark stamped) | FAILED (non-PDF or stamp error) |
    /// SKIPPED (non-PDF file type where stamping is not applicable).
    /// </summary>
    public string WatermarkStatus { get; set; } = "PENDING";

    // Navigation
    public DocumentRecord? Document { get; set; }
}
