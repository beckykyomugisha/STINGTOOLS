namespace Planscape.Core.Entities;

/// <summary>
/// Tracks the last-synced position per project/device for incremental sync.
/// </summary>
public class SyncWatermark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string DeviceId { get; set; } = "";
    public string EntityType { get; set; } = ""; // e.g. "TaggedElement", "BimIssue"
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
    public long LastSyncedSequence { get; set; }

    // Navigation
    public Project? Project { get; set; }
}
