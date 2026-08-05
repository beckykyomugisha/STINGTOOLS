using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// The seat split, keyed on CAPABILITY rather than on a role taxonomy.
///
/// WHY NOT Iso19650Role (the previous attempt, corrected)
/// ------------------------------------------------------
/// Iso19650Role is a functional/discipline taxonomy
/// (A/M/E/S/H/P/C/I/K/Q/F/W/L/Z) and ISO 19650 assigns information-management
/// responsibility, not software seats. "A" is the APPOINTING PARTY — the client
/// — not "Author" and not "Architect". Metering on it counted the client as the
/// only author (hence Authors reading 0 for everyone else) and dropped "BA",
/// BIM Author, onto the other axis. It is the wrong question, not a missing
/// code.
///
/// Author seats now come from ProjectRoles.CanAuthorInformation, the same
/// capability layer access control uses, so billing and access cannot drift.
/// </summary>
public class QuotaSeatCapabilitySplitTests
{
    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "acme";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    // The DbContext MUST get an ITenantContext: CurrentTenantId falls back to
    // Guid.Empty, which matches no rows, so a fixture without one reads zero of
    // everything and every count assertion passes trivially.
    private static PlanscapeDbContext NewContext(SqliteConnection conn, Guid tenantId)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options,
               httpContextAccessor: null!, tenantContext: new FixedTenant(tenantId));

    /// <summary>A Studio tenant (1 author seat, 5 of the other axis) with a
    /// realistic roster: Owner, Manager, 3 Contributors, plus 2 read-only
    /// Viewers.</summary>
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

            var roles = new[]
            {
                UserRole.Owner, UserRole.Manager,
                UserRole.Contributor, UserRole.Contributor, UserRole.Contributor,
                UserRole.Viewer, UserRole.Viewer,
            };
            for (var i = 0; i < roles.Length; i++)
            {
                ctx.Users.Add(new AppUser
                {
                    Id = Guid.NewGuid(), TenantId = tenantId,
                    Email = $"zz-user{i}-{Guid.NewGuid():N}@example.com",
                    DisplayName = $"ZZ User {i}",
                    PasswordHash = "x",
                    Role = roles[i],
                    // Deliberately NOT "A" anywhere. The split must not depend on
                    // the ISO taxonomy at all — including its absence.
                    Iso19650Role = "M",
                    IsActive = true,
                });
            }
            ctx.SaveChanges();
        }
        return (conn, tenantId);
    }

    private static QuotaGuardService NewGuard(SqliteConnection conn, Guid tenantId)
        => new(NewContext(conn, tenantId), new FixedTenant(tenantId));

    [Fact]
    public async Task Every_account_that_can_author_consumes_an_author_seat()
    {
        var (conn, tenantId) = NewDbWithRealisticRoster();
        using (conn)
        {
            var result = await NewGuard(conn, tenantId).CheckCanAddUserAsync("Author");

            // Owner + Manager + 3 Contributors. None carries Iso19650Role "A",
            // which is the whole point: capability, not taxonomy.
            Assert.Equal(5, result.Current);
        }
    }

    [Fact]
    public async Task Read_only_accounts_consume_the_non_authoring_axis()
    {
        var (conn, tenantId) = NewDbWithRealisticRoster();
        using (conn)
        {
            var result = await NewGuard(conn, tenantId).CheckCanAddUserAsync("Coordinator");

            // The two Viewers, and only them.
            Assert.Equal(2, result.Current);
        }
    }

    [Fact]
    public async Task A_viewer_promoted_to_contributor_moves_between_the_axes()
    {
        var (conn, tenantId) = NewDbWithRealisticRoster();
        using (conn)
        {
            using (var ctx = NewContext(conn, tenantId))
            {
                var viewer = ctx.Users.First(u => u.Role == UserRole.Viewer);
                viewer.Role = UserRole.Contributor;
                ctx.SaveChanges();
            }

            Assert.Equal(6, (await NewGuard(conn, tenantId).CheckCanAddUserAsync("Author")).Current);
            Assert.Equal(1, (await NewGuard(conn, tenantId).CheckCanAddUserAsync("Coordinator")).Current);
        }
    }

    [Fact]
    public void The_in_memory_and_EF_capability_forms_agree_for_every_UserRole()
    {
        // CanAuthorInformation is evaluated in memory; CanAuthorInformationPredicate
        // is translated into SQL. If they ever disagree, access control and the
        // seat meter disagree — the exact drift this split exists to prevent.
        var compiled = ProjectRoles.CanAuthorInformationPredicate.Compile();

        foreach (UserRole role in Enum.GetValues<UserRole>())
        {
            var user = new AppUser { Role = role };
            Assert.Equal(ProjectRoles.CanAuthorInformation(role), compiled(user));
        }
    }

    [Fact]
    public async Task Seats_are_counted_per_tenant()
    {
        var (conn, tenantId) = NewDbWithRealisticRoster();
        using (conn)
        {
            // Guards against a rework that drops the TenantId predicate.
            var other = await NewGuard(conn, Guid.NewGuid()).CheckCanAddUserAsync("Author");
            Assert.Equal(0, other.Current);
        }
    }
}
