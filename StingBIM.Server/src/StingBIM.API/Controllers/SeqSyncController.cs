using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StingBIM.Core.Entities;
using StingBIM.Infrastructure.Data;

namespace StingBIM.API.Controllers;

/// <summary>
/// SEQ counter synchronization — ensures unique sequence numbers across multiple Revit instances.
/// Uses max-per-key merge strategy matching the plugin's .sting_seq.json sidecar pattern.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/seq")]
[Authorize]
public class SeqSyncController : ControllerBase
{
    private readonly StingBimDbContext _db;

    public SeqSyncController(StingBimDbContext db) => _db = db;

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
        int merged = 0;

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
                    merged++;
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
                merged++;
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
            DetailsJson = JsonSerializer.Serialize(new { merged, total = req.Counters.Count }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok(new { merged, total = req.Counters.Count });
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
            .ToDictionaryAsync(s => s.CounterKey, s => s.CurrentValue);

        return Ok(counters);
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record SeqSyncRequest
{
    public Dictionary<string, int> Counters { get; init; } = new();
}
