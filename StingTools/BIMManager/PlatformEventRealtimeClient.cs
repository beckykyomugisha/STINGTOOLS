#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using StingTools.BIMManager.PlatformEvents;
using StingTools.Core;

namespace StingTools.BIMManager;

/// <summary>
/// K2 (STING side) — the LIVE path for the platform event spine. Connects to
/// the dedicated /hubs/events hub (separate from /hubs/notifications) and, the
/// instant the server pushes a "PlatformEvent", asks the drainer to pull +
/// apply. The drainer's poll/manual path remains the resilience floor, so a
/// dropped socket never loses an event — this just removes the polling latency.
///
/// Thread-safe singleton. Wire after login:
///   PlatformEventRealtimeClient.Instance.StartAsync(url, token, projectId);
/// </summary>
public sealed class PlatformEventRealtimeClient : IAsyncDisposable
{
    private static readonly Lazy<PlatformEventRealtimeClient> _lazy = new(() => new PlatformEventRealtimeClient());
    public static PlatformEventRealtimeClient Instance => _lazy.Value;

    private HubConnection? _connection;
    private string _serverUrl = "";
    private string _accessToken = "";
    private Guid _projectId = Guid.Empty;

    private PlatformEventRealtimeClient() { }

    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    public async Task<bool> StartAsync(string serverUrl, string accessToken, Guid projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(accessToken) || projectId == Guid.Empty)
        {
            StingLog.Warn("PlatformEventRealtime: missing server URL, token, or project id");
            return false;
        }

        var hubUrl = $"{serverUrl.TrimEnd('/')}/hubs/events";

        if (_connection != null
            && _connection.State == HubConnectionState.Connected
            && string.Equals(_serverUrl, serverUrl, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_accessToken, accessToken, StringComparison.Ordinal)
            && _projectId == projectId)
        {
            return true;
        }

        await StopAsync(ct);
        _serverUrl = serverUrl;
        _accessToken = accessToken;
        _projectId = projectId;

        try
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                    options.AccessTokenProvider = () => Task.FromResult<string?>(_accessToken))
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero, TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
                })
                .Build();

            // Any push → drain. Payload is informational; the drainer pulls the
            // authoritative pending list, so we don't trust the socket body.
            _connection.On<object>("PlatformEvent", _ => PlatformEventDrainer.OnRealtimePlatformEvent());

            // On reconnect, catch up on anything missed while disconnected.
            _connection.Reconnected += async _ =>
            {
                try { await _connection!.InvokeAsync("JoinProject", _projectId.ToString()); } catch { }
                PlatformEventDrainer.OnRealtimePlatformEvent();
            };

            await _connection.StartAsync(ct);
            await _connection.InvokeAsync("JoinProject", _projectId.ToString(), ct);

            // Catch up on events appended before we connected.
            PlatformEventDrainer.OnRealtimePlatformEvent();

            StingLog.Info($"PlatformEventRealtime: connected to {hubUrl}");
            return true;
        }
        catch (Exception ex)
        {
            StingLog.Error("PlatformEventRealtime: connection failed", ex);
            return false;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_connection == null) return;
        try { await _connection.DisposeAsync(); }
        catch (Exception ex) { StingLog.Warn($"PlatformEventRealtime: stop failed — {ex.Message}"); }
        finally { _connection = null; }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
