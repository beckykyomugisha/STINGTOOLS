namespace Planscape.Core.Entities;

/// <summary>
/// S2.1 — one row per attempted/completed payment. Payments link to a single
/// invoice and carry the provider's transaction id for reconciliation.
/// Failed attempts are recorded too so the dunning job (S2.6) can count
/// retries and decide when to suspend.
/// </summary>
public class Payment : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid InvoiceId { get; set; }

    public string Provider { get; set; } = ""; // "stripe" | "flutterwave"
    public string ProviderTransactionId { get; set; } = "";
    public string? ProviderEventId { get; set; } // webhook idempotency key

    public string Currency { get; set; } = "USD";
    public long AmountMinorUnits { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? FailureCode { get; set; } // provider-specific code on failure
    public string? FailureMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Last-4 of card / mobile-money phone (privacy-safe display).</summary>
    public string? MethodSuffix { get; set; }
    public string? MethodKind { get; set; } // "card" | "mtn-momo" | "airtel-money" | "mpesa" | "bank"
}

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Refunded = 3,
}
