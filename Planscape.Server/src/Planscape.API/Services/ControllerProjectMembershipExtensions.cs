using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Authorization;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Services;

/// <summary>
/// S3 — controller-side helper for the project-membership gate. Every
/// project-write endpoint must call this after the tenant ownership
/// check succeeds. Implementation lives in
/// <see cref="ProjectMembershipGuard"/>; this extension just resolves the
/// claims off the active <see cref="ControllerBase.User"/>.
/// </summary>
public static class ControllerProjectMembershipExtensions
{
    /// <summary>
    /// Returns null when the caller is an active member of the project
    /// (or a tenant Admin / Owner). Returns 403 ObjectResult otherwise.
    /// </summary>
    public static async Task<ActionResult?> RequireProjectMemberAsync(
        this ControllerBase controller,
        PlanscapeDbContext db,
        Guid projectId,
        CancellationToken ct = default)
    {
        var user = controller.User;
        var userId = ParseGuid(user.FindFirst("user_id")?.Value
                              ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        var isAdmin = user.IsInRole("Admin") || user.IsInRole("Owner");

        var ok = await ProjectMembershipGuard.IsProjectMemberAsync(db, userId, projectId, isAdmin, ct);
        if (!ok)
        {
            return controller.StatusCode(403, new { error = "You are not a member of this project" });
        }
        return null;
    }

    /// <summary>
    /// True when the caller may CURATE this project — albums, checklists,
    /// distribution groups, deleting another member's saved view.
    ///
    /// Replaces the hand-rolled `ProjectRole == "PM"` check that was copied
    /// into eight controllers. "PM" is an Iso19650Role code, never a
    /// ProjectRole, so those checks matched (essentially) nobody — see
    /// <see cref="ProjectRoles"/> for the full explanation.
    /// </summary>
    public static Task<bool> CanCurateProjectAsync(
        this ControllerBase controller, PlanscapeDbContext db, Guid projectId, CancellationToken ct = default)
        => HasCapabilityAsync(controller, db, projectId, ProjectRoles.CanCurateProject, ct);

    /// <summary>
    /// True when the caller may APPROVE site photos — approve/reject,
    /// include-originals, issue share links, PUT the photo policy. Narrower
    /// than curation: these decisions release imagery outside the project.
    /// </summary>
    public static Task<bool> CanApproveSitePhotosAsync(
        this ControllerBase controller, PlanscapeDbContext db, Guid projectId, CancellationToken ct = default)
        => HasCapabilityAsync(controller, db, projectId, ProjectRoles.CanApproveSitePhotosPredicate, ct);

    /// <summary>
    /// True when the caller may ADMINISTER this project — edit project-level
    /// settings (ISO naming enforcement, the custom deliverable state machine,
    /// the preferences blob).
    ///
    /// Replaces the <c>Iso19650Role == "K" || == "C"</c> check in
    /// <c>ProjectSettingsController.UpdateSettings</c>. Neither code is
    /// assignable through any UI and neither appears in the vocabulary this
    /// server itself serves, so that gate granted access to NOBODY. Routing it
    /// through the capability layer WIDENS who may edit project settings, from
    /// nobody to project managers and above. See <see cref="ProjectRoles"/>.
    /// </summary>
    public static Task<bool> CanAdministerProjectAsync(
        this ControllerBase controller, PlanscapeDbContext db, Guid projectId, CancellationToken ct = default)
        => HasCapabilityAsync(controller, db, projectId, ProjectRoles.CanAdministerProjectPredicate, ct);

    /// <summary>
    /// Shared body. The tenant `role` claim (Admin / Owner) grants without any
    /// ProjectMember row — that is pre-existing behaviour at every site this
    /// replaces, kept deliberately.
    /// </summary>
    private static async Task<bool> HasCapabilityAsync(
        ControllerBase controller,
        PlanscapeDbContext db,
        Guid projectId,
        System.Linq.Expressions.Expression<Func<ProjectMember, bool>> capability,
        CancellationToken ct)
    {
        var user = controller.User;

        // Match the claim shapes the replaced sites used: several read the raw
        // "role" claim rather than IsInRole, so check both.
        var roleClaim = user.FindFirst("role")?.Value ?? "";
        if (roleClaim is "Admin" or "Owner" || user.IsInRole("Admin") || user.IsInRole("Owner"))
            return true;

        var userId = ParseGuid(user.FindFirst("user_id")?.Value
                              ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                              ?? user.FindFirst("sub")?.Value);
        if (userId == Guid.Empty) return false;

        return await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId == userId && m.IsActive)
            .Where(capability)
            .AnyAsync(ct);
    }

    private static Guid ParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : Guid.Empty;
}
