using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Planscape.Core.Interfaces;

namespace Planscape.API.Services;

/// <summary>
/// The OAuth <c>state</c> for the ACC 3-legged flow.
///
/// It was a plain <c>tenant.user.project.nonce</c> string, so anyone who could
/// reach the anonymous callback could forge a state for ANY tenant/project and
/// bind their own Autodesk grant to it. Now:
///
///   * the payload {tenantId, projectId, userId, nonce} is sealed with an
///     <see cref="ITimeLimitedDataProtector"/> (purpose-scoped, expires after
///     <see cref="Lifetime"/>) — a tampered, foreign or expired state fails to
///     unprotect;
///   * the nonce is claimed through <see cref="IReplayGuard"/> (Redis SET NX in
///     production) when the callback redeems it, so each state works ONCE.
///     A replay-store outage fails CLOSED here: binding a team-shared grant is
///     not worth a replay window.
/// </summary>
public sealed class AccOAuthState
{
    public const string Purpose = "acc-oauth-state.v1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const string NonceKeyPrefix = "acc-oauth-nonce:";

    public sealed record Payload(Guid TenantId, Guid ProjectId, Guid UserId, string Nonce);

    public enum RedeemError { None, Invalid, Replayed }

    private readonly ITimeLimitedDataProtector _protector;
    private readonly IReplayGuard _replay;

    public AccOAuthState(IDataProtectionProvider provider, IReplayGuard replay)
    {
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
        _replay = replay;
    }

    public string Issue(Guid tenantId, Guid projectId, Guid userId)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return _protector.Protect(JsonSerializer.Serialize(new Payload(tenantId, projectId, userId, nonce)), Lifetime);
    }

    /// <summary>
    /// Unprotect and consume. <see cref="RedeemError.Invalid"/> for a
    /// tampered/expired/foreign state, <see cref="RedeemError.Replayed"/> when the
    /// nonce was already redeemed. Throws when the replay store is unreachable.
    /// </summary>
    public async Task<(Payload? Payload, RedeemError Error)> RedeemAsync(string state, CancellationToken ct)
    {
        Payload? p;
        try
        {
            p = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(state));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return (null, RedeemError.Invalid);
        }
        if (p == null || p.TenantId == Guid.Empty || p.ProjectId == Guid.Empty || string.IsNullOrEmpty(p.Nonce))
            return (null, RedeemError.Invalid);

        // TTL = lifetime + margin: once the protector has expired the state, the
        // claim no longer matters.
        bool first = await _replay.TryClaimAsync(NonceKeyPrefix + p.Nonce, Lifetime + TimeSpan.FromMinutes(1), ct);
        return first ? (p, RedeemError.None) : (p, RedeemError.Replayed);
    }
}
