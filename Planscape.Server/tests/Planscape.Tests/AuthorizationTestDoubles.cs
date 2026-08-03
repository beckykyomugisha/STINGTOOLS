using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Authorization;

namespace Planscape.Tests;

/// <summary>
/// Inert stand-ins for the two collaborators the authorization handlers resolve
/// from the request scope.
///
/// These exist because several handler tests build their own
/// <c>ServiceCollection</c> rather than going through
/// <c>PlanscapeWebApplicationFactory</c>. When Phase 156 gave the handlers a
/// dependency on <see cref="IPermissionRevocationStore"/> (and the tenant role
/// resolver alongside it), those hand-rolled containers were not updated, so
/// every test in them died at
///   "No service for type 'IPermissionRevocationStore' has been registered."
/// — 20 of the suite's failures, from one omission rather than twenty bugs.
///
/// Both doubles are deliberately neutral: no revocation floor and no tenant
/// override, so a test that does not care about either gets default-allow
/// behaviour and only exercises the rule it is actually about. A test that DOES
/// care should register its own fake instead (see
/// <c>RevocationFloorHandlerTests</c>, which pins a specific floor).
/// </summary>
internal static class AuthorizationTestDoubles
{
    /// <summary>
    /// Registers what the handlers need to resolve: an inert revocation store,
    /// plus the REAL <see cref="DbTenantBimManagerRoleResolver"/> with the same
    /// Scoped lifetime production uses (Program.cs).
    ///
    /// The resolver is deliberately not a double. These suites seed
    /// <c>Tenant.BimManagerIso19650RolesJson</c> and assert on how it is read,
    /// so stubbing the resolver out makes every override test silently assert
    /// nothing — it swaps a missing-service error for a green-looking false
    /// negative, which is worse.
    /// </summary>
    public static IServiceCollection AddAuthorizationTestDoubles(this IServiceCollection services)
    {
        services.AddSingleton<IPermissionRevocationStore, NullRevocationStore>();
        services.AddScoped<ITenantBimManagerRoleResolver, DbTenantBimManagerRoleResolver>();
        services.AddTenantContextDouble();
        return services;
    }

    /// <summary>
    /// Just the ambient-tenant wiring, for suites that register their own
    /// revocation store or role resolver and must not have them overridden.
    ///
    /// Both registrations are needed or neither takes effect:
    /// PlanscapeDbContext's tenant-aware constructor asks for
    /// (DbContextOptions, IHttpContextAccessor, ITenantContext). Miss one and EF
    /// quietly picks the options-only constructor instead, leaving
    /// _tenantContext null and CurrentTenantId at Guid.Empty — the "fails
    /// closed, no rows" path the filter documents. Registering only
    /// ITenantContext changes nothing at all, which makes this easy to
    /// half-apply and conclude it didn't work.
    /// </summary>
    public static IServiceCollection AddTenantContextDouble(this IServiceCollection services)
    {
        services.AddSingleton<TestTenantContext>();
        services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<TestTenantContext>());
        services.AddHttpContextAccessor();
        return services;
    }

    /// <summary>
    /// Sets the ambient tenant these containers otherwise lack.
    ///
    /// PlanscapeDbContext applies a global filter of
    /// <c>TenantId == CurrentTenantId</c>, sourced from <see cref="ITenantContext"/>.
    /// In the real host that is populated per request from the JWT. A hand-built
    /// ServiceCollection registers nothing, so CurrentTenantId is Guid.Empty and
    /// the filter silently excludes every seeded row whose TenantId is a real
    /// GUID — the user lookup returns nothing, the Project behind a ProjectMember
    /// is filtered out of the join, and the handler denies for reasons that have
    /// nothing to do with the rule under test.
    ///
    /// Call this after seeding, with the tenant the fixture used.
    /// </summary>
    public static void UseTenant(this IServiceProvider sp, Guid tenantId) =>
        sp.GetRequiredService<TestTenantContext>().TenantId = tenantId;

    /// <summary>Mutable ambient tenant for tests.</summary>
    internal sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; } = Guid.Empty;
        public string TenantSlug => "test";
        public LicenseTier Tier => LicenseTier.Premium;
        public bool MimEnabled => true;
    }

    /// <summary>No token has ever been revoked.</summary>
    private sealed class NullRevocationStore : IPermissionRevocationStore
    {
        public Task<long?> GetMinIatAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<long?>(null);

        public Task RevokeAllPriorTokensAsync(Guid userId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }


}
