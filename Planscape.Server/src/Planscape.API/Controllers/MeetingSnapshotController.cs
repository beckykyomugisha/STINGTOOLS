using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Pillar A (3B, G13) — replayable meeting snapshots. Save the viewer state at
/// a decision point; list + restore later. Snapshot JSON is opaque to the
/// server (the viewer owns the shape).
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/meeting-sessions/{sessionId:guid}/snapshots")]
[Authorize]
public class MeetingSnapshotController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public MeetingSnapshotController(PlanscapeDbContext db) => _db = db;

    /// <summary>POST — capture a snapshot of the current viewer state.</summary>
    [HttpPost]
    public async Task<ActionResult<object>> Capture(
        Guid projectId, Guid sessionId, [FromBody] CaptureRequest req, CancellationToken ct)
    {
        var session = await _db.MeetingSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.ProjectId == projectId, ct);
        if (session is null) return NotFound();

        var snap = new MeetingSnapshot
        {
            TenantId = _db.CurrentTenantId,
            ProjectId = projectId,
            SessionId = sessionId,
            Label = req?.Label ?? "Snapshot",
            StateJson = string.IsNullOrWhiteSpace(req?.StateJson) ? "{}" : req!.StateJson,
            CapturedBy = User.Identity?.Name ?? "",
            CapturedByUserId = GetUserId(),
        };
        _db.MeetingSnapshots.Add(snap);
        await _db.SaveChangesAsync(ct);
        return Ok(new { snap.Id, snap.Label, snap.CapturedAt });
    }

    /// <summary>GET — list snapshots (newest first), without the JSON blob.</summary>
    [HttpGet]
    public async Task<ActionResult<object>> List(Guid projectId, Guid sessionId, CancellationToken ct)
    {
        var rows = await _db.MeetingSnapshots
            .Where(s => s.SessionId == sessionId && s.ProjectId == projectId)
            .OrderByDescending(s => s.CapturedAt)
            .Select(s => new { s.Id, s.Label, s.CapturedBy, s.CapturedAt })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>GET — one snapshot incl. its replay state JSON.</summary>
    [HttpGet("{snapshotId:guid}")]
    public async Task<ActionResult<object>> Get(
        Guid projectId, Guid sessionId, Guid snapshotId, CancellationToken ct)
    {
        var s = await _db.MeetingSnapshots.FirstOrDefaultAsync(
            x => x.Id == snapshotId && x.SessionId == sessionId && x.ProjectId == projectId, ct);
        return s is null
            ? NotFound()
            : Ok(new { s.Id, s.Label, s.CapturedBy, s.CapturedAt, s.StateJson });
    }

    private Guid? GetUserId()
    {
        var c = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return c != null && Guid.TryParse(c, out var id) ? id : null;
    }

    public class CaptureRequest { public string? Label { get; set; } public string? StateJson { get; set; } }
}
