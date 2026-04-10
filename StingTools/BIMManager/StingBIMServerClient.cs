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
/// HTTP client for StingBIM Server API.
/// Handles authentication, automatic token refresh, and all sync/query operations.
/// Thread-safe singleton — use StingBIMServerClient.Instance.
/// </summary>
public sealed class StingBIMServerClient : IDisposable
{
    // ── Singleton ──────────────────────────────────────────────────────────────
    private static StingBIMServerClient? _instance;
    private static readonly object _instanceLock = new();

    public static StingBIMServerClient Instance
    {
        get
        {
            if (_instance == null)
                lock (_instanceLock) { _instance ??= new StingBIMServerClient(); }
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
    public string ConnectedUser { get; private set; } = "";
    public string TierName      { get; private set; } = "";
    public bool   MimEnabled    { get; private set; }
    public string? LastError    { get; private set; }

    private StingBIMServerClient() { }

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
            StingLog.Info($"StingBIM: Authenticated as {ConnectedUser} @ {_serverUrl} (tier: {TierName})");
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; StingLog.Error("StingBIM: Login failed", ex); return false; }
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
            if (!resp.ok) { StingLog.Warn($"StingBIM: Token refresh failed: {resp.body}"); return false; }

            var json = JObject.Parse(resp.body);
            _accessToken  = json["accessToken"]?.Value<string>()  ?? "";
            _refreshToken = json["refreshToken"]?.Value<string>() ?? _refreshToken;
            _tokenExpiry  = json["expiresAt"]?.Value<DateTime>()  ?? DateTime.UtcNow.AddHours(8);
            _http!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            StingLog.Info("StingBIM: Token refreshed.");
            return true;
        }
        catch (Exception ex) { StingLog.Warn($"StingBIM: Token refresh error: {ex.Message}"); return false; }
    }

    /// <summary>Ensure the token is valid, refreshing if needed. Returns false if not authenticated.</summary>
    private async Task<bool> EnsureAuthenticatedAsync()
    {
        if (string.IsNullOrEmpty(_accessToken)) { LastError = "Not connected to StingBIM server."; return false; }
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
        _http?.DefaultRequestHeaders.Authorization = null;
        StingLog.Info("StingBIM: Disconnected.");
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
        catch (Exception ex) { LastError = ex.Message; StingLog.Error("StingBIM: FullSync failed", ex); return new FullSyncResult { Success = false, Error = ex.Message }; }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Legacy tag sync (kept for backwards-compat with plugin v2.1 callers)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Legacy: sync elements only via POST /api/tagsync/sync.</summary>
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
        catch (Exception ex) { LastError = ex.Message; StingLog.Error("StingBIM: Sync failed", ex); return new SyncResult { Success = false, Error = ex.Message }; }
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
        catch (Exception ex) { StingLog.Warn($"StingBIM: Could not save connection settings: {ex.Message}"); }
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
    }

    public void Dispose()
    {
        _http?.Dispose();
        _http = null;
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
