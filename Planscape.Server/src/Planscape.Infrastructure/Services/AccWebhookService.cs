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
/// for one ACC connection, and browses the ACC folders a Data Management hook
/// can be scoped to.
///
/// WHY: the receiver used to find the connection from <c>payload.projectId</c>,
/// a field nothing verified. Hooks created here carry
/// <c>?connectionId=&lt;PlatformConnection.Id&gt;</c> in their callback URL and
/// their hook ids are recorded on the connection, so the receiver resolves the
/// connection from the URL and can check the delivering hook is one of ours.
///
/// APS CONTRACT — checked 2026-09-30 against the APS OpenAPI specs
/// (github.com/autodesk-platform-services/aps-sdk-openapi, webhooks/webhooks.yaml
/// and datamanagement/datamanagement.yaml):
///   * create = <c>POST /webhooks/v1/systems/{system}/events/{event}/hooks</c> with
///     <c>{ callbackUrl, scope, hookAttribute, autoReactivateHook }</c>; 201 with the
///     hook URL in <c>Location</c> (last segment = hook id); <b>409 "The specified
///     hook already exists"</b> — then the existing hook is looked up with
///     <c>GET …/hooks?scopeName=&amp;scopeValue=</c> and ADOPTED when its callbackUrl
///     is ours (otherwise reported, never recorded).
///   * events: <c>dm.version.added</c> and <c>dm.version.modified</c> (system
///     <c>data</c>) and <c>issue.created-1.0</c> / <c>issue.updated-1.0</c> (system
///     <c>autodesk.construction.issues</c>) are all in the spec's <c>Events</c> enum.
///     There is NO endpoint listing a system's events, so names are validated
///     against <see cref="KnownEvents"/> (a copy of that enum); an unknown
///     configured name is skipped and REPORTED, the rest still subscribe.
///   * secret token: <c>POST /webhooks/v1/tokens</c> = create (200; <b>400 "Secret
///     token already exists"</b>), <c>PUT /webhooks/v1/tokens/@me</c> = update (204;
///     404 when there is none). Per APS app + user (3-legged). Create first, update
///     on 400, and create again when an update answers 404 — see <see cref="SetSecretAsync"/>.
///     APS notes a secret change can take up to 10 minutes to apply.
///   * <c>x-ads-region</c>: a documented header on every webhooks operation, values
///     US / EMEA / AUS / CAN / DEU / IND / JPN / GBR. It is also the region APS
///     calls the callback FROM. Sent when the connection has an <c>accRegion</c>.
///   * DELETE = <c>…/hooks/{hookId}</c>.
///   * Data Management folders: <c>GET /project/v1/hubs/{hub}/projects/{b.project}/topFolders</c>
///     (<c>projectFilesOnly</c>, <c>excludeDeleted</c>) and
///     <c>GET /data/v1/projects/{b.project}/folders/{urn}/contents?filter[type]=folders</c>
///     with <c>links.next</c> paging.
///
/// NOT EXERCISED against a live APS tenant.
/// </summary>
public class AccWebhookService
{
    public const string SystemIssues = "autodesk.construction.issues";
    public const string SystemData = "data";
    /// <summary>AUT-5: Forma/ACC Reviews (webhooks/v1/tutorials/create-a-hook-reviews,
    /// read 2026-10-01): scope <c>{"project": "&lt;uuid&gt;"}</c>, 3-legged token only, data:read +
    /// data:create to create. Delivery is filtered by what the creating user may see.</summary>
    public const string SystemReviews = "autodesk.construction.reviews";
    public static readonly IReadOnlyList<string> IssueEvents = new[] { "issue.created-1.0", "issue.updated-1.0" };
    /// <summary>Only the close: that is when ACC's decision exists to be read back.</summary>
    public static readonly IReadOnlyList<string> ReviewEvents = new[] { "review.closed-1.0" };
    public static readonly IReadOnlyList<string> DataEvents = new[] { "dm.version.added", "dm.version.modified" };
    public const string ReceiverPath = "/api/webhooks/autodesk/event";

    /// <summary>Server-owned ConfigJson key: how the APS secret was last set, and when.</summary>
    public const string KeySecretSetBy = "accWebhookSecretSetBy";

    /// <summary>
    /// The event names this code can subscribe to, per system — copied from the
    /// <c>Events</c> enum of the APS webhooks OpenAPI spec (2026-09-30) for the two
    /// systems Planscape uses. APS has no "list events for a system" endpoint, so
    /// this table IS the validation. Extend it when Autodesk documents new events.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> KnownEvents =
        new Dictionary<string, IReadOnlySet<string>>
        {
            [SystemData] = new HashSet<string>
            {
                "dm.version.added", "dm.version.modified", "dm.version.deleted", "dm.version.moved",
                "dm.version.moved.out", "dm.version.copied", "dm.version.copied.out",
                "dm.lineage.reserved", "dm.lineage.unreserved", "dm.lineage.updated",
                "dm.folder.added", "dm.folder.modified", "dm.folder.deleted", "dm.folder.purged",
                "dm.folder.moved", "dm.folder.moved.out", "dm.folder.copied", "dm.folder.copied.out",
                "dm.operation.started", "dm.operation.completed",
            },
            [SystemReviews] = new HashSet<string> { "review.created-1.0", "review.closed-1.0" },
            [SystemIssues] = new HashSet<string>
            {
                "issue.created-1.0", "issue.updated-1.0", "issue.deleted-1.0", "issue.restored-1.0", "issue.unlinked-1.0",
            },
        };

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
    /// <param name="SecretSetBy">How the APS secret was set this run ("POST /webhooks/v1/tokens (created)" or "PUT /webhooks/v1/tokens/@me (updated)").</param>
    /// <param name="FolderSource">"request" when folder URNs were given, "project top folders" when defaulted, "none" when an empty list was given.</param>
    /// <param name="Folders">The folder URNs the Data Management hooks were scoped to this run.</param>
    /// <param name="Warnings">Non-fatal notes (skipped unknown event names, adopted pre-existing hooks).</param>
    public sealed record Result(string Status, IReadOnlyList<HookRecord> Hooks, IReadOnlyList<string> Errors,
        string? SecretSetBy = null, string? FolderSource = null, IReadOnlyList<string>? Folders = null,
        IReadOnlyList<string>? Warnings = null)
    {
        public bool Success => Status == AccSyncService.StatusOk;
    }

    public sealed record FolderInfo(string Urn, string Name, bool Hidden, int? ObjectCount);

    /// <param name="Status">OK, FAILED or RECONNECT_REQUIRED.</param>
    public sealed record FolderListing(string Status, string? ParentUrn, IReadOnlyList<FolderInfo> Folders, string? Error);

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

    /// <summary>
    /// The events to subscribe for one system: the configured list
    /// (<c>Autodesk:WebhookEvents:{system}</c>, comma-separated) or the default,
    /// split into those <see cref="KnownEvents"/> recognises and those it does not.
    /// </summary>
    public static (IReadOnlyList<string> Valid, IReadOnlyList<string> Unknown) ResolveEvents(IConfiguration config, string system, IReadOnlyList<string> defaults)
    {
        string? raw = config[$"Autodesk:WebhookEvents:{system}"];
        var wanted = string.IsNullOrWhiteSpace(raw)
            ? defaults
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        var known = KnownEvents.TryGetValue(system, out var k) ? k : new HashSet<string>();
        return (wanted.Where(known.Contains).ToList(), wanted.Where(e => !known.Contains(e)).ToList());
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
    /// Create the issue hooks (project scope) and the Data Management version hooks
    /// (folder scope — DM hooks cannot be scoped to a whole project).
    ///
    /// FOLDERS: <paramref name="folderUrns"/> null → the project's top folders
    /// (Project Files, via topFolders?projectFilesOnly=true; a DM hook on a folder
    /// covers its subfolders) — reported in the result. An EMPTY list → no DM hooks.
    ///
    /// Idempotent: a (system, event, scope) already recorded is not created again.
    /// Each created hook is SAVED immediately, so a failure half way leaves an
    /// accurate record of what exists in APS.
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
        var errors = new List<string>();
        var warnings = new List<string>();

        // The secret first: a hook created before its signing secret matches ours
        // would deliver events the receiver can only reject.
        var (secretBy, secretError) = await SetSecretAsync(http, conn.AccessToken!, secret, region, ct);
        if (secretError != null) return Failed(secretError);
        cfg[KeySecretSetBy] = new JObject { ["method"] = secretBy, ["at"] = DateTime.UtcNow.ToString("o") };
        conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
        await _db.SaveChangesAsync(ct);

        // Event names: validated against the spec's enum (APS offers no list endpoint).
        var (issueEvents, unknownIssue) = ResolveEvents(_config, SystemIssues, IssueEvents);
        var (dataEvents, unknownData) = ResolveEvents(_config, SystemData, DataEvents);
        var (reviewEvents, unknownReview) = ResolveEvents(_config, SystemReviews, ReviewEvents);
        foreach (var u in unknownReview) warnings.Add($"Skipped unknown {SystemReviews} event '{u}' (not in the APS webhooks event list).");
        foreach (var u in unknownIssue) warnings.Add($"Skipped unknown {SystemIssues} event '{u}' (not in the APS webhooks event list).");
        foreach (var u in unknownData) warnings.Add($"Skipped unknown {SystemData} event '{u}' (not in the APS webhooks event list).");

        // Folders: as given, or the project's top folders when none were given.
        string folderSource;
        IReadOnlyList<string> folders;
        if (folderUrns != null)
        {
            folders = folderUrns.Select(f => f.Trim()).Where(f => f.Length > 0).Distinct().ToList();
            folderSource = folders.Count > 0 ? "request" : "none";
        }
        else
        {
            folderSource = "project top folders";
            var (top, topErr) = await TopFoldersAsync(http, conn, cfg, projectFilesOnly: true, ct);
            if (top == null)
            {
                folders = Array.Empty<string>();
                errors.Add($"Data Management hooks not created: {topErr} Pass folderUrns explicitly (GET acc/folders lists them).");
            }
            else
            {
                folders = top.Where(f => !f.Hidden).Select(f => f.Urn).ToList();
                if (folders.Count == 0)
                    errors.Add("Data Management hooks not created: the ACC grant sees no top folders in this project. Pass folderUrns explicitly.");
            }
        }

        var wanted = new List<(string System, string Event, string ScopeKey, string ScopeValue)>();
        string accProject = ApsEndpoints.StripHubPrefix(conn.ExternalProjectId);
        foreach (var ev in issueEvents) wanted.Add((SystemIssues, ev, "project", accProject));
        foreach (var ev in reviewEvents) wanted.Add((SystemReviews, ev, "project", accProject));
        foreach (var folder in folders)
            foreach (var ev in dataEvents) wanted.Add((SystemData, ev, "folder", folder));

        var hooks = ReadHooks(cfg);
        int created = 0;
        foreach (var w in wanted)
        {
            if (hooks.Any(h => h.System == w.System && h.Event == w.Event && h.ScopeKey == w.ScopeKey && h.ScopeValue == w.ScopeValue))
                continue;
            var (hookId, adopted, err) = await CreateHookAsync(http, conn, w.System, w.Event, w.ScopeKey, w.ScopeValue, callback, region, ct);
            if (hookId == null) { errors.Add($"{w.System}/{w.Event} ({w.ScopeKey} {w.ScopeValue}): {err}"); continue; }
            if (adopted) warnings.Add($"{w.System}/{w.Event} ({w.ScopeKey} {w.ScopeValue}): already existed in APS with our callback URL — adopted hook {hookId}.");
            hooks.Add(new HookRecord(w.System, w.Event, hookId, w.ScopeKey, w.ScopeValue, DateTime.UtcNow));
            WriteHooks(conn, cfg, hooks);
            await _db.SaveChangesAsync(ct);
            created++;
        }

        string status = errors.Count == 0 ? AccSyncService.StatusOk
            : created > 0 || hooks.Count > 0 ? AccSyncService.StatusPartial : AccSyncService.StatusFailed;
        if (errors.Count > 0 || warnings.Count > 0)
            _logger.LogWarning("ACC webhooks for connection {Id}: {Status}; errors: {Errors}; warnings: {Warnings}",
                conn.Id, status, string.Join(" | ", errors), string.Join(" | ", warnings));
        return new Result(status, hooks, errors, secretBy, folderSource, folders, warnings);
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

    // ── folder browsing (ACC-SRV-6) ─────────────────────────────────────────

    /// <summary>
    /// The folders a Data Management hook can be scoped to. No <paramref name="parentUrn"/>:
    /// the project's top folders the grant can see (all of them, hidden flagged).
    /// With one: that folder's sub-folders (<c>filter[type]=folders</c>, every
    /// <c>links.next</c> page followed).
    /// </summary>
    public async Task<FolderListing> ListFoldersAsync(Guid projectId, string? parentUrn, CancellationToken ct)
    {
        var conn = await FindActiveAsync(projectId, ct);
        if (conn == null) return new FolderListing(AccSyncService.StatusFailed, parentUrn, Array.Empty<FolderInfo>(), "No active ACC connection for this project.");
        if (string.IsNullOrWhiteSpace(conn.ExternalProjectId))
            return new FolderListing(AccSyncService.StatusFailed, parentUrn, Array.Empty<FolderInfo>(), "Choose the ACC project first (ExternalProjectId is empty).");
        if (!AccSyncService.TryParseConfig(conn, out var cfg, out var cfgErr))
            return new FolderListing(AccSyncService.StatusFailed, parentUrn, Array.Empty<FolderInfo>(), cfgErr);

        var tok = await AccTokenRefresher.EnsureFreshAsync(_db, _connectorFactory.GetConnector(PlatformType.ACC), conn, _logger, ct);
        if (!tok.Success)
            return new FolderListing(tok.ReconnectRequired ? AccSyncService.StatusReconnect : AccSyncService.StatusFailed,
                parentUrn, Array.Empty<FolderInfo>(), $"Couldn't obtain an ACC access token. {tok.Error}");

        var http = _httpFactory.CreateClient();
        if (string.IsNullOrWhiteSpace(parentUrn))
        {
            var (top, err) = await TopFoldersAsync(http, conn, cfg, projectFilesOnly: false, ct);
            return top == null
                ? new FolderListing(AccSyncService.StatusFailed, null, Array.Empty<FolderInfo>(), err)
                : new FolderListing(AccSyncService.StatusOk, null, top, null);
        }

        string url = $"{ApsEndpoints.BaseUrl(_config)}/data/v1/projects/{Uri.EscapeDataString(DmProjectId(conn))}" +
                     $"/folders/{Uri.EscapeDataString(parentUrn.Trim())}/contents?filter[type]=folders";
        var (items, e) = await GetDmPagedAsync(http, conn.AccessToken!, url, ct);
        return items == null
            ? new FolderListing(AccSyncService.StatusFailed, parentUrn, Array.Empty<FolderInfo>(), e)
            : new FolderListing(AccSyncService.StatusOk, parentUrn, ToFolders(items), null);
    }

    private async Task<(IReadOnlyList<FolderInfo>? folders, string? error)> TopFoldersAsync(HttpClient http, PlatformConnection conn,
        JObject cfg, bool projectFilesOnly, CancellationToken ct)
    {
        string? hub = (string?)cfg[AccSyncService.KeyHubId];
        if (string.IsNullOrWhiteSpace(hub))
            return (null, "The ACC hub is not recorded on the connection (PUT acc/selection with hubId).");
        string url = $"{ApsEndpoints.HubsUrl(_config)}/{Uri.EscapeDataString(hub.Trim())}/projects/{Uri.EscapeDataString(DmProjectId(conn))}" +
                     $"/topFolders?excludeDeleted=true{(projectFilesOnly ? "&projectFilesOnly=true" : "")}";
        var (items, err) = await GetDmPagedAsync(http, conn.AccessToken!, url, ct);
        return items == null ? (null, err) : (ToFolders(items), null);
    }

    private static IReadOnlyList<FolderInfo> ToFolders(IEnumerable<JToken> items)
        => items.Where(d => (string?)d["type"] == "folders")
            .Select(d => new FolderInfo(
                (string?)d["id"] ?? "",
                (string?)d["attributes"]?["displayName"] ?? (string?)d["attributes"]?["name"] ?? "",
                (bool?)d["attributes"]?["hidden"] ?? false,
                (int?)d["attributes"]?["objectCount"]))
            .Where(f => f.Urn.Length > 0)
            .ToList();

    /// <summary>Data Management addresses projects as <c>b.&lt;guid&gt;</c>.</summary>
    private static string DmProjectId(PlatformConnection conn) => "b." + ApsEndpoints.StripHubPrefix(conn.ExternalProjectId!.Trim());

    private async Task<(IReadOnlyList<JToken>? items, string? error)> GetDmPagedAsync(HttpClient http, string token, string firstUrl, CancellationToken ct)
    {
        var items = new List<JToken>();
        string? url = firstUrl;
        try
        {
            for (int page = 0; url != null && page < 100; page++)
            {
                string current = url;
                using var resp = await ApsRetry.SendAsync(http, () => Request(HttpMethod.Get, current, token, null, null), true, _logger, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("APS Data Management HTTP {Status}: {Body}", (int)resp.StatusCode, body.Length > 500 ? body[..500] : body);
                    return (null, $"APS Data Management query failed (HTTP {(int)resp.StatusCode}).");
                }
                var j = JObject.Parse(body);
                if (j["data"] is not JArray data) return (null, "APS Data Management response had no data array.");
                items.AddRange(data);
                var next = j["links"]?["next"];
                url = next?.Type == JTokenType.Object ? (string?)next["href"] : next?.Type == JTokenType.String ? (string?)next : null;
            }
            // D8: a next link left over at the guard means the list is not complete.
            if (url != null) return (null, $"stopped after 100 pages ({items.Count} entries) with more to read - the list is INCOMPLETE.");
            return (items, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, $"APS Data Management query failed: {ex.Message}");
        }
    }

    // ── APS webhook calls ────────────────────────────────────────────────────

    private string HooksUrl(string system, string ev)
        => $"{ApsEndpoints.BaseUrl(_config)}/webhooks/v1/systems/{Uri.EscapeDataString(system)}/events/{Uri.EscapeDataString(ev)}/hooks";

    private async Task<(string? hookId, bool adopted, string? error)> CreateHookAsync(HttpClient http, PlatformConnection conn,
        string system, string ev, string scopeKey, string scopeValue, string callback, string? region, CancellationToken ct)
    {
        var body = new JObject
        {
            ["callbackUrl"] = callback,
            ["scope"] = new JObject { [scopeKey] = scopeValue },
            ["hookAttribute"] = new JObject { ["planscapeConnectionId"] = conn.Id.ToString() },
            // Documented: re-activate a hook APS deactivated after failed deliveries.
            ["autoReactivateHook"] = true,
        }.ToString(Newtonsoft.Json.Formatting.None);
        try
        {
            // Create is not idempotent: ApsRetry retries only 429 / 503+Retry-After.
            using var resp = await ApsRetry.SendAsync(http,
                () => Request(HttpMethod.Post, HooksUrl(system, ev), conn.AccessToken!, region, body), false, _logger, ct);
            if (resp.StatusCode == HttpStatusCode.Conflict)
            {
                // Documented 409 "hook already exists": adopt it when it is ours.
                var (existing, findErr) = await FindExistingHookAsync(http, conn.AccessToken!, system, ev, scopeKey, scopeValue, callback, region, ct);
                if (existing != null) return (existing, true, null);
                return (null, false, "APS reports this hook already exists but not with our callback URL " +
                                     $"({findErr ?? "no match"}) — it is not recorded; delete it in APS if it is stale.");
            }
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("APS hook create {System}/{Event} HTTP {Status}: {Body}", system, ev, (int)resp.StatusCode,
                    text.Length > 500 ? text[..500] : text);
                return (null, false, $"APS rejected the hook (HTTP {(int)resp.StatusCode}).");
            }
            var location = resp.Headers.Location?.ToString();
            var id = location?.TrimEnd('/').Split('/').LastOrDefault();
            if (string.IsNullOrWhiteSpace(id))
                return (null, false, "APS created the hook but returned no Location header — its id is unknown, so it cannot be recorded or removed.");
            return (id, false, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, false, ex.Message);
        }
    }

    /// <summary>
    /// <c>GET …/hooks?scopeName=&amp;scopeValue=</c>, every page: the id of the hook
    /// whose callbackUrl is exactly ours, else null with the reason.
    /// </summary>
    private async Task<(string? hookId, string? error)> FindExistingHookAsync(HttpClient http, string token, string system, string ev,
        string scopeKey, string scopeValue, string callback, string? region, CancellationToken ct)
    {
        string root = $"{ApsEndpoints.BaseUrl(_config)}/webhooks/v1";
        string? url = $"{HooksUrl(system, ev)}?scopeName={Uri.EscapeDataString(scopeKey)}&scopeValue={Uri.EscapeDataString(scopeValue)}";
        for (int page = 0; url != null && page < 20; page++)
        {
            string current = url;
            using var resp = await ApsRetry.SendAsync(http, () => Request(HttpMethod.Get, current, token, region, null), true, _logger, ct);
            if (resp.StatusCode == HttpStatusCode.NoContent) return (null, "APS lists no hooks for that scope");
            if (!resp.IsSuccessStatusCode) return (null, $"hook lookup HTTP {(int)resp.StatusCode}");
            var j = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
            foreach (var h in (j["data"] as JArray ?? new JArray()).OfType<JObject>())
                if (string.Equals((string?)h["callbackUrl"], callback, StringComparison.Ordinal) && !string.IsNullOrEmpty((string?)h["hookId"]))
                    return ((string?)h["hookId"], null);
            // links.next is relative to /webhooks/v1 in the spec examples ("/systems/…?pageState=…").
            string? next = (string?)j["links"]?["next"];
            url = string.IsNullOrEmpty(next) ? null
                : next.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? next : root + next;
        }
        return (null, "no hook with our callback URL");
    }

    /// <summary>
    /// Set the APS webhook secret token to ours. Documented behaviour: POST
    /// <c>/webhooks/v1/tokens</c> creates (200) and answers 400 "Secret token already
    /// exists"; PUT <c>/webhooks/v1/tokens/@me</c> updates (204) and answers 404 when
    /// there is none. So: create; on anything but success try the update; if the
    /// update says there is none, that contradicts the create's answer and both
    /// codes are reported. Returns which call worked.
    /// </summary>
    internal async Task<(string? setBy, string? error)> SetSecretAsync(HttpClient http, string accessToken, string secret, string? region, CancellationToken ct)
    {
        string body = new JObject { ["token"] = secret }.ToString(Newtonsoft.Json.Formatting.None);
        string root = $"{ApsEndpoints.BaseUrl(_config)}/webhooks/v1/tokens";
        try
        {
            int postCode;
            using (var post = await ApsRetry.SendAsync(http, () => Request(HttpMethod.Post, root, accessToken, region, body), false, _logger, ct))
            {
                if (post.IsSuccessStatusCode) return ("POST /webhooks/v1/tokens (created)", null);
                postCode = (int)post.StatusCode;
                if (post.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return (null, $"APS refused to set the webhook secret (POST tokens HTTP {postCode}) — the grant needs data:read data:write, and data:create to create hooks — reconnect ACC if it was connected before 2026-10-01.");
            }
            using var put = await ApsRetry.SendAsync(http, () => Request(HttpMethod.Put, root + "/@me", accessToken, region, body), true, _logger, ct);
            if (put.IsSuccessStatusCode)
            {
                if (postCode != (int)HttpStatusCode.BadRequest)
                    _logger.LogWarning("APS webhook secret: create answered HTTP {Post} (documented: 400 when one exists); update succeeded.", postCode);
                return ("PUT /webhooks/v1/tokens/@me (updated)", null);
            }
            return (null, $"Could not set the APS webhook secret: create (POST tokens) HTTP {postCode}, update (PUT tokens/@me) HTTP {(int)put.StatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, $"Could not set the APS webhook secret: {ex.Message}");
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
