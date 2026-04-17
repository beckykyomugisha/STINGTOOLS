using Newtonsoft.Json;
using Planscape.Shared.Models;

namespace Planscape.PluginSync;

/// <summary>
/// File-backed queue for offline operation. Stores sync payloads when server is unreachable.
/// Payloads are drained on next successful connection.
/// </summary>
public class OfflineQueue
{
    private readonly string _queueDir;
    private readonly object _lock = new();

    // ── Static facade ─────────────────────────────────────────────────────
    // The SyncScheduler sets a process-wide shared instance when it starts, so
    // plugin code (e.g. OnDocumentSaved hooks) can enqueue without holding a
    // direct reference to the scheduler. Null when the scheduler isn't running
    // — callers should null-check before enqueueing.
    private static OfflineQueue? _shared;
    public static OfflineQueue? Shared => _shared;
    internal static void SetShared(OfflineQueue? queue) => _shared = queue;

    public OfflineQueue(string queueDirectory)
    {
        _queueDir = queueDirectory;
        if (!Directory.Exists(_queueDir))
            Directory.CreateDirectory(_queueDir);
    }

    /// <summary>
    /// Enqueue a sync payload for later delivery.
    /// </summary>
    private const int MaxQueueFiles = 500; // Prevent unbounded growth

    public void Enqueue(PluginSyncPayload payload)
    {
        lock (_lock)
        {
            // Enforce queue size limit — drop oldest if full
            var existing = Directory.GetFiles(_queueDir, "sync_*.json").OrderBy(f => f).ToArray();
            if (existing.Length >= MaxQueueFiles)
            {
                for (int i = 0; i <= existing.Length - MaxQueueFiles; i++)
                    try { File.Delete(existing[i]); } catch (IOException) { }
            }

            string fileName = $"sync_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.json";
            string filePath = Path.Combine(_queueDir, fileName);
            File.WriteAllText(filePath, JsonConvert.SerializeObject(payload, Formatting.None));
        }
    }

    /// <summary>
    /// Get all queued payloads in chronological order.
    /// </summary>
    public List<(string FilePath, PluginSyncPayload Payload)> PeekAll()
    {
        var results = new List<(string, PluginSyncPayload)>();
        lock (_lock)
        {
            var files = Directory.GetFiles(_queueDir, "sync_*.json").OrderBy(f => f);
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var payload = JsonConvert.DeserializeObject<PluginSyncPayload>(json);
                    if (payload != null)
                        results.Add((file, payload));
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    // Skip corrupt or inaccessible files
                }
            }
        }
        return results;
    }

    /// <summary>
    /// Remove a successfully synced payload from the queue.
    /// </summary>
    public void Dequeue(string filePath)
    {
        lock (_lock)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    /// <summary>
    /// Get the number of queued payloads.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return Directory.Exists(_queueDir)
                    ? Directory.GetFiles(_queueDir, "sync_*.json").Length
                    : 0;
            }
        }
    }

    /// <summary>
    /// Drain all queued payloads through the sync client.
    /// Returns the number of successfully synced payloads.
    /// </summary>
    public async Task<int> DrainAsync(SyncClient client)
    {
        var queued = PeekAll();
        int synced = 0;

        foreach (var (filePath, payload) in queued)
        {
            var result = await client.SyncAsync(payload);
            if (result.Success)
            {
                Dequeue(filePath);
                synced++;
            }
            else
            {
                // Stop on first failure — server likely down
                break;
            }
        }

        return synced;
    }
}
