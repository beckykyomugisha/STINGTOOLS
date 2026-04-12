namespace Planscape.Core.Entities;

/// <summary>
/// Records sync conflicts for manual resolution or audit.
/// </summary>
public class SyncConflict
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string EntityType { get; set; } = ""; // e.g. "TaggedElement"
    public string EntityId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string ConflictType { get; set; } = ""; // e.g. "VERSION_MISMATCH", "CONCURRENT_EDIT"
    public string? ClientPayloadJson { get; set; }
    public string? ServerPayloadJson { get; set; }
    public string Resolution { get; set; } = "PENDING"; // PENDING, SERVER_WINS, CLIENT_WINS, MERGED
    public string? ResolvedBy { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    // Navigation
    public Project? Project { get; set; }
}
