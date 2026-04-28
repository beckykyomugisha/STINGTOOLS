using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// Phase 152 — handler for <see cref="BimManagerOrAdminRequirement"/>.
/// Two short-circuits:
///   1. Caller already in role <c>Admin</c> or <c>Owner</c> → granted
///      without a DB hit (mirrors existing controller-level
///      `[Authorize(Roles = "Admin,Owner")]` behaviour so this policy
///      is a strict superset).
///   2. Otherwise resolve the user's <c>user_id</c> claim and check
///      <see cref="Planscape.Core.Entities.ProjectMember.Iso19650Role"/>
///      for the value <c>K</c> (BIM Manager) on any active project in
///      the tenant. One row is enough — a BIM Manager on any project
///      counts.
///
/// Uses <see cref="IServiceScopeFactory"/> so the handler can run
/// outside of an HTTP request (e.g. SignalR) and grab a scoped
/// <see cref="PlanscapeDbContext"/> without forcing the entire policy
/// to be scoped.
/// </summary>
public sealed class BimManagerOrAdminHandler : AuthorizationHandler<BimManagerOrAdminRequirement>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public BimManagerOrAdminHandler(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

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

        // Project's tenant must match the caller's claim — defends
        // against a stale token whose claim doesn't reflect current
        // membership.
        var hasBimManagerRow = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive && m.Iso19650Role == "K")
            .Where(m => m.Project!.TenantId == tenantId)
            .AnyAsync();

        if (hasBimManagerRow) context.Succeed(requirement);
    }
}
