using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// S2.6.1 — daily renewal job for Flutterwave-billed subscriptions.
///
/// Why we need this: Flutterwave's recurring billing API isn't a first-
/// class peer of Stripe's "customer.subscription.created" event stream;
/// most EA mobile-money flows are one-shot tx_ref charges. So Planscape
/// drives the cadence locally — every day this job walks Flutterwave
/// subscriptions whose CurrentPeriodEnd is within the next 24 h, mints
/// a new Invoice (Open + DueAt = period start), and emails the tenant a
/// payment link. The tenant pays via the link; the webhook (S2.3) flips
/// Status to Paid and the InvoicePdfRenderer (S2.5.1) renders the PDF.
///
/// Stripe-billed subscriptions are skipped — Stripe handles renewals
/// itself and emits its own invoice.payment_succeeded events.
///
/// Idempotency: the job checks whether an Invoice already exists for
/// the next period before minting a new one, so a misfired Hangfire
/// run can't double-bill.
/// </summary>
public class FlutterwaveRenewalJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<FlutterwaveRenewalJob> _logger;

    public FlutterwaveRenewalJob(PlanscapeDbContext db, IEmailService email, ILogger<FlutterwaveRenewalJob> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        _db.BypassTenantFilter = true;

        var now = DateTime.UtcNow;
        var horizon = now.AddDays(1);

        var dueSoon = await _db.Subscriptions
            .Where(s => s.Provider == "flutterwave"
                     && s.Status   == SubscriptionStatus.Active
                     && s.CurrentPeriodEnd <= horizon)
            .ToListAsync(ct);

        foreach (var sub in dueSoon)
        {
            var nextPeriodStart = sub.CurrentPeriodEnd;
            var nextPeriodEnd   = sub.Cycle == BillingCycle.Annual
                ? nextPeriodStart.AddYears(1) : nextPeriodStart.AddMonths(1);

            // Idempotent: if an invoice already covers nextPeriodStart,
            // skip — possibly created by an earlier run today.
            var alreadyMinted = await _db.Invoices.AnyAsync(i =>
                i.SubscriptionId == sub.Id && i.PeriodStart == nextPeriodStart, ct);
            if (alreadyMinted) continue;

            var inv = new Invoice
            {
                TenantId          = sub.TenantId,
                SubscriptionId    = sub.Id,
                Provider          = "flutterwave",
                ProviderInvoiceId = "",
                InvoiceNumber     = $"INV-{nextPeriodStart:yyyy-MM}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
                Currency          = sub.Currency,
                AmountMinorUnits  = sub.PriceMinorUnits,
                TotalMinorUnits   = sub.PriceMinorUnits,
                IssuedAt          = now,
                DueAt             = nextPeriodStart.AddDays(7),
                PeriodStart       = nextPeriodStart,
                PeriodEnd         = nextPeriodEnd,
                Status            = InvoiceStatus.Open,
            };
            _db.Invoices.Add(inv);

            sub.CurrentPeriodStart = nextPeriodStart;
            sub.CurrentPeriodEnd   = nextPeriodEnd;
            sub.UpdatedAt          = now;

            // Email the payment link. The tenant pays via the link, FW
            // fires the webhook, BillingController flips InvoiceStatus.
            var tenant = await _db.Tenants.FirstAsync(t => t.Id == sub.TenantId, ct);
            try
            {
                await _email.SendNotificationAsync(
                    tenant.ContactEmail,
                    $"Planscape — invoice {inv.InvoiceNumber} for {nextPeriodStart:MMM yyyy}",
                    $"Hi {tenant.Name},\n\nYour next Planscape invoice is ready.\n\n" +
                    $"Invoice: {inv.InvoiceNumber}\nAmount:  {sub.Currency} {sub.PriceMinorUnits}\n" +
                    $"Period:  {inv.PeriodStart:yyyy-MM-dd} → {inv.PeriodEnd:yyyy-MM-dd}\n\n" +
                    $"Pay now: https://planscape.app/billing/pay/{inv.Id}\n\n— Planscape",
                    ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Renewal email to {To} failed", tenant.ContactEmail); }
        }

        await _db.SaveChangesAsync(ct);
    }
}
