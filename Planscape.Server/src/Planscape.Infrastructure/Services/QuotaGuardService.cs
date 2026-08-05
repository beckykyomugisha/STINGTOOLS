using Microsoft.EntityFrameworkCore;
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

    public async Task<QuotaResult> CheckCanAddProjectAsync(CancellationToken ct = default)
    {
        var (limits, current) = await CountAsync(QuotaAxis.Projects, ct);
        return Result(QuotaAxis.Projects, current, limits.MaxProjects);
    }

    public async Task<QuotaResult> CheckCanAddUserAsync(string projectRole, CancellationToken ct = default)
    {
        // Authors and coordinators have separate caps.
        var axis = string.Equals(projectRole, "Author", StringComparison.OrdinalIgnoreCase)
                 ? QuotaAxis.Authors : QuotaAxis.Coordinators;
        var (limits, current) = await CountAsync(axis, ct);
        var max = axis == QuotaAxis.Authors ? limits.MaxAuthors : limits.MaxCoordinators;
        return Result(axis, current, max);
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

    private async Task<(BillingPlanLimits.Limits, int)> CountAsync(QuotaAxis axis, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, ct);
        var limits = BillingPlanLimits.For(tenant?.Plan ?? BillingPlan.Trial);
        var tid = _tenantContext.TenantId;
        var current = axis switch
        {
            QuotaAxis.Projects     => await _db.Projects.CountAsync(p => p.TenantId == tid, ct),
            QuotaAxis.Authors      => await CountAuthorSeatsAsync(tid, ct),
            QuotaAxis.Coordinators => await CountUserSeatsAsync(tid, ct) - await CountAuthorSeatsAsync(tid, ct),
            _                      => 0,
        };
        return (limits, current);
    }

    /// <summary>
    /// A seat is a PERSON in the tenant, so seats are counted over
    /// <c>AppUser</c> — the row the seat-selling operations actually create.
    ///
    /// This used to count <c>ProjectMembers</c>, which meant the meter read a
    /// table the metered operations never wrote: <c>POST /api/tenant/invite</c>
    /// and <c>POST /api/onboarding/team</c> both gate on these axes and then
    /// create an <c>AppUser</c> and nothing else — no <c>ProjectMember</c> — so
    /// an accepted invite could never move the number it had just been checked
    /// against. Meanwhile the only path that does create <c>ProjectMember</c>
    /// rows (<c>ProjectMembersController</c>) never consults this guard.
    /// Counting people also makes the pre-existing <c>.Distinct()</c> on UserId
    /// unnecessary rather than merely approximate — a person on four projects
    /// was always meant to be one seat.
    ///
    /// <para><c>!u.IsDeleted</c> is written out EXPLICITLY and must stay that
    /// way. <c>AppUser</c> declares <c>HasQueryFilter(u =&gt; !u.IsDeleted)</c>
    /// in its own entity block, but <c>ApplyGlobalQueryFilters</c> runs later in
    /// <c>OnModelCreating</c> and — because EF Core 8 allows only ONE filter per
    /// entity, a second call silently replacing the first — overwrites it with
    /// the tenant predicate. <c>AppUser</c> does not implement
    /// <c>ISoftDeletable</c> either, so nothing puts the tombstone predicate
    /// back. That is documented in PlanscapeDbContext and is deliberately out of
    /// scope to fix here; the consequence for billing is that relying on the
    /// global filter would charge tenants for deleted users.</para>
    /// </summary>
    private Task<int> CountUserSeatsAsync(Guid tid, CancellationToken ct)
        => _db.Users.CountAsync(u => u.TenantId == tid && !u.IsDeleted, ct);

    /// <summary>
    /// Author seats are accounts that can author information, asked of the
    /// shared capability layer (<see cref="ProjectRoles.CanAuthorInformation"/>)
    /// rather than decided here. Billing and access therefore read the SAME
    /// source and cannot drift — two sources for one question is exactly what
    /// produced the eleven dead gates #540 repaired.
    ///
    /// <para>This deliberately does NOT key on <c>Iso19650Role</c>. That is a
    /// functional/discipline taxonomy and ISO 19650 assigns information-
    /// management responsibility, not software seats. <c>"A"</c> is the
    /// APPOINTING PARTY — the client — not "Author": keying on it counted the
    /// client as the only author (so the axis read 0 for everyone else) while
    /// <c>"BA"</c>, BIM Author, fell to the other axis entirely. It is the wrong
    /// question, not a missing code.</para>
    ///
    /// <para>The non-authoring axis is derived as <c>total - authors</c> rather
    /// than by a negated predicate: subtraction is total by construction, so no
    /// row — including one carrying a role this build has never heard of — can
    /// fall off both axes and silently hand out a free seat.</para>
    /// </summary>
    private Task<int> CountAuthorSeatsAsync(Guid tid, CancellationToken ct)
        => _db.Users
            .Where(u => u.TenantId == tid && !u.IsDeleted)
            .Where(ProjectRoles.CanAuthorInformationPredicate)
            .CountAsync(ct);

    private static QuotaResult Result(QuotaAxis axis, int current, int max)
    {
        if (max == int.MaxValue) return QuotaResult.Allow(axis, current, max);
        if (current >= max)
            return QuotaResult.Denied(axis, current, max,
                $"{axis} cap reached ({current} of {max}).");
        return QuotaResult.Allow(axis, current, max);
    }
}

public interface IQuotaGuardService
{
    Task<QuotaResult> CheckCanAddProjectAsync(CancellationToken ct = default);
    Task<QuotaResult> CheckCanAddUserAsync(string projectRole, CancellationToken ct = default);
    Task<QuotaResult> CheckCanUploadBytesAsync(long incomingBytes, CancellationToken ct = default);
}

public enum QuotaAxis { Projects, Authors, Coordinators, Storage }

public sealed record QuotaResult(bool Allowed, QuotaAxis Axis, long Current, long Max, string? Reason)
{
    public static QuotaResult Allow(QuotaAxis axis, long current, long max) => new(true, axis, current, max, null);
    public static QuotaResult Denied(QuotaAxis axis, long current, long max, string reason) => new(false, axis, current, max, reason);
}
