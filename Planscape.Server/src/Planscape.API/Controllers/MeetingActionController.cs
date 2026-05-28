using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Pillar A 3C + Pillar C loop 1 (G12) — turns a meeting decision into a
/// durable, model-bound action. A deferred clash becomes a CLASH
/// <see cref="BimIssue"/> AND a K2 PlatformEvent (type "issue.created") that
/// the STING drainer applies in Revit — closing the meeting → issue → model
/// loop through the single spine. The event carries the session's
/// BaseRevisionId so the drainer's conflict guard rejects a stale write-back.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/meeting-sessions/{sessionId:guid}")]
[Authorize]
public class MeetingActionController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IPlatformEventService _events;

    public MeetingActionController(PlanscapeDbContext db, IPlatformEventService events)
    {
        _db = db;
        _events = events;
    }

    /// <summary>POST .../defer-clash — raise a CLASH issue + emit it onto the spine.</summary>
    [HttpPost("defer-clash")]
    public async Task<ActionResult<object>> DeferClash(
        Guid projectId, Guid sessionId, [FromBody] DeferClashRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("title is required");

        var session = await _db.MeetingSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.ProjectId == projectId, ct);
        if (session is null) return NotFound();

        var n = await _db.Issues.CountAsync(i => i.ProjectId == projectId && i.Type == "CLASH", ct);
        var issue = new BimIssue
        {
            TenantId = _db.CurrentTenantId,
            ProjectId = projectId,
            IssueCode = $"CLASH-{n + 1:D4}",
            Type = "CLASH",
            Title = req.Title,
            Description = req.Description,
            Priority = string.IsNullOrWhiteSpace(req.Priority) ? "MEDIUM" : req.Priority,
            Status = "OPEN",
            AssigneeUserId = req.AssigneeUserId,
            CreatedBy = User.Identity?.Name ?? "",
            CreatedByUserId = GetUserId(),
            Source = "meeting",
            Discipline = req.Discipline,
            Revision = session.BaseRevisionId,
            ModelId = session.ModelId,
            ModelElementGuid = req.TargetIfcGlobalId,
        };
        _db.Issues.Add(issue);
        await _db.SaveChangesAsync(ct);

        // Onto the spine — the STING drainer applies this in Revit (idempotent,
        // conflict-guarded by BaseRevisionId).
        var payload = JsonConvert.SerializeObject(new
        {
            issueId = issue.Id,
            issueCode = issue.IssueCode,
            type = issue.Type,
            title = issue.Title,
            description = issue.Description,
            priority = issue.Priority,
            sessionId,
            clashRef = req.ClashRef,
        });
        var ev = await _events.AppendAsync(new PlatformEventAppend(
            ProjectId: projectId,
            Source: "meeting",
            Type: "issue.created",
            PayloadJson: payload,
            TargetIfcGlobalId: req.TargetIfcGlobalId,
            BaseRevisionId: session.BaseRevisionId,
            ActorUserId: GetUserId()), ct);

        return Ok(new
        {
            issue.Id, issue.IssueCode, issue.Type, issue.Status,
            eventId = ev.Id, eventSequence = ev.Sequence,
        });
    }

    private Guid? GetUserId()
    {
        var c = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return c != null && Guid.TryParse(c, out var id) ? id : null;
    }

    public class DeferClashRequest
    {
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string? Priority { get; set; }
        public string? Discipline { get; set; }
        public string? TargetIfcGlobalId { get; set; }
        public Guid? AssigneeUserId { get; set; }
        public string? ClashRef { get; set; }
    }
}
