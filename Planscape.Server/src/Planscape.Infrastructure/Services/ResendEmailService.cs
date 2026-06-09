using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Resend HTTP-API email transport (POST https://api.resend.com/emails). Preferred
/// for production: better deliverability signals, bounce/complaint webhooks later,
/// no SMTP port hassles. Composition (HTML + plain-text + branding) is inherited
/// from <see cref="EmailServiceBase"/> — identical body to the SMTP provider, so
/// flipping <c>Email:Provider</c> never changes what the recipient sees.
///
/// Selected when <c>Email:Provider=resend</c>. The from-address MUST be on the
/// Resend-verified domain (see docs/EMAIL_RESEND.md) or Resend rejects the send.
///
/// Note: Resend also exposes an SMTP endpoint (smtp.resend.com, user "resend",
/// password = API key). To use that zero-code path instead, keep
/// <c>Email:Provider=smtp</c> and point Smtp__Host/Username/Password at Resend.
/// </summary>
public class ResendEmailService : EmailServiceBase
{
    private readonly IHttpClientFactory _httpFactory;

    // RESEND_API_KEY arrives as either Resend:ApiKey (mapped Resend__ApiKey) or the
    // bare RESEND_API_KEY env var.
    private string ApiKey => _config["Resend:ApiKey"] ?? _config["RESEND_API_KEY"] ?? "";

    public override bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public ResendEmailService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<ResendEmailService> logger,
        IServiceScopeFactory scopeFactory)
        : base(config, logger, scopeFactory)
    {
        _httpFactory = httpFactory;
    }

    protected override async Task SendTransportAsync(string toEmail, RenderedEmail email, CancellationToken ct)
    {
        var apiKey = ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[email] resend skipped — RESEND_API_KEY not set; to={To}", toEmail);
            throw new InvalidOperationException("Resend API key not configured (set Resend__ApiKey / RESEND_API_KEY).");
        }

        var (fromName, fromAddress) = await ResolveFromAsync(ct);
        var from = string.IsNullOrWhiteSpace(fromName) ? fromAddress : $"{fromName} <{fromAddress}>";

        var payload = new
        {
            from,
            to = new[] { toEmail },
            subject = email.Subject,
            html = email.Html,
            text = email.Text,   // plain-text alternative, same as SMTP multipart
        };
        var json = JsonSerializer.Serialize(payload);

        // Light manual retry on 429 / 5xx / transport faults (no Polly dependency).
        HttpResponseMessage? resp = null;
        string body = "";
        Exception? lastTransport = null;
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "emails");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var client = _httpFactory.CreateClient("resend");
                resp = await client.SendAsync(req, ct);
                body = await resp.Content.ReadAsStringAsync(ct);

                int code = (int)resp.StatusCode;
                if (code < 500 && code != 429) break;          // success or non-retryable client error
                if (attempt == maxAttempts) break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                lastTransport = ex;
                if (attempt == maxAttempts) break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), ct);
        }

        if (resp == null)
        {
            _logger.LogError(lastTransport, "[email] resend id=(none) to={To} -> transport-error", toEmail);
            throw new InvalidOperationException($"Resend unreachable: {lastTransport?.Message ?? "unknown transport error"}", lastTransport);
        }

        string? id = TryExtractId(body);
        _logger.LogInformation("[email] resend id={Id} to={To} -> {Status}", id ?? "(none)", toEmail, (int)resp.StatusCode);

        if (!resp.IsSuccessStatusCode)
        {
            string msg = TryExtractError(body) ?? $"HTTP {(int)resp.StatusCode}";
            throw new InvalidOperationException($"Resend send failed ({(int)resp.StatusCode}): {msg}");
        }
    }

    private static string? TryExtractId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        }
        catch { return null; }
    }

    private static string? TryExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) return m.GetString();
            if (root.TryGetProperty("error", out var e))
            {
                if (e.ValueKind == JsonValueKind.String) return e.GetString();
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var em)) return em.GetString();
            }
            if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) return n.GetString();
        }
        catch { /* non-JSON body */ }
        return body.Length > 300 ? body[..300] : body;
    }
}
