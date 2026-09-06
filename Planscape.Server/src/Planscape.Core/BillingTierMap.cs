namespace Planscape.Core;

/// <summary>
/// Maps the plan tier planscape.build's D1 sends across the handoff onto the
/// limits this server enforces.
///
/// <para><b>Why this exists.</b> The handoff ticket has always carried
/// <c>tier: tenant.plan_tier</c> (marketing-site/functions/api/cloud/handoff.ts),
/// and this server threw it away — provisioning every mirrored tenant from a
/// hardcoded <c>BillingPlanLimits.For(BillingPlan.Network)</c>. That is how a
/// customer D1 considers paid could be given limits nobody sold them.</para>
///
/// <para><b>Why the names differ.</b> The sold plans are Solo / Studio / Practice /
/// Firm / Large / Enterprise (marketing-site/pricing.html). This server's
/// <c>BillingPlan</c> enum is Trial / PluginOnly / Studio / Practice / Network /
/// Enterprise — it has no Solo, Firm or Large, and its Network sits where Firm and
/// Large are sold. The two taxonomies drifted apart, and unifying them is a
/// separate piece of work with commercial input. Until then this map is the seam,
/// and it is keyed by the SOLD names because those are the ones a customer paid
/// against.</para>
///
/// <para><b>The numbers come from the pricing table, not from
/// <c>BillingPlanLimits</c>.</b> pricing.html's comparison row reads
/// 3 / 10 / Unlimited / Unlimited / Unlimited / Unlimited. Where the two disagree
/// the published page wins: it is what the customer was shown.</para>
/// </summary>
public static class BillingTierMap
{
    /// <summary>
    /// Projects included in a sold tier. <c>int.MaxValue</c> means unlimited.
    /// Returns <c>null</c> for an unknown or absent tier, which the caller must
    /// treat as "D1 told us nothing" rather than as zero.
    /// </summary>
    public static int? MaxProjectsForTier(string? tier) => Normalise(tier) switch
    {
        "solo"       => 3,
        "studio"     => 10,
        "practice"   => int.MaxValue,
        "firm"       => int.MaxValue,
        "large"      => int.MaxValue,
        "enterprise" => int.MaxValue,
        _            => null,
    };

    /// <summary>
    /// True when the tier is one this server recognises. Used to decide whether a
    /// mirrored tenant carries usable entitlement or only a display value.
    /// </summary>
    public static bool IsKnown(string? tier) => MaxProjectsForTier(tier) != null;

    private static string Normalise(string? tier) => (tier ?? string.Empty).Trim().ToLowerInvariant();
}
