using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;
using Newtonsoft.Json;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// Feature gap 2/3 — BOQ snapshot push (plugin → cloud) and retrieval (mobile dashboard).
/// POST /api/projects/{id}/boq/snapshot  — push snapshot from plugin or IFC importer
/// GET  /api/projects/{id}/boq/snapshot  — latest snapshot + 30-day trend
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/boq")]
[Authorize]
[ProjectAccess]
public class BoqController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;

    public BoqController(PlanscapeDbContext db, IHubContext<NotificationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    // ── POST /api/projects/{projectId}/boq/snapshot ────────────────────────

    /// <summary>
    /// Push a new BOQ snapshot from the Revit plugin or server-side IFC import.
    /// Broadcasts a SignalR notification so connected mobile clients refresh automatically.
    /// </summary>
    [HttpPost("snapshot")]
    public async Task<ActionResult<BoqSnapshot>> PushSnapshot(
        Guid projectId,
        [FromBody] BoqSnapshotDto dto,
        CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var snapshot = new BoqSnapshot
        {
            ProjectId       = projectId,
            TenantId        = GetTenantId(),
            CreatedAt       = DateTime.UtcNow,
            CreatedByUserId = User.FindFirst("sub")?.Value ?? "plugin",
            SnapshotJson    = JsonConvert.SerializeObject(dto),
        };

        _db.BoqSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);

        // Notify connected mobile clients so the cost dashboard refreshes.
        await _hub.Clients.Group($"project:{projectId}")
            .SendAsync("BoqSnapshotUpdated", new
            {
                projectId,
                snapshotId     = snapshot.Id,
                totalEstimated = dto.TotalEstimated,
                totalActual    = dto.TotalActual,
                createdAt      = snapshot.CreatedAt,
            }, ct);

        return CreatedAtAction(nameof(GetSnapshot), new { projectId }, snapshot);
    }

    // ── GET /api/projects/{projectId}/boq/snapshot ─────────────────────────

    /// <summary>
    /// Returns the latest BOQ snapshot plus the 30-day trend (one row per day with a snapshot).
    /// </summary>
    [HttpGet("snapshot")]
    public async Task<ActionResult> GetSnapshot(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var cutoff   = DateTime.UtcNow.AddDays(-30);
        var snapshots = await _db.BoqSnapshots
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        if (snapshots.Count == 0)
            return Ok(new { latest = (object?)null, trend = Array.Empty<BoqTrendPoint>() });

        var latest     = snapshots.First();
        var latestDto  = JsonConvert.DeserializeObject<BoqSnapshotDto>(latest.SnapshotJson) ?? new BoqSnapshotDto();

        // Build trend: one point per snapshot, limited to last 30 days.
        var trend = snapshots
            .Where(s => s.CreatedAt >= cutoff)
            .Select(s =>
            {
                var d = JsonConvert.DeserializeObject<BoqSnapshotDto>(s.SnapshotJson) ?? new BoqSnapshotDto();
                return new BoqTrendPoint
                {
                    Date           = s.CreatedAt,
                    TotalEstimated = d.TotalEstimated,
                    TotalActual    = d.TotalActual,
                };
            })
            .OrderBy(t => t.Date)
            .ToList();

        return Ok(new
        {
            latest = new
            {
                id             = latest.Id,
                createdAt      = latest.CreatedAt,
                createdBy      = latest.CreatedByUserId,
                totalEstimated = latestDto.TotalEstimated,
                totalActual    = latestDto.TotalActual,
                disciplines    = latestDto.Disciplines,
            },
            trend,
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
        => await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == GetTenantId(), ct);

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}
