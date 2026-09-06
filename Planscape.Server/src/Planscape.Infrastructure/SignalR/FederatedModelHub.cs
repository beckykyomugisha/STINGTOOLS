using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Infrastructure.Authorization;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Real-time notifications for the federated model viewer.
/// Clients join a project group and receive <c>ModelUpdated</c> events
/// whenever the Revit plugin (or any ingest adapter) uploads new geometry.
///
/// Client usage (TypeScript / Three.js viewer):
///   connection.on("ModelUpdated", ({ projectId, updatedIds, deletedIds }) => viewer.reload());
/// </summary>
[Authorize]
public class FederatedModelHub : Hub
{
    private readonly IServiceScopeFactory _scopeFactory;

    public FederatedModelHub(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    /// <summary>
    /// Join the model-update stream for a project.
    ///
    /// <c>[Authorize]</c> alone only proves the caller is signed in — it says
    /// nothing about WHICH project they may watch. Without the membership check
    /// below, any authenticated user could join <c>model:{anyProjectId}</c> and
    /// receive that project's ModelUpdated stream (element ids of everything
    /// being edited, across tenants — the group name is the only key).
    /// <see cref="NotificationHub.JoinProject"/> has validated membership since
    /// NEW-LOGIC-15; this hub was missed.
    ///
    /// The membership decision goes through <see cref="ProjectMembershipGuard"/>
    /// so it matches the REST gate on the same resources (ModelTransform /
    /// Alignment / Scene), including the tenant Admin / Owner bypass.
    ///
    /// <para><b>Why the tenant check is explicit here.</b> Everywhere else the
    /// ambient global query filter supplies tenant scope, but it reads
    /// <c>ITenantContext.TenantId</c>, which resolves off
    /// <c>IHttpContextAccessor</c> — and that is not reliably populated inside a
    /// SignalR hub method (the documented accessor for a hub is
    /// <c>Context.GetHttpContext()</c>). An unresolved tenant makes
    /// <c>CurrentTenantId</c> <see cref="Guid.Empty"/>, which the filter treats
    /// as "matches no rows": fail-closed, but it would deny every legitimate
    /// member too. So this reads the tenant straight off the connection's JWT
    /// principal — always present under <c>[Authorize]</c> — bypasses the
    /// ambient filter for the one query, and re-applies tenant scope by hand.
    /// The result is a gate that does not depend on whether HttpContext flows.
    /// The scope (and therefore the bypass) is local to this call.</para>
    /// </summary>
    /// <exception cref="HubException">
    /// Thrown for a malformed id, an unauthenticated caller, a cross-tenant
    /// project, or a non-member. SignalR surfaces this to the caller and the
    /// group is not joined.
    /// </exception>
    public async Task JoinProject(string projectId)
    {
        if (!Guid.TryParse(projectId, out var pid) || pid == Guid.Empty)
            throw new HubException("Invalid project id");

        var user = Context.User;
        if (user == null) throw new HubException("Not authenticated");

        var userId = ProjectVisibility.GetUserId(user);
        var tenantId = ProjectVisibility.GetTenantId(user);
        if (userId == Guid.Empty || tenantId == Guid.Empty)
            throw new HubException("Not authenticated");

        // The DbContext is scoped; a hub instance is per-invocation but may
        // outlive a request scope, so resolve one per call.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;

        // Cross-tenant first: the project must live in the caller's tenant.
        var sameTenant = await db.Projects.AsNoTracking()
            .AnyAsync(p => p.Id == pid && p.TenantId == tenantId);
        if (!sameTenant)
            throw new HubException("Not a member of this project");

        var isAdmin = ProjectVisibility.IsTenantAdmin(user);
        if (!await ProjectMembershipGuard.IsProjectMemberAsync(db, userId, pid, isAdmin))
            throw new HubException("Not a member of this project");

        await Groups.AddToGroupAsync(Context.ConnectionId, ModelGroup(projectId));
    }

    /// <summary>Leave the model-update stream for a project.</summary>
    public async Task LeaveProject(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ModelGroup(projectId));
    }

    /// <summary>
    /// Notify all viewer clients in a project that geometry has changed.
    /// Called by <see cref="Planscape.API.Controllers.FederatedModelController"/>,
    /// <see cref="Planscape.API.Controllers.IfcIngestController"/>, and
    /// <see cref="Planscape.Infrastructure.Services.AutoAlignService"/> after
    /// persisting the delta or a new coordinate transform.
    /// </summary>
    /// <param name="source">
    /// Originating tool: "revit" | "archicad" | "ifc-ingest" | "auto-align" | "unknown".
    /// Clients use this to decide how to refresh (e.g. mobile shows "ArchiCAD updated"
    /// rather than a generic banner).
    /// </param>
    public static async Task NotifyUpdate(
        IHubContext<FederatedModelHub> hubContext,
        string projectId,
        IEnumerable<string> updatedUniqueIds,
        IEnumerable<long> deletedElementIds,
        string source = "unknown",
        IHubContext<NotificationHub>? notificationHub = null)
    {
        // Materialise once — the same payload is fanned to two hubs.
        var updated = updatedUniqueIds as ICollection<string> ?? updatedUniqueIds.ToList();
        var deleted = deletedElementIds as ICollection<long> ?? deletedElementIds.ToList();
        var payload = new
        {
            projectId,
            updatedIds  = updated,
            deletedIds  = deleted,
            source,
            timestamp   = DateTime.UtcNow
        };

        await hubContext.Clients
            .Group(ModelGroup(projectId))
            .SendAsync("ModelUpdated", payload);

        // #12 — the only ModelUpdated consumers (dashboard.js:167, the plugin's
        // PlanscapeRealtimeClient.cs:207) subscribe on NotificationHub
        // (/hubs/notifications), group `project-{id}` — NOT on /hubs/model's
        // `model:{id}` group, which no client joins. Re-emit there so the event
        // actually reaches them.
        if (notificationHub != null)
        {
            await notificationHub.Clients
                .Group($"project-{projectId}")
                .SendAsync("ModelUpdated", payload);
        }
    }

    private static string ModelGroup(string projectId) => $"model:{projectId}";
}
