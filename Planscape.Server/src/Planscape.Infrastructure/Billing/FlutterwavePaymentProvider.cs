using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Billing;

/// <summary>
/// S2.3 — Flutterwave payment provider. Routes UGX / KES / TZS / RWF /
/// NGN / ZAR / ZMW (and others) through the Flutterwave Standard hosted
/// checkout. Uses HttpClient + the public REST API directly — there's no
/// official Flutterwave .NET SDK and the API is small enough that a hand
/// roll is cleaner than a third-party wrapper.
///
/// Configuration:
///   Billing:Flutterwave:SecretKey      FLWSECK-... (live or test)
///   Billing:Flutterwave:WebhookHash    secret hash from FW dashboard
///   Billing:Flutterwave:Currencies     comma list, default
///                                        "UGX,KES,TZS,RWF,NGN,ZAR,ZMW"
/// </summary>
public class FlutterwavePaymentProvider : IPaymentProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FlutterwavePaymentProvider> _logger;
    private readonly string _secretKey;
    private readonly string _webhookHash;
    private readonly HashSet<string> _currencies;

    public string Name => "flutterwave";
    public bool Supports(string currency) => _currencies.Contains(currency.ToUpperInvariant());

    public FlutterwavePaymentProvider(IHttpClientFactory httpFactory, ILogger<FlutterwavePaymentProvider> logger, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _secretKey = config["Billing:Flutterwave:SecretKey"] ?? "";
        _webhookHash = config["Billing:Flutterwave:WebhookHash"] ?? "";
        var currencies = config["Billing:Flutterwave:Currencies"] ?? "UGX,KES,TZS,RWF,NGN,ZAR,ZMW";
        _currencies = currencies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(c => c.ToUpperInvariant())
                                .ToHashSet();
    }

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_secretKey))
            throw new InvalidOperationException("Billing:Flutterwave:SecretKey not configured.");

        var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _secretKey);

        // Flutterwave Standard expects the price as a major-unit number
        // (UGX/RWF have 0 decimals so minor==major; NGN has 2).
        // For simplicity we pass minor / 100 across the board — the local-
        // currency conversion happens via Flutterwave's FX layer.
        var amount = req.PriceMinorUnits / 100m;
        if (req.Currency.Equals("UGX", StringComparison.OrdinalIgnoreCase) ||
            req.Currency.Equals("RWF", StringComparison.OrdinalIgnoreCase))
        {
            amount = req.PriceMinorUnits;
        }

        var txRef = $"PLNS-{req.TenantId:N}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        var body = new
        {
            tx_ref = txRef,
            amount = amount,
            currency = req.Currency.ToUpperInvariant(),
            redirect_url = req.SuccessUrl,
            customer = new
            {
                email = req.ContactEmail,
                name = req.TenantName,
            },
            customizations = new
            {
                title = "Planscape",
                description = $"{req.Plan} plan subscription",
            },
            meta = new
            {
                tenant_id = req.TenantId.ToString(),
                plan = req.Plan,
                cancel_url = req.CancelUrl,
            },
            payment_options = "card,mobilemoneyuganda,mobilemoneykenya,mobilemoneytanzania,mobilemoneyrwanda,mobilemoneyzambia,banktransfer,ussd",
        };

        var resp = await http.PostAsJsonAsync("https://api.flutterwave.com/v3/payments", body, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Flutterwave checkout create failed: {Body}", raw);
            throw new HttpRequestException($"Flutterwave checkout failed: {(int)resp.StatusCode}");
        }
        using var doc = JsonDocument.Parse(raw);
        var status = doc.RootElement.GetProperty("status").GetString();
        if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Flutterwave returned status '{status}': {raw}");

        var link = doc.RootElement.GetProperty("data").GetProperty("link").GetString() ?? "";
        return new CheckoutSession(link, txRef);
    }

    public Task<ProviderEvent?> VerifyAndParseWebhookAsync(string requestBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        // Flutterwave verifies via a constant secret hash header rather than
        // a per-payload HMAC. Constant-time compare to dodge timing leak.
        if (!headers.TryGetValue("verif-hash", out var sigHeader)
         || string.IsNullOrEmpty(_webhookHash)
         || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(sigHeader),
                Encoding.ASCII.GetBytes(_webhookHash)))
        {
            _logger.LogWarning("Flutterwave webhook verif-hash failed.");
            return Task.FromResult<ProviderEvent?>(null);
        }

        using var doc = JsonDocument.Parse(requestBody);
        var root = doc.RootElement;
        var eventType = root.TryGetProperty("event", out var e) ? (e.GetString() ?? "") : "";
        var data = root.TryGetProperty("data", out var d) ? d : root;

        // Stable id for idempotency: Flutterwave's "id" int is per-event.
        var eventId = data.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
            ? idEl.GetInt64().ToString() : Guid.NewGuid().ToString();

        Guid? tenantId = null;
        if (data.TryGetProperty("meta", out var meta)
            && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("tenant_id", out var tEl)
            && Guid.TryParse(tEl.GetString(), out var parsed))
            tenantId = parsed;

        string? cust = data.TryGetProperty("customer", out var cu) && cu.ValueKind == JsonValueKind.Object
            && cu.TryGetProperty("id", out var ci) ? ci.ToString() : null;
        string? txRef = data.TryGetProperty("tx_ref", out var tx) ? tx.GetString() : null;
        long?   amt   = data.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number
            ? (long)(a.GetDecimal() * 100) : (long?)null;
        string? cur   = data.TryGetProperty("currency", out var cu2) ? cu2.GetString()?.ToUpperInvariant() : null;
        string? status = data.TryGetProperty("status", out var st) ? st.GetString() : null;

        // Map Flutterwave events to the same event-type strings the Stripe
        // webhook produces, so BillingController only branches on one set.
        var normalised = (eventType, status) switch
        {
            ("charge.completed",  "successful") => "invoice.payment_succeeded",
            ("charge.completed",  _)            => "invoice.payment_failed",
            ("subscription.cancelled", _)       => "customer.subscription.deleted",
            _                                    => eventType,
        };

        return Task.FromResult<ProviderEvent?>(new ProviderEvent(
            Provider: "flutterwave",
            EventId: eventId,
            EventType: normalised,
            ProviderCustomerId: cust,
            ProviderSubscriptionId: txRef,    // FW doesn't have native subs; use tx_ref
            ProviderInvoiceId: txRef,
            ProviderTransactionId: txRef,
            AmountMinorUnits: amt,
            Currency: cur,
            TenantId: tenantId,
            RawJson: requestBody));
    }
}
