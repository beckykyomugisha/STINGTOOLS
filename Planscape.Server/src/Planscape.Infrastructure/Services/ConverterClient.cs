using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Default <see cref="IConverterClient"/> — talks to the converter sidecar's
/// <c>POST /ifc-to-glb</c> endpoint over HTTP. See <see cref="IConverterClient"/>
/// for the contract; this implementation reads <c>Converter:BaseUrl</c> /
/// <c>Converter:Token</c> from config and posts the JSON the sidecar expects
/// (<c>{ sourceUrl, projectId, fileName, discipline }</c> + <c>x-converter-token</c>).
///
/// CAVEAT: built to the sidecar contract but not yet exercised against a
/// deployed sidecar. The sidecar publishes the GLB back through the authed
/// models endpoint using its own <c>API_BEARER</c>, so that token must belong
/// to a user with Admin/Owner/Coordinator role + project access (set at deploy).
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

    public async Task<ConverterResult> ConvertIfcToGlbAsync(
        string sourceUrl, Guid projectId, string fileName, string? discipline, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new ConverterResult(false, Error: "Converter:BaseUrl not configured.");

        var body = new JObject
        {
            ["sourceUrl"] = sourceUrl,
            ["projectId"] = projectId.ToString(),
            ["fileName"] = fileName,
            ["discipline"] = discipline ?? "",
        };

        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(10); // IfcConvert on a large model is slow
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/ifc-to-glb")
            {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(Token))
                req.Headers.TryAddWithoutValidation("x-converter-token", Token);

            var resp = await http.SendAsync(req, ct);
            string respBody = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("ConverterClient /ifc-to-glb HTTP {Status}: {Body}", (int)resp.StatusCode, Trim(respBody));
                return new ConverterResult(false, Error: $"converter HTTP {(int)resp.StatusCode}");
            }

            var j = JObject.Parse(respBody);
            Guid? modelId = Guid.TryParse((string?)j["modelId"], out var g) ? g : null;
            return new ConverterResult(true, modelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConverterClient /ifc-to-glb failed");
            return new ConverterResult(false, Error: ex.Message);
        }
    }

    private static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 300 ? s.Substring(0, 300) : s);
}
