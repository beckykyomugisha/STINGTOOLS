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
        LoadDropCounter();
    }

    /// <summary>
    /// Enqueue a sync payload for later delivery.
    /// </summary>
    private const int MaxQueueFiles = 500; // Prevent unbounded growth

    /// <summary>
    /// P3 — number of payloads silently dropped because the queue hit
    /// <see cref="MaxQueueFiles"/>. Persisted across plugin restarts in
    /// <c>queue_drop_counter.txt</c> alongside the queue files. The
    /// dock-panel sync chip reads this to render a warning when offline
    /// data is being lost (e.g. "12 payloads dropped — connect to
    /// resync").
    /// </summary>
    public int DroppedSinceLastDrain { get; private set; }

    private string DropCounterPath => Path.Combine(_queueDir, "queue_drop_counter.txt");

    private void LoadDropCounter()
    {
        try
        {
            if (File.Exists(DropCounterPath)
                && int.TryParse(File.ReadAllText(DropCounterPath), out var n))
            {
                DroppedSinceLastDrain = n;
            }
        }
        catch { /* best-effort */ }
    }

    private void WriteDropCounter()
    {
        try { File.WriteAllText(DropCounterPath, DroppedSinceLastDrain.ToString()); }
        catch { /* best-effort */ }
    }

    public void Enqueue(PluginSyncPayload payload)
    {
        lock (_lock)
        {
            // Enforce queue size limit — drop oldest if full
            var existing = Directory.GetFiles(_queueDir, "sync_*.json").OrderBy(f => f).ToArray();
            if (existing.Length >= MaxQueueFiles)
            {
                int dropped = 0;
                for (int i = 0; i <= existing.Length - MaxQueueFiles; i++)
                {
                    try { File.Delete(existing[i]); dropped++; } catch (IOException) { }
                }
                if (dropped > 0)
                {
                    // P3 — surface the loss instead of silently overwriting.
                    DroppedSinceLastDrain += dropped;
                    WriteDropCounter();
                    Console.Error.WriteLine($"[OfflineQueue] dropped {dropped} oldest payload(s); total since last drain: {DroppedSinceLastDrain}");
                }
            }

            string fileName = $"sync_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.json";
            string filePath = Path.Combine(_queueDir, fileName);
            File.WriteAllText(filePath, JsonConvert.SerializeObject(payload, Formatting.None));
        }
    }

    /// <summary>P3 — clear the dropped-count after the dock panel has
    /// acknowledged the warning.</summary>
    public void AcknowledgeDrops()
    {
        lock (_lock)
        {
            DroppedSinceLastDrain = 0;
            WriteDropCounter();
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
    public Task<int> DrainAsync(SyncClient client) => DrainAsync(client, null);

    /// <summary>
    /// Drain queued payloads, optionally filtering elements by discipline (INT-08).
    /// When <paramref name="disciplineFilter"/> is non-null, payloads keep only the
    /// elements matching the allow-list. Empty payloads after filtering are skipped
    /// (they remain in the queue for the next drain that uses a wider filter).
    /// </summary>
    public async Task<int> DrainAsync(SyncClient client, HashSet<string>? disciplineFilter)
    {
        var queued = PeekAll();
        int synced = 0;

        foreach (var (filePath, payload) in queued)
        {
            var toSend = payload;
            if (disciplineFilter != null && disciplineFilter.Count > 0 && payload.TagElements != null)
            {
                var filtered = payload.TagElements
                    .Where(el => string.IsNullOrEmpty(el.Disc) || disciplineFilter.Contains(el.Disc))
                    .ToList();
                if (filtered.Count == 0) continue;       // leave for a later, wider drain
                toSend = new PluginSyncPayload
                {
                    ProjectId = payload.ProjectId,
                    PluginVersion = payload.PluginVersion,
                    RevitVersion = payload.RevitVersion,
                    UserName = payload.UserName,
                    Timestamp = payload.Timestamp,
                    SeqCounters = payload.SeqCounters,
                    Compliance = payload.Compliance,
                    Issues = payload.Issues,
                    WorkflowRuns = payload.WorkflowRuns,
                    TagElements = filtered,
                };
            }

            var result = await client.SyncAsync(toSend);
            if (result.Success)
            {
                Dequeue(filePath);
                synced++;
            }
            else if (result.IsFatalRequestError)
            {
                // P4 — fatal request error (4xx). Re-trying this same
                // payload won't help: it could be a malformed body, a
                // revoked token, or a tenant that no longer exists.
                // Skip and keep draining so one bad apple doesn't lock
                // out the rest of the queue.
                Console.Error.WriteLine(
                    $"[OfflineQueue] dropping payload {Path.GetFileName(filePath)} — server returned {result.StatusCode}: {result.ErrorMessage}");
                Dequeue(filePath);
            }
            else
            {
                // Transient (5xx, network, no status). Stop draining;
                // the next scheduled tick will pick up where we left off.
                break;
            }
        }

        return synced;
    }
}
