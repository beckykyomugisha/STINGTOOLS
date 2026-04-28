using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// Phase 157 — exercises the parallel revocation + tenant-override
/// reads on the auth handler. We can't measure pipelining benefit
/// directly without Redis, but we can verify both lookups complete
/// in roughly the longer of the two latencies (i.e. they ran in
/// parallel rather than serially).
/// </summary>
public class AuthHandlerConcurrentReadsTests
{
    [Fact]
    public async Task RevocationAndTenantOverride_LaunchInParallel()
    {
        // Each fake adds a 200ms delay. If the handler awaited them
        // serially, total wait would be ~400ms; in parallel it's
        // ~200ms (whichever finishes last). We assert <350ms with
        // a margin for CI noise.
        var (handler, ctx) = await ArrangeAsync(
            revocationDelay: TimeSpan.FromMilliseconds(200),
            tenantResolverDelay: TimeSpan.FromMilliseconds(200));

        var sw = Stopwatch.StartNew();
        await handler.HandleAsync(ctx);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 350,
            $"Expected parallel reads (≈200ms), got {sw.ElapsedMilliseconds}ms — likely serial");
        // And the policy still grants on the K-role project member.
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task BothLookups_AreObservedByTheHandler()
    {
        // Verify both fakes are actually called (so the parallel
        // dispatch isn't accidentally short-circuiting one of them).
        var rev = new RecordingRevocationStore(null);
        var ten = new RecordingTenantResolver(null);
        var (handler, ctx) = await ArrangeAsync(rev, ten);
        await handler.HandleAsync(ctx);
        Assert.Equal(1, rev.Calls);
        Assert.Equal(1, ten.Calls);
    }

    [Fact]
    public async Task RevocationFloorAndOverride_BothApplied()
    {
        // Token is fresh enough to clear the floor; tenant override
        // narrows BIM-Manager grant to ["C"]; user has K → denied
        // by the override even though the floor accepts the token.
        var freshIat = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        var oldFloor = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var rev = new RecordingRevocationStore(oldFloor);
        var ten = new RecordingTenantResolver(new[] { "C" });

        var (handler, ctx) = await ArrangeAsync(rev, ten, tokenIat: freshIat);
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    // ── Setup helpers ────────────────────────────────────────────

    private static Task<(BimManagerOrAdminHandler, AuthorizationHandlerContext)> ArrangeAsync(
        TimeSpan revocationDelay, TimeSpan tenantResolverDelay)
        => ArrangeAsync(
            new SlowRevocationStore(revocationDelay, returnFloor: null),
            new SlowTenantResolver(tenantResolverDelay, returnRoles: null),
            tokenIat: null);

    private static async Task<(BimManagerOrAdminHandler, AuthorizationHandlerContext)> ArrangeAsync(
        IPermissionRevocationStore revocationStore,
        ITenantBimManagerRoleResolver tenantResolver,
        long? tokenIat = null)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddDbContext<PlanscapeDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton(revocationStore);
        services.AddSingleton(tenantResolver);
        var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "T", Slug = "t", ContactEmail = "t@x" });
            db.Users.Add(new AppUser { Id = userId, TenantId = tenantId, Email = "u@x", DisplayName = "U", Iso19650Role = "Z" });
            db.Projects.Add(new Project { Id = projectId, TenantId = tenantId, Name = "P", Code = "P" });
            db.ProjectMembers.Add(new ProjectMember
            {
                UserId = userId, ProjectId = projectId,
                Iso19650Role = "K", IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var handler = new BimManagerOrAdminHandler(
            sp.GetRequiredService<IServiceScopeFactory>(), config: null);

        var claims = new List<Claim>
        {
            new("user_id", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new(ClaimTypes.Role, "Contributor"),
        };
        if (tokenIat.HasValue) claims.Add(new("iat", tokenIat.Value.ToString()));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var ctx = new AuthorizationHandlerContext(
            new[] { new BimManagerOrAdminRequirement() }, user, resource: null);
        return (handler, ctx);
    }

    private sealed class SlowRevocationStore : IPermissionRevocationStore
    {
        private readonly TimeSpan _delay;
        private readonly long? _floor;
        public SlowRevocationStore(TimeSpan delay, long? returnFloor)
        { _delay = delay; _floor = returnFloor; }
        public async Task<long?> GetMinIatAsync(Guid userId, CancellationToken ct = default)
        {
            await Task.Delay(_delay, ct); return _floor;
        }
        public Task RevokeAllPriorTokensAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SlowTenantResolver : ITenantBimManagerRoleResolver
    {
        private readonly TimeSpan _delay;
        private readonly IReadOnlyList<string>? _roles;
        public SlowTenantResolver(TimeSpan delay, IReadOnlyList<string>? returnRoles)
        { _delay = delay; _roles = returnRoles; }
        public async Task<IReadOnlyList<string>?> ResolveAsync(Guid tenantId, CancellationToken ct = default)
        {
            await Task.Delay(_delay, ct); return _roles;
        }
    }

    private sealed class RecordingRevocationStore : IPermissionRevocationStore
    {
        private readonly long? _floor;
        public int Calls { get; private set; }
        public RecordingRevocationStore(long? floor) => _floor = floor;
        public Task<long?> GetMinIatAsync(Guid userId, CancellationToken ct = default)
        {
            Calls++; return Task.FromResult(_floor);
        }
        public Task RevokeAllPriorTokensAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingTenantResolver : ITenantBimManagerRoleResolver
    {
        private readonly IReadOnlyList<string>? _roles;
        public int Calls { get; private set; }
        public RecordingTenantResolver(IReadOnlyList<string>? roles) => _roles = roles;
        public Task<IReadOnlyList<string>?> ResolveAsync(Guid tenantId, CancellationToken ct = default)
        {
            Calls++; return Task.FromResult(_roles);
        }
    }
}
