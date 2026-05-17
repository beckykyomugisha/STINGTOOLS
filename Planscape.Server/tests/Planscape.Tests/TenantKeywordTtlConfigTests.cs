using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Workflow;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 153 — exercises configurable + sliding TTLs on the L2
/// distributed cache used by <see cref="DbTenantKeywordResolver"/>.
/// We assert the options actually flow into <see cref="DistributedCacheEntryOptions"/>
/// via a recording stand-in.
/// </summary>
public class TenantKeywordTtlConfigTests
{
    [Fact]
    public async Task DefaultTtls_AppliedWhenNoConfig()
    {
        await using var db = NewDb();
        var tenantId = await SeedTenant(db, """{ "working": ["X"] }""");
        var recorder = new RecordingCache();
        var resolver = new DbTenantKeywordResolver(db, recorder, config: null);

        await resolver.ResolveAsync(tenantId);

        Assert.NotNull(recorder.LastOptions);
        Assert.Equal(TimeSpan.FromDays(14), recorder.LastOptions!.AbsoluteExpirationRelativeToNow);
        Assert.Equal(TimeSpan.FromDays(7), recorder.LastOptions!.SlidingExpiration);
    }

    [Fact]
    public async Task ConfiguredTtls_OverrideDefaults()
    {
        await using var db = NewDb();
        var tenantId = await SeedTenant(db, """{ "terminal": ["FROZEN"] }""");
        var recorder = new RecordingCache();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeliverableStateMachine:Cache:AbsoluteTtlDays"] = "30",
            ["DeliverableStateMachine:Cache:SlidingTtlDays"]  = "3",
        }).Build();
        var resolver = new DbTenantKeywordResolver(db, recorder, config);

        await resolver.ResolveAsync(tenantId);

        Assert.Equal(TimeSpan.FromDays(30), recorder.LastOptions!.AbsoluteExpirationRelativeToNow);
        Assert.Equal(TimeSpan.FromDays(3), recorder.LastOptions!.SlidingExpiration);
    }

    [Theory]
    [InlineData("0")]      // non-positive → fall back
    [InlineData("-5")]     // negative → fall back
    [InlineData("not-a-number")]
    [InlineData("")]
    public async Task BadTtlValues_FallBackToDefault(string raw)
    {
        await using var db = NewDb();
        var tenantId = await SeedTenant(db, """{ "working": ["X"] }""");
        var recorder = new RecordingCache();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeliverableStateMachine:Cache:AbsoluteTtlDays"] = raw,
        }).Build();
        var resolver = new DbTenantKeywordResolver(db, recorder, config);

        await resolver.ResolveAsync(tenantId);

        Assert.Equal(TimeSpan.FromDays(14), recorder.LastOptions!.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task ExcessivelyLargeTtl_CappedAtOneYear()
    {
        await using var db = NewDb();
        var tenantId = await SeedTenant(db, """{ "working": ["X"] }""");
        var recorder = new RecordingCache();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeliverableStateMachine:Cache:AbsoluteTtlDays"] = "9999",
        }).Build();
        var resolver = new DbTenantKeywordResolver(db, recorder, config);

        await resolver.ResolveAsync(tenantId);

        Assert.Equal(TimeSpan.FromDays(365), recorder.LastOptions!.AbsoluteExpirationRelativeToNow);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static PlanscapeDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new PlanscapeDbContext(opts);
    }
    private static async Task<Guid> SeedTenant(PlanscapeDbContext db, string json)
    {
        var t = new Tenant { Name = "T", Slug = "t", ContactEmail = "t@x", KeywordExtensionsJson = json };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    /// <summary>Cache stand-in that records the options passed to
    /// SetStringAsync. Get returns null so the resolver always takes
    /// the write-through path.</summary>
    private sealed class RecordingCache : IDistributedCache
    {
        public DistributedCacheEntryOptions? LastOptions { get; private set; }

        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { LastOptions = options; }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            LastOptions = options;
            return Task.CompletedTask;
        }
    }
}
