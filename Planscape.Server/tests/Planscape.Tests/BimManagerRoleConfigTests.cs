using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Authorization;
using Planscape.Infrastructure.Data;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 153 — exercises the configurable BIM-Manager role list +
/// AppUser fallback grant path on
/// <see cref="BimManagerOrAdminHandler"/>. Distinct from Phase 152's
/// <see cref="BimManagerOrAdminHandlerTests"/> which fixed "K" only.
/// </summary>
public class BimManagerRoleConfigTests
{
    [Fact]
    public async Task ConfiguredRoles_OverrideDefaultK()
    {
        // Tenant policy: BIM Manager grant for ISO 19650 role "M"
        // (mech) AND "K". User has "M" via a project member row.
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "M",
            configRoles: new[] { "K", "M" });
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task ConfiguredRoles_NarrowingExcludesOldGrants()
    {
        // Tenant restricts to "Q" (QS) only. User with "K" (BIM Mgr)
        // is now denied — the policy is exactly what config says.
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "K",
            configRoles: new[] { "Q" });
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task EmptyOrMalformedConfig_FallsBackToDefaultK()
    {
        // Empty list in config → default ["K"] kicks in. User with
        // "K" still gets in.
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "K",
            configRoles: System.Array.Empty<string>());
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task ConfigRoles_AreCaseInsensitive()
    {
        // Config uses lowercase "k"; user row has uppercase "K".
        // Handler uppercases both at compare time so they match.
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "K",
            configRoles: new[] { "k" });
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    // ── AppUser-level fallback ────────────────────────────────────

    [Fact]
    public async Task AppUserIso19650Role_GrantsWithoutProjectMember()
    {
        // AppUser.Iso19650Role = "K", no ProjectMember rows. Phase
        // 152 would have denied; Phase 153 grants.
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: null,           // no membership
            appUserIsoRole: "K",
            configRoles: new[] { "K" });
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task AppUserIso19650Role_FollowsConfigList()
    {
        // AppUser has "M". Config grants only "K" → denied.
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: null,
            appUserIsoRole: "M",
            configRoles: new[] { "K" });
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task AppUserNotInTenant_DoesNotGrant()
    {
        // AppUser exists with role K but in a different tenant.
        // Tenant claim mismatch should deny.
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: null,
            appUserIsoRole: "K",
            configRoles: new[] { "K" },
            crossTenant: true);
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    // ── Setup helpers ────────────────────────────────────────────

    private static async Task<(BimManagerOrAdminHandler, AuthorizationHandlerContext)> ArrangeAsync(
        string? projectMemberRole,
        string[] configRoles,
        string? appUserIsoRole = null,
        bool crossTenant = false)
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddDbContext<PlanscapeDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "T", Slug = "t", ContactEmail = "t@x" });
            if (crossTenant)
                db.Tenants.Add(new Tenant { Id = otherTenantId, Name = "X", Slug = "x", ContactEmail = "x@x" });

            var userTenant = crossTenant ? otherTenantId : tenantId;
            db.Users.Add(new AppUser
            {
                Id = userId,
                TenantId = userTenant,
                Email = "u@x",
                DisplayName = "U",
                Iso19650Role = appUserIsoRole ?? "Z",
            });
            db.Projects.Add(new Project { Id = projectId, TenantId = tenantId, Name = "P", Code = "P" });

            if (projectMemberRole != null)
            {
                db.ProjectMembers.Add(new ProjectMember
                {
                    UserId = userId,
                    ProjectId = projectId,
                    Iso19650Role = projectMemberRole,
                    IsActive = true,
                });
            }
            await db.SaveChangesAsync();
        }

        var configEntries = new Dictionary<string, string?>();
        for (int i = 0; i < configRoles.Length; i++)
            configEntries[$"Authorization:BimManagerIso19650Roles:{i}"] = configRoles[i];
        var config = new ConfigurationBuilder().AddInMemoryCollection(configEntries).Build();

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var handler = new BimManagerOrAdminHandler(scopeFactory, config);

        var claims = new List<Claim>
        {
            new("user_id", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new(ClaimTypes.Role, "Contributor"),
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
        var requirement = new BimManagerOrAdminRequirement();
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);
        return (handler, ctx);
    }
}
