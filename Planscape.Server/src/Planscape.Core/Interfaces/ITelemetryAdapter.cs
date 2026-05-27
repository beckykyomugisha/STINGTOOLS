namespace Planscape.Core.Interfaces;

/// <summary>
/// Pillar B (T4 / 5C) — protocol adapter contract. v1 ingest is HTTP
/// (TelemetryIngestController); MQTT/BACnet/Modbus adapters (and the
/// Planscape.Edge agent) implement this and feed the SAME
/// <see cref="IDeviceTwinService.IngestAsync"/> path, so adding a protocol is
/// zero core change. Adapters are long-running and resolve scoped services per
/// batch (see hosted-service registration).
/// </summary>
public interface ITelemetryAdapter
{
    /// <summary>mqtt | bacnet | modbus | opcua.</summary>
    string Protocol { get; }

    /// <summary>Begin receiving; invoke <paramref name="onBatch"/> per decoded batch.</summary>
    Task StartAsync(Func<TelemetryBatch, Task> onBatch, CancellationToken ct);

    Task StopAsync(CancellationToken ct);
}

/// <summary>A decoded batch from an adapter, tagged with its project.</summary>
public sealed record TelemetryBatch(Guid ProjectId, IReadOnlyList<TelemetryReading> Readings);
