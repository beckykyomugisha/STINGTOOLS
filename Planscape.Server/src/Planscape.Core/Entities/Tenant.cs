namespace Planscape.Core.Entities;

/// <summary>
/// Multi-tenant organization. Each tenant has isolated data and its own license tier.
/// </summary>
/// <remarks>
/// DELIBERATELY NOT <c>ITenantScoped</c> — do not "fix" this.
///
/// <c>PlanscapeDbContext.ApplyTenantQueryFilters</c> adds a global
/// <c>TenantId == CurrentTenantId</c> filter to every type implementing that
/// interface. Tenant has no TenantId — it *is* the tenant — and the exclusion is
/// load-bearing:
///
///   • <c>AuthController.Register</c> checks slug uniqueness with
///     <c>_db.Tenants.AnyAsync(t => t.Slug == …)</c> before any tenant context
///     exists. Filtered, that query finds nothing and DUPLICATE SLUGS are
///     accepted. <c>Register_DuplicateSlug_Returns409</c> is the regression guard.
///   • Subdomain/tenant resolution looks a tenant up by slug the same way.
///   • The cloud→server handoff path resolves a tenant before authentication.
///
/// Audited 2026-07-30: every Tenant read in the API is keyed on the CALLER'S OWN
/// claim (<c>User.FindFirst("tenant_id")</c> via each controller's GetTenantId,
/// or <c>ITenantContext.TenantId</c>), never on a route or query parameter, so no
/// authenticated request can reach another organisation's row. The exceptions are
/// intentional: <c>PlatformRevenueController</c> is platform-owner-only and
/// cross-tenant by design, and <c>AuthController</c> uses explicit
/// <c>IgnoreQueryFilters()</c> where it must resolve a tenant pre-auth.
///
/// If a future endpoint takes a tenant id from the request, constrain it at that
/// call site — do not add a global filter here.
/// </remarks>
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
    // F3 — track last modification time so the admin dashboard and audit trail
    // can surface "last changed" without querying AuditLog on every page load.
    // Auto-stamped by PlanscapeDbContext.SaveChangesAsync on every Modified entry.
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    /// S7.4 — set when the Owner requests erasure under GDPR/POPIA. The
    /// tenant is frozen immediately; a daily DataErasureJob hard-deletes
    /// the rows after this timestamp passes (30-day cooling-off period
    /// during which the request can be cancelled).
    /// </summary>
    public DateTime? PendingErasureAt { get; set; }

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
    /// <summary>Free 30-day trial; converts to PluginOnly on expiry unless cancelled.</summary>
    Trial = 0,
    /// <summary>$15/mo — Revit plugin only, local storage, no cloud sync.</summary>
    PluginOnly = 1,
    /// <summary>$35/mo — plugin + cloud · up to 6 users · 5 projects · 10 GB</summary>
    Studio = 2,
    /// <summary>$55/mo — plugin + cloud · up to 12 users · 10 projects · 25 GB</summary>
    Practice = 3,
    /// <summary>$90/mo — plugin + cloud · up to 20 users · unlimited projects · 50 GB</summary>
    Network = 4,
    /// <summary>Custom — unlimited seats + projects; SSO · SLA · on-prem option</summary>
    Enterprise = 5,
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
    public record Limits(int MaxAuthors, int MaxCoordinators, int MaxProjects, long StorageMb, decimal MonthlyUsd)
    {
        /// <summary>
        /// Total billable headcount — the number <c>Tenant.MaxUsers</c> is
        /// derived from at registration.
        ///
        /// <para>SATURATES. This must never be written as
        /// <c>MaxAuthors + MaxCoordinators</c>: with either side at
        /// <c>int.MaxValue</c> that sum wraps NEGATIVE in C#'s default unchecked
        /// context, and <c>MaxUsers</c> is consumed as
        /// <c>userCount >= tenant.MaxUsers</c> — so a negative cap denies the
        /// tenant's very first user. Enterprise already wrapped to -2 this way;
        /// it was latent only because registration assigns Trial.</para>
        ///
        /// <para>When read-only seats are unlimited this reports the PAID
        /// headcount (<see cref="MaxAuthors"/>) rather than infinity. That is a
        /// deliberate no-giveaway choice: <c>MaxUsers</c> is the only cap
        /// <c>AdminController</c> and <c>ProjectMembersController</c> enforce,
        /// and neither of them consults the quota guard, so returning infinity
        /// here would leave both paths able to add unlimited AUTHORING accounts
        /// with nothing counting them. The cost of this choice is that free
        /// viewers are not yet free in practice — a Studio tenant is still
        /// capped at 6 accounts in total. Lifting that safely means teaching
        /// those two paths the authoring-capability check; until then, erring
        /// toward charging correctly beats erring toward giving seats away.</para>
        /// </summary>
        public int TotalSeats =>
            MaxCoordinators == int.MaxValue
                ? MaxAuthors                                   // paid headcount; also int.MaxValue for Enterprise
                : MaxAuthors == int.MaxValue
                    ? int.MaxValue
                    : MaxAuthors + MaxCoordinators;
    }

    // MaxAuthors is the AUTHORING-seat cap — accounts that can create or change
    // information (ProjectRoles.CanAuthorInformation). It carries each plan's
    // former TOTAL headcount (MaxAuthors + MaxCoordinators), so the paid seat
    // count per plan is unchanged: Trial 1, PluginOnly 1, Studio 6, Practice 12,
    // Network 20.
    //
    // MaxCoordinators is now the READ-ONLY cap and is unlimited — viewers are
    // free. It was previously the only enforced axis purely by accident: the
    // author axis counted ProjectRole == "Author", which nothing ever wrote, so
    // it read 0 forever and MaxAuthors = 1 was never actually tested against a
    // real count. Once seats are counted by capability, a cap of 1 would have
    // denied every realistic roster on every plan below Enterprise (measured:
    // Owner + Manager + 3 Contributors = 5 authoring accounts, denied 5/1 on
    // Trial, PluginOnly, Studio, Practice and Network).
    public static Limits For(BillingPlan plan) => plan switch
    {
        BillingPlan.Trial      => new Limits( 1, int.MaxValue,            1,       5_000,  0m),
        BillingPlan.PluginOnly => new Limits( 1, int.MaxValue, int.MaxValue,           0, 15m),
        BillingPlan.Studio     => new Limits( 6, int.MaxValue,            5,      10_000, 35m),
        BillingPlan.Practice   => new Limits(12, int.MaxValue,           10,      25_000, 55m),
        BillingPlan.Network    => new Limits(20, int.MaxValue, int.MaxValue,      50_000, 90m),
        BillingPlan.Enterprise => new Limits(int.MaxValue, int.MaxValue, int.MaxValue, long.MaxValue, 0m),
        _                      => new Limits( 6, int.MaxValue,            1,         500,  0m),
    };

    /// <summary>Map a legacy LicenseTier onto the new BillingPlan for migrations.</summary>
    public static BillingPlan FromLegacyTier(LicenseTier tier) => tier switch
    {
        LicenseTier.Starter      => BillingPlan.PluginOnly,
        LicenseTier.Professional => BillingPlan.Studio,
        LicenseTier.Premium      => BillingPlan.Practice,
        LicenseTier.Enterprise   => BillingPlan.Enterprise,
        _ => BillingPlan.Trial,
    };
}
