using Microsoft.Extensions.Caching.Distributed;

namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// Phase 156 — Redis-backed implementation of
/// <see cref="IPermissionRevocationStore"/>. Stores the minimum
/// acceptable iat per user in Redis with a TTL matching the longest
/// plausible JWT lifetime (default 30 days, configurable via
/// <c>Authorization:RevocationFloorTtlDays</c>). Once every surviving
/// token predates the floor — i.e. once the floor itself is older
/// than the JWT TTL — the entry is harmless and Redis evicts it.
///
/// Falls back to a no-op when <see cref="IDistributedCache"/> is
/// unavailable (dev configs without Redis). The auth handler treats
/// "no floor" as "accept" so the worst-case behaviour with Redis off
/// is exactly the pre-Phase-156 lag.
/// </summary>
public sealed class RedisPermissionRevocationStore : IPermissionRevocationStore
{
    private const string KeyPrefix = "auth:revocation:";

    private readonly IDistributedCache? _cache;
    private readonly TimeSpan _ttl;

    public RedisPermissionRevocationStore(
        IDistributedCache? cache,
        Microsoft.Extensions.Configuration.IConfiguration? config = null)
    {
        _cache = cache;
        _ttl = ReadTtl(config);
    }

    private static TimeSpan ReadTtl(Microsoft.Extensions.Configuration.IConfiguration? config)
    {
        const int defaultDays = 30;
        if (config == null) return TimeSpan.FromDays(defaultDays);
        var raw = config["Authorization:RevocationFloorTtlDays"];
        if (string.IsNullOrWhiteSpace(raw)) return TimeSpan.FromDays(defaultDays);
        if (!int.TryParse(raw, out var d) || d <= 0) return TimeSpan.FromDays(defaultDays);
        if (d > 365) d = 365;
        return TimeSpan.FromDays(d);
    }

    public async Task<long?> GetMinIatAsync(Guid userId, CancellationToken ct = default)
    {
        if (_cache == null || userId == Guid.Empty) return null;
        try
        {
            var raw = await _cache.GetStringAsync(KeyPrefix + userId, ct);
            if (string.IsNullOrEmpty(raw)) return null;
            return long.TryParse(raw, out var iat) ? iat : null;
        }
        catch
        {
            // Redis blip — never block auth on the lookup. Worst case
            // is the pre-Phase-156 token-revocation lag.
            return null;
        }
    }

    public async Task RevokeAllPriorTokensAsync(Guid userId, CancellationToken ct = default)
    {
        if (_cache == null || userId == Guid.Empty) return;
        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            await _cache.SetStringAsync(
                KeyPrefix + userId,
                nowEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl },
                ct);
        }
        catch
        {
            // If Redis is down at revocation time, the floor isn't
            // recorded — caller should retry. We don't throw because
            // the calling action (admin role change, etc.) shouldn't
            // fail just because Redis is briefly unavailable.
        }
    }
}
