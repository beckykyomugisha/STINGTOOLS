namespace Planscape.Core.Entities;

/// <summary>
/// Phase 178c (T3-24) — A point-in-time snapshot of a
/// <see cref="DocumentRecord"/>. Auto-created whenever the parent doc's
/// CDE state transitions; can also be created manually via the
/// revisions endpoint to capture an interim "P02 issued for comment"
/// snapshot.
///
/// <para>Distinct from <see cref="DocumentVersion"/> (which tracks new
/// file uploads to the same logical document). A new file upload may or
/// may not warrant a new revision; conversely, a CDE transition without
/// a file change still mints a revision row so the audit trail captures
/// "this is the file that was approved at S3".</para>
/// </summary>
public class DocumentRevision : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }

    /// <summary>BS 1192 / ISO 19650 revision label, e.g. "P01", "P02", "C01".</summary>
    public string Revision { get; set; } = "P01";

    /// <summary>The doc's CDE state at the moment this revision was minted.</summary>
    public string CdeStateAtRevision { get; set; } = "WIP";

    /// <summary>The doc's suitability code at the moment this revision was minted.</summary>
    public string? SuitabilityAtRevision { get; set; }

    /// <summary>Snapshot of the file path. Storage layer keeps the underlying
    /// blob immutable; this row points at it.</summary>
    public string? FilePath { get; set; }

    public long? FileSizeBytes { get; set; }

    public string? ContentHash { get; set; }

    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Free-text summary of what changed at this revision.</summary>
    public string? CommentSummary { get; set; }

    /// <summary>Source of the revision: "auto_cde_transition" | "manual" | "upload".</summary>
    public string Source { get; set; } = "manual";

    // Navigation
    public DocumentRecord? Document { get; set; }
}
