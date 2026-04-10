using Planscape.Shared.Models;

namespace Planscape.PluginSync;

/// <summary>
/// Background sync scheduler for the Revit plugin.
/// Periodically batches local changes and pushes to server.
/// Falls back to offline queue when server is unreachable.
/// </summary>
public class SyncScheduler : IDisposable
{
    private readonly SyncClient _client;
    private readonly OfflineQueue _queue;
    private Timer? _timer;
    private bool _syncing;
    private readonly object _lock = new();

    public bool IsRunning { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public string? LastError { get; private set; }
    public int QueuedCount => _queue.Count;

    /// <summary>
    /// Fired after each sync attempt with success/failure info.
    /// </summary>
    public event Action<SyncResult>? OnSyncComplete;

    public SyncScheduler(SyncClient client, string offlineQueueDir)
    {
        _client = client;
        _queue = new OfflineQueue(offlineQueueDir);
    }

    /// <summary>
    /// Start periodic sync (default: every 5 minutes).
    /// </summary>
    public void Start(TimeSpan? interval = null)
    {
        if (IsRunning) return;
        var syncInterval = interval ?? TimeSpan.FromMinutes(5);
        _timer = new Timer(async _ => await TrySyncAsync(), null, syncInterval, syncInterval);
        IsRunning = true;
    }

    /// <summary>
    /// Stop periodic sync.
    /// </summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        IsRunning = false;
    }

    /// <summary>
    /// Queue a payload for next sync cycle. If sync is immediate, tries to send now.
    /// </summary>
    public void QueueSync(PluginSyncPayload payload)
    {
        _queue.Enqueue(payload);
    }

    /// <summary>
    /// Force an immediate sync attempt (e.g., on user request or after tagging operation).
    /// </summary>
    public async Task<SyncResult> SyncNowAsync(PluginSyncPayload? payload = null)
    {
        if (payload != null)
            _queue.Enqueue(payload);

        return await TrySyncCoreAsync();
    }

    private async Task TrySyncAsync()
    {
        await TrySyncCoreAsync();
    }

    private async Task<SyncResult> TrySyncCoreAsync()
    {
        lock (_lock)
        {
            if (_syncing) return new SyncResult { ErrorMessage = "Sync already in progress" };
            _syncing = true;
        }

        var result = new SyncResult();
        try
        {
            if (!_client.IsAuthenticated)
            {
                result.ErrorMessage = "Not authenticated";
                return result;
            }

            // Drain offline queue
            int drained = await _queue.DrainAsync(_client);
            result.Success = true;
            result.TagsCreated = drained; // Simplified — actual counts are per-payload
            LastSyncAt = DateTime.UtcNow;
            LastError = null;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            LastError = ex.Message;
        }
        finally
        {
            lock (_lock) { _syncing = false; }
            OnSyncComplete?.Invoke(result);
        }

        return result;
    }

    public void Dispose()
    {
        Stop();
        _client.Dispose();
    }
}
