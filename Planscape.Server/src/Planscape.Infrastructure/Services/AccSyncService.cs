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
    /// <summary>Per mapped issue: the Planscape UpdatedAt last pushed to ACC (C10).</summary>
    public const string KeyIssuePushedAt = "accIssuePushedAt";
    /// <summary>E2: per mapped issue, what was last pushed (status, title hash, description
    /// hash), so an update sends only what changed in Planscape since.</summary>
    public const string KeyIssuePushedState = "accIssuePushedState";
    /// <summary>D2: issues whose create may have landed in ACC although no answer came back
    /// (timeout, transport error, 5xx), with when the attempt was made. Never re-posted blind.</summary>
    public const string KeyIssuePendingVerify = "accIssuePendingVerify";
    public const string KeySubtypeId     = "accIssueSubtypeId";
    public const string KeyHubId         = "accHubId";
    public const string KeyRegion        = "accRegion";
    public const string KeyWebhookHooks  = "accWebhookHooks";
    /// <summary>ACC-SRV-11: when this connection first synced. Issues closed in Planscape and
    /// never pushed are considered only if raised after it; older ones are history.</summary>
    public const string KeyIssueSyncSince = "accIssueSyncSince";
    /// <summary>ACC-SRV-11: closed-never-pushed issues already reported, so each is reported once.</summary>
    public const string KeyIssueClosedReported = "accIssueClosedReported";
    /// <summary>ACC-SRV-11 policy (client-set): "report" (default) or "create".</summary>
    public const string KeyClosedBetweenSweeps = "accClosedBetweenSweeps";

    /// <summary>ConfigJson keys only the server writes. A client PUT must not replace them.</summary>
    public static readonly IReadOnlyList<string> ServerOwnedConfigKeys = new[] { KeyIssueMap, KeyIssueStatus, KeyIssueStatusAt, KeyIssuePushedAt, KeyIssuePushedState, KeyIssuePendingVerify, KeyWebhookHooks, KeyIssueSyncSince, KeyIssueClosedReported, AccWebhookService.KeySecretSetBy };

    // Documented Issues v1 POST limits.
    private const int TitleMax = 100;
    private const int DescriptionMax = 1000;
    private const int ReadBackBatch = 50;
    internal const int ReadBackMaxPages = 20;

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
        IReadOnlyList<string>? Failures = null,
        int Updated = 0,
        int Diverged = 0,
        int ClosedNotSent = 0);

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
    // D2: the cron runs every 30 minutes; Hangfire's default 10 retries re-ran the whole
    // sweep on top of it (and re-posted unclear creates each time).
    [Hangfire.AutomaticRetry(Attempts = 0)]
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
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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
            // H-1: an SSA failure is a server setting or the SSA's ACC access, not a sign-in to redo.
            string advice = t.ReconnectRequired ? "reconnect ACC"
                : Aps.ApsSsa.IsSsa(conn) ? "this connection uses a Secure Service Account (accAuthMode = ssa); fix the server setting or ACC access named here"
                : "try again";
            return new PlatformTestResult(false, $"Couldn't obtain an ACC access token — {advice}. {t.Error}");
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
    /// <summary>x-ads-region values (APS acc-regions page).</summary>
    internal static readonly IReadOnlyList<string> KnownRegions = new[] { "US", "CAN", "EMEA", "GBR", "DEU", "IND", "JPN", "AUS" };

    public async Task<string?> SaveSelectionAsync(Guid projectId, string? hubId, string? accProjectId, string? region, string? subtypeId, CancellationToken ct)
    {
        var conn = await FindActiveAsync(projectId, ct);
        if (conn == null) return "No active ACC connection for this project.";
        if (!TryParseConfig(conn, out var cfg, out var cfgErr)) return cfgErr;

        // H-7: a hub chosen without a region used to store none, so every server Issues and
        // webhooks call for a non-US hub went without x-ads-region. Take it from the hub's own
        // Data Management record (attributes.region) - before saving anything, so a lookup that
        // fails refuses the whole selection rather than leaving it half-applied.
        if (hubId != null && region == null && string.IsNullOrWhiteSpace((string?)cfg[KeyRegion]))
        {
            var (hubs, hubErr) = await ListHubsAsync(projectId, ct);
            if (hubs == null)
                return $"The hub's region could not be read ({hubErr}). Nothing was saved; send \"region\" with the selection.";
            string want = ApsEndpoints.StripHubPrefix(hubId.Trim());
            var hub = hubs.FirstOrDefault(h => string.Equals(ApsEndpoints.StripHubPrefix(h.Id), want, StringComparison.OrdinalIgnoreCase));
            if (hub == null)
                return $"Hub {hubId} is not among the hubs this ACC grant can see. Nothing was saved.";
            string r = (hub.Region ?? "").Trim().ToUpperInvariant();
            if (KnownRegions.Contains(r)) region = r;
            else _logger.LogWarning("ACC hub {Hub} reports region '{Region}', not a known x-ads-region value; none stored.", hubId, hub.Region);
        }
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
        bool complete = false;
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
            if (results.Count < limit || (total.HasValue && types.Count >= total.Value)) { complete = true; break; }
        }
        // D8: fifty full pages and never a last one — the types list is not complete.
        if (!complete) return (null, $"ACC issue types: stopped after 50 pages ({types.Count}) with more to read - the list is INCOMPLETE.");

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
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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
        var pushedAt = ReadPushedAt(cfg);
        var pendingVerify = ReadPendingVerify(cfg);
        int unverified = 0;

        var sweepStartedUtc = DateTime.UtcNow;
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

            // An issue that came FROM ACC (or is linked to one) is never created in ACC again:
            // pushing it would make a second ACC issue, which the plugin's import brings back as
            // a third, growing by one per cycle with every step reporting success. When its ACC
            // id is known it is LINKED (so status read-back covers it); when not, it is skipped.
            string originAccId = AccOriginId(issue);
            if (originAccId != null)
            {
                map[key] = originAccId;
                skipped++;
                cfg[KeyIssueMap] = JObject.FromObject(map);
                conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
                await _db.SaveChangesAsync(ct);
                continue;
            }
            if (string.Equals(issue.Source, "acc", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }

            // D2: an earlier create of this issue got no clear answer. Look for it in ACC first:
            // found → link it; proven absent → post; can't tell → leave it, never post blind
            // (a blind re-post put a duplicate, assigned to real people, into ACC every run).
            if (pendingVerify.TryGetValue(key, out var since))
            {
                var (found, verifyErr) = await FindCreatedSinceAsync(http, conn, issue, since, region, ct);
                if (verifyErr != null)
                {
                    unverified++;
                    failures.Add($"{issue.IssueCode}: an earlier create got no answer and ACC could not be checked ({verifyErr}) — not sent again");
                    failed++;
                    continue;
                }
                pendingVerify.Remove(key);
                cfg[KeyIssuePendingVerify] = PendingJson(pendingVerify);
                if (found != null)
                {
                    map[key] = found;
                    pushedAt[key] = issue.UpdatedAt;
                    skipped++;
                    cfg[KeyIssueMap] = JObject.FromObject(map);
                    cfg[KeyIssuePushedAt] = PushedAtJson(pushedAt);
                    conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation("AccSyncService: issue {Code} was created in ACC by an earlier unclear push — linked to {Id}", issue.IssueCode, found);
                    continue;
                }
                conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
            }

            // D5: a first sync can push hundreds of issues; keep the token fresh across the run.
            var fresh = await RefreshMidRunAsync(conn, ct);
            if (fresh != null) { failed++; failures.Add($"{issue.IssueCode}: {fresh}"); break; }
            var (success, accId, error, unclear) = await PushIssueAsync(http, conn, issue, subtypeId!, region, ct);
            if (success)
            {
                map[key] = accId!;
                pushedAt[key] = issue.UpdatedAt;
                var states = cfg[KeyIssuePushedState] as JObject ?? new JObject();
                states[key] = AccIssueUpdatePlan.Snapshot.Of(issue.Title, issue.Description, issue.Status).ToJson();
                cfg[KeyIssuePushedState] = states;
                pushed++;
                // Persist the mapping NOW: the issue exists in ACC from this moment.
                cfg[KeyIssueMap] = JObject.FromObject(map);
                cfg[KeyIssuePushedAt] = PushedAtJson(pushedAt);
                conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                failed++;
                failures.Add($"{issue.IssueCode}: {error}" + (unclear ? " — outcome unknown; it is checked in ACC before any re-send" : ""));
                _logger.LogWarning("AccSyncService: push of issue {Code} failed: {Error}", issue.IssueCode, error);
                if (unclear)
                {
                    pendingVerify[key] = DateTime.UtcNow.AddMinutes(-2);   // margin for clock skew
                    cfg[KeyIssuePendingVerify] = PendingJson(pendingVerify);
                    conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
                    await _db.SaveChangesAsync(ct);
                }
            }
        }

        // ACC-SRV-11: an issue raised AND closed in Planscape between two sweeps was never in
        // the open set above, so it never reached ACC and nothing said so.
        var (closedPushed, closedNotSent, closedFailures, closedNote) =
            await HandleClosedBetweenSweepsAsync(http, conn, cfg, map, pushedAt, pendingVerify, subtypeId!, region, sweepStartedUtc, ct);
        pushed += closedPushed;
        failed += closedFailures.Count;
        failures.AddRange(closedFailures);

        // C10: a mapped issue edited in Planscape since its last push (title, description,
        // status) is PATCHed to ACC. Before this a mapped issue was never touched again, so an
        // issue closed or retitled in Planscape stayed open with the old text in ACC while the
        // sync reported OK. ACC-owned issues are never PATCHed (they flow ACC → Planscape), and
        // an issue ACC last reported closed is never reopened from here (the status is withheld
        // and the divergence reported).
        var (updated, diverged, updFailures) = await PushUpdatesAsync(http, conn, cfg, map, pushedAt, region, ct);
        failed += updFailures.Count;
        failures.AddRange(updFailures);

        var freshForReads = await RefreshMidRunAsync(conn, ct);
        if (freshForReads != null) failures.Add("read-back: " + freshForReads);
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
        if (failed > 0) errors.Add($"{failed} of {failed + pushed + updated} push(es) failed; first: {failures[0]}");
        if (diverged > 0) errors.Add($"{diverged} issue(s) changed status in Planscape but ACC changed it too since the last push, " +
                                     "so Planscape's status was not sent (re-checked every sync)");
        if (closedNote != null) errors.Add(closedNote);

        if (pullError != null || (failed > 0 && pushed == 0 && updated == 0)) status = StatusFailed;
        else if (failed > 0 || readBackError != null) status = StatusPartial;
        else status = StatusOk;

        var report = new AccSyncReport(
            status == StatusOk, status, pushed, skipped, pulledOpen, failed, closedInAcc, statusRead,
            errors.Count == 0 ? null : string.Join(" | ", errors), failures, updated, diverged, closedNotSent);
        return Mark(conn, report);
    }

    /// <summary>
    /// ACC-SRV-11: Planscape-born issues that are closed but were never mapped to ACC, and were
    /// raised after this connection's first sync (so they fell between two sweeps). Policy
    /// <see cref="KeyClosedBetweenSweeps"/>: "report" (default) names each once and sends
    /// nothing; "create" creates it in ACC with its closed status and maps it. The first sync
    /// only records the baseline, so turning ACC on never back-fills a project's history.
    /// </summary>
    private async Task<(int pushed, int notSent, List<string> failures, string? note)> HandleClosedBetweenSweepsAsync(
        HttpClient http, PlatformConnection conn, JObject cfg, Dictionary<string, string> map,
        Dictionary<string, DateTime> pushedAt, Dictionary<string, DateTime> pendingVerify,
        string subtypeId, string? region, DateTime sweepStartedUtc, CancellationToken ct)
    {
        var failures = new List<string>();
        var sinceTok = cfg[KeyIssueSyncSince];
        DateTime? since = sinceTok?.Type == JTokenType.Date ? sinceTok.Value<DateTime>()
            : DateTime.TryParse((string?)sinceTok, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
        if (since == null)
        {
            cfg[KeyIssueSyncSince] = sweepStartedUtc.ToString("o");
            conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
            await _db.SaveChangesAsync(ct);
            return (0, 0, failures, null);
        }

        var mode = AccClosedSweepPolicy.Parse((string?)cfg[KeyClosedBetweenSweeps], out string? policyError);
        var closed = await _db.Issues
            .Where(i => i.ProjectId == conn.ProjectId
                     && (i.Status == "RESOLVED" || i.Status == "CLOSED")
                     && i.CreatedAt >= since.Value)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);
        var reported = new HashSet<string>(
            (cfg[KeyIssueClosedReported] as JArray)?.Select(t => (string?)t).Where(t => !string.IsNullOrEmpty(t))!
                ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        int pushed = 0, notSent = 0;
        var notSentCodes = new List<string>();
        bool dirty = false;
        foreach (var issue in closed)
        {
            string key = issue.Id.ToString();
            if (map.ContainsKey(key) || AccOriginId(issue) != null
                || string.Equals(issue.Source, "acc", StringComparison.OrdinalIgnoreCase)) continue;
            if (pendingVerify.ContainsKey(key))
            {
                // An earlier create of it got no answer; it may already be in ACC. Never post blind.
                if (reported.Add(key)) { notSent++; notSentCodes.Add(issue.IssueCode + " (earlier create unconfirmed)"); dirty = true; }
                continue;
            }
            if (mode == AccClosedSweepPolicy.Mode.Report)
            {
                if (reported.Add(key)) { notSent++; notSentCodes.Add(issue.IssueCode); dirty = true; }
                continue;
            }

            var fresh = await RefreshMidRunAsync(conn, ct);
            if (fresh != null) { failures.Add($"{issue.IssueCode}: {fresh}"); break; }
            var (success, accId, error, unclear) = await PushIssueAsync(http, conn, issue, subtypeId, region, ct);
            if (success)
            {
                map[key] = accId!;
                pushedAt[key] = issue.UpdatedAt;
                var states = cfg[KeyIssuePushedState] as JObject ?? new JObject();
                states[key] = AccIssueUpdatePlan.Snapshot.Of(issue.Title, issue.Description, issue.Status).ToJson();
                cfg[KeyIssuePushedState] = states;
                cfg[KeyIssueMap] = JObject.FromObject(map);
                cfg[KeyIssuePushedAt] = PushedAtJson(pushedAt);
                reported.Remove(key);
                pushed++;
                dirty = true;
                conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                failures.Add($"{issue.IssueCode} (raised and closed between sweeps): {error}" +
                             (unclear ? " — outcome unknown; it is checked in ACC before any re-send" : ""));
                if (unclear)
                {
                    pendingVerify[key] = DateTime.UtcNow.AddMinutes(-2);
                    cfg[KeyIssuePendingVerify] = PendingJson(pendingVerify);
                    dirty = true;
                }
            }
        }

        if (dirty)
        {
            cfg[KeyIssueClosedReported] = new JArray(reported.OrderBy(k => k, StringComparer.Ordinal));
            conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
            await _db.SaveChangesAsync(ct);
        }

        var notes = new List<string>();
        if (policyError != null) notes.Add(policyError);
        if (notSent > 0)
            notes.Add($"{notSent} issue(s) were raised and closed in Planscape between syncs and were NOT created in ACC: " +
                      string.Join(", ", notSentCodes.Take(10)) + (notSentCodes.Count > 10 ? ", …" : "") +
                      $". Set {KeyClosedBetweenSweeps}=\"create\" to create such issues in ACC, closed.");
        return (pushed, notSent, failures, notes.Count == 0 ? null : string.Join(" | ", notes));
    }

    /// <summary>C10: PATCH mapped Planscape-born issues edited since their last push.</summary>
    private async Task<(int updated, int diverged, List<string> failures)> PushUpdatesAsync(
        HttpClient http, PlatformConnection conn, JObject cfg, Dictionary<string, string> map,
        Dictionary<string, DateTime> pushedAt, string? region, CancellationToken ct)
    {
        int updated = 0, diverged = 0;
        var failures = new List<string>();
        if (map.Count == 0) return (0, 0, failures);

        var ids = map.Keys.Select(k => Guid.TryParse(k, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        var mapped = await _db.Issues.Where(i => i.ProjectId == conn.ProjectId && ids.Contains(i.Id)).ToListAsync(ct);
        var lastStatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (cfg[KeyIssueStatus] is JObject js)
            foreach (var kv in js) if (kv.Value?.Type == JTokenType.String) lastStatus[kv.Key] = kv.Value.Value<string>()!;
        var pushedState = cfg[KeyIssuePushedState] as JObject ?? new JObject();

        bool dirty = false;
        foreach (var issue in mapped)
        {
            string key = issue.Id.ToString();
            string accId = map[key];
            // ACC owns it: its edits flow ACC → Planscape, never back.
            if (AccOriginId(issue) != null || string.Equals(issue.Source, "acc", StringComparison.OrdinalIgnoreCase)) continue;
            // A mapping from before this change has no baseline: record one, PATCH nothing
            // (a first run must not rewrite every ACC issue from Planscape).
            var prev = AccIssueUpdatePlan.Snapshot.From(pushedState[key] as JObject);
            if (!pushedAt.TryGetValue(key, out var at) || prev == null)
            {
                // No baseline (a mapping from before C10/E2): record what Planscape holds now and
                // PATCH nothing — a first run must not rewrite every ACC issue from Planscape.
                pushedAt[key] = issue.UpdatedAt;
                pushedState[key] = AccIssueUpdatePlan.Snapshot.Of(issue.Title, issue.Description, issue.Status).ToJson();
                dirty = true;
                continue;
            }
            if (issue.UpdatedAt <= at) continue;

            var decision = AccIssueUpdatePlan.Plan(prev, issue.Title, issue.Description, issue.Status,
                lastStatus.TryGetValue(accId, out var st) ? st : null);
            if (decision.Body["status"] != null)
            {
                // F4: the guard above used the previous sweep's read-back, which can be a sweep
                // old (or absent). A status is only sent against ACC's status as it is NOW; if
                // that cannot be read, the status is withheld and re-tried next sweep.
                var (nowStatuses, nowErr) = await ReadBackStatusesAsync(http, conn, new List<string> { accId }, region, ct);
                if (nowStatuses != null && nowStatuses.TryGetValue(accId, out var accNow))
                    decision = AccIssueUpdatePlan.Plan(prev, issue.Title, issue.Description, issue.Status, accNow);
                else
                {
                    decision = AccIssueUpdatePlan.WithoutStatus(decision, prev);
                    failures.Add($"{issue.IssueCode} (update): status not sent - ACC's current status could not be read" +
                                 (nowErr != null ? $" ({nowErr})" : " (ACC did not return the issue)"));
                }
            }
            if (decision.StatusWithheld) diverged++;
            if (decision.Body.Count == 0)
            {
                // UpdatedAt moved but nothing ACC carries changed (or only a withheld status):
                // nothing to send, and nothing of ACC's is touched. F3: a withheld status does
                // NOT advance pushedAt, so the issue is re-planned (and the divergence reported)
                // every sweep until ACC and Planscape agree or ACC returns to what was pushed.
                if (!decision.StatusWithheld) pushedAt[key] = issue.UpdatedAt;
                pushedState[key] = decision.Next.ToJson();
                dirty = true;
                continue;
            }
            var freshUpd = await RefreshMidRunAsync(conn, ct);
            if (freshUpd != null) { failures.Add($"{issue.IssueCode} (update): {freshUpd}"); break; }
            var (ok, error) = await PatchIssueAsync(http, conn, accId, decision.Body, region, ct);
            if (ok)
            {
                updated++;
                if (!decision.StatusWithheld) pushedAt[key] = issue.UpdatedAt;   // F3, as above
                pushedState[key] = decision.Next.ToJson();
                dirty = true;
            }
            else
            {
                failures.Add($"{issue.IssueCode} (update): {error}");
                _logger.LogWarning("AccSyncService: update of issue {Code} failed: {Error}", issue.IssueCode, error);
            }
        }
        if (dirty)
        {
            cfg[KeyIssuePushedAt] = PushedAtJson(pushedAt);
            cfg[KeyIssuePushedState] = pushedState;
            conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
            await _db.SaveChangesAsync(ct);
        }
        return (updated, diverged, failures);
    }

    private async Task<(bool ok, string? error)> PatchIssueAsync(
        HttpClient http, PlatformConnection conn, string accId, JObject body, string? region, CancellationToken ct)
    {
        string url = $"{ApsEndpoints.IssuesProjectUrl(_config, conn.ExternalProjectId)}/issues/{Uri.EscapeDataString(accId)}";
        string json = body.ToString();
        try
        {
            // PATCH sets fields to values: repeating it is harmless, so it may retry.
            using var resp = await ApsRetry.SendAsync(http, () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Patch, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", conn.AccessToken);
                if (!string.IsNullOrWhiteSpace(region)) req.Headers.TryAddWithoutValidation("x-ads-region", region);
                return req;
            }, idempotent: true, _logger, ct);
            if (resp.IsSuccessStatusCode) return (true, null);
            string respBody = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("ACC issue update {Id} HTTP {Status}: {Body}", accId, (int)resp.StatusCode, Truncate(respBody, 1000));
            return (false, $"ACC rejected the update (HTTP {(int)resp.StatusCode}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (false, ex.Message);
        }
    }

    private static Dictionary<string, DateTime> ReadPushedAt(JObject cfg)
    {
        var d = new Dictionary<string, DateTime>();
        if (cfg[KeyIssuePushedAt] is JObject jo)
            foreach (var kv in jo)
            {
                // A Date token's ToString() drops sub-second precision, which made every
                // pushed issue look newer than its stamp and re-PATCHed it on every sync.
                if (kv.Value?.Type == JTokenType.Date) d[kv.Key] = kv.Value.Value<DateTime>();
                else if (kv.Value?.Type == JTokenType.String &&
                         DateTime.TryParse(kv.Value.Value<string>(), System.Globalization.CultureInfo.InvariantCulture,
                                           System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                    d[kv.Key] = t;
            }
        return d;
    }

    /// <summary>Round-trip ("o") strings: no precision or culture loss.</summary>
    private static JObject PushedAtJson(Dictionary<string, DateTime> pushedAt)
    {
        var jo = new JObject();
        foreach (var kv in pushedAt) jo[kv.Key] = kv.Value.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        return jo;
    }

    /// <summary>D5: refresh the access token when it is close to expiry part-way through a
    /// sync. No-op while fresh. Returns null when usable, else why not.</summary>
    private async Task<string?> RefreshMidRunAsync(PlatformConnection conn, CancellationToken ct)
    {
        if (AccTokenRefresher.IsFresh(conn, TimeSpan.FromMinutes(5))) return null;
        var tok = await AccTokenRefresher.EnsureFreshAsync(_db, _connectorFactory.GetConnector(PlatformType.ACC), conn, _logger, ct);
        return tok.Success ? null : "the ACC token expired during the sync and could not be refreshed: " + tok.Error;
    }

    /// <summary>D2: an ACC issue with this issue's (truncated) title created since
    /// <paramref name="since"/>. (id, null) found; (null, null) proven absent; (null, error) unknown.</summary>
    private async Task<(string? accId, string? error)> FindCreatedSinceAsync(
        HttpClient http, PlatformConnection conn, BimIssue issue, DateTime since, string? region, CancellationToken ct)
    {
        string title = Truncate(issue.Title, TitleMax);
        string from = since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            for (int offset = 0, page = 0; page < 20; offset += 100, page++)
            {
                string url = $"{ApsEndpoints.IssuesProjectUrl(_config, conn.ExternalProjectId)}/issues" +
                             $"?filter[createdAt]={Uri.EscapeDataString(from + "..")}&limit=100&offset={offset}";
                using var resp = await ApsRetry.SendAsync(http, () => Get(url, conn.AccessToken!, region), true, _logger, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode) return (null, $"HTTP {(int)resp.StatusCode}");
                var j = JObject.Parse(body);
                var results = j["results"] as JArray ?? new JArray();
                foreach (var r in results)
                    if (string.Equals(((string?)r["title"] ?? "").Trim(), title.Trim(), StringComparison.Ordinal))
                        return ((string?)r["id"], null);
                int total = (int?)j["pagination"]?["totalResults"] ?? results.Count;
                if (offset + results.Count >= total || results.Count == 0) return (null, null);
            }
            return (null, "more than 2,000 issues created since the attempt — not proven absent");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (null, ex.Message);
        }
    }

    private static Dictionary<string, DateTime> ReadPendingVerify(JObject cfg)
    {
        var d = new Dictionary<string, DateTime>();
        if (cfg[KeyIssuePendingVerify] is JObject jo)
            foreach (var kv in jo)
            {
                if (kv.Value?.Type == JTokenType.Date) d[kv.Key] = kv.Value.Value<DateTime>();
                else if (kv.Value?.Type == JTokenType.String &&
                         DateTime.TryParse(kv.Value.Value<string>(), System.Globalization.CultureInfo.InvariantCulture,
                                           System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                    d[kv.Key] = t;
            }
        return d;
    }

    private static JObject PendingJson(Dictionary<string, DateTime> pending)
    {
        var jo = new JObject();
        foreach (var kv in pending) jo[kv.Key] = kv.Value.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        return jo;
    }

    /// <summary>D2: a create whose outcome is unknown — it may have landed in ACC.</summary>
    internal static bool IsUnclearCreateStatus(int status)
        => status == 408 || status == 500 || status == 502 || status == 503 || status == 504;

    private async Task<(bool ok, string? accId, string? error, bool unclear)> PushIssueAsync(
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
                int code = (int)resp.StatusCode;
                return (false, null, $"ACC rejected the issue (HTTP {code}).", IsUnclearCreateStatus(code));
            }

            var j = JObject.Parse(respBody);
            string accId = (string?)j["id"] ?? (string?)j["data"]?["id"] ?? "";
            return string.IsNullOrEmpty(accId)
                ? (false, null, "ACC response had no issue id.", true)
                : (true, accId, null, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A timeout or dropped connection after the request was sent: it may have landed.
            return (false, null, ex is TaskCanceledException ? "the request timed out" : ex.Message, true);
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
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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
                bool complete = false;
                for (int offset = 0, page = 0; page < ReadBackMaxPages; offset += 100, page++)
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
                    if (results.Count < 100 || (total.HasValue && offset + results.Count >= total.Value)) { complete = true; break; }
                }
                // AUT-3: stopping at the page cap used to fall through, and every id not yet
                // read was then recorded as "not_found" - a deleted issue that was never deleted.
                if (!complete)
                    return (null, $"INCOMPLETE: stopped after {ReadBackMaxPages} pages for one batch of {batch.Length} ids");
            }
            // Mapped ids ACC no longer returns (deleted, or no longer visible to this grant).
            foreach (var id in accIds)
                if (!result.ContainsKey(id)) result[id] = "not_found";
            return (result, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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
        // D8: the guard ran out with a next link still set — more remain. A part of the list
        // must never read as the list (a webhook subscribe would miss folders, a hub its projects).
        if (url != null) return (null, $"stopped after 100 pages ({items.Count} entries) with more to read - the list is INCOMPLETE.");
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
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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

    /// <summary>The ACC issue id an issue originated from, when it carries one
    /// (CustomFields {"accIssueId": "..."}), else null. Never throws.</summary>
    internal static string? AccOriginId(Planscape.Core.Entities.BimIssue issue)
    {
        if (string.IsNullOrWhiteSpace(issue?.CustomFields)) return null;
        try
        {
            var id = (string?)JObject.Parse(issue!.CustomFields!)["accIssueId"];
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }
        catch (Newtonsoft.Json.JsonException) { return null; }
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
    ///
    /// H-2: a MERGE, as PlatformController's comment always said - a top-level key the client
    /// sends replaces the stored one; a key it omits is KEPT; a key sent as <c>null</c> is
    /// removed. It used to replace the whole object, so turning SSA on with
    /// <c>{"accAuthMode":"ssa"}</c> silently dropped the hub, region and issue subtype, and any
    /// later PUT that left accAuthMode out silently turned SSA off.
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

        if (inc[Aps.ApsSsa.ModeKey] is JToken modeTok && modeTok.Type != JTokenType.Null)
        {
            string mode = ((string?)modeTok ?? "").Trim().ToLowerInvariant();
            if (mode != Aps.ApsSsa.ModeSsa && mode != "oauth" && mode.Length != 0)
                return (null, $"{Aps.ApsSsa.ModeKey} must be \"ssa\" or \"oauth\" (or null to remove it), not \"{(string?)modeTok}\".");
            inc[Aps.ApsSsa.ModeKey] = mode;
        }

        var result = new JObject();
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
            result = (JObject)existing.DeepClone();
        }
        foreach (var p in inc.Properties())
        {
            if (p.Value.Type == JTokenType.Null) result.Remove(p.Name);   // explicit null removes
            else result[p.Name] = p.Value.DeepClone();
        }
        return (result.ToString(Newtonsoft.Json.Formatting.None), null);
    }

    /// <summary>The ACC Issues status for a create: a known Planscape status, else "open".</summary>
    internal static string MapStatus(string s) => MapStatusOrNull(s) ?? "open";

    /// <summary>E2: case-insensitive; IN_PROGRESS is its own ACC status (it was sent as
    /// "open"); an unknown status maps to null and is never sent on an update.</summary>
    internal static string? MapStatusOrNull(string? s) => (s ?? "").Trim().Replace(" ", "_").ToUpperInvariant() switch
    {
        "OPEN" => "open",
        "IN_PROGRESS" or "INPROGRESS" => "in_progress",
        "RESOLVED" => "completed",
        "CLOSED" => "closed",
        _ => null,
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

/// <summary>
/// C10: what one Planscape → ACC issue update sends. Pure: the rule is tested without HTTP.
/// The status is withheld when ACC last reported the issue closed and Planscape would reopen
/// it — ACC's close is the later fact the read-back has not yet carried into Planscape.
/// </summary>
/// <summary>ACC-SRV-11: what to do with an issue raised and closed between two sweeps.</summary>
public static class AccClosedSweepPolicy
{
    public enum Mode { Report, Create }

    /// <summary>Absent or blank means Report. An unrecognised value is reported and treated as
    /// Report, the choice that sends nothing to ACC.</summary>
    public static Mode Parse(string? value, out string? error)
    {
        error = null;
        string v = (value ?? "").Trim();
        if (v.Length == 0 || v.Equals("report", StringComparison.OrdinalIgnoreCase)) return Mode.Report;
        if (v.Equals("create", StringComparison.OrdinalIgnoreCase)) return Mode.Create;
        error = $"{AccSyncService.KeyClosedBetweenSweeps}=\"{v}\" is not one of report|create — treated as report.";
        return Mode.Report;
    }
}

public static class AccIssueUpdatePlan
{
    /// <summary>What was last pushed for one issue: its Planscape status, and hashes of the
    /// title and description as sent (truncated as ACC receives them).</summary>
    public sealed record Snapshot(string Status, string TitleHash, string DescriptionHash)
    {
        public static Snapshot Of(string? title, string? description, string? status)
            => new((status ?? "").Trim().ToUpperInvariant(), Hash(Trunc(title, 100)), Hash(Trunc(description, 1000)));

        public static Snapshot? From(JObject? o)
            => o == null ? null : new((string?)o["s"] ?? "", (string?)o["t"] ?? "", (string?)o["d"] ?? "");

        public JObject ToJson() => new() { ["s"] = Status, ["t"] = TitleHash, ["d"] = DescriptionHash };
    }

    public sealed record Decision(JObject Body, bool StatusWithheld, Snapshot Next);

    /// <summary>F4: the same decision with its status taken out (ACC's current status is
    /// unknown). The status stays at what was last pushed, so the next sweep tries again.</summary>
    public static Decision WithoutStatus(Decision d, Snapshot previous)
    {
        var body = (JObject)d.Body.DeepClone();
        body.Remove("status");
        return new Decision(body, true, d.Next with { Status = previous.Status });
    }

    /// <summary>
    /// E2: send only what changed in Planscape since <paramref name="previous"/> was pushed.
    /// Title and description go when their text changed. The status goes only when Planscape's
    /// status changed AND ACC still shows the value pushed last time: if the ACC assignee has
    /// moved it since (in_progress, completed, closed…) that is the later fact, so the status is
    /// withheld and the divergence reported. An unknown Planscape status is never sent.
    /// </summary>
    public static Decision Plan(Snapshot previous, string? title, string? description, string? planscapeStatus, string? accLastStatus)
    {
        var now = Snapshot.Of(title, description, planscapeStatus);
        var body = new JObject();
        if (now.TitleHash != previous.TitleHash) body["title"] = Trunc(title, 100);
        if (now.DescriptionHash != previous.DescriptionHash) body["description"] = Trunc(description, 1000);

        bool withheld = false;
        var next = now;
        if (!string.Equals(now.Status, previous.Status, StringComparison.Ordinal))
        {
            string? target = AccSyncService.MapStatusOrNull(planscapeStatus);
            string? expected = AccSyncService.MapStatusOrNull(previous.Status);
            bool accMoved = accLastStatus != null && expected != null &&
                            !string.Equals(accLastStatus, expected, StringComparison.OrdinalIgnoreCase);
            if (target == null) next = now with { Status = previous.Status };
            else if (string.Equals(accLastStatus, target, StringComparison.OrdinalIgnoreCase)) { /* ACC already agrees */ }
            else if (accMoved || string.Equals(accLastStatus, "closed", StringComparison.OrdinalIgnoreCase) && target != "closed")
            {
                withheld = true;
                next = now with { Status = previous.Status };
            }
            else body["status"] = target;
        }
        return new Decision(body, withheld, next);
    }

    private static string Trunc(string? s, int n) => string.IsNullOrEmpty(s) ? "" : (s!.Length > n ? s.Substring(0, n) : s);

    private static string Hash(string s)
    {
        var b = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(b, 0, 8);
    }
}

