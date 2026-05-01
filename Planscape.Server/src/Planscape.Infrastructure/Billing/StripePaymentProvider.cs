using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Billing;

/// <summary>
/// S2.2 — Stripe payment provider. Talks the public REST API directly to
/// avoid the 500 KB Stripe.NET SDK and its frequent breaking changes.
///
/// Configuration:
///   Billing:Stripe:SecretKey         sk_live_... or sk_test_...
///   Billing:Stripe:WebhookSecret     whsec_... (from Stripe dashboard)
///   Billing:Stripe:Currencies        comma-list, default "USD,EUR,GBP"
/// </summary>
public class StripePaymentProvider : IPaymentProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<StripePaymentProvider> _logger;
    private readonly string _secretKey;
    private readonly string _webhookSecret;
    private readonly HashSet<string> _currencies;

    public string Name => "stripe";
    public bool Supports(string currency) => _currencies.Contains(currency.ToUpperInvariant());

    public StripePaymentProvider(IHttpClientFactory httpFactory, ILogger<StripePaymentProvider> logger, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _secretKey = config["Billing:Stripe:SecretKey"] ?? "";
        _webhookSecret = config["Billing:Stripe:WebhookSecret"] ?? "";
        var currencies = config["Billing:Stripe:Currencies"] ?? "USD,EUR,GBP";
        _currencies = currencies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(c => c.ToUpperInvariant())
                                .ToHashSet();
    }

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_secretKey))
            throw new InvalidOperationException("Billing:Stripe:SecretKey not configured.");

        var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _secretKey);

        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", "subscription"),
            new("customer_email", req.ContactEmail),
            new("success_url", req.SuccessUrl),
            new("cancel_url",  req.CancelUrl),
            new("line_items[0][price_data][currency]", req.Currency.ToLowerInvariant()),
            new("line_items[0][price_data][unit_amount]", req.PriceMinorUnits.ToString()),
            new("line_items[0][price_data][recurring][interval]", "month"),
            new("line_items[0][price_data][product_data][name]", $"Planscape {req.Plan}"),
            new("line_items[0][quantity]", "1"),
            new("metadata[tenant_id]", req.TenantId.ToString()),
            new("metadata[plan]", req.Plan),
            new("subscription_data[metadata][tenant_id]", req.TenantId.ToString()),
            new("subscription_data[metadata][plan]", req.Plan),
        };

        var resp = await http.PostAsync("https://api.stripe.com/v1/checkout/sessions",
            new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Stripe checkout create failed: {Body}", body);
            throw new HttpRequestException($"Stripe checkout failed: {(int)resp.StatusCode}");
        }
        using var doc = JsonDocument.Parse(body);
        var url = doc.RootElement.GetProperty("url").GetString() ?? "";
        var id  = doc.RootElement.GetProperty("id").GetString() ?? "";
        return new CheckoutSession(url, id);
    }

    public Task<ProviderEvent?> VerifyAndParseWebhookAsync(string requestBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        if (!headers.TryGetValue("Stripe-Signature", out var sigHeader)
         || !VerifySignature(requestBody, sigHeader, _webhookSecret))
        {
            _logger.LogWarning("Stripe webhook signature verification failed.");
            return Task.FromResult<ProviderEvent?>(null);
        }

        using var doc = JsonDocument.Parse(requestBody);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString() ?? "";
        var id   = root.GetProperty("id").GetString() ?? "";
        var data = root.GetProperty("data").GetProperty("object");

        Guid? tenantId = null;
        if (data.TryGetProperty("metadata", out var meta)
            && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("tenant_id", out var t)
            && Guid.TryParse(t.GetString(), out var parsed))
            tenantId = parsed;

        string? cust = data.TryGetProperty("customer", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        string? sub  = data.TryGetProperty("subscription", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        string? inv  = data.TryGetProperty("invoice", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
        string? txn  = data.TryGetProperty("payment_intent", out var pi) && pi.ValueKind == JsonValueKind.String ? pi.GetString() : null;
        long?   amt  = data.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt64() : (long?)null;
        string? cur  = data.TryGetProperty("currency", out var cu) && cu.ValueKind == JsonValueKind.String ? cu.GetString()?.ToUpperInvariant() : null;

        return Task.FromResult<ProviderEvent?>(new ProviderEvent(
            Provider: "stripe",
            EventId: id,
            EventType: type,
            ProviderCustomerId: cust,
            ProviderSubscriptionId: sub,
            ProviderInvoiceId: inv,
            ProviderTransactionId: txn,
            AmountMinorUnits: amt,
            Currency: cur,
            TenantId: tenantId,
            RawJson: requestBody));
    }

    /// <summary>
    /// Stripe webhook signature verification — implements the v1 scheme
    /// without the Stripe SDK. Header format:
    ///   Stripe-Signature: t=&lt;ts&gt;,v1=&lt;sig&gt;,...
    /// HMAC-SHA256 over <c>timestamp.body</c> with the webhook secret.
    /// </summary>
    private static bool VerifySignature(string body, string header, string secret)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(header)) return false;
        string? ts = null, sig = null;
        foreach (var part in header.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t")  ts  = kv[1];
            if (kv[0] == "v1") sig = kv[1];
        }
        if (ts == null || sig == null) return false;

        // Reject very old timestamps (>5 min skew) to limit replay window.
        if (long.TryParse(ts, out var unix) && Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix) > 300) return false;

        var payload = $"{ts}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(sig));
    }
}
