using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using StingBIM.Shared.Models;

namespace StingBIM.PluginSync;

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

            // Sync tags
            if (payload.TagElements?.Count > 0)
            {
                var tagResult = await PostAsync<TagSyncResult>($"/api/tagsync/sync", new
                {
                    projectId = payload.ProjectId,
                    userName = payload.UserName,
                    revitVersion = payload.RevitVersion,
                    pluginVersion = payload.PluginVersion,
                    elements = payload.TagElements
                });
                if (tagResult != null)
                {
                    result.TagsCreated = tagResult.Created;
                    result.TagsUpdated = tagResult.Updated;
                    result.ServerCompliancePercent = tagResult.CompliancePercent;
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
            // TODO: implement refresh token flow
        }
        return false;
    }

    private async Task<T?> PostAsync<T>(string url, object body) where T : class
    {
        var json = JsonConvert.SerializeObject(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<T>(responseJson);
    }

    private async Task<T?> GetAsync<T>(string url) where T : class
    {
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<T>(json);
    }

    public void Dispose() => _http.Dispose();

    // Internal response models
    private record LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt);
    private record LicenseResult(bool Valid, string Tier, bool MimEnabled, string? ServerUrl, DateTime? ExpiresAt, string? Message);
    private record TagSyncResult(int Received, int Created, int Updated, double CompliancePercent, string RagStatus);
    private record SeqSyncResult(int Merged, int Total);
}
