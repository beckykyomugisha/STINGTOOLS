using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Planscape.Infrastructure.Authorization;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 156 — exercises the JWT permission-revocation store. Two
/// surfaces tested: <see cref="RedisPermissionRevocationStore"/>
/// (with a MemoryDistributedCache stand-in) and
/// <see cref="NullPermissionRevocationStore"/>.
/// </summary>
public class PermissionRevocationStoreTests
{
    [Fact]
    public async Task Get_BeforeRevoke_ReturnsNull()
    {
        var store = MakeStore();
        Assert.Null(await store.GetMinIatAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Revoke_ThenGet_ReturnsRecentEpoch()
    {
        var store = MakeStore();
        var userId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await store.RevokeAllPriorTokensAsync(userId);
        var floor = await store.GetMinIatAsync(userId);
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.NotNull(floor);
        Assert.InRange(floor!.Value, before, after);
    }

    [Fact]
    public async Task Revoke_DifferentUsers_AreIndependent()
    {
        var store = MakeStore();
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        await store.RevokeAllPriorTokensAsync(u1);
        Assert.NotNull(await store.GetMinIatAsync(u1));
        Assert.Null(await store.GetMinIatAsync(u2));
    }

    [Fact]
    public async Task Revoke_Idempotent_LatestWins()
    {
        var store = MakeStore();
        var userId = Guid.NewGuid();
        await store.RevokeAllPriorTokensAsync(userId);
        var first = await store.GetMinIatAsync(userId);
        await Task.Delay(1100); // sleep just over a second so epoch advances
        await store.RevokeAllPriorTokensAsync(userId);
        var second = await store.GetMinIatAsync(userId);
        Assert.True(second >= first);
    }

    [Fact]
    public async Task Revoke_EmptyUserId_NoOps()
    {
        var store = MakeStore();
        await store.RevokeAllPriorTokensAsync(Guid.Empty); // no throw
        Assert.Null(await store.GetMinIatAsync(Guid.Empty));
    }

    [Fact]
    public async Task Get_RedisDown_ReturnsNull()
    {
        // ThrowingCache simulates a Redis blip; the store must
        // gracefully return null so auth doesn't 500.
        var store = new RedisPermissionRevocationStore(new ThrowingDistributedCache(), config: null);
        var floor = await store.GetMinIatAsync(Guid.NewGuid());
        Assert.Null(floor);
    }

    [Fact]
    public async Task Revoke_RedisDown_DoesNotThrow()
    {
        var store = new RedisPermissionRevocationStore(new ThrowingDistributedCache(), config: null);
        // Must not throw — admin actions can't be blocked by a Redis
        // outage.
        await store.RevokeAllPriorTokensAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task Null_StoreNeverReturnsFloor()
    {
        var store = new NullPermissionRevocationStore();
        var u = Guid.NewGuid();
        await store.RevokeAllPriorTokensAsync(u); // no-op
        Assert.Null(await store.GetMinIatAsync(u));
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static IPermissionRevocationStore MakeStore()
    {
        var inner = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return new RedisPermissionRevocationStore(inner, config: null);
    }

    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("redis down");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("redis down");
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("redis down");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw new InvalidOperationException("redis down");
    }
}
