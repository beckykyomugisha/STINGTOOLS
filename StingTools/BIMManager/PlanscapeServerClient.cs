#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StingTools.Core;

namespace StingTools.BIMManager;

/// <summary>
/// HTTP client for Planscape Server API.
/// Handles authentication, automatic token refresh, and all sync/query operations.
/// Thread-safe singleton — use PlanscapeServerClient.Instance.
/// </summary>
public sealed class PlanscapeServerClient : IDisposable
{
    // ── Singleton ──────────────────────────────────────────────────────────────
    private static PlanscapeServerClient? _instance;
    private static readonly object _instanceLock = new();

    public static PlanscapeServerClient Instance
    {
        get
        {
            if (_instance == null)
                lock (_instanceLock) { _instance ??= new PlanscapeServerClient(); }
            return _instance;
        }
    }

    // ── HTTP ───────────────────────────────────────────────────────────────────
    private HttpClient? _http;
    private readonly SemaphoreSlim _httpSem = new(1, 1);

    // ── Auth state ─────────────────────────────────────────────────────────────
    private string _serverUrl    = "";
    private string _accessToken  = "";
    private string _refreshToken = "";
    private DateTime _tokenExpiry = DateTime.MinValue;

    // SEC-EA-08 — local mirror of the server's sliding inactivity window.
    // Updated on every successful API call; if the gap exceeds the
    // window we tear down the session before attempting a refresh
    // (the server will reject it anyway). Mirrors AuthController.cs
    // RefreshInactivityWindow on the server.
    private DateTime _lastActivityTime = DateTime.UtcNow;
    private static readonly TimeSpan SessionInactivityWindow = TimeSpan.FromMinutes(60);

    // ── Public state ───────────────────────────────────────────────────────────
    public bool   IsConnected   => !string.IsNullOrEmpty(_accessToken) && _tokenExpiry > DateTime.UtcNow.AddMinutes(5);
    public string ServerUrl     => _serverUrl;
    /// <summary>
    /// S03: JWT bearer token acquired during login. Used by the Planscape
    /// SyncScheduler so background sync doesn't have to re-authenticate.
    /// </summary>
    public string AuthToken     => _accessToken;
    public string ConnectedUser { get; private set; } = "";
    public string TierName      { get; private set; } = "";
    public bool   MimEnabled    { get; private set; }
    public string? LastError    { get; private set; }

    /// <summary>C2 — tenant + user IDs parsed from the login response's JWT payload
    /// so the real-time client can join the right SignalR groups.</summary>
    public Guid TenantId { get; private set; }
    public Guid UserId   { get; private set; }

    private PlanscapeServerClient() { }

    // ────────────────────────────────────────────────────────────────────────────
    //  Authentication
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Login with email + password. Stores JWT + refresh token in memory.</summary>
    public async Task<bool> LoginAsync(string serverUrl, string email, string password)
    {
        try
        {
            _serverUrl = serverUrl.TrimEnd('/');
            EnsureHttpClient(_serverUrl);

            // ConfigureAwait(false) prevents the continuation from being
            // posted back to a captured SynchronizationContext (e.g. the
            // WPF dispatcher when this is called via .GetResult() from
            // an IExternalEventHandler). Without it, the dispatcher
            // deadlocks waiting for itself.
            var resp = await PostJsonAsync("/api/auth/login", new { email, password }).ConfigureAwait(false);
            if (!resp.ok)
            {
                // 404 means the server URL is reachable but doesn't host the
                // Planscape API. The most common cause is pointing at the
                // retired Render deployment; nudge the user toward the local
                // docker stack.
                LastError = resp.status == 404
                    ? $"Login failed: server at {_serverUrl} did not recognise /api/auth/login (HTTP 404). Confirm the URL — for the docker-compose stack use http://localhost:5000."
                    : resp.status == 401
                        ? "Login failed: email or password is incorrect."
                        : $"Login failed (HTTP {resp.status}): {resp.body}";
                return false;
            }

            ParseAuthResponse(JObject.Parse(resp.body), email);
            LastError = null;
            // SEC-EA-08 — seed the inactivity clock; the PostJsonAsync that
            // wrote /api/auth/login already bumped it but make the intent
            // explicit at the auth boundary.
            TouchActivity();
            // P1 — store the session so a Revit restart doesn't require
            // re-entering credentials. Encrypted with DPAPI (current-user).
            PersistSession();
            StingLog.Info($"Planscape: Authenticated as {ConnectedUser} @ {_serverUrl} (tier: {TierName})");

            // C2 — fire-and-forget SignalR start so real-time updates flow without
            // blocking the login UX. Failures are logged but not fatal.
            if (TenantId != Guid.Empty && UserId != Guid.Empty)
            {
                _ = Task.Run(async () =>
                {
                    try { await PlanscapeRealtimeClient.Instance.StartAsync(_serverUrl, _accessToken, TenantId, UserId); }
                    catch (Exception ex) { StingLog.Warn($"Planscape: realtime start failed — {ex.Message}"); }
                });
            }
            return true;
        }
        catch (Exception ex)
        {
            LastError = BuildConnectivityHint(ex, _serverUrl);
            StingLog.Error("Planscape: Login failed", ex);
            return false;
        }
    }

    /// <summary>Maps the raw HttpClient/socket exception to an actionable message.
    /// Connection refused on localhost almost always means the docker stack
    /// isn't running, which is the most common first-time-setup mistake.</summary>
    private static string BuildConnectivityHint(Exception ex, string serverUrl)
    {
        // Walk the inner exception chain — HttpRequestException usually wraps
        // a SocketException whose ErrorCode tells us refused vs. unreachable
        // vs. DNS failure.
        var sock = ex as System.Net.Sockets.SocketException;
        for (var cur = ex; sock == null && cur != null; cur = cur.InnerException)
            sock = cur.InnerException as System.Net.Sockets.SocketException;

        bool isLocal = !string.IsNullOrEmpty(serverUrl) &&
                       (serverUrl.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        serverUrl.IndexOf("127.0.0.1", StringComparison.OrdinalIgnoreCase) >= 0);

        if (sock != null)
        {
            switch (sock.SocketErrorCode)
            {
                case System.Net.Sockets.SocketError.ConnectionRefused:
                    return isLocal
                        ? $"Login failed: nothing is listening on {serverUrl}. Start the local Planscape server with 'docker compose up -d' from Planscape.Server/docker, then wait for the 'api' container to become healthy (docker compose ps)."
                        : $"Login failed: {serverUrl} refused the connection. The server may be stopped or a firewall is blocking the port.";
                case System.Net.Sockets.SocketError.HostNotFound:
                case System.Net.Sockets.SocketError.NoData:
                    return $"Login failed: could not resolve '{serverUrl}'. Check the URL spelling and your DNS/internet connection.";
                case System.Net.Sockets.SocketError.TimedOut:
                    return $"Login failed: connection to {serverUrl} timed out. Check the server is reachable from this machine and no firewall is dropping the request.";
                case System.Net.Sockets.SocketError.NetworkUnreachable:
                case System.Net.Sockets.SocketError.HostUnreachable:
                    return $"Login failed: {serverUrl} is not reachable from this network.";
            }
        }
        if (ex is TaskCanceledException || ex is OperationCanceledException)
            return $"Login failed: request to {serverUrl} timed out before the server responded.";
        return $"Login failed: {ex.Message}";
    }

    /// <summary>
    /// Silently refresh the access token using the stored refresh token.
    /// Called automatically before any API call when the token is near expiry.
    /// </summary>
    public async Task<bool> RefreshTokenAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken)) return false;
        try
        {
            var resp = await PostJsonAsync("/api/auth/refresh", new { refreshToken = _refreshToken }).ConfigureAwait(false);
            if (!resp.ok) { StingLog.Warn($"Planscape: Token refresh failed: {resp.body}"); return false; }

            var json = JObject.Parse(resp.body);
            _accessToken  = json["accessToken"]?.Value<string>()  ?? "";
            _refreshToken = json["refreshToken"]?.Value<string>() ?? _refreshToken;
            _tokenExpiry  = json["expiresAt"]?.Value<DateTime>()  ?? DateTime.UtcNow.AddHours(8);
            _http!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            // P1 — persist the rotated tokens so they survive a Revit
            // restart between scheduled syncs.
            PersistSession();
            StingLog.Info("Planscape: Token refreshed.");
            return true;
        }
        catch (Exception ex) { StingLog.Warn($"Planscape: Token refresh error: {ex.Message}"); return false; }
    }

    /// <summary>Ensure the token is valid, refreshing if needed. Returns false if not authenticated.</summary>
    private async Task<bool> EnsureAuthenticatedAsync()
    {
        if (string.IsNullOrEmpty(_accessToken)) { LastError = "Not connected to Planscape server."; return false; }

        // SEC-EA-08 — local idle gate. If we've been silent longer than
        // the server's inactivity window, the server will reject the
        // refresh anyway; clear state up-front so the user gets a clean
        // "log in again" message rather than a confusing 401 mid-sync.
        if (DateTime.UtcNow - _lastActivityTime > SessionInactivityWindow)
        {
            StingLog.Info("Planscape: session idle past inactivity window — clearing tokens.");
            _accessToken  = "";
            _refreshToken = "";
            _tokenExpiry  = DateTime.MinValue;
            try { _http?.DefaultRequestHeaders.Authorization = null; } catch { }
            DeletePersistedSession();
            throw new InvalidOperationException(
                "Planscape session expired. Please log in again from the BIM tab.");
        }

        // SEC-EA-03 — access tokens are now 30 min, so refresh once <5 min remain.
        if (_tokenExpiry <= DateTime.UtcNow.AddMinutes(5))
        {
            var ok = await RefreshTokenAsync().ConfigureAwait(false);
            if (ok) _lastActivityTime = DateTime.UtcNow;
            return ok;
        }
        return true;
    }

    /// <summary>SEC-EA-08 — call after every successful API round-trip.</summary>
    private void TouchActivity() => _lastActivityTime = DateTime.UtcNow;

    /// <summary>Disconnect and clear all credentials.</summary>
    public void Disconnect()
    {
        _accessToken  = "";
        _refreshToken = "";
        _tokenExpiry  = DateTime.MinValue;
        ConnectedUser = "";
        TenantId      = Guid.Empty;
        UserId        = Guid.Empty;
        _http?.DefaultRequestHeaders.Authorization = null;
        // P1 — explicit logout removes the persisted session so the next
        // Revit start does not auto-restore.
        DeletePersistedSession();

        // C2 — stop the real-time listener (fire-and-forget; we don't await in a sync method).
        _ = Task.Run(async () => { try { await PlanscapeRealtimeClient.Instance.StopAsync(); } catch { } });

        StingLog.Info("Planscape: Disconnected.");
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Projects
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>List all projects accessible to the current user.</summary>
    public async Task<JArray?> GetProjectsAsync()
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await GetAsync("/api/projects");
            return resp.ok ? JArray.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>
    /// Find a project by name, or create it if it doesn't exist.
    /// Returns the project Id, or Guid.Empty on failure.
    /// </summary>
    public async Task<Guid> GetOrCreateProjectAsync(string projectName, string projectCode)
    {
        if (!await EnsureAuthenticatedAsync()) return Guid.Empty;
        try
        {
            var projects = await GetProjectsAsync();
            if (projects != null)
            {
                foreach (var p in projects)
                {
                    if (string.Equals(p["name"]?.Value<string>(), projectName, StringComparison.OrdinalIgnoreCase))
                        return Guid.Parse(p["id"]!.Value<string>()!);
                }
            }

            // Not found — create
            var resp = await PostJsonAsync("/api/projects", new { name = projectName, code = projectCode });
            if (!resp.ok) { LastError = $"Create project failed: {resp.body}"; return Guid.Empty; }
            var json = JObject.Parse(resp.body);
            return Guid.TryParse(json["id"]?.Value<string>(), out var id) ? id : Guid.Empty;
        }
        catch (Exception ex) { LastError = ex.Message; return Guid.Empty; }
    }

    /// <summary>Get project dashboard (issues, docs, recent workflows).</summary>
    public async Task<JObject?> GetDashboardAsync(Guid projectId)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/dashboard");
            return resp.ok ? JObject.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Full sync (plugin v2.2+)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full sync: elements + compliance snapshot + warning summary + SEQ counters.
    /// Sends everything in one call. Auto-creates the project if projectId == Guid.Empty.
    /// </summary>
    [Obsolete("Use SyncScheduler for sync operations")]
    public async Task<FullSyncResult> FullSyncAsync(FullSyncPayload payload)
    {
        if (!await EnsureAuthenticatedAsync())
            return new FullSyncResult { Success = false, Error = LastError ?? "Not connected." };

        try
        {
            var resp = await PostJsonAsync("/api/tagsync/fullsync",
                new
                {
                    projectId     = payload.ProjectId,
                    projectName   = payload.ProjectName,
                    projectCode   = payload.ProjectCode,
                    userName      = ConnectedUser,
                    revitVersion  = payload.RevitVersion,
                    pluginVersion = payload.PluginVersion,
                    elements      = payload.Elements,
                    compliance    = payload.Compliance,
                    warnings      = payload.Warnings,
                    seqCounters   = payload.SeqCounters
                });

            if (!resp.ok) { LastError = $"FullSync failed ({resp.status}): {resp.body}"; return new FullSyncResult { Success = false, Error = LastError }; }

            var json = JObject.Parse(resp.body);
            return new FullSyncResult
            {
                Success           = true,
                ProjectId         = Guid.TryParse(json["projectId"]?.Value<string>(), out var pid) ? pid : payload.ProjectId,
                ProjectCreated    = json["projectCreated"]?.Value<bool>() ?? false,
                Received          = json["received"]?.Value<int>()          ?? payload.Elements.Count,
                Created           = json["created"]?.Value<int>()           ?? 0,
                Updated           = json["updated"]?.Value<int>()           ?? 0,
                SeqCountersSaved  = json["seqCountersSaved"]?.Value<int>()  ?? 0,
                CompliancePercent = json["compliancePercent"]?.Value<double>() ?? 0,
                RagStatus         = json["ragStatus"]?.Value<string>()      ?? "AMBER"
            };
        }
        catch (Exception ex) { LastError = ex.Message; StingLog.Error("Planscape: FullSync failed", ex); return new FullSyncResult { Success = false, Error = ex.Message }; }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Legacy tag sync (kept for backwards-compat with plugin v2.1 callers)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Legacy: sync elements only via POST /api/tagsync/sync.</summary>
    [Obsolete("Use SyncScheduler for sync operations")]
    public async Task<SyncResult> SyncElementsAsync(
        Guid projectId, string revitVersion, string pluginVersion,
        List<TagElementPayload> elements)
    {
        if (!await EnsureAuthenticatedAsync())
            return new SyncResult { Success = false, Error = LastError ?? "Not connected." };

        try
        {
            var resp = await PostJsonAsync("/api/tagsync/sync", new
            {
                projectId, revitVersion, pluginVersion,
                userName = ConnectedUser, elements
            });
            if (!resp.ok) { LastError = $"Sync failed ({resp.status}): {resp.body}"; return new SyncResult { Success = false, Error = LastError }; }

            var json = JObject.Parse(resp.body);
            return new SyncResult
            {
                Success           = true,
                Received          = json["received"]?.Value<int>()          ?? elements.Count,
                Created           = json["created"]?.Value<int>()           ?? 0,
                Updated           = json["updated"]?.Value<int>()           ?? 0,
                CompliancePercent = json["compliancePercent"]?.Value<double>() ?? 0,
                RagStatus         = json["ragStatus"]?.Value<string>()      ?? "AMBER"
            };
        }
        catch (Exception ex) { LastError = ex.Message; StingLog.Error("Planscape: Sync failed", ex); return new SyncResult { Success = false, Error = ex.Message }; }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Compliance
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Get server-computed compliance summary for a project.</summary>
    public async Task<JObject?> GetComplianceAsync(Guid projectId)
    {
        if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return null; }
        try
        {
            var resp = await GetAsync($"/api/tagsync/compliance/{projectId}");
            return resp.ok ? JObject.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>
    /// INT-03 delta pull — GET /api/tagsync/elements/{projectId}?lastSyncUtc=&lt;watermark&gt;.
    /// When <paramref name="lastSyncUtc"/> is supplied, the server returns only elements
    /// whose LastModifiedUtc (or SyncedAt fallback) is newer than the watermark. With
    /// <c>null</c>, acts as a plain paginated list. Server-side per-device watermark
    /// is tracked via the X-Device-Id header (defaults to "desktop" when absent).
    /// </summary>
    public async Task<JArray?> GetElementsDeltaAsync(Guid projectId, DateTime? lastSyncUtc,
        int page = 1, int pageSize = 500)
    {
        if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return null; }
        try
        {
            var path = $"/api/tagsync/elements/{projectId}?page={page}&pageSize={pageSize}";
            if (lastSyncUtc.HasValue)
                path += $"&lastSyncUtc={Uri.EscapeDataString(lastSyncUtc.Value.ToUniversalTime().ToString("o"))}";
            var resp = await GetAsync(path);
            if (!resp.ok) { LastError = $"Delta pull failed ({resp.status}): {resp.body}"; return null; }
            var json = JObject.Parse(resp.body);
            return json["elements"] as JArray ?? new JArray();
        }
        catch (Exception ex) { LastError = ex.Message; StingLog.Warn($"GetElementsDeltaAsync: {ex.Message}"); return null; }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Issues
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Get open issues for a project (returns raw JArray).</summary>
    public async Task<JArray?> GetIssuesAsync(Guid projectId, string status = "OPEN")
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/issues?status={status}");
            if (!resp.ok) { LastError = $"HTTP {resp.status}"; return null; }
            var json = JObject.Parse(resp.body);
            return json["issues"] as JArray ?? JArray.Parse(resp.body);
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>Create an issue on the server. Returns the new issue code, or null on failure.</summary>
    public async Task<string?> CreateIssueAsync(Guid projectId, string type, string title,
        string priority = "MEDIUM", string? assignee = null, string? discipline = null,
        List<long>? linkedElementIds = null,
        string? optionSetName = null, string? optionName = null)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            // Phase 175 — optionSetName / optionName attach to BimIssue so
            // the server-side SearchController can filter site queries by
            // active design option, ensuring RFIs land against the right
            // alternative when the host doc has parallel options in flight.
            var resp = await PostJsonAsync($"/api/projects/{projectId}/issues", new
            {
                type, title, priority, assignee, discipline,
                linkedElementIds = linkedElementIds ?? new List<long>(),
                optionSetName,
                optionName
            });
            if (!resp.ok) { LastError = $"Create issue failed: {resp.body}"; return null; }
            var json = JObject.Parse(resp.body);
            return json["issueCode"]?.Value<string>();
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>Phase 175 — search server issues filtered by design option.
    /// Returns raw JArray; caller projects to its own DTO.</summary>
    public async Task<Newtonsoft.Json.Linq.JArray?> SearchIssuesByOptionAsync(
        string query, string? optionSetName = null, string? optionName = null, int limit = 25)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var url = $"/api/search?q={Uri.EscapeDataString(query)}&type=issue&limit={limit}";
            if (!string.IsNullOrEmpty(optionSetName)) url += $"&optionSet={Uri.EscapeDataString(optionSetName)}";
            if (!string.IsNullOrEmpty(optionName))    url += $"&option={Uri.EscapeDataString(optionName)}";
            var resp = await GetAsync(url);
            if (!resp.ok) return null;
            var json = JObject.Parse(resp.body);
            return json["results"] as Newtonsoft.Json.Linq.JArray;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  SEQ counters
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Fetch all server SEQ counters for a project.</summary>
    public async Task<Dictionary<string, int>> GetSeqCountersAsync(Guid projectId)
    {
        if (!await EnsureAuthenticatedAsync()) return new Dictionary<string, int>();
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/seq");
            if (!resp.ok) return new Dictionary<string, int>();
            var json = JArray.Parse(resp.body);
            return json.ToDictionary(
                t => t["counterKey"]?.Value<string>() ?? "",
                t => t["currentValue"]?.Value<int>() ?? 0);
        }
        catch { return new Dictionary<string, int>(); }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Persist / Load connection settings
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Save server URL, email, and linked project ID. Never saves passwords or tokens.</summary>
    public void SaveConnectionSettings(string configPath, string email, Guid projectId = default)
    {
        try
        {
            var settings = new JObject
            {
                ["serverUrl"]       = _serverUrl,
                ["email"]           = email,
                ["lastConnected"]   = DateTime.UtcNow.ToString("o")
            };
            if (projectId != Guid.Empty)
                settings["projectId"] = projectId.ToString();

            File.WriteAllText(configPath, settings.ToString(Formatting.Indented));
        }
        catch (Exception ex) { StingLog.Warn($"Planscape: Could not save connection settings: {ex.Message}"); }
    }

    /// <summary>Load saved connection settings (server URL, email, and linked project ID).</summary>
    public static (string serverUrl, string email, string projectId) LoadConnectionSettings(string configPath)
    {
        try
        {
            if (!File.Exists(configPath)) return ("", "", "");
            var json = JObject.Parse(File.ReadAllText(configPath));
            string url = json["serverUrl"]?.Value<string>() ?? "";

            // The legacy Render.com deployment is offline and returns 404 on
            // /api/auth/login, which surfaces in the UI as "Login failed: Not
            // Found". Drop the stale URL so the dialog falls back to its
            // current default (the local docker stack) instead of pinning
            // users to a dead host.
            if (url.IndexOf("planscape-api.onrender.com", StringComparison.OrdinalIgnoreCase) >= 0)
                url = "";

            return (url,
                    json["email"]?.Value<string>()      ?? "",
                    json["projectId"]?.Value<string>()  ?? "");
        }
        catch { return ("", "", ""); }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  C4 — Full domain coverage for Documents / Meetings / Transmittals /
    //  Workflows / Warnings / MIM. Each method follows the same pattern:
    //    1. EnsureAuthenticatedAsync (refreshes the token if within 10 minutes of expiry)
    //    2. GET / POST / PATCH through the existing helper
    //    3. Return JArray / JObject / bool / string as appropriate
    //  Callers in the BCC + Platform Sync UI can walk these without knowing the
    //  wire protocol.
    // ────────────────────────────────────────────────────────────────────────────

    // ── Documents ──────────────────────────────────────────────────────────────

    /// <summary>List documents for a project, optionally filtering by CDE status.</summary>
    public async Task<JArray?> GetDocumentsAsync(Guid projectId, string? cdeStatus = null)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var path = $"/api/projects/{projectId}/documents";
            if (!string.IsNullOrEmpty(cdeStatus)) path += $"?cdeStatus={Uri.EscapeDataString(cdeStatus)}";
            var resp = await GetAsync(path);
            return resp.ok ? (JObject.Parse(resp.body)["items"] as JArray) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>Transition a document through the ISO 19650 CDE state machine.</summary>
    public async Task<bool> TransitionDocumentAsync(Guid projectId, Guid documentId, string newStatus, string? revision = null)
    {
        if (!await EnsureAuthenticatedAsync()) return false;
        try
        {
            var resp = await PostJsonAsync(
                $"/api/projects/{projectId}/documents/{documentId}/transition",
                new { newStatus, revision });
            return resp.ok;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    // ── Meetings ───────────────────────────────────────────────────────────────

    public async Task<JArray?> GetMeetingsAsync(Guid projectId, bool upcomingOnly = true)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var path = $"/api/projects/{projectId}/meetings{(upcomingOnly ? "?upcoming=true" : "")}";
            var resp = await GetAsync(path);
            return resp.ok ? JArray.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<string?> CreateMeetingAsync(Guid projectId, string title, string type,
        DateTime scheduledAt, int durationMinutes = 60, string? agenda = null)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await PostJsonAsync($"/api/projects/{projectId}/meetings",
                new { title, type, scheduledAt, durationMinutes, agenda });
            if (!resp.ok) { LastError = resp.body; return null; }
            return JObject.Parse(resp.body)["id"]?.Value<string>();
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    // ── Transmittals ───────────────────────────────────────────────────────────

    public async Task<JArray?> GetTransmittalsAsync(Guid projectId)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/transmittals");
            return resp.ok ? JArray.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<string?> CreateTransmittalAsync(Guid projectId, string title,
        IEnumerable<Guid> documentIds, string recipients, string? purpose = null)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await PostJsonAsync($"/api/projects/{projectId}/transmittals",
                new { title, documentIds, recipients, purpose });
            if (!resp.ok) { LastError = resp.body; return null; }
            return JObject.Parse(resp.body)["id"]?.Value<string>();
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<bool> SendTransmittalAsync(Guid projectId, Guid transmittalId)
    {
        if (!await EnsureAuthenticatedAsync()) return false;
        try
        {
            var resp = await PostJsonAsync(
                $"/api/projects/{projectId}/transmittals/{transmittalId}/send", new { });
            return resp.ok;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    // ── Workflows ──────────────────────────────────────────────────────────────

    public async Task<JArray?> GetWorkflowRunsAsync(Guid projectId, int limit = 50)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/workflows?limit={limit}");
            return resp.ok ? JArray.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<bool> LogWorkflowRunAsync(Guid projectId, string preset,
        int steps, int passed, int failed, int skipped, double durationSec,
        double? complianceBefore = null, double? complianceAfter = null)
    {
        if (!await EnsureAuthenticatedAsync()) return false;
        try
        {
            var resp = await PostJsonAsync($"/api/projects/{projectId}/workflows",
                new { preset, steps, passed, failed, skipped, durationSec, complianceBefore, complianceAfter });
            return resp.ok;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    // ── Warnings ───────────────────────────────────────────────────────────────

    public async Task<JArray?> GetWarningsAsync(Guid projectId)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/warnings");
            return resp.ok ? JArray.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<bool> PushWarningsAsync(Guid projectId, object payload)
    {
        if (!await EnsureAuthenticatedAsync()) return false;
        try
        {
            var resp = await PostJsonAsync($"/api/projects/{projectId}/warnings", payload);
            return resp.ok;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    // ── MIM (Model Information Management) ────────────────────────────────────

    public async Task<JArray?> GetMimAssetsAsync(Guid projectId, int page = 1, int pageSize = 100)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/mim/assets?page={page}&pageSize={pageSize}");
            return resp.ok ? (JObject.Parse(resp.body)["items"] as JArray) ?? JArray.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<JObject?> GetMimDashboardAsync(Guid projectId)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/mim/dashboard");
            return resp.ok ? JObject.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>
    /// INT-08 — Batch upload MIM assets to <c>POST /api/projects/{id}/mim/assets/bulk</c>.
    /// The server skips duplicates by AssetTag and caps at 10,000 per request.
    /// Caller is responsible for chunking larger batches; this returns the count
    /// reported by the server (-1 on failure, with <see cref="LastError"/> set).
    /// </summary>
    public async Task<int> BulkPushMimAssetsAsync(Guid projectId, IEnumerable<object> assets)
    {
        if (!await EnsureAuthenticatedAsync()) return -1;
        try
        {
            var resp = await PostJsonAsync($"/api/projects/{projectId}/mim/assets/bulk", assets);
            if (!resp.ok) { LastError = $"HTTP {resp.body}"; return -1; }
            try
            {
                var body = JObject.Parse(resp.body);
                return body["created"]?.Value<int>() ?? 0;
            }
            catch
            {
                return 0; // server replied 2xx but body not parseable as JSON object
            }
        }
        catch (Exception ex) { LastError = ex.Message; return -1; }
    }

    /// <summary>
    /// Phase 142 — bulk-create transmittals via
    /// <c>POST /api/projects/{id}/transmittals/bulk</c> (max 200 per request).
    /// Used by the offline queue drain + workflow flush. Returns the count
    /// reported by the server, or -1 on failure with <see cref="LastError"/>.
    /// </summary>
    public async Task<int> BulkCreateTransmittalsAsync(Guid projectId, IEnumerable<object> transmittals)
    {
        if (!await EnsureAuthenticatedAsync()) return -1;
        try
        {
            var resp = await PostJsonAsync($"/api/projects/{projectId}/transmittals/bulk", transmittals);
            if (!resp.ok) { LastError = $"HTTP {resp.body}"; return -1; }
            try
            {
                var body = JObject.Parse(resp.body);
                return body["created"]?.Value<int>() ?? 0;
            }
            catch { return 0; }
        }
        catch (Exception ex) { LastError = ex.Message; return -1; }
    }

    /// <summary>
    /// Phase 142 — bulk-create meetings via
    /// <c>POST /api/projects/{id}/meetings/bulk</c> (max 200 per request).
    /// </summary>
    public async Task<int> BulkCreateMeetingsAsync(Guid projectId, IEnumerable<object> meetings)
    {
        if (!await EnsureAuthenticatedAsync()) return -1;
        try
        {
            var resp = await PostJsonAsync($"/api/projects/{projectId}/meetings/bulk", meetings);
            if (!resp.ok) { LastError = $"HTTP {resp.body}"; return -1; }
            try
            {
                var body = JObject.Parse(resp.body);
                return body["created"]?.Value<int>() ?? 0;
            }
            catch { return 0; }
        }
        catch (Exception ex) { LastError = ex.Message; return -1; }
    }

    // ── Platform connections ──────────────────────────────────────────────────

    public async Task<JArray?> GetPlatformConnectionsAsync(Guid projectId)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/platform");
            return resp.ok ? JArray.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    // ── Models (listing — UploadModelAsync already exists below) ──────────────

    public async Task<JArray?> GetModelsAsync(Guid projectId)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/models");
            return resp.ok ? JArray.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    // ── Issue comments (P2) ───────────────────────────────────────────────────

    public async Task<JArray?> GetIssueCommentsAsync(Guid projectId, Guid issueId)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/issues/{issueId}/comments");
            return resp.ok ? JArray.Parse(resp.body) : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<bool> AddIssueCommentAsync(Guid projectId, Guid issueId, string body)
    {
        if (!await EnsureAuthenticatedAsync()) return false;
        try
        {
            var resp = await PostJsonAsync(
                $"/api/projects/{projectId}/issues/{issueId}/comments", new { body });
            return resp.ok;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────────────────────────────

    private HttpClient EnsureHttpClient(string baseUrl)
    {
        lock (_httpSem)
        {
            if (_http != null && _http.BaseAddress?.ToString().TrimEnd('/') == baseUrl) return _http;
            _http?.Dispose();
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout     = TimeSpan.FromSeconds(60)
            };
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            // M12 — classify plugin-originated writes for the server audit log.
            _http.DefaultRequestHeaders.Add("X-Client-Type", "plugin");
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("StingTools-Revit/1.0");
            if (!string.IsNullOrEmpty(_accessToken))
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _accessToken);
            return _http;
        }
    }

    // Snapshot the current HttpClient under the lock so a concurrent
    // EnsureHttpClient(different base URL) that disposes+replaces _http
    // cannot race with an in-flight PostAsync/GetAsync. Callers must
    // null-check before use (null = LoginAsync hasn't run yet).
    private HttpClient? SnapshotHttpClient()
    {
        lock (_httpSem) { return _http; }
    }

    private async Task<(bool ok, int status, string body)> PostJsonAsync(string path, object payload)
    {
        var content = new StringContent(
            JsonConvert.SerializeObject(payload, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            }),
            Encoding.UTF8, "application/json");

        var http = SnapshotHttpClient();
        if (http == null) throw new InvalidOperationException("HttpClient not initialised — call LoginAsync first.");
        var resp = await http.PostAsync(path, content).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var ok = (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300;
        if (ok) TouchActivity(); // SEC-EA-08
        return (ok, (int)resp.StatusCode, body);
    }

    private async Task<(bool ok, int status, string body)> GetAsync(string path)
    {
        var http = SnapshotHttpClient();
        if (http == null) throw new InvalidOperationException("HttpClient not initialised — call LoginAsync first.");
        var resp = await http.GetAsync(path).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var ok = (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300;
        if (ok) TouchActivity(); // SEC-EA-08
        return (ok, (int)resp.StatusCode, body);
    }

    private void ParseAuthResponse(JObject json, string fallbackEmail)
    {
        _accessToken  = json["accessToken"]?.Value<string>()  ?? json["AccessToken"]?.Value<string>()  ?? "";
        _refreshToken = json["refreshToken"]?.Value<string>() ?? json["RefreshToken"]?.Value<string>() ?? "";
        _tokenExpiry  = json["expiresAt"]?.Value<DateTime>()  ?? json["ExpiresAt"]?.Value<DateTime>()  ?? DateTime.UtcNow.AddHours(8);
        ConnectedUser = json["userName"]?.Value<string>()     ?? json["UserName"]?.Value<string>()     ?? fallbackEmail;
        TierName      = json["tier"]?.Value<string>()         ?? json["Tier"]?.Value<string>()         ?? "Professional";
        MimEnabled    = json["mimEnabled"]?.Value<bool>()     ?? json["MimEnabled"]?.Value<bool>()     ?? false;
        _http!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        // C2 — decode tenant_id + sub from the JWT payload so the SignalR
        // client can register the right groups. JWT format is three base64url
        // segments separated by dots; we decode the middle one as JSON.
        (TenantId, UserId) = ParseTenantAndUser(_accessToken);
    }

    private static (Guid tenantId, Guid userId) ParseTenantAndUser(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return (Guid.Empty, Guid.Empty);
            var json = System.Text.Encoding.UTF8.GetString(
                Base64UrlDecode(parts[1]));
            var payload = JObject.Parse(json);
            var tenant = Guid.TryParse(payload["tenant_id"]?.Value<string>(), out var t) ? t : Guid.Empty;
            var sub    = Guid.TryParse(payload["sub"]?.Value<string>(), out var u) ? u : Guid.Empty;
            return (tenant, sub);
        }
        catch (Exception ex) { StingLog.Warn($"Planscape: Could not parse JWT — {ex.Message}"); return (Guid.Empty, Guid.Empty); }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    // ── P1 — encrypted session persistence ─────────────────────────────────────

    private static readonly byte[] _sessionEntropy = Encoding.UTF8.GetBytes("Planscape.PluginSession.v1");

    private static string SessionFilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Planscape");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "session.bin");
        }
    }

    /// <summary>
    /// P1 — write the current session (server URL + access token + refresh
    /// token + expiry + user metadata) to LocalAppData encrypted with
    /// DPAPI (current-user scope). Without this, a Revit restart logs the
    /// user out and forces them to re-enter credentials, which is a
    /// daily friction point for Authors.
    /// </summary>
    private void PersistSession()
    {
        try
        {
            if (string.IsNullOrEmpty(_refreshToken)) return;
            var payload = new JObject
            {
                ["serverUrl"]    = _serverUrl,
                ["accessToken"]  = _accessToken,
                ["refreshToken"] = _refreshToken,
                ["tokenExpiry"]  = _tokenExpiry,
                ["userName"]     = ConnectedUser,
                ["tier"]         = TierName,
                ["mimEnabled"]   = MimEnabled,
                ["tenantId"]     = TenantId.ToString(),
                ["userId"]       = UserId.ToString()
            }.ToString(Newtonsoft.Json.Formatting.None);

            var raw = Encoding.UTF8.GetBytes(payload);
            // System.Security.Cryptography.ProtectedData lives in
            // Microsoft.Web.Cryptography on .NET 8 — fall back to
            // unencrypted-on-non-Windows so the plugin still runs in
            // CI / Linux test contexts. Revit only ships on Windows so
            // the production path is always DPAPI.
            byte[] encrypted;
            try
            {
                encrypted = System.Security.Cryptography.ProtectedData.Protect(
                    raw, _sessionEntropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
            }
            catch (PlatformNotSupportedException)
            {
                encrypted = raw; // dev/CI only
            }
            File.WriteAllBytes(SessionFilePath, encrypted);
        }
        catch (Exception ex) { StingLog.Warn($"Planscape: PersistSession failed — {ex.Message}"); }
    }

    private void DeletePersistedSession()
    {
        try { if (File.Exists(SessionFilePath)) File.Delete(SessionFilePath); }
        catch (Exception ex) { StingLog.Warn($"Planscape: DeletePersistedSession failed — {ex.Message}"); }
    }

    /// <summary>
    /// P1 — try to restore a previously persisted session. Caller is
    /// expected to invoke this once at startup (e.g. from the dock-panel
    /// init) BEFORE prompting the user for credentials. If the access
    /// token is still valid, the user appears already-logged-in. If the
    /// access token has expired but a refresh token is present, this
    /// silently refreshes. Returns false on no-session, fatal-decrypt,
    /// or refresh-failed.
    /// </summary>
    public async Task<bool> RestoreSessionAsync()
    {
        try
        {
            if (!File.Exists(SessionFilePath)) return false;
            var encrypted = File.ReadAllBytes(SessionFilePath);
            byte[] raw;
            try
            {
                raw = System.Security.Cryptography.ProtectedData.Unprotect(
                    encrypted, _sessionEntropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
            }
            catch (PlatformNotSupportedException)
            {
                raw = encrypted; // matches PersistSession's non-Windows path
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                StingLog.Warn($"Planscape: stored session unreadable, deleting — {ex.Message}");
                DeletePersistedSession();
                return false;
            }

            var json = JObject.Parse(Encoding.UTF8.GetString(raw));
            _serverUrl    = json["serverUrl"]?.Value<string>() ?? "";
            _accessToken  = json["accessToken"]?.Value<string>() ?? "";
            _refreshToken = json["refreshToken"]?.Value<string>() ?? "";
            _tokenExpiry  = json["tokenExpiry"]?.Value<DateTime>() ?? DateTime.MinValue;
            ConnectedUser = json["userName"]?.Value<string>() ?? "";
            TierName      = json["tier"]?.Value<string>() ?? "Professional";
            MimEnabled    = json["mimEnabled"]?.Value<bool>() ?? false;
            TenantId      = Guid.TryParse(json["tenantId"]?.Value<string>(), out var tid) ? tid : Guid.Empty;
            UserId        = Guid.TryParse(json["userId"]?.Value<string>(), out var uid) ? uid : Guid.Empty;

            if (string.IsNullOrEmpty(_serverUrl) || string.IsNullOrEmpty(_refreshToken))
            {
                DeletePersistedSession();
                return false;
            }

            // Discard sessions tied to the offline Render.com host so users
            // aren't pinned to a dead URL after a Revit restart.
            if (_serverUrl.IndexOf("planscape-api.onrender.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                StingLog.Info("Planscape: stored session points to retired onrender host, clearing.");
                DeletePersistedSession();
                _serverUrl = ""; _accessToken = ""; _refreshToken = "";
                ConnectedUser = ""; TenantId = Guid.Empty; UserId = Guid.Empty;
                return false;
            }
            EnsureHttpClient(_serverUrl);
            if (!string.IsNullOrEmpty(_accessToken))
            {
                _http!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            }

            // Access token is still good — we're done.
            if (_tokenExpiry > DateTime.UtcNow.AddMinutes(5))
            {
                StingLog.Info($"Planscape: Session restored for {ConnectedUser} (token valid until {_tokenExpiry:u}).");
                return true;
            }

            // Otherwise refresh. RefreshTokenAsync rewrites _accessToken /
            // _tokenExpiry on success and persists the new state.
            if (await RefreshTokenAsync().ConfigureAwait(false))
            {
                StingLog.Info($"Planscape: Session restored + refreshed for {ConnectedUser}.");
                return true;
            }

            DeletePersistedSession();
            return false;
        }
        catch (Exception ex)
        {
            StingLog.Warn($"Planscape: RestoreSession failed — {ex.Message}");
            DeletePersistedSession();
            return false;
        }
    }

    public void Dispose()
    {
        _http?.Dispose();
        _http = null;
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Models (MODEL-VIEWER)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Upload a 3D model (glTF / GLB / IFC) plus an optional element-map sidecar
    /// to <c>POST /api/projects/{id}/models</c>. Returns the created model id on
    /// success or an error message on failure.
    /// </summary>
    public async Task<(bool ok, Guid modelId, string? error, bool alreadyExisted)> UploadModelAsync(
        Guid projectId,
        string modelFilePath,
        string? elementMapPath = null,
        string? name = null,
        string? description = null,
        string? discipline = null,
        string? revision = null,
        string units = "mm",
        int? elementCount = null,
        double[]? bounds = null)
    {
        if (!await EnsureAuthenticatedAsync()) return (false, Guid.Empty, LastError, false);
        if (!File.Exists(modelFilePath))       return (false, Guid.Empty, $"Model file not found: {modelFilePath}", false);

        try
        {
            using var content = new System.Net.Http.MultipartFormDataContent();

            // Primary geometry — streaming to avoid loading huge files into memory.
            var modelStream = File.OpenRead(modelFilePath);
            var modelContent = new System.Net.Http.StreamContent(modelStream);
            modelContent.Headers.ContentType = new MediaTypeWithQualityHeaderValue(GuessMime(modelFilePath));
            content.Add(modelContent, "File", Path.GetFileName(modelFilePath));

            // Optional element map.
            System.Net.Http.StreamContent? mapContent = null;
            FileStream? mapStream = null;
            if (!string.IsNullOrEmpty(elementMapPath) && File.Exists(elementMapPath))
            {
                mapStream = File.OpenRead(elementMapPath!);
                mapContent = new System.Net.Http.StreamContent(mapStream);
                mapContent.Headers.ContentType = new MediaTypeWithQualityHeaderValue("application/json");
                content.Add(mapContent, "ElementMap", Path.GetFileName(elementMapPath!));
            }

            // Metadata fields (PascalCase to match the server's UploadModelRequest).
            void AddField(string key, string? value)
            {
                if (value != null) content.Add(new System.Net.Http.StringContent(value, Encoding.UTF8), key);
            }
            AddField("Name", name);
            AddField("Description", description);
            AddField("Discipline", discipline);
            AddField("Revision", revision);
            AddField("Units", units);
            if (elementCount.HasValue) AddField("ElementCount", elementCount.Value.ToString());
            if (bounds != null && bounds.Length == 6)
            {
                AddField("BoundsMinX", bounds[0].ToString(System.Globalization.CultureInfo.InvariantCulture));
                AddField("BoundsMinY", bounds[1].ToString(System.Globalization.CultureInfo.InvariantCulture));
                AddField("BoundsMinZ", bounds[2].ToString(System.Globalization.CultureInfo.InvariantCulture));
                AddField("BoundsMaxX", bounds[3].ToString(System.Globalization.CultureInfo.InvariantCulture));
                AddField("BoundsMaxY", bounds[4].ToString(System.Globalization.CultureInfo.InvariantCulture));
                AddField("BoundsMaxZ", bounds[5].ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            try
            {
                using var resp = await _http!.PostAsync($"/api/projects/{projectId}/models", content).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    // 409 Conflict with body {"error":"duplicate_content","id":"<existing>"}
                    // means the server SHA-256-dedup'd this upload — same file is already
                    // published for this project. Treat as soft success and surface the
                    // existing model id so the user can re-use it.
                    if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        try
                        {
                            var conflict = JObject.Parse(body);
                            if (string.Equals(conflict["error"]?.Value<string>(), "duplicate_content", StringComparison.Ordinal)
                                && Guid.TryParse(conflict["id"]?.Value<string>(), out var existingId))
                            {
                                return (true, existingId, null, true);
                            }
                        }
                        catch { /* fall through to generic error */ }
                    }
                    return (false, Guid.Empty, $"HTTP {(int)resp.StatusCode}: {body}", false);
                }
                var json = JObject.Parse(body);
                var id = json["id"]?.Value<string>() ?? "";
                // The server now answers 200 (not 409) for hash-deduped uploads
                // and includes { duplicate: true, sidecarsRefreshed: bool } so
                // re-published models can refresh their element-map sidecar
                // even when the GLB itself is byte-identical. Old behaviour
                // (409 with duplicate_content) is still handled above for
                // back-compat with older server builds.
                bool isDuplicate = json["duplicate"]?.Value<bool>() == true;
                return (true, Guid.TryParse(id, out var g) ? g : Guid.Empty, null, isDuplicate);
            }
            finally
            {
                modelStream.Dispose();
                mapContent?.Dispose();
                mapStream?.Dispose();
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StingLog.Error("Planscape: UploadModelAsync failed", ex);
            return (false, Guid.Empty, ex.Message, false);
        }
    }

    /// <summary>Guess MIME type from the file extension for multipart uploads.</summary>
    private static string GuessMime(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".glb"  => "model/gltf-binary",
            ".gltf" => "model/gltf+json",
            ".ifc"  => "application/x-step",
            ".obj"  => "model/obj",
            ".fbx"  => "application/octet-stream",
            _ => "application/octet-stream",
        };
    }
}

// ────────────────────────────────────────────────────────────────────────────────
//  Payload / result DTOs
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>Full sync payload sent by PlatformSyncCommand to POST /api/tagsync/fullsync.</summary>
public sealed class FullSyncPayload
{
    public Guid   ProjectId     { get; set; }
    public string ProjectName   { get; set; } = "";
    public string ProjectCode   { get; set; } = "";
    public string RevitVersion  { get; set; } = "";
    public string PluginVersion { get; set; } = "";
    public List<TagElementPayload>          Elements    { get; set; } = new();
    public SyncCompliancePayload?           Compliance  { get; set; }
    public SyncWarningPayload?              Warnings    { get; set; }
    public Dictionary<string, int>          SeqCounters { get; set; } = new();
}

/// <summary>Compliance snapshot embedded in FullSyncPayload.</summary>
public sealed class SyncCompliancePayload
{
    [JsonProperty("totalElements")]    public int    TotalElements    { get; set; }
    [JsonProperty("taggedComplete")]   public int    TaggedComplete   { get; set; }
    [JsonProperty("taggedIncomplete")] public int    TaggedIncomplete { get; set; }
    [JsonProperty("untagged")]         public int    Untagged         { get; set; }
    [JsonProperty("fullyResolved")]    public int    FullyResolved    { get; set; }
    [JsonProperty("staleCount")]       public int    StaleCount       { get; set; }
    [JsonProperty("placeholderCount")] public int    PlaceholderCount { get; set; }
    [JsonProperty("tagPercent")]       public double TagPercent       { get; set; }
    [JsonProperty("strictPercent")]    public double StrictPercent    { get; set; }
    [JsonProperty("containerPercent")] public double ContainerPercent { get; set; }
    [JsonProperty("ragStatus")]        public string RagStatus        { get; set; } = "RED";
    [JsonProperty("byDiscipline")]     public Dictionary<string, int> ByDiscipline     { get; set; } = new();
    [JsonProperty("emptyTokenCounts")] public Dictionary<string, int> EmptyTokenCounts { get; set; } = new();
}

/// <summary>Warning summary embedded in FullSyncPayload.</summary>
public sealed class SyncWarningPayload
{
    [JsonProperty("total")]       public int Total       { get; set; }
    [JsonProperty("critical")]    public int Critical    { get; set; }
    [JsonProperty("high")]        public int High        { get; set; }
    [JsonProperty("autoFixable")] public int AutoFixable { get; set; }
    [JsonProperty("healthScore")] public int HealthScore { get; set; }
}

/// <summary>Result of a FullSyncAsync call.</summary>
public sealed class FullSyncResult
{
    public bool   Success           { get; set; }
    public Guid   ProjectId         { get; set; }
    public bool   ProjectCreated    { get; set; }
    public int    Received          { get; set; }
    public int    Created           { get; set; }
    public int    Updated           { get; set; }
    public int    SeqCountersSaved  { get; set; }
    public double CompliancePercent { get; set; }
    public string RagStatus         { get; set; } = "";
    public string? Error            { get; set; }
}

/// <summary>Single tagged element payload. Field names match TagElementDto on the server.</summary>
public sealed class TagElementPayload
{
    [JsonProperty("revitElementId")] public long   RevitElementId  { get; set; }
    [JsonProperty("uniqueId")]       public string UniqueId        { get; set; } = "";
    [JsonProperty("disc")]           public string Disc            { get; set; } = "";
    [JsonProperty("loc")]            public string Loc             { get; set; } = "";
    [JsonProperty("zone")]           public string Zone            { get; set; } = "";
    [JsonProperty("lvl")]            public string Lvl             { get; set; } = "";
    [JsonProperty("sys")]            public string Sys             { get; set; } = "";
    [JsonProperty("func")]           public string Func            { get; set; } = "";
    [JsonProperty("prod")]           public string Prod            { get; set; } = "";
    [JsonProperty("seq")]            public string Seq             { get; set; } = "";
    [JsonProperty("tag1")]           public string Tag1            { get; set; } = "";
    [JsonProperty("tag7")]           public string? Tag7           { get; set; }
    [JsonProperty("categoryName")]   public string CategoryName    { get; set; } = "";
    [JsonProperty("familyName")]     public string FamilyName      { get; set; } = "";
    [JsonProperty("status")]         public string? Status         { get; set; }
    [JsonProperty("rev")]            public string? Rev            { get; set; }
    [JsonProperty("isComplete")]     public bool   IsComplete      { get; set; }
    [JsonProperty("isFullyResolved")]public bool   IsFullyResolved { get; set; }
    /// <summary>
    /// Wall-clock UTC timestamp of the element's most recent STING token
    /// modification. Sourced from <c>ASS_TAG_MODIFIED_DT</c> when populated
    /// by the tagging pipeline, with <c>DateTime.UtcNow</c> as a fallback.
    /// Sent to the server so <c>/api/tagsync/sync</c> can perform
    /// last-write-wins conflict detection instead of accepting every sync
    /// as a full refresh (INT-03).
    /// </summary>
    [JsonProperty("lastModifiedUtc")] public DateTime? LastModifiedUtc { get; set; }

    // ─── Phase 165 — T4-T10 tier payload (tagging workflow repair) ───
    // TAG7A-TAG7F mirror the per-section parameters written by WriteTag7All;
    // T4-T10 are formatted summaries built by BuildTier4To10Summaries; paraDepth
    // is the highest enabled PARA_STATE_N (cumulative); patternMode is the
    // active T4-T10 payload selector (HANDOVER / DC / CUSTOM).
    [JsonProperty("tag7a")]            public string? Tag7A            { get; set; }
    [JsonProperty("tag7b")]            public string? Tag7B            { get; set; }
    [JsonProperty("tag7c")]            public string? Tag7C            { get; set; }
    [JsonProperty("tag7d")]            public string? Tag7D            { get; set; }
    [JsonProperty("tag7e")]            public string? Tag7E            { get; set; }
    [JsonProperty("tag7f")]            public string? Tag7F            { get; set; }
    [JsonProperty("t4Commissioning")]  public string? T4Commissioning  { get; set; }
    [JsonProperty("t5Cost")]           public string? T5Cost           { get; set; }
    [JsonProperty("t6Carbon")]         public string? T6Carbon         { get; set; }
    [JsonProperty("t7Fabrication")]    public string? T7Fabrication    { get; set; }
    [JsonProperty("t8ClashTriage")]    public string? T8ClashTriage    { get; set; }
    [JsonProperty("t9AsBuilt")]        public string? T9AsBuilt        { get; set; }
    [JsonProperty("t10Compliance")]    public string? T10Compliance    { get; set; }
    [JsonProperty("paraDepth")]        public int     ParaDepth        { get; set; }
    [JsonProperty("patternMode")]      public string? PatternMode      { get; set; }
}

/// <summary>Legacy sync result (POST /api/tagsync/sync).</summary>
public sealed class SyncResult
{
    public bool   Success           { get; set; }
    public int    Received          { get; set; }
    public int    Created           { get; set; }
    public int    Updated           { get; set; }
    public double CompliancePercent { get; set; }
    public string RagStatus         { get; set; } = "";
    public string? Error            { get; set; }
}
