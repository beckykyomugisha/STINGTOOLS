namespace Planscape.Core.Entities;

/// <summary>
/// Records the outcome of a single Primavera P6 live-link sync attempt for a project.
/// Provides the data surfaced by GET /api/projects/{id}/p6/status.
/// </summary>
public class P6SyncLog : ITenantScoped
{
    public int      Id               { get; set; }
    public int      ProjectId        { get; set; }
    public Project  Project          { get; set; } = null!;
    public int      TenantId         { get; set; }
    public DateTime SyncedAt         { get; set; } = DateTime.UtcNow;
    public int      ActivitiesPolled { get; set; }
    public int      ElementsUpdated  { get; set; }
    /// <summary>Null when the sync succeeded; non-null on partial or full failure.</summary>
    public string?  ErrorMessage     { get; set; }
}
