using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
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
/// Phase 156 — exercises the iat-vs-revocation-floor path on
/// <see cref="BimManagerOrAdminHandler"/>. Distinct from the existing
/// handler tests because we need a non-trivial revocation store that
/// actually returns floors.
/// </summary>
public class RevocationFloorHandlerTests
{
    [Fact]
    public async Task TokenIssuedAfterRevocation_StillGrants()
    {
        // User had their permissions revoked at T0; new token issued
        // at T1 > T0. iat > floor → grant proceeds.
        var floor = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var iat   = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "K", revocationFloor: floor, tokenIat: iat);
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TokenIssuedBeforeRevocation_Denied()
    {
        // User's permissions were revoked AFTER the token was issued.
        // iat < floor → deny even though the token signature is fine
        // and the project membership still says "K".
        var iat   = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var floor = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "K", revocationFloor: floor, tokenIat: iat);
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task NoRevocationFloor_TokenWithoutIatStillGrants()
    {
        // No floor recorded → handler skips the iat comparison
        // entirely. Tokens without iat are allowed when no floor
        // exists (legacy clients).
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "K", revocationFloor: null, tokenIat: null);
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task RevocationFloor_TokenWithoutIatDenied()
    {
        // A floor exists and the token has no iat — conservative:
        // deny. Forces clients to migrate to iat-bearing tokens.
        var floor = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: "K", revocationFloor: floor, tokenIat: null);
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task AdminRole_BypassesRevocationCheck()
    {
        // Admin short-circuits before the iat check, so an admin
        // can't accidentally lock themselves out by triggering their
        // own revocation.
        var floor = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds(); // future floor
        var iat   = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var (handler, ctx) = await ArrangeAsync(
            projectMemberRole: null, revocationFloor: floor, tokenIat: iat,
            tenantRole: "Admin");
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    // ── Setup helper ─────────────────────────────────────────────

    private static async Task<(BimManagerOrAdminHandler, AuthorizationHandlerContext)> ArrangeAsync(
        string? projectMemberRole,
        long? revocationFloor,
        long? tokenIat,
        string tenantRole = "Contributor")
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddDbContext<PlanscapeDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton<ITenantBimManagerRoleResolver>(new NoopTenantRoleResolver());
        services.AddSingleton<IPermissionRevocationStore>(new FakeRevocationStore(revocationFloor));
        var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "T", Slug = "t", ContactEmail = "t@x" });
            db.Users.Add(new AppUser { Id = userId, TenantId = tenantId, Email = "u@x", DisplayName = "U", Iso19650Role = "Z" });
            db.Projects.Add(new Project { Id = projectId, TenantId = tenantId, Name = "P", Code = "P" });
            if (projectMemberRole != null)
            {
                db.ProjectMembers.Add(new ProjectMember
                {
                    UserId = userId, ProjectId = projectId,
                    Iso19650Role = projectMemberRole, IsActive = true,
                });
            }
            await db.SaveChangesAsync();
        }

        var handler = new BimManagerOrAdminHandler(sp.GetRequiredService<IServiceScopeFactory>(), config: null);

        var claims = new List<Claim>
        {
            new("user_id", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new(ClaimTypes.Role, tenantRole),
        };
        if (tokenIat.HasValue) claims.Add(new("iat", tokenIat.Value.ToString()));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var ctx = new AuthorizationHandlerContext(
            new[] { new BimManagerOrAdminRequirement() }, user, resource: null);
        return (handler, ctx);
    }

    private sealed class FakeRevocationStore : IPermissionRevocationStore
    {
        private readonly long? _floor;
        public FakeRevocationStore(long? floor) => _floor = floor;
        public Task<long?> GetMinIatAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_floor);
        public Task RevokeAllPriorTokensAsync(Guid userId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopTenantRoleResolver : ITenantBimManagerRoleResolver
    {
        public Task<IReadOnlyList<string>?> ResolveAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>?>(null);
    }
}
