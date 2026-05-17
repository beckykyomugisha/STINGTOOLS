namespace Planscape.Core.Interfaces;

/// <summary>
/// S2.2/S2.3 — provider-agnostic billing surface. Implementations:
///
///   • <c>StripePaymentProvider</c>     for USD / EUR / GBP
///   • <c>FlutterwavePaymentProvider</c> for UGX / KES / TZS / RWF / NGN / ZAR / ZMW
///
/// PaymentRouter picks the provider based on the tenant's chosen
/// <c>Currency</c>. Both implementations talk HTTP REST directly so we
/// don't pull in heavy SDKs (Stripe.NET is 500 KB, Flutterwave has no
/// official .NET SDK at all).
/// </summary>
public interface IPaymentProvider
{
    /// <summary>"stripe" | "flutterwave"</summary>
    string Name { get; }

    /// <summary>True if this provider can charge the given ISO 4217 currency.</summary>
    bool Supports(string currency);

    /// <summary>
    /// Create a hosted checkout session for the tenant + plan. The returned
    /// URL is what we redirect the buyer to. Provider returns webhooks on
    /// success / failure that PaymentsController reconciles.
    /// </summary>
    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest req, CancellationToken ct = default);

    /// <summary>
    /// Verify a webhook signature + parse the event. Returns null when the
    /// signature is invalid (request should be 401'd at the boundary).
    /// </summary>
    Task<ProviderEvent?> VerifyAndParseWebhookAsync(string requestBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);
}

public record CheckoutRequest(
    Guid TenantId,
    string TenantName,
    string ContactEmail,
    string Plan,
    string Currency,
    long PriceMinorUnits,
    string SuccessUrl,
    string CancelUrl);

public record CheckoutSession(string Url, string ProviderSessionId);

public record ProviderEvent(
    string Provider,
    string EventId,
    string EventType,        // "checkout.completed" | "invoice.paid" | "payment.failed" | ...
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    string? ProviderInvoiceId,
    string? ProviderTransactionId,
    long?   AmountMinorUnits,
    string? Currency,
    Guid?   TenantId,         // resolved by metadata when present
    string  RawJson);
