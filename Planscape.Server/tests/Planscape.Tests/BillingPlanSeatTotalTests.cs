using Planscape.Core.Entities;

namespace Planscape.Tests;

/// <summary>
/// <c>BillingPlanLimits.Limits.TotalSeats</c> is the headcount a plan is MARKETED
/// with — "up to 6 users" on the pricing page. Since #653 it is <b>display only</b>:
/// <c>Tenant.MaxUsers</c> is no longer derived from it (see
/// <c>AccountCeilingTests</c> for what replaced it).
///
/// This file survives the decoupling on purpose. The arithmetic is still wrong in a
/// way that is easy to reintroduce: C# is unchecked by default, so
/// <c>MaxAuthors + MaxCoordinators</c> wraps NEGATIVE the moment either side is
/// <c>int.MaxValue</c>:
///
///     1 + int.MaxValue            = -2,147,483,648
///     int.MaxValue + int.MaxValue = -2
///
/// Enterprise is <c>(int.MaxValue, int.MaxValue)</c> today. A wrapped figure is a bug
/// even as a display value, and keeping the saturation pinned means that if anyone
/// ever rewires this to a cap again it degrades to "unlimited" rather than to the #616
/// lockout, where a tenant was denied its FIRST user by <c>User limit (-2) reached</c>.
///
/// See #616 and #653. The loop enumerates <see cref="BillingPlan"/> rather than listing
/// plans on purpose: a plan added later is covered without anyone remembering this file
/// exists.
/// </summary>
public class BillingPlanSeatTotalTests
{
    [Fact]
    public void Total_seats_never_wraps_negative_for_any_plan()
    {
        foreach (BillingPlan plan in Enum.GetValues<BillingPlan>())
        {
            var total = BillingPlanLimits.For(plan).TotalSeats;

            Assert.True(total > 0,
                $"{plan} yields TotalSeats = {total}. The sum must saturate, not wrap — " +
                "a negative headcount is nonsense on the pricing page, and is a lockout " +
                "for anyone who rewires this to a cap again. See #616.");
        }
    }

    [Fact]
    public void An_unlimited_axis_saturates_instead_of_summing()
    {
        // The specific arithmetic that wraps, pinned so a future edit that
        // "simplifies" TotalSeats back to a raw sum fails here rather than in
        // production. Enterprise is the plan that has both axes unlimited.
        Assert.Equal(int.MaxValue, BillingPlanLimits.For(BillingPlan.Enterprise).TotalSeats);
    }

    [Fact]
    public void Finite_plans_still_report_their_real_total()
    {
        // Saturation must not become "everything is unlimited". Studio is
        // 1 author + 5 coordinators on main.
        var studio = BillingPlanLimits.For(BillingPlan.Studio);
        Assert.Equal(studio.MaxAuthors + studio.MaxCoordinators, studio.TotalSeats);
        Assert.NotEqual(int.MaxValue, studio.TotalSeats);
    }
}
