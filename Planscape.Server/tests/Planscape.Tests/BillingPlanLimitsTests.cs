using Planscape.Core.Entities;

namespace Planscape.Tests;

/// <summary>
/// The plan limits, after seats were re-keyed onto authoring capability.
///
/// MaxAuthors is now the AUTHORING-seat cap and carries each plan's former
/// TOTAL headcount; MaxCoordinators is the read-only cap and is unlimited.
/// The previous MaxAuthors = 1 was never tested against a real count — the
/// author axis counted <c>ProjectRole == "Author"</c>, which nothing writes, so
/// it read 0 forever. Counting by capability made that cap bite immediately.
/// </summary>
public class BillingPlanLimitsTests
{
    /// <summary>Paid headcount per plan BEFORE this change
    /// (old MaxAuthors + old MaxCoordinators).</summary>
    public static TheoryData<BillingPlan, int> PreviousTotalSeats => new()
    {
        { BillingPlan.Trial,      1 },
        { BillingPlan.PluginOnly, 1 },
        { BillingPlan.Studio,     6 },
        { BillingPlan.Practice,  12 },
        { BillingPlan.Network,   20 },
    };

    [Theory]
    [MemberData(nameof(PreviousTotalSeats))]
    public void Paid_headcount_per_plan_is_unchanged(BillingPlan plan, int previousTotal)
    {
        // Price-neutrality, asserted rather than asserted-in-prose. If someone
        // later edits MaxAuthors, this fails and forces the pricing question to
        // be answered on purpose.
        //
        // The paid number is MaxAuthors, NOT TotalSeats: with read-only seats
        // unlimited there is no total-account cap, so TotalSeats is int.MaxValue
        // on every plan (see Unlimited_plans_saturate_rather_than_summing).
        Assert.Equal(previousTotal, BillingPlanLimits.For(plan).MaxAuthors);
    }

    [Theory]
    [MemberData(nameof(PreviousTotalSeats))]
    public void Read_only_seats_are_unlimited(BillingPlan plan, int _)
        => Assert.Equal(int.MaxValue, BillingPlanLimits.For(plan).MaxCoordinators);

    [Fact]
    public void Total_seats_never_wraps_negative_for_any_plan()
    {
        // Tenant.MaxUsers is derived from TotalSeats and consumed as
        // `userCount >= tenant.MaxUsers`, so a negative cap denies the tenant's
        // FIRST user. Written as MaxAuthors + MaxCoordinators this wraps in C#'s
        // default unchecked context the moment either side is int.MaxValue:
        // 1 + int.MaxValue = -2147483648, and Enterprise already wrapped to -2.
        foreach (BillingPlan plan in Enum.GetValues<BillingPlan>())
        {
            var total = BillingPlanLimits.For(plan).TotalSeats;
            Assert.True(total > 0,
                $"{plan} yields TotalSeats = {total}; a non-positive cap denies every user.");
        }
    }

    [Fact]
    public void Unlimited_plans_saturate_rather_than_summing()
    {
        // Every plan has unlimited read-only seats, so there is no total-account
        // cap anywhere and TotalSeats saturates rather than wrapping. The
        // saturation is the whole point: written as MaxAuthors + MaxCoordinators
        // this is 6 + int.MaxValue = -2147483643 for Studio, and Tenant.MaxUsers
        // is consumed as `userCount >= MaxUsers`, so that would deny the
        // tenant's first user.
        foreach (BillingPlan plan in Enum.GetValues<BillingPlan>())
            Assert.Equal(int.MaxValue, BillingPlanLimits.For(plan).TotalSeats);

        // The paid number lives on MaxAuthors and is unaffected.
        Assert.Equal(6, BillingPlanLimits.For(BillingPlan.Studio).MaxAuthors);
    }

    [Fact]
    public void A_realistic_authoring_roster_fits_every_paid_multi_seat_plan()
    {
        // Owner + Manager + 3 Contributors = 5 authoring accounts. This was
        // DENIED 5/1 on every plan below Enterprise before the limits change.
        const int realisticRoster = 5;

        foreach (var plan in new[] { BillingPlan.Studio, BillingPlan.Practice, BillingPlan.Network })
        {
            var limits = BillingPlanLimits.For(plan);
            Assert.True(limits.MaxAuthors >= realisticRoster,
                $"{plan} permits only {limits.MaxAuthors} authoring seats; a {realisticRoster}-person team cannot use it.");
        }

        // Single-seat plans are single-seat BY DESIGN and are not a regression:
        // their former total was 1. Pinned so the distinction stays deliberate.
        Assert.Equal(1, BillingPlanLimits.For(BillingPlan.Trial).MaxAuthors);
        Assert.Equal(1, BillingPlanLimits.For(BillingPlan.PluginOnly).MaxAuthors);
    }
}
