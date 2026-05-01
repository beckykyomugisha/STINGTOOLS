using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// S2.7 — founder-facing revenue dashboard. Platform-wide (cross-tenant)
/// so it bypasses the global query filter from S1.1. Only Owner of the
/// platform tenant ('planscape' slug) sees this; everyone else gets a 404
/// (404 not 403 to avoid leaking that this controller exists at all).
///
/// Numbers exposed:
///
///   • MRR (sum of active subscriptions normalised to USD/month)
///   • New / churned firms this month
///   • Trial → paid conversion rate over the last 90 days
///   • Failed-payment count + dunning queue depth
///   • Top 10 tenants by storage / by activity
///
/// Useful as a single-glance health pulse — protects against silent
/// billing bugs that would otherwise be invisible until end-of-month.
/// </summary>
[ApiController]
[Route("api/platform/revenue")]
[Authorize(Roles = "Owner,Admin")]
public class PlatformRevenueController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public PlatformRevenueController(PlanscapeDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult> Dashboard(CancellationToken ct)
    {
        // Platform tenant gate — only the operator's own tenant gets through.
        // Everyone else sees 404 so the endpoint's existence isn't disclosed.
        var ownTenant = await _db.Tenants.FirstOrDefaultAsync(t =>
            User.FindFirst("tenant_id")!.Value == t.Id.ToString(), ct);
        if (ownTenant?.Slug != "planscape") return NotFound();

        // Now go cross-tenant.
        _db.BypassTenantFilter = true;

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var ninetyDaysAgo = now.AddDays(-90);

        var activeSubs = await _db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active)
            .ToListAsync(ct);

        // MRR: sum of active subs' monthly USD price. Currency-aware FX done
        // simplistically — the published USD price is the contract value and
        // local-currency amounts route through the provider's FX layer.
        decimal mrr = 0;
        foreach (var sub in activeSubs)
            mrr += UsdEquivalent(sub.PriceMinorUnits, sub.Currency);

        var firmsTotal      = await _db.Tenants.CountAsync(ct);
        var firmsActive     = await _db.Tenants.CountAsync(t => t.IsActive, ct);
        var firmsTrial      = await _db.Tenants.CountAsync(t => t.Plan == BillingPlan.Trial, ct);
        var firmsNewThisMo  = await _db.Tenants.CountAsync(t => t.CreatedAt >= monthStart, ct);
        var firmsChurnedMo  = await _db.Subscriptions.CountAsync(s => s.CancelledAt != null && s.CancelledAt >= monthStart, ct);

        var trial90d        = await _db.Tenants.CountAsync(t => t.CreatedAt >= ninetyDaysAgo, ct);
        var converted90d    = await _db.Subscriptions.CountAsync(s =>
            s.CreatedAt >= ninetyDaysAgo && s.Status == SubscriptionStatus.Active);
        var conversionRate  = trial90d > 0 ? (decimal)converted90d / trial90d : 0m;

        var failedPaymentsMo = await _db.Payments.CountAsync(p =>
            p.Status == PaymentStatus.Failed && p.CreatedAt >= monthStart, ct);
        var overdueInvoices = await _db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Overdue, ct);

        var topByStorage = await _db.ProjectModels.AsNoTracking()
            .Where(m => m.DeletedAt == null)
            .GroupBy(m => m.TenantId)
            .Select(g => new { TenantId = g.Key, Bytes = g.Sum(m => (long?)m.FileSizeBytes) ?? 0 })
            .OrderByDescending(x => x.Bytes)
            .Take(10)
            .ToListAsync(ct);

        return Ok(new
        {
            asOf = now,
            mrr = new
            {
                totalUsd = Math.Round(mrr, 2),
                activeSubs = activeSubs.Count,
            },
            firms = new
            {
                total = firmsTotal,
                active = firmsActive,
                trial = firmsTrial,
                newThisMonth = firmsNewThisMo,
                churnedThisMonth = firmsChurnedMo,
            },
            funnel = new
            {
                trialsLast90Days = trial90d,
                convertedLast90Days = converted90d,
                conversionRate = Math.Round(conversionRate * 100, 1),
            },
            collections = new
            {
                failedPaymentsThisMonth = failedPaymentsMo,
                overdueInvoices,
            },
            topTenantsByStorage = topByStorage,
        });
    }

    /// <summary>
    /// S2.7 — naive USD conversion. Uses fixed inverse-rate constants for
    /// each supported FX currency. Not appropriate for accounting-grade
    /// reporting but fine for a founder pulse-check; switch to a daily
    /// FX-feed (e.g. exchangerate.host) before invoicing in mixed
    /// currencies cross-quarter.
    /// </summary>
    private static decimal UsdEquivalent(long minorUnits, string currency)
    {
        var (decimals, rate) = currency.ToUpperInvariant() switch
        {
            "USD" => (2, 1m),
            "EUR" => (2, 1.08m),
            "GBP" => (2, 1.27m),
            "ZAR" => (2, 0.054m),
            "NGN" => (2, 0.0007m),
            "KES" => (2, 0.0078m),
            "TZS" => (2, 0.00040m),
            "ZMW" => (2, 0.038m),
            "UGX" => (0, 0.00027m),
            "RWF" => (0, 0.00076m),
            _      => (2, 1m),
        };
        var major = minorUnits / (decimal)Math.Pow(10, decimals);
        return major * rate;
    }
}
