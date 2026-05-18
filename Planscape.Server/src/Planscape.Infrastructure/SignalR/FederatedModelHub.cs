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
        string source = "unknown")
    {
        await hubContext.Clients
            .Group(ModelGroup(projectId))
            .SendAsync("ModelUpdated", new
            {
                projectId,
                updatedIds  = updatedUniqueIds,
                deletedIds  = deletedElementIds,
                source,
                timestamp   = DateTime.UtcNow
            });
    }

    private static string ModelGroup(string projectId) => $"model:{projectId}";
}
