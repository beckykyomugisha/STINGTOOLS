namespace Planscape.Core.Entities;

/// <summary>
/// Multi-tenant organization. Each tenant has isolated data and its own license tier.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = ""; // subdomain: {slug}.planscape.io
    public string ContactEmail { get; set; } = "";
    public LicenseTier Tier { get; set; } = LicenseTier.Starter;
    public bool MimEnabled { get; set; } // Planscape MIM add-on
    public MimTier MimTier { get; set; } = MimTier.None;
    public int MaxUsers { get; set; } = 5;
    public int MaxProjects { get; set; } = 1;
    public long StorageLimitBytes { get; set; } = 500 * 1024 * 1024; // 500 MB
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TrialExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    /// <summary>
    /// S1.3 — canonical billing plan for the East-Africa pricing strategy.
    /// Populated alongside the legacy <see cref="Tier"/> field; new code
    /// (signup, billing, quota guards, dashboards) reads <see cref="Plan"/>.
    /// Tier stays for backwards compatibility and legacy license-key lookups.
    /// Mapping in <see cref="BillingPlanLimits"/>.
    /// </summary>
    public BillingPlan Plan { get; set; } = BillingPlan.Trial;

    /// <summary>
    /// S1.3 — current billing currency. Determines whether invoices use
    /// Stripe (USD/EUR/GBP) or Flutterwave (UGX/KES/TZS/RWF/NGN/ZAR/...).
    /// ISO 4217 code; defaults to USD for new accounts.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// S1.3 — billing cycle. Annual prepay grants two months free per the
    /// pricing plan; monthly renews via the chosen payment provider.
    /// </summary>
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

    /// <summary>
    /// S1.6 — bitmask of which trial-expiry reminders have been sent.
    /// Bit 4 = 7-day · Bit 2 = 3-day · Bit 1 = 1-day. Stops the trial
    /// state machine job from emailing the same warning every day.
    /// </summary>
    public int TrialReminderSentDays { get; set; }

    /// <summary>
    /// Phase 151 — tenant-scoped keyword extensions for the deliverable
    /// state machine. JSON shape mirrors the per-project block:
    ///   { "working": ["PARKED"], "terminal": ["DECOMMISSIONED"] }
    /// Sits between platform-wide keywords (deployment-global, in
    /// appsettings) and project-level keywords (per-project JSON).
    /// Project-level wins, then tenant, then platform, then built-ins.
    /// Null/empty means "use platform + built-ins only" — same behaviour
    /// as Phase 150.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "jsonb")]
    public string? KeywordExtensionsJson { get; set; }

    /// <summary>
    /// Phase 154 — tenant-scoped override for the BIM-Manager grant
    /// list used by <c>BimManagerOrAdminHandler</c>. JSON array of
    /// ISO 19650 single-letter role codes, e.g. <c>["K", "C", "M"]</c>.
    /// Null/empty falls back to the deployment-wide
    /// <c>Authorization:BimManagerIso19650Roles</c> appsettings list,
    /// which itself defaults to <c>["K"]</c>. Lets a multi-tenant
    /// deployment grant tenant-coordinator (C) keyword-edit rights on
    /// one tenant without affecting others.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "jsonb")]
    public string? BimManagerIso19650RolesJson { get; set; }

    // Navigation
    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<LicenseKey> LicenseKeys { get; set; } = new List<LicenseKey>();
}

public enum LicenseTier
{
    Starter = 0,
    Professional = 1,
    Premium = 2,
    Enterprise = 3
}

public enum MimTier
{
    None = 0,
    MimStarter = 1,
    MimProfessional = 2,
    MimEnterprise = 3
}

/// <summary>
/// S1.3 — canonical billing plans aligned to the East-Africa pricing
/// strategy (proposal Apr 2026). Trial is the entry state for new
/// signups; the rest map to monthly USD price points.
/// </summary>
public enum BillingPlan
{
    /// <summary>Free 30-day trial; converts to Studio on expiry unless cancelled.</summary>
    Trial = 0,
    /// <summary>$30/mo — 1 author + 10 coordinators · 3 projects · 5 GB</summary>
    Studio = 1,
    /// <summary>$80/mo — 1 author + 25 coordinators · 10 projects · 25 GB</summary>
    Practice = 2,
    /// <summary>$150/mo — 3 authors + 35 coordinators · 10 projects · 50 GB</summary>
    Network = 3,
    /// <summary>≥$3,500/mo — custom; SLA + dedicated support + on-prem option</summary>
    Enterprise = 4,
}

public enum BillingCycle
{
    Monthly = 0,
    Annual = 1,
}

/// <summary>
/// S1.3 — single source of truth for the per-plan quota envelope. Driven by
/// the proposal's pricing table and consumed by the quota-guard middleware
/// (S1.4). Storage in MB to keep numbers human-readable.
/// </summary>
public static class BillingPlanLimits
{
    public record Limits(int MaxAuthors, int MaxCoordinators, int MaxProjects, long StorageMb, decimal MonthlyUsd);

    public static Limits For(BillingPlan plan) => plan switch
    {
        BillingPlan.Trial      => new Limits(1, 25, 3,  5_000, 0m),
        BillingPlan.Studio     => new Limits(1, 10, 3,  5_000, 30m),
        BillingPlan.Practice   => new Limits(1, 25, 10, 25_000, 80m),
        BillingPlan.Network    => new Limits(3, 35, 10, 50_000, 150m),
        BillingPlan.Enterprise => new Limits(int.MaxValue, int.MaxValue, int.MaxValue, long.MaxValue, 3_500m),
        _ => new Limits(1, 5, 1, 500, 0m),
    };

    /// <summary>Map a legacy LicenseTier onto the new BillingPlan for migrations.</summary>
    public static BillingPlan FromLegacyTier(LicenseTier tier) => tier switch
    {
        LicenseTier.Starter      => BillingPlan.Trial,
        LicenseTier.Professional => BillingPlan.Studio,
        LicenseTier.Premium      => BillingPlan.Practice,
        LicenseTier.Enterprise   => BillingPlan.Enterprise,
        _ => BillingPlan.Trial,
    };
}
