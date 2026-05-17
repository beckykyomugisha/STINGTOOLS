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
///
/// Hardening:
///   • Every persisted Action is normalised to a <c>plugin.</c> prefix so a
///     malicious plugin can't smuggle a row that looks like a server-side
///     event (<c>admin_login</c>, <c>token_revoked</c>, …).
///   • EntityType is whitelisted to a known set; unknown types are rejected
///     so an attacker can't pollute the audit table with arbitrary "types".
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/audit-events")]
[Authorize]
[ProjectAccess]
[EnableRateLimiting("mobile")]
public class AuditEventsController : ControllerBase
{
    private const int MaxBatch = 200;

    // Phase 177 — only these EntityTypes are accepted from the plugin. Anything
    // else (e.g. "AppUser", "AuditLog") is dropped silently and counted as
    // rejected so the metric reveals abuse attempts.
    private static readonly HashSet<string> AllowedEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Deliverable", "Document", "Transmittal", "Workflow", "Meeting",
    };

    // Action namespace forced on every plugin-sourced row.
    private const string PluginActionPrefix = "plugin.";

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
        var rejected = 0;
        var now = DateTime.UtcNow;
        var floor = now.AddDays(-30);   // backdating older than 30 days is dropped
        var ceil  = now.AddMinutes(5);  // small clock-skew window forward

        foreach (var ev in req.Events)
        {
            if (string.IsNullOrWhiteSpace(ev.Action) || string.IsNullOrWhiteSpace(ev.EntityType))
            { rejected++; continue; }
            if (!AllowedEntityTypes.Contains(ev.EntityType))
            { rejected++; continue; }

            // Force the plugin. namespace — strip any pre-existing prefix so
            // an attacker can't smuggle "admin_login" or "token_revoked".
            var rawAction = ev.Action.Trim();
            if (rawAction.StartsWith(PluginActionPrefix, StringComparison.OrdinalIgnoreCase))
                rawAction = rawAction.Substring(PluginActionPrefix.Length);
            var normalisedAction = PluginActionPrefix + rawAction;

            // Clamp the timestamp to a sane window so a backdated event can't
            // poison the audit chain ordering.
            var ts = ev.Timestamp ?? now;
            if (ts < floor || ts > ceil) ts = now;

            rows.Add(new AuditLog
            {
                TenantId    = tenantId,
                ProjectId   = projectId,
                UserId      = userId,
                Action      = Truncate(normalisedAction, 100)!,
                EntityType  = Truncate(ev.EntityType, 80)!,
                EntityId    = Truncate(ev.EntityId, 200),
                DetailsJson = Truncate(ev.DetailsJson, 8000),
                IpAddress   = ipAddress,
                Timestamp   = ts,
                Source      = "plugin",
                DeviceId    = Truncate(ev.DeviceId, 100),
            });
        }

        if (rows.Count == 0)
            return BadRequest(new { message = "no valid events in batch", rejected });

        _db.AuditLogs.AddRange(rows);
        await _db.SaveChangesAsync();

        if (rejected > 0)
            _log.LogWarning("AuditEvents: {Rejected}/{Total} plugin events rejected (entityType / clock window)",
                rejected, req.Events.Count);

        return Ok(new { accepted = rows.Count, rejected });
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
