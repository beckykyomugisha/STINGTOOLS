using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// T3 — Azure AI Vision Read API v4.0 implementation of <see cref="IOcrService"/>.
///
/// Config:
///   Ocr:Azure:Endpoint = https://your-resource.cognitiveservices.azure.com
///   Ocr:Azure:ApiKey   = ...
///   Ocr:Azure:Language = en
///
/// The Read API v4 ("Image Analysis 4.0") returns results synchronously for
/// images under ~20 MB, so we don't need the v3 async poll loop.
/// </summary>
public class AzureVisionOcrService : IOcrService
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<AzureVisionOcrService> _logger;

    public string ProviderName => "azure-vision";

    public AzureVisionOcrService(IHttpClientFactory http, IConfiguration config,
        ILogger<AzureVisionOcrService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<OcrServerResult> RecognizeAsync(Stream image, string mimeType, CancellationToken ct = default)
    {
        var endpoint = _config["Ocr:Azure:Endpoint"];
        var apiKey   = _config["Ocr:Azure:ApiKey"];
        var language = _config["Ocr:Azure:Language"] ?? "en";
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            return new OcrServerResult(false, ProviderName, "", 0, "Not configured");

        var url = $"{endpoint.TrimEnd('/')}/computervision/imageanalysis:analyze" +
                  $"?api-version=2024-02-01&features=read&language={language}";

        try
        {
            using var client = _http.CreateClient("webhook");
            client.Timeout = TimeSpan.FromSeconds(20);

            using var ms = new MemoryStream();
            await image.CopyToAsync(ms, ct);
            ms.Position = 0;

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new ByteArrayContent(ms.ToArray()),
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrEmpty(mimeType) ? "application/octet-stream" : mimeType);
            req.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);

            using var resp = await client.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Azure Vision {Status}: {Body}", (int)resp.StatusCode, body);
                return new OcrServerResult(false, ProviderName, "", 0, body);
            }

            // v4 schema: readResult.blocks[].lines[].text + confidence.
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("readResult", out var rr) ||
                !rr.TryGetProperty("blocks", out var blocks))
                return new OcrServerResult(false, ProviderName, "", 0, "No readResult");

            var texts = new List<string>();
            var confSum = 0.0; var confCount = 0;
            foreach (var block in blocks.EnumerateArray())
            {
                if (!block.TryGetProperty("lines", out var lines)) continue;
                foreach (var line in lines.EnumerateArray())
                {
                    if (line.TryGetProperty("text", out var t)) texts.Add(t.GetString() ?? "");
                    if (line.TryGetProperty("words", out var words))
                    {
                        foreach (var w in words.EnumerateArray())
                        {
                            if (w.TryGetProperty("confidence", out var c))
                            { confSum += c.GetDouble(); confCount++; }
                        }
                    }
                }
            }
            var avgConf = confCount > 0 ? confSum / confCount : 0.0;
            return new OcrServerResult(true, ProviderName, string.Join("\n", texts), avgConf, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure Vision OCR failed");
            return new OcrServerResult(false, ProviderName, "", 0, ex.Message);
        }
    }
}

/// <summary>T3 — Default no-op OCR service. Swap for AzureVisionOcrService in Program.cs.</summary>
public class NullOcrService : IOcrService
{
    public string ProviderName => "null";
    public Task<OcrServerResult> RecognizeAsync(Stream image, string mimeType, CancellationToken ct = default)
        => Task.FromResult(new OcrServerResult(false, ProviderName, "", 0, "No server OCR configured"));
}
