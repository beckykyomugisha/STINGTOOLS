using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Planscape.Companion;

/// <summary>Raised when the server rejected our credentials — an Error, not an Offline.</summary>
internal sealed class CompanionAuthException : Exception
{
    public CompanionAuthException(string message) : base(message) { }
}

/// <summary>One document as the delta feed describes it.</summary>
internal sealed class RemoteDocument
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string? DocumentType { get; set; }
    public string CdeStatus { get; set; } = "";
    public string? SuitabilityCode { get; set; }
    public string? Revision { get; set; }
    public string? Discipline { get; set; }
    public long FileSizeBytes { get; set; }
    public string? ContentHash { get; set; }
    public string? ScanStatus { get; set; }
    public DateTime ChangedAt { get; set; }
}

internal sealed class ChangedSincePage
{
    public List<RemoteDocument> Items { get; set; } = new();
    public bool HasMore { get; set; }
    public DateTime ServerTimeUtc { get; set; }
}

/// <summary>
/// The Companion's REST client. Small on purpose — it needs three calls: exchange
/// a token, read a delta, download a file.
/// </summary>
internal sealed class PlanscapeApiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly string _baseUrl;
    private readonly string _personalAccessToken;
    private string? _jwt;
    private DateTime _jwtObtainedUtc;

    /// <summary>
    /// Re-exchange the PAT well before the JWT's own expiry rather than waiting
    /// for a 401. A background process that only discovers expiry by failing
    /// turns every token rollover into a visible sync failure.
    /// </summary>
    private static readonly TimeSpan JwtRefreshAfter = TimeSpan.FromMinutes(30);

    public PlanscapeApiClient(string baseUrl, string personalAccessToken)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _personalAccessToken = personalAccessToken;
    }

    /// <summary>The JWT, exchanged from the PAT and cached. Thread-confined to the sync loop.</summary>
    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_jwt != null && DateTime.UtcNow - _jwtObtainedUtc < JwtRefreshAfter) return _jwt;

        var body = new StringContent(
            JsonConvert.SerializeObject(new { token = _personalAccessToken }),
            Encoding.UTF8, "application/json");

        using var res = await _http.PostAsync($"{_baseUrl}/api/auth/token/exchange", body, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new CompanionAuthException(
                "Planscape rejected the access token. Create a new one in the web app under Settings → Access tokens.");
        res.EnsureSuccessStatusCode();

        var json = JObject.Parse(await res.Content.ReadAsStringAsync(ct));
        var jwt = json["accessToken"]?.Value<string>()
            ?? throw new CompanionAuthException("token exchange returned no accessToken");

        _jwt = jwt;
        _jwtObtainedUtc = DateTime.UtcNow;
        CompanionLog.Info("access token exchanged");
        return jwt;
    }

    /// <summary>A JWT for the SignalR client, which does its own connecting.</summary>
    public Task<string> GetAccessTokenAsync(CancellationToken ct = default) => TokenAsync(ct);

    private async Task<HttpRequestMessage> AuthorisedAsync(HttpMethod method, string url, CancellationToken ct)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        return req;
    }

    /// <summary>
    /// One page of the delta feed. <paramref name="since"/> null = everything
    /// currently visible (the initial-link case).
    /// </summary>
    public async Task<ChangedSincePage> ChangedSinceAsync(
        string projectId, DateTime? since, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/api/projects/{projectId}/documents/changed-since";
        if (since.HasValue)
            url += "?since=" + Uri.EscapeDataString(
                DateTime.SpecifyKind(since.Value, DateTimeKind.Utc).ToString("O"));

        using var req = await AuthorisedAsync(HttpMethod.Get, url, ct);
        using var res = await _http.SendAsync(req, ct);

        if (res.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The cached JWT went stale sooner than expected (a server restart
            // rotating its signing key does this). Drop it and let the next pass
            // re-exchange rather than reporting a permanent auth failure.
            _jwt = null;
            throw new CompanionAuthException("session expired; will re-authenticate");
        }
        if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            throw new InvalidOperationException(
                $"project {projectId} is not visible to this account — unlink it or check the token's user.");
        res.EnsureSuccessStatusCode();

        var json = JObject.Parse(await res.Content.ReadAsStringAsync(ct));
        return new ChangedSincePage
        {
            Items = json["items"]?.ToObject<List<RemoteDocument>>() ?? new List<RemoteDocument>(),
            HasMore = json["hasMore"]?.Value<bool>() ?? false,
            ServerTimeUtc = json["serverTimeUtc"]?.Value<DateTime>() ?? DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Stream a document's bytes to <paramref name="destination"/>.
    ///
    /// Streamed, not buffered: a 100 MB drawing set held in memory on a laptop
    /// that is also running Revit is a real cost for no benefit.
    /// </summary>
    public async Task DownloadAsync(string projectId, Guid documentId, string destination, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/api/projects/{projectId}/documents/{documentId}/download";
        using var req = await AuthorisedAsync(HttpMethod.Get, url, ct);
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (res.StatusCode == HttpStatusCode.Locked)
            throw new InvalidOperationException("document is still awaiting an antivirus scan");
        res.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var src = await res.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(destination))
            await src.CopyToAsync(dst, ct);
    }

    public void Dispose() => _http.Dispose();
}
