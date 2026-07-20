using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// The unique-index half of R4, on a provider that actually ENFORCES unique
/// indexes.
///
/// EF InMemory does not: two inserts of the same (TenantId, Code) both succeed
/// there, so the suite above cannot demonstrate the constraint that makes the
/// concurrent-redemption race real. In-memory SQLite is a genuine relational
/// provider and does enforce it.
///
/// WHY THIS IS NOT AN HTTP TEST. Booting the WebApplicationFactory against
/// SQLite is not possible: Program.cs's schema block (~line 1341) branches on
/// `db.Database.IsRelational()`, which is TRUE for SQLite, and then issues
/// `SELECT … FROM information_schema.tables` — Postgres-only DDL that SQLite
/// has no equivalent for. The block has no try/catch, so the host would fail to
/// start before a single request ran. Rather than pretend otherwise, these
/// tests exercise the exact mechanism the fix relies on — a failed insert
/// followed by detaching it — directly against the real schema.
/// </summary>
public class HandoffProvisioningSqliteTests
{
    private static PlanscapeDbContext NewContext(Microsoft.Data.Sqlite.SqliteConnection conn)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseSqlite(conn)
            .Options);

    private static (Microsoft.Data.Sqlite.SqliteConnection conn, Guid tenantId) NewDb()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);
        ctx.Database.EnsureCreated();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "Race Org", Slug = $"race-{Guid.NewGuid():N}"[..20],
            ContactEmail = "race@example.com", Tier = LicenseTier.Starter,
            MaxUsers = 5, MaxProjects = 5, IsActive = true
        };
        ctx.Tenants.Add(tenant);
        ctx.SaveChanges();
        return (conn, tenant.Id);
    }

    private static Project StarterFor(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Name = "My First Project",
        Code = "PRJ-001",
        Phase = "Design",
        Status = ProjectStatus.Active
    };

    [Fact]
    public void Sqlite_enforces_the_unique_index_that_makes_the_race_real()
    {
        var (conn, tenantId) = NewDb();
        using var _ = conn;

        using var a = NewContext(conn);
        a.Projects.Add(StarterFor(tenantId));
        a.SaveChanges();

        // The losing concurrent redemption.
        using var b = NewContext(conn);
        b.Projects.Add(StarterFor(tenantId));
        Assert.Throws<DbUpdateException>(() => b.SaveChanges());

        // Guard: if this ever stops throwing, the test above has quietly become
        // vacuous and the InMemory caveat in the class docstring is wrong.
        using var check = NewContext(conn);
        Assert.Single(check.Projects.IgnoreQueryFilters().Where(p => p.TenantId == tenantId));
    }

    [Fact]
    public void A_failed_insert_poisons_the_next_SaveChanges_until_it_is_detached()
    {
        var (conn, tenantId) = NewDb();
        using var _ = conn;

        using (var seed = NewContext(conn))
        {
            seed.Projects.Add(StarterFor(tenantId));
            seed.SaveChanges();
        }

        using var ctx = NewContext(conn);
        ctx.Projects.Add(StarterFor(tenantId));
        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());

        // An UNRELATED write on the same context — the refresh-token save that
        // follows provisioning. It fails, because the dead Project is still
        // sitting in the tracker as Added. THIS is the 500 the user saw.
        var tenant = ctx.Tenants.IgnoreQueryFilters().First(t => t.Id == tenantId);
        tenant.Name = "Renamed";
        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());

        // The fix: drop what provisioning staged…
        foreach (var e in ctx.ChangeTracker.Entries()
                     .Where(e => e.State == EntityState.Added
                                 && (e.Entity is Project || e.Entity is ProjectMember))
                     .ToList())
        {
            e.State = EntityState.Detached;
        }

        // …and the unrelated write now goes through, which is what keeps the
        // user's session alive.
        ctx.SaveChanges();

        using var verify = NewContext(conn);
        Assert.Equal("Renamed", verify.Tenants.IgnoreQueryFilters().First(t => t.Id == tenantId).Name);
        Assert.Single(verify.Projects.IgnoreQueryFilters().Where(p => p.TenantId == tenantId));
    }
}
