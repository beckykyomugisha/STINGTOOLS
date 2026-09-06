using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Planscape.API;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// The demo seeder writes <c>MaxUsers</c> on the tenant and then adds users to
/// it. Nothing tied those two numbers together.
///
/// WHAT THIS IS AND IS NOT
/// -----------------------
/// It is NOT a repair of a live overage. Measured against the running dev
/// database, the seeder accounts for **13** of the demo tenant's users
/// (1 admin + 6 lead roles + 6 per-project leads) against its own
/// <c>MaxUsers = 50</c>. It has always fitted.
///
/// The 400-user overage that once blocked <c>POST /api/admin/users</c> came
/// from <c>load/seed-loadtest-data.sql</c>, which INSERTs
/// <c>loadtest1..400@planscape.demo</c> straight into Postgres via
/// <c>generate_series</c>. No application-level guard can see that, and none of
/// it is the seeder's doing.
///
/// So this pins an invariant that currently holds, and proves the guard fires
/// when it stops holding — which is the only moment it matters.
/// </summary>
public class SeedDataUserCapTests
{
    private sealed class StartupTenant : ITenantContext
    {
        // Guid.Empty is what TenantContext really returns during startup — there
        // is no HTTP context — which is precisely why SeedData sets
        // BypassTenantFilter. Modelling it faithfully keeps the test honest.
        public Guid TenantId => Guid.Empty;
        public string TenantSlug => "";
        public LicenseTier Tier => LicenseTier.Premium;
        public bool MimEnabled => false;
    }

    private sealed class DevEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Planscape.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private static PlanscapeDbContext NewContext(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options,
               httpContextAccessor: null!, tenantContext: new StartupTenant());

    private static SqliteConnection NewOpenDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);
        ctx.Database.EnsureCreated();
        return conn;
    }

    [Fact]
    public async Task Seeding_leaves_the_demo_tenant_within_its_own_user_cap()
    {
        using var conn = NewOpenDb();
        using var db = NewContext(conn);

        await SeedData.SeedAsync(db, new DevEnvironment());

        db.BypassTenantFilter = true;
        var tenant = await db.Tenants.FirstAsync(t => t.Slug == "demo");
        var users = await db.Users.CountAsync(u => u.TenantId == tenant.Id && !u.IsDeleted);

        // Non-vacuous: a seeder that produced nothing would satisfy "<= cap"
        // while proving nothing at all.
        Assert.True(users > 0, "the seeder produced no users, so the cap assertion below is vacuous");

        Assert.True(users <= tenant.MaxUsers,
            $"the seeder left the demo tenant over its own cap: {users} users against MaxUsers = {tenant.MaxUsers}");
    }

    [Fact]
    public async Task The_guard_fails_loudly_when_the_seed_would_exceed_the_cap()
    {
        using var conn = NewOpenDb();

        // Pre-mint the demo tenant with a cap the seed cannot fit. SeedData
        // find-or-creates on Slug == "demo" (DemoSandboxJob may get there first),
        // so it adopts this row and its cap rather than writing its own.
        using (var setup = NewContext(conn))
        {
            setup.BypassTenantFilter = true;
            setup.Tenants.Add(new Tenant
            {
                Name = "Planscape Demo",
                Slug = "demo",
                Tier = LicenseTier.Premium,
                MaxUsers = 2,            // the seed adds 13
                MaxProjects = 20,
                Plan = BillingPlan.Enterprise,
            });
            await setup.SaveChangesAsync();
        }

        using var db = NewContext(conn);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SeedData.SeedAsync(db, new DevEnvironment()));

        // The message has to be actionable on its own — this fires at startup,
        // far from whoever last edited the seed list.
        Assert.Contains("over its own user cap", ex.Message);
        Assert.Contains("MaxUsers = 2", ex.Message);

        // And it must fire BEFORE the write, or it is a report of damage rather
        // than a guard against it.
        db.BypassTenantFilter = true;
        var persisted = await db.Users.CountAsync();
        Assert.Equal(0, persisted);
    }
}
