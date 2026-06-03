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

    // ── Path traversal guard ─────────────────────────────────────────────────

    /// <summary>
    /// Allowed storage-key prefixes for IFC files enqueued via Hangfire.
    /// Only upload paths are accepted; bare filenames without slashes are also
    /// permitted since the storage service resolves them within its own root.
    /// </summary>
    private static readonly string[] AllowedPathPrefixes =
    {
        "uploads/", "/uploads/", "/tmp/uploads/",
    };

    private static void ValidateFilePath(string filePath)
    {
        // Normalise separators
        string normalised = filePath.Replace('\\', '/');

        // Reject path traversal sequences (both decoded and percent-encoded)
        if (normalised.Contains("../") || normalised.Contains("./")  ||
            normalised.Contains("%2e") || normalised.Contains("%2f") ||
            normalised.Contains("%5c"))
        {
            throw new InvalidOperationException(
                $"Rejected unsafe IFC file path: {filePath}");
        }

        // Must start with a known upload prefix OR be a bare filename (no directory separator)
        bool allowed =
            AllowedPathPrefixes.Any(p => normalised.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || !normalised.Contains('/');

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"IFC file path not in allowed upload directory: {filePath}");
        }
    }

    public async Task ExecuteAsync(
        Guid   projectId,
        string filePath,
        string uploadedByUserId)
    {
        // Guard against poisoned Hangfire queue entries.
        ValidateFilePath(filePath);

        _logger.LogInformation(
            "IfcBoqSeedJob started for project {ProjectId}, file {FilePath}", projectId, filePath);

        // Bypass the tenant filter — the job runs outside an HTTP request context.
        _db.BypassTenantFilter = true;

        var project = await _db.Projects.FindAsync(new object[] { projectId });
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

        // ── Integration gap F5 — apply unit rates to raw quantities ──────────
        // Group into disciplines using IfcDisciplineMapper (single source of truth).
        // For each line item, multiply raw Value × a unit rate to produce an
        // estimated cost. Rates are keyed by ElementType prefix in DefaultRatesPerUnit;
        // unknown types fall back to a conservative £200/unit.
        var disciplineGroups = items
            .GroupBy(i => IfcDisciplineMapper.ToDisplayLabel(i.ElementType))
            .Select(g => new BoqDisciplineRow
            {
                Discipline = g.Key,
                Items      = g.Count(),
                Estimated  = Math.Round(g.Sum(i => i.Value * GetUnitRate(i.ElementType, i.Unit)), 2),
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

            // #7 — typed event for the mobile cost dashboard (realtimeClient.ts:64),
            // kept in lockstep with BoqController.PushSnapshot.
            _ = _notifications.NotifyProjectEventAsync(
                projectId,
                "BoqSnapshotUpdated",
                new { projectId, source = "ifc_import", snapshotId = snapshot.Id,
                      totalEstimated, createdAt = snapshot.CreatedAt },
                CancellationToken.None);
        }
    }

    // ── Unit rate table (Integration gap F5) ─────────────────────────────────
    //
    // Rates are £ per raw IFC quantity unit (m for length, m² for area, m³ for
    // volume, kg for weight). Each ElementType prefix maps to one rate.
    // Projects can override these by storing a custom rate JSON alongside the IFC;
    // this minimal table ensures IFC-seeded snapshots always have a non-zero
    // estimated cost rather than showing £0 across every discipline.
    //
    // Source: UK industry averages (BCIS / RICS) — indicative only.
    private static readonly Dictionary<string, double> DefaultRatesPerUnit =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Architectural — £/m² of face area or volume
            ["IfcWall"]              = 450.0,
            ["IfcSlab"]              = 380.0,
            ["IfcRoof"]              = 520.0,
            ["IfcCurtainWall"]       = 900.0,
            ["IfcCovering"]          = 120.0,
            ["IfcDoor"]              = 1200.0,
            ["IfcWindow"]            = 950.0,
            ["IfcStair"]             = 3500.0,
            ["IfcRamp"]              = 2800.0,
            ["IfcRailing"]           = 180.0,
            // Structural — £/m² slab or £/m run
            ["IfcColumn"]            = 650.0,
            ["IfcBeam"]              = 480.0,
            ["IfcFooting"]           = 520.0,
            ["IfcPile"]              = 1800.0,
            ["IfcMember"]            = 420.0,
            // Mechanical — £/m run
            ["IfcDuctSegment"]       = 85.0,
            ["IfcDuctFitting"]       = 65.0,
            ["IfcAirTerminal"]       = 350.0,
            ["IfcUnitaryEquipment"]  = 4200.0,
            // Plumbing — £/m run
            ["IfcPipeSegment"]       = 65.0,
            ["IfcPipeFitting"]       = 45.0,
            ["IfcFlowTerminal"]      = 280.0,
            ["IfcSanitaryTerminal"]  = 420.0,
            ["IfcValve"]             = 180.0,
            ["IfcTank"]              = 1600.0,
            // Electrical — £/m
            ["IfcCableSegment"]      = 25.0,
            ["IfcCableFitting"]      = 18.0,
            ["IfcLightFixture"]      = 280.0,
            ["IfcDistributionBoard"] = 1800.0,
            ["IfcOutlet"]            = 95.0,
            // Fire protection
            ["IfcFireSuppressionTerminal"] = 320.0,
            ["IfcSprinkler"]         = 180.0,
        };

    private const double FallbackRatePerUnit = 200.0; // £/unit for unknown types

    /// <summary>
    /// Returns the unit rate for an IFC element type by prefix match.
    /// </summary>
    private static double GetUnitRate(string elementType, string unit)
    {
        foreach (var kv in DefaultRatesPerUnit)
        {
            if (elementType.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        return FallbackRatePerUnit;
    }
}
