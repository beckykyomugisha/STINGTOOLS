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
/// 404 means EVERY capability is false. It must not be a 403, and it must not
/// return a body a client could misread as "unknown, proceed". That is asserted
/// directly in <see cref="Invisible_project_is_404_so_clients_default_everything_to_false"/>,
/// because a fail-open default here would undo the gate on all three surfaces
/// at once.
///
/// FIXTURE TRAP THIS FILE AVOIDS
/// -----------------------------
/// PlanscapeDbContext's global filter is `TenantId == CurrentTenantId`, falling
/// back to Guid.Empty with no ITenantContext — matching NO rows, so assertions
/// pass vacuously against an empty set. Every context here is built WITH a
/// tenant, and <see cref="Sanity_the_fixture_actually_has_rows"/> proves the
/// rows are visible before any capability claim is made.
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

    private static readonly Guid TenantId  = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AuthorId  = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>(projectRole, iso19650Role) seeded as real rows, one user each.</summary>
    private static readonly (string Project, string Iso, string Label)[] Seed =
    {
        ("Manager",     "M",  "manager"),
        ("Coordinator", "M",  "coordinator"),
        ("Contributor", "PM", "contributor-who-is-the-iso-PM"),
        ("Contributor", "M",  "plain-contributor"),
        ("Viewer",      "V",  "viewer"),
    };

    private static Guid UserIdFor(string label)
    {
        // Deterministic per-label GUID so tests can look a member up by role.
        var bytes = new byte[16];
        var src = System.Text.Encoding.UTF8.GetBytes(label);
        for (int i = 0; i < src.Length && i < 16; i++) bytes[i] = src[i];
        bytes[15] = 0xAA;
        return new Guid(bytes);
    }

    private static (SqliteConnection conn, PlanscapeDbContext db) NewDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var db = new PlanscapeDbContext(
            new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options,
            httpContextAccessor: null!, tenantContext: new FixedTenant(TenantId));
        db.Database.EnsureCreated();

        // Tenant first: Project.TenantId and AppUser.TenantId are real FKs, and
        // SQLite (unlike EF InMemory) enforces them.
        db.Tenants.Add(new Tenant
        {
            Id = TenantId, Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}"[..16],
            ContactEmail = "a@example.com", Tier = LicenseTier.Professional,
            Plan = BillingPlan.Studio, MaxUsers = 50, MaxProjects = 50,
        });

        // The author is a real user, because Project.CreatedById is an FK and
        // because author-visibility is one of the branches WhereVisibleTo takes.
        db.Users.Add(new AppUser
        {
            Id = AuthorId, TenantId = TenantId,
            Email = $"author-{Guid.NewGuid():N}@example.com",
            DisplayName = "author", PasswordHash = "x", IsActive = true,
        });

        db.Projects.Add(new Project
        {
            Id = ProjectId, TenantId = TenantId, Name = "Kampala Temple",
            Code = $"P-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            CreatedById = AuthorId, PurgeAfter = null,
        });

        foreach (var (projectRole, iso, label) in Seed)
        {
            var uid = UserIdFor(label);
            // AppUser row is required: ProjectMember.UserId is a real FK.
            db.Users.Add(new AppUser
            {
                Id = uid, TenantId = TenantId,
                Email = $"{label}-{Guid.NewGuid():N}@example.com", DisplayName = label,
                PasswordHash = "x", IsActive = true,
            });
            db.ProjectMembers.Add(new ProjectMember
            {
                Id = Guid.NewGuid(), TenantId = TenantId,
                ProjectId = ProjectId, UserId = uid,
                ProjectRole = projectRole, Iso19650Role = iso, IsActive = true,
            });
        }

        db.SaveChanges();
        return (conn, db);
    }

    private static ProjectMembersController NewController(
        PlanscapeDbContext db, Guid userId, string? tenantRole = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("tenant_id", TenantId.ToString()),
        };
        if (tenantRole != null)
        {
            claims.Add(new Claim("role", tenantRole));
            claims.Add(new Claim(ClaimTypes.Role, tenantRole));
        }

        // Only _db and User are touched by GetMyCapabilities; the remaining
        // dependencies are deliberately not stubbed so an accidental future
        // dependency on them fails loudly here rather than silently passing.
        var controller = new ProjectMembersController(db, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                },
            },
        };
        return controller;
    }

    private static (bool curate, bool approve, Guid projectId, Guid userId) Read(ActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var v = ok.Value!;
        var t = v.GetType();
        return (
            (bool)t.GetProperty("canCurateProject")!.GetValue(v)!,
            (bool)t.GetProperty("canApproveSitePhotos")!.GetValue(v)!,
            (Guid)t.GetProperty("projectId")!.GetValue(v)!,
            (Guid)t.GetProperty("userId")!.GetValue(v)!);
    }

    // ── Fixture sanity — must run green before any assertion below means anything ──

    [Fact]
    public void Sanity_the_fixture_actually_has_rows()
    {
        var (conn, db) = NewDb();
        using (conn)
        {
            Assert.Equal(Seed.Length, db.ProjectMembers.Count(m => m.ProjectId == ProjectId));
            Assert.True(db.Projects.Any(p => p.Id == ProjectId),
                "Project invisible through the tenant filter — every assertion below would pass vacuously.");
        }
    }

    // ── The 404 contract ──────────────────────────────────────────────────────

    [Fact]
    public async Task Invisible_project_is_404_so_clients_default_everything_to_false()
    {
        var (conn, db) = NewDb();
        using (conn)
        {
            // A real, authenticated user who is simply not on this project and
            // did not author it.
            var stranger = Guid.Parse("99999999-9999-9999-9999-999999999999");
            var result = await NewController(db, stranger).GetMyCapabilities(ProjectId);

            var notFound = Assert.IsType<NotFoundResult>(result);
            Assert.Equal(404, notFound.StatusCode);
        }
    }

    [Fact]
    public async Task Unknown_project_id_is_404_not_an_all_false_body()
    {
        var (conn, db) = NewDb();
        using (conn)
        {
            var result = await NewController(db, UserIdFor("manager"))
                .GetMyCapabilities(Guid.Parse("44444444-4444-4444-4444-444444444444"));

            // Deliberately NOT an Ok with all-false: a body implies "I know the
            // answer". Absence of the project must be indistinguishable from
            // absence of authority, which is what makes 404 the safe default.
            Assert.IsType<NotFoundResult>(result);
        }
    }

    // ── Capability resolution per role ────────────────────────────────────────

    [Theory]
    [InlineData("manager",                       true,  true)]
    [InlineData("coordinator",                   true,  false)] // curates, cannot release imagery
    [InlineData("contributor-who-is-the-iso-PM", true,  true)]  // authority via Iso19650Role
    [InlineData("plain-contributor",             false, false)]
    [InlineData("viewer",                        false, false)]
    public async Task Capabilities_match_the_role(string label, bool expectCurate, bool expectApprove)
    {
        var (conn, db) = NewDb();
        using (conn)
        {
            var userId = UserIdFor(label);
            var (curate, approve, pid, uid) = Read(
                await NewController(db, userId).GetMyCapabilities(ProjectId));

            Assert.Equal(expectCurate, curate);
            Assert.Equal(expectApprove, approve);
            Assert.Equal(ProjectId, pid);
            Assert.Equal(userId, uid);
        }
    }

    [Fact]
    public async Task Coordinator_curates_but_cannot_approve_photos()
    {
        // Called out on its own because it is the one place the two predicates
        // genuinely diverge. If someone "simplifies" them into one flag, this
        // is the test that fails.
        var (conn, db) = NewDb();
        using (conn)
        {
            var (curate, approve, _, _) = Read(
                await NewController(db, UserIdFor("coordinator")).GetMyCapabilities(ProjectId));

            Assert.True(curate);
            Assert.False(approve);
        }
    }

    // ── Tenant admin bypass — pre-existing behaviour, kept deliberately ────────

    [Fact]
    public async Task Tenant_admin_gets_both_without_any_project_member_row()
    {
        var (conn, db) = NewDb();
        using (conn)
        {
            var admin = Guid.Parse("88888888-8888-8888-8888-888888888888");
            Assert.False(db.ProjectMembers.Any(m => m.UserId == admin),
                "Fixture must NOT give the admin a member row, or this proves nothing.");

            var (curate, approve, _, _) = Read(
                await NewController(db, admin, tenantRole: "Admin").GetMyCapabilities(ProjectId));

            Assert.True(curate);
            Assert.True(approve);
        }
    }

    // ── Response shape ────────────────────────────────────────────────────────

    [Fact]
    public async Task Response_carries_exactly_four_fields()
    {
        // Two booleans, matching the two predicates that exist. A third
        // capability goes through propose-first review, so an inline addition
        // should fail here rather than ship unnoticed.
        var (conn, db) = NewDb();
        using (conn)
        {
            var ok = Assert.IsType<OkObjectResult>(
                await NewController(db, UserIdFor("manager")).GetMyCapabilities(ProjectId));

            var names = ok.Value!.GetType().GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
            Assert.Equal(
                new[] { "canApproveSitePhotos", "canCurateProject", "projectId", "userId" },
                names);
        }
    }
}
