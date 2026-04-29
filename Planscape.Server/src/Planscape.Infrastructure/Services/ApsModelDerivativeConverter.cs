using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// P7 — Autodesk Platform Services (APS, formerly Forge) Model Derivative
/// converter. Faster + higher fidelity than IfcOpenShell on Revit/IFC, paid.
///
/// Flow:
///   1. POST /authentication/v2/token       — 2-legged OAuth (client credentials)
///   2. PUT  OSS bucket + signed S3 upload  — push the source IFC/RVT
///   3. POST /modelderivative/v2/designdata/job — request glTF translation
///   4. GET  /modelderivative/v2/designdata/{urn}/manifest — poll until success
///   5. GET  derivative manifest → resolve glTF urn → download → unzip → emit GLB
///
/// Configuration (Program.cs picks the provider via ModelConverter:Provider="aps"):
///   ModelConverter:Aps:ClientId       — APS app client id
///   ModelConverter:Aps:ClientSecret   — APS app client secret
///   ModelConverter:Aps:BucketKey      — OSS bucket (created on first run)
///   ModelConverter:Aps:Region         — "US" | "EMEA" | "APAC"
///   ModelConverter:Aps:PollIntervalMs — manifest poll cadence (default 5000)
///   ModelConverter:Aps:TimeoutMs      — overall timeout (default 600000)
///
/// This is a stub: it implements the OAuth + job-submit + manifest-poll skeleton
/// but the derivative download step is left as TODO until APS credentials are
/// provisioned. Returns Success=false with a clear "credentials missing" or
/// "download not yet implemented" message so the derivative job logs an
/// informative warning rather than crashing.
/// </summary>
public class ApsModelDerivativeConverter : IModelConverter
{
    private readonly ILogger<ApsModelDerivativeConverter> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _bucketKey;
    private readonly string _region;
    private readonly int _pollIntervalMs;
    private readonly int _timeoutMs;

    public string ProviderName => "aps";

    public ApsModelDerivativeConverter(
        ILogger<ApsModelDerivativeConverter> logger,
        IHttpClientFactory httpFactory,
        IConfiguration config)
    {
        _logger = logger;
        _httpFactory = httpFactory;
        _clientId = config["ModelConverter:Aps:ClientId"] ?? "";
        _clientSecret = config["ModelConverter:Aps:ClientSecret"] ?? "";
        _bucketKey = config["ModelConverter:Aps:BucketKey"] ?? "planscape-derivatives";
        _region = config["ModelConverter:Aps:Region"] ?? "US";
        _pollIntervalMs = int.TryParse(config["ModelConverter:Aps:PollIntervalMs"], out var p) ? p : 5000;
        _timeoutMs = int.TryParse(config["ModelConverter:Aps:TimeoutMs"], out var t) ? t : 600_000;
    }

    public async Task<ConversionResult> ConvertToGlbAsync(
        string inputPath, string outputPath, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
        {
            return new ConversionResult(false, ProviderName, sw.ElapsedMilliseconds, 0, null,
                "APS client_id/client_secret not configured (ModelConverter:Aps:ClientId / ClientSecret).");
        }

        try
        {
            // IHttpClientFactory owns the underlying HttpMessageHandler lifetime
            // — disposing the issued HttpClient closes the pooled handler. Keep
            // the reference scoped to this method and let the factory clean up.
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromMilliseconds(_timeoutMs);

            var token = await AuthenticateAsync(http, ct);
            if (token == null)
                return new ConversionResult(false, ProviderName, sw.ElapsedMilliseconds, 0, null, "APS authentication failed.");

            var urn = await UploadAsync(http, token, inputPath, ct);
            if (urn == null)
                return new ConversionResult(false, ProviderName, sw.ElapsedMilliseconds, 0, null, "APS upload failed.");

            var jobOk = await SubmitTranslationJobAsync(http, token, urn, ct);
            if (!jobOk)
                return new ConversionResult(false, ProviderName, sw.ElapsedMilliseconds, 0, null, "APS translation job failed to submit.");

            var status = await PollManifestAsync(http, token, urn, ct);
            if (status != "success")
                return new ConversionResult(false, ProviderName, sw.ElapsedMilliseconds, 0, null, $"APS translation status: {status}");

            // TODO: walk derivative manifest → find glTF child → download SVF/glTF
            // bundle → unzip → flatten to a single .glb at outputPath. This step
            // requires access to a sandbox APS account to verify the response shape.
            sw.Stop();
            _logger.LogWarning("ApsModelDerivativeConverter: translation succeeded for urn={Urn} but glTF download is not yet implemented.", urn);
            return new ConversionResult(false, ProviderName, sw.ElapsedMilliseconds, 0, null,
                "APS translation succeeded; glTF download not yet implemented (see TODO).");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "APS conversion crashed");
            return new ConversionResult(false, ProviderName, sw.ElapsedMilliseconds, 0, null, ex.Message);
        }
    }

    private async Task<string?> AuthenticateAsync(HttpClient http, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://developer.api.autodesk.com/authentication/v2/token");
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}")));
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("grant_type", "client_credentials"),
            new KeyValuePair<string,string>("scope", "data:read data:write data:create bucket:read bucket:create"),
        });
        var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("access_token", out var tok) ? tok.GetString() : null;
    }

    private async Task<string?> UploadAsync(HttpClient http, string token, string inputPath, CancellationToken ct)
    {
        // Real impl: ensureBucket → /signeds3upload → S3 PUT → /signeds3upload (finalize).
        // Stub: return a deterministic URN derived from filename so the rest of the
        // pipeline can be smoke-tested with a fake bucket if necessary.
        await Task.Yield();
        var key = Path.GetFileName(inputPath);
        var fakeUrn = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"urn:adsk.objects:os.object:{_bucketKey}/{key}"))
            .TrimEnd('=');
        _logger.LogInformation("ApsModelDerivativeConverter: TODO upload bypassed; using deterministic urn for {File}", key);
        return fakeUrn;
    }

    private async Task<bool> SubmitTranslationJobAsync(HttpClient http, string token, string urn, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            "https://developer.api.autodesk.com/modelderivative/v2/designdata/job");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("x-ads-region", _region);
        req.Content = JsonContent.Create(new
        {
            input = new { urn },
            output = new
            {
                formats = new[] { new { type = "svf2", views = new[] { "3d" } } }
            }
        });
        var resp = await http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    private async Task<string> PollManifestAsync(HttpClient http, string token, string urn, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(_timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://developer.api.autodesk.com/modelderivative/v2/designdata/{urn}/manifest");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("status", out var s))
                {
                    var status = s.GetString() ?? "";
                    if (status is "success" or "failed" or "timeout") return status;
                }
            }
            await Task.Delay(_pollIntervalMs, ct);
        }
        return "timeout";
    }
}
