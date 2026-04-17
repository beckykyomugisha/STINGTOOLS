namespace Planscape.Core.Entities;

/// <summary>
/// Records a sync conflict where a client attempted to overwrite a server
/// record with a stale (or concurrent) edit. Used for audit and later
/// reconciliation via the dashboard or the mobile client.
/// </summary>
public class SyncConflict
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? TaggedElementId { get; set; }

    /// <summary>
    /// Revit ElementId (or other external id) for the element that conflicted.
    /// Stored as a string so this entity can be reused for non-Revit sources.
    /// </summary>
    public string ElementId { get; set; } = "";

    /// <summary>STALE_UPDATE, CONCURRENT_EDIT, DELETED_ON_SERVER, …</summary>
    public string ConflictType { get; set; } = "STALE_UPDATE";

    /// <summary>SERVER_WINS, CLIENT_WINS, MERGED, PENDING.</summary>
    public string Resolution { get; set; } = "SERVER_WINS";

    public DateTime? ServerTimestamp { get; set; }
    public DateTime? ClientTimestamp { get; set; }

    public string? ClientUserName { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Project? Project { get; set; }
    public TaggedElement? TaggedElement { get; set; }
}
