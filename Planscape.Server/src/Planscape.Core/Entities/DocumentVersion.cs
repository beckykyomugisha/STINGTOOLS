namespace Planscape.Core.Entities;

/// <summary>
/// Tracks each uploaded revision of a DocumentRecord.
/// When the same project + filename is re-uploaded, a new version row is created
/// and DocumentRecord.Revision is incremented.
/// </summary>
public class DocumentVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public int VersionNumber { get; set; }
    public string FilePath { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string? ContentHash { get; set; }
    public string UploadedBy { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public DocumentRecord? Document { get; set; }
}
