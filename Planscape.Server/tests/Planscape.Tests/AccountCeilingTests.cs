using Planscape.Core;
using Planscape.Core.Entities;

namespace Planscape.Tests;

/// <summary>
/// Covers what replaced the <c>TotalSeats</c> derivation of <c>Tenant.MaxUsers</c>
/// (#653): a flat <see cref="BillingPlanLimits.AccountCeiling"/> read through
/// <see cref="AccountCeilingPolicy"/>.
///
/// Two properties are being pinned, and they are the two acceptance criteria:
/// <list type="number">
/// <item>no configuration can produce a cap that refuses a tenant its FIRST account
/// — the #616 failure, which the raw <c>count &gt;= MaxUsers</c> at both enforcement
/// sites would still reproduce for any non-positive value; and</item>
/// <item>adding a free viewer does not consume a paid-role allowance — i.e. the
/// ceiling does not move with <c>MaxAuthors</c>/<c>MaxCoordinators</c>, so it cannot
/// re-acquire a coupling to paid roles by accident.</item>
/// </list>
/// </summary>
public class AccountCeilingTests
{
    // ── 1. No cap can deny the first account ─────────────────────────────────

    [Theory]
    [InlineData(-2)]              // the #616 wrapped sum
    [InlineData(int.MinValue)]    // 1 + int.MaxValue
    [InlineData(-1)]              // the repo's own "unlimited" sentinel (the deleted TierLimits)
    [InlineData(0)]               // a partially-populated row
    public void A_nonsensical_cap_reads_as_unlimited_not_as_deny_everyone(int cap)
    {
        Assert.True(AccountCeilingPolicy.Allows(activeUserCount: 0, maxUsers: cap),
            $"MaxUsers = {cap} denied the tenant its first account. A non-positive cap " +
            "must fail open; `count >= MaxUsers` is what produced 'User limit (-2) " +
            "reached' in #616.");

        Assert.DoesNotContain("-", AccountCeilingPolicy.Label(cap));
    }

    [Fact]
    public void No_plan_can_produce_a_ceiling_that_denies_the_first_account()
    {
        // Enumerated, not listed: a plan added later is covered automatically.
        foreach (BillingPlan plan in Enum.GetValues<BillingPlan>())
        {
            var tenant = NewTenant(plan);
            Assert.True(AccountCeilingPolicy.Allows(0, tenant),
                $"a tenant created on {plan} could not add its first account " +
                $"(MaxUsers = {tenant.MaxUsers}).");
        }
    }

    [Fact]
    public void The_entity_default_also_admits_a_first_account()
    {
        // Paths that construct a Tenant without setting MaxUsers must not inherit a
        // cap tighter than the ceiling; the default used to be 5.
        Assert.True(AccountCeilingPolicy.Allows(0, new Tenant()));
        Assert.Equal(BillingPlanLimits.AccountCeiling, new Tenant().MaxUsers);
    }

    // ── 2. Free accounts do not consume a paid-role allowance ────────────────

    [Fact]
    public void The_ceiling_does_not_move_with_the_paid_role_axes()
    {
        // The defect: Studio is 1 author + 5 coordinators, so a seat-derived cap
        // refused the firm's 7th account — including a free viewer, which the pricing
        // page FAQ promises does not count. Assert the ceiling is above every plan's
        // marketed seat total, so no plan's role caps can bind an account count.
        foreach (BillingPlan plan in Enum.GetValues<BillingPlan>())
        {
            var limits = BillingPlanLimits.For(plan);
            Assert.Equal(BillingPlanLimits.AccountCeiling, NewTenant(plan).MaxUsers);

            if (limits.TotalSeats == int.MaxValue) continue;   // unlimited by design
            Assert.True(BillingPlanLimits.AccountCeiling > limits.TotalSeats,
                $"{plan}'s marketed seat total ({limits.TotalSeats}) reaches the account " +
                "ceiling, so paid role caps bound free accounts again. See #653.");
        }
    }

    [Fact]
    public void A_studio_firm_can_add_far_more_accounts_than_its_seat_total()
    {
        // The concrete case in #653, stated as behaviour rather than as a constant:
        // the 7th account on Studio (1 + 5 = 6) must be admitted.
        var studio = NewTenant(BillingPlan.Studio);
        Assert.True(AccountCeilingPolicy.Allows(
            activeUserCount: BillingPlanLimits.For(BillingPlan.Studio).TotalSeats, studio));
    }

    // ── 3. It is still a ceiling ─────────────────────────────────────────────

    [Fact]
    public void Runaway_signup_is_still_refused()
    {
        // Decoupling must not become "no bound at all": since #626 removed the 402
        // invite gate this is the only thing standing between a tenant and scripted
        // account creation.
        var tenant = NewTenant(BillingPlan.Studio);
        Assert.False(AccountCeilingPolicy.Allows(BillingPlanLimits.AccountCeiling, tenant));
        Assert.False(AccountCeilingPolicy.Allows(BillingPlanLimits.AccountCeiling + 1, tenant));
        Assert.True(AccountCeilingPolicy.Allows(BillingPlanLimits.AccountCeiling - 1, tenant));
    }

    [Fact]
    public void An_explicit_admin_override_is_still_honoured()
    {
        // Positive values still bind — SeedData, DemoSandboxJob and PlatformTenantSeeder
        // all set MaxUsers deliberately, and SeedDataUserCapTests asserts against it.
        var tenant = new Tenant { MaxUsers = 50 };
        Assert.True(AccountCeilingPolicy.Allows(49, tenant));
        Assert.False(AccountCeilingPolicy.Allows(50, tenant));
        Assert.Equal("50", AccountCeilingPolicy.Label(50));
    }

    [Fact]
    public void A_missing_tenant_does_not_bound_anything()
    {
        // ProjectMembersController reaches this with a nullable tenant and previously
        // skipped the check entirely when it was null. Preserve that.
        Assert.True(AccountCeilingPolicy.Allows(int.MaxValue, (Tenant?)null));
    }

    /// <summary>
    /// Mirrors what the two tenant-creation paths in <c>AuthController</c> write.
    /// If this drifts from them the tests stop covering production.
    /// </summary>
    private static Tenant NewTenant(BillingPlan plan) => new()
    {
        Plan        = BillingPlan.Trial,
        MaxUsers    = BillingPlanLimits.AccountCeiling,
        // 0, not BillingPlanLimits.For(plan).MaxProjects. Signup no longer provisions
        // the column from the caller-supplied plan — it is a tightening override and
        // entitlement comes from the plan itself. See ProjectCeilingPolicy and
        // ProjectCeilingTests. `plan` still parameterises the seat assertions above.
        MaxProjects = 0,
    };
}
