using Microsoft.EntityFrameworkCore;
using Planscape.Core;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// S1.4 — checks whether the current tenant can grow along a given axis
/// (add a user, add a project, upload another N bytes) under their
/// <see cref="BillingPlan"/> envelope. Used by the
/// <see cref="Planscape.Infrastructure.Authorization.QuotaAttribute"/>
/// filter and by controllers that want to fail early with an upsell hint
/// rather than a generic 402.
///
/// Counts are read live from the database (cheap COUNTs over indexed
/// TenantId columns added by S1.1). Could be cached in Redis with a 60-s
/// TTL once we cross firm #10 — see roadmap.
/// </summary>
public class QuotaGuardService : IQuotaGuardService
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenantContext;

    public QuotaGuardService(PlanscapeDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// The <b>single</b> project gate. <c>ProjectsController.CreateProject</c> used to
    /// carry a second one inline (<c>projectCount &gt;= tenant.MaxProjects</c>) reading
    /// a different source; two gates on one action meant the answer depended on
    /// filter-vs-body ordering rather than on policy. If another project-creating path
    /// appears, give it <c>[Quota(QuotaAxis.Projects)]</c> — do not re-add a local check.
    /// </summary>
    public async Task<QuotaResult> CheckCanAddProjectAsync(CancellationToken ct = default)
    {
        var (_, current, tenant) = await CountAsync(QuotaAxis.Projects, ct);

        // D1's tier grants where present, else the local plan; the tenant column may
        // only tighten. Resolving all three in ONE place is the point — a caller that
        // read a single source would disagree with this service and, being closer to
        // the action, would win.
        return Result(QuotaAxis.Projects, current, ProjectCeilingPolicy.EffectiveCap(tenant));
    }

    public async Task<QuotaResult> CheckCanUploadBytesAsync(long incomingBytes, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, ct);
        if (tenant == null) return QuotaResult.Denied(QuotaAxis.Storage, 0, 0, "Unknown tenant");
        var limits = BillingPlanLimits.For(tenant.Plan);
        var capBytes = limits.StorageMb * 1024L * 1024L;

        // Sum current model storage; ProjectModel.FileSizeBytes covers the
        // bulk; document attachments would be another sum once those
        // entities are billable. For v1 we only meter model storage.
        var used = await _db.ProjectModels.AsNoTracking()
            .Where(m => m.DeletedAt == null)
            .SumAsync(m => (long?)m.FileSizeBytes, ct) ?? 0;

        if (used + incomingBytes > capBytes)
            return QuotaResult.Denied(QuotaAxis.Storage, used, capBytes,
                $"Storage cap reached ({used / 1024 / 1024:N0} of {capBytes / 1024 / 1024:N0} MB)");

        return QuotaResult.Allow(QuotaAxis.Storage, used, capBytes);
    }

    /// <summary>
    /// Returns the tenant alongside its plan limits and the live count, because the
    /// caller needs BOTH sources to resolve a cap and re-querying would risk them
    /// disagreeing.
    /// </summary>
    private async Task<(BillingPlanLimits.Limits, int, Tenant?)> CountAsync(QuotaAxis axis, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, ct);
        var limits = BillingPlanLimits.For(tenant?.Plan ?? BillingPlan.Trial);
        var tid = _tenantContext.TenantId;
        var current = axis switch
        {
            QuotaAxis.Projects => await _db.Projects.CountAsync(p => p.TenantId == tid, ct),
            _                  => 0,
        };
        return (limits, current, tenant);
    }

    private static QuotaResult Result(QuotaAxis axis, int current, int max)
    {
        // Non-positive is "unlimited", not "deny everyone". A bare `current >= max`
        // here would refuse a tenant its FIRST project on a 0 or a -1 sentinel, and
        // then report the cap it refused against — which is the #616 failure in a
        // different axis. Same convention as ProjectCeilingPolicy/AccountCeilingPolicy.
        if (max <= 0 || max == int.MaxValue) return QuotaResult.Allow(axis, current, max);
        if (current >= max)
            return QuotaResult.Denied(axis, current, max,
                $"{axis} cap reached ({current} of {max}).");
        return QuotaResult.Allow(axis, current, max);
    }
}

public interface IQuotaGuardService
{
    Task<QuotaResult> CheckCanAddProjectAsync(CancellationToken ct = default);
    Task<QuotaResult> CheckCanUploadBytesAsync(long incomingBytes, CancellationToken ct = default);
}

/// <summary>
/// Authors and Coordinators are DELETED, not merely unused. Seat entitlement is
/// the StingTools licence, counted in D1 by
/// <c>marketing-site/functions/api/license/_lib/seats.ts</c> (#621).
///
/// Deleting the members rather than leaving them is the safer of the two: a
/// stray <c>[Quota(QuotaAxis.Authors)]</c> is now a COMPILE error, where an
/// unused member would have fallen to the filter's default arm and silently
/// ALLOWED — which is indistinguishable from a working cap right up until
/// someone relies on it. See #619 for what the removed arms actually counted.
/// </summary>
public enum QuotaAxis { Projects, Storage }

public sealed record QuotaResult(bool Allowed, QuotaAxis Axis, long Current, long Max, string? Reason)
{
    public static QuotaResult Allow(QuotaAxis axis, long current, long max) => new(true, axis, current, max, null);
    public static QuotaResult Denied(QuotaAxis axis, long current, long max, string reason) => new(false, axis, current, max, reason);
}
