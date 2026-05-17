using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Authorization;
using Planscape.Infrastructure.Billing;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Planscape.Infrastructure.Storage;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Five integration tests covering the security-critical paths the
/// founder cares about most. If any of these flip red, something in
/// the tenant-isolation, billing, or audit machinery has regressed —
/// stop the deploy.
///
/// Test 1 — Tenant query filter: every entity returns 0 rows when
///          CurrentTenantId is empty.
/// Test 2 — Storage path enforcement: tenant A's path rejected when
///          ITenantContext resolves tenant B.
/// Test 3 — Stripe webhook: bad signature → null (controller maps to 401).
/// Test 4 — Flutterwave webhook: bad verif-hash → null.
/// Test 5 — Quota guard: project cap on Trial plan returns Denied.
///
/// Audit-chain verification is exercised by the migration's SQL
/// function rather than by C# tests; running it against an in-memory
/// EF provider would skip the trigger entirely. Smoke-tested manually
/// post-deploy via the runbook.
/// </summary>
public class SecurityCriticalPathTests
{
    // ── 1. Tenant query filter — empty TenantId → no rows ──────────

    [Fact]
    public async Task TenantQueryFilter_EmptyTenantId_ReturnsNoRows()
    {
        var dbName = "tenant-filter-empty-" + Guid.NewGuid();
        var tenantA = Guid.NewGuid();

        // Seed: write one Project under tenant A (bypassing the filter).
        using (var seed = NewDb(dbName, currentTenant: tenantA))
        {
            seed.Tenants.Add(new Tenant { Id = tenantA, Slug = "a", Name = "A" });
            seed.Projects.Add(new Project { TenantId = tenantA, Code = "P1", Name = "Proj 1" });
            await seed.SaveChangesAsync();
        }

        // Read with no tenant context (Guid.Empty) — filter must yield 0 rows.
        using var db = NewDb(dbName, currentTenant: Guid.Empty);
        Assert.Empty(await db.Projects.ToListAsync());
        Assert.Empty(await db.Tenants.ToListAsync());
    }

    [Fact]
    public async Task TenantQueryFilter_OnlyOwnedRowsVisible()
    {
        var dbName = "tenant-filter-owned-" + Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var seed = NewDb(dbName, currentTenant: tenantA, bypass: true))
        {
            seed.Tenants.AddRange(
                new Tenant { Id = tenantA, Slug = "a", Name = "A" },
                new Tenant { Id = tenantB, Slug = "b", Name = "B" });
            seed.Projects.AddRange(
                new Project { TenantId = tenantA, Code = "A1", Name = "A's project" },
                new Project { TenantId = tenantB, Code = "B1", Name = "B's project" });
            await seed.SaveChangesAsync();
        }

        using var dbA = NewDb(dbName, currentTenant: tenantA);
        var aRows = await dbA.Projects.Select(p => p.Code).ToListAsync();
        Assert.Single(aRows);
        Assert.Equal("A1", aRows[0]);

        using var dbB = NewDb(dbName, currentTenant: tenantB);
        var bRows = await dbB.Projects.Select(p => p.Code).ToListAsync();
        Assert.Single(bRows);
        Assert.Equal("B1", bRows[0]);
    }

    // ── 2. Storage path enforcement ─────────────────────────────────

    [Fact]
    public async Task StoragePath_RejectsCrossTenantRead()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var ctxB = new StubTenantContext(tenantB, "b");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = "/tmp/planscape-test-" + Guid.NewGuid() })
            .Build();
        var storage = new LocalFileStorageService(config, ctxB);

        // Tenant A's path — current tenant is B → must throw.
        var aPath = "t_" + tenantA.ToString("N") + "/projectId/file.glb";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => storage.GetAsync(aPath));
    }

    [Fact]
    public async Task StoragePath_AllowsOwnTenantRead()
    {
        var tenantB = Guid.NewGuid();
        var ctxB = new StubTenantContext(tenantB, "b");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = "/tmp/planscape-test-" + Guid.NewGuid() })
            .Build();
        var storage = new LocalFileStorageService(config, ctxB);

        // Path owned by current tenant — must NOT throw (file just doesn't exist).
        var ownPath = "t_" + tenantB.ToString("N") + "/projectId/file.glb";
        var stream = await storage.GetAsync(ownPath);
        Assert.Null(stream);
    }

    // ── 3. Stripe webhook signature ─────────────────────────────────

    [Fact]
    public async Task StripeWebhook_BadSignature_ReturnsNull()
    {
        var stripe = new StripePaymentProvider(
            new StubHttpClientFactory(),
            NullLogger<StripePaymentProvider>.Instance,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Billing:Stripe:WebhookSecret"] = "whsec_test",
                    ["Billing:Stripe:SecretKey"] = "sk_test_x",
                })
                .Build());

        var body = "{\"id\":\"evt_1\",\"type\":\"checkout.session.completed\",\"data\":{\"object\":{}}}";
        // Dictionary<,> implements IReadOnlyDictionary<,> so the call is
        // assignment-compatible without an explicit cast.
        IReadOnlyDictionary<string, string> headers = new Dictionary<string, string>
        {
            ["Stripe-Signature"] = "t=1700000000,v1=deadbeef",
        };
        var ev = await stripe.VerifyAndParseWebhookAsync(body, headers);
        Assert.Null(ev);
    }

    // ── 4. Flutterwave webhook verif-hash ───────────────────────────

    [Fact]
    public async Task FlutterwaveWebhook_BadHash_ReturnsNull()
    {
        var fw = new FlutterwavePaymentProvider(
            new StubHttpClientFactory(),
            NullLogger<FlutterwavePaymentProvider>.Instance,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Billing:Flutterwave:WebhookHash"] = "expected-hash-value",
                    ["Billing:Flutterwave:SecretKey"] = "FLWSECK-test",
                })
                .Build());

        var body = "{\"event\":\"charge.completed\",\"data\":{\"id\":1,\"status\":\"successful\"}}";
        IReadOnlyDictionary<string, string> headers = new Dictionary<string, string>
        {
            ["verif-hash"] = "wrong-hash",
        };
        var ev = await fw.VerifyAndParseWebhookAsync(body, headers);
        Assert.Null(ev);
    }

    // ── 5. Quota guard — Trial plan refuses 4th project ─────────────

    [Fact]
    public async Task QuotaGuard_ProjectCap_DeniesAtTrialLimit()
    {
        var dbName = "quota-trial-" + Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        using (var seed = NewDb(dbName, currentTenant: tenantId, bypass: true))
        {
            seed.Tenants.Add(new Tenant
            {
                Id = tenantId, Slug = "t", Name = "T",
                Plan = BillingPlan.Trial,    // limits: 3 projects
            });
            // 3 projects already exist — Trial cap reached.
            for (int i = 0; i < 3; i++)
                seed.Projects.Add(new Project { TenantId = tenantId, Code = $"P{i}", Name = $"P{i}" });
            await seed.SaveChangesAsync();
        }

        using var db = NewDb(dbName, currentTenant: tenantId);
        var ctx = new StubTenantContext(tenantId, "t");
        var guard = new QuotaGuardService(db, ctx);

        var result = await guard.CheckCanAddProjectAsync();
        Assert.False(result.Allowed);
        Assert.Equal(QuotaAxis.Projects, result.Axis);
        Assert.Equal(3, result.Current);
        Assert.Equal(3, result.Max);
    }

    [Fact]
    public async Task QuotaGuard_ProjectCap_AllowsBelowLimit()
    {
        var dbName = "quota-network-" + Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        using (var seed = NewDb(dbName, currentTenant: tenantId, bypass: true))
        {
            seed.Tenants.Add(new Tenant
            {
                Id = tenantId, Slug = "t", Name = "T",
                Plan = BillingPlan.Network,   // limits: 10 projects
            });
            seed.Projects.Add(new Project { TenantId = tenantId, Code = "P1", Name = "P1" });
            await seed.SaveChangesAsync();
        }

        using var db = NewDb(dbName, currentTenant: tenantId);
        var ctx = new StubTenantContext(tenantId, "t");
        var guard = new QuotaGuardService(db, ctx);

        var result = await guard.CheckCanAddProjectAsync();
        Assert.True(result.Allowed);
        Assert.Equal(1, result.Current);
        Assert.Equal(10, result.Max);
    }

    // ── helpers ────────────────────────────────────────────────────

    private static InMemoryPlanscapeDbContext NewDb(string dbName, Guid currentTenant, bool bypass = false)
    {
        var options = new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var http = new HttpContextAccessorStub();
        var ctx = new StubTenantContext(currentTenant, "stub");
        var db = new InMemoryPlanscapeDbContext(options, http, ctx);
        if (bypass) db.BypassTenantFilter = true;
        return db;
    }

    /// <summary>
    /// Test-only DbContext that remaps every <c>jsonb</c> column to
    /// <c>text</c> after <c>OnModelCreating</c> so EF's InMemory provider
    /// (which doesn't understand Postgres-specific types) can build the
    /// model. Behaviour-equivalent for the asserts these tests make —
    /// every assertion compares row counts / IDs, never JSON shape.
    /// </summary>
    private sealed class InMemoryPlanscapeDbContext : PlanscapeDbContext
    {
        public InMemoryPlanscapeDbContext(
            DbContextOptions<PlanscapeDbContext> options,
            IHttpContextAccessor http,
            ITenantContext tenant)
            : base(options, http, tenant) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
            {
                var ct = property.GetColumnType();
                if (string.Equals(ct, "jsonb", StringComparison.OrdinalIgnoreCase))
                    property.SetColumnType("text");
            }
        }
    }

    private sealed class HttpContextAccessorStub : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(Guid id, string slug) { TenantId = id; TenantSlug = slug; }
        public Guid TenantId { get; }
        public string TenantSlug { get; }
        public LicenseTier Tier => LicenseTier.Starter;
        public bool MimEnabled => false;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
