using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// Pillar A (3A) — live 3D meeting-viewer room management. Creates sessions,
/// tracks participants, assigns host, binds the model. High-frequency sync
/// (camera/highlight) goes over <see cref="MeetingHub"/>; this controller owns
/// the durable room state and pushes RoomChanged when host/model/status moves.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/meeting-sessions")]
[Authorize]
public class MeetingRoomController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<MeetingHub> _hub;
    private readonly IConfiguration _config;

    public MeetingRoomController(PlanscapeDbContext db, IHubContext<MeetingHub> hub, IConfiguration config)
    {
        _db = db;
        _hub = hub;
        _config = config;
    }

    /// <summary>POST — open a live session (creator becomes host + first participant).</summary>
    [HttpPost]
    public async Task<ActionResult<object>> Create(
        Guid projectId, [FromBody] CreateSessionRequest req, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return NotFound();
        var userId = GetUserId();

        var session = new MeetingSession
        {
            TenantId = _db.CurrentTenantId,
            ProjectId = projectId,
            MeetingId = req?.MeetingId,
            ModelId = req?.ModelId,
            BaseRevisionId = req?.BaseRevisionId,
            HostUserId = userId,
            Status = "ACTIVE",
            CreatedBy = User.Identity?.Name ?? "",
            CreatedByUserId = userId,
        };
        _db.MeetingSessions.Add(session);

        if (userId is { } uid)
        {
            _db.MeetingViewerParticipants.Add(new MeetingViewerParticipant
            {
                TenantId = _db.CurrentTenantId,
                SessionId = session.Id,
                UserId = uid,
                DisplayName = req?.DisplayName ?? User.Identity?.Name ?? "Host",
                IsHost = true,
                IsFollowingHost = false,
                Surface = req?.Surface ?? "web",
            });
        }
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(session));
    }

    /// <summary>GET — current room state.</summary>
    [HttpGet("{sessionId:guid}")]
    public async Task<ActionResult<object>> Get(Guid projectId, Guid sessionId, CancellationToken ct)
    {
        var s = await _db.MeetingSessions.FirstOrDefaultAsync(
            x => x.Id == sessionId && x.ProjectId == projectId, ct);
        return s is null ? NotFound() : Ok(ToDto(s));
    }

    /// <summary>GET — active participants.</summary>
    [HttpGet("{sessionId:guid}/participants")]
    public async Task<ActionResult<object>> Participants(Guid projectId, Guid sessionId, CancellationToken ct)
    {
        var rows = await _db.MeetingViewerParticipants
            .Where(p => p.SessionId == sessionId && p.LeftAt == null)
            .OrderBy(p => p.JoinedAt)
            .Select(p => new { p.UserId, p.DisplayName, p.IsHost, p.IsFollowingHost, p.Surface, p.JoinedAt })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>POST — join (idempotent upsert of the participant row).</summary>
    [HttpPost("{sessionId:guid}/join")]
    public async Task<ActionResult<object>> Join(
        Guid projectId, Guid sessionId, [FromBody] JoinSessionRequest req, CancellationToken ct)
    {
        var session = await _db.MeetingSessions.FirstOrDefaultAsync(
            x => x.Id == sessionId && x.ProjectId == projectId, ct);
        if (session is null) return NotFound();
        if (session.Status != "ACTIVE") return BadRequest("session ended");
        var userId = GetUserId();
        if (userId is not { } uid) return Unauthorized();

        var p = await _db.MeetingViewerParticipants
            .FirstOrDefaultAsync(x => x.SessionId == sessionId && x.UserId == uid, ct);
        if (p is null)
        {
            p = new MeetingViewerParticipant
            {
                TenantId = _db.CurrentTenantId,
                SessionId = sessionId,
                UserId = uid,
                DisplayName = req?.DisplayName ?? User.Identity?.Name ?? "Guest",
                IsHost = session.HostUserId == uid,
                IsFollowingHost = session.HostUserId != uid, // followers default to following
                Surface = req?.Surface ?? "web",
            };
            _db.MeetingViewerParticipants.Add(p);
        }
        else
        {
            p.LeftAt = null;
            p.LastSeenAt = DateTime.UtcNow;
            if (req?.DisplayName is { Length: > 0 }) p.DisplayName = req.DisplayName;
            if (req?.Surface is { Length: > 0 }) p.Surface = req.Surface;
        }
        await _db.SaveChangesAsync(ct);
        await MeetingHub.NotifyRoomChanged(_hub, sessionId, ToDto(session));
        return Ok(ToDto(session));
    }

    /// <summary>POST — leave (marks the participant row left).</summary>
    [HttpPost("{sessionId:guid}/leave")]
    public async Task<IActionResult> Leave(Guid projectId, Guid sessionId, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is not { } uid) return Unauthorized();
        var p = await _db.MeetingViewerParticipants
            .FirstOrDefaultAsync(x => x.SessionId == sessionId && x.UserId == uid && x.LeftAt == null, ct);
        if (p is null) return NoContent();
        p.LeftAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>POST — set host (their camera drives followers); pushes RoomChanged.</summary>
    [HttpPost("{sessionId:guid}/host")]
    public async Task<ActionResult<object>> SetHost(
        Guid projectId, Guid sessionId, [FromBody] SetHostRequest req, CancellationToken ct)
    {
        var session = await _db.MeetingSessions.FirstOrDefaultAsync(
            x => x.Id == sessionId && x.ProjectId == projectId, ct);
        if (session is null) return NotFound();
        if (req?.UserId is not { } newHost) return BadRequest("userId required");

        session.HostUserId = newHost;
        var participants = await _db.MeetingViewerParticipants
            .Where(p => p.SessionId == sessionId && p.LeftAt == null).ToListAsync(ct);
        foreach (var p in participants)
        {
            p.IsHost = p.UserId == newHost;
            if (p.UserId == newHost) p.IsFollowingHost = false;
        }
        await _db.SaveChangesAsync(ct);
        await MeetingHub.NotifyRoomChanged(_hub, sessionId, ToDto(session));
        return Ok(ToDto(session));
    }

    /// <summary>POST — bind/rebind the model + base revision the room is viewing.</summary>
    [HttpPost("{sessionId:guid}/bind-model")]
    public async Task<ActionResult<object>> BindModel(
        Guid projectId, Guid sessionId, [FromBody] BindModelRequest req, CancellationToken ct)
    {
        var session = await _db.MeetingSessions.FirstOrDefaultAsync(
            x => x.Id == sessionId && x.ProjectId == projectId, ct);
        if (session is null) return NotFound();
        session.ModelId = req?.ModelId;
        session.BaseRevisionId = req?.BaseRevisionId;
        await _db.SaveChangesAsync(ct);
        await MeetingHub.NotifyRoomChanged(_hub, sessionId, ToDto(session));
        return Ok(ToDto(session));
    }

    /// <summary>POST — end the session.</summary>
    [HttpPost("{sessionId:guid}/end")]
    public async Task<IActionResult> End(Guid projectId, Guid sessionId, CancellationToken ct)
    {
        var session = await _db.MeetingSessions.FirstOrDefaultAsync(
            x => x.Id == sessionId && x.ProjectId == projectId, ct);
        if (session is null) return NotFound();
        session.Status = "ENDED";
        session.EndedAt = DateTime.UtcNow;

        // N5 — when this session backs a formal Meeting, flow the live viewer roster
        // back as ATTENDANCE (so the BCC meeting record reflects who actually attended)
        // and complete the meeting. Action items + snapshots already flow live; minutes
        // are (re)generated via POST /meetings/{id}/export/minutes after end.
        if (session.MeetingId is { } mid)
        {
            var parts = await _db.MeetingViewerParticipants
                .Where(p => p.SessionId == sessionId)
                .GroupBy(p => p.UserId)
                .Select(g => new { UserId = g.Key, Name = g.Max(p => p.DisplayName) })
                .ToListAsync(ct);
            var existing = await _db.MeetingAttendees.Where(a => a.MeetingId == mid).ToListAsync(ct);
            foreach (var p in parts)
            {
                var row = existing.FirstOrDefault(a => a.UserId == p.UserId);
                if (row != null) { row.AttendanceStatus = "ATTENDED"; }
                else
                {
                    _db.MeetingAttendees.Add(new MeetingAttendee
                    {
                        TenantId = session.TenantId,
                        MeetingId = mid,
                        UserId = p.UserId,
                        Name = string.IsNullOrWhiteSpace(p.Name) ? "Participant" : p.Name,
                        Role = "ATTENDEE",
                        AttendanceStatus = "ATTENDED",
                    });
                }
            }
            var meeting = await _db.Meetings.FirstOrDefaultAsync(m => m.Id == mid, ct);
            if (meeting != null && meeting.Status == "IN_PROGRESS") meeting.Status = "COMPLETED";
        }

        await _db.SaveChangesAsync(ct);
        await MeetingHub.NotifyRoomChanged(_hub, sessionId, ToDto(session));
        return NoContent();
    }

    /// <summary>
    /// M4 — link this live session to a formal <see cref="Meeting"/> record
    /// (agenda / attendees / actions / minutes). Sets <c>MeetingSession.MeetingId</c>
    /// so decisions captured live (action items) and minutes generation target the
    /// right Meeting. Pushes RoomChanged so every client learns the link.
    /// </summary>
    [HttpPost("{sessionId:guid}/link-meeting")]
    public async Task<ActionResult<object>> LinkMeeting(
        Guid projectId, Guid sessionId, [FromBody] LinkMeetingRequest req, CancellationToken ct)
    {
        var session = await _db.MeetingSessions.FirstOrDefaultAsync(
            x => x.Id == sessionId && x.ProjectId == projectId, ct);
        if (session is null) return NotFound();
        if (req?.MeetingId is not { } mid) return BadRequest("meetingId required");
        var owned = await _db.Meetings.AnyAsync(m => m.Id == mid && m.ProjectId == projectId, ct);
        if (!owned) return BadRequest("meeting is not in this project");

        session.MeetingId = mid;
        await _db.SaveChangesAsync(ct);
        await MeetingHub.NotifyRoomChanged(_hub, sessionId, ToDto(session));
        return Ok(ToDto(session));
    }

    /// <summary>
    /// WS3 — set the active surface every client shows (model | document | screen)
    /// and broadcast it to the room. Document surface carries the shared doc id.
    /// </summary>
    [HttpPost("{sessionId:guid}/surface")]
    public async Task<ActionResult<object>> SetSurface(
        Guid projectId, Guid sessionId, [FromBody] SetSurfaceRequest req, CancellationToken ct)
    {
        var session = await _db.MeetingSessions.FirstOrDefaultAsync(
            x => x.Id == sessionId && x.ProjectId == projectId, ct);
        if (session is null) return NotFound();
        if (session.Status != "ACTIVE") return BadRequest("session ended");

        var surface = (req?.Surface ?? "model").ToLowerInvariant();
        if (surface != "model" && surface != "document" && surface != "screen")
            return BadRequest("surface must be model | document | screen");

        session.ActiveSurface = surface;
        session.ActiveDocumentId = surface == "document" ? req?.DocumentId : null;
        await _db.SaveChangesAsync(ct);

        await _hub.Clients.Group($"meeting:{sessionId}").SendAsync("SurfaceChanged",
            new { surface = session.ActiveSurface, documentId = session.ActiveDocumentId }, ct);
        return Ok(ToDto(session));
    }

    /// <summary>
    /// WS3 — mint a LiveKit access token so this participant can publish/subscribe
    /// camera + mic (and screen-share when host/presenter) in the LiveKit room for
    /// this session. Room = session id, identity = user id. Returns the token + the
    /// LiveKit server URL the client connects to. 501 when LiveKit isn't configured.
    /// </summary>
    [HttpPost("{sessionId:guid}/livekit-token")]
    public async Task<ActionResult<object>> LiveKitToken(
        Guid projectId, Guid sessionId, [FromBody] LiveKitTokenRequest? req, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return NotFound();

        var session = await _db.MeetingSessions.FirstOrDefaultAsync(
            x => x.Id == sessionId && x.ProjectId == projectId, ct);
        if (session is null) return NotFound();
        if (session.Status != "ACTIVE") return BadRequest("session ended");

        var url    = _config["LiveKit:Url"]       ?? _config["LIVEKIT_URL"];
        var apiKey = _config["LiveKit:ApiKey"]    ?? _config["LIVEKIT_API_KEY"];
        var secret = _config["LiveKit:ApiSecret"] ?? _config["LIVEKIT_API_SECRET"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secret))
            return StatusCode(501, new { error = "LiveKit is not configured (set LiveKit:Url / ApiKey / ApiSecret)" });

        var userId = GetUserId();
        if (userId is not { } uid) return Unauthorized();
        var identity = uid.ToString();

        // Screen-share is gated to the host (presenter). Everyone may publish cam+mic.
        var isPresenter = session.HostUserId == uid;
        var room = sessionId.ToString();
        var name = req?.DisplayName
                   ?? (await _db.MeetingViewerParticipants
                            .Where(p => p.SessionId == sessionId && p.UserId == uid)
                            .Select(p => p.DisplayName).FirstOrDefaultAsync(ct))
                   ?? User.Identity?.Name ?? "Guest";

        var token = LiveKitTokenFactory.Create(
            apiKey!, secret!, room, identity, name,
            canPublish: true, canSubscribe: true, allowScreenShare: isPresenter,
            ttl: TimeSpan.FromHours(4));

        return Ok(new { token, url, identity, room, isPresenter });
    }

    // ── N2 — meeting recording via LiveKit Egress (host-gated, consent-visible) ──

    /// <summary>POST — host starts recording the live room (LiveKit Egress → object store).
    /// 501 when egress/S3 isn't configured. Idempotent: returns the running recording if any.
    /// Broadcasts RecordingChanged so every client shows the consent "● REC" indicator.</summary>
    [HttpPost("{sessionId:guid}/recording/start")]
    public async Task<ActionResult<object>> StartRecording(
        Guid projectId, Guid sessionId, [FromBody] StartRecordingRequest? req, CancellationToken ct)
    {
        var session = await _db.MeetingSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.ProjectId == projectId, ct);
        if (session is null) return NotFound();
        if (session.Status != "ACTIVE") return BadRequest("session ended");
        var userId = GetUserId();
        if (session.HostUserId != userId) return Forbid();   // host-gated

        var egress = new LiveKitEgressClient(_config);
        if (!egress.IsConfigured)
            return StatusCode(501, new { error = "LiveKit Egress is not configured (set LiveKit:ServerUrl + LiveKit:Egress:S3:*)" });

        // Idempotent — one active recording per session.
        var existing = await _db.MeetingRecordings
            .FirstOrDefaultAsync(r => r.SessionId == sessionId && (r.Status == "ACTIVE" || r.Status == "STARTING"), ct);
        if (existing != null) return Ok(ToRecDto(existing));

        var audioOnly = req?.AudioOnly ?? false;
        var result = await egress.StartAsync(sessionId.ToString(), audioOnly, ct);
        if (result is null) return StatusCode(502, new { error = "egress start failed" });

        var rec = new MeetingRecording
        {
            TenantId = session.TenantId,
            ProjectId = projectId,
            SessionId = sessionId,
            MeetingId = session.MeetingId,
            EgressId = result.EgressId,
            Kind = audioOnly ? "audio-only" : "room-composite",
            Status = "ACTIVE",
            StorageKey = result.StorageKey,
            StartedBy = User.Identity?.Name ?? "",
            StartedByUserId = userId,
        };
        _db.MeetingRecordings.Add(rec);
        await _db.SaveChangesAsync(ct);
        await _hub.Clients.Group($"meeting:{sessionId}").SendAsync("RecordingChanged",
            new { recording = true, recordingId = rec.Id, kind = rec.Kind }, ct);
        return Ok(ToRecDto(rec));
    }

    /// <summary>POST — host stops the running recording. Broadcasts RecordingChanged (off).</summary>
    [HttpPost("{sessionId:guid}/recording/stop")]
    public async Task<ActionResult<object>> StopRecording(Guid projectId, Guid sessionId, CancellationToken ct)
    {
        var session = await _db.MeetingSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.ProjectId == projectId, ct);
        if (session is null) return NotFound();
        var userId = GetUserId();
        if (session.HostUserId != userId) return Forbid();

        var rec = await _db.MeetingRecordings
            .FirstOrDefaultAsync(r => r.SessionId == sessionId && (r.Status == "ACTIVE" || r.Status == "STARTING"), ct);
        if (rec is null) return NoContent();

        var egress = new LiveKitEgressClient(_config);
        if (egress.IsConfigured && !string.IsNullOrEmpty(rec.EgressId))
            await egress.StopAsync(rec.EgressId, ct);   // best-effort; webhook finalises file metadata

        rec.Status = "STOPPING";
        rec.EndedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _hub.Clients.Group($"meeting:{sessionId}").SendAsync("RecordingChanged",
            new { recording = false, recordingId = rec.Id }, ct);
        return Ok(ToRecDto(rec));
    }

    /// <summary>GET — the latest recording for this session (null when none).</summary>
    [HttpGet("{sessionId:guid}/recording")]
    public async Task<ActionResult<object>> GetRecording(Guid projectId, Guid sessionId, CancellationToken ct)
    {
        var owned = await _db.MeetingSessions.AnyAsync(x => x.Id == sessionId && x.ProjectId == projectId, ct);
        if (!owned) return NotFound();
        var rec = await _db.MeetingRecordings.Where(r => r.SessionId == sessionId)
            .OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct);
        return Ok(rec is null ? null : ToRecDto(rec));
    }

    public class StartRecordingRequest { public bool AudioOnly { get; set; } }

    private static object ToRecDto(MeetingRecording r) => new
    {
        r.Id, r.SessionId, r.MeetingId, r.EgressId, r.Kind, r.Status,
        r.StorageKey, r.FileName, r.FileSizeBytes, r.DurationSeconds,
        r.StartedBy, r.StartedAt, r.EndedAt, r.Error,
    };

    private static object ToDto(MeetingSession s) => new
    {
        s.Id, s.ProjectId, s.MeetingId, s.HostUserId, s.ModelId,
        s.BaseRevisionId, s.Status, s.CreatedAt, s.EndedAt,
        // Null (a row predating the column) ⇒ the default "model" surface — app logic,
        // not a DB constraint, so the column can stay nullable + non-breaking.
        ActiveSurface = s.ActiveSurface ?? "model",
        s.ActiveDocumentId,
    };

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
        => await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == GetTenantId(), ct);

    private Guid GetTenantId()
    {
        var c = User.FindFirst("tenant_id")?.Value;
        return c != null && Guid.TryParse(c, out var id) ? id : Guid.Empty;
    }

    private Guid? GetUserId()
    {
        var c = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return c != null && Guid.TryParse(c, out var id) ? id : null;
    }

    public class CreateSessionRequest
    {
        public Guid? MeetingId { get; set; }
        public Guid? ModelId { get; set; }
        public string? BaseRevisionId { get; set; }
        public string? DisplayName { get; set; }
        public string? Surface { get; set; }
    }
    public class JoinSessionRequest { public string? DisplayName { get; set; } public string? Surface { get; set; } }
    public class SetHostRequest { public Guid? UserId { get; set; } }
    public class BindModelRequest { public Guid? ModelId { get; set; } public string? BaseRevisionId { get; set; } }
    public class SetSurfaceRequest { public string? Surface { get; set; } public Guid? DocumentId { get; set; } }
    public class LiveKitTokenRequest { public string? DisplayName { get; set; } }
    public class LinkMeetingRequest { public Guid? MeetingId { get; set; } }
}
