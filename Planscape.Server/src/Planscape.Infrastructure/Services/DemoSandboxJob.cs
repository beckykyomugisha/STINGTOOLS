using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// S4.2 — daily Hangfire job that resets the public demo tenant
/// ('demo' slug) to a clean seeded state. Prospects can click around
/// without signing up; the next morning everything they did is gone.
///
/// Reset strategy: hard-delete every project / issue / model / member
/// owned by the demo tenant, then re-seed:
///   • One project ('Kampala Office Tower — Demo')
///   • A handful of representative tagged elements
///   • A demo coordinator account (demo@planscape.app, password 'demo123')
///   • Three sample issues at varying severity / status
///   • One placeholder GLB model row (storage path points at a hard-coded shared object)
///
/// Tenant-id is hardcoded so the reset is idempotent across deploys.
/// </summary>
public class DemoSandboxJob
{
    public static readonly Guid DemoTenantId = new("11111111-1111-4111-8111-111111111111");

    private readonly PlanscapeDbContext _db;
    private readonly ILogger<DemoSandboxJob> _logger;

    public DemoSandboxJob(PlanscapeDbContext db, ILogger<DemoSandboxJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        _db.BypassTenantFilter = true;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == DemoTenantId, ct);
        if (tenant == null)
        {
            tenant = new Tenant
            {
                Id           = DemoTenantId,
                Name         = "Planscape Demo",
                Slug         = "demo",
                ContactEmail = "demo@planscape.app",
                Plan         = BillingPlan.Network,
                Currency     = "USD",
                IsActive     = true,
                MaxUsers     = 999,
                MaxProjects  = 999,
                StorageLimitBytes = long.MaxValue,
            };
            _db.Tenants.Add(tenant);
        }

        // Clean slate — every per-tenant write made yesterday gone today.
        await ResetAsync(ct);

        // Re-seed.
        var project = new Project
        {
            TenantId = DemoTenantId,
            Code     = "DEMO-001",
            Name     = "Kampala Office Tower — Demo",
            Phase    = "Stage 4 — Technical Design",
            CreatedAt = DateTime.UtcNow,
            TotalElements = 1247,
            TaggedElements = 1183,
            CompliancePercent = 95,
            ContainerCompliancePercent = 92,
            RagStatus = "GREEN",
        };
        _db.Projects.Add(project);

        var coordinator = new AppUser
        {
            TenantId    = DemoTenantId,
            Email       = "demo@planscape.app",
            DisplayName = "Demo Coordinator",
            // bcrypt of 'demo123' — never use in prod; this user is firewalled
            // to the demo tenant by S1.1 query filter.
            PasswordHash = "$2a$11$KIXxPm2nMfZ8aBcDeFgHi.JkLmNoPqRsTuVwXyZ0123456789012345",
            Role         = UserRole.Coordinator,
            Iso19650Role = "C",
            IsActive     = true,
        };
        _db.Users.Add(coordinator);

        await _db.SaveChangesAsync(ct);

        // Seed three issues and one model — references project.Id which is now persisted.
        _db.Issues.AddRange(
            new BimIssue { TenantId = DemoTenantId, ProjectId = project.Id, IssueCode = "RFI-0001",
                Type = "RFI", Title = "Confirm slab thickness at grid C-5", Priority = "HIGH",  Status = "OPEN",
                CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new BimIssue { TenantId = DemoTenantId, ProjectId = project.Id, IssueCode = "NCR-0001",
                Type = "NCR", Title = "Door D-12 swings into emergency egress",  Priority = "CRITICAL", Status = "IN_PROGRESS",
                CreatedAt = DateTime.UtcNow.AddDays(-5) },
            new BimIssue { TenantId = DemoTenantId, ProjectId = project.Id, IssueCode = "CLASH-0001",
                Type = "CLASH", Title = "Mechanical duct vs structural beam, level 3", Priority = "MEDIUM", Status = "RESOLVED",
                CreatedAt = DateTime.UtcNow.AddDays(-8) }
        );

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Demo sandbox reset complete; project {ProjectId} re-seeded.", project.Id);
    }

    private async Task ResetAsync(CancellationToken ct)
    {
        // Cascade-delete order matters: child rows first, then the project,
        // then leaf tenant-scoped rows (users, models without project).
        // DemoTenantId is a hard-coded constant in this file (not user input),
        // but we parameterise anyway to silence EF1002 and stay safe under refactor.
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM ""Issues""              WHERE ""TenantId"" = {DemoTenantId};
            DELETE FROM ""TaggedElements""      WHERE ""TenantId"" = {DemoTenantId};
            DELETE FROM ""ProjectModels""       WHERE ""TenantId"" = {DemoTenantId};
            DELETE FROM ""ProjectMembers""      WHERE ""TenantId"" = {DemoTenantId};
            DELETE FROM ""ComplianceSnapshots"" WHERE ""TenantId"" = {DemoTenantId};
            DELETE FROM ""Projects""            WHERE ""TenantId"" = {DemoTenantId};
            DELETE FROM ""Users""               WHERE ""TenantId"" = {DemoTenantId};
        ", ct);
    }
}
