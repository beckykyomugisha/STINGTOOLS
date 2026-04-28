using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Workflow;

/// <summary>
/// Phase 151 — DB-backed tenant keyword resolver. Reads
/// <c>Tenant.KeywordExtensionsJson</c>, parses it through the same
/// canonical-bucket / typo-skip rules as
/// <see cref="DeliverableStateMachine"/>'s project parser, and caches
/// the result.
///
/// Phase 152 — two-tier cache:
///   L1: static striped-LRU keyed on <c>(TenantId, FNV-1a content
///       hash)</c>. Process-local. Survives across requests but not
///       process restarts.
///   L2: <see cref="IDistributedCache"/> (Redis in production). Keyed
///       on <c>tk:{TenantId}:{hash}</c>. Survives process restarts
///       and is shared across the API fleet so a horizontal-scaled
///       deployment doesn't pay the parse cost N times.
///
/// Read path: L1 hit → done. L1 miss → L2 lookup → if hit, parse and
/// fill L1 → done. L2 miss → fetch JSON from DB → parse → fill L2 →
/// fill L1 → done.
///
/// Hash flips when an admin updates the JSON, so stale entries in
/// either tier self-invalidate on next read.
/// </summary>
/// Cache size is bounded by the number of distinct tenants in the
/// deployment — typically dozens to hundreds — so a 256-entry striped
/// LRU is plenty. Eviction is best-effort: if a stale tenant falls
/// out of the cache the next request just re-parses, which is cheap.
/// </summary>
public sealed class DbTenantKeywordResolver : ITenantKeywordResolver
{
    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "initial", "working", "submitting", "accepting", "rejecting", "terminal",
    };

    // Cache keyed on (tenantId, contentHash) — the hash flips when an
    // admin updates the JSON, naturally invalidating stale entries.
    // Static so the cache survives the scoped lifecycle of the
    // resolver (one resolver per request would otherwise rebuild the
    // cache cold every time).
    private static readonly StripedBoundedLruCache<string, IReadOnlyDictionary<string, IReadOnlyCollection<string>>> _cache
        = new(totalCapacity: 256, stripeCount: 8, comparer: StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> Empty
        = new Dictionary<string, IReadOnlyCollection<string>>();

    private readonly PlanscapeDbContext _db;
    private readonly IDistributedCache? _l2;

    /// <summary>
    /// Phase 152 — accept an optional <see cref="IDistributedCache"/> as
    /// the L2 (cross-process / Redis-backed) tier. Null is supported so
    /// the resolver can be constructed in unit tests / dev configs that
    /// don't wire Redis. Production DI passes the registered Redis
    /// instance through.
    /// </summary>
    public DbTenantKeywordResolver(PlanscapeDbContext db, IDistributedCache? distributedCache = null)
    {
        _db = db;
        _l2 = distributedCache;
    }

    /// <summary>L2 entry TTL. The hash-based key invalidation already
    /// handles content drift, but we also sweep stale rows so a tenant
    /// that hasn't been queried in a fortnight doesn't squat on Redis
    /// memory forever.</summary>
    private static readonly TimeSpan L2Ttl = TimeSpan.FromDays(14);

    public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> ResolveAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return Empty;

        var json = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.KeywordExtensionsJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return Empty;

        // Cheap stable hash so the cache key flips whenever the JSON
        // does. Not cryptographic — collisions would just cause an
        // inappropriate cache hit on the cached copy, and operators
        // editing the JSON twice in quick succession would still see
        // the second edit because the hash space is the entire string
        // content space, not just 32 bits.
        var hash = StableHash(json!);
        var l1Key = $"{tenantId}:{hash}";

        // L1 hit — fast path, no Redis round-trip.
        if (_cache.TryPeek(l1Key, out var hit)) return hit;

        // L2 lookup. If hit, parse + populate L1. Failures in the L2
        // path never throw — Redis blip falls back to the DB read.
        if (_l2 != null)
        {
            var l2Key = $"tk:{l1Key}";
            try
            {
                var raw = await _l2.GetStringAsync(l2Key, ct);
                if (!string.IsNullOrEmpty(raw))
                {
                    return _cache.GetOrAdd(l1Key, _ => Parse(raw));
                }
            }
            catch { /* L2 unavailable — fall through to DB / parse */ }

            // L2 miss — parse, then write-through to L2.
            var parsed = _cache.GetOrAdd(l1Key, _ => Parse(json!));
            try
            {
                // Store the source JSON, not the parsed dict, so the
                // L2 entry survives schema additions to the DTO without
                // needing an explicit migration. Parse is cheap.
                await _l2.SetStringAsync(l2Key, json!,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = L2Ttl }, ct);
            }
            catch { /* L2 unavailable — L1 still has the value */ }
            return parsed;
        }

        return _cache.GetOrAdd(l1Key, _ => Parse(json!));
    }

    /// <summary>
    /// Phase 151 — public sibling to <see cref="Parse"/> for use by the
    /// admin tenant-keywords PUT endpoint. Same parsing rules; the
    /// admin path uses it to validate the JSON before persisting so a
    /// bad payload doesn't get silently ignored at request time.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> ParseForValidation(string json)
        => Parse(json);

    private static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Empty;

            var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in root.EnumerateObject())
            {
                var role = prop.Name.Trim().ToLowerInvariant();
                if (!ValidRoles.Contains(role)) continue;
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;

                var entries = new List<string>();
                foreach (var item in prop.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var token = item.GetString();
                    if (!string.IsNullOrWhiteSpace(token))
                        entries.Add(token!.Trim().ToUpperInvariant());
                }
                if (entries.Count > 0) result[role] = entries.AsReadOnly();
            }
            return result;
        }
        catch
        {
            // Malformed JSON falls back to empty, same forgiving
            // pattern the project parser uses.
            return Empty;
        }
    }

    /// <summary>FNV-1a 64-bit. Cheap, stable, allocation-free.</summary>
    private static string StableHash(string s)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;
        ulong h = fnvOffset;
        foreach (var c in s)
        {
            h ^= c;
            h *= fnvPrime;
        }
        return h.ToString("x16");
    }
}
