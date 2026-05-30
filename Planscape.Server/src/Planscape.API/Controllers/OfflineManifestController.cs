using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Mobile offline 3D cache manifest: the mobile app pushes its cached
/// scene-chunk inventory after a sync so the server can drive incremental
/// delta delivery and mark stale chunks.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/offline-manifest")]
[Authorize]
public class OfflineManifestController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public OfflineManifestController(PlanscapeDbContext db) => _db = db;

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirst("tenant_id")?.Value
            ?? throw new InvalidOperationException("tenant_id claim missing"));

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("userId claim missing"));

    // ── Push manifest (mobile → server) ──────────────────────────────────

    /// <summary>
    /// The mobile app calls this after each model sync to register which
    /// scene chunks it has cached locally.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> PushManifest(Guid projectId,
        [FromBody] PushManifestRequest req)
    {
        var tenantId = GetTenantId();
        var userId   = GetUserId();

        // Validate model exists
        var model = await _db.ProjectModels
            .FirstOrDefaultAsync(m => m.Id == req.ProjectModelId && m.ProjectId == projectId);
        if (model is null) return NotFound("ProjectModel not found.");

        // Upsert by (UserId, DeviceId, ProjectModelId, SceneNodeId)
        var incomingNodeIds = req.Entries.Select(e => e.SceneNodeId).ToHashSet();

        var existing = await _db.MobileOfflineModelManifests
            .Where(m => m.UserId == userId && m.DeviceId == req.DeviceId
                     && m.ProjectModelId == req.ProjectModelId && m.TenantId == tenantId)
            .ToListAsync();

        var existingMap = existing.ToDictionary(e => e.SceneNodeId);

        var toAdd    = new List<MobileOfflineModelManifest>();
        var staleDelta = new List<Guid>();

        // Batch-load all scene nodes in one query to avoid N+1 round-trips.
        var incomingNodeIdList = req.Entries.Select(e => e.SceneNodeId).Distinct().ToList();
        var sceneNodeHashMap = await _db.SceneNodes
            .Where(s => incomingNodeIdList.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.ContentHash ?? "");

        foreach (var entry in req.Entries)
        {
            sceneNodeHashMap.TryGetValue(entry.SceneNodeId, out var serverHash);
            serverHash ??= "";
            var isStale    = !string.IsNullOrEmpty(serverHash) && serverHash != entry.ContentHash;

            if (existingMap.TryGetValue(entry.SceneNodeId, out var row))
            {
                row.ContentHash    = entry.ContentHash;
                row.CachedSizeBytes = entry.CachedSizeBytes;
                row.Format         = entry.Format ?? "Glb";
                row.Tier           = entry.Tier   ?? "Active";
                row.LastAccessedAt = DateTime.UtcNow;
                row.LastSyncedAt   = DateTime.UtcNow;
                row.IsStale        = isStale;
            }
            else
            {
                toAdd.Add(new MobileOfflineModelManifest
                {
                    TenantId        = tenantId,
                    ProjectId       = projectId,
                    UserId          = userId,
                    ProjectModelId  = req.ProjectModelId,
                    DeviceId        = req.DeviceId,
                    SceneNodeId     = entry.SceneNodeId,
                    ContentHash     = entry.ContentHash,
                    CachedSizeBytes = entry.CachedSizeBytes,
                    Format          = entry.Format ?? "Glb",
                    Tier            = entry.Tier   ?? "Active",
                    IsStale         = isStale,
                });
                if (isStale) staleDelta.Add(entry.SceneNodeId);
            }
        }

        // Mark rows for scene nodes no longer cached as evicted (remove)
        var removedNodeIds = existingMap.Keys.Except(incomingNodeIds).ToList();
        var toRemove = existing.Where(e => removedNodeIds.Contains(e.SceneNodeId)).ToList();
        _db.MobileOfflineModelManifests.RemoveRange(toRemove);
        _db.MobileOfflineModelManifests.AddRange(toAdd);

        await _db.SaveChangesAsync();

        var allStale = await _db.MobileOfflineModelManifests
            .Where(m => m.UserId == userId && m.DeviceId == req.DeviceId
                     && m.ProjectModelId == req.ProjectModelId && m.TenantId == tenantId
                     && m.IsStale)
            .Select(m => m.SceneNodeId)
            .ToListAsync();

        return Ok(new
        {
            added       = toAdd.Count,
            updated     = existing.Count - toRemove.Count,
            evicted     = toRemove.Count,
            staleChunks = allStale,
        });
    }

    // ── Pull manifest (server → mobile, what needs re-download) ──────────

    [HttpGet]
    public async Task<ActionResult> GetManifest(Guid projectId,
        [FromQuery] Guid projectModelId, [FromQuery] string deviceId)
    {
        var tenantId = GetTenantId();
        var userId   = GetUserId();

        var manifest = await _db.MobileOfflineModelManifests
            .Where(m => m.UserId == userId && m.DeviceId == deviceId
                     && m.ProjectModelId == projectModelId && m.TenantId == tenantId)
            .OrderBy(m => m.Tier).ThenBy(m => m.LastAccessedAt)
            .Select(m => new
            {
                m.SceneNodeId,
                m.ContentHash,
                m.CachedSizeBytes,
                m.Format,
                m.Tier,
                m.IsStale,
                m.LastAccessedAt,
                m.LastSyncedAt,
            })
            .ToListAsync();

        var totalBytes = manifest.Sum(m => m.CachedSizeBytes);
        var staleCount = manifest.Count(m => m.IsStale);

        return Ok(new
        {
            projectModelId,
            deviceId,
            chunkCount   = manifest.Count,
            totalBytes,
            staleCount,
            entries      = manifest,
        });
    }

    // ── Mark stale (server-side invalidation) ────────────────────────────

    [HttpPost("invalidate")]
    public async Task<ActionResult> Invalidate(Guid projectId,
        [FromBody] InvalidateManifestRequest req)
    {
        var tenantId = GetTenantId();

        // Mark all manifests for these scene nodes as stale across all devices
        var affected = await _db.MobileOfflineModelManifests
            .Where(m => m.ProjectId == projectId && m.TenantId == tenantId
                     && req.SceneNodeIds.Contains(m.SceneNodeId))
            .ToListAsync();

        foreach (var entry in affected)
            entry.IsStale = true;

        await _db.SaveChangesAsync();
        return Ok(new { invalidated = affected.Count });
    }

    // ── Device storage summary ────────────────────────────────────────────

    [HttpGet("storage-summary")]
    public async Task<ActionResult> GetStorageSummary(Guid projectId)
    {
        var tenantId = GetTenantId();
        var userId   = GetUserId();

        var summary = await _db.MobileOfflineModelManifests
            .Where(m => m.UserId == userId && m.ProjectId == projectId && m.TenantId == tenantId)
            .GroupBy(m => new { m.DeviceId, m.ProjectModelId, m.Tier })
            .Select(g => new
            {
                g.Key.DeviceId,
                g.Key.ProjectModelId,
                g.Key.Tier,
                Chunks     = g.Count(),
                TotalBytes = g.Sum(m => m.CachedSizeBytes),
                StaleCount = g.Count(m => m.IsStale),
            })
            .ToListAsync();
        return Ok(summary);
    }
}

public record ManifestEntry(
    Guid   SceneNodeId,
    string ContentHash,
    long   CachedSizeBytes,
    string? Format,
    string? Tier);

public record PushManifestRequest(
    Guid   ProjectModelId,
    string DeviceId,
    List<ManifestEntry> Entries);

public record InvalidateManifestRequest(List<Guid> SceneNodeIds);
