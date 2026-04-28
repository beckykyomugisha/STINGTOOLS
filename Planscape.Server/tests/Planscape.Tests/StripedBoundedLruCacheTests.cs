using System.Collections.Generic;
using System.Threading.Tasks;
using Planscape.Infrastructure.Workflow;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 151 — exercises the striped LRU variant. Tests the invariants
/// that distinguish it from the un-striped <see cref="BoundedLruCache{TKey,TValue}"/>:
/// stripe count is rounded up to a power of two, total capacity is
/// honoured (modulo the per-stripe rounding), and concurrent get-or-add
/// from many threads doesn't crash.
/// </summary>
public class StripedBoundedLruCacheTests
{
    [Fact]
    public void Capacity_ZeroOrNegative_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            _ = new StripedBoundedLruCacheTestProxy<string, string>(0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            _ = new StripedBoundedLruCacheTestProxy<string, string>(-1));
    }

    [Fact]
    public void StripeCount_RoundsUpToPowerOfTwo()
    {
        Assert.Equal(1, new StripedBoundedLruCacheTestProxy<string, string>(8, stripes: 1).StripeCount);
        Assert.Equal(2, new StripedBoundedLruCacheTestProxy<string, string>(8, stripes: 2).StripeCount);
        Assert.Equal(4, new StripedBoundedLruCacheTestProxy<string, string>(8, stripes: 3).StripeCount);
        Assert.Equal(8, new StripedBoundedLruCacheTestProxy<string, string>(8, stripes: 5).StripeCount);
        Assert.Equal(16, new StripedBoundedLruCacheTestProxy<string, string>(64, stripes: 16).StripeCount);
    }

    [Fact]
    public void GetOrAdd_RetrievesFromStripeOnRepeat()
    {
        var cache = new StripedBoundedLruCacheTestProxy<string, int>(64, stripes: 8);
        int factoryCalls = 0;
        cache.GetOrAdd("k1", _ => { factoryCalls++; return 1; });
        cache.GetOrAdd("k1", _ => { factoryCalls++; return 2; }); // would-be 2 must not run
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void GetOrAdd_HonoursCustomComparer()
    {
        var cache = new StripedBoundedLruCacheTestProxy<string, string>(
            64, stripes: 8, cmp: System.StringComparer.OrdinalIgnoreCase);
        cache.GetOrAdd("KEY", _ => "v");
        Assert.True(cache.TryPeek("key", out var v));
        Assert.Equal("v", v);
    }

    [Fact]
    public void GetOrAdd_TotalCapBoundsAggregateCount()
    {
        // 64-total / 8-stripes = 8 per stripe. Insert 1000 keys —
        // each stripe converges on 8 entries, total ≤ 64.
        var cache = new StripedBoundedLruCacheTestProxy<int, int>(64, stripes: 8);
        for (int i = 0; i < 1000; i++)
            cache.GetOrAdd(i, x => x);
        Assert.True(cache.Count <= 64, $"Expected ≤ 64 entries, got {cache.Count}");
    }

    [Fact]
    public async Task GetOrAdd_ConcurrentReaders_DoNotCrash()
    {
        // The whole point of striping. 16 threads × 1000 reads each on
        // a 4-stripe cache. We don't assert exact counts (eviction
        // races are expected), only that nothing throws and the cap
        // holds.
        var cache = new StripedBoundedLruCacheTestProxy<int, int>(64, stripes: 4);
        var tasks = new List<Task>();
        for (int t = 0; t < 16; t++)
        {
            int seed = t;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                    cache.GetOrAdd((seed * 1000) + i, x => x);
            }));
        }
        await Task.WhenAll(tasks);
        Assert.True(cache.Count <= 64);
    }

    /// <summary>Test-only thin wrapper over the internal cache.</summary>
    private sealed class StripedBoundedLruCacheTestProxy<TKey, TValue> where TKey : notnull
    {
        private readonly StripedBoundedLruCache<TKey, TValue> _inner;
        public int Count => _inner.Count;
        public int StripeCount => _inner.StripeCount;
        public StripedBoundedLruCacheTestProxy(int totalCap, int stripes = 16, IEqualityComparer<TKey>? cmp = null)
            => _inner = new StripedBoundedLruCache<TKey, TValue>(totalCap, stripes, cmp);
        public TValue GetOrAdd(TKey k, System.Func<TKey, TValue> f) => _inner.GetOrAdd(k, f);
        public bool TryPeek(TKey k, out TValue v) => _inner.TryPeek(k, out v);
    }
}
