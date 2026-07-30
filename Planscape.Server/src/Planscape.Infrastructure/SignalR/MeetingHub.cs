using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly LiveKitRoomService _lkRooms;

    public MeetingHub(PlanscapeDbContext db, IConfiguration config)
    {
        _db = db;
        _lkRooms = new LiveKitRoomService(config);
    }

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

    /// <summary>
    /// H-7 — emit ParticipantLeft on any disconnect (tab crash, network drop,
    /// mobile backgrounding). Previously only the explicit best-effort
    /// LeaveSession (beforeunload) cleared a participant, so dropped clients
    /// lingered as ghosts in every peer's presence panel. SignalR removes the
    /// connection from its groups automatically but does NOT notify the group.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var sessionId in Authorized.ToArray())
        {
            await Clients.OthersInGroup(Group(sessionId)).SendAsync("ParticipantLeft", new
            {
                connectionId = Context.ConnectionId,
                userId = Context.UserIdentifier,
            });
        }
        await base.OnDisconnectedAsync(exception);
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
    /// WS3 (MeetingMedia) — presenter switches the active surface every client
    /// shows (model | document | screen). Broadcast to the WHOLE group (incl. the
    /// sender) so the presenter's own UI stays in lock-step. <paramref name="surface"/>
    /// carries { surface, documentId? }. LiveKit owns the actual screen-share media;
    /// this only tells clients which pane to show.
    /// </summary>
    public Task BroadcastSurface(string sessionId, object surface)
        => Authorized.Contains(sessionId)
            ? Clients.Group(Group(sessionId)).SendAsync("SurfaceChanged", surface)
            : Task.CompletedTask;

    /// <summary>
    /// M2 (MeetingMarkup) — collaborative markup on the shared DOCUMENT surface.
    /// Carries one markup op to the OTHER participants (the sender already drew
    /// it locally): { op: "add", stroke } · { op: "clear" } · { op: "grant", on }.
    /// This is co-presence data (the wire) — LiveKit (media plane) is untouched.
    /// Durable capture (Save-as-Snapshot / Save-as-Issue) is a separate REST call;
    /// the hub only mirrors live strokes so everyone sees markup as it is drawn.
    /// </summary>
    public Task BroadcastDocMarkup(string sessionId, object markup)
        => Authorized.Contains(sessionId)
            ? Clients.OthersInGroup(Group(sessionId)).SendAsync("DocMarkupChanged", markup)
            : Task.CompletedTask;

    // ── M3 — conferencing essentials (co-presence: chat / reactions / hand /
    //    moderation). Media stays on LiveKit; mute/remove are SIGNALS the target
    //    client self-applies on its media plane. ──────────────────────────────

    /// <summary>In-meeting chat line → the other participants (sender echoes locally).</summary>
    public Task BroadcastChat(string sessionId, object message)
        => Authorized.Contains(sessionId)
            ? Clients.OthersInGroup(Group(sessionId)).SendAsync("ChatReceived", message)
            : Task.CompletedTask;

    /// <summary>Ephemeral reaction (👍/👏/❤️/😂 …) → the other participants.</summary>
    public Task BroadcastReaction(string sessionId, object reaction)
        => Authorized.Contains(sessionId)
            ? Clients.OthersInGroup(Group(sessionId)).SendAsync("ReactionReceived", reaction)
            : Task.CompletedTask;

    /// <summary>Raise / lower hand → others update the roster's hand indicator.</summary>
    public Task BroadcastHand(string sessionId, bool raised)
        => Authorized.Contains(sessionId)
            ? Clients.OthersInGroup(Group(sessionId)).SendAsync("HandChanged",
                new { connectionId = Context.ConnectionId, userId = Context.UserIdentifier, raised })
            : Task.CompletedTask;

    /// <summary>
    /// Host-only: mute everyone but the host.
    ///
    /// Two layers, both host-gated by <see cref="HubTenantGuard.IsSessionHostAsync"/>:
    ///   1. ENFORCE — LiveKit <c>RoomService.MutePublishedTrack</c> on every remote
    ///      microphone track. The SFU stops forwarding the audio; a modified client
    ///      cannot decline. Screen-share audio is deliberately left alone.
    ///   2. SIGNAL — the existing <c>Moderation</c> broadcast, so each client's own mic
    ///      button paints "off" and the user is told what happened.
    /// The signal is still sent when LiveKit is unconfigured (or a participant is on the
    /// co-presence plane only, with no A/V joined): moderation degrades to the old
    /// advisory behaviour rather than failing. <c>enforced</c> in the payload says which
    /// happened, so the UI never claims more than it did.
    /// </summary>
    public async Task MuteAll(string sessionId)
    {
        if (!Authorized.Contains(sessionId)) return;
        if (!Guid.TryParse(sessionId, out var sid)
            || !await HubTenantGuard.IsSessionHostAsync(Context.User, _db, sid)) return;

        // room == sessionId (MeetingRoomController.LiveKitToken), identity == userId.
        var mutedTracks = await _lkRooms.MuteAllMicrophonesAsync(sessionId, CallerUserId(), Context.ConnectionAborted);

        await Clients.OthersInGroup(Group(sessionId)).SendAsync("Moderation",
            new { action = "mute-all", by = Context.UserIdentifier, enforced = _lkRooms.IsConfigured, mutedTracks });
    }

    /// <summary>
    /// Host-only: remove a participant.
    ///
    /// ENFORCE — LiveKit <c>RoomService.RemoveParticipant</c> evicts them from the media
    /// room outright. SIGNAL — the existing group-scoped <c>Moderation</c> broadcast still
    /// fires so the target leaves the co-presence plane too (SignalR group, roster,
    /// markup) and everyone else drops the row. Enforcement covers media; the signal
    /// covers everything media doesn't own.
    ///
    /// <paramref name="targetUserId"/> is the LiveKit identity to evict. It is NOT trusted
    /// blindly: it must be a participant of THIS session, so a host cannot aim the eviction
    /// at a room they don't own. (The connection id can't be used for this — SignalR does
    /// not expose a connection→user map, and with the Redis backplane the target's
    /// connection may live on another instance.)
    /// </summary>
    public async Task RemoveParticipant(string sessionId, string targetConnectionId, string? targetUserId)
    {
        if (!Authorized.Contains(sessionId)) return;
        if (!Guid.TryParse(sessionId, out var sid)
            || !await HubTenantGuard.IsSessionHostAsync(Context.User, _db, sid)) return;

        var enforced = false;
        if (Guid.TryParse(targetUserId, out var targetUid))
        {
            var inSession = await _db.MeetingViewerParticipants.IgnoreQueryFilters()
                .AnyAsync(p => p.SessionId == sid && p.UserId == targetUid);
            if (inSession)
                enforced = await _lkRooms.RemoveParticipantAsync(
                    sessionId, targetUid.ToString(), Context.ConnectionAborted);
        }

        await Clients.Group(Group(sessionId)).SendAsync("Moderation",
            new { action = "remove", connectionId = targetConnectionId, by = Context.UserIdentifier, enforced });
    }

    /// <summary>The caller's user id — the LiveKit identity minted for them by
    /// MeetingRoomController.LiveKitToken. Used to exempt the host from mute-all.</summary>
    private string? CallerUserId() =>
        Context.UserIdentifier
        ?? Context.User?.FindFirst("sub")?.Value
        ?? Context.User?.FindFirst("user_id")?.Value;

    // ── S2 — late-join state replay ───────────────────────────────────────────
    //
    // The hub mirrors live ops, so a tab joining mid-session saw a blank markup
    // canvas and an empty roster: the strokes, the raised hands and the
    // ParticipantJoined events it needed all happened before it arrived.
    //
    // The fix is a round-trip between PEERS rather than a server-side buffer.
    // The hub stays a wire — it holds no meeting state, so nothing to bound, to
    // evict, or to lose when an instance restarts, and it works unchanged behind
    // the Redis backplane (a server-side buffer would live on one instance only).
    // The clients already hold the authoritative copy of what needs replaying.

    /// <summary>A client that just joined asks the room for its current state.
    /// Peers answer with <see cref="SendState"/>.</summary>
    public Task RequestState(string sessionId)
        => Authorized.Contains(sessionId)
            ? Clients.OthersInGroup(Group(sessionId)).SendAsync("StateRequested",
                new { connectionId = Context.ConnectionId, userId = Context.UserIdentifier })
            : Task.CompletedTask;

    /// <summary>
    /// A peer's answer to <see cref="RequestState"/>, addressed to
    /// <paramref name="targetConnectionId"/>.
    ///
    /// Deliberately sent to the GROUP with a <c>to</c> field the client filters on,
    /// not to <c>Clients.Client(targetConnectionId)</c>: a connection id is not a
    /// session-scoped capability, so relaying to an arbitrary one would let a caller
    /// push a payload at any connection whose id they learned. Group-scoped keeps the
    /// blast radius inside the session, and the extra recipients learn nothing new —
    /// they are already receiving every one of these ops live.
    ///
    /// The sender's identity is stamped server-side (connection id + user id) so a
    /// replay cannot claim to be from someone else.
    /// </summary>
    public Task SendState(string sessionId, string targetConnectionId, object payload)
        => Authorized.Contains(sessionId) && !string.IsNullOrEmpty(targetConnectionId)
            ? Clients.Group(Group(sessionId)).SendAsync("StateReplay", new
            {
                to = targetConnectionId,
                fromConnectionId = Context.ConnectionId,
                fromUserId = Context.UserIdentifier,
                payload,
            })
            : Task.CompletedTask;

    /// <summary>
    /// Server-side push (from MeetingRoomController) when host/model/status
    /// changes so late joiners and existing clients re-sync their room state.
    /// </summary>
    public static Task NotifyRoomChanged(IHubContext<MeetingHub> hub, Guid sessionId, object state)
        => hub.Clients.Group(Group(sessionId.ToString())).SendAsync("RoomChanged", state);
}
