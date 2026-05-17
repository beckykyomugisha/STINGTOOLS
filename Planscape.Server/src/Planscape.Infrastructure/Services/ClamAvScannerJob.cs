using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Security;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 175 audit P1-15 — promotes presigned-URL uploads from
/// <c>uploads/raw/...</c> to <c>safe/...</c> after a clean ClamAV
/// scan, or to <c>quarantine/...</c> on a hit. Polls the Documents
/// table for rows in ScanStatus = PENDING; the partial index
/// IX_Documents_ScanStatus_Pending makes the poll cheap.
///
/// Recommended schedule: every 30 seconds. The job picks up at most
/// 50 docs per run so a flood of uploads doesn't pin a worker for an
/// hour.
///
/// Uses BypassTenantFilter because Hangfire has no tenant context
/// (mirrors ComplianceCheckJob et al). Per-row TenantId on
/// DocumentRecord scopes the storage move and the SaveChanges write.
/// </summary>
public class ClamAvScannerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ClamAvScannerJob> _logger;

    private const int BatchSize = 50;
    private const string SafeKeyPrefix = "safe/";
    private const string QuarantineKeyPrefix = "quarantine/";
    private const string RawKeyPrefix = "uploads/raw/";

    public ClamAvScannerJob(IServiceScopeFactory scopeFactory, ILogger<ClamAvScannerJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;

        var pending = await db.Documents
            .Where(d => d.ScanStatus == "PENDING" && d.FilePath != null)
            .OrderBy(d => d.UploadedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        var scanner = scope.ServiceProvider.GetRequiredService<IClamAvScanner>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        int promoted = 0, quarantined = 0, errored = 0;
        foreach (var doc in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!doc.FilePath!.StartsWith(RawKeyPrefix, StringComparison.Ordinal))
                {
                    // Already promoted by an earlier run, or migrated
                    // from the legacy multipart path. Mark CLEAN and
                    // move on so the partial index doesn't keep
                    // pointing at it.
                    doc.ScanStatus = "CLEAN";
                    doc.ScanScannedAt = DateTime.UtcNow;
                    continue;
                }

                await using var stream = await storage.GetAsync(doc.FilePath, ct, bypassTenantCheck: true);
                if (stream == null)
                {
                    // Upload didn't actually land or was deleted; mark
                    // INFECTED with a synthetic threat so the API
                    // refuses the download instead of 404'ing forever.
                    doc.ScanStatus = "INFECTED";
                    doc.ScanScannedAt = DateTime.UtcNow;
                    doc.ScanThreatName = "MISSING_OBJECT";
                    errored++;
                    continue;
                }

                var result = await scanner.ScanStreamAsync(stream, ct);
                var destPrefix = result.IsClean ? SafeKeyPrefix : QuarantineKeyPrefix;
                var destKey = destPrefix + doc.FilePath.Substring(RawKeyPrefix.Length);

                await storage.MoveAsync(doc.FilePath, destKey, ct, bypassTenantCheck: true);

                doc.FilePath = destKey;
                doc.ScanScannedAt = DateTime.UtcNow;
                if (result.IsClean)
                {
                    doc.ScanStatus = "CLEAN";
                    promoted++;
                }
                else
                {
                    doc.ScanStatus = "INFECTED";
                    doc.ScanThreatName = result.ThreatName;
                    quarantined++;
                    _logger.LogWarning(
                        "ClamAV quarantined doc {DocId} ({FileName}): {Threat}",
                        doc.Id, doc.FileName, result.ThreatName);
                }
            }
            catch (Exception ex)
            {
                errored++;
                _logger.LogError(ex,
                    "ClamAV scan failed for doc {DocId} ({FileName}); will retry on next pass",
                    doc.Id, doc.FileName);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "ClamAvScannerJob: scanned={Scanned} promoted={Promoted} quarantined={Quarantined} errored={Errored}",
            pending.Count, promoted, quarantined, errored);
    }
}
