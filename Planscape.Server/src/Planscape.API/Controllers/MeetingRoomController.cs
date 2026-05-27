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

    public MeetingRoomController(PlanscapeDbContext db, IHubContext<MeetingHub> hub)
    {
        _db = db;
        _hub = hub;
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
        await _db.SaveChangesAsync(ct);
        await MeetingHub.NotifyRoomChanged(_hub, sessionId, ToDto(session));
        return NoContent();
    }

    private static object ToDto(MeetingSession s) => new
    {
        s.Id, s.ProjectId, s.MeetingId, s.HostUserId, s.ModelId,
        s.BaseRevisionId, s.Status, s.CreatedAt, s.EndedAt,
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
}
