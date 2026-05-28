using Planscape.Core.Entities;

namespace Planscape.Core.Interfaces;

/// <summary>
/// Pillar B (5A) — telemetry ingest + last-known-state. Stores time-series
/// points and maintains the DeviceTwin live snapshot (LastState, LastSeen,
/// HealthState) that drives the RAG overlay and mobile Live tab.
/// </summary>
public interface IDeviceTwinService
{
    /// <summary>Upsert a minimal twin row (idempotent on projectId+deviceId).</summary>
    Task<DeviceTwin> EnsureTwinAsync(
        Guid projectId, string deviceId, string protocol = "mqtt", CancellationToken ct = default);

    /// <summary>
    /// Ingest a batch: persist TelemetryPoints, refresh each twin's LastState +
    /// LastSeen. Returns the distinct twins touched (for SignalR fan-out + rule
    /// evaluation by the caller).
    /// </summary>
    Task<IReadOnlyList<DeviceTwin>> IngestAsync(
        Guid projectId, IReadOnlyList<TelemetryReading> readings, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceTwin>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task<DeviceTwin?> GetAsync(Guid projectId, string deviceId, CancellationToken ct = default);

    /// <summary>Recent points for one device+metric (sparkline / rule history).</summary>
    Task<IReadOnlyList<TelemetryPoint>> RecentAsync(
        Guid projectId, string deviceId, string metric, int max = 200, CancellationToken ct = default);

    /// <summary>Set a twin's health state (called by the rule engine on breach/clear).</summary>
    Task SetHealthAsync(Guid projectId, string deviceId, string healthState, CancellationToken ct = default);
}

/// <summary>One incoming telemetry reading (HTTP ingest or edge adapter).</summary>
public sealed record TelemetryReading(
    string DeviceId, string Metric, double Value, string? Unit = null, DateTime? Ts = null);
