using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Default <see cref="IConverterClient"/> — talks to the converter sidecar's
/// <c>POST /ifc-to-glb</c> endpoint over HTTP and streams the GLB back. See
/// <see cref="IConverterClient"/> for the contract; this implementation reads
/// <c>Converter:BaseUrl</c> / <c>Converter:Token</c> from config and posts the
/// JSON the sidecar expects (<c>{ sourceUrl, fileName, discipline }</c> +
/// <c>x-converter-token</c>).
///
/// Uses <see cref="HttpCompletionOption.ResponseHeadersRead"/> so the GLB body
/// is NOT buffered in memory — the caller streams it straight to storage. The
/// SHA-256 + byte-count come back as response headers so the caller never has
/// to re-read the stream to hash it.
///
/// CAVEAT: built to the sidecar contract but not yet exercised against a
/// deployed sidecar.
/// </summary>
public class ConverterClient : IConverterClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ConverterClient> _logger;

    public ConverterClient(IHttpClientFactory httpFactory, IConfiguration config, ILogger<ConverterClient> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string BaseUrl => (_config["Converter:BaseUrl"] ?? "").TrimEnd('/');
    private string Token   => _config["Converter:Token"] ?? "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

    public async Task<ConverterGlbResult> ConvertIfcToGlbAsync(
        string sourceUrl, string fileName, string? discipline, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return ConverterGlbResult.Fail("Converter:BaseUrl not configured.");

        var body = new JObject
        {
            ["sourceUrl"] = sourceUrl,
            ["fileName"] = fileName,
            ["discipline"] = discipline ?? "",
        };

        HttpResponseMessage? resp = null;
        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(10); // IfcConvert on a large model is slow
            var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/ifc-to-glb")
            {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(Token))
                req.Headers.TryAddWithoutValidation("x-converter-token", Token);

            // ResponseHeadersRead — get headers (incl. the hash) without buffering the GLB body.
            resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                string err = await SafeReadAsync(resp, ct);
                _logger.LogWarning("ConverterClient /ifc-to-glb HTTP {Status}: {Body}", (int)resp.StatusCode, err);
                resp.Dispose();
                return ConverterGlbResult.Fail($"converter HTTP {(int)resp.StatusCode}");
            }

            string? sha = FirstHeader(resp, "X-Glb-Sha256");
            long bytes = long.TryParse(FirstHeader(resp, "X-Glb-Bytes"), out var b) ? b : 0;
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            // Hand ownership of the response (and thus the stream) to the result.
            return ConverterGlbResult.Ok(stream, sha, bytes, resp);
        }
        catch (Exception ex)
        {
            resp?.Dispose();
            _logger.LogWarning(ex, "ConverterClient /ifc-to-glb failed");
            return ConverterGlbResult.Fail(ex.Message);
        }
    }

    private static string? FirstHeader(HttpResponseMessage resp, string name)
    {
        if (resp.Headers.TryGetValues(name, out var v)) return v.FirstOrDefault();
        if (resp.Content.Headers.TryGetValues(name, out var cv)) return cv.FirstOrDefault();
        return null;
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { var s = await resp.Content.ReadAsStringAsync(ct); return s.Length > 300 ? s.Substring(0, 300) : s; }
        catch { return ""; }
    }
}
