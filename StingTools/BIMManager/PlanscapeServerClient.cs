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

            var resp = await PostJsonAsync("/api/auth/login", new { email, password });
            if (!resp.ok) { LastError = $"Login failed: {resp.body}"; return false; }

            ParseAuthResponse(JObject.Parse(resp.body), email);
            LastError = null;
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
        catch (Exception ex) { LastError = ex.Message; StingLog.Error("Planscape: Login failed", ex); return false; }
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
            var resp = await PostJsonAsync("/api/auth/refresh", new { refreshToken = _refreshToken });
            if (!resp.ok) { StingLog.Warn($"Planscape: Token refresh failed: {resp.body}"); return false; }

            var json = JObject.Parse(resp.body);
            _accessToken  = json["accessToken"]?.Value<string>()  ?? "";
            _refreshToken = json["refreshToken"]?.Value<string>() ?? _refreshToken;
            _tokenExpiry  = json["expiresAt"]?.Value<DateTime>()  ?? DateTime.UtcNow.AddHours(8);
            _http!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            StingLog.Info("Planscape: Token refreshed.");
            return true;
        }
        catch (Exception ex) { StingLog.Warn($"Planscape: Token refresh error: {ex.Message}"); return false; }
    }

    /// <summary>Ensure the token is valid, refreshing if needed. Returns false if not authenticated.</summary>
    private async Task<bool> EnsureAuthenticatedAsync()
    {
        if (string.IsNullOrEmpty(_accessToken)) { LastError = "Not connected to Planscape server."; return false; }
        // Refresh if token expires within 10 minutes
        if (_tokenExpiry <= DateTime.UtcNow.AddMinutes(10))
            return await RefreshTokenAsync();
        return true;
    }

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
        List<long>? linkedElementIds = null)
    {
        if (!await EnsureAuthenticatedAsync()) return null;
        try
        {
            var resp = await PostJsonAsync($"/api/projects/{projectId}/issues", new
            {
                type, title, priority, assignee, discipline,
                linkedElementIds = linkedElementIds ?? new List<long>()
            });
            if (!resp.ok) { LastError = $"Create issue failed: {resp.body}"; return null; }
            var json = JObject.Parse(resp.body);
            return json["issueCode"]?.Value<string>();
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
            return (json["serverUrl"]?.Value<string>()  ?? "",
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

    private void EnsureHttpClient(string baseUrl)
    {
        lock (_httpSem)
        {
            if (_http != null && _http.BaseAddress?.ToString().TrimEnd('/') == baseUrl) return;
            _http?.Dispose();
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout     = TimeSpan.FromSeconds(60)
            };
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrEmpty(_accessToken))
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _accessToken);
        }
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

        var resp = await _http!.PostAsync(path, content).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300, (int)resp.StatusCode, body);
    }

    private async Task<(bool ok, int status, string body)> GetAsync(string path)
    {
        var resp = await _http!.GetAsync(path).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300, (int)resp.StatusCode, body);
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
    public async Task<(bool ok, Guid modelId, string? error)> UploadModelAsync(
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
        if (!await EnsureAuthenticatedAsync()) return (false, Guid.Empty, LastError);
        if (!File.Exists(modelFilePath))       return (false, Guid.Empty, $"Model file not found: {modelFilePath}");

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
                    return (false, Guid.Empty, $"HTTP {(int)resp.StatusCode}: {body}");
                }
                var json = JObject.Parse(body);
                var id = json["id"]?.Value<string>() ?? "";
                return (true, Guid.TryParse(id, out var g) ? g : Guid.Empty, null);
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
            return (false, Guid.Empty, ex.Message);
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
