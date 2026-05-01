namespace Planscape.Core.Entities;

/// <summary>
/// Audit trail for all write operations — required for ISO 19650 compliance.
///
/// S1.8 — every row carries a SHA-256 <see cref="RowHash"/> chained to the
/// previous row's <see cref="RowHash"/> via <see cref="PrevHash"/>, so an
/// auditor can verify integrity by replaying the chain. Hashing happens in
/// a Postgres BEFORE-INSERT trigger (created by the migration) so it can't
/// be bypassed by direct SQL writes. Rows are partitioned by month on
/// <see cref="Timestamp"/>; cold partitions detach to S3 Parquet for
/// long-term retention without bloating the hot tablespace.
/// </summary>
public class AuditLog : ITenantScoped
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = ""; // e.g., "tag_sync", "issue_created", "cde_transition"
    public string EntityType { get; set; } = ""; // e.g., "TaggedElement", "BimIssue", "DocumentRecord"
    public string? EntityId { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Mobile device tracking (S11)
    public string? DeviceId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Source { get; set; } = "desktop";

    /// <summary>
    /// S1.8 — SHA-256 hex digest of (PrevHash || canonical row payload).
    /// Computed by a Postgres BEFORE-INSERT trigger so application code
    /// can't tamper. Verifying the chain is a single SQL pass: each row's
    /// PrevHash must equal the prior row's RowHash (per tenant).
    /// </summary>
    public string? PrevHash { get; set; }
    public string? RowHash { get; set; }
}
