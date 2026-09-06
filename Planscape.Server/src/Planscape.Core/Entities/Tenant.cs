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
    /// <summary>
    /// Abuse ceiling on total active <see cref="AppUser"/> accounts in this tenant.
    /// Enforced as a count of active rows in <c>AdminController.CreateUser</c> and
    /// <c>ProjectMembersController</c>.
    ///
    /// <para>This is NOT a commercial seat cap and must not be derived from one.
    /// The commercial position is that author seats are the only paid unit and
    /// viewers/coordinators are free and unlimited — a promise we make in public on
    /// the pricing page — so a ceiling that scales with paid role caps refuses free
    /// accounts we said were free. It was previously
    /// <c>BillingPlanLimits.Limits.TotalSeats</c>; see #653. Its only job now is to
    /// stop runaway automated signup, so it is a single flat number
    /// (<see cref="BillingPlanLimits.AccountCeiling"/>) that no legitimate practice
    /// reaches.</para>
    ///
    /// <para>Non-positive means unlimited, and both enforcement sites read it through
    /// <see cref="Planscape.Core.AccountCeilingPolicy.Allows"/> rather than comparing
    /// directly — a raw <c>count &gt;= MaxUsers</c> treats 0 and -1 as "deny everyone",
    /// which is how #616 denied a tenant its first user.</para>
    /// </summary>
    public int MaxUsers { get; set; } = BillingPlanLimits.AccountCeiling;
    /// <summary>
    /// Per-tenant project ceiling. <b>A tightening override, not an entitlement.</b>
    ///
    /// <para>The plan grants capacity (<see cref="BillingPlanLimits.Limits.MaxProjects"/>);
    /// this column may only reduce it. Letting a column loosen a sold cap means any
    /// generous provisioning value silently upgrades the tenant — which is exactly
    /// what happened: signup wrote this from the CALLER-SUPPLIED plan defaulting to
    /// <see cref="BillingPlan.Network"/>, so every self-signup carried
    /// <c>int.MaxValue</c> here while its actual plan allowed 1.</para>
    ///
    /// <para>That disagreement was invisible only because
    /// <c>ProjectsController.CreateProject</c> carried TWO gates — the
    /// <c>[Quota(QuotaAxis.Projects)]</c> filter reading the PLAN and an inline
    /// <c>projectCount &gt;= tenant.MaxProjects</c> reading THIS COLUMN — and an
    /// action filter happens to run before the action body. The stricter limit won
    /// by ordering, not by design, and either gate moving would have changed
    /// behaviour. There is now one gate.</para>
    ///
    /// <para>Non-positive means "no override" — read it through
    /// <see cref="Planscape.Core.ProjectCeilingPolicy"/>, never as a bare
    /// comparison. Defaults to 0 rather than 1 so an un-provisioned tenant inherits
    /// its plan instead of being pinned to a single project.</para>
    /// </summary>
    public int MaxProjects { get; set; }

    /// <summary>
    /// The plan tier as named by planscape.build's D1 — <c>solo</c>, <c>studio</c>,
    /// <c>practice</c>, <c>firm</c>, <c>large</c>, <c>enterprise</c> — carried
    /// across the cloud handoff and, until now, discarded.
    ///
    /// <para><c>marketing-site/functions/api/cloud/handoff.ts</c> has always put
    /// <c>tier: tenant.plan_tier</c> in the signed ticket. The handoff endpoint read
    /// every other field and provisioned limits from a hardcoded
    /// <c>BillingPlanLimits.For(BillingPlan.Network)</c>, so a Solo customer arrived
    /// here holding Network's entitlement. D1 is the billing source of truth
    /// (see the licences/seat metering in
    /// <c>marketing-site/functions/api/license/_lib/seats.ts</c>); this server must
    /// not invent a plan for a tenant D1 has already priced.</para>
    ///
    /// <para>Stored as the raw D1 string, not parsed into <see cref="BillingPlan"/>,
    /// because the two taxonomies genuinely differ — there is no Solo, Firm or Large
    /// in that enum, and Network sits where two sold tiers are. Mapping happens in
    /// <see cref="BillingTierMap"/>, which keeps the seam in one readable place
    /// instead of spreading a lossy conversion. An unrecognised tier is kept
    /// verbatim and grants nothing.</para>
    /// </summary>
    public string? PlanTier { get; set; }
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
    /// <summary>
    /// The per-plan quota envelope.
    ///
    /// <para><see cref="Limits.MaxAuthors"/> and <see cref="Limits.MaxCoordinators"/>
    /// are <b>display figures for the pricing table</b> and nothing else. Since #653
    /// decoupled <see cref="Tenant.MaxUsers"/> from <see cref="Limits.TotalSeats"/>,
    /// and #626 retired the <c>Authors</c>/<c>Coordinators</c> quota axes, no
    /// enforcement decision anywhere reads them. Their three consumers are all
    /// presentation: <c>PricingController.Render</c>, the <c>limits</c> block of the
    /// signup response in <c>AuthController.Register</c>, and
    /// <c>TenantAdminController</c>. Do not reintroduce them as a cap without
    /// reading #653 first — they describe project roles, not accounts.</para>
    ///
    /// <para><see cref="Limits.MaxProjects"/> and <see cref="Limits.StorageMb"/> ARE
    /// enforced, via <c>QuotaGuardService</c> and the surviving
    /// <c>QuotaAxis.Projects</c>/<c>QuotaAxis.Storage</c> axes.</para>
    /// </summary>
    public record Limits(int MaxAuthors, int MaxCoordinators, int MaxProjects, long StorageMb, decimal MonthlyUsd)
    {
        /// <summary>
        /// Total headcount the plan is <b>marketed</b> with — the "up to 6 users" on
        /// the pricing page is this number. <b>Display only.</b>
        ///
        /// <para>It used to be the source of <see cref="Tenant.MaxUsers"/> at both
        /// tenant-creation paths. It is not any more (#653): summing two paid ROLE
        /// caps to bound total ACCOUNTS charged free viewers against a paid
        /// allowance, contradicting the pricing page's own FAQ. The account ceiling
        /// is now <see cref="BillingPlanLimits.AccountCeiling"/>, which is flat and
        /// independent of these axes. <b>Do not wire this back to a cap.</b></para>
        ///
        /// <para>SATURATES, and must never be rewritten as
        /// <c>MaxAuthors + MaxCoordinators</c>. C# arithmetic is unchecked by
        /// default, so that sum wraps NEGATIVE once either side is
        /// <c>int.MaxValue</c> — <c>int.MaxValue + int.MaxValue = -2</c>, which is
        /// what Enterprise produces today. A wrapped figure is a bug even for a
        /// display value, and the saturation is what stops it becoming a live
        /// lockout again if anyone does rewire it. Guarded by
        /// <c>BillingPlanSeatTotalTests</c>. See #616.</para>
        /// </summary>
        public int TotalSeats =>
            MaxAuthors == int.MaxValue || MaxCoordinators == int.MaxValue
                ? int.MaxValue
                : MaxAuthors + MaxCoordinators;
    }

    /// <summary>
    /// Flat ceiling on total active accounts per tenant, written to
    /// <see cref="Tenant.MaxUsers"/> at both tenant-creation paths.
    ///
    /// <para>Deliberately NOT per-plan and NOT derived from
    /// <see cref="Limits.MaxAuthors"/>/<see cref="Limits.MaxCoordinators"/> (#653).
    /// Since #626 removed the 402 invite gate this is the only thing bounding
    /// account creation, so it cannot simply be deleted — but it is an
    /// <b>anti-abuse</b> bound, not a commercial one. Paid entitlement is metered
    /// by the StingTools licence count in D1
    /// (<c>marketing-site/functions/api/license/_lib/seats.ts</c>), which counts
    /// machines; viewers hold no licence, so nothing on that side can bound them
    /// and nothing on this side should charge for them.</para>
    ///
    /// <para>10,000 is far above any legitimate practice — the largest published
    /// plan is "up to 20 users" plus their unlimited free clients and contractors —
    /// while still stopping scripted signup. Raise it rather than special-casing a
    /// tenant; a customer legitimately at this number is a conversation, not a
    /// config change.</para>
    /// </summary>
    public const int AccountCeiling = 10_000;

    /// <summary>
    /// <b><see cref="Limits.MaxProjects"/> here is the published entitlement</b> —
    /// what the customer bought — and is the only thing that GRANTS project
    /// capacity. <c>Tenant.MaxProjects</c> can tighten it per tenant but never
    /// loosen it; <see cref="ProjectCeilingPolicy"/> is where those two combine.
    ///
    /// <para>Trial is 3, not 1. It was 1, which contradicted
    /// <c>marketing-site/pricing.html</c> — the comparison row has read
    /// "Active projects: 3" for Solo since the page was written — and 1 is not
    /// enough to evaluate the product: a coordinator cannot compare two projects
    /// or try a second without deleting the first.</para>
    ///
    /// <para>The plan names here do not match the sold ones (Solo / Studio /
    /// Practice / Firm / Large / Enterprise). See <see cref="BillingTierMap"/>,
    /// which is the seam between the two taxonomies.</para>
    /// </summary>
    public static Limits For(BillingPlan plan) => plan switch
    {
        BillingPlan.Trial      => new Limits(1,  0,           3,           5_000,      0m),
        BillingPlan.PluginOnly => new Limits(1,  0, int.MaxValue,               0,     15m),
        BillingPlan.Studio     => new Limits(1,  5,           5,          10_000,      35m),
        BillingPlan.Practice   => new Limits(1, 11,          10,          25_000,      55m),
        BillingPlan.Network    => new Limits(1, 19, int.MaxValue,          50_000,      90m),
        BillingPlan.Enterprise => new Limits(int.MaxValue, int.MaxValue, int.MaxValue, long.MaxValue, 0m),
        _ => new Limits(1, 5, 1, 500, 0m),
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
