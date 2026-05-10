namespace Planscape.Core.Entities;

/// <summary>
/// Phase 179 — Named collection of <see cref="SitePhoto"/> records.
///
/// Albums are the primitive used by the BIM-manager surface to curate
/// site photos for a specific audience (client weekly digest, handover
/// bundle, internal toolbox-talk pack, etc.). Membership is many-to-many
/// via <see cref="PhotoAlbumPhoto"/> so a single photo can sit in
/// multiple albums (e.g. a defect shot can be both in "Snag list 2026-W19"
/// and in "Handover deficiencies").
///
/// Visibility:
///   - <see cref="Visibility"/> = Internal      → project members only
///   - <see cref="Visibility"/> = Members       → project members; client portal hidden
///   - <see cref="Visibility"/> = Client        → ClientGuest tenant readers see redacted
///   - <see cref="Visibility"/> = Distribution  → only members of the linked DistributionGroup
///
/// Coordinators can VIEW albums their distribution group covers; only
/// project Author / PM / Admin / Owner can MUTATE (add/remove photos,
/// edit metadata, lock).
/// </summary>
public class PhotoAlbum : ITenantScoped
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid ProjectId { get; set; }

    public string Name        { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Internal | Members | Client | Distribution.</summary>
    public string Visibility { get; set; } = "Members";

    /// <summary>When Visibility=Distribution, the group whose members can see this album.</summary>
    public Guid? DistributionGroupId { get; set; }

    /// <summary>Free-text taxonomy (Weekly | Handover | SnagList | ToolboxTalk | …) — used for filtering.</summary>
    public string? Kind { get; set; }

    /// <summary>Cover thumbnail — first photo's id by default; settable for cosmetic curation.</summary>
    public Guid? CoverPhotoId { get; set; }

    /// <summary>Locked albums reject add/remove until an admin unlocks. Used to freeze a handover bundle.</summary>
    public bool IsLocked { get; set; } = false;
    public DateTime? LockedAt { get; set; }
    public Guid?     LockedByUserId { get; set; }

    /// <summary>When set, photos auto-archive (Audience→Withdrawn) this many days after AlbumCreatedAt.</summary>
    public int? AutoArchiveAfterDays { get; set; }

    /// <summary>
    /// Phase 180 — Smart-album filter spec. When set, the album is
    /// "auto-curated": a Hangfire job (PhotoSmartAlbumMaterialiseJob)
    /// re-evaluates this filter and resets the album membership to the
    /// matching photos. Membership cannot be hand-edited while a smart
    /// filter is active (the job resets it).
    ///
    /// Schema (JSON):
    ///   {
    ///     "reason":      "Defect"   // optional, exact match
    ///     "level":       "L02"      // optional
    ///     "zone":        "Z01"      // optional
    ///     "discipline":  "M"        // optional, joined via Issue.Discipline
    ///     "audienceIn":  ["Approved","ClientPortal"]   // optional
    ///     "fromDays":    14         // optional, capturedAt within last N days
    ///     "limit":       100        // optional, defaults to 200
    ///   }
    /// </summary>
    public string? SavedFilterJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid?    CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid?     UpdatedByUserId { get; set; }

    public Project? Project { get; set; }
    public DistributionGroup? DistributionGroup { get; set; }

    public static readonly string[] ValidVisibilities = { "Internal", "Members", "Client", "Distribution" };
}
