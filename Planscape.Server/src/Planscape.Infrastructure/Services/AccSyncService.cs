using System.Collections.Concurrent;
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
/// Server-side ACC issue sync — the "push Planscape issues → ACC issues" half of
/// the team-shared Autodesk Construction Cloud integration.
///
/// Walks open <see cref="BimIssue"/>s, POSTs the ones not yet pushed to the ACC
/// Issues v1 API (<c>/construction/issues/v1/projects/{id}/issues</c>), and keeps
/// the mapping (Planscape issue id → ACC issue id) in
/// <see cref="PlatformConnection.ConfigJson"/> under <c>accIssueMap</c>.
///
/// IDEMPOTENCY
///   * The map is written and SAVED after EACH successful push, so a crash half
///     way through cannot forget pushes that already landed in ACC (which would
///     re-create them as duplicates on the next run).
///   * One sync per connection at a time: a Postgres session advisory lock
///     (cross-process: API manual /sync vs worker sweep) plus an in-process lock.
///     A second caller gets <see cref="StatusBusy"/>, not a concurrent run.
///   * A ConfigJson that does not parse FAILS the sync. It is never read as {} —
///     an empty map would re-push every open issue.
///   * <see cref="ServerOwnedConfigKeys"/> are preserved by PlatformController PUT.
///
/// STATUS (LastSyncStatus): OK · PARTIAL (some pushes failed, or the ACC status
/// read-back failed) · FAILED (every attempted push failed, the open-count pull
/// failed, or the sync could not start) · BUSY (another sync holds the lock) ·
/// RECONNECT_REQUIRED (the stored tokens cannot be decrypted, there is no refresh
/// token, or ACC answered invalid_grant — a person must reconnect ACC).
///
/// READ-BACK: for every mapped issue the ACC status is fetched and recorded under
/// <c>accIssueStatus</c> (+ <c>accIssueStatusAt</c>), and the report counts how many
/// are closed in ACC. DECISION: Planscape issue status is deliberately NOT changed
/// from ACC. Closing a Planscape issue is an ISO 19650 workflow step with its own
/// audit trail and permissions; an ACC-side close is recorded and reported so a
/// coordinator can act on it, never applied behind their back.
///
/// CAVEAT: built to the documented APS Issues v1 signatures; not yet exercised
/// against a live ACC project.
/// </summary>
public class AccSyncService
{
    public const string StatusOk      = "OK";
    public const string StatusPartial = "PARTIAL";
    public const string StatusFailed  = "FAILED";
    public const string StatusBusy    = "BUSY";
    public const string StatusReconnect = "RECONNECT_REQUIRED";

    public const string KeyIssueMap      = "accIssueMap";
    public const string KeyIssueStatus   = "accIssueStatus";
    public const string KeyIssueStatusAt = "accIssueStatusAt";
    public const string KeySubtypeId     = "accIssueSubtypeId";
    public const string KeyHubId         = "accHubId";
    public const string KeyRegion        = "accRegion";
    public const string KeyWebhookHooks  = "accWebhookHooks";

    /// <summary>ConfigJson keys only the server writes. A client PUT must not replace them.</summary>
    public static readonly IReadOnlyList<string> ServerOwnedConfigKeys = new[] { KeyIssueMap, KeyIssueStatus, KeyIssueStatusAt, KeyWebhookHooks };

    // Documented Issues v1 POST limits.
    private const int TitleMax = 100;
    private const int DescriptionMax = 1000;
    private const int ReadBackBatch = 50;

    private readonly PlanscapeDbContext _db;
    private readonly IPlatformConnectorFactory _connectorFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AccSyncService> _logger;
    private readonly IConfiguration? _config;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _syncLocks = new();

    public AccSyncService(
        PlanscapeDbContext db,
        IPlatformConnectorFactory connectorFactory,
        IHttpClientFactory httpFactory,
        ILogger<AccSyncService> logger,
        IConfiguration? config = null)
    {
        _db = db;
        _connectorFactory = connectorFactory;
        _httpFactory = httpFactory;
        _logger = logger;
        _config = config;
    }

    public sealed record AccSyncReport(
        bool Success,
        string Status,
        int Pushed = 0,
        int Skipped = 0,
        int? PulledOpen = null,
        int Failed = 0,
        int? MappedClosedInAcc = null,
        int? MappedStatusRead = null,
        string? Error = null,
        IReadOnlyList<string>? Failures = null);

    private static AccSyncReport Fail(string error) => new(false, StatusFailed, Error: error);
    private static AccSyncReport Reconnect(string error) => new(false, StatusReconnect, Error: error);

    // ── public entry points ──

    /// <summary>
    /// Sync the single active ACC connection for one project. Tenant-scoped: relies
    /// on the caller's tenant filter (controller path).
    /// </summary>
    public async Task<AccSyncReport> SyncProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var conn = await _db.PlatformConnections
            .FirstOrDefaultAsync(c => c.ProjectId == projectId
                                   && c.Platform == PlatformType.ACC
                                   && c.IsActive, ct);
        if (conn == null)
            return Fail("No active ACC connection for this project.");

        return await SyncLockedAsync(conn, ct);
    }

    /// <summary>
    /// Scheduled sweep: every active ACC connection across all tenants. Each
    /// connection is loaded, synced and SAVED on its own, and a failure clears the
    /// change tracker, so one connection's exception cannot lose another's token
    /// rotation or issue map.
    /// </summary>
    public async Task SyncAllActiveAsync(CancellationToken ct = default)
    {
        _db.BypassTenantFilter = true;
        var ids = await _db.PlatformConnections
            .Where(c => c.IsActive && c.Platform == PlatformType.ACC)
            .Select(c => c.Id)
            .ToListAsync(ct);

        int ok = 0, partial = 0, fail = 0, busy = 0;
        foreach (var id in ids)
        {
            try
            {
                var conn = await _db.PlatformConnections.FirstOrDefaultAsync(c => c.Id == id, ct);
                if (conn == null || !conn.IsActive) continue;
                var r = await SyncLockedAsync(conn, ct);
                switch (r.Status)
                {
                    case StatusOk: ok++; break;
                    case StatusPartial: partial++; break;
                    case StatusBusy: busy++; break;
                    case StatusReconnect: fail++; break;
                    default: fail++; break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                fail++;
                _logger.LogError(ex, "AccSyncService: connection {Id} threw", id);
                _db.ChangeTracker.Clear();
                await TryRecordErrorAsync(id, ex.Message, ct);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        _logger.LogInformation(
            "AccSyncService.SyncAllActiveAsync — {Total} ACC connections: {Ok} ok, {Partial} partial, {Fail} failed, {Busy} busy",
            ids.Count, ok, partial, fail, busy);
    }

    /// <summary>
    /// Token-unification seam. Returns a currently-valid ACC access token for the
    /// project's connection, refreshing (and persisting the rotation) if needed.
    /// </summary>
    public async Task<(string? Token, string? Error)> GetFreshAccessTokenAsync(Guid projectId, CancellationToken ct = default)
    {
        var conn = await FindActiveAsync(projectId, ct);
        if (conn == null) return (null, "No active ACC connection for this project.");
        var t = await AccTokenRefresher.EnsureFreshAsync(_db, _connectorFactory.GetConnector(PlatformType.ACC), conn, _logger, ct);
        return t.Success ? (conn.AccessToken, null) : (null, t.Error);
    }

    /// <summary>
    /// Connectivity test for the generic PlatformController /test endpoint. Goes
    /// through <see cref="AccTokenRefresher"/> (advisory lock + immediate persist
    /// of a rotated refresh token) BEFORE the connector's hubs probe, so the
    /// connector never refreshes on its own. When the token cannot be obtained
    /// the connection's LastSyncStatus becomes RECONNECT_REQUIRED if that is the
    /// reason; a successful test does not touch the sync status.
    /// </summary>
    public async Task<PlatformTestResult> TestConnectionAsync(PlatformConnection conn, CancellationToken ct = default)
    {
        var connector = _connectorFactory.GetConnector(PlatformType.ACC);
        var t = await AccTokenRefresher.EnsureFreshAsync(_db, connector, conn, _logger, ct);
        if (!t.Success)
        {
            if (t.ReconnectRequired)
            {
                Mark(conn, Reconnect($"Couldn't obtain an ACC access token — reconnect ACC. {t.Error}"));
                await _db.SaveChangesAsync(ct);
            }
            return new PlatformTestResult(false, $"Couldn't obtain an ACC access token — {(t.ReconnectRequired ? "reconnect ACC" : "try again")}. {t.Error}");
        }
        return await connector.TestConnectionAsync(conn, ct);
    }

    // ── discovery (hubs → projects → issue subtype) ──

    public sealed record HubInfo(string Id, string Name, string? Region);
    public sealed record ProjectInfo(string Id, string IssuesProjectId, string Name);
    public sealed record SubtypeOption(string TypeId, string TypeTitle, string SubtypeId, string SubtypeTitle, bool Suggested);
    public sealed record IssueTypeResolution(bool Resolved, string? SubtypeId, IReadOnlyList<SubtypeOption> Options, string? Message);

    public async Task<(IReadOnlyList<HubInfo>? Hubs, string? Error)> ListHubsAsync(Guid projectId, CancellationToken ct)
    {
        var (conn, err) = await ConnectionWithTokenAsync(projectId, ct);
        if (conn == null) return (null, err);
        var (items, e) = await GetDmPagedAsync(conn, ApsEndpoints.HubsUrl(_config), ct);
        if (items == null) return (null, e);
        return (items.Select(d => new HubInfo(
            (string?)d["id"] ?? "",
            (string?)d["attributes"]?["name"] ?? "",
            (string?)d["attributes"]?["region"])).ToList(), null);
    }

    public async Task<(IReadOnlyList<ProjectInfo>? Projects, string? Error)> ListHubProjectsAsync(Guid projectId, string hubId, CancellationToken ct)
    {
        var (conn, err) = await ConnectionWithTokenAsync(projectId, ct);
        if (conn == null) return (null, err);
        var (items, e) = await GetDmPagedAsync(conn, $"{ApsEndpoints.HubsUrl(_config)}/{Uri.EscapeDataString(hubId)}/projects", ct);
        if (items == null) return (null, e);
        return (items.Select(d =>
        {
            var id = (string?)d["id"] ?? "";
            return new ProjectInfo(id, ApsEndpoints.StripHubPrefix(id), (string?)d["attributes"]?["name"] ?? "");
        }).ToList(), null);
    }

    /// <summary>
    /// Store the chosen hub / ACC project / region / subtype on the connection. Any
    /// argument left null is left unchanged. User-owned config keys only.
    /// </summary>
    public async Task<string?> SaveSelectionAsync(Guid projectId, string? hubId, string? accProjectId, string? region, string? subtypeId, CancellationToken ct)
    {
        var conn = await FindActiveAsync(projectId, ct);
        if (conn == null) return "No active ACC connection for this project.";
        if (!TryParseConfig(conn, out var cfg, out var cfgErr)) return cfgErr;
        if (accProjectId != null) conn.ExternalProjectId = ApsEndpoints.StripHubPrefix(accProjectId.Trim());
        if (hubId != null) cfg[KeyHubId] = hubId.Trim();
        if (region != null) cfg[KeyRegion] = region.Trim().ToUpperInvariant();
        if (subtypeId != null) cfg[KeySubtypeId] = subtypeId.Trim();
        conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
        await _db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// Find the issue subtype to push clashes under. Picks one automatically ONLY
    /// when exactly one active subtype is an unambiguous match (subtype title
    /// containing "clash"/"coordination", else a matching type with a single
    /// active subtype). Otherwise returns every option for a user to choose —
    /// never silently the first.
    /// </summary>
    public async Task<(IssueTypeResolution? Result, string? Error)> ResolveIssueTypeAsync(Guid projectId, bool store, CancellationToken ct)
    {
        var (conn, err) = await ConnectionWithTokenAsync(projectId, ct);
        if (conn == null) return (null, err);
        if (string.IsNullOrWhiteSpace(conn.ExternalProjectId))
            return (null, "Choose the ACC project first (ExternalProjectId is empty).");
        if (!TryParseConfig(conn, out var cfg, out var cfgErr)) return (null, cfgErr);
        string? region = (string?)cfg[KeyRegion];

        var http = _httpFactory.CreateClient();
        var types = new List<JToken>();
        const int limit = 100;
        for (int offset = 0, page = 0; page < 50; offset += limit, page++)
        {
            string url = $"{ApsEndpoints.IssuesProjectUrl(_config, conn.ExternalProjectId)}/issue-types?include=subtypes&limit={limit}&offset={offset}";
            using var resp = await ApsRetry.SendAsync(http, () => Get(url, conn.AccessToken!, region), true, _logger, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("ACC issue-types HTTP {Status}: {Body}", (int)resp.StatusCode, Truncate(body, 500));
                return (null, $"ACC issue-types query failed (HTTP {(int)resp.StatusCode}).");
            }
            var j = JObject.Parse(body);
            var results = j["results"] as JArray;
            if (results == null) return (null, "ACC issue-types response had no results array.");
            types.AddRange(results);
            int? total = (int?)j["pagination"]?["totalResults"];
            if (results.Count < limit || (total.HasValue && types.Count >= total.Value)) break;
        }

        var options = new List<SubtypeOption>();
        foreach (var t in types)
        {
            if ((bool?)t["isActive"] == false) continue;
            string typeId = (string?)t["id"] ?? "", typeTitle = (string?)t["title"] ?? "";
            if (t["subtypes"] is not JArray subs) continue;
            foreach (var s in subs)
            {
                if ((bool?)s["isActive"] == false) continue;
                string subId = (string?)s["id"] ?? "", subTitle = (string?)s["title"] ?? "";
                if (subId.Length == 0) continue;
                options.Add(new SubtypeOption(typeId, typeTitle, subId, subTitle, false));
            }
        }

        static bool Matches(string s) =>
            s.Contains("clash", StringComparison.OrdinalIgnoreCase)
            || s.Contains("coordination", StringComparison.OrdinalIgnoreCase);

        var bySubtype = options.Where(o => Matches(o.SubtypeTitle)).ToList();
        var candidates = bySubtype.Count > 0
            ? bySubtype
            : options.Where(o => Matches(o.TypeTitle)).ToList();
        var suggested = new HashSet<string>(candidates.Select(c => c.SubtypeId));
        options = options.Select(o => o with { Suggested = suggested.Contains(o.SubtypeId) }).ToList();

        if (candidates.Count == 1)
        {
            var chosen = candidates[0].SubtypeId;
            if (store)
            {
                cfg[KeySubtypeId] = chosen;
                conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
                await _db.SaveChangesAsync(ct);
            }
            return (new IssueTypeResolution(true, chosen, options,
                $"Matched '{candidates[0].TypeTitle} / {candidates[0].SubtypeTitle}'."), null);
        }

        string msg = candidates.Count == 0
            ? "No active issue subtype mentions clash or coordination — choose one from the list."
            : $"{candidates.Count} subtypes match clash/coordination — choose one from the list.";
        return (new IssueTypeResolution(false, null, options, msg), null);
    }

    // ── sync internals ──

    private async Task<AccSyncReport> SyncLockedAsync(PlatformConnection conn, CancellationToken ct)
    {
        await using var guard = await SyncGuard.TryAcquireAsync(_db, conn.Id, ct);
        if (guard == null)
        {
            _logger.LogInformation("AccSyncService: sync for connection {Id} skipped — another sync holds the lock.", conn.Id);
            return new AccSyncReport(false, StatusBusy, Error: "A sync for this ACC connection is already running.");
        }

        AccSyncReport report;
        try
        {
            report = await SyncConnectionAsync(conn, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AccSyncService: sync of connection {Id} threw", conn.Id);
            report = Mark(conn, Fail(ex.Message));
        }
        await _db.SaveChangesAsync(ct);
        return report;
    }

    internal async Task<AccSyncReport> SyncConnectionAsync(PlatformConnection conn, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(conn.ExternalProjectId))
            return Mark(conn, Fail("ExternalProjectId (ACC project id) is empty — choose the ACC project first."));

        if (!TryParseConfig(conn, out var cfg, out var cfgErr))
            return Mark(conn, Fail(cfgErr!));

        string? subtypeId = (string?)cfg[KeySubtypeId];
        if (string.IsNullOrWhiteSpace(subtypeId))
            return Mark(conn, Fail("No ACC issue subtype configured (accIssueSubtypeId) — resolve it via GET acc/issue-types."));
        string? region = (string?)cfg[KeyRegion];

        var tok = await AccTokenRefresher.EnsureFreshAsync(_db, _connectorFactory.GetConnector(PlatformType.ACC), conn, _logger, ct);
        if (!tok.Success)
            return Mark(conn, tok.ReconnectRequired
                ? Reconnect($"Couldn't obtain an ACC access token — reconnect ACC. {tok.Error}")
                : Fail($"Couldn't obtain an ACC access token — {tok.Error}"));

        var map = ReadIssueMap(cfg);

        var open = await _db.Issues
            .Where(i => i.ProjectId == conn.ProjectId
                     && (i.Status == "OPEN" || i.Status == "IN_PROGRESS"))
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);

        int pushed = 0, skipped = 0, failed = 0;
        var failures = new List<string>();
        var http = _httpFactory.CreateClient();

        foreach (var issue in open)
        {
            string key = issue.Id.ToString();
            if (map.ContainsKey(key)) { skipped++; continue; }

            var (success, accId, error) = await PushIssueAsync(http, conn, issue, subtypeId!, region, ct);
            if (success)
            {
                map[key] = accId!;
                pushed++;
                // Persist the mapping NOW: the issue exists in ACC from this moment.
                cfg[KeyIssueMap] = JObject.FromObject(map);
                conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                failed++;
                failures.Add($"{issue.IssueCode}: {error}");
                _logger.LogWarning("AccSyncService: push of issue {Code} failed: {Error}", issue.IssueCode, error);
            }
        }

        var (pulledOpen, pullError) = await PullOpenCountAsync(http, conn, region, ct);

        int? closedInAcc = null, statusRead = null;
        string? readBackError = null;
        if (map.Count > 0)
        {
            var (statuses, rbErr) = await ReadBackStatusesAsync(http, conn, map.Values.Distinct().ToList(), region, ct);
            if (statuses != null)
            {
                cfg[KeyIssueStatus] = JObject.FromObject(statuses);
                cfg[KeyIssueStatusAt] = DateTime.UtcNow.ToString("o");
                conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
                statusRead = statuses.Count;
                closedInAcc = statuses.Values.Count(s => string.Equals(s, "closed", StringComparison.OrdinalIgnoreCase));
            }
            else readBackError = rbErr;
        }
        else { closedInAcc = 0; statusRead = 0; }

        string status;
        var errors = new List<string>();
        if (pullError != null) errors.Add($"Open-count pull failed: {pullError}");
        if (readBackError != null) errors.Add($"Status read-back failed: {readBackError}");
        if (failed > 0) errors.Add($"{failed} of {failed + pushed} push(es) failed; first: {failures[0]}");

        if (pullError != null || (failed > 0 && pushed == 0)) status = StatusFailed;
        else if (failed > 0 || readBackError != null) status = StatusPartial;
        else status = StatusOk;

        var report = new AccSyncReport(
            status == StatusOk, status, pushed, skipped, pulledOpen, failed, closedInAcc, statusRead,
            errors.Count == 0 ? null : string.Join(" | ", errors), failures);
        return Mark(conn, report);
    }

    private async Task<(bool ok, string? accId, string? error)> PushIssueAsync(
        HttpClient http, PlatformConnection conn, BimIssue issue, string subtypeId, string? region, CancellationToken ct)
    {
        var body = new JObject
        {
            ["title"] = Truncate(issue.Title, TitleMax),
            ["description"] = Truncate(issue.Description, DescriptionMax),
            ["issueSubtypeId"] = subtypeId,
            ["status"] = MapStatus(issue.Status),
            // ACC creates issues UNPUBLISHED by default — visible only to the creator
            // and assignee. A team-shared coordination issue must be published.
            ["published"] = true,
        };
        string url = $"{ApsEndpoints.IssuesProjectUrl(_config, conn.ExternalProjectId)}/issues";
        string json = body.ToString();

        try
        {
            // POST create is not idempotent: ApsRetry retries only 429 / 503+Retry-After.
            using var resp = await ApsRetry.SendAsync(http, () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", conn.AccessToken);
                if (!string.IsNullOrWhiteSpace(region)) req.Headers.TryAddWithoutValidation("x-ads-region", region);
                return req;
            }, idempotent: false, _logger, ct);
            string respBody = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                // The APS body goes to the log only: the report reaches the browser
                // (/acc/sync response, LastSyncError) and APS bodies can carry ids.
                _logger.LogWarning("ACC issue create for {Code} HTTP {Status}: {Body}", issue.IssueCode, (int)resp.StatusCode, Truncate(respBody, 1000));
                return (false, null, $"ACC rejected the issue (HTTP {(int)resp.StatusCode}).");
            }

            var j = JObject.Parse(respBody);
            string accId = (string?)j["id"] ?? (string?)j["data"]?["id"] ?? "";
            return string.IsNullOrEmpty(accId)
                ? (false, null, "ACC response had no issue id.")
                : (true, accId, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>Total open issues in the ACC project. A failed read is an error, never 0.</summary>
    internal async Task<(int? count, string? error)> PullOpenCountAsync(HttpClient http, PlatformConnection conn, string? region, CancellationToken ct)
    {
        try
        {
            string url = $"{ApsEndpoints.IssuesProjectUrl(_config, conn.ExternalProjectId)}/issues?limit=1&filter[status]=open";
            using var resp = await ApsRetry.SendAsync(http, () => Get(url, conn.AccessToken!, region), true, _logger, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return (null, $"HTTP {(int)resp.StatusCode}");
            var total = (int?)JObject.Parse(body)["pagination"]?["totalResults"];
            return total.HasValue ? (total, null) : (null, "response had no pagination.totalResults");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AccSyncService: open-count pull failed for connection {Id}", conn.Id);
            return (null, ex.Message);
        }
    }

    /// <summary>ACC status for each mapped ACC issue id (filter[id], batched, paged).</summary>
    private async Task<(Dictionary<string, string>? statuses, string? error)> ReadBackStatusesAsync(
        HttpClient http, PlatformConnection conn, List<string> accIds, string? region, CancellationToken ct)
    {
        var result = new Dictionary<string, string>();
        try
        {
            foreach (var batch in accIds.Chunk(ReadBackBatch))
            {
                string ids = string.Join(",", batch.Select(Uri.EscapeDataString));
                for (int offset = 0, page = 0; page < 20; offset += 100, page++)
                {
                    string url = $"{ApsEndpoints.IssuesProjectUrl(_config, conn.ExternalProjectId)}/issues?filter[id]={ids}&limit=100&offset={offset}";
                    using var resp = await ApsRetry.SendAsync(http, () => Get(url, conn.AccessToken!, region), true, _logger, ct);
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    if (!resp.IsSuccessStatusCode) return (null, $"HTTP {(int)resp.StatusCode}");
                    var j = JObject.Parse(body);
                    if (j["results"] is not JArray results) return (null, "response had no results array");
                    foreach (var r in results)
                    {
                        var id = (string?)r["id"];
                        if (!string.IsNullOrEmpty(id)) result[id] = (string?)r["status"] ?? "";
                    }
                    int? total = (int?)j["pagination"]?["totalResults"];
                    if (results.Count < 100 || (total.HasValue && offset + results.Count >= total.Value)) break;
                }
            }
            // Mapped ids ACC no longer returns (deleted, or no longer visible to this grant).
            foreach (var id in accIds)
                if (!result.ContainsKey(id)) result[id] = "not_found";
            return (result, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, ex.Message);
        }
    }

    private async Task<(IReadOnlyList<JToken>? items, string? error)> GetDmPagedAsync(PlatformConnection conn, string firstUrl, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        var items = new List<JToken>();
        string? url = firstUrl + (firstUrl.Contains('?') ? "&" : "?") + "page[limit]=200";
        for (int page = 0; url != null && page < 100; page++)
        {
            string current = url;
            using var resp = await ApsRetry.SendAsync(http, () => Get(current, conn.AccessToken!, null), true, _logger, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("APS Data Management HTTP {Status}: {Body}", (int)resp.StatusCode, Truncate(body, 500));
                return (null, $"APS Data Management query failed (HTTP {(int)resp.StatusCode}).");
            }
            var j = JObject.Parse(body);
            if (j["data"] is JArray data) items.AddRange(data);
            else return (null, "APS Data Management response had no data array.");
            var next = j["links"]?["next"];
            url = next?.Type == JTokenType.Object ? (string?)next["href"] : next?.Type == JTokenType.String ? (string?)next : null;
        }
        return (items, null);
    }

    private async Task<(PlatformConnection? conn, string? error)> ConnectionWithTokenAsync(Guid projectId, CancellationToken ct)
    {
        var conn = await FindActiveAsync(projectId, ct);
        if (conn == null) return (null, "No active ACC connection for this project.");
        var t = await AccTokenRefresher.EnsureFreshAsync(_db, _connectorFactory.GetConnector(PlatformType.ACC), conn, _logger, ct);
        return t.Success ? (conn, null) : (null, $"Couldn't obtain an ACC access token — (re)connect ACC. {t.Error}");
    }

    private Task<PlatformConnection?> FindActiveAsync(Guid projectId, CancellationToken ct)
        => _db.PlatformConnections.FirstOrDefaultAsync(c => c.ProjectId == projectId
                                                         && c.Platform == PlatformType.ACC
                                                         && c.IsActive, ct);

    private static HttpRequestMessage Get(string url, string token, string? region)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrWhiteSpace(region)) req.Headers.TryAddWithoutValidation("x-ads-region", region);
        return req;
    }

    private async Task TryRecordErrorAsync(Guid connectionId, string error, CancellationToken ct)
    {
        try
        {
            var c = await _db.PlatformConnections.FirstOrDefaultAsync(x => x.Id == connectionId, ct);
            if (c == null) return;
            c.LastSyncStatus = StatusFailed;
            c.LastSyncError = Truncate(error, 1000);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AccSyncService: could not record the error for connection {Id}", connectionId);
        }
    }

    private AccSyncReport Mark(PlatformConnection conn, AccSyncReport report)
    {
        conn.LastSyncAt = DateTime.UtcNow;
        conn.LastSyncStatus = report.Status;
        conn.LastSyncError = report.Error == null ? null : Truncate(report.Error, 1000);
        if (report.Status != StatusOk)
            _logger.LogWarning("AccSyncService: connection {Id} → {Status}: {Error}", conn.Id, report.Status, report.Error);
        return report;
    }

    // ── ConfigJson helpers ──

    /// <summary>Parse ConfigJson. Empty → {}. Anything else that is not a JSON object is an ERROR.</summary>
    public static bool TryParseConfig(PlatformConnection conn, out JObject cfg, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(conn.ConfigJson)) { cfg = new JObject(); return true; }
        try
        {
            if (JToken.Parse(conn.ConfigJson) is JObject o) { cfg = o; return true; }
            error = "ConfigJson is not a JSON object — refusing to sync (an unreadable issue map would re-push every issue).";
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            error = $"ConfigJson is not valid JSON ({ex.Message}) — refusing to sync (an unreadable issue map would re-push every issue).";
        }
        cfg = new JObject();
        return false;
    }

    private static Dictionary<string, string> ReadIssueMap(JObject cfg)
    {
        var map = new Dictionary<string, string>();
        if (cfg[KeyIssueMap] is JObject jm)
            foreach (var kv in jm)
                if (kv.Value != null && kv.Value.Type == JTokenType.String)
                    map[kv.Key] = kv.Value.Value<string>()!;
        return map;
    }

    /// <summary>
    /// Merge a client-supplied ConfigJson over the stored one, keeping every
    /// <see cref="ServerOwnedConfigKeys"/> value from the stored copy. Returns null
    /// + error when the incoming value is not a JSON object.
    /// </summary>
    public static (string? merged, string? error) MergeClientConfig(string? stored, string incoming)
    {
        JObject inc;
        try
        {
            if (JToken.Parse(incoming) is not JObject o) return (null, "ConfigJson must be a JSON object.");
            inc = o;
        }
        catch (Newtonsoft.Json.JsonException ex) { return (null, $"ConfigJson is not valid JSON: {ex.Message}"); }

        foreach (var k in ServerOwnedConfigKeys) inc.Remove(k);

        if (!string.IsNullOrWhiteSpace(stored))
        {
            JObject? existing;
            try { existing = JToken.Parse(stored) as JObject; }
            catch (Newtonsoft.Json.JsonException) { existing = null; }
            // A stored value we cannot read may still hold the issue map as far as
            // anyone can tell. Overwriting it would silently drop the map and the
            // next sync would re-push every issue — refuse instead.
            if (existing == null)
                return (null, "The stored ConfigJson is unreadable; it may hold the ACC issue map. Repair it server-side before replacing it.");
            foreach (var k in ServerOwnedConfigKeys)
                if (existing[k] != null) inc[k] = existing[k]!.DeepClone();
        }
        return (inc.ToString(Newtonsoft.Json.Formatting.None), null);
    }

    private static string MapStatus(string s) => s switch
    {
        "RESOLVED" => "completed",
        "CLOSED"   => "closed",
        _ => "open",
    };

    private static string Truncate(string? s, int n)
        => string.IsNullOrEmpty(s) ? "" : (s!.Length > n ? s.Substring(0, n) : s);

    /// <summary>One-sync-per-connection guard: Postgres session advisory lock + in-process semaphore.</summary>
    private sealed class SyncGuard : IAsyncDisposable
    {
        private readonly PlanscapeDbContext _db;
        private readonly SemaphoreSlim _gate;
        private readonly long? _pgKey;

        private SyncGuard(PlanscapeDbContext db, SemaphoreSlim gate, long? pgKey) { _db = db; _gate = gate; _pgKey = pgKey; }

        public static async Task<SyncGuard?> TryAcquireAsync(PlanscapeDbContext db, Guid connectionId, CancellationToken ct)
        {
            var gate = _syncLocks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0, ct)) return null;

            if (!db.Database.IsNpgsql()) return new SyncGuard(db, gate, null);

            long key = AccTokenRefresher.LockKey("acc-sync", connectionId);
            try
            {
                await db.Database.OpenConnectionAsync(ct);   // session lock needs one connection for its lifetime
                bool got = await db.Database
                    .SqlQuery<bool>($"SELECT pg_try_advisory_lock({key}) AS \"Value\"")
                    .SingleAsync(ct);
                if (got) return new SyncGuard(db, gate, key);
                await db.Database.CloseConnectionAsync();
                gate.Release();
                return null;
            }
            catch
            {
                try { await db.Database.CloseConnectionAsync(); } catch { /* best-effort; original exception rethrown */ }
                gate.Release();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_pgKey.HasValue)
                {
                    await _db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({_pgKey.Value}) AS \"Value\"").SingleAsync();
                    await _db.Database.CloseConnectionAsync();
                }
            }
            finally { _gate.Release(); }
        }
    }
}
