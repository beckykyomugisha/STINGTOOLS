using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Workflow;

namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// Phase 155 — DB-backed resolver with a static striped-LRU cache.
/// Static so the cache survives across the scoped-handler lifecycle
/// (one resolver per request) — otherwise we'd rebuild the cache cold
/// on every authorisation. Capacity 256 / 8 stripes mirrors the
/// keyword resolver, plenty for any plausible tenant count without
/// growing without bound.
///
/// Cache value type is <c>IReadOnlyList&lt;string&gt;?</c> — null is
/// a valid cached result meaning "this tenant's JSON is malformed /
/// empty, use deployment fallback". Avoiding re-parsing in the
/// fallback case is the whole point of caching.
/// </summary>
public sealed class DbTenantBimManagerRoleResolver : ITenantBimManagerRoleResolver
{
    // Cache key is "(tenantId):(hash)" — hash flips on admin update,
    // naturally invalidating the stale entry without explicit eviction.
    private static readonly StripedBoundedLruCache<string, IReadOnlyList<string>?> _cache
        = new(totalCapacity: 256, stripeCount: 8, comparer: StringComparer.Ordinal);

    private readonly PlanscapeDbContext _db;
    private readonly IDistributedCache? _l2;
    private readonly TimeSpan _absoluteTtl;
    private readonly TimeSpan _slidingTtl;

    /// <summary>
    /// Phase 156 — accept an optional <see cref="IDistributedCache"/>
    /// as the L2 (Redis-backed) tier, mirroring the keyword resolver
    /// from Phase 152. Two-tier cache:
    ///   L1 — static striped LRU keyed on <c>(TenantId, FNV-1a hash)</c>.
    ///   L2 — IDistributedCache keyed on <c>tbmr:{TenantId}:{hash}</c>.
    ///        Survives process restarts; shared across the API fleet
    ///        so horizontal-scaled deployments don't pay the parse
    ///        cost N times.
    /// TTLs read from <c>Authorization:BimManagerRoles:{Absolute,Sliding}TtlDays</c>
    /// (defaults 14 / 7 days). L2 errors degrade gracefully to L1 +
    /// DB rather than 500-ing the auth path.
    /// </summary>
    public DbTenantBimManagerRoleResolver(
        PlanscapeDbContext db,
        IDistributedCache? distributedCache = null,
        IConfiguration? config = null)
    {
        _db = db;
        _l2 = distributedCache;
        _absoluteTtl = ReadTtl(config, "Authorization:BimManagerRoles:AbsoluteTtlDays", defaultDays: 14);
        _slidingTtl = ReadTtl(config, "Authorization:BimManagerRoles:SlidingTtlDays",  defaultDays: 7);
    }

    private static TimeSpan ReadTtl(IConfiguration? config, string key, int defaultDays)
    {
        if (config == null) return TimeSpan.FromDays(defaultDays);
        var raw = config[key];
        if (string.IsNullOrWhiteSpace(raw)) return TimeSpan.FromDays(defaultDays);
        if (!int.TryParse(raw, out var days) || days <= 0) return TimeSpan.FromDays(defaultDays);
        if (days > 365) days = 365;
        return TimeSpan.FromDays(days);
    }

    public async Task<IReadOnlyList<string>?> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return null;

        var json = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.BimManagerIso19650RolesJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return null;

        var hash = StableHash(json!);
        var l1Key = $"{tenantId}:{hash}";

        // L1 hit — fast path. Note: L1 caches null (no usable
        // override) too, so a re-parse storm on malformed JSON is
        // bounded.
        if (_cache.TryPeek(l1Key, out var hit)) return hit;

        // L2 read-through. The cached value is the source JSON
        // (not the parsed list) so a future DTO change doesn't
        // invalidate the cluster-wide cache. Parse cost is
        // microseconds; cluster-coordination cost is what we save.
        if (_l2 != null)
        {
            var l2Key = $"tbmr:{l1Key}";
            try
            {
                var raw = await _l2.GetStringAsync(l2Key, ct);
                if (!string.IsNullOrEmpty(raw))
                {
                    return _cache.GetOrAdd(l1Key, _ => Parse(raw));
                }
            }
            catch { /* L2 blip — fall through to DB / parse */ }

            var parsed = _cache.GetOrAdd(l1Key, _ => Parse(json!));
            try
            {
                await _l2.SetStringAsync(l2Key, json!,
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _absoluteTtl,
                        SlidingExpiration = _slidingTtl,
                    }, ct);
            }
            catch { /* L2 unavailable — L1 still has the value */ }
            return parsed;
        }

        return _cache.GetOrAdd(l1Key, _ => Parse(json!));
    }

    /// <summary>Phase 155 — exposed for the admin PUT endpoint to
    /// validate the body before persisting. Mirrors the in-handler
    /// parser exactly so PUT-time and request-time semantics match.</summary>
    public static IReadOnlyList<string>? ParseForValidation(string json) => Parse(json);

    private static IReadOnlyList<string>? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var entries = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var v = el.GetString();
                if (!string.IsNullOrWhiteSpace(v))
                    entries.Add(v!.Trim().ToUpperInvariant());
            }
            entries = entries.Distinct().ToList();
            return entries.Count > 0 ? entries : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>FNV-1a 64-bit. Same helper as DbTenantKeywordResolver.</summary>
    private static string StableHash(string s)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;
        ulong h = fnvOffset;
        foreach (var c in s) { h ^= c; h *= fnvPrime; }
        return h.ToString("x16");
    }
}
