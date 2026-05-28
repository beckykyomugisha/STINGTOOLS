namespace Planscape.Core.Interfaces;

/// <summary>
/// Pillar B (6B, T6) — condition/runtime-based maintenance. Watches a device's
/// cumulative runtime metric and auto-raises a preventive WorkOrder when the
/// hours since last service cross the service interval — telemetry-driven PPM
/// instead of a fixed calendar. Evaluated on the ingest path alongside rules.
/// </summary>
public interface ICbmPlanner
{
    Task EvaluateAsync(
        Guid projectId, IReadOnlyList<TelemetryReading> readings, CancellationToken ct = default);
}
