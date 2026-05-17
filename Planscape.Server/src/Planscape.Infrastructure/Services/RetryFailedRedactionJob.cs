using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 178 (Gap 3) — Recurring Hangfire job that re-enqueues
/// <see cref="SitePhoto"/> rows whose <c>BlurStatus</c> is <c>"Failed"</c>
/// and whose <c>Audience</c> is still <c>"Approved"</c>.
///
/// A photo reaches this state when <see cref="RedactPublishedPhotoJob"/>
/// fails after all its Hangfire retries (e.g. ONNX model cold-start OOM,
/// transient storage unavailability). The recurring job gives those photos
/// a second chance every 4 hours without requiring manual admin re-approval.
///
/// To avoid re-processing quarantined crowd-shots (which have
/// <c>BlurStatus = "Failed"</c> intentionally), we additionally check that
/// <c>Audience == "Approved"</c> — quarantined photos remain at <c>Approved</c>
/// but are only ever retried here, never escalated to <c>ClientPortal</c>.
/// Admins can block a permanent retry by flipping <c>Audience</c> to
/// <c>"Rejected"</c> in the back-office panel.
///
/// Capped at 50 photos per run to prevent queue floods after a long storage
/// outage. Photos are ordered by <c>CapturedAt</c> ascending (oldest first).
/// </summary>
public class RetryFailedRedactionJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<RetryFailedRedactionJob> _log;

    public RetryFailedRedactionJob(
        PlanscapeDbContext db,
        IBackgroundJobClient jobs,
        ILogger<RetryFailedRedactionJob> log)
    {
        _db = db;
        _jobs = jobs;
        _log = log;
    }

    [Hangfire.AutomaticRetry(Attempts = 1, OnAttemptsExceeded = Hangfire.AttemptsExceededAction.Delete)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task RunAsync(CancellationToken ct)
    {
        // Hangfire has no HttpContext — bypass the global tenant filter
        // so the query sees all tenants' photos.
        _db.BypassTenantFilter = true;

        var failed = await _db.SitePhotos
            .Where(p => p.BlurStatus == "Failed" && p.Audience == "Approved")
            .OrderBy(p => p.CapturedAt)
            .Select(p => p.Id)
            .Take(50)
            .ToListAsync(ct);

        foreach (var id in failed)
        {
            _jobs.Enqueue<RedactPublishedPhotoJob>(j => j.RunAsync(id, CancellationToken.None));
            _log.LogInformation("RetryFailedRedactionJob: re-enqueued failed redaction for photo {PhotoId}", id);
        }

        _log.LogInformation(
            "RetryFailedRedactionJob: re-enqueued {Count} photos for redaction retry", failed.Count);
    }
}
