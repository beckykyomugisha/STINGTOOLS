using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Server-side Autodesk Construction Cloud connector — the team-shared half of
/// the ACC integration (the plugin half lives in StingTools V6.AccIssueSync).
/// Tokens are held per-project in <see cref="PlatformConnection"/> (seeded by the
/// 3-legged flow in AccOAuthController), so the whole team shares one ACC grant.
///
/// Reads the APS app credentials from config: <c>Acc:ClientId</c> /
/// <c>Acc:ClientSecret</c> (env <c>Acc__ClientId</c> / <c>Acc__ClientSecret</c>),
/// and the APS host from <c>Aps:BaseUrl</c> (see <see cref="Aps.ApsEndpoints"/>).
///
/// Token refresh ROTATES the pair onto the entity (APS invalidates the old refresh
/// token). It does not persist — callers that own a DbContext go through
/// <see cref="AccTokenRefresher.EnsureFreshAsync"/>, which saves the rotation
/// immediately and serialises refreshes across processes. The in-process lock
/// below is the fallback for providers without advisory locks.
///
/// CAVEAT: built to documented APS signatures but NOT yet exercised against a
/// live ACC project or a deployed server.
/// </summary>
public class AccConnector : IPlatformConnector
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AccConnector> _logger;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _refreshLocks = new();

    public AccConnector(IConfiguration config, IHttpClientFactory httpFactory, ILogger<AccConnector> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public PlatformType Platform => PlatformType.ACC;

    private (string id, string secret) AppCreds() =>
        (_config["Acc:ClientId"] ?? "", _config["Acc:ClientSecret"] ?? "");

    public async Task<PlatformTokenResult> RefreshTokenAsync(PlatformConnection connection, CancellationToken ct = default)
    {
        var (id, secret) = AppCreds();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
            return new PlatformTokenResult(false, Error: "Acc:ClientId / Acc:ClientSecret not configured on the server.");
        if (Planscape.Infrastructure.Security.PlatformTokenProtection.IsUnreadable(connection.RefreshToken))
            return new PlatformTokenResult(false, Error: AccTokenRefresher.UnreadableTokenError);
        if (string.IsNullOrWhiteSpace(connection.RefreshToken))
            return new PlatformTokenResult(false, Error:
                "No usable refresh token — connect ACC via /api/acc/oauth/start.");

        var gate = _refreshLocks.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (AccTokenRefresher.IsFresh(connection, AccTokenRefresher.DefaultBuffer))
                return new PlatformTokenResult(true, connection.AccessToken, connection.RefreshToken, connection.TokenExpiresAt);

            var http = _httpFactory.CreateClient();
            string refreshToken = connection.RefreshToken!;
            // Refresh is NOT idempotent (it rotates) — ApsRetry retries only 429 / 503+Retry-After.
            using var resp = await Aps.ApsRetry.SendAsync(http, () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, Aps.ApsEndpoints.TokenUrl(_config))
                {
                    Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "refresh_token"),
                        new KeyValuePair<string, string>("refresh_token", refreshToken),
                    })
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{id}:{secret}")));
                return req;
            }, idempotent: false, _logger, ct);

            string body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("ACC token refresh HTTP {Status}: {Body}", (int)resp.StatusCode, body);
                bool invalidGrant = body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase);
                return new PlatformTokenResult(false, Error: invalidGrant
                    ? "ACC rejected the refresh token (invalid_grant) — reconnect ACC."
                    : $"ACC token refresh failed (HTTP {(int)resp.StatusCode}).");
            }

            var j = JObject.Parse(body);
            string? access = (string?)j["access_token"];
            if (string.IsNullOrEmpty(access))
                return new PlatformTokenResult(false, Error: "ACC token refresh response had no access_token.");
            string refresh = (string?)j["refresh_token"] ?? refreshToken;
            int expiresIn  = (int?)j["expires_in"] ?? 3600;
            var expiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

            connection.AccessToken    = access;
            connection.RefreshToken   = refresh;
            connection.TokenExpiresAt = expiry;
            return new PlatformTokenResult(true, access, refresh, expiry);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ACC token refresh failed");
            return new PlatformTokenResult(false, Error: ex.Message);
        }
        finally { gate.Release(); }
    }

    private async Task<bool> EnsureTokenAsync(PlatformConnection c, CancellationToken ct)
    {
        if (AccTokenRefresher.IsFresh(c, AccTokenRefresher.DefaultBuffer)) return true;
        return (await RefreshTokenAsync(c, ct)).Success;
    }

    public async Task<PlatformTestResult> TestConnectionAsync(PlatformConnection connection, CancellationToken ct = default)
    {
        var (id, secret) = AppCreds();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
            return new PlatformTestResult(false, "Acc:ClientId / Acc:ClientSecret not configured on the server.");
        if (!await EnsureTokenAsync(connection, ct))
            return new PlatformTestResult(false, "Couldn't obtain an ACC access token — (re)connect ACC first.");

        try
        {
            var http = _httpFactory.CreateClient();
            using var resp = await Aps.ApsRetry.SendAsync(http, () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, Aps.ApsEndpoints.HubsUrl(_config));
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);
                return req;
            }, idempotent: true, _logger, ct);
            return resp.IsSuccessStatusCode
                ? new PlatformTestResult(true, "ACC reachable.")
                : new PlatformTestResult(false, $"ACC hubs query returned HTTP {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new PlatformTestResult(false, ex.Message); }
    }

    public async Task<PlatformSyncResult> SyncAsync(PlatformConnection connection, IReadOnlyList<TaggedElement> elements, CancellationToken ct = default)
    {
        if (!await EnsureTokenAsync(connection, ct))
            return new PlatformSyncResult(false, Error: "No valid ACC token — (re)connect ACC first.");
        string project = connection.ExternalProjectId;
        if (string.IsNullOrWhiteSpace(project))
            return new PlatformSyncResult(false, Error: "PlatformConnection.ExternalProjectId (ACC project id) is empty.");

        try
        {
            var http = _httpFactory.CreateClient();
            using var resp = await Aps.ApsRetry.SendAsync(http, () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"{Aps.ApsEndpoints.IssuesProjectUrl(_config, project)}/issues?limit=1");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);
                return req;
            }, idempotent: true, _logger, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new PlatformSyncResult(false, Error: $"ACC issues query HTTP {(int)resp.StatusCode}.");

            // With limit=1, results.Count is at most 1 — it is not a total. Only
            // pagination.totalResults is; its absence is an error, not zero.
            var total = (int?)JObject.Parse(body)["pagination"]?["totalResults"];
            if (total == null)
                return new PlatformSyncResult(false, Error: "ACC issues response had no pagination.totalResults.");
            // Element-centric pull-only path. The issue-centric PUSH lives in AccSyncService.
            return new PlatformSyncResult(true, PushedCount: 0, PulledCount: total.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new PlatformSyncResult(false, Error: ex.Message); }
    }

    public Task<PlatformWebhookResult> HandleWebhookAsync(PlatformConnection connection, string payload, string? signature, CancellationToken ct = default)
        // Inbound Autodesk webhooks are processed by AutodeskWebhooksController; this
        // connector hook just acknowledges so the platform sync pipeline doesn't error.
        => Task.FromResult(new PlatformWebhookResult(true, Action: "acknowledged"));
}

/// <summary>
/// Stub connector for Procore.
/// Replace with real Procore REST API v1.1 calls when integration is configured.
/// </summary>
public class ProcoreConnector : IPlatformConnector
{
    public PlatformType Platform => PlatformType.Procore;

    public Task<PlatformTestResult> TestConnectionAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTestResult(false, "Procore connector not yet configured."));

    public Task<PlatformTokenResult> RefreshTokenAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTokenResult(false, Error: "Procore token refresh not implemented."));

    public Task<PlatformSyncResult> SyncAsync(PlatformConnection connection, IReadOnlyList<TaggedElement> elements, CancellationToken ct = default)
        => Task.FromResult(new PlatformSyncResult(false, Error: "Procore sync not implemented."));

    public Task<PlatformWebhookResult> HandleWebhookAsync(PlatformConnection connection, string payload, string? signature, CancellationToken ct = default)
        => Task.FromResult(new PlatformWebhookResult(false, Error: "Procore webhook handling not implemented."));
}

/// <summary>
/// Stub connector for Oracle Aconex.
/// Replace with real Aconex REST API calls when integration is configured.
/// </summary>
public class AconexConnector : IPlatformConnector
{
    public PlatformType Platform => PlatformType.Aconex;

    public Task<PlatformTestResult> TestConnectionAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTestResult(false, "Aconex connector not yet configured."));

    public Task<PlatformTokenResult> RefreshTokenAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTokenResult(false, Error: "Aconex token refresh not implemented."));

    public Task<PlatformSyncResult> SyncAsync(PlatformConnection connection, IReadOnlyList<TaggedElement> elements, CancellationToken ct = default)
        => Task.FromResult(new PlatformSyncResult(false, Error: "Aconex sync not implemented."));

    public Task<PlatformWebhookResult> HandleWebhookAsync(PlatformConnection connection, string payload, string? signature, CancellationToken ct = default)
        => Task.FromResult(new PlatformWebhookResult(false, Error: "Aconex webhook handling not implemented."));
}

/// <summary>
/// Stub connector for Trimble Connect.
/// Replace with real Trimble Connect API calls when integration is configured.
/// </summary>
public class TrimbleConnector : IPlatformConnector
{
    public PlatformType Platform => PlatformType.Trimble;

    public Task<PlatformTestResult> TestConnectionAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTestResult(false, "Trimble Connect connector not yet configured."));

    public Task<PlatformTokenResult> RefreshTokenAsync(PlatformConnection connection, CancellationToken ct = default)
        => Task.FromResult(new PlatformTokenResult(false, Error: "Trimble token refresh not implemented."));

    public Task<PlatformSyncResult> SyncAsync(PlatformConnection connection, IReadOnlyList<TaggedElement> elements, CancellationToken ct = default)
        => Task.FromResult(new PlatformSyncResult(false, Error: "Trimble sync not implemented."));

    public Task<PlatformWebhookResult> HandleWebhookAsync(PlatformConnection connection, string payload, string? signature, CancellationToken ct = default)
        => Task.FromResult(new PlatformWebhookResult(false, Error: "Trimble webhook handling not implemented."));
}

/// <summary>
/// Resolves the correct IPlatformConnector for a given PlatformType.
/// Falls back to a no-op connector if the platform is unknown.
/// </summary>
public class PlatformConnectorFactory : IPlatformConnectorFactory
{
    private readonly Dictionary<PlatformType, IPlatformConnector> _connectors;
    private readonly ILogger<PlatformConnectorFactory> _logger;

    public PlatformConnectorFactory(IEnumerable<IPlatformConnector> connectors, ILogger<PlatformConnectorFactory> logger)
    {
        _connectors = connectors.ToDictionary(c => c.Platform);
        _logger = logger;
    }

    public IPlatformConnector GetConnector(PlatformType platform)
    {
        if (_connectors.TryGetValue(platform, out var connector))
            return connector;

        _logger.LogWarning("No connector registered for platform {Platform}", platform);
        throw new NotSupportedException($"Platform {platform} is not supported.");
    }
}
