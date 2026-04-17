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
    /// INT-08 — Discipline allow-list. When non-empty, only payloads whose
    /// element disciplines intersect this set are pushed; everything else stays
    /// in the offline queue. Empty/null means "sync everything" (legacy behaviour).
    /// </summary>
    public HashSet<string>? DisciplineFilter { get; set; }

    /// <summary>
    /// INT-07 — Sync status snapshot exposed for the Revit dock indicator.
    /// </summary>
    public SyncStatus Status => new()
    {
        IsRunning = IsRunning,
        IsSyncing = _syncing,
        QueuedCount = QueuedCount,
        LastSyncAt = LastSyncAt,
        LastError = LastError,
        DisciplineFilter = DisciplineFilter?.ToArray(),
    };

    /// <summary>
    /// Fired after each sync attempt with success/failure info.
    /// </summary>
    public event Action<SyncResult>? OnSyncComplete;

    /// <summary>
    /// Phase 91 — fired on each timer tick BEFORE the offline-queue drain.
    /// Plugin hosts (e.g. Revit) hook this to marshal to their UI/API thread
    /// via ExternalEvent, build a payload from the currently open document,
    /// and push it onto <see cref="OfflineQueue.Shared"/>. The drain that
    /// follows then delivers it to the server. Best-effort — exceptions are
    /// swallowed so a misbehaving host never kills the scheduler timer.
    /// </summary>
    public static Action? OnTick { get; set; }

    // ── Static process-wide facade (S03) ──────────────────────────────────
    // The Revit plugin only ever wants ONE scheduler per Revit session, so we
    // expose a static singleton + lifecycle helpers. These coexist with the
    // instance API (used by hosted/test scenarios and the server).
    private static SyncScheduler? _instance;
    private static readonly object _instanceLock = new();

    /// <summary>The process-wide scheduler instance, or null if Start was never called.</summary>
    public static SyncScheduler? Instance => _instance;

    /// <summary>
    /// Start the process-wide scheduler with a pre-authenticated bearer token.
    /// Idempotent — subsequent calls are no-ops while a scheduler is running.
    /// </summary>
    public static void Start(string serverUrl, string authToken,
        string? offlineQueueDir = null, TimeSpan? interval = null)
    {
        lock (_instanceLock)
        {
            if (_instance != null && _instance.IsRunning) return;
            offlineQueueDir ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "STING Tools", "sync-queue");
            var client = new SyncClient(serverUrl);
            client.SetAuthToken(authToken);
            _instance = new SyncScheduler(client, offlineQueueDir);
            _instance.Start(interval);
            OfflineQueue.SetShared(_instance._queue);
        }
    }

    /// <summary>
    /// Stop and dispose the process-wide scheduler (call on Revit shutdown).
    /// Name differs from the instance Stop() because C# disallows static/instance
    /// overloads with identical signatures.
    /// </summary>
    public static void StopShared()
    {
        lock (_instanceLock)
        {
            try { _instance?.Stop(); }
            catch { /* best-effort — sync is opportunistic */ }
            try { _instance?.Dispose(); }
            catch { /* best-effort */ }
            _instance = null;
            OfflineQueue.SetShared(null);
        }
    }

    /// <summary>
    /// Force an immediate sync attempt against the process-wide scheduler.
    /// Returns a completed task with an error if no scheduler has been started.
    /// </summary>
    public static Task<SyncResult> SyncNow(PluginSyncPayload? payload = null)
    {
        var inst = _instance;
        if (inst == null)
            return Task.FromResult(new SyncResult { ErrorMessage = "SyncScheduler not started" });
        return inst.SyncNowAsync(payload);
    }

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

        // Phase 91 — manual sync path: the caller already built the payload it
        // wants sent, so don't invoke OnTick (would duplicate the payload).
        return await TrySyncCoreAsync(fromTimer: false);
    }

    private async Task TrySyncAsync()
    {
        // Phase 91 — timer tick path: invoke OnTick so the host can enqueue a
        // fresh payload from the current document before we drain.
        await TrySyncCoreAsync(fromTimer: true);
    }

    private async Task<SyncResult> TrySyncCoreAsync(bool fromTimer = false)
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

            // Phase 91 — on timer-initiated syncs, give the host a chance to
            // enqueue a fresh payload from the current document before we
            // drain. Skipped on SyncNowAsync (manual) because the caller
            // already queued the exact payload it wants sent. Best-effort:
            // if the host throws we still drain whatever is already queued.
            if (fromTimer)
            {
                try { OnTick?.Invoke(); }
                catch (Exception tickEx) { LastError = $"OnTick: {tickEx.Message}"; }
            }

            // Drain offline queue, honouring discipline filter (INT-08)
            int drained = await _queue.DrainAsync(_client, DisciplineFilter);
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

/// <summary>
/// Snapshot of scheduler state for the dock-panel sync indicator (INT-07).
/// </summary>
public class SyncStatus
{
    public bool IsRunning { get; set; }
    public bool IsSyncing { get; set; }
    public int QueuedCount { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string? LastError { get; set; }
    public string[]? DisciplineFilter { get; set; }

    public string ShortLabel
    {
        get
        {
            if (!IsRunning) return "Sync: off";
            if (IsSyncing) return "Sync: working…";
            if (!string.IsNullOrEmpty(LastError)) return $"Sync: error ({LastError})";
            if (QueuedCount > 0) return $"Sync: {QueuedCount} queued";
            return LastSyncAt.HasValue
                ? $"Sync: ok ({(DateTime.UtcNow - LastSyncAt.Value).TotalMinutes:F0}m ago)"
                : "Sync: ready";
        }
    }
}
