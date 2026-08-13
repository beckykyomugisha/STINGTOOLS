using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// SEQ counter synchronization — ensures unique sequence numbers across multiple Revit instances.
/// Uses max-per-key merge strategy matching the plugin's .planscape_seq.json sidecar pattern.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/seq")]
[Authorize]
[ProjectAccess]
public class SeqSyncController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ISequenceCounterService _counters;

    public SeqSyncController(PlanscapeDbContext db, ISequenceCounterService counters)
    {
        _db = db;
        _counters = counters;
    }

    /// <summary>
    /// Push SEQ counters from plugin — server keeps max per key.
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult> SyncCounters(Guid projectId, [FromBody] SeqSyncRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        var userName = User.FindFirst("display_name")?.Value ?? "Unknown";
        var merged = new List<string>();

        // Load all existing counters in one query (avoids N+1)
        var existingCounters = await _db.SeqCounters
            .Where(s => s.ProjectId == projectId)
            .ToDictionaryAsync(s => s.CounterKey);

        foreach (var (key, value) in req.Counters)
        {
            if (existingCounters.TryGetValue(key, out var existing))
            {
                if (value > existing.CurrentValue)
                {
                    existing.CurrentValue = value;
                    existing.UpdatedBy = userName;
                    existing.UpdatedAt = DateTime.UtcNow;
                    merged.Add(key);
                }
            }
            else
            {
                _db.SeqCounters.Add(new SeqCounter
                {
                    ProjectId = projectId,
                    CounterKey = key,
                    CurrentValue = value,
                    UpdatedBy = userName
                });
                merged.Add(key);
            }
        }

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId,
            Action = "seq_counters_synced",
            EntityType = "SeqCounter",
            DetailsJson = JsonSerializer.Serialize(new { mergedCount = merged.Count, total = req.Counters.Count }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok(new { merged, total = req.Counters.Count });
    }

    /// <summary>
    /// Atomically reserve a block of sequence numbers per counter key.
    ///
    /// WHY THIS EXISTS ALONGSIDE /sync: the sync path is a max-per-key MERGE. It
    /// is correct for the Revit plugin's model — each instance allocates locally
    /// against a document it holds, then reconciles — but it cannot make two
    /// independent writers safe on its own. Both can read the same high-water
    /// mark, both mint the same number, and the max-merge then happily accepts
    /// the higher of two identical values. Any client that has no local document
    /// to allocate against (StingBridge, CI, a headless importer) needs the
    /// counter bumped and read in a single indivisible step instead.
    ///
    /// Concurrency: a single INSERT … ON CONFLICT DO UPDATE … RETURNING per key.
    /// Postgres takes a row lock for the duration, so concurrent callers
    /// serialise on the row and each gets a disjoint block. Reading and then
    /// writing from application code would reintroduce exactly the race this
    /// endpoint removes.
    ///
    /// Returns, per key, the inclusive block [start, end] the caller now owns.
    /// Numbers are consumed whether or not the caller uses them — gaps are
    /// acceptable, duplicates are not.
    /// </summary>
    /// <response code="200">Reserved. Each entry is a block the caller owns exclusively.</response>
    /// <response code="400">A requested count was not between 1 and 10000.</response>
    [HttpPost("reserve")]
    public async Task<ActionResult> ReserveCounters(Guid projectId, [FromBody] SeqReserveRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        if (req?.Reservations == null || req.Reservations.Count == 0)
            return Ok(new { assignments = new Dictionary<string, object>() });

        // Bound the request: an unbounded count would let one caller exhaust the
        // 4-digit SEQ space for a key in a single call.
        foreach (var (key, count) in req.Reservations)
        {
            if (count < 1 || count > 10000)
                return BadRequest(new { message = $"Reservation count for '{key}' must be between 1 and 10000." });
            if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
                return BadRequest(new { message = "Counter keys must be non-empty and at most 200 characters." });
        }

        var userName = User.FindFirst("display_name")?.Value ?? "Unknown";
        var assignments = new Dictionary<string, object>();

        // Ordered purely so the response and audit log are reproducible for a
        // given request. (An earlier comment here claimed the ordering prevented
        // deadlocks between concurrent multi-key reservations — it does not.
        // Each AllocateAsync is a single autocommit UPSERT that takes and
        // releases its row lock before the next key is touched, so no
        // transaction ever holds two counter locks at once and there is no
        // cross-key lock cycle to order against.)
        foreach (var key in req.Reservations.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var count = req.Reservations[key];

            // Delegates to the shared counter service rather than re-issuing the
            // UPSERT by hand. Two reasons beyond deduplication: the service goes
            // through _db.Database.SqlQueryRaw, so the RLS DbConnectionInterceptor
            // fires — the old raw conn.OpenAsync() bypassed it entirely, which
            // would silently become a tenant-isolation hole the moment
            // Database:RlsEnabled is tightened; and its GREATEST(...) seedFloor
            // handling is the behaviour every other counter caller already gets.
            var newValue = await _counters.AllocateAsync(
                tenantId, projectId, key, seedFloor: 0, count: count, updatedBy: userName);
            assignments[key] = new
            {
                start = newValue - count + 1,
                end   = newValue,
                count
            };
        }

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId    = tenantId,
            ProjectId   = projectId,
            UserId      = userId,
            Action      = "seq_counters_reserved",
            EntityType  = "SeqCounter",
            DetailsJson = JsonSerializer.Serialize(new
            {
                keys = req.Reservations.Count,
                total = req.Reservations.Values.Sum()
            }),
            Timestamp = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return Ok(new { assignments });
    }

    /// <summary>
    /// Get all server SEQ counters for a project (plugin pulls on startup).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> GetCounters(Guid projectId)
    {
        var tenantId = GetTenantId();
        var counters = await _db.SeqCounters
            .Where(s => s.ProjectId == projectId && s.Project!.TenantId == tenantId)
            .Select(s => new { key = s.CounterKey, value = s.CurrentValue })
            .ToListAsync();

        return Ok(counters);
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record SeqSyncRequest
{
    public Dictionary<string, int> Counters { get; init; } = new();
}

public record SeqReserveRequest
{
    /// <summary>counter key → how many consecutive numbers to reserve.</summary>
    public Dictionary<string, int> Reservations { get; init; } = new();
}
