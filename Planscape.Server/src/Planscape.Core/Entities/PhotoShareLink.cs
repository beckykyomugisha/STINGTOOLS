namespace Planscape.Core.Entities;

/// <summary>
/// Phase 179 — Time-bounded signed link to a single photo or an album.
/// Generates a random opaque token; consumers GET
/// <c>/api/share/{token}</c> with no auth and receive either the
/// redacted derivative (when set) or the original. Used to share one-off
/// photos with engineers / sub-contractors outside the tenant without
/// minting them a ClientGuest user.
///
/// Revocation: setting <see cref="RevokedAt"/> blocks all subsequent
/// fetches. Expiry is checked on every fetch.
/// </summary>
public class PhotoShareLink : ITenantScoped
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Set when the link points at a single photo.</summary>
    public Guid? PhotoId { get; set; }

    /// <summary>Set when the link points at an album (recipients see the album page).</summary>
    public Guid? AlbumId { get; set; }

    /// <summary>Opaque token in the URL; cryptographically random ≥ 32 bytes.</summary>
    public string Token { get; set; } = "";

    /// <summary>Free-text label visible to the issuing user only — "Sent to Acme MEP 9 May".</summary>
    public string? Label { get; set; }

    /// <summary>UTC; null = no expiry (rare; default the caller passes 14 days).</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Forces redacted derivatives even for original-eligible audiences.</summary>
    public bool ForceRedacted { get; set; } = true;

    /// <summary>Lifetime cap on number of fetches; null = unlimited.</summary>
    public int? MaxFetches  { get; set; }
    public int  FetchCount  { get; set; } = 0;

    public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
    public Guid?    CreatedByUserId  { get; set; }
    public DateTime? RevokedAt       { get; set; }
    public Guid?     RevokedByUserId { get; set; }

    public SitePhoto?  Photo  { get; set; }
    public PhotoAlbum? Album  { get; set; }
    public Project?    Project { get; set; }
}
