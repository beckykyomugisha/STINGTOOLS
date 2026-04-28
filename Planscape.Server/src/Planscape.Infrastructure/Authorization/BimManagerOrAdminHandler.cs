using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// Phase 152 — handler for <see cref="BimManagerOrAdminRequirement"/>.
/// Three grant paths in order:
///   1. Caller already in role <c>Admin</c> or <c>Owner</c> → granted
///      without a DB hit.
///   2. Caller's <see cref="Planscape.Core.Entities.AppUser.Iso19650Role"/>
///      matches a configured BIM-Manager role code → granted with a
///      single Users lookup. Lets a tenant flag a user as the project
///      BIM Manager via the AppUser row even when no per-project
///      ProjectMember row exists yet.
///   3. Caller has at least one active
///      <see cref="Planscape.Core.Entities.ProjectMember"/> row whose
///      <c>Iso19650Role</c> matches a configured BIM-Manager role code
///      on a project in the caller's tenant → granted.
///
/// Phase 153 — the "K" hardcode became a configurable list. Read from
/// <c>Authorization:BimManagerIso19650Roles</c> in appsettings; defaults
/// to <c>["K"]</c>. Operators can broaden grants to e.g. <c>["K", "C"]</c>
/// (BIM Manager + Coordinator) without rebuilding.
///
/// Uses <see cref="IServiceScopeFactory"/> so the handler can run
/// outside of an HTTP request (e.g. SignalR) and grab a scoped
/// <see cref="PlanscapeDbContext"/> without forcing the entire policy
/// to be scoped.
/// </summary>
public sealed class BimManagerOrAdminHandler : AuthorizationHandler<BimManagerOrAdminRequirement>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<string> _bimManagerRoles;

    /// <summary>Default BIM Manager role list when no config is provided.
    /// "K" is the canonical ISO 19650 BIM Manager code.</summary>
    public static readonly IReadOnlyList<string> DefaultBimManagerRoles = new[] { "K" };

    public BimManagerOrAdminHandler(IServiceScopeFactory scopeFactory, IConfiguration? config = null)
    {
        _scopeFactory = scopeFactory;
        _bimManagerRoles = ReadRoleList(config);
    }

    /// <summary>Phase 153 — read the configured role list from
    /// <c>Authorization:BimManagerIso19650Roles</c>. Empty / missing
    /// config falls back to <see cref="DefaultBimManagerRoles"/>;
    /// non-string entries are dropped silently. All comparisons are
    /// case-insensitive — the list is uppercased once at construction
    /// time so the hot-path check is a simple <c>Contains</c>.</summary>
    private static IReadOnlyList<string> ReadRoleList(IConfiguration? config)
    {
        if (config == null) return DefaultBimManagerRoles;
        var section = config.GetSection("Authorization:BimManagerIso19650Roles");
        if (!section.Exists()) return DefaultBimManagerRoles;
        var entries = section.GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
        return entries.Count > 0 ? entries : DefaultBimManagerRoles;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BimManagerOrAdminRequirement requirement)
    {
        // Tenant-Admin / Owner short-circuit. Both Identity-style
        // role claims and the bare "role" claim are checked because
        // different test fixtures use different claim names.
        if (context.User.IsInRole("Admin") || context.User.IsInRole("Owner"))
        {
            context.Succeed(requirement);
            return;
        }

        // Resolve user_id; without it we have no basis to check
        // membership.
        var userIdClaim = context.User.FindFirst("user_id")?.Value
                         ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId)) return;

        var tenantIdClaim = context.User.FindFirst("tenant_id")?.Value;
        if (!Guid.TryParse(tenantIdClaim, out var tenantId)) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();

        // Phase 153 — AppUser-level grant. Some tenants populate
        // AppUser.Iso19650Role at onboarding before any per-project
        // membership row exists. Honour that signal so a BIM Manager
        // who's freshly invited can curate vocabulary on day one
        // without first being added to a project.
        var userIso = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == tenantId)
            .Select(u => u.Iso19650Role)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(userIso)
            && _bimManagerRoles.Contains(userIso!.ToUpperInvariant()))
        {
            context.Succeed(requirement);
            return;
        }

        // Project-membership grant. Project's tenant must match the
        // caller's claim — defends against a stale token whose claim
        // doesn't reflect current membership.
        var hasBimManagerRow = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive)
            .Where(m => _bimManagerRoles.Contains(m.Iso19650Role.ToUpper()))
            .Where(m => m.Project!.TenantId == tenantId)
            .AnyAsync();

        if (hasBimManagerRow) context.Succeed(requirement);
    }
}
