namespace Planscape.Core.Interfaces;

/// <summary>
/// A single-use claim on a short-lived identifier — the "has this token id been
/// seen before?" question behind replay protection.
///
/// Exists as an interface so the decision is testable. The production
/// implementation is a Redis SET NX, which a WebApplicationFactory test cannot
/// drive: with no Redis reachable, every call took the caller's fail-open
/// branch, so the *blocking* half of the guard had no coverage at all.
/// </summary>
public interface IReplayGuard
{
    /// <summary>
    /// Atomically claims <paramref name="key"/> for <paramref name="ttl"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c> if this call took the claim (first use); <c>false</c> if the
    /// key was already claimed (a replay).
    /// </returns>
    /// <remarks>
    /// Implementations THROW on transport failure rather than swallowing it.
    /// Whether an unreachable backing store fails open or closed is a security
    /// decision that belongs to the caller, at the call site, where it can be
    /// read next to the thing it protects — not buried in a storage adapter.
    /// </remarks>
    Task<bool> TryClaimAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
