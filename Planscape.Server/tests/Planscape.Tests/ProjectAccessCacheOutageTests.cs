using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Planscape.API.Authorization;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// The project-visibility cache in <see cref="ProjectAccessAttribute"/> is an
/// optimisation over the authoritative EF query, never a dependency. When
/// Redis was unreachable the exception propagated and every gated request
/// became a 500 — including the cross-tenant ones the filter exists to answer
/// with 404. CI had no Redis service, so five tenant-isolation tests
/// (Issues_OtherTenant_Returns404, TenantIsolation_OtherTenantCannotAccess*,
/// DeepLink_NonMember_GetsNotFound_NotForbidden) failed
/// "Expected: NotFound / Actual: InternalServerError".
///
/// Those five only caught it because Redis happened to be absent. CI now runs
/// a Redis service, which would quietly stop exercising the outage path — so
/// the guarantee is pinned here instead, by injecting a cache that always
/// throws. No <c>WebApplicationFactory</c> is involved: a second factory is
/// unreliable in this suite (ROADMAP DEP-7, process-wide Hangfire teardown),
/// so the filter is driven directly, as Phase 222 did for the handoff tests.
/// </summary>
public class ProjectAccessCacheOutageTests
{
    private static readonly Guid TenantId  = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid UserId    = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid OwnProj   = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid OtherProj = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");

    [Fact]
    public async Task CacheOutage_ForeignProject_Still404s_NotA500()
    {
        var (ctx, next) = BuildFilterContext(OtherProj);

        await new ProjectAccessAttribute().OnActionExecutionAsync(ctx, next.Delegate);

        Assert.IsType<NotFoundResult>(ctx.Result);
        Assert.False(next.WasCalled);
    }

    [Fact]
    public async Task CacheOutage_VisibleProject_StillAllowed()
    {
        // The mirror case. A fail-soft that denied everything would also make
        // the test above pass while breaking every legitimate request.
        var (ctx, next) = BuildFilterContext(OwnProj);

        await new ProjectAccessAttribute().OnActionExecutionAsync(ctx, next.Delegate);

        Assert.Null(ctx.Result);
        Assert.True(next.WasCalled);
    }

    // ── harness ───────────────────────────────────────────────────────────

    private static (ActionExecutingContext, NextSpy) BuildFilterContext(Guid projectId)
    {
        var db = NewSeededDb();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IDistributedCache>(new ThrowingDistributedCache());

        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
                new Claim("tenant_id", TenantId.ToString()),
            }, "test")),
        };

        var routeData = new RouteData();
        routeData.Values["projectId"] = projectId.ToString();

        var ctx = new ActionExecutingContext(
            new ActionContext(http, routeData, new ControllerActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);

        return (ctx, new NextSpy(ctx));
    }

    private static PlanscapeDbContext NewSeededDb()
    {
        var options = new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase($"CacheOutage_{Guid.NewGuid():N}")
            .Options;

        // The tenant context MUST be wired. PlanscapeDbContext's global query
        // filter falls back to Guid.Empty without one, which matches no rows —
        // so a context built from the parameterless ctor makes every project
        // invisible and the 404 assertion would pass for the wrong reason.
        var db = new PlanscapeDbContext(
            options,
            new HttpContextAccessor(),
            new StubTenantContext(TenantId));

        // Visible: same tenant, authored by the caller.
        db.Projects.Add(new Project
        {
            Id = OwnProj, TenantId = TenantId, CreatedById = UserId,
            Code = "PV-OWN-001", Name = "Own project",
        });

        // Not visible: different tenant, different author, no membership.
        db.Projects.Add(new Project
        {
            Id = OtherProj,
            TenantId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            CreatedById = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"),
            Code = "PV-OTHER-001", Name = "Other tenant project",
        });

        db.SaveChanges();
        return db;
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "test-tenant";
        public LicenseTier Tier => LicenseTier.Premium;
        public bool MimEnabled => true;
    }

    /// <summary>Stands in for Redis being unreachable on every operation.</summary>
    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        private static Exception Boom() =>
            new InvalidOperationException("simulated cache outage (stands in for RedisConnectionException)");

        public byte[]? Get(string key) => throw Boom();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Boom();
        public void Refresh(string key) => throw Boom();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Boom();
        public void Remove(string key) => throw Boom();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Boom();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Boom();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
                             CancellationToken token = default) => throw Boom();
    }

    private sealed class NextSpy
    {
        private readonly ActionExecutingContext _ctx;
        public NextSpy(ActionExecutingContext ctx) => _ctx = ctx;
        public bool WasCalled { get; private set; }

        public ActionExecutionDelegate Delegate => () =>
        {
            WasCalled = true;
            return Task.FromResult(new ActionExecutedContext(
                _ctx, new List<IFilterMetadata>(), controller: null!));
        };
    }
}
