using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// The capability layer that replaced eleven `ProjectRole == "PM"` gates.
///
/// WHAT WAS WRONG
/// --------------
/// `ProjectRole` and `Iso19650Role` carry different vocabularies, and each
/// documents its own (ProjectMember.cs:15 and :18). "PM" only ever appears in
/// the ISO 19650 list. Eleven sites read that code off the ProjectRole column,
/// so the gates matched essentially nobody — a wrong-field bug, not a data
/// migration.
///
/// FIXTURE TRAP THIS FILE AVOIDS
/// -----------------------------
/// PlanscapeDbContext's global filter is `TenantId == CurrentTenantId`, which
/// falls back to Guid.Empty when no ITenantContext is supplied — matching NO
/// rows. A fixture on the parameterless ctor makes every count assertion pass
/// against an empty set. So the context here is always built with a tenant, and
/// <see cref="Sanity_the_fixture_actually_has_rows"/> asserts non-empty before
/// any capability claim is made.
///
/// SQLite, not EF InMemory: the EF-translation test below is meaningless on a
/// provider that cannot produce SQL.
/// </summary>
public class ProjectRoleCapabilityTests
{
    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "acme";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    private static PlanscapeDbContext NewContext(SqliteConnection conn, Guid tenantId)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options,
               httpContextAccessor: null!, tenantContext: new FixedTenant(tenantId));

    /// <summary>(projectRole, iso19650Role) pairs seeded as real rows.</summary>
    private static readonly (string Project, string Iso, string Label)[] Seed =
    {
        ("Manager",     "M",  "manager"),
        ("Contributor", "PM", "contributor-who-is-the-iso-PM"),
        ("Contributor", "M",  "plain-contributor"),
        ("Coordinator", "M",  "coordinator"),
        ("Viewer",      "V",  "viewer"),
    };

    private static (SqliteConnection conn, Guid tenantId, Guid projectId) NewDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        using var ctx = NewContext(conn, tenantId);
        ctx.Database.EnsureCreated();
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}"[..16],
            ContactEmail = "a@example.com", Tier = LicenseTier.Professional,
            Plan = BillingPlan.Studio, MaxUsers = 50, MaxProjects = 50,
        });
        ctx.Projects.Add(new Project
        {
            Id = projectId, TenantId = tenantId, Name = "Tower",
            Code = $"P-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
        });

        foreach (var (projectRole, iso, label) in Seed)
        {
            var userId = Guid.NewGuid();
            ctx.Users.Add(new AppUser
            {
                Id = userId, TenantId = tenantId,
                Email = $"{label}-{Guid.NewGuid():N}@example.com",
                DisplayName = label, PasswordHash = "x", IsActive = true,
            });
            ctx.ProjectMembers.Add(new ProjectMember
            {
                TenantId = tenantId, ProjectId = projectId, UserId = userId,
                ProjectRole = projectRole, Iso19650Role = iso, IsActive = true,
            });
        }
        ctx.SaveChanges();
        return (conn, tenantId, projectId);
    }

    [Fact]
    public void Sanity_the_fixture_actually_has_rows()
    {
        var (conn, tenantId, projectId) = NewDb();
        using (conn)
        {
            using var ctx = NewContext(conn, tenantId);
            // If the tenant filter were mis-wired this would be 0 and every
            // other test in this file would pass vacuously.
            Assert.Equal(Seed.Length, ctx.ProjectMembers.Count(m => m.ProjectId == projectId));
        }
    }

    // ── The EF predicate — this is the one that can silently client-evaluate ──

    [Fact]
    public void CurateProject_predicate_translates_to_SQL()
    {
        var (conn, tenantId, projectId) = NewDb();
        using (conn)
        {
            using var ctx = NewContext(conn, tenantId);
            var query = ctx.ProjectMembers
                .Where(m => m.ProjectId == projectId && m.IsActive)
                .Where(ProjectRoles.CanCurateProject);

            // If EF cannot translate the predicate it falls back to client
            // evaluation, which in prod pulls the whole table per notification.
            // The generated SQL must therefore carry the role comparison.
            var sql = query.ToQueryString();
            Assert.Contains("ProjectRole", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Iso19650Role", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CurateProject_predicate_selects_the_right_members_from_the_database()
    {
        var (conn, tenantId, projectId) = NewDb();
        using (conn)
        {
            using var ctx = NewContext(conn, tenantId);
            var rows = ctx.ProjectMembers
                .Where(m => m.ProjectId == projectId && m.IsActive)
                .Where(ProjectRoles.CanCurateProject)
                .Select(m => m.ProjectRole + "/" + m.Iso19650Role)
                .OrderBy(x => x)
                .ToList();

            Assert.NotEmpty(rows);                       // guards the empty-set trap
            Assert.Equal(
                new[] { "Contributor/PM", "Coordinator/M", "Manager/M" },
                rows);
        }
    }

    [Fact]
    public void ApproveSitePhotos_predicate_is_narrower_than_curate()
    {
        var (conn, tenantId, projectId) = NewDb();
        using (conn)
        {
            using var ctx = NewContext(conn, tenantId);
            var rows = ctx.ProjectMembers
                .Where(m => m.ProjectId == projectId && m.IsActive)
                .Where(ProjectRoles.CanApproveSitePhotosPredicate)
                .Select(m => m.ProjectRole + "/" + m.Iso19650Role)
                .OrderBy(x => x)
                .ToList();

            Assert.NotEmpty(rows);
            // Coordinator curates but does NOT approve — that is the split.
            Assert.Equal(new[] { "Contributor/PM", "Manager/M" }, rows);
        }
    }

    // ── In-memory form, which must agree with the expression form ────────────

    [Theory]
    // A REAL member can now pass a gate that previously matched nobody.
    [InlineData("Manager",     "M",  true,  true)]
    [InlineData("Owner",       "M",  true,  true)]
    [InlineData("Admin",       "M",  true,  true)]
    // Curates but cannot approve — the deliberate split.
    [InlineData("Coordinator", "M",  true,  false)]
    // The wrong-column bug, fixed: the ISO code is read off the right field.
    [InlineData("Contributor", "PM", true,  true)]
    [InlineData("Contributor", "A",  true,  true)]
    [InlineData("Contributor", "BC", true,  false)]   // BC curates, does not approve
    // Plain members stay denied.
    [InlineData("Contributor", "M",  false, false)]
    [InlineData("Viewer",      "V",  false, false)]
    [InlineData("ClientGuest", "M",  false, false)]
    public void Capability_matrix(string projectRole, string iso, bool curate, bool approve)
    {
        Assert.Equal(curate,  ProjectRoles.CanCurate(projectRole, iso));
        Assert.Equal(approve, ProjectRoles.CanApproveSitePhotos(projectRole, iso));
    }

    [Fact]
    public void Tenant_admin_short_circuits_both_capabilities()
    {
        Assert.True(ProjectRoles.CanCurate("Viewer", "V", isTenantAdmin: true));
        Assert.True(ProjectRoles.CanApproveSitePhotos("Viewer", "V", isTenantAdmin: true));
    }

    /// <summary>
    /// The expression and the in-memory helper are two encodings of one rule.
    /// This asserts they cannot drift, which a reader cannot verify by eye.
    /// </summary>
    [Fact]
    public void Expression_and_in_memory_forms_agree_on_every_seeded_row()
    {
        var (conn, tenantId, projectId) = NewDb();
        using (conn)
        {
            using var ctx = NewContext(conn, tenantId);
            var all = ctx.ProjectMembers.Where(m => m.ProjectId == projectId).ToList();
            Assert.NotEmpty(all);

            var curateSql = ctx.ProjectMembers
                .Where(m => m.ProjectId == projectId)
                .Where(ProjectRoles.CanCurateProject)
                .Select(m => m.UserId).ToHashSet();
            var curateMem = all.Where(m => ProjectRoles.CanCurate(m.ProjectRole, m.Iso19650Role))
                               .Select(m => m.UserId).ToHashSet();
            Assert.Equal(curateMem, curateSql);

            var approveSql = ctx.ProjectMembers
                .Where(m => m.ProjectId == projectId)
                .Where(ProjectRoles.CanApproveSitePhotosPredicate)
                .Select(m => m.UserId).ToHashSet();
            var approveMem = all.Where(m => ProjectRoles.CanApproveSitePhotos(m.ProjectRole, m.Iso19650Role))
                                .Select(m => m.UserId).ToHashSet();
            Assert.Equal(approveMem, approveSql);
        }
    }

    // ── Widening property ────────────────────────────────────────────────────

    /// <summary>
    /// The change promises nobody LOSES access. The old gate was
    /// `ProjectRole == "PM"`; ProjectRole was unvalidated before this change, so
    /// such a row was possible even though no UI writes it. It must still pass.
    /// </summary>
    [Fact]
    public void A_legacy_row_with_ProjectRole_PM_still_passes_both_gates()
    {
        Assert.True(ProjectRoles.CanCurate("PM", "M"));
        Assert.True(ProjectRoles.CanApproveSitePhotos("PM", "M"));
    }

    // ── Canonical vocabulary ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Contributor")]
    [InlineData("Coordinator")]
    [InlineData("Manager")]
    [InlineData("Owner")]
    [InlineData("Admin")]
    [InlineData("ClientGuest")]
    [InlineData("manager")]              // case-insensitive
    public void Canonical_roles_are_accepted(string role)
        => Assert.True(ProjectRoles.IsCanonical(role));

    [Theory]
    [InlineData("Wizard")]
    [InlineData("PM")]                   // an Iso19650Role code, not a ProjectRole
    [InlineData("Author")]               // read by QuotaGuardService, never written
    [InlineData("")]
    [InlineData(null)]
    public void Non_canonical_roles_are_rejected(string? role)
        => Assert.False(ProjectRoles.IsCanonical(role));
}
