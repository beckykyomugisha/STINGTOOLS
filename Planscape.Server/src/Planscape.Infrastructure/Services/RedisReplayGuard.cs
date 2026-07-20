using Planscape.Core.Interfaces;
using StackExchange.Redis;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// <see cref="IReplayGuard"/> backed by Redis <c>SET key val EX ttl NX</c> —
/// one round trip, atomic across every API instance, and self-expiring so the
/// keyspace stays bounded without a sweeper.
/// </summary>
/// <remarks>
/// Deliberately does not catch transport exceptions. See the interface remarks:
/// the fail-open/fail-closed choice lives at the call site.
/// </remarks>
public sealed class RedisReplayGuard : IReplayGuard
{
    private readonly IConnectionMultiplexer _redis;

    public RedisReplayGuard(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<bool> TryClaimAsync(string key, TimeSpan ttl, CancellationToken ct = default)
        => await _redis.GetDatabase().StringSetAsync(key, 1, ttl, When.NotExists);
}
