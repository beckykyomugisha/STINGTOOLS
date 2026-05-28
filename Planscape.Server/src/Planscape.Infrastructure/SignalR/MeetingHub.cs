using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Planscape.Infrastructure.Data;

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
    private const string AuthKey = "auth_sessions";
    private readonly PlanscapeDbContext _db;
    public MeetingHub(PlanscapeDbContext db) => _db = db;

    private static string Group(string sessionId) => $"meeting:{sessionId}";

    // Sessions this connection passed the tenant check for. Cached per-connection
    // so the high-frequency broadcasts are O(1) (no per-camera-move DB hit) yet
    // can't fan into a session the caller never legitimately joined.
    private HashSet<string> Authorized =>
        Context.Items.TryGetValue(AuthKey, out var v) && v is HashSet<string> set
            ? set
            : (HashSet<string>)(Context.Items[AuthKey] = new HashSet<string>(StringComparer.Ordinal));

    public async Task JoinSession(string sessionId, string displayName)
    {
        if (!Guid.TryParse(sessionId, out var sid)
            || !await HubTenantGuard.OwnsSessionAsync(Context.User, _db, sid))
            return;

        Authorized.Add(sessionId);
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
        Authorized.Remove(sessionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(sessionId));
        await Clients.OthersInGroup(Group(sessionId)).SendAsync("ParticipantLeft", new
        {
            connectionId = Context.ConnectionId,
            userId = Context.UserIdentifier,
        });
    }

    /// <summary>Host camera move → followers track it (sent to others only).</summary>
    public Task BroadcastCamera(string sessionId, object camera)
        => Authorized.Contains(sessionId)
            ? Clients.OthersInGroup(Group(sessionId)).SendAsync("CameraMoved", new { camera })
            : Task.CompletedTask;

    /// <summary>Element selection broadcast (guids resolve cross-host via K1).</summary>
    public Task BroadcastHighlight(string sessionId, object guids)
        => Authorized.Contains(sessionId)
            ? Clients.OthersInGroup(Group(sessionId)).SendAsync("HighlightChanged", new { guids })
            : Task.CompletedTask;

    /// <summary>K3 — push a ViewerOverlayProfile to every participant.</summary>
    public Task BroadcastOverlay(string sessionId, object overlayProfile)
        => Authorized.Contains(sessionId)
            ? Clients.Group(Group(sessionId)).SendAsync("OverlayChanged", overlayProfile)
            : Task.CompletedTask;

    /// <summary>Section-plane / box change broadcast.</summary>
    public Task BroadcastSection(string sessionId, object section)
        => Authorized.Contains(sessionId)
            ? Clients.OthersInGroup(Group(sessionId)).SendAsync("SectionChanged", new { section })
            : Task.CompletedTask;

    /// <summary>
    /// Server-side push (from MeetingRoomController) when host/model/status
    /// changes so late joiners and existing clients re-sync their room state.
    /// </summary>
    public static Task NotifyRoomChanged(IHubContext<MeetingHub> hub, Guid sessionId, object state)
        => hub.Clients.Group(Group(sessionId.ToString())).SendAsync("RoomChanged", state);
}
