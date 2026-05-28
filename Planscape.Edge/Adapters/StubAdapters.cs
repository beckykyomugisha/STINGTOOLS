using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Edge.Adapters;

/// <summary>
/// 5C placeholders — BACnet/IP and Modbus. The contract + host wiring are in
/// place; the protocol clients (e.g. BACnet/IP via BACnet stack, Modbus via
/// NModbus) are the remaining named work. Enabling one logs a notice rather
/// than failing the host, so the edge still runs its other adapters.
/// </summary>
public sealed class BacnetTelemetryAdapter : ITelemetryAdapter
{
    private readonly ILogger<BacnetTelemetryAdapter> _log;
    public BacnetTelemetryAdapter(ILogger<BacnetTelemetryAdapter> log) => _log = log;
    public string Protocol => "bacnet";
    public Task StartAsync(Func<TelemetryBatch, Task> onBatch, CancellationToken ct)
    {
        _log.LogWarning("BACnet adapter enabled but not yet implemented — no points will be read. " +
                        "Wire a BACnet/IP client here producing TelemetryBatch via onBatch.");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

public sealed class ModbusTelemetryAdapter : ITelemetryAdapter
{
    private readonly ILogger<ModbusTelemetryAdapter> _log;
    public ModbusTelemetryAdapter(ILogger<ModbusTelemetryAdapter> log) => _log = log;
    public string Protocol => "modbus";
    public Task StartAsync(Func<TelemetryBatch, Task> onBatch, CancellationToken ct)
    {
        _log.LogWarning("Modbus adapter enabled but not yet implemented — no registers will be polled. " +
                        "Wire a Modbus TCP client (register map → metrics) here producing TelemetryBatch.");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
