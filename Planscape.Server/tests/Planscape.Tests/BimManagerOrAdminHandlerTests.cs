using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
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
        services.AddDbContext<PlanscapeDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
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
}
