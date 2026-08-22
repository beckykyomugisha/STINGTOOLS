using Planscape.Core.Entities;

namespace Planscape.Core;

/// <summary>
/// The one place that decides whether a tenant may create another project.
///
/// <para>Before this, ONE action carried TWO gates reading TWO different sources.
/// <c>ProjectsController.CreateProject</c> had both
/// <c>[Quota(QuotaAxis.Projects)]</c> — which resolved the limit from
/// <c>BillingPlanLimits.For(tenant.Plan)</c> — and an inline
/// <c>projectCount &gt;= tenant.MaxProjects</c> reading the tenant COLUMN. For a
/// self-signup those disagreed completely: the plan said 1 and the column said
/// <c>int.MaxValue</c>. The stricter one won purely because an action filter runs
/// before the action body, which is ordering, not design.</para>
///
/// <para><b>Precedence: the plan grants, the column only tightens.</b> Entitlement
/// is what the customer bought, so a per-tenant column must never be able to
/// LOOSEN a sold cap — otherwise provisioning a generous anti-abuse value would
/// silently hand every tenant an unlimited plan. It can tighten, which is what an
/// admin override is for.</para>
///
/// <para>Fails OPEN on a non-positive cap, for the same reason
/// <see cref="AccountCeilingPolicy"/> does: <c>count &gt;= cap</c> cannot express
/// "unlimited", so a <c>-1</c> sentinel (the convention the deleted
/// <c>TierLimits</c> documented), a <c>0</c> from a partially-populated row, or a
/// wrapped negative all deny the tenant its FIRST project while reporting a limit
/// that points at nothing.</para>
///
/// <para><b>Deliberately unlike the <c>TierLimits.BelowLimit</c> deleted alongside
/// that file.</b> It read <c>adminOverride &gt; 0 ? adminOverride : tierLimit</c> — a
/// REPLACEMENT, so a per-tenant value could raise a cap above what the plan sold.
/// Nothing ever called it, so those semantics were never exercised; they are not the
/// ones adopted here.</para>
/// </summary>
public static class ProjectCeilingPolicy
{
    /// <summary>Treats any non-positive value as "unlimited".</summary>
    private static bool IsUnlimited(int cap) => cap <= 0 || cap == int.MaxValue;

    /// <summary>
    /// The cap actually in force: the plan's entitlement, tightened by the tenant
    /// column when that column is a positive, stricter value.
    /// </summary>
    public static int EffectiveCap(int planCap, int columnCap)
    {
        if (IsUnlimited(planCap) && IsUnlimited(columnCap)) return int.MaxValue;
        if (IsUnlimited(planCap)) return columnCap;
        if (IsUnlimited(columnCap)) return planCap;
        return planCap < columnCap ? planCap : columnCap;
    }

    /// <summary>
    /// True if a tenant currently holding <paramref name="projectCount"/> projects
    /// may create one more.
    /// </summary>
    public static bool Allows(int projectCount, int planCap, int columnCap)
    {
        var cap = EffectiveCap(planCap, columnCap);
        return IsUnlimited(cap) || projectCount < cap;
    }

    /// <summary>
    /// The cap that GRANTS capacity for a tenant, before any tightening.
    ///
    /// <para>If planscape.build's D1 named a tier this server recognises, <b>that
    /// wins</b> — D1 is the billing authority and this database is a mirror of it.
    /// Only when D1 said nothing usable does the local <see cref="Tenant.Plan"/>
    /// decide.</para>
    ///
    /// <para>This ordering is the fix for a live defect, not a refinement. The cloud
    /// handoff created mirrored tenants with <c>Plan = BillingPlan.Trial</c> while
    /// computing (and discarding) Network's limits, so the
    /// <c>[Quota(QuotaAxis.Projects)]</c> filter — which reads the plan — allowed a
    /// D1-paying customer exactly ONE project. The tenant row said
    /// <c>int.MaxValue</c>, the inline gate agreed, and the filter refused anyway.</para>
    /// </summary>
    public static int GrantingCap(Tenant? tenant)
    {
        if (tenant == null) return BillingPlanLimits.For(BillingPlan.Trial).MaxProjects;
        return BillingTierMap.MaxProjectsForTier(tenant.PlanTier)
               ?? BillingPlanLimits.For(tenant.Plan).MaxProjects;
    }

    /// <summary>The cap actually in force for a tenant: granted, then tightened.</summary>
    public static int EffectiveCap(Tenant? tenant)
        => EffectiveCap(GrantingCap(tenant), tenant?.MaxProjects ?? 0);

    /// <inheritdoc cref="Allows(int,int,int)"/>
    public static bool Allows(int projectCount, Tenant? tenant)
    {
        var cap = EffectiveCap(tenant);
        return IsUnlimited(cap) || projectCount < cap;
    }

    /// <summary>
    /// Human-readable cap for an error message. Never renders a non-positive or
    /// sentinel value, which would put "Project limit (-1) reached" in front of a
    /// customer.
    /// </summary>
    public static string Label(int planCap, int columnCap)
    {
        var cap = EffectiveCap(planCap, columnCap);
        return IsUnlimited(cap) ? "unlimited" : cap.ToString();
    }
}
