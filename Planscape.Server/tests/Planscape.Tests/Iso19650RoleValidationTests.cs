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
/// ProjectMember.Iso19650Role is validated on write — the change the controller's
/// own comment was waiting for:
///
///     // Iso19650Role is deliberately NOT validated in this pass — its
///     // vocabulary lives in GetRoles() below and constraining it is a
///     // separate, wider change.
///
/// THE TWO PROPERTIES THAT MATTER, AND THEY PULL AGAINST EACH OTHER
/// ----------------------------------------------------------------
/// 1. A supplied value outside the vocabulary is REJECTED, so the column stops
///    drifting. An unvalidated column is what made eleven gates read the wrong
///    field (see <see cref="ProjectRoles"/>).
/// 2. A row that ALREADY holds a stray stays editable. Two exist locally — 'S'
///    (a code from AppUser's different vocabulary) and 'EL' (in no vocabulary).
///    An edit that omits the field must succeed, or someone fixing a member's
///    ProjectRole is blocked by a code they did not write and cannot interpret.
///
/// Property 2 is the one a naive implementation breaks, so it is tested first and
/// tested hardest.
///
/// FIXTURE TRAPS — the same two this suite documents elsewhere
/// -----------------------------------------------------------
/// PlanscapeDbContext's global filter is TenantId == CurrentTenantId, falling back
/// to Guid.Empty without an ITenantContext — matching NO rows, so assertions pass
/// vacuously. Every context here is built WITH a tenant, and the sanity test proves
/// the rows are visible before any claim is made. Identifiers are minted per
/// fixture; a fixed GUID collides with the shared web-factory store and produces
/// non-deterministic failures in unrelated classes.
/// </summary>
public class Iso19650RoleValidationTests
{
    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "acme";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    private sealed class Fixture : IDisposable
    {
        public required SqliteConnection Conn { get; init; }
        public required PlanscapeDbContext Db { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid ProjectId { get; init; }
        public required Guid ManagerUserId { get; init; }
        public required Guid StrayMemberId { get; init; }
        public required Guid CleanMemberId { get; init; }
        public void Dispose() { Db.Dispose(); Conn.Dispose(); }
    }

    /// <summary>The exact value found in the wild, from AppUser's OTHER vocabulary.</summary>
    private const string StrayFromTheOtherVocabulary = "S";

    private static Fixture NewDb()
    {
        var tenantId  = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var authorId  = Guid.NewGuid();
        var mgrId     = Guid.NewGuid();
        var strayUser = Guid.NewGuid();
        var cleanUser = Guid.NewGuid();

        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new PlanscapeDbContext(
            new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options,
            httpContextAccessor: null!, tenantContext: new FixedTenant(tenantId));
        db.Database.EnsureCreated();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Acme", Slug = "acme-" + Guid.NewGuid().ToString("N").Substring(0, 10),
            ContactEmail = "a@example.com", Tier = LicenseTier.Professional,
            Plan = BillingPlan.Studio, MaxUsers = 50, MaxProjects = 50,
        });

        foreach (var pair in new[]
                 {
                     (Id: authorId, Label: "author"), (Id: mgrId, Label: "manager"),
                     (Id: strayUser, Label: "stray"), (Id: cleanUser, Label: "clean"),
                 })
            db.Users.Add(new AppUser
            {
                Id = pair.Id, TenantId = tenantId,
                Email = pair.Label + "-" + Guid.NewGuid().ToString("N") + "@example.com",
                DisplayName = pair.Label, PasswordHash = "x", IsActive = true,
            });

        db.Projects.Add(new Project
        {
            Id = projectId, TenantId = tenantId, Name = "Kampala Temple",
            Code = "P-" + Guid.NewGuid().ToString("N").Substring(0, 6),
            Status = ProjectStatus.Active, CreatedById = authorId, PurgeAfter = null,
        });

        var strayMember = new ProjectMember
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId,
            UserId = strayUser, ProjectRole = "Contributor",
            // Seeded DIRECTLY, bypassing the controller — which is how the real rows
            // got here, and the only way to reproduce them now the write path refuses.
            Iso19650Role = StrayFromTheOtherVocabulary, IsActive = true,
        };
        var cleanMember = new ProjectMember
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId,
            UserId = cleanUser, ProjectRole = "Contributor",
            Iso19650Role = "M", IsActive = true,
        };
        db.ProjectMembers.Add(strayMember);
        db.ProjectMembers.Add(cleanMember);
        db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId,
            UserId = mgrId, ProjectRole = "Manager", Iso19650Role = "PM", IsActive = true,
        });

        db.SaveChanges();
        return new Fixture
        {
            Conn = conn, Db = db, TenantId = tenantId, ProjectId = projectId,
            ManagerUserId = mgrId, StrayMemberId = strayMember.Id, CleanMemberId = cleanMember.Id,
        };
    }

    private static ProjectMembersController NewController(Fixture f, Guid userId)
        => new ProjectMembersController(f.Db, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                        new Claim("sub", userId.ToString()),
                        new Claim("tenant_id", f.TenantId.ToString()),
                    }, "test")),
                },
            },
        };

    // ── Sanity — nothing below means anything until this is green ────────────

    [Fact]
    public void Sanity_the_fixture_has_a_visible_stray_row()
    {
        using var f = NewDb();
        Assert.Equal(3, f.Db.ProjectMembers.Count(m => m.ProjectId == f.ProjectId));
        var stray = f.Db.ProjectMembers.Single(m => m.Id == f.StrayMemberId);
        Assert.Equal(StrayFromTheOtherVocabulary, stray.Iso19650Role);
        Assert.False(Iso19650Roles.IsCanonical(stray.Iso19650Role),
            "the fixture must actually be outside the vocabulary or the tolerance tests prove nothing");
    }

    // ── Property 2 — an existing stray stays editable ────────────────────────

    [Fact]
    public async Task An_edit_that_omits_the_field_leaves_a_stray_row_untouched_and_succeeds()
    {
        // The whole point of Option 1. A manager fixing this member's ProjectRole
        // must not be blocked by an ISO code they did not write.
        using var f = NewDb();
        var c = NewController(f, f.ManagerUserId);

        var result = await c.UpdateMember(f.ProjectId, f.StrayMemberId,
            new UpdateMemberRequest("Coordinator", null, null, null, null, null));

        Assert.IsType<OkObjectResult>(result);

        var after = f.Db.ProjectMembers.Single(m => m.Id == f.StrayMemberId);
        Assert.Equal("Coordinator", after.ProjectRole);                 // the edit landed
        Assert.Equal(StrayFromTheOtherVocabulary, after.Iso19650Role);  // untouched, not "corrected"
    }

    [Fact]
    public async Task A_stray_row_can_be_corrected_to_a_canonical_value()
    {
        // Tolerance must not mean the row is frozen — the cleanup has to be possible.
        using var f = NewDb();
        var c = NewController(f, f.ManagerUserId);

        var result = await c.UpdateMember(f.ProjectId, f.StrayMemberId,
            new UpdateMemberRequest(null, "QS", null, null, null, null));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("QS", f.Db.ProjectMembers.Single(m => m.Id == f.StrayMemberId).Iso19650Role);
    }

    // ── Property 1 — a supplied stray is rejected ────────────────────────────

    [Theory]
    [InlineData("S")]    // real: from AppUser's different vocabulary
    [InlineData("EL")]   // real: in no declared vocabulary at all
    [InlineData("XX")]
    [InlineData("")]     // empty string is not "omitted" — it is a supplied non-value
    public async Task A_supplied_value_outside_the_vocabulary_is_rejected(string bad)
    {
        using var f = NewDb();
        var c = NewController(f, f.ManagerUserId);

        var result = await c.UpdateMember(f.ProjectId, f.CleanMemberId,
            new UpdateMemberRequest(null, bad, null, null, null, null));

        var br = Assert.IsType<BadRequestObjectResult>(result);
        var t = br.Value!.GetType();
        // Error SHAPE must match invalid_project_role exactly, so a client parses
        // one thing, not two.
        Assert.Equal("invalid_iso19650_role", t.GetProperty("error")!.GetValue(br.Value));
        Assert.NotNull(t.GetProperty("allowed"));

        // And it must not have written anything on the way to refusing.
        Assert.Equal("M", f.Db.ProjectMembers.Single(m => m.Id == f.CleanMemberId).Iso19650Role);
    }

    [Fact]
    public async Task Every_canonical_code_is_accepted()
    {
        // Enumerated rather than spot-checked: a rejection list that happens to
        // exclude a legal code is a lockout, and would surface as "I cannot set this
        // member to Civil Engineer" long after the change shipped.
        foreach (var code in Iso19650Roles.All)
        {
            using var f = NewDb();
            var c = NewController(f, f.ManagerUserId);

            var result = await c.UpdateMember(f.ProjectId, f.CleanMemberId,
                new UpdateMemberRequest(null, code, null, null, null, null));

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(code, f.Db.ProjectMembers.Single(m => m.Id == f.CleanMemberId).Iso19650Role);
        }
    }

    [Fact]
    public void Validation_is_case_insensitive_like_ProjectRole()
    {
        // Matches ProjectRoles.IsCanonical, so the two columns behave the same way.
        Assert.True(Iso19650Roles.IsCanonical("pm"));
        Assert.True(Iso19650Roles.IsCanonical("Pm"));
        Assert.False(Iso19650Roles.IsCanonical("P M"));
        Assert.False(Iso19650Roles.IsCanonical(null));
    }

    // ── The served list and the validated list are one list ──────────────────

    [Fact]
    public void GetRoles_serves_exactly_the_vocabulary_that_is_validated_against()
    {
        // The drift this whole change is about: the list clients are offered used to
        // be a response literal, and the write path checked nothing. If the two ever
        // diverge again, a user picks a role from a dropdown and the server rejects it.
        using var f = NewDb();
        var ok = Assert.IsType<OkObjectResult>(NewController(f, f.ManagerUserId).GetRoles());

        var served = ((System.Collections.IEnumerable)ok.Value!)
            .Cast<object>()
            .Select(o => (string)o.GetType().GetProperty("Code")!.GetValue(o)!)
            .ToArray();

        Assert.NotEmpty(served);
        Assert.Equal(Iso19650Roles.All, served);
        Assert.All(served, code => Assert.True(Iso19650Roles.IsCanonical(code)));
    }

    [Fact]
    public void The_vocabulary_still_contains_every_code_it_shipped_with()
    {
        // Pins the list against an accidental deletion. Dropping a code silently
        // makes existing rows carrying it unwritable — the same failure the tolerance
        // tests above guard from the other direction.
        foreach (var code in new[] { "A", "PM", "BC", "BA", "AR", "SE", "ME", "CE", "QS",
                                     "CA", "CT", "SC", "FM", "OM", "CL", "M", "V", "Z" })
            Assert.Contains(code, Iso19650Roles.All);

        Assert.Equal(18, Iso19650Roles.All.Count);
    }
}
