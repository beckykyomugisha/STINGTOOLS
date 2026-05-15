using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.BackgroundJobs;

/// <summary>
/// GAP-F — IFC BOQ extraction Hangfire job.
/// Replaces the fire-and-forget Task.Run in DocumentsController so failures are
/// automatically retried by Hangfire (up to 3 attempts). The job opens the
/// saved IFC file via IFileStorageService, runs IfcBoqExtractor, persists a
/// BoqSnapshot, and logs the result. Dead-lettering on final failure is handled
/// by Hangfire's AttemptsExceededAction.Delete policy.
/// </summary>
[AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
public class IfcBoqSeedJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly ILogger<IfcBoqSeedJob> _logger;
    private readonly INotificationService? _notifications;

    public IfcBoqSeedJob(
        PlanscapeDbContext   db,
        IFileStorageService  storage,
        ILogger<IfcBoqSeedJob> logger,
        INotificationService? notifications = null)
    {
        _db            = db;
        _storage       = storage;
        _logger        = logger;
        _notifications = notifications;
    }

    public async Task ExecuteAsync(
        Guid   projectId,
        string filePath,
        string uploadedByUserId)
    {
        _logger.LogInformation(
            "IfcBoqSeedJob started for project {ProjectId}, file {FilePath}", projectId, filePath);

        // Bypass the tenant filter — the job runs outside an HTTP request context.
        _db.BypassTenantFilter = true;

        var project = await _db.Projects.FindAsync([projectId]);
        if (project == null)
        {
            _logger.LogWarning("IfcBoqSeedJob: project {ProjectId} not found, skipping.", projectId);
            return;
        }

        // Open the IFC file from storage. Use bypassTenantCheck because this job
        // runs without an HTTP tenant context.
        var ifcStream = await _storage.GetAsync(filePath, bypassTenantCheck: true);
        if (ifcStream == null)
        {
            _logger.LogWarning("IfcBoqSeedJob: IFC file not found at {FilePath}, skipping.", filePath);
            return;
        }

        List<BoqLineItem> items;
        await using (ifcStream)
        {
            var extractor = new IfcBoqExtractor();
            items = extractor.Extract(ifcStream);
        }

        if (items.Count == 0)
        {
            _logger.LogInformation(
                "IfcBoqSeedJob: no quantity items found in {FilePath}.", filePath);
            return;
        }

        // Group into disciplines using the IfcElement type prefix (same logic as
        // the inline Task.Run in DocumentsController).
        var disciplineGroups = items
            .GroupBy(i => MapIfcTypeToDiscipline(i.ElementType))
            .Select(g => new BoqDisciplineRow
            {
                Discipline = g.Key,
                Items      = g.Count(),
                Estimated  = Math.Round(g.Sum(i => i.Value), 2),
                Actual     = 0,
            }).ToList();

        double totalEstimated = disciplineGroups.Sum(r => r.Estimated);

        var dto = new BoqSnapshotDto
        {
            TotalEstimated = totalEstimated,
            TotalActual    = 0,
            Disciplines    = disciplineGroups,
        };

        var snapshot = new BoqSnapshot
        {
            ProjectId       = projectId,
            TenantId        = project.TenantId,
            CreatedAt       = DateTime.UtcNow,
            CreatedByUserId = string.IsNullOrEmpty(uploadedByUserId)
                ? "system-ifc-import"
                : uploadedByUserId,
            SnapshotJson    = JsonConvert.SerializeObject(dto),
        };

        _db.BoqSnapshots.Add(snapshot);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "IfcBoqSeedJob: seeded BoqSnapshot {SnapshotId} for project {ProjectId} " +
            "({Items} items, estimated {Total:F2})",
            snapshot.Id, projectId, items.Count, totalEstimated);

        // Broadcast to connected mobile clients so the cost dashboard auto-refreshes
        // (same signal as BoqController.PushSnapshot sends after a plugin push).
        if (_notifications != null)
        {
            _ = _notifications.NotifyProjectAsync(
                projectId,
                "boq_snapshot_updated",
                "BOQ Snapshot Seeded",
                $"IFC BOQ extraction complete — {items.Count} items, estimated {totalEstimated:F2}.",
                new { projectId, source = "ifc_import", snapshotId = snapshot.Id },
                CancellationToken.None);
        }
    }

    // Mirror of DocumentsController.MapIfcTypeToDiscipline — kept in sync manually.
    private static string MapIfcTypeToDiscipline(string ifcTypeName)
    {
        if (string.IsNullOrEmpty(ifcTypeName)) return "General";

        return ifcTypeName.ToUpperInvariant() switch
        {
            var t when t.Contains("WALL") || t.Contains("SLAB") || t.Contains("BEAM")
                    || t.Contains("COLUMN") || t.Contains("STAIR") || t.Contains("RAMP")
                    || t.Contains("DOOR") || t.Contains("WINDOW") => "Structural/Arch",

            var t when t.Contains("PIPE") || t.Contains("DUCT") || t.Contains("FITTING")
                    || t.Contains("FLOW") || t.Contains("HVAC") => "Mechanical",

            var t when t.Contains("ELECTRIC") || t.Contains("CABLE")
                    || t.Contains("LIGHT") || t.Contains("SWITCH") => "Electrical",

            var t when t.Contains("PLUMB") || t.Contains("SANITARY")
                    || t.Contains("DRAIN") => "Plumbing",

            var t when t.Contains("FIRE") || t.Contains("SPRINKLER") => "Fire Protection",

            _ => "General",
        };
    }
}
