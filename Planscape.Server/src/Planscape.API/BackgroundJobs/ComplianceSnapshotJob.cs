using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.BackgroundJobs;

/// <summary>
/// Runs every 6 hours. For each project that had a sync in the last 6 hours,
/// computes a server-side compliance snapshot and stores it.
/// </summary>
public class ComplianceSnapshotJob
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<ComplianceSnapshotJob> _log;

    public ComplianceSnapshotJob(PlanscapeDbContext db, ILogger<ComplianceSnapshotJob> logger)
    {
        _db = db;
        _log = logger;
    }

    public async Task ExecuteAsync()
    {
        var cutoff = DateTime.UtcNow.AddHours(-6);
        var projects = await _db.Projects
            .Where(p => p.Status == ProjectStatus.Active && p.LastSyncAt >= cutoff)
            .ToListAsync();

        int saved = 0;
        foreach (var project in projects)
        {
            try
            {
                var q = _db.TaggedElements.Where(e => e.ProjectId == project.Id);
                int total    = await q.CountAsync();
                int tagged   = await q.CountAsync(e => !string.IsNullOrEmpty(e.Tag1));
                int resolved = await q.CountAsync(e => e.IsFullyResolved);
                int stale    = await q.CountAsync(e => e.IsStale);

                double pct = total > 0 ? tagged * 100.0 / total : 0;
                string rag = pct >= 80 ? "GREEN" : pct >= 50 ? "AMBER" : "RED";

                var byDisc = await q.Where(e => !string.IsNullOrEmpty(e.Disc))
                    .GroupBy(e => e.Disc!)
                    .Select(g => new { Key = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                var snap = new ComplianceSnapshot
                {
                    ProjectId         = project.Id,
                    CapturedAt        = DateTime.UtcNow,
                    CapturedBy        = "system",
                    TotalElements     = total,
                    TaggedComplete    = tagged,
                    Untagged          = total - tagged,
                    FullyResolved     = resolved,
                    StaleCount        = stale,
                    TagPercent        = Math.Round(pct, 1),
                    RagStatus         = rag,
                    ByDisciplineJson  = System.Text.Json.JsonSerializer.Serialize(byDisc)
                };
                _db.ComplianceSnapshots.Add(snap);

                // Update project cached metrics
                project.CompliancePercent = snap.TagPercent;
                project.RagStatus         = snap.RagStatus;
                project.TotalElements     = total;
                project.TaggedElements    = tagged;
                saved++;
            }
            catch (Exception ex)
            {
                _log.LogWarning("ComplianceSnapshotJob: project {Id} failed: {Msg}", project.Id, ex.Message);
            }
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("ComplianceSnapshotJob: {Count} snapshots saved", saved);
    }
}
