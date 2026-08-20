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

    // #633 — where this document may go next, and whether approval is needed
    // first. NOT PERSISTED and NOT part of the state machine: it is computed
    // from DocumentsController's ValidTransitions + ApprovalRequiredTransitions
    // and attached to the response, so a client stops keeping its own copy of
    // rules the server already enforces. See CdeTransitionOption.
    //
    // NULL means "this response did not compute it" — an older server, or an
    // endpoint that does not fill it. It does NOT mean "no transitions are
    // available", and a client must not render it that way: unknown and
    // none-allowed are different answers, and only one of them is a refusal.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public IReadOnlyList<CdeTransitionOption>? AllowedTransitions { get; set; }

    // Navigation
    public Project? Project { get; set; }
    public CdeContainer? Container { get; set; }
    public List<DocumentVersion> Versions { get; set; } = new();
}
