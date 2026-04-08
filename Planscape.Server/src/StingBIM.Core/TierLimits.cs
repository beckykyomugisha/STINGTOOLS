using StingBIM.Core.Entities;

namespace StingBIM.Core;

/// <summary>
/// Defines per-tier resource limits for StingBIM tenants.
/// -1 means unlimited. All Professional tiers and above get unlimited projects.
///
/// The Tenant.MaxUsers and Tenant.MaxProjects fields act as admin overrides —
/// set them to -1 to inherit tier defaults, or to a specific value to cap a tenant.
/// </summary>
public static class TierLimits
{
    // ── Users ────────────────────────────────────────────────────────────────

    /// <summary>Maximum user accounts per tenant. -1 = unlimited.</summary>
    public static int MaxUsers(LicenseTier tier) => tier switch
    {
        LicenseTier.Starter      => 20,
        LicenseTier.Professional => 100,
        LicenseTier.Premium      => 500,
        LicenseTier.Enterprise   => -1,   // unlimited
        _                        => 20
    };

    // ── Projects ─────────────────────────────────────────────────────────────

    /// <summary>Maximum active projects per tenant. -1 = unlimited.</summary>
    public static int MaxProjects(LicenseTier tier) => tier switch
    {
        LicenseTier.Starter      => 10,   // trial — generous enough to evaluate
        LicenseTier.Professional => -1,   // unlimited
        LicenseTier.Premium      => -1,   // unlimited
        LicenseTier.Enterprise   => -1,   // unlimited
        _                        => 10
    };

    // ── Storage ──────────────────────────────────────────────────────────────

    /// <summary>Storage limit in bytes. -1 = unlimited.</summary>
    public static long StorageLimitBytes(LicenseTier tier) => tier switch
    {
        LicenseTier.Starter      => 2L  * 1024 * 1024 * 1024,  //  2 GB
        LicenseTier.Professional => 20L * 1024 * 1024 * 1024,  // 20 GB
        LicenseTier.Premium      => 100L* 1024 * 1024 * 1024,  // 100 GB
        LicenseTier.Enterprise   => -1,                          // unlimited
        _                        => 2L  * 1024 * 1024 * 1024
    };

    // ── License activations ───────────────────────────────────────────────────

    /// <summary>Revit plugin activations per license key. -1 = unlimited.</summary>
    public static int MaxActivations(LicenseTier tier) => tier switch
    {
        LicenseTier.Starter      => 5,
        LicenseTier.Professional => 25,
        LicenseTier.Premium      => 100,
        LicenseTier.Enterprise   => -1,
        _                        => 5
    };

    // ── MIM assets ───────────────────────────────────────────────────────────

    /// <summary>Asset records per project. -1 = unlimited.</summary>
    public static int MaxMimAssets(LicenseTier tier) => tier switch
    {
        LicenseTier.Starter      => 1_000,
        LicenseTier.Professional => 50_000,
        LicenseTier.Premium      => 250_000,
        LicenseTier.Enterprise   => -1,
        _                        => 1_000
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="count"/> has not reached the limit.
    /// -1 (unlimited) always returns true.
    /// Also respects an admin override — if <paramref name="adminOverride"/> is
    /// &gt; 0 it takes precedence over the tier default.
    /// </summary>
    public static bool BelowLimit(int count, int tierLimit, int adminOverride = 0)
    {
        int effective = adminOverride > 0 ? adminOverride : tierLimit;
        return effective < 0 || count < effective;
    }

    /// <summary>Human-readable limit string for error messages.</summary>
    public static string LimitLabel(int tierLimit, int adminOverride = 0)
    {
        int effective = adminOverride > 0 ? adminOverride : tierLimit;
        return effective < 0 ? "unlimited" : effective.ToString();
    }
}
