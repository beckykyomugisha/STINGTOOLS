namespace Planscape.Core.Entities;

/// <summary>
/// S2.1 — one row per billing-period invoice. Generated when a subscription
/// renews; persists provider-side ids for reconciliation. Currency stored
/// alongside amount because a tenant can switch currency at renewal.
/// </summary>
public class Invoice : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SubscriptionId { get; set; }

    public string Provider { get; set; } = "";
    public string ProviderInvoiceId { get; set; } = "";

    /// <summary>Tenant-readable code, e.g. "INV-2026-04-0001". Unique per tenant.</summary>
    public string InvoiceNumber { get; set; } = "";

    public string Currency { get; set; } = "USD";
    public long AmountMinorUnits { get; set; }
    public long TaxMinorUnits { get; set; }
    public long TotalMinorUnits { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime DueAt { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Open;

    /// <summary>Path to the rendered PDF in <see cref="IFileStorageService"/>.</summary>
    public string? PdfStoragePath { get; set; }

    /// <summary>Optional buyer-supplied PO number that surfaces on the PDF.</summary>
    public string? PurchaseOrderRef { get; set; }
}

public enum InvoiceStatus
{
    Open = 0,
    Paid = 1,
    Overdue = 2,
    Void = 3,
    Refunded = 4,
}
