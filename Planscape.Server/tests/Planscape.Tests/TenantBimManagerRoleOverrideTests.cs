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
/// Phase 154 — exercises tenant-scoped overrides of the BIM-Manager
/// role list. Distinct from Phase 153's
/// <see cref="BimManagerRoleConfigTests"/> which only covered the
/// deployment-global appsettings list.
/// </summary>
public class TenantBimManagerRoleOverrideTests
{
    [Fact]
    public async Task TenantOverride_NarrowsGrantBelowDeployment()
    {
        // Deployment grants K + C. Tenant overrides to just C.
        // User has K via project member → previously granted at
        // deployment level, now denied because tenant narrowed.
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "K",
            deploymentRoles: new[] { "K", "C" },
            tenantOverrideJson: """["C"]""");
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TenantOverride_BroadensGrantBeyondDeployment()
    {
        // Deployment grants K only. Tenant adds "M" so a Mech lead
        // can edit vocabulary on this tenant.
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "M",
            deploymentRoles: new[] { "K" },
            tenantOverrideJson: """["K", "M"]""");
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TenantOverride_CaseInsensitive()
    {
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "K",
            deploymentRoles: new[] { "K" },
            tenantOverrideJson: """["k"]""");
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]                  // wrong root type
    [InlineData("\"string\"")]          // wrong root type
    [InlineData("[]")]                  // empty array
    [InlineData("[1, true, null]")]     // no string entries
    public async Task MalformedOverride_FallsBackToDeployment(string overrideJson)
    {
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "K",
            deploymentRoles: new[] { "K" },
            tenantOverrideJson: overrideJson);
        await handler.HandleAsync(ctx);
        // Deployment still grants — fallback path engaged.
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task NullOverride_FallsBackToDeployment()
    {
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "K",
            deploymentRoles: new[] { "K" },
            tenantOverrideJson: null);
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TenantOverride_AppliesToAppUserGrantPath()
    {
        // No project member; user has Iso19650Role = "M".
        // Deployment is K only; tenant overrides to ["M"].
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: null,
            appUserIsoRole: "M",
            deploymentRoles: new[] { "K" },
            tenantOverrideJson: """["M"]""");
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    // ── Setup helper ─────────────────────────────────────────────

    private static async Task<(BimManagerOrAdminHandler, AuthorizationHandlerContext)> ArrangeAsync(
        string? projectMemberRole,
        string[] deploymentRoles,
        string? tenantOverrideJson,
        string? appUserIsoRole = null)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddDbContext<PlanscapeDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "T",
                Slug = "t",
                ContactEmail = "t@x",
                BimManagerIso19650RolesJson = tenantOverrideJson,
            });
            db.Users.Add(new AppUser
            {
                Id = userId,
                TenantId = tenantId,
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
        for (int i = 0; i < deploymentRoles.Length; i++)
            configEntries[$"Authorization:BimManagerIso19650Roles:{i}"] = deploymentRoles[i];
        var config = new ConfigurationBuilder().AddInMemoryCollection(configEntries).Build();

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var handler = new BimManagerOrAdminHandler(scopeFactory, config);

        var claims = new List<Claim>
        {
            new("user_id", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new(ClaimTypes.Role, "Contributor"),
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var ctx = new AuthorizationHandlerContext(
            new[] { new BimManagerOrAdminRequirement() }, user, resource: null);
        return (handler, ctx);
    }
}
