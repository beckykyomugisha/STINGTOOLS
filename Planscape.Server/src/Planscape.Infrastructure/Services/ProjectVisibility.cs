using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 175 — single source of truth for "who can see this project".
///
/// A project is visible to:
///   1. Tenant Admins / Owners / SecurityOfficers (full visibility)
///   2. The project author (Project.CreatedById)
///   3. Any user with an active ProjectMember row
///
/// Cross-tenant access is always denied; the predicate also requires
/// p.TenantId == userTenantId regardless of role.
/// </summary>
public static class ProjectVisibility
{
    /// <summary>
    /// Roles that bypass per-project membership checks. Mirrors the
    /// "see everything in your tenant" privilege.
    /// </summary>
    public static bool IsTenantAdmin(ClaimsPrincipal user)
    {
        var role = user.FindFirst("role")?.Value ?? "";
        return role is "Admin" or "Owner" or "SecurityOfficer";
    }

    public static Guid GetTenantId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    public static Guid GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? user.FindFirst("sub")?.Value
               ?? user.FindFirst("user_id")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Filters a project query down to projects the given user is allowed
    /// to see. Tenant scope is enforced unconditionally; admin role
    /// short-circuits the membership join.
    /// </summary>
    public static IQueryable<Project> WhereVisibleTo(
        this IQueryable<Project> projects,
        PlanscapeDbContext db,
        Guid tenantId,
        Guid userId,
        bool isTenantAdmin)
    {
        var q = projects.Where(p => p.TenantId == tenantId);
        if (isTenantAdmin) return q;

        // Author OR active member
        return q.Where(p =>
            p.CreatedById == userId
            || db.ProjectMembers.Any(m =>
                m.ProjectId == p.Id && m.UserId == userId && m.IsActive));
    }

    /// <summary>
    /// Convenience overload that pulls user/tenant/role from the
    /// ClaimsPrincipal so controllers don't have to repeat the boilerplate.
    /// </summary>
    public static IQueryable<Project> WhereVisibleTo(
        this IQueryable<Project> projects,
        PlanscapeDbContext db,
        ClaimsPrincipal user)
    {
        return projects.WhereVisibleTo(
            db,
            GetTenantId(user),
            GetUserId(user),
            IsTenantAdmin(user));
    }

    /// <summary>
    /// True when the calling user is allowed to access the given project
    /// (admin, author, or active member). Returns false for missing or
    /// cross-tenant projects so callers can return 404 without leaking
    /// existence.
    /// </summary>
    public static async Task<bool> CanSeeProjectAsync(
        PlanscapeDbContext db,
        Guid projectId,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId(user);
        var userId = GetUserId(user);
        var isAdmin = IsTenantAdmin(user);

        var visible = await db.Projects
            .Where(p => p.Id == projectId)
            .WhereVisibleTo(db, tenantId, userId, isAdmin)
            .AnyAsync(ct);

        return visible;
    }
}
