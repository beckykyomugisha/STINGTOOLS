using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Pillar A (3A) — live 3D meeting-viewer sync. Carries the high-frequency,
/// non-persisted traffic between participants of a <c>MeetingSession</c>:
/// camera moves, element highlights, K3 overlay changes, and section planes.
/// Durable state (who's host, which model) lives in the entity + controller;
/// this hub is the wire.
///
/// Client usage (meeting-sync.js over window.STING_VIEWER):
///   conn.on("CameraMoved",     ({camera}) => if(following) viewer.restoreCamera(camera));
///   conn.on("HighlightChanged",({guids})  => viewer.highlight(guids));
///   conn.on("OverlayChanged",  (profile)  => STING_VIEWER.applyOverlay(profile));  // K3
///   conn.on("SectionChanged",  (section)  => viewer.setSection(section));
///   conn.invoke("BroadcastCamera", sessionId, cameraJson);
/// </summary>
[Authorize]
public class MeetingHub : Hub
{
    private static string Group(string sessionId) => $"meeting:{sessionId}";

    public async Task JoinSession(string sessionId, string displayName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(sessionId));
        await Clients.OthersInGroup(Group(sessionId)).SendAsync("ParticipantJoined", new
        {
            connectionId = Context.ConnectionId,
            userId = Context.UserIdentifier,
            displayName,
        });
    }

    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(sessionId));
        await Clients.OthersInGroup(Group(sessionId)).SendAsync("ParticipantLeft", new
        {
            connectionId = Context.ConnectionId,
            userId = Context.UserIdentifier,
        });
    }

    /// <summary>Host camera move → followers track it (sent to others only).</summary>
    public Task BroadcastCamera(string sessionId, object camera)
        => Clients.OthersInGroup(Group(sessionId)).SendAsync("CameraMoved", new { camera });

    /// <summary>Element selection broadcast (guids resolve cross-host via K1).</summary>
    public Task BroadcastHighlight(string sessionId, object guids)
        => Clients.OthersInGroup(Group(sessionId)).SendAsync("HighlightChanged", new { guids });

    /// <summary>K3 — push a ViewerOverlayProfile to every participant.</summary>
    public Task BroadcastOverlay(string sessionId, object overlayProfile)
        => Clients.Group(Group(sessionId)).SendAsync("OverlayChanged", overlayProfile);

    /// <summary>Section-plane / box change broadcast.</summary>
    public Task BroadcastSection(string sessionId, object section)
        => Clients.OthersInGroup(Group(sessionId)).SendAsync("SectionChanged", new { section });

    /// <summary>
    /// Server-side push (from MeetingRoomController) when host/model/status
    /// changes so late joiners and existing clients re-sync their room state.
    /// </summary>
    public static Task NotifyRoomChanged(IHubContext<MeetingHub> hub, Guid sessionId, object state)
        => hub.Clients.Group(Group(sessionId.ToString())).SendAsync("RoomChanged", state);
}
