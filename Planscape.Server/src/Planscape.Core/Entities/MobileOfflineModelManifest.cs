namespace Planscape.Core.Entities;

/// <summary>
/// Server-side record of every mobile-cached scene chunk per (user,
/// project, model). The mobile app pushes the manifest after a sync so
/// the server can:
/// <list type="bullet">
/// <item>Drive incremental sync — only ship chunks whose ContentHash changed.</item>
/// <item>Estimate device storage usage across the user's projects.</item>
/// <item>Expire/purge old cache on logout / project removal.</item>
/// <item>Pre-warm the next project's cache when the user pins a project.</item>
/// </list>
///
/// The chunk-level metadata mirrors the <see cref="SceneNode"/> graph —
/// each cached row corresponds to one scene node the mobile renderer
/// has fetched. Geometry payloads themselves live on the device
/// filesystem (or IndexedDB on web); this row stores only the manifest
/// pointer + hash for change detection.
/// </summary>
public class MobileOfflineModelManifest : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public Guid ProjectModelId { get; set; }

    /// <summary>Device identifier — same as the push-token DeviceId.</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>
    /// SceneNode id this manifest row mirrors. Joined back to
    /// <see cref="SceneNode"/> to find the canonical chunk URL +
    /// current hash for delta calculation.
    /// </summary>
    public Guid SceneNodeId { get; set; }

    /// <summary>SHA-256 of the cached geometry chunk on device.</summary>
    public string ContentHash { get; set; } = "";

    /// <summary>Approximate bytes used on device for this chunk.</summary>
    public long CachedSizeBytes { get; set; }

    /// <summary>"Glb" / "Glft" / "Xkt" — payload format used on device.</summary>
    public string Format { get; set; } = "Glb";

    /// <summary>
    /// Cache tier — "Pinned" stays until user unpins, "Active" survives
    /// LRU eviction, "Background" pre-fetched but evicted first.
    /// </summary>
    public string Tier { get; set; } = "Active";

    public DateTime FirstCachedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the device version is current vs the server.</summary>
    public bool IsStale { get; set; } = false;

    public Project? Project { get; set; }
    public ProjectModel? ProjectModel { get; set; }
    public SceneNode? SceneNode { get; set; }
}
