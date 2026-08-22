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
/// Write validation for <see cref="ProjectMember.Iso19650Role"/>.
///
/// WHY THIS EXISTS
/// ---------------
/// The column was free text. Five write sites did
/// <c>req.Iso19650Role ?? … ?? "M"</c> with nothing checking the value. That is
/// how three dead authorization gates happened: a gate compares against a value
/// that nothing prevents and nothing supplies. The worst of them required this
/// column to be "K" or "C" — codes in no vocabulary this server serves — so
/// nobody could edit project settings.
///
/// THE TWO PROPERTIES THAT MATTER, AND THEY PULL AGAINST EACH OTHER
/// ----------------------------------------------------------------
/// 1. A new BAD write is rejected.       <see cref="A_non_canonical_role_is_rejected_on_update"/> etc.
/// 2. An existing STRAY row stays saveable on an unrelated edit.
///                                        <see cref="An_existing_stray_row_survives_an_unrelated_edit"/>
///
/// (2) is the one a naive implementation breaks. The local measurement found
/// two stray rows ('S', leaked from the AppUser vocabulary, and 'EL', which is
/// in no vocabulary at all). Validating the COLUMN rather than the SUPPLIED
/// VALUE would make those two members impossible to edit at all — punishing
/// people for a bug in the code that stored their role. Strays are reported at
/// boot instead, and identified for a human to resolve rather than remapped by
/// a guess.
///
/// ONE SOURCE OF TRUTH
/// -------------------
/// <see cref="GetRoles_serves_exactly_the_canonical_vocabulary"/> pins the
/// endpoint to the same constant the validator uses. A validator and an
/// endpoint with separate literal lists is two sources of truth, and drift
/// between two lists is the root cause this whole change addresses.
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
        public required Guid SpareUserId { get; init; }
        public void Dispose() { Db.Dispose(); Conn.Dispose(); }
    }

    /// <summary>
    /// Seeds a manager (so the write endpoints authorize), a member carrying the
    /// real-world stray value 'EL', and a spare user with no membership.
    /// </summary>
    private static Fixture NewDb()
    {
        var tenantId   = Guid.NewGuid();
        var projectId  = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        var strayUser  = Guid.NewGuid();
        var spareUser  = Guid.NewGuid();
        var strayMemId = Guid.NewGuid();

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

        foreach (var (id, label) in new[] { (managerId, "manager"), (strayUser, "stray"), (spareUser, "spare") })
        {
            db.Users.Add(new AppUser
            {
                Id = id, TenantId = tenantId,
                Email = $"{label}-{Guid.NewGuid():N}@example.com",
                DisplayName = label, PasswordHash = "x", IsActive = true,
            });
        }

        db.Projects.Add(new Project
        {
            Id = projectId, TenantId = tenantId, Name = "Kampala Temple",
            Code = $"P-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            CreatedById = managerId,
        });

        db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId,
            UserId = managerId, ProjectRole = "Manager", Iso19650Role = "PM", IsActive = true,
        });

        // A row exactly like the ones production holds: written before there was
        // any validation, carrying a code in no vocabulary.
        db.ProjectMembers.Add(new ProjectMember
        {
            Id = strayMemId, TenantId = tenantId, ProjectId = projectId,
            UserId = strayUser, ProjectRole = "Contributor", Iso19650Role = "EL", IsActive = true,
        });

        db.SaveChanges();
        return new Fixture
        {
            Conn = conn, Db = db, TenantId = tenantId, ProjectId = projectId,
            ManagerUserId = managerId, StrayMemberId = strayMemId, SpareUserId = spareUser,
        };
    }

    private static ProjectMembersController NewController(Fixture f, Guid actingUserId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actingUserId.ToString()),
            new("user_id", actingUserId.ToString()),
            new("sub", actingUserId.ToString()),
            new("tenant_id", f.TenantId.ToString()),
        };

        // Dependencies the validated paths do not reach are left null so an
        // accidental future dependency fails loudly here rather than silently.
        return new ProjectMembersController(
            f.Db, null!, null!, null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProjectMembersController>.Instance)
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

    /// <summary>Unwraps the 400 body and asserts its shape matches the
    /// invalid_project_role precedent exactly.</summary>
    private static void AssertInvalidIsoBody(ActionResult result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var v = bad.Value!;
        var t = v.GetType();
        Assert.Equal("invalid_iso19650_role", t.GetProperty("error")!.GetValue(v));

        var allowed = Assert.IsAssignableFrom<IEnumerable<string>>(
            t.GetProperty("allowed")!.GetValue(v)).ToList();
        Assert.NotEmpty(allowed);
        // The caller must be able to act on the refusal, which means the list
        // has to be the real vocabulary, not a placeholder.
        Assert.Contains("PM", allowed);
        Assert.Contains("BC", allowed);
        Assert.Equal(Iso19650Roles.AllCodes, allowed);
    }

    // ── Fixture sanity ──────────────────────────────────────────────────────

    [Fact]
    public void Sanity_the_fixture_actually_has_rows()
    {
        using var f = NewDb();
        Assert.Equal(2, f.Db.ProjectMembers.Count(m => m.ProjectId == f.ProjectId));
        Assert.Equal("EL", f.Db.ProjectMembers.Single(m => m.Id == f.StrayMemberId).Iso19650Role);
    }

    // ── One source of truth ─────────────────────────────────────────────────

    /// <summary>
    /// The endpoint and the validator must agree. Before this change GetRoles
    /// held its own literal list; a validator with a second copy would drift
    /// from it, which is the exact failure mode being fixed.
    /// </summary>
    [Fact]
    public void GetRoles_serves_exactly_the_canonical_vocabulary()
    {
        using var f = NewDb();
        var ok = Assert.IsType<OkObjectResult>(NewController(f, f.ManagerUserId).GetRoles());

        var served = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value)
            .Cast<object>()
            .Select(o => (string)o.GetType().GetProperty("Code")!.GetValue(o)!)
            .ToList();

        Assert.NotEmpty(served);
        Assert.Equal(Iso19650Roles.AllCodes, served);
    }

    /// <summary>
    /// The two codes the dead gate required. Their absence is the bug; this
    /// pins it so a future "tidy-up" cannot quietly add them and make the old
    /// gate look reasonable in hindsight.
    /// </summary>
    [Theory]
    [InlineData("K")]
    [InlineData("C")]
    public void The_dead_gate_codes_are_not_in_the_vocabulary(string code)
        => Assert.False(Iso19650Roles.IsCanonical(code));

    // ── Property 1: new bad writes are rejected ─────────────────────────────

    [Theory]
    [InlineData("K")]      // the dead-gate code
    [InlineData("C")]      // the other one
    [InlineData("EL")]     // a real stray found in the database
    [InlineData("S")]      // the other real stray, leaked from the AppUser list
    [InlineData("Wizard")]
    [InlineData("")]
    public async Task A_non_canonical_role_is_rejected_on_update(string iso)
    {
        using var f = NewDb();
        var before = f.Db.ProjectMembers.Single(m => m.Id == f.StrayMemberId).Iso19650Role;

        var result = await NewController(f, f.ManagerUserId)
            .UpdateMember(f.ProjectId, f.StrayMemberId, new UpdateMemberRequest(null, iso));

        AssertInvalidIsoBody(result);
        Assert.Equal(before, f.Db.ProjectMembers.Single(m => m.Id == f.StrayMemberId).Iso19650Role);
    }

    [Fact]
    public async Task A_non_canonical_role_is_rejected_on_add()
    {
        using var f = NewDb();
        var countBefore = f.Db.ProjectMembers.Count();

        var result = await NewController(f, f.ManagerUserId)
            .AddMember(f.ProjectId, new AddMemberRequest(f.SpareUserId, null, "K"));

        AssertInvalidIsoBody(result);
        Assert.Equal(countBefore, f.Db.ProjectMembers.Count());
    }

    [Fact]
    public async Task A_non_canonical_role_is_rejected_on_invite()
    {
        using var f = NewDb();
        var usersBefore = f.Db.Users.Count();

        var result = await NewController(f, f.ManagerUserId)
            .InviteByEmail(f.ProjectId, new InviteByEmailRequest(
                "newcomer@example.com", "Newcomer", null, "K"));

        AssertInvalidIsoBody(result);
        // The invite path creates an AppUser before writing the membership;
        // rejecting late would leave a half-onboarded user behind.
        Assert.Equal(usersBefore, f.Db.Users.Count());
    }

    [Theory]
    [InlineData("PM")]
    [InlineData("BC")]
    [InlineData("A")]
    [InlineData("Z")]
    [InlineData("pm")]     // case-insensitive, matching IsCanonical for ProjectRole
    public async Task A_canonical_role_is_accepted_on_update(string iso)
    {
        using var f = NewDb();

        var result = await NewController(f, f.ManagerUserId)
            .UpdateMember(f.ProjectId, f.StrayMemberId, new UpdateMemberRequest(null, iso));

        Assert.IsNotType<BadRequestObjectResult>(result);
        Assert.Equal(iso, f.Db.ProjectMembers.Single(m => m.Id == f.StrayMemberId).Iso19650Role);
    }

    // ── Property 2: existing strays stay saveable ───────────────────────────

    /// <summary>
    /// THE TEST THAT KEEPS THE PROMISE. A member already carrying 'EL' — a code
    /// no validator would accept today — is edited on a different axis. That
    /// edit must succeed and must leave the stray untouched.
    ///
    /// Validating the COLUMN rather than the SUPPLIED VALUE would 400 here, and
    /// two real people would become uneditable because of a bug in the code that
    /// stored their role. This is why the guard tests `iso != null` first.
    /// </summary>
    [Fact]
    public async Task An_existing_stray_row_survives_an_unrelated_edit()
    {
        using var f = NewDb();
        Assert.False(Iso19650Roles.IsCanonical("EL"));   // premise, stated not assumed

        var result = await NewController(f, f.ManagerUserId)
            .UpdateMember(f.ProjectId, f.StrayMemberId,
                new UpdateMemberRequest(ProjectRole: "Coordinator", Iso19650Role: null));

        Assert.IsNotType<BadRequestObjectResult>(result);

        var after = f.Db.ProjectMembers.Single(m => m.Id == f.StrayMemberId);
        Assert.Equal("Coordinator", after.ProjectRole);   // the edit landed
        Assert.Equal("EL", after.Iso19650Role);           // the stray is left alone
    }

    /// <summary>
    /// A stray can still be corrected — the row is not frozen, only protected
    /// from collateral rejection. This is the path a human takes after the boot
    /// warning names it.
    /// </summary>
    [Fact]
    public async Task An_existing_stray_row_can_be_corrected_to_a_canonical_value()
    {
        using var f = NewDb();

        var result = await NewController(f, f.ManagerUserId)
            .UpdateMember(f.ProjectId, f.StrayMemberId, new UpdateMemberRequest(null, "ME"));

        Assert.IsNotType<BadRequestObjectResult>(result);
        Assert.Equal("ME", f.Db.ProjectMembers.Single(m => m.Id == f.StrayMemberId).Iso19650Role);
    }

    // ── The constant itself ─────────────────────────────────────────────────

    [Fact]
    public void Null_is_not_canonical_but_that_must_never_mean_rejected()
    {
        // IsCanonical(null) is false, which is correct for a membership test.
        Assert.False(Iso19650Roles.IsCanonical(null));
        // The behavioural consequence is asserted by
        // An_existing_stray_row_survives_an_unrelated_edit: every write site
        // reads null as "leave it alone", never as "reject".
    }

    [Fact]
    public void Every_code_is_distinct_and_non_empty()
    {
        Assert.Equal(Iso19650Roles.AllCodes.Count, Iso19650Roles.AllCodes.Distinct().Count());
        Assert.All(Iso19650Roles.AllCodes, c => Assert.False(string.IsNullOrWhiteSpace(c)));
        Assert.All(Iso19650Roles.All, r => Assert.False(string.IsNullOrWhiteSpace(r.Label)));
    }
}
