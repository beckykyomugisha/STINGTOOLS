using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// S3 — every write to a project-scoped resource (issues, documents,
/// transmittals, meetings, workflows, etc.) must verify the caller is an
/// active member of that project. Without this check, a user invited to
/// any project in Tenant A can mutate <i>every</i> project in Tenant A,
/// because most controllers only verify <c>Project.TenantId</c>.
/// <para/>
/// Tenant Admins / Owners are allowed through implicitly when the role
/// claim is set; callers should still call <see cref="IsProjectMemberAsync"/>
/// even for Admin paths so the audit trail records membership state.
/// </summary>
public static class ProjectMembershipGuard
{
    /// <summary>
    /// Returns true when the user is an active <c>ProjectMember</c> on the
    /// given project, or the user is a tenant Admin / Owner. The caller
    /// is expected to have already verified <c>Project.TenantId == userTenantId</c>.
    /// </summary>
    public static async Task<bool> IsProjectMemberAsync(
        PlanscapeDbContext db,
        Guid userId,
        Guid projectId,
        bool isTenantAdmin = false,
        CancellationToken ct = default)
    {
        if (isTenantAdmin) return true;
        if (userId == Guid.Empty || projectId == Guid.Empty) return false;
        return await db.ProjectMembers
            .AsNoTracking()
            .AnyAsync(m => m.ProjectId == projectId
                        && m.UserId == userId
                        && m.IsActive, ct);
    }
}
