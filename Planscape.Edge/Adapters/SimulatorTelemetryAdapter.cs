using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Planscape.Core.Interfaces;

namespace Planscape.Edge.Adapters;

/// <summary>
/// Zero-dependency adapter that emits synthetic readings (random-walk around a
/// per-metric baseline). Lets you exercise the full edge → server → twin →
/// overlay pipeline without a broker or field devices.
/// </summary>
public sealed class SimulatorTelemetryAdapter : ITelemetryAdapter
{
    private readonly EdgeOptions _opt;
    private readonly ILogger<SimulatorTelemetryAdapter> _log;
    private readonly Random _rng = new();
    private readonly Dictionary<string, double> _last = new();
    private Task? _loop;

    public string Protocol => "simulator";

    public SimulatorTelemetryAdapter(IOptions<EdgeOptions> opt, ILogger<SimulatorTelemetryAdapter> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    public Task StartAsync(Func<TelemetryBatch, Task> onBatch, CancellationToken ct)
    {
        var cfg = _opt.Adapters.Simulator;
        var devices = cfg.Devices.Count > 0 ? cfg.Devices : new() { "SIM-01" };
        var metrics = cfg.Metrics.Count > 0 ? cfg.Metrics : new() { "supply_air_temp_c" };
        _log.LogInformation("Simulator: {Devices} devices × {Metrics} metrics every {Ms}ms",
            devices.Count, metrics.Count, cfg.IntervalMs);

        _loop = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var readings = new List<TelemetryReading>();
                foreach (var d in devices)
                    foreach (var m in metrics)
                    {
                        var key = $"{d}|{m}";
                        var baseline = Baseline(m);
                        var prev = _last.TryGetValue(key, out var v) ? v : baseline;
                        var next = prev + (_rng.NextDouble() - 0.5) * baseline * 0.05;
                        _last[key] = next;
                        readings.Add(new TelemetryReading(d, m, Math.Round(next, 3), Unit(m), DateTime.UtcNow));
                    }
                try { await onBatch(new TelemetryBatch(_opt.ProjectId, readings)); }
                catch (Exception ex) { _log.LogWarning(ex, "simulator onBatch failed"); }

                try { await Task.Delay(cfg.IntervalMs, ct); } catch (TaskCanceledException) { break; }
            }
        }, ct);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => _loop ?? Task.CompletedTask;

    private static double Baseline(string metric) => metric switch
    {
        "supply_air_temp_c" => 22,
        "co2_ppm"           => 600,
        "dhw_temp_c"        => 60,
        "room_pressure_pa"  => 10,
        "filter_dp_pa"      => 120,
        "power_kw"          => 15,
        "vibration_mm_s"    => 2,
        _                   => 50,
    };

    private static string Unit(string metric) => metric switch
    {
        "supply_air_temp_c" or "dhw_temp_c" => "°C",
        "co2_ppm"                            => "ppm",
        "room_pressure_pa" or "filter_dp_pa" => "Pa",
        "power_kw"                           => "kW",
        "vibration_mm_s"                     => "mm/s",
        _                                    => "",
    };
}
