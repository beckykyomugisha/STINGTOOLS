namespace Planscape.Core.Entities;

/// <summary>
/// Phase 179 — Named recipient list referenced by photo / album / digest
/// audience controls. Replaces the implicit "everyone in the project"
/// behaviour of the v1 site-photo workflow with explicit, project-scoped
/// distribution groups (e.g. "Client weekly", "Contractor only", "Design
/// team only"). Members are <see cref="DistributionGroupMember"/> rows
/// — supporting both internal AppUser principals and external email
/// recipients.
///
/// Used by:
///   - <see cref="PhotoAlbum.DistributionGroupId"/> for album visibility
///   - <see cref="PhotoAccessRule.DistributionGroupId"/> for per-photo ACLs
///   - daily digest job for recipient resolution
///   - share-link revocation lists
/// </summary>
public class DistributionGroup : ITenantScoped
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid ProjectId { get; set; }

    public string Name        { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Client | Internal | Mixed — drives default redaction behaviour for items shared with the group.</summary>
    public string Kind { get; set; } = "Internal";

    /// <summary>When true, the group is included in the project's daily-digest dispatch.</summary>
    public bool IncludeInDailyDigest { get; set; } = false;

    /// <summary>When true, members of this group always receive redacted derivatives (treated as ClientGuest-equivalent).</summary>
    public bool ForceRedacted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid?    CreatedByUserId { get; set; }

    public Project? Project { get; set; }

    public static readonly string[] ValidKinds = { "Client", "Internal", "Mixed" };
}
