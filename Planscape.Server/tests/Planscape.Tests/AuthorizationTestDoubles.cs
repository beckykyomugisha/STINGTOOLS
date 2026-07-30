using Microsoft.Extensions.DependencyInjection;
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
        return services;
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
