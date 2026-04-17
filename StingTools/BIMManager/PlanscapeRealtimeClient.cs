#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    /// <summary>
    /// C2 — SignalR client that keeps the Revit plugin in sync with server-side
    /// events in real time. Connects to <c>/hubs/notifications</c> on the Planscape
    /// server using the same JWT that <see cref="PlanscapeServerClient"/> holds,
    /// subscribes to the tenant + user + project groups, and raises .NET events
    /// that the BIM Coordination Center can listen for.
    ///
    /// Lifecycle:
    ///   - <see cref="StartAsync"/>       — call once after LoginAsync succeeds
    ///   - <see cref="JoinProjectAsync"/> — whenever the user opens a project
    ///   - <see cref="StopAsync"/>        — on Revit shutdown or Planscape disconnect
    ///
    /// Auto-reconnect is built into the SignalR client; we pipe its state changes
    /// through <see cref="ConnectionStateChanged"/> so the BCC status strip
    /// stays honest.
    /// </summary>
    public sealed class PlanscapeRealtimeClient : IAsyncDisposable
    {
        private static readonly Lazy<PlanscapeRealtimeClient> _instance =
            new(() => new PlanscapeRealtimeClient());
        public static PlanscapeRealtimeClient Instance => _instance.Value;

        private HubConnection? _connection;
        private string? _serverUrl;
        private string? _accessToken;
        private Guid _tenantId;
        private Guid _userId;
        private Guid _currentProjectId;

        public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

        public event EventHandler<RealtimeEvent>? IssueCreated;
        public event EventHandler<RealtimeEvent>? IssueUpdated;
        public event EventHandler<RealtimeEvent>? ComplianceChanged;
        public event EventHandler<RealtimeEvent>? RevisionCreated;
        public event EventHandler<RealtimeEvent>? TransmittalUpdated;
        public event EventHandler<RealtimeEvent>? DocumentUpdated;
        public event EventHandler<RealtimeEvent>? GenericNotification;

        /// <summary>Raised whenever the connection changes state. UI consumers wire this to a status chip.</summary>
        public event EventHandler<HubConnectionState>? ConnectionStateChanged;

        private PlanscapeRealtimeClient() { }

        /// <summary>
        /// Start the SignalR connection to the Planscape server. Safe to call
        /// multiple times — the existing connection is re-used when the server +
        /// token haven't changed.
        /// </summary>
        public async Task<bool> StartAsync(
            string serverUrl,
            string accessToken,
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(accessToken))
            {
                StingLog.Warn("PlanscapeRealtime: missing server URL or access token");
                return false;
            }

            var hubUrl = $"{serverUrl.TrimEnd('/')}/hubs/notifications";

            // Already connected to the same hub with the same token — no-op.
            if (_connection != null
                && _connection.State == HubConnectionState.Connected
                && string.Equals(_serverUrl, serverUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_accessToken, accessToken, StringComparison.Ordinal))
            {
                return true;
            }

            await StopAsync(ct);

            _serverUrl    = serverUrl;
            _accessToken  = accessToken;
            _tenantId     = tenantId;
            _userId       = userId;

            try
            {
                _connection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options =>
                    {
                        // Server's JwtBearer.OnMessageReceived reads ?access_token=...
                        // for WebSocket upgrades, but the Authorization header covers
                        // long-polling / SSE fallbacks.
                        options.AccessTokenProvider = () => Task.FromResult<string?>(_accessToken);
                    })
                    .WithAutomaticReconnect(new[]
                    {
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(30),
                    })
                    .Build();

                _connection.Closed += OnClosed;
                _connection.Reconnecting += OnReconnecting;
                _connection.Reconnected += OnReconnected;

                RegisterHandlers(_connection);

                await _connection.StartAsync(ct);
                ConnectionStateChanged?.Invoke(this, _connection.State);

                // Tenant group is always safe to join.
                await _connection.InvokeAsync("JoinTenant", tenantId.ToString(), ct);
                // User group lets targeted pushes land without a project context.
                await _connection.InvokeAsync("RegisterUser", userId.ToString(), ct);

                StingLog.Info($"PlanscapeRealtime: connected to {hubUrl}");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error("PlanscapeRealtime: connection failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Join the per-project SignalR group. Call this whenever the user switches
        /// projects — the hub allows multiple concurrent joins, but repeatedly
        /// joining the same project is idempotent on the server side.
        /// </summary>
        public async Task JoinProjectAsync(Guid projectId, CancellationToken ct = default)
        {
            if (_connection == null || _connection.State != HubConnectionState.Connected) return;
            if (projectId == Guid.Empty) return;

            try
            {
                if (_currentProjectId != Guid.Empty && _currentProjectId != projectId)
                    await _connection.InvokeAsync("LeaveProject", _currentProjectId.ToString(), ct);

                await _connection.InvokeAsync("JoinProject", projectId.ToString(), ct);
                _currentProjectId = projectId;
                StingLog.Info($"PlanscapeRealtime: joined project {projectId}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlanscapeRealtime: JoinProject failed — {ex.Message}");
            }
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            if (_connection == null) return;
            try
            {
                await _connection.StopAsync(ct);
            }
            catch (Exception ex) { StingLog.Warn($"PlanscapeRealtime: stop — {ex.Message}"); }

            try { await _connection.DisposeAsync(); } catch { }
            _connection = null;
            _currentProjectId = Guid.Empty;
            ConnectionStateChanged?.Invoke(this, HubConnectionState.Disconnected);
        }

        public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

        // ── Server event handlers ────────────────────────────────────────

        private void RegisterHandlers(HubConnection c)
        {
            // Generic "Notification" channel — used by the server for tenant-wide broadcasts.
            c.On<object>("Notification", payload => Raise(GenericNotification, payload, "generic"));
            c.On<object>("UserNotification", payload => Raise(GenericNotification, payload, "user"));

            c.On<object>("IssueCreated",        p => Raise(IssueCreated,        p, "issue"));
            c.On<object>("IssueUpdated",        p => Raise(IssueUpdated,        p, "issue"));
            c.On<object>("ComplianceChanged",   p => Raise(ComplianceChanged,   p, "compliance"));
            c.On<object>("RevisionCreated",     p => Raise(RevisionCreated,     p, "revision"));
            c.On<object>("TransmittalUpdated",  p => Raise(TransmittalUpdated,  p, "transmittal"));
            c.On<object>("DocumentUpdated",     p => Raise(DocumentUpdated,     p, "document"));
        }

        private void Raise(EventHandler<RealtimeEvent>? ev, object payload, string kind)
        {
            if (ev == null) return;
            try
            {
                var json = payload is JObject jo ? jo : JObject.FromObject(payload);
                ev.Invoke(this, new RealtimeEvent(kind, json));
            }
            catch (Exception ex) { StingLog.Warn($"PlanscapeRealtime: handler error — {ex.Message}"); }
        }

        private Task OnClosed(Exception? ex)
        {
            if (ex != null) StingLog.Warn($"PlanscapeRealtime: closed — {ex.Message}");
            ConnectionStateChanged?.Invoke(this, HubConnectionState.Disconnected);
            return Task.CompletedTask;
        }
        private Task OnReconnecting(Exception? ex)
        {
            StingLog.Info($"PlanscapeRealtime: reconnecting…{(ex != null ? $" ({ex.Message})" : "")}");
            ConnectionStateChanged?.Invoke(this, HubConnectionState.Reconnecting);
            return Task.CompletedTask;
        }
        private async Task OnReconnected(string? connectionId)
        {
            StingLog.Info($"PlanscapeRealtime: reconnected ({connectionId})");
            ConnectionStateChanged?.Invoke(this, HubConnectionState.Connected);
            try
            {
                if (_tenantId != Guid.Empty)
                    await _connection!.InvokeAsync("JoinTenant", _tenantId.ToString());
                if (_userId != Guid.Empty)
                    await _connection!.InvokeAsync("RegisterUser", _userId.ToString());
                if (_currentProjectId != Guid.Empty)
                    await _connection!.InvokeAsync("JoinProject", _currentProjectId.ToString());
            }
            catch (Exception ex) { StingLog.Warn($"PlanscapeRealtime: re-join failed — {ex.Message}"); }
        }
    }

    /// <summary>Payload wrapper raised on every real-time event.</summary>
    public sealed class RealtimeEvent : EventArgs
    {
        public string Kind { get; }
        public JObject Data { get; }
        public DateTime ReceivedAt { get; } = DateTime.UtcNow;

        public RealtimeEvent(string kind, JObject data)
        {
            Kind = kind;
            Data = data;
        }

        /// <summary>Convenience — extract a property, returning default when missing.</summary>
        public T? Get<T>(string key)
        {
            try { return Data[key] != null ? Data[key]!.ToObject<T>() : default; }
            catch { return default; }
        }
    }
}
