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
/// <c>Acc:ClientSecret</c> (env <c>Acc__ClientId</c> / <c>Acc__ClientSecret</c>).
/// Returns a configuration error when those are absent so the dashboard can show
/// a setup CTA.
///
/// Implemented: OAuth refresh (rotates tokens onto the connection so the caller's
/// SaveChanges persists them), connectivity test (lists hubs), and a pull-sync
/// that reports the ACC Issues count for the connected container. Pushing STING
/// elements as ACC issues is a documented TODO.
///
/// CAVEAT: built to documented APS signatures but NOT yet exercised against a
/// live ACC project or a deployed server.
/// </summary>
public class AccConnector : IPlatformConnector
{
    private const string TokenUrl  = "https://developer.api.autodesk.com/authentication/v2/token";
    private const string HubsUrl   = "https://developer.api.autodesk.com/project/v1/hubs";
    private const string IssuesUrl = "https://developer.api.autodesk.com/construction/issues/v1";

    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AccConnector> _logger;

    // Serialize refreshes per connection id so parallel operations in THIS
    // server instance don't race the rotating refresh token. (Single-instance
    // guard only — full cross-instance safety would need a DB re-read under a
    // distributed lock; tracked as a follow-up.)
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _refreshLocks = new();

    public AccConnector(IConfiguration config, IHttpClientFactory httpFactory, ILogger<AccConnector> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public PlatformType Platform => PlatformType.ACC;

    // IConfiguration already layers in environment variables (Acc__ClientId →
    // Acc:ClientId), so a single indexer lookup covers both file and env config.
    private (string id, string secret) AppCreds() =>
        (_config["Acc:ClientId"] ?? "", _config["Acc:ClientSecret"] ?? "");

    public async Task<PlatformTokenResult> RefreshTokenAsync(PlatformConnection connection, CancellationToken ct = default)
    {
        var (id, secret) = AppCreds();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
            return new PlatformTokenResult(false, Error: "Acc:ClientId / Acc:ClientSecret not configured on the server.");
        if (string.IsNullOrWhiteSpace(connection.RefreshToken))
            return new PlatformTokenResult(false, Error: "No refresh token — connect ACC via /api/acc/oauth/start first.");

        var gate = _refreshLocks.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Another op on this instance may have just refreshed this same
            // tracked entity — reuse it rather than burning the refresh token again.
            if (!string.IsNullOrEmpty(connection.AccessToken)
                && connection.TokenExpiresAt.HasValue
                && connection.TokenExpiresAt.Value > DateTime.UtcNow.AddMinutes(5))
                return new PlatformTokenResult(true, connection.AccessToken, connection.RefreshToken, connection.TokenExpiresAt);

            var http = _httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("refresh_token", connection.RefreshToken!),
                })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{id}:{secret}")));

            var resp = await http.SendAsync(req, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("ACC token refresh HTTP {Status}: {Body}", (int)resp.StatusCode, body);
                return new PlatformTokenResult(false, Error: $"ACC token refresh failed (HTTP {(int)resp.StatusCode}).");
            }

            var j = JObject.Parse(body);
            string access  = (string?)j["access_token"]  ?? "";
            string refresh = (string?)j["refresh_token"] ?? connection.RefreshToken!;
            int expiresIn  = (int?)j["expires_in"] ?? 3600;
            var expiry = DateTime.UtcNow.AddSeconds(expiresIn);

            // Rotate onto the connection so the scoped caller's SaveChanges persists it.
            connection.AccessToken    = access;
            connection.RefreshToken   = refresh;
            connection.TokenExpiresAt = expiry;
            return new PlatformTokenResult(true, access, refresh, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ACC token refresh failed");
            return new PlatformTokenResult(false, Error: ex.Message);
        }
        finally { gate.Release(); }
    }

    /// <summary>Refresh the access token when missing or within 5 min of expiry.</summary>
    private async Task<bool> EnsureTokenAsync(PlatformConnection c, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(c.AccessToken)
            && c.TokenExpiresAt.HasValue
            && c.TokenExpiresAt.Value > DateTime.UtcNow.AddMinutes(5))
            return true;
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
            using var req = new HttpRequestMessage(HttpMethod.Get, HubsUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);
            var resp = await http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode
                ? new PlatformTestResult(true, "ACC reachable.")
                : new PlatformTestResult(false, $"ACC hubs query returned HTTP {(int)resp.StatusCode}.");
        }
        catch (Exception ex) { return new PlatformTestResult(false, ex.Message); }
    }

    public async Task<PlatformSyncResult> SyncAsync(PlatformConnection connection, IReadOnlyList<TaggedElement> elements, CancellationToken ct = default)
    {
        if (!await EnsureTokenAsync(connection, ct))
            return new PlatformSyncResult(false, Error: "No valid ACC token — (re)connect ACC first.");
        string container = connection.ExternalProjectId;
        if (string.IsNullOrWhiteSpace(container))
            return new PlatformSyncResult(false, Error: "PlatformConnection.ExternalProjectId (ACC Issues container) is empty.");

        try
        {
            var http = _httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{IssuesUrl}/containers/{container}/issues?limit=1");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);
            var resp = await http.SendAsync(req, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new PlatformSyncResult(false, Error: $"ACC issues query HTTP {(int)resp.StatusCode}.");

            var j = JObject.Parse(body);
            int total = (int?)j["pagination"]?["totalResults"] ?? ((j["results"] as JArray)?.Count ?? 0);
            // Element-centric pull-only path. The issue-centric PUSH (open Planscape
            // BimIssues → ACC issues) lives in AccSyncService — it needs DB + BimIssue
            // access the connector interface deliberately doesn't expose.
            return new PlatformSyncResult(true, PushedCount: 0, PulledCount: total);
        }
        catch (Exception ex) { return new PlatformSyncResult(false, Error: ex.Message); }
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
