using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// Characterisation tests for the billing seat split (`QuotaGuardService`).
///
/// WHY THESE EXIST
/// ---------------
/// `CountAsync` splits seats on a single string:
///
///     Authors      => ProjectMembers.Where(m => m.ProjectRole == "Author")
///     Coordinators => ProjectMembers.Where(m => m.ProjectRole != "Author")
///
/// but **no first-party path ever writes `"Author"`**. The roles actually
/// written are "Owner" (AuthController), "Manager" (ProjectsController) and
/// "Contributor" (ProjectMembersController defaults). So the author axis
/// counts 0 forever and the coordinator axis counts every member — including
/// the project owner, who is not a coordinator in any product sense.
///
/// These tests PIN THE CURRENT (WRONG) BEHAVIOUR deliberately. They are the
/// "before" half of the before/after the fix needs. When the seat split is
/// corrected, `Authors_axis_counts_zero_for_a_realistic_roster` and
/// `Coordinators_axis_counts_every_member_including_the_owner` are expected to
/// FAIL — that failure is the signal the fix landed, and they should be
/// rewritten to assert the intended split at that point.
///
/// WHAT THEY DO NOT CLAIM
/// ----------------------
/// That any user is currently blocked. `CheckCanAddUserAsync` has only two
/// production callers (OnboardingController:93, TenantAdminController:108);
/// the `[Quota]` filter is applied to the Projects axis only
/// (ProjectsController:116), and the main project-member invite path enforces
/// `tenant.MaxUsers` instead (ProjectMembersController:256). The mis-count is
/// real; its blast radius is those two paths.
///
/// SQLite (not EF InMemory) so the relational query semantics are the real ones.
/// </summary>
public class QuotaSeatCountingTests
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
    /// The DbContext MUST be given an ITenantContext. Its global query filter is
    /// `TenantId == CurrentTenantId`, and CurrentTenantId falls back to
    /// Guid.Empty when no tenant context is wired — which matches no rows and
    /// fails closed. A fixture built on the parameterless ctor therefore reads
    /// zero of everything and every count assertion passes trivially, measuring
    /// nothing. (That is exactly what the first draft of this file did; two of
    /// the four tests "passed" against an empty result set.)
    /// </summary>
    private static PlanscapeDbContext NewContext(SqliteConnection conn, Guid tenantId)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options,
               httpContextAccessor: null!, tenantContext: new FixedTenant(tenantId));

    /// <summary>A tenant on Studio (1 author seat, 5 coordinator seats) with a
    /// realistic roster: one Owner, one Manager, three Contributors.</summary>
    private static (SqliteConnection conn, Guid tenantId) NewDbWithRealisticRoster()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var tenantId = Guid.NewGuid();
        using (var ctx = NewContext(conn, tenantId))
        {
            ctx.Database.EnsureCreated();
            ctx.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Acme",
                Slug = $"acme-{Guid.NewGuid():N}"[..16],
                ContactEmail = "acme@example.com",
                Tier = LicenseTier.Professional,
                Plan = BillingPlan.Studio,
                MaxUsers = 50,
                MaxProjects = 50,
            });

            var projectId = Guid.NewGuid();
            ctx.Projects.Add(new Project
            {
                Id = projectId, TenantId = tenantId,
                Name = "Tower", Code = $"P-{Guid.NewGuid():N}"[..8],
                Status = ProjectStatus.Active,
            });

            // Exactly the roles first-party code writes. ProjectMember.UserId
            // is a real FK, so each needs an AppUser behind it.
            var roles = new[] { "Owner", "Manager", "Contributor", "Contributor", "Contributor" };
            for (var i = 0; i < roles.Length; i++)
            {
                var userId = Guid.NewGuid();
                ctx.Users.Add(new AppUser
                {
                    Id = userId, TenantId = tenantId,
                    Email = $"user{i}-{Guid.NewGuid():N}@example.com",
                    DisplayName = $"User {i}",
                    PasswordHash = "x",
                    IsActive = true,
                });
                ctx.ProjectMembers.Add(new ProjectMember
                {
                    TenantId = tenantId, ProjectId = projectId,
                    UserId = userId, ProjectRole = roles[i], IsActive = true,
                });
            }
            ctx.SaveChanges();
        }
        return (conn, tenantId);
    }

    private static QuotaGuardService NewGuard(SqliteConnection conn, Guid tenantId)
        => new(NewContext(conn, tenantId), new FixedTenant(tenantId));

    [Fact]
    public async Task Authors_axis_counts_zero_for_a_realistic_roster()
    {
        var (conn, tenantId) = NewDbWithRealisticRoster();
        using (conn)
        {
            var guard = NewGuard(conn, tenantId);

            // "Author" routes to QuotaAxis.Authors (QuotaGuardService:40-41).
            var result = await guard.CheckCanAddUserAsync("Author");

            // Five members exist. None is counted, because none carries the
            // literal ProjectRole "Author".
            Assert.Equal(0, result.Current);

            // Studio allows 1 author seat, so the paid-for seat reads as free
            // no matter how many people are actually on the project.
            Assert.Equal(1, result.Max);
            Assert.True(result.Allowed);
        }
    }

    [Fact]
    public async Task Coordinators_axis_counts_every_member_including_the_owner()
    {
        var (conn, tenantId) = NewDbWithRealisticRoster();
        using (conn)
        {
            var guard = NewGuard(conn, tenantId);

            var result = await guard.CheckCanAddUserAsync("Coordinator");

            // All five — Owner and Manager included — land on the coordinator
            // axis, because the predicate is merely `!= "Author"`.
            Assert.Equal(5, result.Current);
            Assert.Equal(5, result.Max);          // Studio: 5 coordinators

            // At the cap already: the sixth person cannot be added, while the
            // author seat sits unused. That is the inversion.
            Assert.False(result.Allowed);
        }
    }

    [Fact]
    public async Task A_member_explicitly_marked_Author_moves_between_the_two_axes()
    {
        var (conn, tenantId) = NewDbWithRealisticRoster();
        using (conn)
        {
            // Only an API caller can produce this: ProjectRole is accepted
            // unvalidated at ProjectMembersController:418. No UI offers it.
            using (var ctx = NewContext(conn, tenantId))
            {
                var m = ctx.ProjectMembers.First(x => x.ProjectRole == "Contributor");
                m.ProjectRole = "Author";
                ctx.SaveChanges();
            }

            Assert.Equal(1, (await NewGuard(conn, tenantId).CheckCanAddUserAsync("Author")).Current);
            Assert.Equal(4, (await NewGuard(conn, tenantId).CheckCanAddUserAsync("Coordinator")).Current);
        }
    }

    [Fact]
    public async Task Seats_are_counted_per_tenant()
    {
        var (conn, tenantId) = NewDbWithRealisticRoster();
        using (conn)
        {
            // A different tenant sees none of the rows above — guards against a
            // "fix" that drops the TenantId predicate while reworking the split.
            var other = await NewGuard(conn, Guid.NewGuid()).CheckCanAddUserAsync("Coordinator");
            Assert.Equal(0, other.Current);
        }
    }
}
