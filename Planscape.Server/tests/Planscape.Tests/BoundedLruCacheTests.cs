using System.Collections.Generic;
using Planscape.Infrastructure.Workflow;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 150 — exercises the bounded LRU cache that backs runtime role
/// memoisation in <see cref="DeliverableStateMachine"/>. Tests the
/// invariants we actually rely on: capacity is honoured, repeated
/// access promotes, and the factory is invoked exactly once per
/// resident key.
/// </summary>
public class BoundedLruCacheTests
{
    [Fact]
    public void Capacity_ZeroOrNegative_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => _ = new BoundedLruCacheTestProxy<string, string>(0));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => _ = new BoundedLruCacheTestProxy<string, string>(-1));
    }

    [Fact]
    public void GetOrAdd_FirstCall_InvokesFactory()
    {
        var cache = new BoundedLruCacheTestProxy<string, string>(4);
        int calls = 0;
        var v = cache.GetOrAdd("k1", _ => { calls++; return "v1"; });
        Assert.Equal("v1", v);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GetOrAdd_RepeatedCall_DoesNotInvokeFactoryAgain()
    {
        var cache = new BoundedLruCacheTestProxy<string, string>(4);
        int calls = 0;
        cache.GetOrAdd("k1", _ => { calls++; return "v1"; });
        cache.GetOrAdd("k1", _ => { calls++; return "v1"; });
        cache.GetOrAdd("k1", _ => { calls++; return "v1"; });
        Assert.Equal(1, calls);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void GetOrAdd_ExceedingCapacity_EvictsLru()
    {
        var cache = new BoundedLruCacheTestProxy<string, int>(3);
        cache.GetOrAdd("a", _ => 1);
        cache.GetOrAdd("b", _ => 2);
        cache.GetOrAdd("c", _ => 3);
        Assert.Equal(3, cache.Count);

        // d evicts the oldest — a — because b/c were inserted later
        // and not since touched.
        cache.GetOrAdd("d", _ => 4);
        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryPeek("a", out _));
        Assert.True(cache.TryPeek("b", out var b)); Assert.Equal(2, b);
        Assert.True(cache.TryPeek("c", out var c)); Assert.Equal(3, c);
        Assert.True(cache.TryPeek("d", out var d)); Assert.Equal(4, d);
    }

    [Fact]
    public void GetOrAdd_TouchPromotesEntry_PreventsEviction()
    {
        var cache = new BoundedLruCacheTestProxy<string, int>(3);
        cache.GetOrAdd("a", _ => 1);
        cache.GetOrAdd("b", _ => 2);
        cache.GetOrAdd("c", _ => 3);

        // Touch "a" — should now be MRU. Adding "d" must drop "b" not "a".
        cache.GetOrAdd("a", _ => -1);   // factory shouldn't run
        cache.GetOrAdd("d", _ => 4);

        Assert.True(cache.TryPeek("a", out _));
        Assert.False(cache.TryPeek("b", out _));
        Assert.True(cache.TryPeek("c", out _));
        Assert.True(cache.TryPeek("d", out _));
    }

    [Fact]
    public void GetOrAdd_HonoursCustomEqualityComparer()
    {
        var cache = new BoundedLruCacheTestProxy<string, string>(4, System.StringComparer.OrdinalIgnoreCase);
        cache.GetOrAdd("KEY", _ => "v");
        Assert.True(cache.TryPeek("key", out var v));
        Assert.Equal("v", v);
    }

    [Fact]
    public void GetOrAdd_ManyInsertions_CapBoundsCount()
    {
        var cache = new BoundedLruCacheTestProxy<int, int>(8);
        for (int i = 0; i < 1000; i++)
            cache.GetOrAdd(i, x => x);
        Assert.Equal(8, cache.Count);
    }

    /// <summary>
    /// Test-only thin wrapper that exposes <c>internal</c>
    /// <see cref="BoundedLruCache{TKey,TValue}"/> through the
    /// <c>InternalsVisibleTo</c> declaration the production csproj
    /// already grants the test assembly. Lets us exercise it without
    /// promoting the cache to public surface.
    /// </summary>
    private sealed class BoundedLruCacheTestProxy<TKey, TValue> where TKey : notnull
    {
        private readonly BoundedLruCache<TKey, TValue> _inner;
        public int Count => _inner.Count;
        public BoundedLruCacheTestProxy(int capacity, IEqualityComparer<TKey>? cmp = null)
            => _inner = new BoundedLruCache<TKey, TValue>(capacity, cmp);
        public TValue GetOrAdd(TKey key, System.Func<TKey, TValue> factory) => _inner.GetOrAdd(key, factory);
        public bool TryPeek(TKey key, out TValue value) => _inner.TryPeek(key, out value);
    }
}
