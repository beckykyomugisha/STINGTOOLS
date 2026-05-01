using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Distributed;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// S7.2 — burn-rate SLA alert job. Runs every 5 minutes; computes the
/// rolling-window error budget and fires a high-severity push to the
/// founder when burn rate exceeds the threshold.
///
/// SLO targets:
///   - 99.5% successful API responses (status 5xx counts as failure)
///   - P99 endpoint latency &lt; 2 s
///   - 99% successful tag sync (controller-side)
///
/// Burn-rate windows (Google SRE workbook pattern):
///   1-hour window  · alert at 14.4× burn (~5 min budget burn)
///   6-hour window  · alert at 6×    burn (~30 min budget burn)
///
/// 5xx count + total request count come from a tiny Redis bucket
/// updated by middleware. Until that's wired in, the job no-ops
/// gracefully — we ship the alerting framework now and the metrics
/// pipe in S7.2 follow-up.
/// </summary>
public class SlaBurnRateJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly INotificationService _notify;
    private readonly ILogger<SlaBurnRateJob> _logger;

    private const double SloTarget = 0.995;     // 99.5 %
    private const double Alert1hMultiplier = 14.4;
    private const double Alert6hMultiplier = 6.0;

    public SlaBurnRateJob(
        PlanscapeDbContext db,
        IDistributedCache cache,
        INotificationService notify,
        ILogger<SlaBurnRateJob> logger)
    {
        _db = db; _cache = cache; _notify = notify; _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var stats1h = await ReadWindowAsync("1h", ct);
        var stats6h = await ReadWindowAsync("6h", ct);
        if (stats1h.Total == 0 && stats6h.Total == 0)
        {
            _logger.LogDebug("SlaBurnRateJob: no metrics in cache yet; skipping.");
            return;
        }

        // Burn rate = (errorRate / errorBudget). Budget = 1 - SLO target.
        // For 99.5% target, budget = 0.005 (0.5%).
        var budget = 1 - SloTarget;
        var burn1h = stats1h.Total > 0 ? (stats1h.Errors / (double)stats1h.Total) / budget : 0;
        var burn6h = stats6h.Total > 0 ? (stats6h.Errors / (double)stats6h.Total) / budget : 0;

        if (burn1h >= Alert1hMultiplier)
            await AlertAsync("Critical: 1-h burn rate", burn1h, stats1h, ct);
        else if (burn6h >= Alert6hMultiplier)
            await AlertAsync("High: 6-h burn rate", burn6h, stats6h, ct);
    }

    private async Task<(long Total, long Errors)> ReadWindowAsync(string window, CancellationToken ct)
    {
        var totalKey  = $"sla:{window}:total";
        var errorKey  = $"sla:{window}:5xx";
        long.TryParse(await _cache.GetStringAsync(totalKey, ct) ?? "0", out var total);
        long.TryParse(await _cache.GetStringAsync(errorKey, ct) ?? "0", out var errors);
        return (total, errors);
    }

    private async Task AlertAsync(string title, double burn, (long Total, long Errors) stats, CancellationToken ct)
    {
        _logger.LogWarning("SLA burn alert: {Title} ({Burn}x) — {Errors}/{Total} 5xx", title, burn, stats.Errors, stats.Total);
        // Send to the founder tenant via SignalR + push. The platform
        // 'planscape' tenant receives operational alerts; tenant-scoped
        // tenants don't get cross-tenant noise.
        var founderTenantId = await PlatformTenantIdAsync();
        if (founderTenantId != Guid.Empty)
            await _notify.NotifyAsync(founderTenantId, "ops",
                title, $"Burn rate {burn:F1}× — {stats.Errors}/{stats.Total} 5xx in window. Investigate immediately.",
                null, ct);
    }

    private async Task<Guid> PlatformTenantIdAsync()
    {
        _db.BypassTenantFilter = true;
        var t = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == "planscape");
        return t?.Id ?? Guid.Empty;
    }
}
