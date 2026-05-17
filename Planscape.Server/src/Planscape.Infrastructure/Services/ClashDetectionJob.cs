// Pairwise AABB overlap detection between SceneNodes of different disciplines.
// Runs per-project on demand (POST /api/projects/{id}/clashes/run) and on a
// scheduled basis (Hangfire daily). NOT a true geometry clash — that requires
// Xbim.Geometry. This is a fast first-pass that catches obvious zone violations
// and flags them for manual review. Real geometry clash should layer on top.

namespace Planscape.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;

public interface IClashDetectionJob
{
    Task<ClashDetectionResult> RunAsync(Guid projectId, Guid tenantId, CancellationToken ct);
}

public sealed record ClashDetectionResult(
    int ScannedPairs,
    int ClashesFound,
    int ClashesNew,
    int ClashesUpdated,
    int CriticalClashes,
    TimeSpan Duration);

public sealed class ClashDetectionJob : IClashDetectionJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IClashAutomationService _automation;
    private readonly ILogger<ClashDetectionJob> _logger;

    public ClashDetectionJob(
        PlanscapeDbContext db,
        IClashAutomationService automation,
        ILogger<ClashDetectionJob> logger)
    { _db = db; _automation = automation; _logger = logger; }

    public async Task<ClashDetectionResult> RunAsync(Guid projectId, Guid tenantId, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Load all SceneNodes for the project, grouped by discipline
        var nodes = await _db.SceneNodes.AsNoTracking()
            .Where(n => n.ProjectId == projectId && n.DeletedAt == null)
            .ToListAsync(ct);

        // Group by discipline — only pair across disciplines (intra-discipline overlaps are expected)
        var byDisc = nodes.GroupBy(n => n.Discipline).ToList();

        // Track the ids of new clashes so the automation service can post-process them
        // (auto-issue, push notifications, webhook fan-out) after the main save commits.
        var newClashIds = new List<Guid>();

        int scanned = 0, found = 0, created = 0, updated = 0, critical = 0;

        // For each pair of disciplines, find AABB overlaps
        for (int i = 0; i < byDisc.Count; i++)
        {
            for (int j = i + 1; j < byDisc.Count; j++)
            {
                foreach (var a in byDisc[i])
                {
                    foreach (var b in byDisc[j])
                    {
                        scanned++;
                        if (!Overlaps(a, b)) continue;

                        // Compute overlap centre and volume
                        var cx = (Math.Max(a.MinX, b.MinX) + Math.Min(a.MaxX, b.MaxX)) / 2;
                        var cy = (Math.Max(a.MinY, b.MinY) + Math.Min(a.MaxY, b.MaxY)) / 2;
                        var cz = (Math.Max(a.MinZ, b.MinZ) + Math.Min(a.MaxZ, b.MaxZ)) / 2;

                        var dx = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
                        var dy = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
                        var dz = Math.Min(a.MaxZ, b.MaxZ) - Math.Max(a.MinZ, b.MinZ);
                        var volume = dx * dy * dz;
                        var depth = Math.Min(dx, Math.Min(dy, dz));

                        var severity = volume > 100_000_000 ? ClashSeverity.Critical
                                      : volume > 10_000_000 ? ClashSeverity.Major
                                      : volume > 1_000_000 ? ClashSeverity.Minor
                                      : ClashSeverity.Info;

                        var hash = ComputeHash(a.SourceModelId, a.Id, b.SourceModelId, b.Id);

                        var existing = await _db.ClashRecords
                            .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.ClashHash == hash, ct);

                        if (existing == null)
                        {
                            var clash = new ClashRecord
                            {
                                TenantId = tenantId,
                                ProjectId = projectId,
                                ClashHash = hash,
                                Kind = ClashKind.Hard,
                                Severity = severity,
                                Status = ClashStatus.New,
                                ModelAId = a.SourceModelId,
                                ElementAGuid = a.Id.ToString(),
                                DisciplineA = a.Discipline,
                                ModelBId = b.SourceModelId,
                                ElementBGuid = b.Id.ToString(),
                                DisciplineB = b.Discipline,
                                DistanceMm = depth,
                                CentreX = cx, CentreY = cy, CentreZ = cz,
                                OverlapVolumeMm3 = volume,
                                LevelCode = a.LevelCode ?? b.LevelCode,
                            };
                            _db.ClashRecords.Add(clash);
                            newClashIds.Add(clash.Id);
                            created++;
                            if (severity == ClashSeverity.Critical) critical++;
                        }
                        else if (existing.Status == ClashStatus.New)
                        {
                            // Refresh severity if recomputed
                            existing.Severity = severity;
                            existing.OverlapVolumeMm3 = volume;
                            existing.DistanceMm = depth;
                            updated++;
                        }
                        found++;
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        // Fire automation rules over the newly-created clashes — auto-issue,
        // push notifications, webhook fan-out. Best-effort: any failure here
        // logs but does NOT roll back the detection run, since the clash
        // records themselves are the authoritative output.
        if (newClashIds.Count > 0)
        {
            try
            {
                await _automation.ProcessNewClashesAsync(projectId, tenantId, newClashIds, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Clash automation post-processing failed for project {ProjectId} — clashes still saved, automation skipped",
                    projectId);
            }
        }

        sw.Stop();

        _logger.LogInformation("Clash detection: project {ProjectId} — {Scanned} pairs scanned, {Found} clashes, {Created} new, {Critical} critical in {Duration}ms",
            projectId, scanned, found, created, critical, sw.ElapsedMilliseconds);

        return new ClashDetectionResult(scanned, found, created, updated, critical, sw.Elapsed);
    }

    private static bool Overlaps(SceneNode a, SceneNode b)
        => a.MaxX > b.MinX && a.MinX < b.MaxX
        && a.MaxY > b.MinY && a.MinY < b.MaxY
        && a.MaxZ > b.MinZ && a.MinZ < b.MaxZ;

    private static string ComputeHash(Guid m1, Guid e1, Guid m2, Guid e2)
    {
        // Order-independent hash — clashes A↔B and B↔A are the same clash
        var ordered = string.CompareOrdinal(e1.ToString(), e2.ToString()) < 0
            ? $"{m1}|{e1}|{m2}|{e2}"
            : $"{m2}|{e2}|{m1}|{e1}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ordered));
        return Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 32);
    }
}
