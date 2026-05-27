using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>Pillar D / 6C — telemetry → HealthcarePressureLog (HTM 03-01).</summary>
public sealed class HealthcareComplianceFeed : IHealthcareComplianceFeed
{
    private const string PressureMetric = "room_pressure_pa";
    private readonly PlanscapeDbContext _db;

    public HealthcareComplianceFeed(PlanscapeDbContext db) => _db = db;

    public async Task RecordAsync(
        Guid projectId, IReadOnlyList<TelemetryReading> readings, CancellationToken ct = default)
    {
        var pressure = readings.Where(r => r.Metric == PressureMetric).ToList();
        if (pressure.Count == 0) return;

        var deviceIds = pressure.Select(r => r.DeviceId).Distinct().ToList();
        var twins = await _db.DeviceTwins
            .Where(t => t.ProjectId == projectId && deviceIds.Contains(t.DeviceId))
            .ToListAsync(ct);
        var byId = twins.ToDictionary(t => t.DeviceId);

        var tenantId = _db.CurrentTenantId;
        bool wrote = false;
        foreach (var r in pressure)
        {
            if (!byId.TryGetValue(r.DeviceId, out var twin)) continue;
            var (regime, designDeltaPa) = ParseRegime(twin.MetadataJson);
            if (regime is null) continue; // only provisioned healthcare devices

            bool inBand = regime switch
            {
                "NEG" => r.Value <= 0,
                "POS" => r.Value >= 0,
                _     => Math.Abs(r.Value) <= 5,   // NEUTRAL
            };

            _db.HealthcarePressureLogs.Add(new HealthcarePressureLog
            {
                TenantId = tenantId,
                ProjectId = projectId,
                RoomBimId = twin.IfcGlobalId ?? twin.DeviceId,
                RoomName = twin.AssetTag ?? "",
                RoomClass = "",
                DesignRegime = regime,
                DesignDeltaPa = designDeltaPa ?? 0,
                LiveDeltaPa = r.Value,
                InBand = inBand,
                CapturedAt = r.Ts ?? DateTime.UtcNow,
                CapturedBy = "telemetry",
                Source = "TELEMETRY",
            });
            wrote = true;
        }
        if (wrote) await _db.SaveChangesAsync(ct);
    }

    private static (string? regime, double? designDeltaPa) ParseRegime(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return (null, null);
        try
        {
            var o = JObject.Parse(metadataJson);
            var regime = (string?)o["pressureRegime"];
            if (string.IsNullOrWhiteSpace(regime)) return (null, null);
            return (regime!.ToUpperInvariant(), (double?)o["designDeltaPa"]);
        }
        catch { return (null, null); }
    }
}
