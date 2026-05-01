using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planscape.Core.Entities;

namespace Planscape.API.Controllers;

/// <summary>
/// S2.4 — public pricing surface. Returns the canonical plan list for both
/// the marketing site (HTML) and the mobile app (JSON). Single source of
/// truth lives in <see cref="BillingPlanLimits"/>; this controller just
/// presents it.
/// </summary>
[ApiController]
[Route("api/pricing")]
[AllowAnonymous]
public class PricingController : ControllerBase
{
    /// <summary>JSON pricing list — used by the mobile signup flow + public web.</summary>
    [HttpGet]
    public ActionResult Plans()
    {
        return Ok(new
        {
            plans = new[]
            {
                Render(BillingPlan.Studio,   "Studio",   "Solo studio · 1 author + 10 coordinators · 3 projects",  highlight: false),
                Render(BillingPlan.Practice, "Practice", "Small practice · 1 author + 25 coordinators · 10 projects", highlight: false),
                Render(BillingPlan.Network,  "Network",  "Mid-size firm · 3 authors + 35 coordinators · 10 projects", highlight: true),
                Render(BillingPlan.Enterprise, "Enterprise", "≥100 seats · SSO · SLA · on-prem option", highlight: false, custom: true),
            },
            mim = new
            {
                name = "MIM Add-on",
                description = "Asset lifecycle + handover module for FM phase",
                priceUsd = 12,
                cycle = "monthly",
            },
            annualDiscount = new
            {
                description = "Pay yearly, get 2 months free",
                multiplierMonthsPerYear = 10,
            },
            currencies = new
            {
                stripe = new[] { "USD", "EUR", "GBP" },
                flutterwave = new[] { "UGX", "KES", "TZS", "RWF", "NGN", "ZAR", "ZMW" },
            },
            comparison = new[]
            {
                new { vs = "Autodesk Construction Cloud", price = "$40-80/seat/mo", planscape = "$4.50/seat at Network" },
                new { vs = "Procore",                     price = "$375-549+/mo",   planscape = "$80-150/firm" },
                new { vs = "Trimble Connect",             price = "$20-40/seat",    planscape = "$4.50/seat" },
                new { vs = "BIM Track",                   price = "$40-55/seat",    planscape = "issue-tracking + viewer + plugin" },
                new { vs = "Dalux",                       price = "$30-50/seat",    planscape = "offline-first parity" },
            },
        });
    }

    /// <summary>Marketing-friendly HTML pricing page for the public site.</summary>
    [HttpGet("html")]
    [Produces("text/html")]
    public ContentResult HtmlPage()
    {
        var s = BillingPlanLimits.For(BillingPlan.Studio);
        var p = BillingPlanLimits.For(BillingPlan.Practice);
        var n = BillingPlanLimits.For(BillingPlan.Network);

        var html = $@"<!doctype html><html lang=""en""><head>
<meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>Planscape — Pricing</title>
<style>
 :root{{--accent:#E8912D;--ink:#1c1f26;--muted:#6c7280;--bg:#fafbfc;--card:#fff;--line:#eceef3}}
 *{{box-sizing:border-box}}body{{margin:0;font:16px/1.5 -apple-system,system-ui,Segoe UI,Roboto,sans-serif;color:var(--ink);background:var(--bg)}}
 header{{padding:64px 24px 24px;text-align:center}}
 h1{{font-size:34px;margin:0 0 8px}}
 p.lead{{color:var(--muted);max-width:640px;margin:0 auto}}
 .grid{{display:grid;gap:20px;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));max-width:1100px;margin:36px auto;padding:0 24px}}
 .card{{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:24px;display:flex;flex-direction:column}}
 .card.featured{{border:2px solid var(--accent);box-shadow:0 12px 36px -12px rgba(232,145,45,.35)}}
 .name{{font-weight:700;font-size:18px}}
 .price{{font-size:36px;font-weight:700;margin:12px 0 4px}}
 .cycle{{color:var(--muted);font-size:13px}}
 ul.feat{{padding:16px 0 0 18px;margin:0 0 24px;color:#34394a}}
 ul.feat li{{margin:6px 0}}
 a.cta{{display:inline-block;text-align:center;padding:12px;border-radius:10px;border:1px solid var(--ink);text-decoration:none;color:var(--ink);font-weight:600;margin-top:auto}}
 a.cta.primary{{background:var(--accent);border-color:var(--accent);color:#fff}}
 footer{{text-align:center;color:var(--muted);font-size:13px;padding:24px}}
 .pill{{display:inline-block;background:rgba(232,145,45,.12);color:#a85700;padding:4px 10px;border-radius:99px;font-size:11px;font-weight:600;margin-bottom:12px}}
</style></head>
<body>
<header>
  <h1>Pricing built for East African firms</h1>
  <p class=""lead"">Per-firm, per-month, USD. Pay annually and get two months free. Invoiced in your local currency via Flutterwave (UGX/KES/TZS/RWF/NGN/ZAR/ZMW) or in USD/EUR/GBP via Stripe.</p>
</header>
<section class=""grid"">
  {Card("Studio",   s, "For solo studios just getting started.",         featured: false, plan: "Studio")}
  {Card("Practice", p, "Small practice with one author and a field team.", featured: false, plan: "Practice")}
  {Card("Network",  n, "Mid-size firm. The default we recommend.",      featured: true,  plan: "Network", pill: "Most popular")}
  {EnterpriseCard()}
</section>
<footer>
  Need ISO 19650 transmittals, MIM hand-over, or on-prem? <a href=""mailto:hello@planscape.app"">Talk to us</a>.
</footer>
</body></html>";
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8" };
    }

    private static object Render(BillingPlan plan, string label, string blurb, bool highlight, bool custom = false)
    {
        var l = BillingPlanLimits.For(plan);
        return new
        {
            id = plan.ToString(),
            name = label,
            blurb,
            priceUsd = custom ? (object)"custom" : l.MonthlyUsd,
            cycle = "monthly",
            highlight,
            limits = new
            {
                authors = custom ? (object)"unlimited" : l.MaxAuthors,
                coordinators = custom ? (object)"unlimited" : l.MaxCoordinators,
                projects = custom ? (object)"unlimited" : l.MaxProjects,
                storageGb = custom ? (object)"custom" : l.StorageMb / 1024,
            },
            features = FeaturesFor(plan),
        };
    }

    private static string[] FeaturesFor(BillingPlan plan) => plan switch
    {
        BillingPlan.Studio => new[]
        {
            "Revit plugin (1 seat)", "Mobile + web viewer (10 seats)",
            "Issue tracking", "ISO 19650 tag templates", "Offline-first mobile",
        },
        BillingPlan.Practice => new[]
        {
            "Everything in Studio", "25 coordinator seats", "10 active projects",
            "25 GB model storage", "Document control + CDE", "Meeting minutes + transmittals",
        },
        BillingPlan.Network => new[]
        {
            "Everything in Practice", "3 author seats", "35 coordinator seats",
            "50 GB model storage", "4D/5D scheduling", "Federation across disciplines", "Priority support",
        },
        BillingPlan.Enterprise => new[]
        {
            "Everything in Network", "Unlimited seats + projects", "SSO (SAML/OIDC)",
            "Dedicated tenant + on-prem option", "99.9% SLA", "Audit replay + ISO 27001-light evidence pack",
        },
        _ => Array.Empty<string>(),
    };

    private static string Card(string name, BillingPlanLimits.Limits l, string blurb, bool featured, string plan, string? pill = null)
    {
        var pillHtml = pill is null ? "" : $"<div class=\"pill\">{pill}</div>";
        var ctaClass = featured ? "cta primary" : "cta";
        var feats = string.Join("", FeaturesFor(plan switch
        {
            "Studio"     => BillingPlan.Studio,
            "Practice"   => BillingPlan.Practice,
            _            => BillingPlan.Network,
        }).Select(f => $"<li>{f}</li>"));
        return $@"<article class=""card {(featured ? "featured" : "")}"">
  {pillHtml}
  <div class=""name"">{name}</div>
  <div class=""price"">${l.MonthlyUsd:0}<span class=""cycle""> / firm / mo</span></div>
  <div class=""cycle"">Pay annually = ${l.MonthlyUsd * 10:0} / year</div>
  <ul class=""feat"">{feats}</ul>
  <a class=""{ctaClass}"" href=""/signup?plan={plan}"">Start 30-day trial</a>
</article>";
    }

    private static string EnterpriseCard() => @"<article class=""card"">
  <div class=""name"">Enterprise</div>
  <div class=""price"">Custom</div>
  <div class=""cycle"">From $3,500 / mo</div>
  <ul class=""feat"">
    <li>Unlimited seats + projects</li>
    <li>SSO (SAML/OIDC)</li>
    <li>On-prem deployment option</li>
    <li>99.9% SLA</li>
    <li>ISO 27001-light evidence pack</li>
  </ul>
  <a class=""cta"" href=""mailto:hello@planscape.app"">Talk to us</a>
</article>";
}
