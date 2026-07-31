using Microsoft.AspNetCore.SignalR.Client;

namespace Planscape.Companion;

/// <summary>
/// The push half of the client: a SignalR connection to
/// <c>/hubs/document-sync</c>.
///
/// Two behaviours carry the design's "push, with polling only as the reconnect
/// fallback" rule:
///
/// <list type="number">
/// <item>A <c>DocumentChanged</c> event triggers the same sync routine every
/// other trigger uses. The event is a nudge, not data — it carries no file
/// content, and the Companion answers it by calling <c>changed-since</c>, which
/// re-applies the caller's ACL server-side.</item>
/// <item><b>On every (re)connect, sync immediately.</b> This is the whole point
/// of the fallback: while the connection was down, pushes were missed, and
/// nothing will ever resend them. Reconnecting without a delta sweep leaves the
/// machine quietly stale until the next unrelated change happens to fire.</item>
/// </list>
/// </summary>
internal sealed class SyncHubClient : IAsyncDisposable
{
    private readonly string _baseUrl;
    private readonly Func<CancellationToken, Task<string>> _tokenFactory;
    private readonly Func<string, bool, Task> _onDocumentChanged;
    private readonly Func<Task> _onConnected;
    private readonly Action<bool> _onConnectionStateChanged;
    private HubConnection? _connection;

    public SyncHubClient(
        string baseUrl,
        Func<CancellationToken, Task<string>> tokenFactory,
        Func<string, bool, Task> onDocumentChanged,
        Func<Task> onConnected,
        Action<bool> onConnectionStateChanged)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _tokenFactory = tokenFactory;
        _onDocumentChanged = onDocumentChanged;
        _onConnected = onConnected;
        _onConnectionStateChanged = onConnectionStateChanged;
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task StartAsync(IEnumerable<string> projectIds, CancellationToken ct)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}/hubs/document-sync", options =>
            {
                // Re-read per connection attempt so a reconnect after a long
                // offline stretch picks up a freshly exchanged JWT rather than
                // replaying an expired one forever.
                options.AccessTokenProvider = async () => await _tokenFactory(CancellationToken.None);
            })
            // Explicit ladder rather than WithAutomaticReconnect()'s default
            // (0s, 2s, 10s, 30s, then GIVE UP). A closed laptop is offline for
            // hours; a client that stops trying after 30 seconds is a client that
            // never syncs again until the user restarts it. `null` at the end of
            // the delegate would mean stop, so this one never returns null.
            .WithAutomaticReconnect(new BoundedBackoff())
            .Build();

        // A CONCRETE type, not a JObject. SignalR's default hub protocol is
        // System.Text.Json, which cannot materialise a Newtonsoft JObject - the
        // handler silently never fires, the connection stays up and healthy, and
        // the only symptom is that pushes do nothing. This was found exactly that
        // way: the server logged the push, the client logged nothing at all.
        _connection.On<DocumentChangedPayload>("DocumentChanged", async payload =>
        {
            var projectId = payload?.ProjectId;
            var kind = payload?.Kind ?? "change";
            if (string.IsNullOrEmpty(projectId)) return;
            var autoSync = payload?.AutoSyncEnabled ?? true;
            CompanionLog.Info($"push: {kind} on project {projectId} (auto-sync {(autoSync ? "on" : "off")})");
            try { await _onDocumentChanged(projectId, autoSync); }
            catch (Exception ex) { CompanionLog.Error("push-triggered sync failed", ex); }
        });

        _connection.Reconnecting += _ =>
        {
            _onConnectionStateChanged(false);
            CompanionLog.Warn("hub connection lost; reconnecting");
            return Task.CompletedTask;
        };

        _connection.Reconnected += async _ =>
        {
            CompanionLog.Info("hub reconnected; running catch-up delta");
            _onConnectionStateChanged(true);
            await RejoinAsync(projectIds);
            // The reconnect fallback. Without this the missed pushes are simply lost.
            await _onConnected();
        };

        _connection.Closed += async error =>
        {
            _onConnectionStateChanged(false);
            CompanionLog.Warn($"hub closed: {error?.Message ?? "no error"}");
            await Task.CompletedTask;
        };

        await _connection.StartAsync(ct);
        _onConnectionStateChanged(true);
        CompanionLog.Info($"hub connected to {_baseUrl}/hubs/document-sync");
        await RejoinAsync(projectIds);
        await _onConnected();
    }

    /// <summary>
    /// Group membership does NOT survive a reconnect — SignalR gives the client a
    /// new connection id and the server's groups are keyed on it. Re-joining is
    /// mandatory, and forgetting it is the classic silent SignalR bug: everything
    /// looks connected and no events ever arrive.
    /// </summary>
    private async Task RejoinAsync(IEnumerable<string> projectIds)
    {
        if (_connection == null) return;
        foreach (var id in projectIds)
        {
            try
            {
                await _connection.InvokeAsync("JoinProject", id);
                CompanionLog.Info($"joined project group {id}");
            }
            catch (Exception ex)
            {
                CompanionLog.Warn($"could not join project {id}: {ex.Message}");
            }
        }
    }

    public async Task JoinAsync(string projectId)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("JoinProject", projectId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null) await _connection.DisposeAsync();
    }

    /// <summary>
    /// Backoff that never gives up: 0s, 2s, 5s, 10s, 30s, then every 60s forever.
    /// A laptop shut for the weekend must find its way back on Monday without
    /// anyone restarting anything.
    /// </summary>
    private sealed class BoundedBackoff : IRetryPolicy
    {
        private static readonly TimeSpan[] Ladder =
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
        };

        public TimeSpan? NextRetryDelay(RetryContext ctx) =>
            ctx.PreviousRetryCount < Ladder.Length
                ? Ladder[ctx.PreviousRetryCount]
                : TimeSpan.FromSeconds(60);
    }
}

/// <summary>
/// Wire shape of <c>DocumentChanged</c> - mirrors <c>DocumentSyncHub.Payload</c>.
/// Property names bind case-insensitively under SignalR's JSON protocol, so the
/// server's camelCase maps onto these directly.
/// </summary>
internal sealed class DocumentChangedPayload
{
    public string? ProjectId { get; set; }
    public string? DocumentId { get; set; }
    public string? Kind { get; set; }
    public string? CdeStatus { get; set; }

    /// <summary>
    /// The project's "Auto-sync this project" flag as the server sees it, sent
    /// with the notification so the Companion can decide whether it may act
    /// without first calling the API to ask.
    ///
    /// Nullable, and a null is treated as TRUE by the caller: an older server
    /// that does not send the field must not be read as "auto-sync off", which
    /// would silently stop syncing for everyone on that deployment.
    /// </summary>
    public bool? AutoSyncEnabled { get; set; }

    public DateTime? ChangedAtUtc { get; set; }
}
