using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// T3 — Azure OpenAI implementation of <see cref="INlpLlmResolver"/>.
///
/// Called only when the rule-based NLP resolver returns low confidence AND
/// <c>Nlp:EnableLlmFallback=true</c>. Text is already PII-redacted by the
/// caller (<see cref="PiiRedactor"/>). We send a strict JSON-mode prompt and
/// parse the response into <see cref="NlpCandidate"/>s.
///
/// Config:
///   Nlp:Azure:Endpoint       = https://your-resource.openai.azure.com
///   Nlp:Azure:Deployment     = gpt-4o-mini
///   Nlp:Azure:ApiKey         = ...
///   Nlp:Azure:ApiVersion     = 2024-08-01-preview
///
/// Fails closed on any error — we'd rather miss a hit than surface a fake one.
/// </summary>
public class AzureOpenAiLlmResolver : INlpLlmResolver
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<AzureOpenAiLlmResolver> _logger;

    public string ProviderName => "azure-openai";

    public AzureOpenAiLlmResolver(IHttpClientFactory http, IConfiguration config,
        ILogger<AzureOpenAiLlmResolver> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NlpCandidate>> ResolveAsync(
        Guid projectId, string redactedText, string? language,
        int maxCandidates, CancellationToken ct)
    {
        var endpoint   = _config["Nlp:Azure:Endpoint"];
        var deployment = _config["Nlp:Azure:Deployment"];
        var apiKey     = _config["Nlp:Azure:ApiKey"];
        var version    = _config["Nlp:Azure:ApiVersion"] ?? "2024-08-01-preview";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("Azure OpenAI not configured — returning empty");
            return Array.Empty<NlpCandidate>();
        }

        var url = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={version}";

        // JSON-mode prompt. The model is asked to output an array; we clamp
        // maxCandidates in the prompt AND parse-side.
        var systemMsg = $@"You are a BIM coordination assistant for the ISO 19650 platform Planscape.
Extract at most {maxCandidates} structured references from the user's text.
Respond ONLY with a JSON object of shape: {{""candidates"": [{{""kind"":""IsoTag|GridReference|Element|Room|Discipline"",""label"":""human label"",""confidence"":0.0-1.0,""target"":{{""key"":""value""}}}}]}}.
If nothing plausible is found, respond with {{""candidates"":[]}}.";

        var userMsg = $"Text (PII redacted): {redactedText}";

        var body = new
        {
            messages = new object[]
            {
                new { role = "system", content = systemMsg },
                new { role = "user",   content = userMsg },
            },
            temperature = 0.1,
            top_p = 0.9,
            max_tokens = 500,
            response_format = new { type = "json_object" },
        };

        try
        {
            using var client = _http.CreateClient("webhook"); // re-use the webhook pool
            client.Timeout = TimeSpan.FromSeconds(12);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("api-key", apiKey);

            using var resp = await client.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Azure OpenAI {Status}: {Body}", (int)resp.StatusCode, text);
                return Array.Empty<NlpCandidate>();
            }

            // Azure OpenAI response: { choices: [{ message: { content: "<json>" } }] }
            using var doc = JsonDocument.Parse(text);
            var content = doc.RootElement.GetProperty("choices")[0]
                             .GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content)) return Array.Empty<NlpCandidate>();

            using var payload = JsonDocument.Parse(content);
            if (!payload.RootElement.TryGetProperty("candidates", out var arr) ||
                arr.ValueKind != JsonValueKind.Array) return Array.Empty<NlpCandidate>();

            var results = new List<NlpCandidate>();
            foreach (var item in arr.EnumerateArray().Take(maxCandidates))
            {
                var kindStr = item.TryGetProperty("kind", out var k) ? k.GetString() ?? "Unknown" : "Unknown";
                var label   = item.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                var confidence = item.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.5;
                var target = new Dictionary<string, string?>();
                if (item.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in t.EnumerateObject())
                        target[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                }
                if (!Enum.TryParse<NlpCandidateKind>(kindStr, true, out var kind)) kind = NlpCandidateKind.Unknown;
                // Cap LLM confidence at 0.85 so a high-confidence deterministic
                // regex hit always outranks an LLM guess.
                results.Add(new NlpCandidate(kind, label, Math.Min(0.85, confidence), "Llm", target));
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure OpenAI call failed — falling back to empty");
            return Array.Empty<NlpCandidate>();
        }
    }
}
