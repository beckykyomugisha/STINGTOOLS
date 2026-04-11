namespace Planscape.Core.Entities;

/// <summary>
/// ISO 19650 document record with CDE lifecycle state management.
/// </summary>
public class DocumentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string FileName { get; set; } = "";
    public string? FilePath { get; set; }
    public string? Description { get; set; }
    public string DocumentType { get; set; } = ""; // DR, SH, SP, SK, etc.
    public string CdeStatus { get; set; } = "WIP"; // WIP, SHARED, PUBLISHED, ARCHIVE
    public string SuitabilityCode { get; set; } = "S0"; // S0-S7, CR, AB
    public string? Revision { get; set; }
    public string? Discipline { get; set; }
    public string? Originator { get; set; } // ISO 19650 originator code
    public long FileSizeBytes { get; set; }
    public string? ContentHash { get; set; } // SHA-256 for dedup
    public string UploadedBy { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? StatusHistoryJson { get; set; } // JSON array of status transitions

    // Navigation
    public Project? Project { get; set; }
    public List<DocumentVersion> Versions { get; set; } = new();
}
