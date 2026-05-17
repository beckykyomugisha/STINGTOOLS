// Planscape Server — ArchiCAD Live Hub.
//
// Clients (Planscape Web, Desktop, Mobile) connect to:
//   /hubs/archicad?projectId=<guid>
//
// They join a group keyed by projectId and receive live push events
// whenever ArchiCAD (via StingBridge) makes model changes:
//
//   ElementChanged   { elementId, type, properties }
//   ElementAdded     { elementId, type, properties }
//   ElementDeleted   { elementId }
//   ModelStatus      { connectedAuthors, lastPushUtc, activeLayers }
//
// StingBridge authenticates with a project API key (X-StingBridge-Key header)
// and calls the REST endpoint POST /api/archicad/push — the controller
// then calls Clients.Group(projectId).SendAsync(...) to fan out.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Planscape.Infrastructure.SignalR
{
    [Authorize]
    public class ArchiCADHub : Hub
    {
        public async Task JoinProject(string projectId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"archicad:{projectId}");
            await Clients.Caller.SendAsync("Joined", new { projectId, connectedAt = DateTime.UtcNow });
        }

        public async Task LeaveProject(string projectId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"archicad:{projectId}");
        }
    }
}
