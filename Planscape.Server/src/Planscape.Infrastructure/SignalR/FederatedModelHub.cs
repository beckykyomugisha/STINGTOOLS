using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

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
    /// <summary>Join the model-update stream for a project.</summary>
    public async Task JoinProject(string projectId)
    {
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
