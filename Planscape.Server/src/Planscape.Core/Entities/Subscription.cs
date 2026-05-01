namespace Planscape.Core.Entities;

/// <summary>
/// S2.1 — one row per active billing subscription. Provider-agnostic:
/// the same shape covers Stripe (USD/EUR/GBP) and Flutterwave (UGX/KES/
/// TZS/RWF/NGN/ZAR/ZMW). The provider's own subscription / customer ids
/// land in <see cref="ProviderSubscriptionId"/> and <see cref="ProviderCustomerId"/>.
/// </summary>
public class Subscription : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>"stripe" | "flutterwave" — drives webhook routing + invoice format.</summary>
    public string Provider { get; set; } = "";
    public string ProviderCustomerId { get; set; } = "";
    public string ProviderSubscriptionId { get; set; } = "";

    /// <summary>BillingPlan as a string for forward-compat with future plans.</summary>
    public string Plan { get; set; } = nameof(BillingPlan.Trial);

    /// <summary>ISO 4217. Drives provider routing.</summary>
    public string Currency { get; set; } = "USD";
    public BillingCycle Cycle { get; set; } = BillingCycle.Monthly;

    /// <summary>Cents-style minor units (UGX has 0 minor units; USD has 2 — store as the smallest unit per ISO 4217).</summary>
    public long PriceMinorUnits { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public DateTime CurrentPeriodStart { get; set; } = DateTime.UtcNow;
    public DateTime CurrentPeriodEnd   { get; set; } = DateTime.UtcNow.AddMonths(1);
    public DateTime? CancelledAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum SubscriptionStatus
{
    Active = 0,
    PastDue = 1,
    Cancelled = 2,
    Suspended = 3,
}
