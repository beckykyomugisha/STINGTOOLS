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
        var current = axis switch
        {
            QuotaAxis.Projects     => await _db.Projects.CountAsync(ct),
            QuotaAxis.Authors      => await _db.ProjectMembers.Where(m => m.ProjectRole == "Author").Select(m => m.UserId).Distinct().CountAsync(ct),
            QuotaAxis.Coordinators => await _db.ProjectMembers.Where(m => m.ProjectRole != "Author").Select(m => m.UserId).Distinct().CountAsync(ct),
            _                      => 0,
        };
        return (limits, current);
    }

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
