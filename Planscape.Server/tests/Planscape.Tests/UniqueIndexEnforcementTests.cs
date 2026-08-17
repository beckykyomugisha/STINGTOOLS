using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// R1 (Phase A, Increment 2b) — the DB-level backstop. EF InMemory does NOT
/// enforce unique indexes, so these use in-memory SQLite (a real relational
/// provider) to prove the new UNIQUE (ProjectId, IfcGlobalId) index actually
/// rejects a duplicate at the database — the guarantee that, together with the
/// GlobalId-first upsert precedence, keeps one row per physical element. The
/// production Postgres DDL (incl. the guarded patcher conversion) is exercised by
/// the PLANSCAPE_TEST_PG-gated Postgres tests.
/// </summary>
public class UniqueIndexEnforcementTests
{
    private static PlanscapeDbContext NewContext(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options);

    private static (SqliteConnection conn, Guid tenantId, Guid projectId) NewDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);
        ctx.Database.EnsureCreated();      // materialises the model — incl. the unique index

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "Org", Slug = $"o-{Guid.NewGuid():N}"[..18],
            ContactEmail = "o@e.com", Tier = LicenseTier.Starter, MaxUsers = 5, MaxProjects = 5, IsActive = true,
        };
        var project = new Project
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "P", Code = "PRJ-1",
            Phase = "Design", Status = ProjectStatus.Active,
        };
        ctx.Tenants.Add(tenant);
        ctx.Projects.Add(project);
        ctx.SaveChanges();
        return (conn, tenant.Id, project.Id);
    }

    private static TaggedElement El(Guid tid, Guid pid, long revitId, string uid, string? gid)
        => new()
        {
            Id = Guid.NewGuid(), TenantId = tid, ProjectId = pid,
            RevitElementId = revitId, UniqueId = uid, IfcGlobalId = gid, Tag1 = "A", Disc = "A",
        };

    [Fact]
    public void UniqueIndex_RejectsDuplicateIfcGlobalIdPerProject()
    {
        var (conn, tid, pid) = NewDb();
        using (conn)
        {
            using var db = NewContext(conn);
            db.TaggedElements.Add(El(tid, pid, 1, "u1", "GID000000000000000001"));
            db.SaveChanges();

            db.TaggedElements.Add(El(tid, pid, 2, "u2", "GID000000000000000001")); // same (project, gid)
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
    }

    [Fact]
    public void UniqueIndex_AllowsManyRowsWithNullIfcGlobalId()
    {
        // The index is FILTERED (WHERE IfcGlobalId IS NOT NULL): a not-yet-keyed
        // row must never be blocked, or legacy/unstabilised elements can't sync.
        var (conn, tid, pid) = NewDb();
        using (conn)
        {
            using var db = NewContext(conn);
            db.TaggedElements.Add(El(tid, pid, 1, "u1", null));
            db.TaggedElements.Add(El(tid, pid, 2, "u2", null));
            db.SaveChanges();   // must NOT throw
            Assert.Equal(2, db.TaggedElements.IgnoreQueryFilters().Count());
        }
    }

    [Fact]
    public void UniqueIndex_IsScopedPerProject()
    {
        // Same GlobalId in two different projects is legitimate (federation) — the
        // index key is (ProjectId, IfcGlobalId), not IfcGlobalId alone.
        var (conn, tid, pid1) = NewDb();
        using (conn)
        {
            using var db = NewContext(conn);
            var project2 = new Project
            {
                Id = Guid.NewGuid(), TenantId = tid, Name = "P2", Code = "PRJ-2",
                Phase = "Design", Status = ProjectStatus.Active,
            };
            db.Projects.Add(project2);
            db.TaggedElements.Add(El(tid, pid1, 1, "u1", "GID000000000000000001"));
            db.TaggedElements.Add(El(tid, project2.Id, 1, "u2", "GID000000000000000001"));
            db.SaveChanges();   // must NOT throw — different projects
            Assert.Equal(2, db.TaggedElements.IgnoreQueryFilters().Count(e => e.IfcGlobalId == "GID000000000000000001"));
        }
    }
}
