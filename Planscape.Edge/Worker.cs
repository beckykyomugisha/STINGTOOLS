using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Planscape.Core.Interfaces;

namespace Planscape.Edge;

/// <summary>
/// 5B host loop. Starts each enabled protocol adapter (its decoded batches are
/// enqueued to the durable queue, never sent inline) and runs a forward loop
/// that drains the queue to the server, deleting batches only on success.
/// Adapter ingest and server forwarding are decoupled by the queue — that
/// decoupling is what makes the edge offline-safe.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly IEnumerable<ITelemetryAdapter> _adapters;
    private readonly StoreAndForwardQueue _queue;
    private readonly ServerForwarder _forwarder;
    private readonly EdgeOptions _opt;
    private readonly ILogger<Worker> _log;

    public Worker(
        IEnumerable<ITelemetryAdapter> adapters,
        StoreAndForwardQueue queue,
        ServerForwarder forwarder,
        IOptions<EdgeOptions> opt,
        ILogger<Worker> log)
    {
        _adapters = adapters;
        _queue = queue;
        _forwarder = forwarder;
        _opt = opt.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("Planscape.Edge → {Server} project {Project}; {Pending} batches buffered",
            _opt.ServerUrl, _opt.ProjectId, _queue.PendingCount());

        foreach (var adapter in _adapters.Where(a => IsEnabled(a.Protocol)))
        {
            try
            {
                await adapter.StartAsync(b => _queue.EnqueueAsync(b.Readings), ct);
                _log.LogInformation("Started adapter: {Protocol}", adapter.Protocol);
            }
            catch (Exception ex) { _log.LogError(ex, "Adapter {Protocol} failed to start", adapter.Protocol); }
        }

        while (!ct.IsCancellationRequested)
        {
            try { await DrainAsync(ct); }
            catch (Exception ex) { _log.LogWarning(ex, "drain pass failed"); }
            try { await Task.Delay(_opt.ForwardIntervalMs, ct); } catch (TaskCanceledException) { break; }
        }

        foreach (var adapter in _adapters)
            try { await adapter.StopAsync(CancellationToken.None); } catch { }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        // Drain the whole backlog each pass so a reconnect catches up fast.
        while (!ct.IsCancellationRequested)
        {
            var files = _queue.PendingFiles(maxFiles: 50);
            if (files.Count == 0) return;

            var batch = new List<TelemetryReading>();
            var consumed = new List<string>();
            foreach (var f in files)
            {
                batch.AddRange(_queue.ReadFile(f));
                consumed.Add(f);
                if (batch.Count >= _opt.ForwardBatchSize) break;
            }

            if (await _forwarder.ForwardAsync(batch, ct))
                _queue.Commit(consumed);
            else
                return; // server unreachable — keep backlog, retry next tick
        }
    }

    private bool IsEnabled(string protocol) => protocol switch
    {
        "simulator" => _opt.Adapters.Simulator.Enabled,
        "mqtt"      => _opt.Adapters.Mqtt.Enabled,
        "modbus"    => _opt.Adapters.Modbus.Enabled,
        "bacnet"    => _opt.Adapters.Bacnet.Enabled,
        _           => false,
    };
}
