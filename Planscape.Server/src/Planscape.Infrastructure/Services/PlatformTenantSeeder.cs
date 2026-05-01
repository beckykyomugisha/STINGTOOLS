using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Idempotent startup task: ensures the well-known 'planscape' platform
/// tenant exists. PlatformRevenueController gates on this slug, and
/// SlaBurnRateJob routes operational alerts to it. Without the row,
/// /api/platform/revenue 404s for every user (correct behaviour) but
/// the founder also can't see their own dashboard — so we mint it on
/// first boot.
///
/// Re-runs on every deploy; first run creates, subsequent runs no-op.
/// </summary>
public class PlatformTenantSeeder
{
    public static readonly Guid PlatformTenantId = new("00000000-0000-4000-8000-000000000001");
    public const string PlatformSlug = "planscape";

    private readonly PlanscapeDbContext _db;
    private readonly ILogger<PlatformTenantSeeder> _logger;

    public PlatformTenantSeeder(PlanscapeDbContext db, ILogger<PlatformTenantSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        _db.BypassTenantFilter = true;

        var existing = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == PlatformSlug, ct);
        if (existing != null)
        {
            // Make sure our well-known id matches what's there. If a previous
            // hand-rolled seed used a different uuid, log + leave alone — the
            // controller filters by slug, not id, so we're still wired up.
            if (existing.Id != PlatformTenantId)
                _logger.LogInformation("Platform tenant exists with id {Id} (expected {Expected}); slug-based gates still work.", existing.Id, PlatformTenantId);
            return;
        }

        var tenant = new Tenant
        {
            Id           = PlatformTenantId,
            Name         = "Planscape",
            Slug         = PlatformSlug,
            ContactEmail = "ops@planscape.app",
            Plan         = BillingPlan.Enterprise,
            Tier         = LicenseTier.Enterprise,
            Currency     = "USD",
            BillingCycle = BillingCycle.Annual,
            MaxUsers     = int.MaxValue,
            MaxProjects  = int.MaxValue,
            StorageLimitBytes = long.MaxValue,
            IsActive     = true,
            CreatedAt    = DateTime.UtcNow,
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded platform tenant 'planscape' ({Id}). Operator should add their Owner account now.", PlatformTenantId);
    }
}
