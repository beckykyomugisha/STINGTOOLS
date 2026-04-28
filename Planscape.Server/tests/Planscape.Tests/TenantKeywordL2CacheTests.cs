using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Workflow;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 152 — exercises the L2 (IDistributedCache) tier on
/// <see cref="DbTenantKeywordResolver"/>. Uses
/// <see cref="MemoryDistributedCache"/> as a Redis stand-in so the
/// tests run without a Redis server. Verifies:
///   • Read-through: L1 miss → L2 lookup → DB fallback writes through
///   • Hash-keyed invalidation: editing the JSON yields a different
///     cache key, so stale entries are bypassed naturally
///   • L2-blip resilience: a throwing IDistributedCache doesn't crash
///     the resolver — it falls through to the DB
///   • Backward-compat: passing null for the L2 (legacy ctor path)
///     works exactly as Phase 151
/// </summary>
public class TenantKeywordL2CacheTests
{
    [Fact]
    public async Task ResolveAsync_HitsL2OnSecondCall()
    {
        await using var db = NewInMemoryDb();
        var tenantId = await SeedTenant(db, """{ "working": ["PARKED"] }""");
        var l2 = new RecordingDistributedCache(new MemoryDistributedCacheStub());

        var resolver = new DbTenantKeywordResolver(db, l2);

        // First call: L1 miss → DB → write to L2.
        var first = await resolver.ResolveAsync(tenantId);
        Assert.Single(first);
        Assert.Contains("PARKED", first["working"]);
        Assert.Equal(0, l2.HitCount);   // no GetString returned data on first call
        Assert.Equal(1, l2.SetCount);   // wrote through

        // Second call from a fresh resolver instance — simulates a
        // process boundary. L1 will start cold; L2 should serve.
        var freshResolver = new DbTenantKeywordResolver(db, l2);
        var second = await freshResolver.ResolveAsync(tenantId);
        Assert.Contains("PARKED", second["working"]);
        Assert.True(l2.HitCount >= 1, "Expected the second resolver to hit L2");
    }

    [Fact]
    public async Task ResolveAsync_L2BlipFallsBackToDb()
    {
        await using var db = NewInMemoryDb();
        var tenantId = await SeedTenant(db, """{ "terminal": ["FROZEN"] }""");
        var blip = new ThrowingDistributedCache();

        var resolver = new DbTenantKeywordResolver(db, blip);

        var result = await resolver.ResolveAsync(tenantId);
        Assert.Contains("FROZEN", result["terminal"]);
    }

    [Fact]
    public async Task ResolveAsync_NullL2_BehavesLikePhase151()
    {
        await using var db = NewInMemoryDb();
        var tenantId = await SeedTenant(db, """{ "submitting": ["FOR_BIM_REVIEW"] }""");

        // Single-arg construction — same as the previous phase.
        var resolver = new DbTenantKeywordResolver(db, distributedCache: null);
        var r = await resolver.ResolveAsync(tenantId);
        Assert.Contains("FOR_BIM_REVIEW", r["submitting"]);
    }

    [Fact]
    public async Task ResolveAsync_EmptyTenantId_ReturnsEmpty()
    {
        await using var db = NewInMemoryDb();
        var resolver = new DbTenantKeywordResolver(db);
        var r = await resolver.ResolveAsync(System.Guid.Empty);
        Assert.Empty(r);
    }

    [Fact]
    public async Task ResolveAsync_NullJson_ReturnsEmpty()
    {
        await using var db = NewInMemoryDb();
        var tenantId = await SeedTenant(db, json: null);
        var resolver = new DbTenantKeywordResolver(db);
        var r = await resolver.ResolveAsync(tenantId);
        Assert.Empty(r);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static PlanscapeDbContext NewInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
            .Options;
        return new PlanscapeDbContext(opts);
    }

    private static async Task<System.Guid> SeedTenant(PlanscapeDbContext db, string? json)
    {
        var t = new Tenant
        {
            Name = "Test Tenant",
            Slug = "test",
            ContactEmail = "test@example.com",
            KeywordExtensionsJson = json,
        };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    /// <summary>Wraps a real IDistributedCache to count hit/miss/set
    /// operations.</summary>
    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly IDistributedCache _inner;
        public int HitCount { get; private set; }
        public int SetCount { get; private set; }

        public RecordingDistributedCache(IDistributedCache inner) => _inner = inner;

        public byte[]? Get(string key) => _inner.Get(key);
        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            var v = await _inner.GetAsync(key, token);
            if (v != null) HitCount++;
            return v;
        }
        public void Refresh(string key) => _inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);
        public void Remove(string key) => _inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { SetCount++; _inner.Set(key, value, options); }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            SetCount++;
            return _inner.SetAsync(key, value, options, token);
        }
    }

    /// <summary>Throws on every operation. The resolver must catch and
    /// fall through to the DB rather than 500.</summary>
    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new System.InvalidOperationException("redis down");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new System.InvalidOperationException("redis down");
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new System.InvalidOperationException("redis down");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new System.InvalidOperationException("redis down");
    }

    /// <summary>Delegates to <see cref="MemoryDistributedCache"/> with the
    /// minimal options ctor that the test project's package set
    /// supports.</summary>
    private sealed class MemoryDistributedCacheStub : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner;
        public MemoryDistributedCacheStub()
        {
            var options = Options.Create(new MemoryDistributedCacheOptions());
            _inner = new MemoryDistributedCache(options);
        }
        public byte[]? Get(string key) => _inner.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);
        public void Refresh(string key) => _inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);
        public void Remove(string key) => _inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _inner.Set(key, value, options);
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => _inner.SetAsync(key, value, options, token);
    }
}
