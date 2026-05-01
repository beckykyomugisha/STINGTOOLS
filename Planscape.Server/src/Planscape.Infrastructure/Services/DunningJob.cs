using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// S2.6 — daily Hangfire job that walks every Overdue invoice and runs
/// the dunning sequence:
///
///   Day  0  → invoice marked Overdue (by webhook) — first reminder email
///   Day  3  → second reminder
///   Day  7  → third + final reminder
///   Day 10  → suspend the tenant (Subscription.Status = Suspended,
///             Tenant.IsActive = false). Reads continue (existing JWTs);
///             new logins blocked.
///
/// Idempotency: each step is gated by counting how many failed Payment
/// rows exist for the invoice within the cycle. The job is self-healing —
/// if the customer pays mid-cycle the webhook flips Status back to Paid
/// and the next iteration of this job skips the row.
/// </summary>
public class DunningJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<DunningJob> _logger;

    public DunningJob(PlanscapeDbContext db, IEmailService email, ILogger<DunningJob> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // Background job — bypass the global tenant filter.
        _db.BypassTenantFilter = true;

        var now = DateTime.UtcNow;
        var overdue = await _db.Invoices
            .Where(i => i.Status == InvoiceStatus.Overdue || i.Status == InvoiceStatus.Open && i.DueAt < now)
            .ToListAsync(ct);

        foreach (var inv in overdue)
        {
            var daysLate = (int)Math.Floor((now - inv.DueAt).TotalDays);
            if (daysLate < 0) continue;

            var tenant = await _db.Tenants.FirstAsync(t => t.Id == inv.TenantId, ct);

            // Reminder cadence at days 0, 3, 7. Bitmask on a per-invoice
            // counter would be cleaner; for v1 we count failed Payment
            // rows on this invoice as the natural counter.
            var failedAttempts = await _db.Payments
                .CountAsync(p => p.InvoiceId == inv.Id && p.Status == PaymentStatus.Failed, ct);

            if (daysLate == 0 && failedAttempts <= 1)
                await SendAsync(tenant.ContactEmail, "Payment failed — please update your billing details",
                    Body(tenant, inv, "Your most recent Planscape payment didn't go through. Please update your card or mobile-money method to keep service running.", "https://planscape.app/billing"));
            else if (daysLate == 3 && failedAttempts <= 2)
                await SendAsync(tenant.ContactEmail, "Reminder: payment still outstanding",
                    Body(tenant, inv, "Your Planscape subscription has an outstanding payment. We'll suspend access in 7 days if it isn't resolved.", "https://planscape.app/billing"));
            else if (daysLate == 7 && failedAttempts <= 3)
                await SendAsync(tenant.ContactEmail, "Final notice: Planscape access will be suspended in 3 days",
                    Body(tenant, inv, "This is the third and final reminder. Without a successful payment in the next 3 days, your team will lose access on day 10.", "https://planscape.app/billing"));
            else if (daysLate >= 10)
            {
                // Suspend.
                if (tenant.IsActive)
                {
                    tenant.IsActive = false;
                    var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == inv.SubscriptionId, ct);
                    if (sub != null) sub.Status = SubscriptionStatus.Suspended;
                    _logger.LogInformation("Suspending tenant {Slug} after {DaysLate} days overdue", tenant.Slug, daysLate);
                    await SendAsync(tenant.ContactEmail, "Planscape access suspended",
                        Body(tenant, inv, "Your Planscape access has been suspended due to an outstanding invoice. Your data is preserved — pay any time within the next 30 days to restore service.", "https://planscape.app/billing"));
                }
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task SendAsync(string to, string subject, string body)
    {
        try { await _email.SendNotificationAsync(to, subject, body); }
        catch (Exception ex) { _logger.LogWarning(ex, "Dunning email to {To} failed", to); }
    }

    private static string Body(Tenant t, Invoice inv, string message, string ctaUrl)
        => $"Hi {t.Name},\n\n{message}\n\nInvoice: {inv.InvoiceNumber} ({inv.Currency})\nDue: {inv.DueAt:yyyy-MM-dd}\n\nManage billing: {ctaUrl}\n\n— Planscape";
}
