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
/// GET api/projects/{projectId}/members/capabilities.
///
/// The endpoint exists so three surfaces stop re-deriving authority from
/// `projectRole` / `iso19650Role`. Re-implementing one rule in three clients is
/// how the eleven dead `ProjectRole == "PM"` gates happened.
///
/// THE CONTRACT UNDER TEST
/// -----------------------
/// 404 means EVERY capability is false. Not a 403, and not a 200 carrying an
/// all-false body — a body implies the server knows the answer, and absence of
/// the project must be indistinguishable from absence of authority. Asserted
/// directly, because a fail-open default here would undo the gate on all three
/// surfaces at once.
///
/// FIXTURE TRAP THIS FILE AVOIDS (1) — the empty-set pass
/// -----------------------------------------------------
/// PlanscapeDbContext's global filter is `TenantId == CurrentTenantId`, falling
/// back to Guid.Empty with no ITenantContext — matching NO rows, so assertions
/// pass vacuously. Every context here is built WITH a tenant, and
/// <see cref="Sanity_the_fixture_actually_has_rows"/> proves the rows are
/// visible before any capability claim is made.
///
/// FIXTURE TRAP THIS FILE AVOIDS (2) — the shared well-known tenant
/// ---------------------------------------------------------------
/// PlanscapeWebApplicationFactory.cs:374 seeds its shared, process-wide EF
/// InMemory store with tenant 11111111-1111-1111-1111-111111111111. This class
/// originally reused that GUID. The result was a suite that failed
/// non-deterministically (46-120 failures) in HTTP test classes that never
/// touch this file, while every class still passed in isolation — the worst
/// possible signal. Identifiers are minted per fixture instead, which is what
/// ProjectRoleCapabilityTests already does. Do not reintroduce a fixed GUID.
/// </summary>
public class ProjectCapabilitiesEndpointTests
{
    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "acme";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    /// <summary>(projectRole, iso19650Role) seeded as real rows, one user each.</summary>
    private static readonly (string Project, string Iso, string Label)[] Seed =
    {
        ("Manager",     "M",  "manager"),
        ("Coordinator", "M",  "coordinator"),
        ("Contributor", "PM", "contributor-who-is-the-iso-PM"),
        ("Contributor", "M",  "plain-contributor"),
        ("Viewer",      "V",  "viewer"),
    };

    private sealed class Fixture : IDisposable
    {
        public required SqliteConnection Conn { get; init; }
        public required PlanscapeDbContext Db { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid ProjectId { get; init; }
        public required Dictionary<string, Guid> Users { get; init; }

        public Guid User(string label) => Users[label];

        public void Dispose()
        {
            Db.Dispose();
            Conn.Dispose();
        }
    }

    private static Fixture NewDb()
    {
        // Fresh every time — see the class docstring for why this must not be a
        // constant.
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

        // Tenant first: Project.TenantId and AppUser.TenantId are real FKs, and
        // SQLite (unlike EF InMemory) enforces them.
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}"[..16],
            ContactEmail = "a@example.com", Tier = LicenseTier.Professional,
            Plan = BillingPlan.Studio, MaxUsers = 50, MaxProjects = 50,
        });

        // The author is a real user: Project.CreatedById is an FK, and
        // author-visibility is one of the branches WhereVisibleTo takes.
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
            CreatedById = authorId, PurgeAfter = null,
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

    private static ProjectMembersController NewController(
        Fixture f, Guid userId, string? tenantRole = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("tenant_id", f.TenantId.ToString()),
        };
        if (tenantRole != null)
        {
            claims.Add(new Claim("role", tenantRole));
            claims.Add(new Claim(ClaimTypes.Role, tenantRole));
        }

        // Only _db and User are touched by GetMyCapabilities. The remaining
        // dependencies are deliberately left null so an accidental future
        // dependency on them fails loudly here rather than passing silently.
        return new ProjectMembersController(f.Db, null!, null!, null!, null!)
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

    private static (bool curate, bool approve, bool administer, Guid projectId, Guid userId) Read(ActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var v = ok.Value!;
        var t = v.GetType();
        return (
            (bool)t.GetProperty("canCurateProject")!.GetValue(v)!,
            (bool)t.GetProperty("canApproveSitePhotos")!.GetValue(v)!,
            (bool)t.GetProperty("canAdministerProject")!.GetValue(v)!,
            (Guid)t.GetProperty("projectId")!.GetValue(v)!,
            (Guid)t.GetProperty("userId")!.GetValue(v)!);
    }

    // ── Fixture sanity — must be green before anything below means anything ───

    [Fact]
    public void Sanity_the_fixture_actually_has_rows()
    {
        using var f = NewDb();
        Assert.Equal(Seed.Length, f.Db.ProjectMembers.Count(m => m.ProjectId == f.ProjectId));
        Assert.True(f.Db.Projects.Any(p => p.Id == f.ProjectId),
            "Project invisible through the tenant filter — every assertion below would pass vacuously.");
    }

    // ── The 404 contract ──────────────────────────────────────────────────────

    [Fact]
    public async Task Invisible_project_is_404_so_clients_default_everything_to_false()
    {
        using var f = NewDb();

        // A real, authenticated user who is simply not on this project and did
        // not author it.
        var result = await NewController(f, Guid.NewGuid()).GetMyCapabilities(f.ProjectId);

        var notFound = Assert.IsType<NotFoundResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task Unknown_project_id_is_404_not_an_all_false_body()
    {
        using var f = NewDb();

        var result = await NewController(f, f.User("manager")).GetMyCapabilities(Guid.NewGuid());

        // Deliberately NOT an Ok with all-false: a body implies the server knows
        // the answer. Absence of the project must be indistinguishable from
        // absence of authority — that is what makes 404 the safe client default.
        Assert.IsType<NotFoundResult>(result);
    }

    // ── Capability resolution per role ────────────────────────────────────────

    [Theory]
    //                                           curate approve administer
    [InlineData("manager",                       true,  true,   true)]
    [InlineData("coordinator",                   true,  false,  false)] // curates; neither releases imagery nor administers
    [InlineData("contributor-who-is-the-iso-PM", true,  true,   true)]  // authority via Iso19650Role
    [InlineData("plain-contributor",             false, false,  false)]
    [InlineData("viewer",                        false, false,  false)]
    public async Task Capabilities_match_the_role(
        string label, bool expectCurate, bool expectApprove, bool expectAdminister)
    {
        using var f = NewDb();
        var userId = f.User(label);

        var (curate, approve, administer, pid, uid) = Read(
            await NewController(f, userId).GetMyCapabilities(f.ProjectId));

        Assert.Equal(expectCurate, curate);
        Assert.Equal(expectApprove, approve);
        Assert.Equal(expectAdminister, administer);
        Assert.Equal(f.ProjectId, pid);
        Assert.Equal(userId, uid);
    }

    [Fact]
    public async Task Coordinator_curates_but_cannot_administer()
    {
        // The second place the predicates diverge, and the one that matters to the
        // mobile screen: a Coordinator may organise albums and checklists, and may
        // NOT change ISO naming enforcement or the deliverable state machine.
        // The role set this replaces on that screen — {Admin, Owner, PM,
        // BIM_Manager, BIMManager} — got this right by accident and got Manager
        // wrong, since "Manager" was not in it.
        using var f = NewDb();

        var (curate, _, administer, _, _) = Read(
            await NewController(f, f.User("coordinator")).GetMyCapabilities(f.ProjectId));

        Assert.True(curate);
        Assert.False(administer);
    }

    [Fact]
    public async Task A_project_Manager_can_administer_which_the_replaced_client_gate_denied()
    {
        // Regression-in-reverse: project-settings/index.tsx tested projectRole
        // against {'Admin','Owner','PM','BIM_Manager','BIMManager'}. A plain
        // project MANAGER — the most common administrator — was not in that set,
        // so the screen greyed the toggles out for exactly the person meant to use
        // them, while the server (pre-#737) refused everyone anyway.
        using var f = NewDb();

        var (_, _, administer, _, _) = Read(
            await NewController(f, f.User("manager")).GetMyCapabilities(f.ProjectId));

        Assert.True(administer);
    }

    [Fact]
    public async Task Coordinator_curates_but_cannot_approve_photos()
    {
        // Called out separately because it is the one place the two predicates
        // genuinely diverge. If someone "simplifies" them into a single flag,
        // this is the test that fails.
        using var f = NewDb();

        var (curate, approve, _, _, _) = Read(
            await NewController(f, f.User("coordinator")).GetMyCapabilities(f.ProjectId));

        Assert.True(curate);
        Assert.False(approve);
    }

    // ── Tenant admin bypass — pre-existing behaviour, kept deliberately ────────

    [Fact]
    public async Task Tenant_admin_gets_both_without_any_project_member_row()
    {
        using var f = NewDb();
        var admin = Guid.NewGuid();

        Assert.False(f.Db.ProjectMembers.Any(m => m.UserId == admin),
            "Fixture must NOT give the admin a member row, or this proves nothing.");

        var (curate, approve, administer, _, _) = Read(
            await NewController(f, admin, tenantRole: "Admin").GetMyCapabilities(f.ProjectId));

        Assert.True(curate);
        Assert.True(approve);
        Assert.True(administer);
    }

    // ── Response shape ────────────────────────────────────────────────────────

    [Fact]
    public async Task Response_carries_exactly_five_fields()
    {
        // Three booleans, matching the three predicates that exist. A fourth
        // capability goes through propose-first review, so an inline addition
        // should fail here rather than ship unnoticed.
        //
        // This guard DID fire when canAdministerProject was added, which is the
        // point of it. That capability was proposed in #666 and approved before it
        // was written, so the expectation moves with it — deliberately, in the same
        // change, rather than the guard being weakened to stop complaining.
        using var f = NewDb();

        var ok = Assert.IsType<OkObjectResult>(
            await NewController(f, f.User("manager")).GetMyCapabilities(f.ProjectId));

        var names = ok.Value!.GetType().GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(
            new[] { "canAdministerProject", "canApproveSitePhotos", "canCurateProject", "projectId", "userId" },
            names);
    }
}
