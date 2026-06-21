using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Server-side ACC issue sync — the "push STING issues → ACC issues" half of the
/// team-shared Autodesk Construction Cloud integration (#3 scaffold).
///
/// The existing <see cref="PlatformSyncJob"/> + <see cref="AccConnector.SyncAsync"/>
/// path is element-centric (TaggedElements) and pull-only. This service is the
/// issue-centric counterpart: it walks open <see cref="BimIssue"/>s, POSTs the
/// ones we haven't pushed yet to the ACC Issues API, and remembers the mapping
/// (Planscape issue id → ACC issue id) in <see cref="PlatformConnection.ConfigJson"/>
/// under the <c>accIssueMap</c> key so re-runs are idempotent (no EF migration —
/// the dedup map rides the existing ConfigJson column).
///
/// Token handling delegates to <see cref="AccConnector.RefreshTokenAsync"/> (which
/// rotates the access/refresh pair onto the connection entity); this service then
/// SaveChanges so the rotation persists — the same token-rotation discipline that
/// the PlatformController /test fix established.
///
/// CAVEAT: SCAFFOLD. Built to the documented APS Issues v1 signatures but NOT
/// exercised against a live ACC project or deployed server. The ACC create-issue
/// payload requires a real <c>issueSubtypeId</c> from the container's issue-type
/// catalogue; until the dashboard lets an admin pick one (stored in ConfigJson
/// key <c>accIssueSubtypeId</c>), the push is attempted best-effort and a missing
/// subtype is reported per-issue rather than aborting the batch.
/// </summary>
public class AccSyncService
{
    private const string IssuesBase = "https://developer.api.autodesk.com/construction/issues/v1";

    private readonly PlanscapeDbContext _db;
    private readonly IPlatformConnectorFactory _connectorFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AccSyncService> _logger;

    public AccSyncService(
        PlanscapeDbContext db,
        IPlatformConnectorFactory connectorFactory,
        IHttpClientFactory httpFactory,
        ILogger<AccSyncService> logger)
    {
        _db = db;
        _connectorFactory = connectorFactory;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public sealed record AccSyncReport(
        bool Success,
        int Pushed = 0,
        int Skipped = 0,
        int PulledOpen = 0,
        int Failed = 0,
        string? Error = null);

    /// <summary>
    /// Sync the single active ACC connection for one project. Tenant-scoped: relies
    /// on the caller's tenant filter (controller path) so it only ever touches the
    /// caller's own connection. For the cross-tenant scheduled sweep use
    /// <see cref="SyncAllActiveAsync"/>.
    /// </summary>
    public async Task<AccSyncReport> SyncProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var conn = await _db.PlatformConnections
            .FirstOrDefaultAsync(c => c.ProjectId == projectId
                                   && c.Platform == PlatformType.ACC
                                   && c.IsActive, ct);
        if (conn == null)
            return new AccSyncReport(false, Error: "No active ACC connection for this project.");

        var report = await SyncConnectionAsync(conn, ct);
        await _db.SaveChangesAsync(ct);
        return report;
    }

    /// <summary>
    /// Scheduled sweep: every active ACC connection across all tenants. Bypasses the
    /// tenant filter (Hangfire has no tenant context — same pattern as PlatformSyncJob).
    /// </summary>
    public async Task SyncAllActiveAsync(CancellationToken ct = default)
    {
        _db.BypassTenantFilter = true;
        var connections = await _db.PlatformConnections
            .Where(c => c.IsActive && c.Platform == PlatformType.ACC)
            .ToListAsync(ct);

        int ok = 0, fail = 0;
        foreach (var conn in connections)
        {
            try
            {
                var r = await SyncConnectionAsync(conn, ct);
                if (r.Success) ok++; else fail++;
            }
            catch (Exception ex)
            {
                fail++;
                conn.LastSyncStatus = "ERROR";
                conn.LastSyncError = ex.Message;
                _logger.LogError(ex, "AccSyncService: connection {Id} threw", conn.Id);
            }
        }

        if (connections.Count > 0)
            await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AccSyncService.SyncAllActiveAsync — {Total} ACC connections, {Ok} ok, {Fail} failed",
            connections.Count, ok, fail);
    }

    /// <summary>
    /// Token-unification seam. Returns a currently-valid ACC access token for the
    /// project's connection, refreshing (and persisting the rotation) if needed.
    /// The plugin calls this so the team shares ONE ACC grant rather than each
    /// engineer running their own 3-legged flow. Null when no connection / refresh fails.
    /// </summary>
    public async Task<string?> GetFreshAccessTokenAsync(Guid projectId, CancellationToken ct = default)
    {
        var conn = await _db.PlatformConnections
            .FirstOrDefaultAsync(c => c.ProjectId == projectId
                                   && c.Platform == PlatformType.ACC
                                   && c.IsActive, ct);
        if (conn == null) return null;

        if (!await EnsureTokenAsync(conn, ct)) return null;
        await _db.SaveChangesAsync(ct);  // persist any rotation
        return conn.AccessToken;
    }

    // ── internals ──

    private async Task<AccSyncReport> SyncConnectionAsync(PlatformConnection conn, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(conn.ExternalProjectId))
            return Mark(conn, new AccSyncReport(false, Error: "ExternalProjectId (ACC Issues container) is empty."));

        if (!await EnsureTokenAsync(conn, ct))
            return Mark(conn, new AccSyncReport(false, Error: "Couldn't obtain an ACC access token — (re)connect ACC first."));

        var map = ReadIssueMap(conn);
        string? subtypeId = ReadConfig(conn, "accIssueSubtypeId");

        // Push open Planscape issues we haven't pushed yet.
        var open = await _db.Issues
            .Where(i => i.ProjectId == conn.ProjectId
                     && (i.Status == "OPEN" || i.Status == "IN_PROGRESS"))
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);

        int pushed = 0, skipped = 0, failed = 0;
        var http = _httpFactory.CreateClient();

        foreach (var issue in open)
        {
            string key = issue.Id.ToString();
            if (map.ContainsKey(key)) { skipped++; continue; }   // already in ACC

            var (success, accId, error) = await PushIssueAsync(http, conn, issue, subtypeId, ct);
            if (success && !string.IsNullOrEmpty(accId))
            {
                map[key] = accId!;
                pushed++;
            }
            else
            {
                failed++;
                _logger.LogWarning("AccSyncService: push of issue {Code} failed: {Error}", issue.IssueCode, error);
            }
        }

        if (pushed > 0) WriteIssueMap(conn, map);

        // Pull side — report the ACC open-issue count (lightweight visibility metric).
        int pulledOpen = await PullOpenCountAsync(http, conn, ct);

        var report = new AccSyncReport(true, pushed, skipped, pulledOpen, failed);
        return Mark(conn, report);
    }

    private async Task<(bool ok, string? accId, string? error)> PushIssueAsync(
        HttpClient http, PlatformConnection conn, BimIssue issue, string? subtypeId, CancellationToken ct)
    {
        // APS Issues v1 create. NOTE: a real container requires issueSubtypeId from
        // the container's type catalogue; we send it when configured and let ACC
        // reject (with a clear message) when it's absent so the gap is visible.
        var body = new JObject
        {
            ["title"] = Truncate(issue.Title, 250),
            ["description"] = issue.Description ?? "",
            ["status"] = MapStatus(issue.Status),
        };
        if (!string.IsNullOrWhiteSpace(subtypeId))
            body["issueSubtypeId"] = subtypeId;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{IssuesBase}/containers/{conn.ExternalProjectId}/issues")
            {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", conn.AccessToken);

            var resp = await http.SendAsync(req, ct);
            string respBody = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (false, null, $"HTTP {(int)resp.StatusCode}: {Truncate(respBody, 200)}");

            var j = JObject.Parse(respBody);
            string accId = (string?)j["id"] ?? (string?)j["data"]?["id"] ?? "";
            return string.IsNullOrEmpty(accId)
                ? (false, null, "ACC response had no issue id.")
                : (true, accId, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private async Task<int> PullOpenCountAsync(HttpClient http, PlatformConnection conn, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{IssuesBase}/containers/{conn.ExternalProjectId}/issues?limit=1&filter[status]=open");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", conn.AccessToken);
            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return 0;
            var j = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
            return (int?)j["pagination"]?["totalResults"] ?? ((j["results"] as JArray)?.Count ?? 0);
        }
        catch { return 0; }
    }

    /// <summary>Refresh the access token when missing or within 5 min of expiry, via the connector.</summary>
    private async Task<bool> EnsureTokenAsync(PlatformConnection conn, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(conn.AccessToken)
            && conn.TokenExpiresAt.HasValue
            && conn.TokenExpiresAt.Value > DateTime.UtcNow.AddMinutes(5))
            return true;

        var connector = _connectorFactory.GetConnector(PlatformType.ACC);
        var result = await connector.RefreshTokenAsync(conn, ct);  // rotates onto the entity
        return result.Success;
    }

    private AccSyncReport Mark(PlatformConnection conn, AccSyncReport report)
    {
        conn.LastSyncAt = DateTime.UtcNow;
        conn.LastSyncStatus = report.Success ? "OK" : "FAILED";
        conn.LastSyncError = report.Error;
        return report;
    }

    // ── ConfigJson helpers (dedup map + config keys) ──

    private static Dictionary<string, string> ReadIssueMap(PlatformConnection conn)
    {
        var cfg = ParseConfig(conn);
        var map = new Dictionary<string, string>();
        if (cfg["accIssueMap"] is JObject jm)
            foreach (var kv in jm)
                if (kv.Value != null) map[kv.Key] = kv.Value.Value<string>() ?? "";
        return map;
    }

    private static void WriteIssueMap(PlatformConnection conn, Dictionary<string, string> map)
    {
        var cfg = ParseConfig(conn);
        cfg["accIssueMap"] = JObject.FromObject(map);
        conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string? ReadConfig(PlatformConnection conn, string key)
        => (string?)ParseConfig(conn)[key];

    private static JObject ParseConfig(PlatformConnection conn)
    {
        if (string.IsNullOrWhiteSpace(conn.ConfigJson)) return new JObject();
        try { return JObject.Parse(conn.ConfigJson); }
        catch { return new JObject(); }
    }

    private static string MapStatus(string s) => s switch
    {
        "RESOLVED" => "answered",
        "CLOSED"   => "closed",
        "IN_PROGRESS" => "open",
        _ => "open",
    };

    private static string Truncate(string? s, int n)
        => string.IsNullOrEmpty(s) ? "" : (s!.Length > n ? s.Substring(0, n) : s);
}
