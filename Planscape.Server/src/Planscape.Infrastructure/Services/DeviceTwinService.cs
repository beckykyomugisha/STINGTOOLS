using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>Pillar B (5A) implementation of telemetry ingest + last-known-state.</summary>
public sealed class DeviceTwinService : IDeviceTwinService
{
    private readonly PlanscapeDbContext _db;
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(15);

    public DeviceTwinService(PlanscapeDbContext db) => _db = db;

    public async Task<DeviceTwin> EnsureTwinAsync(
        Guid projectId, string deviceId, string protocol = "mqtt", CancellationToken ct = default)
    {
        var twin = await _db.DeviceTwins
            .FirstOrDefaultAsync(t => t.ProjectId == projectId && t.DeviceId == deviceId, ct);
        if (twin is not null) return twin;

        twin = new DeviceTwin
        {
            TenantId = _db.CurrentTenantId,
            ProjectId = projectId,
            DeviceId = deviceId,
            Protocol = protocol,
            HealthState = "UNKNOWN",
        };
        _db.DeviceTwins.Add(twin);
        await _db.SaveChangesAsync(ct);
        return twin;
    }

    public async Task<IReadOnlyList<DeviceTwin>> IngestAsync(
        Guid projectId, IReadOnlyList<TelemetryReading> readings, CancellationToken ct = default)
    {
        if (readings.Count == 0) return Array.Empty<DeviceTwin>();
        var now = DateTime.UtcNow;
        var tenantId = _db.CurrentTenantId;

        // Persist raw points.
        foreach (var r in readings)
        {
            _db.TelemetryPoints.Add(new TelemetryPoint
            {
                TenantId = tenantId,
                ProjectId = projectId,
                DeviceId = r.DeviceId,
                Metric = r.Metric,
                Value = r.Value,
                Unit = r.Unit,
                Ts = r.Ts ?? now,
            });
        }

        // Refresh per-device last-known state.
        var deviceIds = readings.Select(r => r.DeviceId).Distinct().ToList();
        var twins = await _db.DeviceTwins
            .Where(t => t.ProjectId == projectId && deviceIds.Contains(t.DeviceId))
            .ToListAsync(ct);
        var byId = twins.ToDictionary(t => t.DeviceId);

        foreach (var grp in readings.GroupBy(r => r.DeviceId))
        {
            if (!byId.TryGetValue(grp.Key, out var twin))
            {
                twin = new DeviceTwin
                {
                    TenantId = tenantId, ProjectId = projectId, DeviceId = grp.Key,
                    Protocol = "mqtt", HealthState = "UNKNOWN",
                };
                _db.DeviceTwins.Add(twin);
                byId[grp.Key] = twin;
                twins.Add(twin);
            }

            var state = MergeState(twin.LastStateJson, grp);
            twin.LastStateJson = JsonConvert.SerializeObject(state);
            twin.LastSeenAt = now;
            // Rules (6A) own WARNING/ALARM; here we only lift OFFLINE/UNKNOWN to OK on fresh data.
            if (twin.HealthState is "UNKNOWN" or "OFFLINE") twin.HealthState = "OK";
        }

        await _db.SaveChangesAsync(ct);
        return twins;
    }

    private static Dictionary<string, object> MergeState(
        string? existingJson, IEnumerable<TelemetryReading> readings)
    {
        Dictionary<string, object> state;
        try
        {
            state = string.IsNullOrWhiteSpace(existingJson)
                ? new()
                : JsonConvert.DeserializeObject<Dictionary<string, object>>(existingJson) ?? new();
        }
        catch { state = new(); }

        // Last reading per metric wins.
        foreach (var r in readings.OrderBy(r => r.Ts ?? DateTime.UtcNow))
            state[r.Metric] = new { value = r.Value, unit = r.Unit, ts = (r.Ts ?? DateTime.UtcNow) };
        return state;
    }

    public async Task<IReadOnlyList<DeviceTwin>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        // AsNoTracking — the OFFLINE staleness below is a display-time derivation,
        // not a persisted state change; tracking would risk it being saved by an
        // unrelated SaveChanges later in the same request scope.
        var twins = await _db.DeviceTwins.AsNoTracking()
            .Where(t => t.ProjectId == projectId).ToListAsync(ct);
        var cutoff = DateTime.UtcNow - OfflineAfter;
        foreach (var t in twins)
            if (t.LastSeenAt is { } seen && seen < cutoff && t.HealthState != "OFFLINE")
                t.HealthState = "OFFLINE";
        return twins;
    }

    public Task<DeviceTwin?> GetAsync(Guid projectId, string deviceId, CancellationToken ct = default)
        => _db.DeviceTwins.FirstOrDefaultAsync(t => t.ProjectId == projectId && t.DeviceId == deviceId, ct);

    public async Task<IReadOnlyList<TelemetryPoint>> RecentAsync(
        Guid projectId, string deviceId, string metric, int max = 200, CancellationToken ct = default)
    {
        max = Math.Clamp(max, 1, 5000);
        return await _db.TelemetryPoints
            .Where(p => p.ProjectId == projectId && p.DeviceId == deviceId && p.Metric == metric)
            .OrderByDescending(p => p.Ts)
            .Take(max)
            .ToListAsync(ct);
    }

    public async Task SetHealthAsync(Guid projectId, string deviceId, string healthState, CancellationToken ct = default)
    {
        var twin = await _db.DeviceTwins
            .FirstOrDefaultAsync(t => t.ProjectId == projectId && t.DeviceId == deviceId, ct);
        if (twin is null) return;
        twin.HealthState = healthState;
        await _db.SaveChangesAsync(ct);
    }
}
