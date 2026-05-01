namespace Planscape.Core.Entities;

/// <summary>
/// Audit trail for all write operations — required for ISO 19650 compliance.
/// </summary>
public class AuditLog : ITenantScoped,  ITenantScoped
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
}
