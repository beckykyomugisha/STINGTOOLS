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
///
/// Hot path: runs on every telemetry batch, so it preloads the batch's open
/// alerts + twins (one query each), caches recent points per (device,metric),
/// and applies all alert/health/resolution mutations in a single SaveChanges —
/// no per-reading round-trips.
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

        var deviceIds = readings.Select(r => r.DeviceId).Distinct().ToList();

        // Preload (tracked so mutations persist on the single SaveChanges below).
        var twinById = (await _db.DeviceTwins
                .Where(t => t.ProjectId == projectId && deviceIds.Contains(t.DeviceId))
                .ToListAsync(ct))
            .ToDictionary(t => t.DeviceId);

        var openByKey = new Dictionary<(string, Guid?), TwinAlert>();
        foreach (var a in await _db.TwinAlerts
                     .Where(a => a.ProjectId == projectId && a.Status == "OPEN" && deviceIds.Contains(a.DeviceId))
                     .ToListAsync(ct))
            openByKey[(a.DeviceId, a.RuleId)] = a; // last-wins guards against accidental dupes

        // Cache recent points per (device,metric) so multiple rules on one metric
        // — and anomaly + consecutive checks — don't re-query.
        var recentCache = new Dictionary<(string, string), IReadOnlyList<TelemetryPoint>>();
        async Task<IReadOnlyList<TelemetryPoint>> Recent(string d, string m)
        {
            if (!recentCache.TryGetValue((d, m), out var v))
            {
                v = await _twins.RecentAsync(projectId, d, m, 64, ct);
                recentCache[(d, m)] = v;
            }
            return v;
        }

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
                    var pts = await Recent(reading.DeviceId, reading.Metric);
                    // Exclude the newest point: ingest already persisted the current
                    // reading, so including it would pull the EWMA baseline toward
                    // itself and mask the very anomaly we're testing for.
                    var hist = pts.Skip(1).Select(p => p.Value).Reverse().ToList();
                    breach = TwinAnomalyDetector.IsAnomaly(hist, reading.Value, rule.AnomalySigma, out z);
                }
                else
                {
                    breach = Breaches(rule.Operator, reading.Value, rule.Threshold);
                    if (breach && rule.ConsecutiveBreaches > 1)
                    {
                        var pts = await Recent(reading.DeviceId, reading.Metric);
                        breach = pts.Count >= rule.ConsecutiveBreaches
                            && pts.Take(rule.ConsecutiveBreaches)
                                  .All(p => Breaches(rule.Operator, p.Value, rule.Threshold));
                    }
                }

                openByKey.TryGetValue((reading.DeviceId, rule.Id), out var existingOpen);

                if (breach)
                {
                    if (existingOpen != null) continue; // already firing — debounce duplicates
                    twinById.TryGetValue(reading.DeviceId, out var twin);

                    var alert = new TwinAlert
                    {
                        TenantId = _db.CurrentTenantId,
                        ProjectId = projectId,
                        RuleId = rule.Id,
                        DeviceId = reading.DeviceId,
                        IfcGlobalId = twin?.IfcGlobalId,
                        Metric = reading.Metric,
                        Value = reading.Value,
                        Severity = rule.Severity,
                        Message = rule.Operator == "anomaly"
                            ? $"{reading.Metric} anomaly (z={z:F1}) on {reading.DeviceId}"
                            : $"{reading.Metric} {rule.Operator} {rule.Threshold} → {reading.Value} on {reading.DeviceId}",
                        Status = "OPEN",
                    };
                    _db.TwinAlerts.Add(alert);
                    openByKey[(reading.DeviceId, rule.Id)] = alert; // block dup fires within batch
                    deviceWorst[reading.DeviceId] = Worse(deviceWorst.GetValueOrDefault(reading.DeviceId, "OK"), rule.Severity);
                    if (rule.RaiseWorkOrder) firedToAutomate.Add(alert);

                    // Id is client-assigned (entity default), so safe to push pre-save.
                    await _hub.Clients.Group($"twin:{projectId}")
                        .SendAsync("TwinAlert", new
                        {
                            alert.Id, alert.DeviceId, alert.Metric, alert.Value,
                            alert.Severity, alert.Message, alert.IfcGlobalId, alert.FiredAt,
                        }, ct);
                }
                else if (existingOpen != null)
                {
                    existingOpen.Status = "RESOLVED";
                    existingOpen.ResolvedAt = DateTime.UtcNow;
                    openByKey.Remove((reading.DeviceId, rule.Id));
                }
            }
        }

        // Health roll-up (in-memory on preloaded tracked twins):
        //  • fired this batch → worst new severity
        //  • no open alerts remain → OK
        //  • still-open-but-not-fired → leave as-is
        var devicesWithOpen = openByKey.Keys.Select(k => k.Item1).ToHashSet();
        foreach (var deviceId in deviceIds)
        {
            if (!twinById.TryGetValue(deviceId, out var twin)) continue;
            if (deviceWorst.TryGetValue(deviceId, out var sev)) twin.HealthState = sev;
            else if (!devicesWithOpen.Contains(deviceId)) twin.HealthState = "OK";
        }

        await _db.SaveChangesAsync(ct);

        foreach (var alert in firedToAutomate)
            await _automator.RaiseFromAlertAsync(projectId, alert, ct);
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
