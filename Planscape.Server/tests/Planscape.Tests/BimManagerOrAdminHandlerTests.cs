using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Authorization;
using Planscape.Infrastructure.Data;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 152 — exercises the BimManagerOrAdmin authorisation handler.
/// Three behaviours we depend on:
///   1. Admin / Owner role short-circuits (no DB touch)
///   2. ProjectMember row with Iso19650Role == "K" grants
///   3. Unrelated roles deny
/// In-memory DbContext stand-in keeps tests pure-Compute.
/// </summary>
public class BimManagerOrAdminHandlerTests
{
    [Fact]
    public async Task AdminRole_GrantsWithoutDbHit()
    {
        var (handler, ctx) = await ArrangeAsync(seedBimManager: false, role: "Admin");
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task OwnerRole_GrantsWithoutDbHit()
    {
        var (handler, ctx) = await ArrangeAsync(seedBimManager: false, role: "Owner");
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task BimManagerProjectMember_Grants()
    {
        var (handler, ctx) = await ArrangeAsync(seedBimManager: true, role: "Contributor");
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task UnrelatedRole_Denies()
    {
        var (handler, ctx) = await ArrangeAsync(seedBimManager: false, role: "Contributor");
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task MissingUserId_Denies()
    {
        var (handler, ctx) = await ArrangeAsync(seedBimManager: true, role: "Contributor", omitUserId: true);
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task InactiveProjectMember_Denies()
    {
        var (handler, ctx) = await ArrangeAsync(seedBimManager: true, role: "Contributor",
            seedActive: false);
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    // ── Setup helpers ────────────────────────────────────────────

    private static async Task<(BimManagerOrAdminHandler, AuthorizationHandlerContext)> ArrangeAsync(
        bool seedBimManager, string role, bool omitUserId = false, bool seedActive = true)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        // Per-test isolated in-memory DB.
        var services = new ServiceCollection();
        // Both stubs must be registered before AddDbContext so DI selects the
        // 3-arg PlanscapeDbContext ctor — see StubTenantContext below.
        services.AddSingleton<IHttpContextAccessor>(new StubHttpContextAccessor());
        services.AddSingleton<ITenantContext>(new StubTenantContext(tenantId));
        // The name must be hoisted out of the lambda: AddDbContext invokes the
        // options action once per context instance, so generating it inline would
        // hand the handler's own scope a brand-new empty store.
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<PlanscapeDbContext>(o => o.UseInMemoryDatabase(dbName));
        // The handler resolves both of these from its own scope. Null store =
        // "no revocation recorded"; the resolver is the real one so tenant
        // overrides are parsed exactly as in production.
        services.AddSingleton<IPermissionRevocationStore, NullPermissionRevocationStore>();
        services.AddScoped<ITenantBimManagerRoleResolver>(s =>
            new DbTenantBimManagerRoleResolver(s.GetRequiredService<PlanscapeDbContext>()));
        var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "T", Slug = "t", ContactEmail = "t@x" });
            db.Projects.Add(new Project { Id = projectId, TenantId = tenantId, Name = "P", Code = "P" });
            db.Users.Add(new AppUser { Id = userId, TenantId = tenantId, Email = "u@x", DisplayName = "U" });
            if (seedBimManager)
            {
                db.ProjectMembers.Add(new ProjectMember
                {
                    TenantId = tenantId,
                    UserId = userId,
                    ProjectId = projectId,
                    Iso19650Role = "K",
                    IsActive = seedActive,
                });
            }
            await db.SaveChangesAsync();
        }

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var handler = new BimManagerOrAdminHandler(scopeFactory);

        var claims = new List<Claim>
        {
            new("tenant_id", tenantId.ToString()),
            new(ClaimTypes.Role, role),
        };
        if (!omitUserId) claims.Add(new("user_id", userId.ToString()));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));

        var requirement = new BimManagerOrAdminRequirement();
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);
        return (handler, ctx);
    }

    /// <summary>
    /// PlanscapeDbContext applies a global filter of
    /// <c>BypassTenantFilter || e.TenantId == CurrentTenantId</c> to every
    /// ITenantScoped entity, and CurrentTenantId falls back to Guid.Empty
    /// when no ITenantContext is wired. Without this stub the fixture's own
    /// rows are filtered out and both DB grant paths are dead.
    /// </summary>
    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
        public string TenantSlug => "t";
        public LicenseTier Tier => LicenseTier.Starter;
        public bool MimEnabled => false;
    }

    private sealed class StubHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
    }
}
