using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Planscape.Shared.Models;

namespace Planscape.PluginSync;

/// <summary>
/// HTTP + SignalR client for plugin-to-server communication.
/// Designed for the Revit plugin: offline-first, async, retry-capable.
/// </summary>
public class SyncClient : IDisposable
{
    private readonly HttpClient _http;
    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _tokenExpiresAt;
    private readonly string _baseUrl;

    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken) && _tokenExpiresAt > DateTime.UtcNow;
    public string? ServerUrl => _baseUrl;

    /// <summary>
    /// S03: Inject a bearer token that was obtained elsewhere (e.g. by the
    /// StingBIMServerClient in the Revit plugin). Avoids a second login
    /// round-trip when the plugin already has a valid session.
    /// </summary>
    public void SetAuthToken(string token, DateTime? expiresAt = null)
    {
        _accessToken = token ?? "";
        _tokenExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(8);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    public SyncClient(string serverUrl)
    {
        _baseUrl = serverUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Authenticate with the server using email/password.
    /// </summary>
    public async Task<bool> LoginAsync(string email, string password)
    {
        try
        {
            var response = await PostAsync<LoginResponse>("/api/auth/login",
                new { email, password });

            if (response != null && !string.IsNullOrEmpty(response.AccessToken))
            {
                _accessToken = response.AccessToken;
                _refreshToken = response.RefreshToken;
                _tokenExpiresAt = response.ExpiresAt;
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _accessToken);
                return true;
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        return false;
    }

    /// <summary>
    /// Validate a license key and get server configuration.
    /// </summary>
    public async Task<LicenseResult?> ActivateLicenseAsync(string licenseKey, string machineId, string revitVersion, string userName)
    {
        try
        {
            return await PostAsync<LicenseResult>("/api/auth/license/activate",
                new { licenseKey, machineId, revitVersion, userName });
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Push a full sync payload (tags + SEQ + compliance + issues + workflows).
    /// </summary>
    public async Task<SyncResult> SyncAsync(PluginSyncPayload payload)
    {
        var result = new SyncResult();

        try
        {
            if (!await EnsureAuthenticatedAsync())
            {
                result.ErrorMessage = "Not authenticated";
                return result;
            }

            // Sync tags in batches to avoid timeout on large models
            if (payload.TagElements?.Count > 0)
            {
                for (int i = 0; i < payload.TagElements.Count; i += BatchSize)
                {
                    var batch = payload.TagElements.Skip(i).Take(BatchSize).ToList();
                    var tagResult = await PostAsync<TagSyncResult>($"/api/tagsync/sync", new
                    {
                        projectId = payload.ProjectId,
                        userName = payload.UserName,
                        revitVersion = payload.RevitVersion,
                        pluginVersion = payload.PluginVersion,
                        elements = batch
                    });
                    if (tagResult != null)
                    {
                        result.TagsCreated += tagResult.Created;
                        result.TagsUpdated += tagResult.Updated;
                        result.ServerCompliancePercent = tagResult.CompliancePercent;
                    }
                }
            }

            // Sync SEQ counters
            if (payload.SeqCounters?.Count > 0)
            {
                var seqResult = await PostAsync<SeqSyncResult>($"/api/projects/{payload.ProjectId}/seq/sync",
                    new { counters = payload.SeqCounters });
                if (seqResult != null)
                    result.SeqCountersMerged = seqResult.Merged;
            }

            // Push compliance snapshot
            if (payload.Compliance != null)
            {
                await PostAsync<object>($"/api/projects/{payload.ProjectId}/compliance", payload.Compliance);
            }

            // Sync workflow runs
            if (payload.WorkflowRuns?.Count > 0)
            {
                foreach (var run in payload.WorkflowRuns)
                {
                    await PostAsync<object>($"/api/projects/{payload.ProjectId}/workflows/run", run);
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            LastError = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Get server SEQ counters for merge on plugin startup.
    /// </summary>
    public async Task<Dictionary<string, int>?> GetSeqCountersAsync(Guid projectId)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync()) return null;
            return await GetAsync<Dictionary<string, int>>($"/api/projects/{projectId}/seq");
        }
        catch { return null; }
    }

    /// <summary>
    /// Get compliance summary from server.
    /// </summary>
    public async Task<ComplianceSync?> GetComplianceAsync(Guid projectId)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync()) return null;
            return await GetAsync<ComplianceSync>($"/api/tagsync/compliance/{projectId}");
        }
        catch { return null; }
    }

    public string? LastError { get; private set; }

    private async Task<bool> EnsureAuthenticatedAsync()
    {
        if (IsAuthenticated) return true;
        if (!string.IsNullOrEmpty(_refreshToken))
        {
            try
            {
                var response = await PostAsync<RefreshResponse>("/api/auth/refresh",
                    new { refreshToken = _refreshToken });
                if (response != null && !string.IsNullOrEmpty(response.AccessToken))
                {
                    _accessToken = response.AccessToken;
                    _refreshToken = response.RefreshToken;
                    _tokenExpiresAt = response.ExpiresAt;
                    _http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", _accessToken);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LastError = $"Token refresh failed: {ex.Message}";
            }
        }
        return false;
    }

    private const int MaxRetries = 3;
    private const int BatchSize = 2000; // Max elements per sync batch

    private async Task<T?> PostAsync<T>(string url, object body) where T : class
    {
        var json = JsonConvert.SerializeObject(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await _http.PostAsync(url, content);
                response.EnsureSuccessStatusCode();
                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(responseJson);
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))); // 1s, 2s, 4s
                content = new StringContent(json, Encoding.UTF8, "application/json"); // recreate disposed content
            }
        }
        return null;
    }

    private async Task<T?> GetAsync<T>(string url) where T : class
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await _http.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
        }
        return null;
    }

    public void Dispose() => _http.Dispose();

    // Internal response models
    private record LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt);
    public record LicenseResult(bool Valid, string Tier, bool MimEnabled, string? ServerUrl, DateTime? ExpiresAt, string? Message);
    private record RefreshResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt);
    private record TagSyncResult(int Received, int Created, int Updated, double CompliancePercent, string RagStatus);
    private record SeqSyncResult(int Merged, int Total);
}
