using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

    public DbTenantBimManagerRoleResolver(PlanscapeDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>?> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return null;

        var json = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.BimManagerIso19650RolesJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return null;

        var key = $"{tenantId}:{StableHash(json!)}";
        return _cache.GetOrAdd(key, _ => Parse(json!));
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
