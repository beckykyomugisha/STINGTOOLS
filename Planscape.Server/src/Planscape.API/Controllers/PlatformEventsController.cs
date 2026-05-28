using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// K2 keystone — append/drain endpoints for the platform event spine.
/// Any surface POSTs an event; the STING plugin GETs pending events on its
/// poll fallback (SignalR is the live path) and Ack/Rejects after applying.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/events")]
[Authorize]
public class PlatformEventsController : ControllerBase
{
    private readonly IPlatformEventService _events;
    private readonly PlanscapeDbContext _db;

    public PlatformEventsController(IPlatformEventService events, PlanscapeDbContext db)
    {
        _events = events;
        _db = db;
    }

    /// <summary>POST /api/projects/{projectId}/events — append a cross-surface event.</summary>
    [HttpPost]
    public async Task<ActionResult<object>> Append(
        Guid projectId, [FromBody] AppendEventRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest("missing body");
        if (string.IsNullOrWhiteSpace(request.Type)) return BadRequest("type is required");
        if (string.IsNullOrWhiteSpace(request.Source)) return BadRequest("source is required");
        if (!await ProjectInTenant(projectId, ct)) return NotFound();

        var ev = await _events.AppendAsync(new PlatformEventAppend(
            projectId, request.Source.Trim(), request.Type.Trim(),
            request.PayloadJson ?? "{}", request.TargetIfcGlobalId,
            request.BaseRevisionId, GetUserId()), ct);

        return Ok(new { ev.Id, ev.Sequence, ev.RowHash, status = ev.Status.ToString() });
    }

    /// <summary>GET /api/projects/{projectId}/events/pending?sinceSeq=N — drain cursor.</summary>
    [HttpGet("pending")]
    public async Task<ActionResult<object>> Pending(
        Guid projectId, [FromQuery] long sinceSeq = 0, [FromQuery] int max = 200, CancellationToken ct = default)
    {
        if (!await ProjectInTenant(projectId, ct)) return NotFound();
        var rows = await _events.GetPendingAsync(projectId, sinceSeq, max, ct);
        return Ok(new
        {
            items = rows.Select(e => new
            {
                e.Id, e.Sequence, e.Source, e.Type, e.PayloadJson,
                e.TargetIfcGlobalId, e.BaseRevisionId, e.CreatedUtc,
            }),
            nextSeq = rows.Count > 0 ? rows[^1].Sequence : sinceSeq,
        });
    }

    /// <summary>POST /api/projects/{projectId}/events/{eventId}/ack — applied OK.</summary>
    [HttpPost("{eventId:guid}/ack")]
    public async Task<IActionResult> Ack(Guid projectId, Guid eventId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return NotFound();
        return await _events.AckAsync(eventId, ct) ? NoContent() : NotFound();
    }

    /// <summary>POST /api/projects/{projectId}/events/{eventId}/reject — stale base or handler error.</summary>
    [HttpPost("{eventId:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid projectId, Guid eventId, [FromBody] RejectEventRequest request, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return NotFound();
        var reason = request?.Reason ?? "rejected";
        return await _events.RejectAsync(eventId, reason, request?.Retryable ?? false, ct)
            ? NoContent() : NotFound();
    }

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        return await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
    }

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return claim != null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return claim != null && Guid.TryParse(claim, out var id) ? id : null;
    }

    public class AppendEventRequest
    {
        public string Source { get; set; } = "";
        public string Type { get; set; } = "";
        public string? PayloadJson { get; set; }
        public string? TargetIfcGlobalId { get; set; }
        public string? BaseRevisionId { get; set; }
    }

    public class RejectEventRequest
    {
        public string? Reason { get; set; }
        public bool Retryable { get; set; }
    }
}
