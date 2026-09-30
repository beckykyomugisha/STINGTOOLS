using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services.Aps;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Registers (and removes) the APS webhooks that feed AutodeskWebhooksController
/// for one ACC connection.
///
/// WHY: the receiver used to find the connection from <c>payload.projectId</c>,
/// a field nothing verified. Hooks created here carry
/// <c>?connectionId=&lt;PlatformConnection.Id&gt;</c> in their callback URL and
/// their hook ids are recorded on the connection, so the receiver resolves the
/// connection from the URL and can check the delivering hook is one of ours.
///
/// APS CONTRACT — what is confirmed and what is not (checked 2026-09-30)
///   * CONFIRMED: create = <c>POST /webhooks/v1/systems/{system}/events/{event}/hooks</c>
///     with <c>{ callbackUrl, scope, hookAttribute }</c>; 201 with an empty body and
///     the hook URL in the <c>Location</c> header, whose last segment is the hook id.
///   * CONFIRMED: ACC Issues — system <c>autodesk.construction.issues</c>, events
///     <c>issue.created-1.0</c> / <c>issue.updated-1.0</c>, scope <c>{ "project": id }</c>,
///     3-legged token of a Project Admin (APS blog "Webhook API of Forma Issue").
///   * CONFIRMED: Data Management — system <c>data</c>, event <c>dm.version.added</c>,
///     scope <c>{ "folder": folderUrn }</c> (APS DM hook tutorial).
///   * UNCONFIRMED: <c>dm.version.modified</c> is taken from the APS DM event list
///     but its page was not readable in this environment.
///   * UNCONFIRMED: secret token endpoints <c>PUT /webhooks/v1/tokens/@me</c>
///     (update) and <c>POST /webhooks/v1/tokens</c> (create) with <c>{ "token" }</c>.
///     The secret is per APS app + user, so it is set once and every hook the
///     caller owns is signed with it; it must equal <c>Autodesk:WebhookSecret</c>.
///   * UNCONFIRMED: the <c>x-ads-region</c> header on webhook calls (sent when the
///     connection has an <c>accRegion</c>; APS ignores unknown headers).
///   * DELETE = <c>/webhooks/v1/systems/{system}/events/{event}/hooks/{hookId}</c>.
///
/// NOT EXERCISED against a live APS tenant.
/// </summary>
public class AccWebhookService
{
    public const string SystemIssues = "autodesk.construction.issues";
    public const string SystemData = "data";
    public static readonly IReadOnlyList<string> IssueEvents = new[] { "issue.created-1.0", "issue.updated-1.0" };
    public static readonly IReadOnlyList<string> DataEvents = new[] { "dm.version.added", "dm.version.modified" };
    public const string ReceiverPath = "/api/webhooks/autodesk/event";

    private readonly PlanscapeDbContext _db;
    private readonly IPlatformConnectorFactory _connectorFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AccWebhookService> _logger;
    private readonly IConfiguration _config;

    public AccWebhookService(PlanscapeDbContext db, IPlatformConnectorFactory connectorFactory,
        IHttpClientFactory httpFactory, ILogger<AccWebhookService> logger, IConfiguration config)
    {
        _db = db;
        _connectorFactory = connectorFactory;
        _httpFactory = httpFactory;
        _logger = logger;
        _config = config;
    }

    public sealed record HookRecord(string System, string Event, string HookId, string ScopeKey, string ScopeValue, DateTime CreatedAt);

    /// <param name="Status">OK · PARTIAL · FAILED · RECONNECT_REQUIRED (same vocabulary as AccSyncService).</param>
    public sealed record Result(string Status, IReadOnlyList<HookRecord> Hooks, IReadOnlyList<string> Errors)
    {
        public bool Success => Status == AccSyncService.StatusOk;
    }

    private static Result Failed(string error) => new(AccSyncService.StatusFailed, Array.Empty<HookRecord>(), new[] { error });

    /// <summary>
    /// The receiver URL for one connection: <c>Autodesk:WebhookCallbackUrl</c>, else
    /// the origin of <c>Acc:CallbackUrl</c> (the OAuth callback, already configured
    /// wherever ACC is connected) + <see cref="ReceiverPath"/>. Must be https —
    /// APS will not deliver elsewhere, and a plain-http secret-signed callback is a
    /// misconfiguration worth refusing. Null when neither is usable.
    /// </summary>
    public static string? CallbackUrl(IConfiguration config, Guid connectionId)
    {
        string? baseUrl = config["Autodesk:WebhookCallbackUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!Uri.TryCreate(config["Acc:CallbackUrl"], UriKind.Absolute, out var oauth)) return null;
            baseUrl = oauth.GetLeftPart(UriPartial.Authority) + ReceiverPath;
        }
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return null;
        string sep = string.IsNullOrEmpty(u.Query) ? "?" : "&";
        return $"{u.GetLeftPart(UriPartial.Path)}{u.Query}{sep}connectionId={connectionId}";
    }

    /// <summary>Hooks recorded on a connection's ConfigJson (server-owned key).</summary>
    public static List<HookRecord> ReadHooks(JObject cfg)
    {
        var list = new List<HookRecord>();
        if (cfg[AccSyncService.KeyWebhookHooks] is not JArray arr) return list;
        foreach (var h in arr.OfType<JObject>())
        {
            string? id = (string?)h["hookId"];
            if (string.IsNullOrEmpty(id)) continue;
            list.Add(new HookRecord((string?)h["system"] ?? "", (string?)h["event"] ?? "", id,
                (string?)h["scopeKey"] ?? "", (string?)h["scopeValue"] ?? "",
                (DateTime?)h["createdAt"] ?? DateTime.MinValue));
        }
        return list;
    }

    private static void WriteHooks(PlatformConnection conn, JObject cfg, IEnumerable<HookRecord> hooks)
    {
        cfg[AccSyncService.KeyWebhookHooks] = new JArray(hooks.Select(h => new JObject
        {
            ["system"] = h.System, ["event"] = h.Event, ["hookId"] = h.HookId,
            ["scopeKey"] = h.ScopeKey, ["scopeValue"] = h.ScopeValue, ["createdAt"] = h.CreatedAt.ToString("o"),
        }));
        conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>
    /// Create the issue hooks (project scope) and, for each folder URN given, the
    /// Data Management version hooks (folder scope — DM hooks cannot be scoped to a
    /// whole project). Idempotent: a (system, event, scope) already recorded is
    /// not created again. Each created hook is SAVED immediately, so a failure
    /// half way leaves an accurate record of what exists in APS.
    /// </summary>
    public async Task<Result> SubscribeAsync(Guid projectId, IReadOnlyList<string>? folderUrns, CancellationToken ct)
    {
        var conn = await FindActiveAsync(projectId, ct);
        if (conn == null) return Failed("No active ACC connection for this project.");
        if (string.IsNullOrWhiteSpace(conn.ExternalProjectId))
            return Failed("Choose the ACC project first (ExternalProjectId is empty).");
        if (!AccSyncService.TryParseConfig(conn, out var cfg, out var cfgErr)) return Failed(cfgErr!);

        string secret = _config["Autodesk:WebhookSecret"] ?? "";
        if (secret.Length == 0)
            return Failed("Autodesk:WebhookSecret is not configured — the receiver would reject every delivery.");
        string? callback = CallbackUrl(_config, conn.Id);
        if (callback == null)
            return Failed("No https webhook callback URL: set Autodesk:WebhookCallbackUrl (or an https Acc:CallbackUrl).");

        var tok = await AccTokenRefresher.EnsureFreshAsync(_db, _connectorFactory.GetConnector(PlatformType.ACC), conn, _logger, ct);
        if (!tok.Success)
            return new Result(tok.ReconnectRequired ? AccSyncService.StatusReconnect : AccSyncService.StatusFailed,
                Array.Empty<HookRecord>(), new[] { $"Couldn't obtain an ACC access token. {tok.Error}" });

        string? region = (string?)cfg[AccSyncService.KeyRegion];
        var http = _httpFactory.CreateClient();

        // The secret first: a hook created before its signing secret matches ours
        // would deliver events the receiver can only reject.
        var secretError = await SetSecretAsync(http, conn.AccessToken!, secret, region, ct);
        if (secretError != null) return Failed(secretError);

        var wanted = new List<(string System, string Event, string ScopeKey, string ScopeValue)>();
        string accProject = ApsEndpoints.StripHubPrefix(conn.ExternalProjectId);
        foreach (var ev in IssueEvents) wanted.Add((SystemIssues, ev, "project", accProject));
        foreach (var folder in (folderUrns ?? Array.Empty<string>()).Select(f => f.Trim()).Where(f => f.Length > 0).Distinct())
            foreach (var ev in DataEvents) wanted.Add((SystemData, ev, "folder", folder));

        var hooks = ReadHooks(cfg);
        var errors = new List<string>();
        int created = 0;
        foreach (var w in wanted)
        {
            if (hooks.Any(h => h.System == w.System && h.Event == w.Event && h.ScopeKey == w.ScopeKey && h.ScopeValue == w.ScopeValue))
                continue;
            var (hookId, err) = await CreateHookAsync(http, conn, w.System, w.Event, w.ScopeKey, w.ScopeValue, callback, region, ct);
            if (hookId == null) { errors.Add($"{w.System}/{w.Event} ({w.ScopeKey}): {err}"); continue; }
            hooks.Add(new HookRecord(w.System, w.Event, hookId, w.ScopeKey, w.ScopeValue, DateTime.UtcNow));
            WriteHooks(conn, cfg, hooks);
            await _db.SaveChangesAsync(ct);
            created++;
        }

        string status = errors.Count == 0 ? AccSyncService.StatusOk
            : created > 0 || hooks.Count > 0 ? AccSyncService.StatusPartial : AccSyncService.StatusFailed;
        if (errors.Count > 0)
            _logger.LogWarning("ACC webhooks for connection {Id}: {Status}; {Errors}", conn.Id, status, string.Join(" | ", errors));
        return new Result(status, hooks, errors);
    }

    /// <summary>Delete every recorded hook. A hook APS no longer has (404) counts as removed.</summary>
    public async Task<Result> UnsubscribeAsync(Guid projectId, CancellationToken ct)
    {
        var conn = await FindActiveAsync(projectId, ct);
        if (conn == null) return Failed("No active ACC connection for this project.");
        if (!AccSyncService.TryParseConfig(conn, out var cfg, out var cfgErr)) return Failed(cfgErr!);
        var hooks = ReadHooks(cfg);
        if (hooks.Count == 0) return new Result(AccSyncService.StatusOk, hooks, Array.Empty<string>());

        var tok = await AccTokenRefresher.EnsureFreshAsync(_db, _connectorFactory.GetConnector(PlatformType.ACC), conn, _logger, ct);
        if (!tok.Success)
            return new Result(tok.ReconnectRequired ? AccSyncService.StatusReconnect : AccSyncService.StatusFailed,
                hooks, new[] { $"Couldn't obtain an ACC access token. {tok.Error}" });

        string? region = (string?)cfg[AccSyncService.KeyRegion];
        var http = _httpFactory.CreateClient();
        var remaining = new List<HookRecord>();
        var errors = new List<string>();
        foreach (var h in hooks)
        {
            string url = $"{HooksUrl(h.System, h.Event)}/{Uri.EscapeDataString(h.HookId)}";
            try
            {
                using var resp = await ApsRetry.SendAsync(http, () => Request(HttpMethod.Delete, url, conn.AccessToken!, region, null), true, _logger, ct);
                if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.NotFound) continue;
                errors.Add($"{h.System}/{h.Event} {h.HookId}: HTTP {(int)resp.StatusCode}");
                remaining.Add(h);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{h.System}/{h.Event} {h.HookId}: {ex.Message}");
                remaining.Add(h);
            }
        }
        WriteHooks(conn, cfg, remaining);
        await _db.SaveChangesAsync(ct);
        string status = errors.Count == 0 ? AccSyncService.StatusOk
            : remaining.Count < hooks.Count ? AccSyncService.StatusPartial : AccSyncService.StatusFailed;
        return new Result(status, remaining, errors);
    }

    private string HooksUrl(string system, string ev)
        => $"{ApsEndpoints.BaseUrl(_config)}/webhooks/v1/systems/{Uri.EscapeDataString(system)}/events/{Uri.EscapeDataString(ev)}/hooks";

    private async Task<(string? hookId, string? error)> CreateHookAsync(HttpClient http, PlatformConnection conn,
        string system, string ev, string scopeKey, string scopeValue, string callback, string? region, CancellationToken ct)
    {
        var body = new JObject
        {
            ["callbackUrl"] = callback,
            ["scope"] = new JObject { [scopeKey] = scopeValue },
            ["hookAttribute"] = new JObject { ["planscapeConnectionId"] = conn.Id.ToString() },
        }.ToString(Newtonsoft.Json.Formatting.None);
        try
        {
            // Create is not idempotent: ApsRetry retries only 429 / 503+Retry-After.
            using var resp = await ApsRetry.SendAsync(http,
                () => Request(HttpMethod.Post, HooksUrl(system, ev), conn.AccessToken!, region, body), false, _logger, ct);
            if (resp.StatusCode == HttpStatusCode.Conflict)
                return (null, "APS reports this hook already exists (created outside Planscape) — delete it in APS or leave it; it is not recorded here.");
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("APS hook create {System}/{Event} HTTP {Status}: {Body}", system, ev, (int)resp.StatusCode,
                    text.Length > 500 ? text[..500] : text);
                return (null, $"APS rejected the hook (HTTP {(int)resp.StatusCode}).");
            }
            var location = resp.Headers.Location?.ToString();
            var id = location?.TrimEnd('/').Split('/').LastOrDefault();
            if (string.IsNullOrWhiteSpace(id))
                return (null, "APS created the hook but returned no Location header — its id is unknown, so it cannot be recorded or removed.");
            return (id, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>Set the APS webhook secret token to ours: update, else create.</summary>
    private async Task<string?> SetSecretAsync(HttpClient http, string accessToken, string secret, string? region, CancellationToken ct)
    {
        string body = new JObject { ["token"] = secret }.ToString(Newtonsoft.Json.Formatting.None);
        string root = $"{ApsEndpoints.BaseUrl(_config)}/webhooks/v1/tokens";
        try
        {
            using (var put = await ApsRetry.SendAsync(http, () => Request(HttpMethod.Put, root + "/@me", accessToken, region, body), true, _logger, ct))
            {
                if (put.IsSuccessStatusCode) return null;
                if (put.StatusCode != HttpStatusCode.NotFound)
                    return $"Could not set the APS webhook secret (PUT tokens/@me HTTP {(int)put.StatusCode}).";
            }
            using var post = await ApsRetry.SendAsync(http, () => Request(HttpMethod.Post, root, accessToken, region, body), false, _logger, ct);
            return post.IsSuccessStatusCode ? null
                : $"Could not create the APS webhook secret (POST tokens HTTP {(int)post.StatusCode}).";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Could not set the APS webhook secret: {ex.Message}";
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string token, string? region, string? json)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrWhiteSpace(region)) req.Headers.TryAddWithoutValidation("x-ads-region", region);
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return req;
    }

    private Task<PlatformConnection?> FindActiveAsync(Guid projectId, CancellationToken ct)
        => _db.PlatformConnections.FirstOrDefaultAsync(c => c.ProjectId == projectId
                                                         && c.Platform == PlatformType.ACC
                                                         && c.IsActive, ct);
}
