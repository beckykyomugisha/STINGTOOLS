using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Planscape.Shared.Models;

namespace Planscape.PluginSync;

/// <summary>
/// HTTP + SignalR client for plugin-to-server communication.
/// Designed for the Revit plugin: offline-first, async, retry-capable.
/// </summary>
/// <remarks>
/// S3.5 — DEPRECATED. Superseded by
/// <c>StingTools.BIMManager.PlanscapeServerClient</c>. Kept compiling
/// because <c>PlatformLinkCommands</c> still calls <c>SyncScheduler</c>
/// as the background-tick driver; that wiring will move to
/// <c>PlanscapeServerClient.RegisterBackgroundTick</c> in a follow-up
/// sprint, after which the whole project can be deleted.
///
/// Migration map: see <c>docs/plugin-sync-consolidation.md</c>.
/// </remarks>
[Obsolete("S3.5: use StingTools.BIMManager.PlanscapeServerClient. Will be removed in a follow-up sprint.")]
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
    /// P5 — fired after each successful batch upload so the dock-panel
    /// sync chip can render "syncing 3 of 10..." instead of a binary
    /// spinner. Setter is intentionally public so the consumer (the
    /// SyncScheduler / UI) can subscribe without inheritance.
    /// </summary>
    public Action<SyncProgress>? OnProgress { get; set; }

    /// <summary>
    /// S03: Inject a bearer token that was obtained elsewhere (e.g. by the
    /// PlanscapeServerClient in the Revit plugin). Avoids a second login
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

            // P2 — proactively force a refresh if the token is within 15
            // minutes of expiry BEFORE we start the batch loop. A large
            // payload may take 5–10 minutes; without this, batch 5/10
            // can fail with 401 and the remainder silently drop. Failing
            // fast here surfaces the auth problem to the caller.
            if (_tokenExpiresAt <= DateTime.UtcNow.AddMinutes(15)
                && !string.IsNullOrEmpty(_refreshToken))
            {
                _tokenExpiresAt = DateTime.MinValue; // force EnsureAuthenticatedAsync to refresh
                if (!await EnsureAuthenticatedAsync())
                {
                    result.ErrorMessage = "Token expired and refresh failed before batch sync started";
                    return result;
                }
            }

            // Sync tags in batches to avoid timeout on large models
            if (payload.TagElements?.Count > 0)
            {
                for (int i = 0; i < payload.TagElements.Count; i += BatchSize)
                {
                    // P2 — re-check token between batches. A 60-minute total
                    // sync can outlive even a fresh access token, so we must
                    // refresh mid-loop instead of letting batch N silently
                    // 401-fail.
                    if (!await EnsureAuthenticatedAsync())
                    {
                        result.ErrorMessage = $"Token expired mid-batch (after batch {i / BatchSize}/{(payload.TagElements.Count + BatchSize - 1) / BatchSize})";
                        return result;
                    }

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

                        // P7 — accumulate per-batch conflicts so the
                        // dock-panel sync chip can show "Sync: N conflicts"
                        // and the user can open an inspector to resolve.
                        if (tagResult.Conflicts != null && tagResult.Conflicts.Count > 0)
                        {
                            foreach (var c in tagResult.Conflicts)
                            {
                                result.Conflicts.Add(
                                    $"Element {c.RevitElementId}: server kept {c.Field}='{c.ServerValue}' over client '{c.ClientValue}'");
                            }
                        }

                        // P5 — fire progress callback so the UI can show
                        // "batch 3 of 10" instead of a binary spinner.
                        OnProgress?.Invoke(new SyncProgress
                        {
                            BatchNumber = (i / BatchSize) + 1,
                            TotalBatches = (payload.TagElements.Count + BatchSize - 1) / BatchSize,
                            ElementsProcessed = i + batch.Count,
                            TotalElements = payload.TagElements.Count
                        });
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

        // P4 — surface the last HTTP status code so the offline-queue
        // drain can decide whether to retry or skip-and-continue.
        result.StatusCode = _lastHttpStatus;
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
    // P8 — keep client batch size aligned with the server-side
    // SyncBatchSize (TagSyncController.SyncBatchSize = 500). Sending 2000
    // forces the server to internally re-chunk to 500 anyway, while
    // burning client memory serialising the wider payload and risking
    // a 30-second HTTP timeout on slow networks.
    private const int BatchSize = 500;

    // P4 — last-seen HTTP status from PostAsync; SyncAsync reads it
    // when a call fails so SyncResult.StatusCode can distinguish 4xx
    // (fatal — skip the queued payload) from 5xx (transient — break
    // and retry).
    private int _lastHttpStatus;

    private async Task<T?> PostAsync<T>(string url, object body) where T : class
    {
        var json = JsonConvert.SerializeObject(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await _http.PostAsync(url, content);
                _lastHttpStatus = (int)response.StatusCode;
                // P4 — for 4xx errors, don't retry: the payload is bad
                // and exponential-backoff just wastes the queue's time.
                if (_lastHttpStatus >= 400 && _lastHttpStatus < 500)
                {
                    response.EnsureSuccessStatusCode(); // throws — caught above
                }
                response.EnsureSuccessStatusCode();
                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(responseJson);
            }
            catch (HttpRequestException) when (attempt < MaxRetries
                                                && (_lastHttpStatus == 0 || _lastHttpStatus >= 500))
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))); // 1s, 2s, 4s
                content = new StringContent(json, Encoding.UTF8, "application/json"); // recreate disposed content
            }
            catch (HttpRequestException)
            {
                // Fatal 4xx — drop out without further retries.
                return null;
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
    // P7 — server returns server-side conflict descriptors when a sync
    // arrives with a stale timestamp; we hoist them into SyncResult so
    // the plugin UI can render "N conflicts" + a drill-down dialog.
    private record TagSyncResult(int Received, int Created, int Updated, double CompliancePercent, string RagStatus,
                                 List<ConflictRow>? Conflicts = null);
    public record ConflictRow(string RevitElementId, string Field, string ServerValue, string ClientValue);
    private record SeqSyncResult(int Merged, int Total);
}

/// <summary>
/// P5 — progress event payload. <see cref="BatchNumber"/> is 1-based;
/// <see cref="TotalBatches"/> is the total count for the current sync.
/// </summary>
public sealed class SyncProgress
{
    public int BatchNumber { get; set; }
    public int TotalBatches { get; set; }
    public int ElementsProcessed { get; set; }
    public int TotalElements { get; set; }
}
