namespace Planscape.Core.Entities;

/// <summary>
/// Version-snapshotting join table between a Transmittal and the exact
/// document versions that were physically sent. Replaces the free-form
/// DocumentIdsJson column (which loses version context when a document
/// is later superseded) with a typed row per included document.
/// </summary>
public class TransmittalDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransmittalId { get; set; }
    public Guid DocumentId { get; set; }

    /// <summary>
    /// FK to DocumentVersion — the specific revision that was sent.
    /// Null for documents included via the legacy DocumentIdsJson path.
    /// </summary>
    public Guid? DocumentVersionId { get; set; }

    /// <summary>
    /// Snapshot of CdeStatus at the moment of inclusion (e.g. "SHARED").
    /// </summary>
    public string? CdeStateAtTransmittal { get; set; }

    /// <summary>
    /// Snapshot of SuitabilityCode at the moment of inclusion (e.g. "S3").
    /// </summary>
    public string? SuitabilityAtTransmittal { get; set; }

    /// <summary>
    /// Snapshot of the file storage path at the moment of inclusion so
    /// the transmittal record stays accurate even after the document is
    /// revised or the storage path changes.
    /// </summary>
    public string? FilePathAtTransmittal { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Transmittal? Transmittal { get; set; }
    public DocumentRecord? Document { get; set; }
    public DocumentVersion? DocumentVersion { get; set; }
}
