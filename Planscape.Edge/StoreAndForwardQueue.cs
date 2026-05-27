using Newtonsoft.Json;
using Planscape.Core.Interfaces;

namespace Planscape.Edge;

/// <summary>
/// 5B (T5) — disk-backed store-and-forward queue. Every enqueued batch is one
/// atomically-written file (temp + move) in the queue directory. The forwarder
/// drains oldest-first and deletes only on a confirmed 2xx, so a WAN cut just
/// accumulates files and replays them in order on reconnect — no telemetry
/// loss. One-file-per-batch keeps each write atomic and crash-safe (a partial
/// temp file is ignored).
/// </summary>
public sealed class StoreAndForwardQueue
{
    private readonly string _dir;
    private readonly object _lock = new();

    public StoreAndForwardQueue(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(_dir);
    }

    public Task EnqueueAsync(IReadOnlyList<TelemetryReading> readings)
    {
        if (readings.Count == 0) return Task.CompletedTask;
        var name = $"{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}.json";
        var finalPath = Path.Combine(_dir, name);
        var tmpPath = finalPath + ".tmp";
        var json = JsonConvert.SerializeObject(readings);
        lock (_lock)
        {
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, finalPath); // atomic publish
        }
        return Task.CompletedTask;
    }

    /// <summary>Oldest-first pending batch files (ignores half-written .tmp).</summary>
    public IReadOnlyList<string> PendingFiles(int maxFiles)
    {
        lock (_lock)
        {
            return Directory.EnumerateFiles(_dir, "*.json")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .Take(Math.Max(1, maxFiles))
                .ToList();
        }
    }

    public IReadOnlyList<TelemetryReading> ReadFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<List<TelemetryReading>>(json) ?? new();
        }
        catch
        {
            return new List<TelemetryReading>(); // corrupt file → drop on commit
        }
    }

    public void Commit(IEnumerable<string> files)
    {
        lock (_lock)
            foreach (var f in files)
                try { File.Delete(f); } catch { /* already gone */ }
    }

    public int PendingCount()
    {
        lock (_lock)
            return Directory.EnumerateFiles(_dir, "*.json").Count();
    }
}
