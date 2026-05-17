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
            pluginOnly = Render(BillingPlan.PluginOnly, "StingTools Plugin", "Revit plugin only · local workflow · no cloud needed", highlight: false),
            plans = new[]
            {
                Render(BillingPlan.Studio,    "Small",      "Plugin + cloud · up to 6 users · 5 projects",            highlight: false),
                Render(BillingPlan.Practice,  "Medium",     "Plugin + cloud · up to 12 users · 10 projects",          highlight: true),
                Render(BillingPlan.Network,   "Large",      "Plugin + cloud · up to 20 users · unlimited projects",   highlight: false),
                Render(BillingPlan.Enterprise,"Enterprise", "≥20 seats · SSO · SLA · on-prem option",                 highlight: false, custom: true),
            },
            mim = new
            {
                name = "MIM Add-on",
                description = "Asset lifecycle + handover module for FM phase",
                priceUsd = 10,
                cycle = "monthly",
            },
            annualDiscount = new
            {
                description = "Pay annually — get 1 month free (pay 11, use 12)",
                multiplierMonthsPerYear = 11,
            },
            ngoDiscount = new
            {
                description = "NGO and government organisations",
                discountPct = 15,
            },
            currencies = new
            {
                stripe = new[] { "USD", "EUR", "GBP" },
                flutterwave = new[] { "UGX", "KES", "TZS", "RWF", "NGN", "ZAR", "ZMW" },
            },
            comparison = new[]
            {
                new { vs = "Autodesk Construction Cloud", price = "$40-80/seat/mo",    planscape = "$35-90/firm (not per seat)" },
                new { vs = "Procore",                     price = "$375-549+/mo",      planscape = "$35-90/firm" },
                new { vs = "Trimble Connect",             price = "$20-40/seat",       planscape = "flat firm pricing" },
                new { vs = "BIM Track",                   price = "$40-55/seat",       planscape = "issue-tracking + viewer + plugin included" },
                new { vs = "Dalux",                       price = "$30-50/seat",       planscape = "offline-first + local currency billing" },
            },
        });
    }

    /// <summary>Marketing-friendly HTML pricing page for the public site.</summary>
    [HttpGet("html")]
    [Produces("text/html")]
    public ContentResult HtmlPage()
    {
        var plugin = BillingPlanLimits.For(BillingPlan.PluginOnly);
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
  <p class=""lead"">Per-firm, per-month, USD. Pay annually and get 1 month free. NGO &amp; government: 15% discount. Invoiced in your local currency via Flutterwave (UGX/KES/TZS/RWF/NGN/ZAR/ZMW) or in USD/EUR/GBP via Stripe.</p>
</header>
<section class=""grid"">
  {PluginOnlyCard(plugin)}
  {Card("Small",   s, "Plugin + cloud · up to 6 users · 5 projects.",          featured: false, plan: "Studio")}
  {Card("Medium",  p, "Plugin + cloud · up to 12 users · 10 projects.",        featured: true,  plan: "Practice", pill: "Most popular")}
  {Card("Large",   n, "Plugin + cloud · up to 20 users · unlimited projects.", featured: false, plan: "Network")}
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
        BillingPlan.PluginOnly => new[]
        {
            "Full Revit 2025/2026/2027 plugin", "ISO 19650 tagging suite",
            "IFC 4 export + property sets", "Local file storage only",
            "Unlimited Revit users on one machine", "No internet required",
        },
        BillingPlan.Studio => new[]
        {
            "Everything in StingTools Plugin", "Up to 6 users (cloud)",
            "Cloud sync + real-time dashboard", "5 active projects · 10 GB storage",
            "Issue tracker (BCF 2.1)", "Offline-first mobile app",
        },
        BillingPlan.Practice => new[]
        {
            "Everything in Small", "Up to 12 users", "10 active projects · 25 GB storage",
            "Document control + CDE", "Meeting minutes + transmittals",
        },
        BillingPlan.Network => new[]
        {
            "Everything in Medium", "Up to 20 users", "Unlimited projects · 50 GB storage",
            "4D/5D scheduling", "Federation across disciplines", "Priority support",
        },
        BillingPlan.Enterprise => new[]
        {
            "Everything in Large", "Unlimited seats + projects", "SSO (SAML/OIDC)",
            "On-prem or dedicated tenant", "99.9% SLA", "ISO 27001-light evidence pack",
        },
        _ => Array.Empty<string>(),
    };

    private static string PluginOnlyCard(BillingPlanLimits.Limits l)
    {
        var feats = string.Join("", FeaturesFor(BillingPlan.PluginOnly).Select(f => $"<li>{f}</li>"));
        return $@"<article class=""card plugin-card"">
  <div class=""pill"" style=""background:rgba(28,31,38,.08);color:#1c1f26"">Plugin only</div>
  <div class=""name"">StingTools</div>
  <div class=""price"">${l.MonthlyUsd:0}<span class=""cycle""> / firm / mo</span></div>
  <div class=""cycle"">or ${l.MonthlyUsd * 11:0} / year · no cloud needed</div>
  <ul class=""feat"">{feats}</ul>
  <a class=""cta"" href=""/signup?plan=PluginOnly"">Start 30-day trial</a>
</article>";
    }

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
  <div class=""cycle"">Pay annually = ${l.MonthlyUsd * 11:0} / year (1 month free)</div>
  <ul class=""feat"">{feats}</ul>
  <a class=""{ctaClass}"" href=""/signup?plan={plan}"">Start 30-day trial</a>
</article>";
    }

    private static string EnterpriseCard() => @"<article class=""card"">
  <div class=""name"">Enterprise</div>
  <div class=""price"">Custom</div>
  <div class=""cycle"">Unlimited users, on-prem option</div>
  <ul class=""feat"">
    <li>Everything in Large</li>
    <li>Unlimited seats + projects</li>
    <li>SSO (SAML/OIDC)</li>
    <li>On-prem deployment option</li>
    <li>99.9% SLA</li>
    <li>ISO 27001-light evidence pack</li>
  </ul>
  <a class=""cta"" href=""mailto:hello@planscape.app"">Talk to us</a>
</article>";
}
