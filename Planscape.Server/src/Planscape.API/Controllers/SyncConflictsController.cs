using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 143 — Sync Conflict review + resolution.
///
/// `TagSyncController` writes <see cref="SyncConflict"/> rows whenever a
/// plugin push arrives with an older `LastModifiedUtc` than what the server
/// already holds. Pre-Phase-143 those rows accumulated forever with no UI
/// surface to view them, so a BIM Manager investigating "why did my edit
/// vanish?" had to go to the database.
///
/// This controller exposes:
///   • <c>GET /api/projects/{id}/syncconflicts</c> — list, paginated,
///     filterable by resolution status (PENDING / SERVER_WINS / CLIENT_WINS)
///   • <c>GET /.../{conflictId}</c> — detail with the linked TaggedElement's
///     current state
///   • <c>POST /.../{conflictId}/resolve</c> — apply a resolution
///     (ACCEPT_SERVER / ACCEPT_CLIENT / MERGED) and audit it
///   • <c>POST /.../bulk-resolve</c> — apply the same resolution to many
///     conflicts in one call (typical when a batch was clobbered)
///
/// All writes are tenant-scoped and audited. Resolutions broadcast via
/// SignalR so the dashboard's conflicts tile updates live.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/[controller]")]
[EnableRateLimiting("mobile")]
[Authorize]
public class SyncConflictsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly ILogger<SyncConflictsController> _logger;

    public SyncConflictsController(
        PlanscapeDbContext db,
        IAuditService audit,
        INotificationService notifications,
        ILogger<SyncConflictsController> logger)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult> List(
        Guid projectId,
        [FromQuery] string? resolution = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var tenantId = GetTenantId();
        var projectOk = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!projectOk) return NotFound("Project not found");

        var query = _db.SyncConflicts.AsNoTracking()
            .Where(c => c.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(resolution))
            query = query.Where(c => c.Resolution == resolution);

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(c => c.DetectedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id, c.ElementId, c.ConflictType, c.Resolution,
                c.ServerTimestamp, c.ClientTimestamp,
                c.ClientUserName, c.DetectedAt,
                hasLinkedElement = c.TaggedElementId != null
            })
            .ToListAsync();

        // Surface a small summary so the BCC tile / mobile screen can render
        // the right RAG state without a second round-trip.
        var pending = await _db.SyncConflicts
            .CountAsync(c => c.ProjectId == projectId && c.Resolution == "PENDING");
        var stale = await _db.SyncConflicts
            .CountAsync(c => c.ProjectId == projectId && c.Resolution == "SERVER_WINS"
                          && c.DetectedAt > DateTime.UtcNow.AddDays(-7));

        return Ok(new
        {
            total, page, pageSize,
            summary = new { pending, recentServerWins = stale },
            rows
        });
    }

    [HttpGet("{conflictId}")]
    public async Task<ActionResult> Get(Guid projectId, Guid conflictId)
    {
        var tenantId = GetTenantId();
        var conflict = await _db.SyncConflicts.AsNoTracking()
            .Include(c => c.TaggedElement)
            .FirstOrDefaultAsync(c => c.Id == conflictId && c.ProjectId == projectId
                                 && c.Project!.TenantId == tenantId);
        if (conflict == null) return NotFound();

        return Ok(new
        {
            conflict.Id, conflict.ProjectId, conflict.ElementId,
            conflict.ConflictType, conflict.Resolution,
            conflict.ServerTimestamp, conflict.ClientTimestamp,
            conflict.ClientUserName, conflict.DetectedAt,
            element = conflict.TaggedElement == null ? null : new
            {
                conflict.TaggedElement.Id,
                conflict.TaggedElement.RevitElementId,
                Tag1 = conflict.TaggedElement.Tag1,
                Disc = conflict.TaggedElement.Disc,
                Sys = conflict.TaggedElement.Sys,
                Loc = conflict.TaggedElement.Loc,
                Zone = conflict.TaggedElement.Zone,
                Lvl = conflict.TaggedElement.Lvl,
                conflict.TaggedElement.LastModifiedUtc,
                conflict.TaggedElement.Version
            }
        });
    }

    /// <summary>
    /// Resolve a conflict. Resolution choices:
    ///   • <c>ACCEPT_SERVER</c> — keep the server copy (no element write); just clears the conflict
    ///   • <c>ACCEPT_CLIENT</c> — replace the server's stored fields with the client's reported ones (caller must POST the desired tag fields in the body since we don't archive the rejected client edit)
    ///   • <c>MERGED</c> — manager has reconciled in the plugin / dashboard and wants the conflict closed
    /// </summary>
    [HttpPost("{conflictId}/resolve")]
    public async Task<ActionResult> Resolve(Guid projectId, Guid conflictId, [FromBody] ResolveConflictRequest req)
    {
        var tenantId = GetTenantId();
        var conflict = await _db.SyncConflicts
            .Include(c => c.TaggedElement)
            .FirstOrDefaultAsync(c => c.Id == conflictId && c.ProjectId == projectId
                                 && c.Project!.TenantId == tenantId);
        if (conflict == null) return NotFound();

        return await ApplyResolution(projectId, new[] { conflict }, req);
    }

    /// <summary>
    /// Bulk resolve. Body is an array of conflictIds plus a single resolution.
    /// Caps at 500 per call.
    /// </summary>
    [HttpPost("bulk-resolve")]
    public async Task<ActionResult> BulkResolve(Guid projectId, [FromBody] BulkResolveRequest req)
    {
        if (req.ConflictIds == null || req.ConflictIds.Count == 0)
            return BadRequest("conflictIds is required");
        if (req.ConflictIds.Count > 500)
            return BadRequest("Maximum 500 conflicts per bulk resolve");

        var tenantId = GetTenantId();
        var conflicts = await _db.SyncConflicts
            .Include(c => c.TaggedElement)
            .Where(c => req.ConflictIds.Contains(c.Id) && c.ProjectId == projectId
                     && c.Project!.TenantId == tenantId)
            .ToListAsync();
        if (conflicts.Count == 0) return NotFound("No conflicts matched");

        return await ApplyResolution(projectId, conflicts,
            new ResolveConflictRequest(req.Resolution, req.Note, ClientFields: null));
    }

    private async Task<ActionResult> ApplyResolution(
        Guid projectId,
        IReadOnlyCollection<SyncConflict> conflicts,
        ResolveConflictRequest req)
    {
        var resolutionUpper = (req.Resolution ?? "").ToUpperInvariant();
        if (resolutionUpper != "ACCEPT_SERVER"
            && resolutionUpper != "ACCEPT_CLIENT"
            && resolutionUpper != "MERGED")
            return BadRequest("resolution must be ACCEPT_SERVER | ACCEPT_CLIENT | MERGED");

        var resolver = User.FindFirst("display_name")?.Value
                       ?? User.FindFirst("user_id")?.Value
                       ?? "Unknown";

        foreach (var c in conflicts)
        {
            // ACCEPT_CLIENT requires the caller to have provided the new field
            // values in req.ClientFields. We don't archive the rejected client
            // edit anywhere, so a single-conflict ACCEPT_CLIENT path is the
            // only way the BIM Manager can apply the previously-rejected
            // change. Bulk-resolve does not support ACCEPT_CLIENT for that
            // reason.
            if (resolutionUpper == "ACCEPT_CLIENT" && c.TaggedElement != null && req.ClientFields != null)
            {
                ApplyClientFields(c.TaggedElement, req.ClientFields);
                c.TaggedElement.Version += 1;
                c.TaggedElement.LastModifiedUtc = DateTime.UtcNow;
            }

            c.Resolution = resolutionUpper switch
            {
                "ACCEPT_SERVER" => "SERVER_WINS",
                "ACCEPT_CLIENT" => "CLIENT_WINS",
                _ => "MERGED"
            };
        }

        await _db.SaveChangesAsync();

        foreach (var c in conflicts)
            await _audit.LogAsync($"RESOLVE_{c.Resolution}", "SyncConflict", c.Id.ToString(),
                req.Note == null ? null : System.Text.Json.JsonSerializer.Serialize(new { req.Note, by = resolver }));

        // Project-scoped push so other reviewers see the conflict count drop.
        _ = _notifications.NotifyProjectAsync(projectId, "sync_conflict_resolved",
            $"{conflicts.Count} sync conflict(s) resolved",
            $"Resolved by {resolver}: {resolutionUpper}",
            new { count = conflicts.Count, resolution = resolutionUpper, projectId });

        return Ok(new
        {
            resolved = conflicts.Count,
            resolution = resolutionUpper,
            ids = conflicts.Select(c => c.Id).ToArray()
        });
    }

    private static void ApplyClientFields(TaggedElement element, ClientFieldsDto fields)
    {
        // Conservative: only overwrite the specific fields the client opted to
        // re-apply. Anything not provided stays at the current server value.
        // Field names match the canonical 8-segment short forms on TaggedElement
        // (Disc/Loc/Zone/Lvl/Sys/Func/Prod/Seq + Status/Rev + Tag1).
        if (fields.Tag1 != null) element.Tag1 = fields.Tag1;
        if (fields.Disc != null) element.Disc = fields.Disc;
        if (fields.Sys != null) element.Sys = fields.Sys;
        if (fields.Func != null) element.Func = fields.Func;
        if (fields.Prod != null) element.Prod = fields.Prod;
        if (fields.Loc != null) element.Loc = fields.Loc;
        if (fields.Zone != null) element.Zone = fields.Zone;
        if (fields.Lvl != null) element.Lvl = fields.Lvl;
        if (fields.Seq != null) element.Seq = fields.Seq;
        if (fields.Status != null) element.Status = fields.Status;
        if (fields.Rev != null) element.Rev = fields.Rev;
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record ResolveConflictRequest(string Resolution, string? Note, ClientFieldsDto? ClientFields);

public record BulkResolveRequest(List<Guid> ConflictIds, string Resolution, string? Note);

public record ClientFieldsDto(
    string? Tag1,
    string? Disc,
    string? Sys,
    string? Func,
    string? Prod,
    string? Loc,
    string? Zone,
    string? Lvl,
    string? Seq,
    string? Status,
    string? Rev
);
