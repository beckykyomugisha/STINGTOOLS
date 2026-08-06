namespace Planscape.Companion;

/// <summary>Why a sync ran. Logging only — every trigger runs the same code.</summary>
internal enum SyncTrigger
{
    /// <summary>A DocumentChanged push arrived.</summary>
    Push,

    /// <summary>The hub (re)connected; sweeping up what was missed while offline.</summary>
    Reconnect,

    /// <summary>A project was just linked on this machine — `since` is unset.</summary>
    InitialLink,

    /// <summary>Someone pressed Sync now, in BCC or the tray.</summary>
    Manual,
}


/// <summary>
/// The Companion's brain: owns the API client, the hub connection, the IPC
/// server and the status record, and is the single place a sync is triggered
/// from — whichever of the four triggers fired.
///
/// Separate from the tray so that <c>--diagnose</c> can run exactly the same
/// logic with no window, which is what makes the thing testable at all.
/// </summary>
internal sealed class CompanionService : IDisposable
{
    private readonly CompanionSettings _settings;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private PlanscapeApiClient? _api;
    private SyncHubClient? _hub;
    private CompanionIpcServer? _ipc;

    public SyncStatus Status { get; }

    /// <summary>Raised whenever the status changes so the tray can repaint.</summary>
    public event Action? StatusChanged;

    public CompanionService(CompanionSettings settings)
    {
        _settings = settings;
        Status = SyncStatus.Load();
        Status.LinkedProjects = settings.Projects.Count;
    }

    private bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.ServerUrl) && !string.IsNullOrWhiteSpace(_settings.AccessToken);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Start the IPC surface and the hub connection. Returns immediately.</summary>
    public void Start()
    {
        // IPC first, and unconditionally. An unconfigured Companion is exactly
        // the one BCC most needs to be able to interrogate — "it's running but it
        // has no token" is a far more useful answer than silence.
        _ipc = new CompanionIpcServer(() => Status, SyncNowAsync, DownloadHistoryAsync);
        _ipc.Start();
        // Count before the first sync as well: the files are already on disk from
        // previous sessions, and a tray that reads 0 until the first sync of the
        // day would be wrong for as long as the user is offline.
        RefreshCheckedOut();

        if (!IsConfigured)
        {
            CompanionLog.Warn("no server URL or access token configured — idle until set");
            SetState(SyncState.Error, "not configured — set a server and access token");
            return;
        }

        _ = Task.Run(() => ConnectLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Keep trying to establish the initial connection. SignalR's automatic
    /// reconnect only covers a connection that once succeeded — a Companion that
    /// starts at login before the VPN is up would otherwise sit dead forever.
    /// </summary>
    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _api ??= new PlanscapeApiClient(_settings.ServerUrl!, _settings.AccessToken!);
                _hub = new SyncHubClient(
                    _settings.ServerUrl!,
                    _api.GetAccessTokenAsync,
                    onDocumentChanged: (projectId, autoSync) =>
                        SyncOneAsync(projectId, SyncTrigger.Push, ct, serverAutoSync: autoSync),
                    onConnected: () => SyncAllAsync(SyncTrigger.Reconnect, ct),
                    onConnectionStateChanged: connected =>
                    {
                        if (!connected && Status.State != SyncState.Error) SetState(SyncState.Offline);
                        else if (connected && Status.State == SyncState.Offline) SetState(SyncState.Idle);
                    });

                await _hub.StartAsync(_settings.Projects.Select(p => p.ProjectId), ct);
                return; // connected; SignalR's own reconnect takes it from here
            }
            catch (CompanionAuthException ex)
            {
                // A rejected token is NOT offline. It will never fix itself, so it
                // is the state that shouts (see SyncStatus).
                SetState(SyncState.Error, ex.Message);
                CompanionLog.Error("authentication failed — stopping connection attempts", ex);
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                SetState(SyncState.Offline);
                var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(attempt, 6))));
                CompanionLog.Warn($"connect attempt {attempt} failed ({ex.Message}); retrying in {delay.TotalSeconds:0}s");
                try { await Task.Delay(delay, ct); } catch { return; }
            }
        }
    }

    // ── Triggers — all four land on SyncProjectAsync ──────────────────────────

    /// <summary>Manual "Sync now" (tray or BCC over IPC). Null = every linked project.</summary>
    public async Task<string> SyncNowAsync(string? projectId)
    {
        var outcome = projectId == null
            ? await SyncAllAsync(SyncTrigger.Manual, _cts.Token)
            : await SyncOneAsync(projectId, SyncTrigger.Manual, _cts.Token);
        return outcome;
    }

    /// <summary>Link a project and immediately pull everything visible.</summary>
    public async Task<string> LinkAndSyncAsync(string projectId, string projectCode, CancellationToken ct = default)
    {
        if (_settings.Find(projectId) == null)
            _settings.Projects.Add(new LinkedProject { ProjectId = projectId, ProjectCode = projectCode });
        _settings.Save();
        Status.LinkedProjects = _settings.Projects.Count;

        if (_hub != null) await _hub.JoinAsync(projectId);
        return await SyncOneAsync(projectId, SyncTrigger.InitialLink, ct);
    }

    private async Task<string> SyncAllAsync(SyncTrigger trigger, CancellationToken ct)
    {
        var results = new List<string>();
        foreach (var project in _settings.Projects.ToList())
        {
            // The per-project auto/manual toggle gates only the AUTOMATIC
            // triggers. A user who explicitly pressed Sync now has overridden it
            // by definition, and refusing them would be obtuse.
            if (!project.AutoSync && trigger is SyncTrigger.Push or SyncTrigger.Reconnect)
            {
                CompanionLog.Info($"skipping {project.ProjectCode} — auto-sync is off");
                continue;
            }
            results.Add(await SyncProjectAsync(project, trigger, ct));
        }
        return results.Count == 0 ? "nothing to sync" : string.Join("; ", results);
    }

    /// <param name="serverAutoSync">
    /// The project's flag as the SERVER just reported it, when the caller has it
    /// (a push carries it). Overrides the cached copy, because the cache can be
    /// one sync behind and the push is by definition current.
    /// </param>
    private async Task<string> SyncOneAsync(string projectId, SyncTrigger trigger, CancellationToken ct,
        bool? serverAutoSync = null)
    {
        var project = _settings.Find(projectId);
        if (project == null) return $"project {projectId} is not linked on this machine";

        if (serverAutoSync.HasValue && project.AutoSync != serverAutoSync.Value)
        {
            // Keep the cache honest even when we are about to skip: the tray and
            // BCC read it, and showing "auto-sync on" while silently not syncing
            // would be worse than either behaviour on its own.
            project.AutoSync = serverAutoSync.Value;
            _settings.Save();
        }

        if (!project.AutoSync && trigger is SyncTrigger.Push or SyncTrigger.Reconnect)
            return $"{project.ProjectCode}: auto-sync is off";
        return await SyncProjectAsync(project, trigger, ct);
    }

    /// <summary>
    /// The one place a sync actually happens. Serialised by a semaphore: two
    /// passes writing the same folder would race on the supersede-then-replace
    /// sequence and could leave a document with no live copy at all.
    /// </summary>
    private async Task<string> SyncProjectAsync(LinkedProject project, SyncTrigger trigger, CancellationToken ct)
    {
        if (_api == null) return "not connected";

        await _syncGate.WaitAsync(ct);
        try
        {
            SetState(SyncState.Syncing);
            var engine = new SyncEngine(_api, _settings);
            var outcome = await engine.SyncProjectAsync(project, trigger, ct);

            _settings.Save();  // persist the advanced high-water mark
            Status.FilesLastSync = outcome.Downloaded;
            Status.LastSuccessUtc = DateTime.UtcNow;
            RefreshCheckedOut();
            Status.ConsecutiveFailures = 0;
            SetState(SyncState.Idle);
            return $"{project.ProjectCode}: {outcome}";
        }
        catch (CompanionAuthException ex)
        {
            Status.ConsecutiveFailures++;
            SetState(SyncState.Error, ex.Message);
            return $"{project.ProjectCode}: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            return $"{project.ProjectCode}: cancelled";
        }
        catch (HttpRequestException ex)
        {
            // Offline, not broken — quiet, and it will retry itself.
            Status.ConsecutiveFailures++;
            SetState(SyncState.Offline);
            CompanionLog.Warn($"{project.ProjectCode}: {ex.Message}");
            return $"{project.ProjectCode}: offline";
        }
        catch (Exception ex)
        {
            // Everything else — an unwritable folder, a full disk — needs a human.
            Status.ConsecutiveFailures++;
            SetState(SyncState.Error, ex.Message);
            CompanionLog.Error($"{project.ProjectCode}: sync failed", ex);
            return $"{project.ProjectCode}: {ex.Message}";
        }
        finally
        {
            _syncGate.Release();
        }
    }

    /// <summary>One-shot for <c>--diagnose</c>: sync every linked project once.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        if (!IsConfigured) return false;
        _api = new PlanscapeApiClient(_settings.ServerUrl!, _settings.AccessToken!);

        // Zero linked projects is a SETUP state, not a connectivity one.
        //
        // Without this branch the per-project loop below never runs, nothing ever
        // moves State off its Offline default, and a correctly configured
        // Companion greets its first user with "offline, will retry" while the
        // server and token are perfectly fine. The default itself is right —
        // Offline is the quiet, non-alarming bucket for "not proven yet" (see
        // SyncStatus) — but leaving it unproven here is what made it a lie.
        //
        // So prove it directly: a token exchange is the cheapest authenticated
        // round-trip available and separates the three outcomes that genuinely
        // differ — reachable and authorised (Idle), unreachable (Offline),
        // rejected (Error). The tray path never needed this because establishing
        // the hub connection already flips the state; --diagnose has no hub.
        if (_settings.Projects.Count == 0)
            return await ReportConnectionOnlyAsync(ct);

        var result = await SyncAllAsync(SyncTrigger.Manual, ct);
        Console.WriteLine(result);
        return Status.State != SyncState.Error;
    }

    /// <summary>
    /// Prove the server and credentials with no project to sync, and say what is
    /// actually missing. Deliberately does NOT set <c>LastSuccessUtc</c>: nothing
    /// synced, and claiming a successful sync would be a second, quieter lie than
    /// the one this method exists to fix.
    /// </summary>
    private async Task<bool> ReportConnectionOnlyAsync(CancellationToken ct)
    {
        try
        {
            await _api!.GetAccessTokenAsync(ct);
            SetState(SyncState.Idle);
            Console.WriteLine("Connected — server and access token are good.");
            Console.WriteLine("No projects are linked on this machine yet, so there is nothing to sync.");
            Console.WriteLine("Link one with:  Planscape.Companion.exe --link <projectId> <projectCode>");
            CompanionLog.Info("connection verified; no projects linked on this machine");
            return true;
        }
        catch (CompanionAuthException ex)
        {
            // Rejected credentials will never fix themselves — this is the state
            // that should shout, and the one Offline must not be confused with.
            SetState(SyncState.Error, ex.Message);
            Console.WriteLine($"The server rejected the access token: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            // Genuinely unreachable. Here "offline, will retry" is the truth.
            SetState(SyncState.Offline);
            Console.WriteLine($"Could not reach {_settings.ServerUrl}: {ex.Message}");
            CompanionLog.Warn($"connection check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Slice E — full revision history for ONE document, on explicit request.
    /// Never called by any automatic trigger.
    /// </summary>
    public async Task<string> DownloadHistoryAsync(string projectId, string documentId)
    {
        var project = _settings.Find(projectId);
        if (project == null) return $"project {projectId} is not linked on this machine";
        if (!Guid.TryParse(documentId, out var docGuid)) return $"'{documentId}' is not a document id";
        if (_api == null)
        {
            // History is a deliberate user action, so it is worth connecting for
            // even when the background loop has not managed to yet.
            if (!IsConfigured) return "not configured — set a server and access token";
            _api = new PlanscapeApiClient(_settings.ServerUrl!, _settings.AccessToken!);
        }

        await _syncGate.WaitAsync(_cts.Token);
        try
        {
            SetState(SyncState.Syncing);
            var engine = new SyncEngine(_api, _settings);
            var result = await engine.DownloadHistoryAsync(project, docGuid, _cts.Token);
            SetState(SyncState.Idle);
            return result;
        }
        catch (Exception ex)
        {
            SetState(SyncState.Error, ex.Message);
            return $"history download failed: {ex.Message}";
        }
        finally
        {
            _syncGate.Release();
        }
    }

    /// <summary>
    /// Recount the WIP working copies on disk across every linked project.
    /// Cheap (a directory enumeration per project) and always truthful, because
    /// it reads the same filesystem the user is looking at.
    /// </summary>
    private void RefreshCheckedOut()
    {
        try
        {
            var all = new List<string>();
            foreach (var project in _settings.Projects.ToList())
                foreach (var name in SyncEngine.WorkingCopiesIn(_settings.FolderFor(project)))
                    all.Add($"{project.ProjectCode}/{name}");

            Status.CheckedOutCount = all.Count;
            // Capped: the tray shows a short list, and a status file carrying
            // thousands of names would be written on every state change.
            Status.CheckedOut = all.Take(50).ToList();
        }
        catch (Exception ex)
        {
            CompanionLog.Warn($"checked-out refresh: {ex.Message}");
        }
    }

    // ── Status ────────────────────────────────────────────────────────────────

    private void SetState(SyncState state, string? error = null)
    {
        Status.State = state;
        if (state == SyncState.Error && error != null)
        {
            Status.LastError = error;
            Status.LastErrorUtc = DateTime.UtcNow;
        }
        else if (state == SyncState.Idle)
        {
            // A success clears the sticky error — otherwise the tray keeps
            // reporting a problem that fixed itself hours ago.
            Status.LastError = null;
        }
        Status.Save();
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _ipc?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _hub?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); } catch { }
        _api?.Dispose();
        _cts.Dispose();
        _syncGate.Dispose();
    }
}
