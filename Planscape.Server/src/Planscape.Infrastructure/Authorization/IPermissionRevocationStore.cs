namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// Phase 156 — JWT permission-revocation lag mitigation. Standard JWT
/// auth has no built-in revocation: a user demoted from BIM Manager
/// retains access until the token expires. This store records the
/// minimum acceptable token-issued-at (<c>iat</c> claim) per user,
/// updated whenever an admin changes the user's role / membership /
/// active flag. The
/// <see cref="BimManagerOrAdminHandler"/> compares the token's
/// <c>iat</c> against this floor and denies stale tokens immediately
/// — even though the JWT itself is still cryptographically valid.
///
/// The store is intentionally narrow:
///   • Set on permission-changing actions (role demote, membership
///     deactivate, ISO 19650 role change). Costs one write.
///   • Read on every policy-gated authorisation. Cost is one Redis
///     GET (or DB fallback when Redis is unavailable). The handler
///     short-circuits on Admin/Owner so the read only fires for the
///     ProjectMember / AppUser grant paths.
///
/// TTL matches the longest plausible token lifetime so entries
/// auto-expire once no surviving tokens predate the floor.
/// </summary>
public interface IPermissionRevocationStore
{
    /// <summary>
    /// Get the current "minimum acceptable iat" for a user, in
    /// seconds since unix epoch. Returns null when no revocation has
    /// been recorded — equivalent to "no floor, accept any iat".
    /// </summary>
    Task<long?> GetMinIatAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Bump the floor for a user to "now". Any token issued before
    /// this call will fail policy checks even if its JWT signature
    /// is still valid. Idempotent — called multiple times in the
    /// same second yields the same floor.
    /// </summary>
    Task RevokeAllPriorTokensAsync(Guid userId, CancellationToken ct = default);
}
