using Planscape.Core;
using Planscape.Core.Entities;

namespace Planscape.Tests;

/// <summary>
/// Pins the project cap after collapsing two disagreeing gates into one.
///
/// <para><c>ProjectsController.CreateProject</c> carried BOTH
/// <c>[Quota(QuotaAxis.Projects)]</c> — resolving the limit from the PLAN — and an
/// inline <c>projectCount &gt;= tenant.MaxProjects</c> reading the COLUMN. For every
/// self-signup those disagreed outright (plan 1, column <c>int.MaxValue</c>) and the
/// stricter one won only because an action filter runs before an action body. These
/// tests exist so the resolution is a property of the policy rather than of MVC's
/// execution order.</para>
///
/// <para>Four things are held down: a cap can never deny a tenant its first project;
/// the column tightens but cannot loosen; D1's tier outranks the local plan when D1
/// named one; and Trial still matches the published pricing page.</para>
/// </summary>
public class ProjectCeilingTests
{
    // ── 1. No cap can deny the first project ─────────────────────────────────

    [Theory]
    [InlineData(-1)]           // the "unlimited" sentinel the deleted TierLimits used
    [InlineData(0)]            // a partially-populated row, and the new column default
    [InlineData(int.MinValue)] // a wrapped sum, as in #616
    public void A_nonpositive_cap_reads_as_unlimited_not_as_deny_everyone(int cap)
    {
        Assert.True(ProjectCeilingPolicy.Allows(projectCount: 0, planCap: cap, columnCap: cap),
            $"cap {cap} refused the tenant its FIRST project. `count >= cap` cannot " +
            "express unlimited, which is how #616 produced 'limit (-2) reached'.");

        Assert.DoesNotContain("-", ProjectCeilingPolicy.Label(cap, cap));
    }

    [Fact]
    public void No_plan_can_produce_a_ceiling_that_denies_the_first_project()
    {
        // Enumerated, so a plan added later is covered without editing this test.
        foreach (BillingPlan plan in Enum.GetValues<BillingPlan>())
        {
            var tenant = new Tenant { Plan = plan, MaxProjects = 0 };
            Assert.True(ProjectCeilingPolicy.Allows(0, tenant),
                $"a tenant on {plan} could not create its first project.");
        }
    }

    [Fact]
    public void The_entity_default_inherits_the_plan_rather_than_pinning_one_project()
    {
        // The column defaulted to 1, which silently tightened every tenant constructed
        // without setting it. 0 means "no override".
        Assert.Equal(0, new Tenant().MaxProjects);
        Assert.Equal(
            BillingPlanLimits.For(BillingPlan.Trial).MaxProjects,
            ProjectCeilingPolicy.EffectiveCap(new Tenant()));
    }

    // ── 2. The column tightens; it never loosens ─────────────────────────────

    [Fact]
    public void The_column_can_tighten_the_plan()
    {
        // What a support override is for: this tenant bought Practice but is held to 2.
        var tenant = new Tenant { Plan = BillingPlan.Practice, MaxProjects = 2 };
        Assert.Equal(2, ProjectCeilingPolicy.EffectiveCap(tenant));
        Assert.True(ProjectCeilingPolicy.Allows(1, tenant));
        Assert.False(ProjectCeilingPolicy.Allows(2, tenant));
    }

    [Fact]
    public void The_column_cannot_loosen_the_plan()
    {
        // THE defect. Signup wrote the column from the caller-supplied plan (default
        // Network, so int.MaxValue) while assigning Trial, leaving an unlimited column
        // against a 3-project plan. If a column could grant, every self-signup would be
        // an unlimited account the moment the inline gate was the only one consulted.
        var tenant = new Tenant { Plan = BillingPlan.Trial, MaxProjects = int.MaxValue };
        Assert.Equal(BillingPlanLimits.For(BillingPlan.Trial).MaxProjects,
                     ProjectCeilingPolicy.EffectiveCap(tenant));
        Assert.False(ProjectCeilingPolicy.Allows(
            BillingPlanLimits.For(BillingPlan.Trial).MaxProjects, tenant));
    }

    [Fact]
    public void An_unlimited_plan_with_no_override_stays_unlimited()
    {
        var tenant = new Tenant { Plan = BillingPlan.Enterprise, MaxProjects = 0 };
        Assert.True(ProjectCeilingPolicy.Allows(1_000_000, tenant));
        Assert.Equal("unlimited", ProjectCeilingPolicy.Label(int.MaxValue, 0));
    }

    // ── 3. D1's tier outranks the local plan ─────────────────────────────────

    [Fact]
    public void A_known_tier_grants_instead_of_the_local_plan()
    {
        // The live defect this fixes: the handoff mirrored every D1 customer onto a
        // plan chosen here, so the [Quota] filter — which reads the plan — capped a
        // paying Practice customer at whatever that local plan allowed.
        var tenant = new Tenant { Plan = BillingPlan.Trial, PlanTier = "practice" };
        Assert.True(
            ProjectCeilingPolicy.Allows(
                BillingPlanLimits.For(BillingPlan.Trial).MaxProjects + 50, tenant),
            "a Practice tenant from D1 was held to the local Trial plan.");
    }

    [Fact]
    public void A_known_tier_can_be_tighter_than_the_local_plan()
    {
        // The tier is the entitlement, not a floor: Solo arriving on a generously
        // mirrored plan must still be Solo.
        var tenant = new Tenant { Plan = BillingPlan.Network, PlanTier = "solo" };
        Assert.Equal(3, ProjectCeilingPolicy.EffectiveCap(tenant));
        Assert.False(ProjectCeilingPolicy.Allows(3, tenant));
    }

    [Theory]
    [InlineData("SOLO")]
    [InlineData("  Studio  ")]
    public void Tier_matching_tolerates_case_and_whitespace(string tier)
    {
        // The value is whatever D1 stored in plan_tier and is compared as text, so a
        // stray case or space must not silently demote a paying customer to the
        // fallback plan.
        Assert.True(BillingTierMap.IsKnown(tier));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("platinum")]      // a tier invented on the D1 side later
    public void An_unknown_tier_grants_nothing_and_falls_back_to_the_plan(string? tier)
    {
        Assert.Null(BillingTierMap.MaxProjectsForTier(tier));

        // Falls back rather than denying — an unrecognised string must not read as 0.
        var tenant = new Tenant { Plan = BillingPlan.Studio, PlanTier = tier };
        Assert.Equal(BillingPlanLimits.For(BillingPlan.Studio).MaxProjects,
                     ProjectCeilingPolicy.EffectiveCap(tenant));
    }

    [Fact]
    public void Every_sold_tier_is_mapped()
    {
        // These six are the columns of marketing-site/pricing.html. A tier the map does
        // not know falls back to the mirror's plan, which is the exact class of bug this
        // change removes — so adding a tier to the pricing page without adding it here
        // should fail loudly right here.
        foreach (var tier in new[] { "solo", "studio", "practice", "firm", "large", "enterprise" })
            Assert.True(BillingTierMap.IsKnown(tier), $"sold tier '{tier}' is unmapped.");
    }

    // ── 4. Trial matches the published pricing page ──────────────────────────

    [Fact]
    public void Trial_allows_three_projects_as_the_pricing_page_says()
    {
        // pricing.html's comparison row reads "Active projects: 3" for Solo. It was 1
        // here, so the product contradicted the page the customer signed up from — and
        // one project cannot evaluate anything that involves comparing two.
        Assert.Equal(3, BillingPlanLimits.For(BillingPlan.Trial).MaxProjects);
        Assert.Equal(3, BillingTierMap.MaxProjectsForTier("solo"));

        var tenant = new Tenant { Plan = BillingPlan.Trial, MaxProjects = 0 };
        Assert.True(ProjectCeilingPolicy.Allows(2, tenant));
        Assert.False(ProjectCeilingPolicy.Allows(3, tenant));
    }

    [Fact]
    public void A_missing_tenant_does_not_grant_more_than_a_trial()
    {
        // QuotaGuardService reaches this with a nullable tenant. Failing open to
        // "unlimited" there would turn an unknown tenant into a free unlimited one.
        Assert.Equal(BillingPlanLimits.For(BillingPlan.Trial).MaxProjects,
                     ProjectCeilingPolicy.EffectiveCap((Tenant?)null));
    }
}
