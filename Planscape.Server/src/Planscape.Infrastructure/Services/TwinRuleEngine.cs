using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Pillar B (6A, T2) — the real <see cref="ITwinRuleEvaluator"/>. For each
/// incoming reading it matches enabled <see cref="TwinRule"/> rows (threshold
/// or anomaly), debounces by ConsecutiveBreaches, dedupes against an already-
/// OPEN alert, fires a <see cref="TwinAlert"/>, sets twin health, pushes the
/// alert over TwinHub, and (when configured) raises a work order onto the K2
/// spine. Recovery auto-resolves the OPEN alert and clears health to OK.
/// </summary>
public sealed class TwinRuleEngine : ITwinRuleEvaluator
{
    private readonly PlanscapeDbContext _db;
    private readonly IDeviceTwinService _twins;
    private readonly IWorkOrderAutomator _automator;
    private readonly IHubContext<TwinHub> _hub;

    public TwinRuleEngine(
        PlanscapeDbContext db, IDeviceTwinService twins,
        IWorkOrderAutomator automator, IHubContext<TwinHub> hub)
    {
        _db = db;
        _twins = twins;
        _automator = automator;
        _hub = hub;
    }

    public async Task EvaluateAsync(
        Guid projectId, IReadOnlyList<TelemetryReading> readings, CancellationToken ct = default)
    {
        var metrics = readings.Select(r => r.Metric).Distinct().ToList();
        var rules = await _db.TwinRules
            .Where(r => r.ProjectId == projectId && r.Enabled && metrics.Contains(r.Metric))
            .ToListAsync(ct);
        if (rules.Count == 0) return;

        // worst severity seen per device this batch → health
        var deviceWorst = new Dictionary<string, string>();
        var firedToAutomate = new List<TwinAlert>();

        foreach (var reading in readings)
        {
            foreach (var rule in rules.Where(r =>
                r.Metric == reading.Metric &&
                (string.IsNullOrEmpty(r.DeviceId) || r.DeviceId == reading.DeviceId)))
            {
                bool breach;
                double z = 0;
                if (rule.Operator == "anomaly")
                {
                    var hist = await HistoryAsync(projectId, reading.DeviceId, reading.Metric, ct);
                    breach = TwinAnomalyDetector.IsAnomaly(hist, reading.Value, rule.AnomalySigma, out z);
                }
                else
                {
                    breach = Breaches(rule.Operator, reading.Value, rule.Threshold)
                             && await ConsecutiveAsync(projectId, reading.DeviceId, rule, ct);
                }

                var existingOpen = await _db.TwinAlerts.FirstOrDefaultAsync(
                    a => a.ProjectId == projectId && a.DeviceId == reading.DeviceId
                         && a.RuleId == rule.Id && a.Status == "OPEN", ct);

                if (breach)
                {
                    if (existingOpen != null) continue; // already firing — debounce duplicates
                    var ifcGuid = await _db.DeviceTwins
                        .Where(t => t.ProjectId == projectId && t.DeviceId == reading.DeviceId)
                        .Select(t => t.IfcGlobalId).FirstOrDefaultAsync(ct);

                    var alert = new TwinAlert
                    {
                        TenantId = _db.CurrentTenantId,
                        ProjectId = projectId,
                        RuleId = rule.Id,
                        DeviceId = reading.DeviceId,
                        IfcGlobalId = ifcGuid,
                        Metric = reading.Metric,
                        Value = reading.Value,
                        Severity = rule.Severity,
                        Message = rule.Operator == "anomaly"
                            ? $"{reading.Metric} anomaly (z={z:F1}) on {reading.DeviceId}"
                            : $"{reading.Metric} {rule.Operator} {rule.Threshold} → {reading.Value} on {reading.DeviceId}",
                        Status = "OPEN",
                    };
                    _db.TwinAlerts.Add(alert);
                    deviceWorst[reading.DeviceId] = Worse(deviceWorst.GetValueOrDefault(reading.DeviceId, "OK"), rule.Severity);
                    if (rule.RaiseWorkOrder) firedToAutomate.Add(alert);

                    await _hub.Clients.Group($"twin:{projectId}")
                        .SendAsync("TwinAlert", new
                        {
                            alert.Id, alert.DeviceId, alert.Metric, alert.Value,
                            alert.Severity, alert.Message, alert.IfcGlobalId, alert.FiredAt,
                        }, ct);
                }
                else if (existingOpen != null)
                {
                    // Recovery — clear the alert.
                    existingOpen.Status = "RESOLVED";
                    existingOpen.ResolvedAt = DateTime.UtcNow;
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        // Health: set to worst fired severity; clear devices with no remaining open alerts to OK.
        foreach (var (deviceId, severity) in deviceWorst)
            await _twins.SetHealthAsync(projectId, deviceId, severity, ct);

        var clearedDevices = readings.Select(r => r.DeviceId).Distinct()
            .Where(d => !deviceWorst.ContainsKey(d)).ToList();
        foreach (var d in clearedDevices)
        {
            var stillOpen = await _db.TwinAlerts.AnyAsync(
                a => a.ProjectId == projectId && a.DeviceId == d && a.Status == "OPEN", ct);
            if (!stillOpen)
                await _twins.SetHealthAsync(projectId, d, "OK", ct);
        }

        foreach (var alert in firedToAutomate)
            await _automator.RaiseFromAlertAsync(projectId, alert, ct);
    }

    private async Task<bool> ConsecutiveAsync(Guid projectId, string deviceId, TwinRule rule, CancellationToken ct)
    {
        if (rule.ConsecutiveBreaches <= 1) return true;
        var recent = await _twins.RecentAsync(projectId, deviceId, rule.Metric, rule.ConsecutiveBreaches, ct);
        if (recent.Count < rule.ConsecutiveBreaches) return false;
        return recent.All(p => Breaches(rule.Operator, p.Value, rule.Threshold));
    }

    private async Task<IReadOnlyList<double>> HistoryAsync(
        Guid projectId, string deviceId, string metric, CancellationToken ct)
    {
        var pts = await _twins.RecentAsync(projectId, deviceId, metric, 64, ct);
        // RecentAsync is newest-first; the detector wants oldest-first.
        return pts.Select(p => p.Value).Reverse().ToList();
    }

    private static bool Breaches(string op, double value, double? threshold)
    {
        if (threshold is not { } t) return false;
        return op switch
        {
            "gt"  => value > t,
            "gte" => value >= t,
            "lt"  => value < t,
            "lte" => value <= t,
            "eq"  => Math.Abs(value - t) < 1e-9,
            "ne"  => Math.Abs(value - t) >= 1e-9,
            _     => false,
        };
    }

    private static string Worse(string a, string b)
    {
        int Rank(string s) => s switch { "ALARM" => 3, "WARNING" => 2, "OK" => 1, _ => 0 };
        return Rank(b) > Rank(a) ? b : a;
    }
}
