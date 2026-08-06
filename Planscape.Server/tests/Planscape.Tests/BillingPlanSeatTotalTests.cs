using Planscape.Core.Entities;

namespace Planscape.Tests;

/// <summary>
/// <c>Tenant.MaxUsers</c> is derived from the plan's seat total at tenant
/// creation, and consumed as <c>userCount &gt;= tenant.MaxUsers</c>
/// (<c>AdminController</c>, <c>ProjectMembersController</c>). A non-positive
/// cap is therefore true at <c>userCount = 0</c> — the tenant is refused its
/// very first user, with a message (<c>User limit (-2) reached</c>) that points
/// at nothing.
///
/// C# arithmetic is unchecked by default, so <c>MaxAuthors + MaxCoordinators</c>
/// wraps negative the moment either side is <c>int.MaxValue</c>:
///
///     1 + int.MaxValue            = -2,147,483,648
///     int.MaxValue + int.MaxValue = -2
///
/// Enterprise is <c>(int.MaxValue, int.MaxValue)</c> today, so this is reachable
/// on main right now. It is latent only because both tenant-creation paths
/// (<c>AuthController</c> registration and the D1 handoff) assign
/// <c>BillingPlan.Trial</c>, which is <c>1 + 0</c>. It goes live the moment any
/// plan with an unlimited axis is assigned at creation.
///
/// See #616. The loop enumerates <see cref="BillingPlan"/> rather than listing
/// plans on purpose: a plan added later is covered without anyone remembering
/// this file exists.
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
                $"{plan} yields TotalSeats = {total}. A non-positive cap is written to " +
                "Tenant.MaxUsers and compared as `userCount >= MaxUsers`, so it denies " +
                "the tenant its first user. The sum must saturate, not wrap.");
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
