using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Planscape.API.Authorization;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 177 — accepts a batch of audit events from the plugin so the
/// SHA-256-chained tamper-evidence log written under
/// <c>&lt;project&gt;/_BIM_COORD/audit_log_*.jsonl</c> reaches the server
/// where it can be queried, retained, and verified centrally.
///
/// The plugin keeps its local chain (verified by <c>AuditLog.VerifyChain</c>);
/// this endpoint mirrors selected events into the server's
/// <see cref="Planscape.Core.Entities.AuditLog"/> table so cross-workstation
/// queries and SOC2 reports see the union, not just what the server itself
/// generated.
///
/// Trust model: the plugin runs as the JWT user; we attribute every event
/// to that user and ignore any conflicting <c>userId</c> claim in the body.
/// The server is authoritative for tenant + user; the plugin is
/// authoritative for action/entity/details/timestamp.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/audit-events")]
[Authorize]
[ProjectAccess]
[EnableRateLimiting("mobile")]
public class AuditEventsController : ControllerBase
{
    private const int MaxBatch = 200;

    private readonly PlanscapeDbContext _db;
    private readonly ILogger<AuditEventsController> _log;

    public AuditEventsController(PlanscapeDbContext db, ILogger<AuditEventsController> log)
    {
        _db = db;
        _log = log;
    }

    [HttpPost("batch")]
    public async Task<ActionResult> PostBatch(Guid projectId, [FromBody] AuditEventBatch req)
    {
        if (req?.Events == null || req.Events.Count == 0)
            return BadRequest(new { message = "events array is required" });
        if (req.Events.Count > MaxBatch)
            return BadRequest(new { message = $"batch exceeds {MaxBatch} events" });

        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var t) ? t : Guid.Empty;
        if (tenantId == Guid.Empty) return Unauthorized();

        var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        var userId = Guid.TryParse(subClaim, out var u) ? (Guid?)u : null;

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var rows = new List<AuditLog>(req.Events.Count);
        foreach (var ev in req.Events)
        {
            if (string.IsNullOrWhiteSpace(ev.Action) || string.IsNullOrWhiteSpace(ev.EntityType))
                continue;

            rows.Add(new AuditLog
            {
                TenantId    = tenantId,
                ProjectId   = projectId,
                UserId      = userId,
                Action      = Truncate(ev.Action, 100),
                EntityType  = Truncate(ev.EntityType, 80),
                EntityId    = Truncate(ev.EntityId, 200),
                DetailsJson = Truncate(ev.DetailsJson, 8000),
                IpAddress   = ipAddress,
                Timestamp   = ev.Timestamp ?? DateTime.UtcNow,
                Source      = "plugin",
                DeviceId    = Truncate(ev.DeviceId, 100),
            });
        }

        if (rows.Count == 0)
            return BadRequest(new { message = "no valid events in batch" });

        _db.AuditLogs.AddRange(rows);
        await _db.SaveChangesAsync();

        return Ok(new { accepted = rows.Count, rejected = req.Events.Count - rows.Count });
    }

    private static string? Truncate(string? s, int max)
    {
        if (s == null) return null;
        return s.Length <= max ? s : s.Substring(0, max);
    }
}

public record AuditEventBatch(List<AuditEventDto> Events);

public record AuditEventDto(
    string    Action,
    string    EntityType,
    string?   EntityId,
    string?   DetailsJson,
    DateTime? Timestamp,
    string?   DeviceId);
