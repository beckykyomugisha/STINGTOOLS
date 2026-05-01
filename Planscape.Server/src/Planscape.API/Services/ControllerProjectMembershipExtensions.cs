using Microsoft.AspNetCore.Mvc;
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

    private static Guid ParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : Guid.Empty;
}
