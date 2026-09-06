using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Controllers;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// The third capability — CanAdministerProject — and the dead gate it replaces.
///
/// WHAT WAS WRONG
/// --------------
/// ProjectSettingsController.UpdateSettings required
/// <c>member.Iso19650Role == "K" || == "C"</c>. Neither code is assignable.
/// <c>GET api/projects/{id}/members/roles</c> — the vocabulary this same server
/// serves, and the one the web grid saves from — offers
/// A/PM/BC/BA/AR/SE/ME/CE/QS/CA/CT/SC/FM/OM/CL/M/V/Z. No K. No C. Those two
/// belong to a THIRD list, on AppUser.Iso19650Role (AppUser.cs:14), so the check
/// was written against one column and applied to another.
///
/// Measured 2026-08-18 on the local stack: zero of 34 ProjectMember rows carry
/// K or C — and zero AppUser rows carry them either. So it was never a column
/// mix-up that used to work for somebody. Nobody could edit project settings.
///
/// THIS IS A WIDENING, AND IT IS TESTED AS ONE.
/// <see cref="The_replaced_gate_matched_nobody"/> runs the old predicate against
/// the same fixture, so the "widens from NOBODY" claim in the PR is a test
/// result rather than a sentence.
///
/// FIXTURE TRAP THIS FILE AVOIDS
/// -----------------------------
/// The PlanscapeDbContext global filter is `TenantId == CurrentTenantId`, which
/// falls back to Guid.Empty with no ITenantContext — matching NO rows, so every
/// count assertion would pass against an empty set. Contexts are always built
/// with a tenant, identifiers are minted per fixture (never the shared
/// well-known GUID that made an earlier suite fail non-deterministically), and
/// <see cref="Sanity_the_fixture_actually_has_rows"/> proves non-empty first.
///
/// SQLite, not EF InMemory: the EF-translation test is meaningless on a provider
/// that cannot produce SQL.
/// </summary>
public class ProjectAdministerCapabilityTests
{
    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "acme";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    /// <summary>
    /// (projectRole, iso19650Role) seeded as real rows.
    ///
    /// "dead-gate-K" and "dead-gate-C" are deliberately present even though no
    /// UI can produce them: they are the values the replaced check tested, so
    /// their presence is what lets this file prove the old gate matched nobody
    /// except them, and that the new one does not silently inherit them.
    /// </summary>
    private static readonly (string Project, string Iso, string Label)[] Seed =
    {
        ("Manager",     "M",  "manager"),
        ("Owner",       "M",  "owner"),
        ("Coordinator", "M",  "coordinator"),
        ("Contributor", "PM", "contributor-who-is-the-iso-PM"),
        ("Contributor", "A",  "contributor-who-is-appointing-party"),
        ("Contributor", "BC", "contributor-who-is-bim-coordinator"),
        ("Contributor", "M",  "plain-contributor"),
        ("Viewer",      "V",  "viewer"),
        ("Contributor", "K",  "dead-gate-K"),
        ("Contributor", "C",  "dead-gate-C"),
    };

    private sealed class Fixture : IDisposable
    {
        public required SqliteConnection Conn { get; init; }
        public required PlanscapeDbContext Db { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid ProjectId { get; init; }
        public required Dictionary<string, Guid> Users { get; init; }
        public Guid User(string label) => Users[label];
        public void Dispose() { Db.Dispose(); Conn.Dispose(); }
    }

    private static Fixture NewDb()
    {
        var tenantId  = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var authorId  = Guid.NewGuid();
        var users     = new Dictionary<string, Guid>();

        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var db = new PlanscapeDbContext(
            new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options,
            httpContextAccessor: null!, tenantContext: new FixedTenant(tenantId));
        db.Database.EnsureCreated();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}"[..16],
            ContactEmail = "a@example.com", Tier = LicenseTier.Professional,
            Plan = BillingPlan.Studio, MaxUsers = 50, MaxProjects = 50,
        });

        db.Users.Add(new AppUser
        {
            Id = authorId, TenantId = tenantId,
            Email = $"author-{Guid.NewGuid():N}@example.com",
            DisplayName = "author", PasswordHash = "x", IsActive = true,
        });

        db.Projects.Add(new Project
        {
            Id = projectId, TenantId = tenantId, Name = "Kampala Temple",
            Code = $"P-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            CreatedById = authorId,
        });

        foreach (var (projectRole, iso, label) in Seed)
        {
            var uid = Guid.NewGuid();
            users[label] = uid;
            db.Users.Add(new AppUser
            {
                Id = uid, TenantId = tenantId,
                Email = $"{label}-{Guid.NewGuid():N}@example.com",
                DisplayName = label, PasswordHash = "x", IsActive = true,
            });
            db.ProjectMembers.Add(new ProjectMember
            {
                Id = Guid.NewGuid(), TenantId = tenantId,
                ProjectId = projectId, UserId = uid,
                ProjectRole = projectRole, Iso19650Role = iso, IsActive = true,
            });
        }

        db.SaveChanges();
        return new Fixture
        {
            Conn = conn, Db = db, TenantId = tenantId,
            ProjectId = projectId, Users = users,
        };
    }

    // ── Fixture sanity — green before anything below means anything ─────────

    [Fact]
    public void Sanity_the_fixture_actually_has_rows()
    {
        using var f = NewDb();
        Assert.Equal(Seed.Length, f.Db.ProjectMembers.Count(m => m.ProjectId == f.ProjectId));
        Assert.True(f.Db.Projects.Any(p => p.Id == f.ProjectId));
    }

    // ── The claim the PR body makes, as a test ──────────────────────────────

    /// <summary>
    /// The predicate the fix removes, run against a fixture containing a
    /// Manager, an Owner, a Coordinator and three ISO office-holders. It selects
    /// NONE of them. This is the "widens access from NOBODY" claim, measured
    /// rather than asserted in prose.
    ///
    /// The only rows it does select are the two seeded solely to prove the
    /// point — values no UI can write, and which the measurement found zero of.
    /// </summary>
    [Fact]
    public void The_replaced_gate_matched_nobody()
    {
        using var f = NewDb();

        var assignable = f.Db.ProjectMembers
            .Where(m => m.ProjectId == f.ProjectId && m.IsActive)
            .Where(m => m.Iso19650Role != "K" && m.Iso19650Role != "C")
            .ToList();
        Assert.NotEmpty(assignable);   // guards the empty-set trap

        // The old gate, verbatim, over every assignable row.
        var passedOldGate = assignable
            .Where(m => m.Iso19650Role == "K" || m.Iso19650Role == "C")
            .ToList();
        Assert.Empty(passedOldGate);

        // ... and the people the new gate admits.
        var passedNewGate = assignable
            .Where(m => ProjectRoles.CanAdministerProject(m.ProjectRole, m.Iso19650Role))
            .Select(m => m.ProjectRole + "/" + m.Iso19650Role)
            .OrderBy(x => x)
            .ToList();
        Assert.Equal(
            new[] { "Contributor/A", "Contributor/BC", "Contributor/PM", "Manager/M", "Owner/M" },
            passedNewGate);
    }

    /// <summary>
    /// K and C are not silently inherited. They were never legitimate values on
    /// this column, and carrying them forward would preserve the wrong-column
    /// bug in a new place.
    /// </summary>
    [Theory]
    [InlineData("K")]
    [InlineData("C")]
    public void The_dead_gate_codes_confer_nothing_on_their_own(string iso)
        => Assert.False(ProjectRoles.CanAdministerProject("Contributor", iso));

    // ── The EF predicate — the one that can silently client-evaluate ────────

    [Fact]
    public void AdministerProject_predicate_translates_to_SQL()
    {
        using var f = NewDb();
        var query = f.Db.ProjectMembers
            .Where(m => m.ProjectId == f.ProjectId && m.IsActive)
            .Where(ProjectRoles.CanAdministerProjectPredicate);

        // Client evaluation would pull the whole table and still return the
        // right answer here, so a correctness assertion alone cannot catch it.
        // The generated SQL must carry the comparison.
        var sql = query.ToQueryString();
        Assert.Contains("ProjectRole",  sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Iso19650Role", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdministerProject_predicate_selects_the_right_members_from_the_database()
    {
        using var f = NewDb();
        var rows = f.Db.ProjectMembers
            .Where(m => m.ProjectId == f.ProjectId && m.IsActive)
            .Where(ProjectRoles.CanAdministerProjectPredicate)
            .Select(m => m.ProjectRole + "/" + m.Iso19650Role)
            .OrderBy(x => x)
            .ToList();

        Assert.NotEmpty(rows);
        Assert.Equal(
            new[] { "Contributor/A", "Contributor/BC", "Contributor/PM", "Manager/M", "Owner/M" },
            rows);
    }

    /// <summary>
    /// The expression and the in-memory helper are two encodings of one rule.
    /// A reader cannot verify by eye that they agree; this can.
    /// </summary>
    [Fact]
    public void Expression_and_in_memory_forms_agree_on_every_seeded_row()
    {
        using var f = NewDb();
        var all = f.Db.ProjectMembers.Where(m => m.ProjectId == f.ProjectId).ToList();
        Assert.NotEmpty(all);

        var fromSql = f.Db.ProjectMembers
            .Where(m => m.ProjectId == f.ProjectId)
            .Where(ProjectRoles.CanAdministerProjectPredicate)
            .Select(m => m.UserId).ToHashSet();
        var fromMemory = all
            .Where(m => ProjectRoles.CanAdministerProject(m.ProjectRole, m.Iso19650Role))
            .Select(m => m.UserId).ToHashSet();

        Assert.NotEmpty(fromSql);
        Assert.Equal(fromMemory, fromSql);
    }

    // ── In-memory matrix ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Manager",     "M",  true)]
    [InlineData("Owner",       "M",  true)]
    [InlineData("Admin",       "M",  true)]
    [InlineData("manager",     "m",  true)]   // case-insensitive
    // The wrong-column bug, fixed: the ISO code is read off the ISO field.
    [InlineData("Contributor", "PM", true)]
    [InlineData("Contributor", "A",  true)]
    [InlineData("Contributor", "BC", true)]
    // Denied.
    [InlineData("Contributor", "M",  false)]
    [InlineData("Viewer",      "V",  false)]
    [InlineData("ClientGuest", "M",  false)]
    // Coordinator CURATES but does not ADMINISTER. Deliberate: curation is
    // organising albums and checklists; administration rewrites the rules the
    // project deliverables are validated against.
    [InlineData("Coordinator", "M",  false)]
    public void Administer_matrix(string projectRole, string iso, bool expected)
        => Assert.Equal(expected, ProjectRoles.CanAdministerProject(projectRole, iso));

    [Fact]
    public void Coordinator_curates_but_does_not_administer()
    {
        Assert.True (ProjectRoles.CanCurate("Coordinator", "M"));
        Assert.False(ProjectRoles.CanAdministerProject("Coordinator", "M"));
    }

    [Fact]
    public void Tenant_admin_short_circuits_the_capability()
        => Assert.True(ProjectRoles.CanAdministerProject("Viewer", "V", isTenantAdmin: true));

    // ── The endpoint gate, end to end ───────────────────────────────────────

    private static ProjectSettingsController NewSettingsController(
        Fixture f, Guid userId, string? tenantRole = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("user_id", userId.ToString()),
            new("tenant_id", f.TenantId.ToString()),
        };
        if (tenantRole != null)
        {
            claims.Add(new Claim("role", tenantRole));
            claims.Add(new Claim(ClaimTypes.Role, tenantRole));
        }

        // config is only read on the GET path; null here means an accidental
        // future dependency on it fails loudly rather than passing silently.
        return new ProjectSettingsController(f.Db, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                },
            },
        };
    }

    private static Dictionary<string, object?> Body()
        => new() { ["enforceIso19650Naming"] = true };

    [Theory]
    [InlineData("manager")]
    [InlineData("owner")]
    [InlineData("contributor-who-is-the-iso-PM")]
    [InlineData("contributor-who-is-appointing-party")]
    [InlineData("contributor-who-is-bim-coordinator")]
    public async Task UpdateSettings_now_admits_a_project_administrator(string label)
    {
        using var f = NewDb();
        var controller = NewSettingsController(f, f.User(label));

        var result = await controller.UpdateSettings(f.ProjectId, Body());

        Assert.IsNotType<ForbidResult>(result);
        Assert.IsNotType<NotFoundResult>(result);
        // The write actually landed. A gate that admits but does nothing is the
        // same failure wearing a different hat.
        Assert.True(f.Db.Projects.Single(p => p.Id == f.ProjectId).EnforceIso19650Naming);
    }

    [Theory]
    [InlineData("plain-contributor")]
    [InlineData("viewer")]
    [InlineData("coordinator")]
    public async Task UpdateSettings_still_refuses_a_member_without_the_capability(string label)
    {
        using var f = NewDb();
        var before = f.Db.Projects.Single(p => p.Id == f.ProjectId).EnforceIso19650Naming;
        var controller = NewSettingsController(f, f.User(label));

        var result = await controller.UpdateSettings(f.ProjectId, Body());

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(before, f.Db.Projects.Single(p => p.Id == f.ProjectId).EnforceIso19650Naming);
    }

    /// <summary>
    /// A tenant Owner with NO ProjectMember row. The replaced check looked up a
    /// member row first and returned Forbid when it found none, so a tenant
    /// Owner was refused their own tenant's project settings. The capability
    /// layer resolves the tenant claim before touching the table — behaviour
    /// every other capability site already had.
    /// </summary>
    [Fact]
    public async Task UpdateSettings_admits_a_tenant_owner_with_no_member_row()
    {
        using var f = NewDb();
        var strangerId = Guid.NewGuid();
        f.Db.Users.Add(new AppUser
        {
            Id = strangerId, TenantId = f.TenantId,
            Email = $"owner-{Guid.NewGuid():N}@example.com",
            DisplayName = "tenant-owner", PasswordHash = "x", IsActive = true,
        });
        f.Db.SaveChanges();
        Assert.False(f.Db.ProjectMembers.Any(m => m.UserId == strangerId));

        var controller = NewSettingsController(f, strangerId, tenantRole: "Owner");
        var result = await controller.UpdateSettings(f.ProjectId, Body());

        Assert.IsNotType<ForbidResult>(result);
        Assert.True(f.Db.Projects.Single(p => p.Id == f.ProjectId).EnforceIso19650Naming);
    }

    /// <summary>
    /// A user with no relationship to the project at all. Guards against the
    /// capability being so wide it stops being a gate.
    /// </summary>
    [Fact]
    public async Task UpdateSettings_refuses_a_stranger()
    {
        using var f = NewDb();
        var controller = NewSettingsController(f, Guid.NewGuid());
        var result = await controller.UpdateSettings(f.ProjectId, Body());
        Assert.IsType<ForbidResult>(result);
    }
}
