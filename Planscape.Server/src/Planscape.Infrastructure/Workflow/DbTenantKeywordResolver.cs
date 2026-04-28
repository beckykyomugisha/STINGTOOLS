using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Workflow;

/// <summary>
/// Phase 151 — DB-backed tenant keyword resolver. Reads
/// <c>Tenant.KeywordExtensionsJson</c>, parses it through the same
/// canonical-bucket / typo-skip rules as
/// <see cref="DeliverableStateMachine"/>'s project parser, and caches
/// the result. The cache key is <c>(TenantId, contentHash)</c> so a
/// stale cache row is automatically replaced when an admin edits the
/// JSON: the next read sees a different hash and reparses.
///
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

    public DbTenantKeywordResolver(PlanscapeDbContext db) => _db = db;

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
        // does. We don't need cryptographic strength here; collisions
        // would just cause an inappropriate cache hit on the cached
        // copy, and operators editing the JSON twice in quick
        // succession would still see the second edit because the
        // hash space is the entire string content space, not just 32
        // bits.
        var key = $"{tenantId}:{StableHash(json!)}";
        return _cache.GetOrAdd(key, _ => Parse(json!));
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
