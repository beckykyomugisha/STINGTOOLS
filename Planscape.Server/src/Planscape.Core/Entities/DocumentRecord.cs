namespace Planscape.Core.Entities;

/// <summary>
/// ISO 19650 document record with CDE lifecycle state management.
/// </summary>
public class DocumentRecord : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
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

    // Phase 175 audit P1-15 — antivirus scan tracking. Files uploaded
    // via the presigned-URL flow start as PENDING and are flipped to
    // CLEAN by the scanner job (or INFECTED → moved to quarantine).
    // Multipart uploads through the API skip the scan entirely (legacy
    // path) and stay at SKIPPED.
    public string ScanStatus { get; set; } = "SKIPPED"; // PENDING / CLEAN / INFECTED / SKIPPED
    public DateTime? ScanScannedAt { get; set; }
    public string? ScanThreatName { get; set; }

    // Gap 1 — CDE folder hierarchy. Null = root / unclassified.
    public Guid? ContainerId { get; set; }

    // Gap 4 — E-signature on S4 publication. Populated when the document
    // transitions SHARED→PUBLISHED; stamped by DocumentPublicationStampJob.
    public string? PublishedByUserId { get; set; }
    public string? PublishedByName { get; set; }
    public DateTime? PublishedAt { get; set; }

    // GAP-18 — retention policy. When set, DocumentRetentionArchiveJob will
    // auto-transition the document from PUBLISHED to ARCHIVE on this date.
    public DateTime? RetentionExpiresAt { get; set; }

    // Navigation
    public Project? Project { get; set; }
    public CdeContainer? Container { get; set; }
    public List<DocumentVersion> Versions { get; set; } = new();
}
