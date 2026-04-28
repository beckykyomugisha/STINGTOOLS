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
