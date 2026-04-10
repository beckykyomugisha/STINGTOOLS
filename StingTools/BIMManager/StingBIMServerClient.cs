#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using StingTools.Core;

namespace StingTools.BIMManager;

/// <summary>
/// Lightweight HTTP client for Planscape Server API communication.
/// Handles authentication, token refresh, and tag element synchronisation.
/// Stores credentials persistently so re-connection survives Revit restarts.
/// </summary>
public sealed class PlanscapeServerClient : IDisposable
{
    // ── Singleton ──
    private static PlanscapeServerClient? _instance;
    private static readonly object _lock = new();

    public static PlanscapeServerClient Instance
    {
        get
        {
            if (_instance == null)
                lock (_lock) { _instance ??= new PlanscapeServerClient(); }
            return _instance;
        }
    }

    // ── HTTP ──
    private HttpClient? _http;
    private readonly object _httpLock = new();

    // ── Auth state ──
    private string _serverUrl  = "";
    private string _accessToken  = "";
    private string _refreshToken = "";
    private DateTime _tokenExpiry = DateTime.MinValue;

    // ── Public state ──
    public bool IsConnected      => !string.IsNullOrEmpty(_accessToken) && _tokenExpiry > DateTime.UtcNow.AddMinutes(5);
    public string ServerUrl      => _serverUrl;
    public string ConnectedUser  { get; private set; } = "";
    public string TierName       { get; private set; } = "";
    public bool   MimEnabled     { get; private set; }
    public string? LastError     { get; private set; }

    private PlanscapeServerClient() { }

    // ────────────────────────────────────────────────────────────────────
    //  Authentication
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticate with the Planscape server using email and password.
    /// Stores the JWT token in memory; persists the server URL and email.
    /// </summary>
    public async Task<bool> LoginAsync(string serverUrl, string email, string password)
    {
        try
        {
            _serverUrl = serverUrl.TrimEnd('/');
            EnsureHttpClient(_serverUrl);

            var payload = new { email, password };
            var content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8, "application/json");

            var resp = await _http!.PostAsync("/api/auth/login", content).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"Login failed ({(int)resp.StatusCode}): {body}";
                return false;
            }

            var json = JObject.Parse(body);
            _accessToken  = json["accessToken"]?.Value<string>()  ?? json["AccessToken"]?.Value<string>()  ?? "";
            _refreshToken = json["refreshToken"]?.Value<string>() ?? json["RefreshToken"]?.Value<string>() ?? "";
            _tokenExpiry  = json["expiresAt"]?.Value<DateTime>()  ?? json["ExpiresAt"]?.Value<DateTime>()  ?? DateTime.UtcNow.AddHours(8);
            ConnectedUser = json["userName"]?.Value<string>()     ?? json["UserName"]?.Value<string>()     ?? email;
            TierName      = json["tier"]?.Value<string>()         ?? json["Tier"]?.Value<string>()         ?? "Professional";
            MimEnabled    = json["mimEnabled"]?.Value<bool>()     ?? json["MimEnabled"]?.Value<bool>()     ?? false;

            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);

            LastError = null;
            StingLog.Info($"Planscape: Authenticated as {ConnectedUser} on {_serverUrl} (tier: {TierName})");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StingLog.Error("Planscape: Login failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Disconnect and clear credentials.
    /// </summary>
    public void Disconnect()
    {
        _accessToken  = "";
        _refreshToken = "";
        _tokenExpiry  = DateTime.MinValue;
        ConnectedUser = "";
        _http?.DefaultRequestHeaders.Authorization = null;
        StingLog.Info("Planscape: Disconnected.");
    }

    // ────────────────────────────────────────────────────────────────────
    //  Tag Synchronisation
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sync a list of tagged element DTOs to the Planscape server.
    /// Returns (created, updated, compliancePercent, ragStatus) on success.
    /// </summary>
    public async Task<SyncResult> SyncElementsAsync(
        Guid projectId,
        string revitVersion,
        string pluginVersion,
        List<TagElementPayload> elements)
    {
        if (!IsConnected)
            return new SyncResult { Success = false, Error = "Not connected to Planscape server." };

        try
        {
            var request = new
            {
                projectId,
                revitVersion,
                pluginVersion,
                userName = ConnectedUser,
                elements
            };

            var content = new StringContent(
                JsonConvert.SerializeObject(request),
                Encoding.UTF8, "application/json");

            var resp = await _http!.PostAsync("/api/tagsync/sync", content).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"Sync failed ({(int)resp.StatusCode}): {body}";
                return new SyncResult { Success = false, Error = LastError };
            }

            var json = JObject.Parse(body);
            return new SyncResult
            {
                Success          = true,
                Received         = json["received"]?.Value<int>()          ?? elements.Count,
                Created          = json["created"]?.Value<int>()           ?? 0,
                Updated          = json["updated"]?.Value<int>()           ?? 0,
                CompliancePercent= json["compliancePercent"]?.Value<double>() ?? 0.0,
                RagStatus        = json["ragStatus"]?.Value<string>()      ?? "AMBER"
            };
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StingLog.Error("Planscape: Sync failed", ex);
            return new SyncResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Get compliance summary for a project from the server.
    /// Returns null on failure; check LastError.
    /// </summary>
    public async Task<JObject?> GetComplianceAsync(Guid projectId)
    {
        if (!IsConnected) { LastError = "Not connected."; return null; }
        try
        {
            var resp = await _http!.GetAsync($"/api/tagsync/compliance/{projectId}").ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) { LastError = $"HTTP {(int)resp.StatusCode}"; return null; }
            return JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>
    /// List all projects accessible to the current user.
    /// </summary>
    public async Task<JArray?> GetProjectsAsync()
    {
        if (!IsConnected) { LastError = "Not connected."; return null; }
        try
        {
            var resp = await _http!.GetAsync("/api/projects").ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) { LastError = $"HTTP {(int)resp.StatusCode}"; return null; }
            return JArray.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Persist / Load connection settings
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Save connection settings (server URL + email) to disk.
    /// Never persists the password or token.
    /// </summary>
    public void SaveConnectionSettings(string configPath, string email)
    {
        try
        {
            var settings = new JObject
            {
                ["serverUrl"] = _serverUrl,
                ["email"]     = email,
                ["lastConnected"] = DateTime.UtcNow.ToString("o")
            };
            File.WriteAllText(configPath, settings.ToString(Formatting.Indented));
        }
        catch (Exception ex) { StingLog.Warn($"Planscape: Could not save connection settings: {ex.Message}"); }
    }

    /// <summary>
    /// Load saved connection settings (server URL + email only; no password stored).
    /// </summary>
    public static (string serverUrl, string email) LoadConnectionSettings(string configPath)
    {
        try
        {
            if (!File.Exists(configPath)) return ("", "");
            var json = JObject.Parse(File.ReadAllText(configPath));
            return (json["serverUrl"]?.Value<string>() ?? "",
                    json["email"]?.Value<string>()     ?? "");
        }
        catch { return ("", ""); }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────────────────────

    private void EnsureHttpClient(string baseUrl)
    {
        lock (_httpLock)
        {
            if (_http != null && _http.BaseAddress?.ToString().TrimEnd('/') == baseUrl)
                return;
            _http?.Dispose();
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    public void Dispose()
    {
        _http?.Dispose();
        _http = null;
    }
}

// ────────────────────────────────────────────────────────────────────────────
//  DTOs
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Payload for a single tagged element sent to POST /api/tagsync/sync.
/// Field names match the TagElementDto record in Planscape.Core.
/// </summary>
public sealed class TagElementPayload
{
    [JsonProperty("revitElementId")] public long   RevitElementId { get; set; }
    [JsonProperty("uniqueId")]       public string UniqueId       { get; set; } = "";
    [JsonProperty("disc")]           public string Disc           { get; set; } = "";
    [JsonProperty("loc")]            public string Loc            { get; set; } = "";
    [JsonProperty("zone")]           public string Zone           { get; set; } = "";
    [JsonProperty("lvl")]            public string Lvl            { get; set; } = "";
    [JsonProperty("sys")]            public string Sys            { get; set; } = "";
    [JsonProperty("func")]           public string Func           { get; set; } = "";
    [JsonProperty("prod")]           public string Prod           { get; set; } = "";
    [JsonProperty("seq")]            public string Seq            { get; set; } = "";
    [JsonProperty("tag1")]           public string Tag1           { get; set; } = "";
    [JsonProperty("tag7")]           public string? Tag7          { get; set; }
    [JsonProperty("categoryName")]   public string CategoryName   { get; set; } = "";
    [JsonProperty("familyName")]     public string FamilyName     { get; set; } = "";
    [JsonProperty("status")]         public string? Status        { get; set; }
    [JsonProperty("rev")]            public string? Rev           { get; set; }
    [JsonProperty("isComplete")]     public bool   IsComplete     { get; set; }
    [JsonProperty("isFullyResolved")]public bool   IsFullyResolved{ get; set; }
}

/// <summary>
/// Result of a Planscape server sync operation.
/// </summary>
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
