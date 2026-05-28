namespace Planscape.Core.Interfaces;

/// <summary>
/// Pillar D / 6C (T7) — continuous healthcare compliance evidence from
/// telemetry. Pressure-regime telemetry on a provisioned room device writes a
/// HealthcarePressureLog row (HTM 03-01), turning the model into a live
/// evidence source instead of a periodic manual survey. No-op for readings
/// whose device carries no regime metadata.
/// </summary>
public interface IHealthcareComplianceFeed
{
    Task RecordAsync(
        Guid projectId, IReadOnlyList<TelemetryReading> readings, CancellationToken ct = default);
}
