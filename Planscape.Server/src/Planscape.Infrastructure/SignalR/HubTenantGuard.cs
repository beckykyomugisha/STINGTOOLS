using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Tenant-isolation guard for SignalR hub group joins. A hub connection has no
/// HttpContext, so the DbContext tenant query filter resolves to an empty
/// TenantId and can't be relied on — the check reads the tenant from the
/// connection's own claims and queries with IgnoreQueryFilters. Without this,
/// any authenticated user could join another tenant's project group (by
/// guessing the GUID) and receive its telemetry / overlay / alert pushes.
/// </summary>
internal static class HubTenantGuard
{
    public static async Task<bool> OwnsProjectAsync(
        ClaimsPrincipal? user, PlanscapeDbContext db, Guid projectId)
    {
        var claim = user?.FindFirst("tenant_id")?.Value;
        if (projectId == Guid.Empty || !Guid.TryParse(claim, out var tenantId)) return false;
        return await db.Projects.IgnoreQueryFilters()
            .AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
    }

    public static async Task<bool> OwnsSessionAsync(
        ClaimsPrincipal? user, PlanscapeDbContext db, Guid sessionId)
    {
        var claim = user?.FindFirst("tenant_id")?.Value;
        if (sessionId == Guid.Empty || !Guid.TryParse(claim, out var tenantId)) return false;
        return await db.MeetingSessions.IgnoreQueryFilters()
            .AnyAsync(s => s.Id == sessionId && s.TenantId == tenantId);
    }

    /// <summary>
    /// M3 — host gate for moderation hub methods (mute-all / remove). True only
    /// when the caller is the session's HostUserId within their own tenant.
    /// </summary>
    public static async Task<bool> IsSessionHostAsync(
        ClaimsPrincipal? user, PlanscapeDbContext db, Guid sessionId)
    {
        var tclaim = user?.FindFirst("tenant_id")?.Value;
        var uclaim = user?.FindFirst("sub")?.Value ?? user?.FindFirst("user_id")?.Value;
        if (sessionId == Guid.Empty
            || !Guid.TryParse(tclaim, out var tenantId)
            || !Guid.TryParse(uclaim, out var userId)) return false;
        return await db.MeetingSessions.IgnoreQueryFilters()
            .AnyAsync(s => s.Id == sessionId && s.TenantId == tenantId && s.HostUserId == userId);
    }
}
