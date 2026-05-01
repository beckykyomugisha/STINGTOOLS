using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Billing;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// S2.2/S2.3 — checkout + webhook surface. /api/billing/checkout starts a
/// hosted checkout session at the provider matching the tenant's currency;
/// /api/billing/webhook/stripe and /api/billing/webhook/flutterwave receive
/// provider events and reconcile Subscription / Invoice / Payment rows.
///
/// Webhook endpoints are anonymous — provider signature is the auth.
/// Checkout requires Owner/Admin role.
/// </summary>
[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly PaymentRouter _router;

    public BillingController(PlanscapeDbContext db, ITenantContext tenant, PaymentRouter router)
    {
        _db = db; _tenant = tenant; _router = router;
    }

    [HttpPost("checkout")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult> Checkout([FromBody] CheckoutBody body, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct);
        if (tenant == null) return NotFound();

        var plan = Enum.TryParse<BillingPlan>(body.Plan, ignoreCase: true, out var p) ? p : BillingPlan.Network;
        var limits = BillingPlanLimits.For(plan);
        var currency = body.Currency ?? tenant.Currency;
        var provider = _router.RouteByCurrency(currency);

        // Translate USD price to minor units of the chosen currency. For v1
        // we use the published USD price as the contract value across all
        // currencies — local-currency conversion happens at the payment
        // provider via their FX rate. UGX/KES/etc. have 0 minor units.
        var priceMinor = plan == BillingPlan.Studio ? 30_00L
                       : plan == BillingPlan.Practice ? 80_00L
                       : plan == BillingPlan.Network ? 150_00L
                       : 0L;
        if (currency.Equals("UGX", StringComparison.OrdinalIgnoreCase) ||
            currency.Equals("RWF", StringComparison.OrdinalIgnoreCase))
        {
            // Zero-minor-unit currencies; multiply USD x ~3700 (UGX) or x ~1300 (RWF) at runtime.
            // For v1 we leave conversion to the provider's display layer and
            // pass the USD-equivalent integer for record-keeping.
            priceMinor = priceMinor / 100;
        }

        var session = await provider.CreateCheckoutAsync(new CheckoutRequest(
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            ContactEmail: tenant.ContactEmail,
            Plan: plan.ToString(),
            Currency: currency,
            PriceMinorUnits: priceMinor,
            SuccessUrl: body.SuccessUrl ?? "https://planscape.app/billing/success",
            CancelUrl:  body.CancelUrl  ?? "https://planscape.app/billing/cancel"
        ), ct);

        return Ok(new { checkoutUrl = session.Url, providerSessionId = session.ProviderSessionId, provider = provider.Name });
    }

    [HttpPost("webhook/{providerName}")]
    [AllowAnonymous]
    public async Task<ActionResult> Webhook(string providerName, CancellationToken ct)
    {
        // Read body as string for signature verification.
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

        var provider = _router.RouteByName(providerName);
        var ev = await provider.VerifyAndParseWebhookAsync(body, headers, ct);
        if (ev == null) return Unauthorized();

        // Bypass tenant filter — webhooks aren't authenticated as a tenant.
        _db.BypassTenantFilter = true;

        // Idempotency: if we've already recorded a Payment with this
        // (provider, eventId), short-circuit. Stripe guarantees retries on
        // 5xx so this gate is essential.
        var alreadyHandled = await _db.Payments.AnyAsync(p =>
            p.Provider == ev.Provider && p.ProviderEventId == ev.EventId, ct);
        if (alreadyHandled) return Ok(new { duplicate = true });

        // Resolve the tenant from event metadata or by following the
        // Subscription row's TenantId.
        Guid? tenantId = ev.TenantId;
        if (tenantId == null && !string.IsNullOrEmpty(ev.ProviderSubscriptionId))
        {
            tenantId = await _db.Subscriptions
                .Where(s => s.ProviderSubscriptionId == ev.ProviderSubscriptionId)
                .Select(s => (Guid?)s.TenantId)
                .FirstOrDefaultAsync(ct);
        }
        if (tenantId == null)
            return BadRequest(new { error = "tenant_not_resolved" });

        switch (ev.EventType)
        {
            case "checkout.session.completed":
            case "customer.subscription.created":
                await UpsertSubscriptionAsync(ev, tenantId.Value, ct);
                break;
            case "invoice.payment_succeeded":
            case "invoice.paid":
                await RecordSuccessfulPaymentAsync(ev, tenantId.Value, ct);
                break;
            case "invoice.payment_failed":
            case "charge.failed":
                await RecordFailedPaymentAsync(ev, tenantId.Value, ct);
                break;
            case "customer.subscription.deleted":
                await CancelSubscriptionAsync(ev, ct);
                break;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { handled = ev.EventType });
    }

    private async Task UpsertSubscriptionAsync(ProviderEvent ev, Guid tenantId, CancellationToken ct)
    {
        var existing = !string.IsNullOrEmpty(ev.ProviderSubscriptionId)
            ? await _db.Subscriptions.FirstOrDefaultAsync(s => s.ProviderSubscriptionId == ev.ProviderSubscriptionId, ct)
            : null;
        if (existing == null)
        {
            _db.Subscriptions.Add(new Subscription
            {
                TenantId = tenantId,
                Provider = ev.Provider,
                ProviderCustomerId = ev.ProviderCustomerId ?? "",
                ProviderSubscriptionId = ev.ProviderSubscriptionId ?? "",
                Currency = ev.Currency ?? "USD",
                PriceMinorUnits = ev.AmountMinorUnits ?? 0,
                Status = SubscriptionStatus.Active,
                CurrentPeriodStart = DateTime.UtcNow,
                CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            });
            // Promote the tenant's plan from Trial → resolved plan.
            var tenant = await _db.Tenants.FirstAsync(t => t.Id == tenantId, ct);
            tenant.IsActive = true;
            if (tenant.Plan == BillingPlan.Trial) tenant.Plan = BillingPlan.Network;
        }
        else
        {
            existing.Status = SubscriptionStatus.Active;
            existing.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task RecordSuccessfulPaymentAsync(ProviderEvent ev, Guid tenantId, CancellationToken ct)
    {
        var invoice = await EnsureInvoiceAsync(ev, tenantId, ct);
        invoice.Status = InvoiceStatus.Paid;
        _db.Payments.Add(new Payment
        {
            TenantId = tenantId,
            InvoiceId = invoice.Id,
            Provider = ev.Provider,
            ProviderTransactionId = ev.ProviderTransactionId ?? "",
            ProviderEventId = ev.EventId,
            Currency = ev.Currency ?? invoice.Currency,
            AmountMinorUnits = ev.AmountMinorUnits ?? invoice.TotalMinorUnits,
            Status = PaymentStatus.Succeeded,
            CompletedAt = DateTime.UtcNow,
        });
    }

    private async Task RecordFailedPaymentAsync(ProviderEvent ev, Guid tenantId, CancellationToken ct)
    {
        var invoice = await EnsureInvoiceAsync(ev, tenantId, ct);
        invoice.Status = InvoiceStatus.Overdue;
        _db.Payments.Add(new Payment
        {
            TenantId = tenantId,
            InvoiceId = invoice.Id,
            Provider = ev.Provider,
            ProviderTransactionId = ev.ProviderTransactionId ?? "",
            ProviderEventId = ev.EventId,
            Currency = ev.Currency ?? invoice.Currency,
            AmountMinorUnits = ev.AmountMinorUnits ?? invoice.TotalMinorUnits,
            Status = PaymentStatus.Failed,
        });
    }

    private async Task CancelSubscriptionAsync(ProviderEvent ev, CancellationToken ct)
    {
        var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.ProviderSubscriptionId == ev.ProviderSubscriptionId, ct);
        if (sub == null) return;
        sub.Status = SubscriptionStatus.Cancelled;
        sub.CancelledAt = DateTime.UtcNow;
    }

    private async Task<Invoice> EnsureInvoiceAsync(ProviderEvent ev, Guid tenantId, CancellationToken ct)
    {
        var existing = !string.IsNullOrEmpty(ev.ProviderInvoiceId)
            ? await _db.Invoices.FirstOrDefaultAsync(i => i.ProviderInvoiceId == ev.ProviderInvoiceId, ct)
            : null;
        if (existing != null) return existing;

        var sub = await _db.Subscriptions.FirstAsync(s => s.ProviderSubscriptionId == ev.ProviderSubscriptionId, ct);
        var inv = new Invoice
        {
            TenantId = tenantId,
            SubscriptionId = sub.Id,
            Provider = ev.Provider,
            ProviderInvoiceId = ev.ProviderInvoiceId ?? "",
            InvoiceNumber = $"INV-{DateTime.UtcNow:yyyy-MM}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            Currency = ev.Currency ?? sub.Currency,
            AmountMinorUnits = ev.AmountMinorUnits ?? sub.PriceMinorUnits,
            TotalMinorUnits  = ev.AmountMinorUnits ?? sub.PriceMinorUnits,
            IssuedAt = DateTime.UtcNow,
            DueAt = DateTime.UtcNow.AddDays(7),
            PeriodStart = sub.CurrentPeriodStart,
            PeriodEnd = sub.CurrentPeriodEnd,
            Status = InvoiceStatus.Open,
        };
        _db.Invoices.Add(inv);
        return inv;
    }
}

public record CheckoutBody(string Plan, string? Currency, string? SuccessUrl, string? CancelUrl);
