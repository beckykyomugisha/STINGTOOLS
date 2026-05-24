namespace Planscape.Core.Entities;

/// <summary>
/// Phase 179.2 — Per-user, per-photo NDA acceptance record. Created
/// the first time a user opens a photo that has at least one
/// <see cref="PhotoAccessRule"/> with <c>RequiresNdaAcceptance = true</c>.
///
/// The acceptance is keyed on (PhotoId, UserId) — re-acceptance is
/// idempotent (PK collision returns the existing row). Audit fields
/// capture the request fingerprint at acceptance time so a later
/// dispute can prove the click happened.
///
/// Tenant scoping is implicit via the photo's project; we don't repeat
/// TenantId here (the photo is the source of truth and the cascading
/// FK ensures rows die with the photo).
/// </summary>
public class PhotoNdaAcceptance
{
    public Guid PhotoId { get; set; }
    public Guid UserId  { get; set; }

    public DateTime AcceptedAt   { get; set; } = DateTime.UtcNow;
    public string?  IpAddress    { get; set; }
    public string?  UserAgent    { get; set; }

    /// <summary>The NDA text snapshot the user agreed to (stored verbatim
    /// so a later text revision doesn't retroactively change what was
    /// accepted). Optional — defaults to the project policy boilerplate.</summary>
    public string? AcceptedTextSha256 { get; set; }

    public SitePhoto? Photo { get; set; }
    public AppUser?   User  { get; set; }
}
