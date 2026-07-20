using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.API.Controllers;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// `AuthController.EnsureStarterProjectAsync` against a provider that actually
/// ENFORCES unique indexes.
///
/// WHY SQLITE AND NOT THE USUAL InMemory PROVIDER
/// ----------------------------------------------
/// EF InMemory does not enforce unique indexes, so it cannot reproduce the
/// concurrent-redemption race on `(TenantId, Code)` at all — the second insert
/// simply succeeds and there is no failure to recover from. In-memory SQLite is
/// a real relational provider and rejects it.
///
/// WHY THIS CALLS THE REAL METHOD
/// ------------------------------
/// The first version of this file re-implemented the detach loop inline and
/// asserted that *the copy* behaved. A regression in the controller's catch
/// block would have left it green — it tested a restatement of the fix rather
/// than the fix itself. `EnsureStarterProjectAsync` is now `internal` (with
/// `InternalsVisibleTo`) and is invoked directly.
///
/// WHY NOT AN HTTP TEST
/// --------------------
/// A `WebApplicationFactory` cannot be booted against SQLite: Program.cs's schema
/// block branches on `IsRelational()` — true for SQLite — then issues
/// `SELECT … FROM information_schema.tables`, which is Postgres-only and has no
/// try/catch, so the host dies before serving a request. Constructing the
/// controller directly also avoids the process-wide Hangfire teardown that makes
/// extra factories unreliable in this assembly (ROADMAP DEP-7). Only `_db` and
/// `_logger` are touched by the method under test, so the remaining constructor
/// dependencies are not needed.
/// </summary>
public class HandoffProvisioningSqliteTests
{
    private static PlanscapeDbContext NewContext(
        SqliteConnection conn, params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseSqlite(conn)
            .AddInterceptors(interceptors)
            .Options);

    /// <summary>The controller under test, wired to <paramref name="db"/>.</summary>
    private static AuthController NewController(PlanscapeDbContext db)
        => new(db, config: null!, revocations: null!, redis: null!,
               logger: NullLogger<AuthController>.Instance);

    private static (SqliteConnection conn, Guid tenantId) NewDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);
        ctx.Database.EnsureCreated();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Race Org",
            Slug = $"race-{Guid.NewGuid():N}"[..20],
            ContactEmail = "race@example.com",
            Tier = LicenseTier.Starter,
            MaxUsers = 5,
            MaxProjects = 5,
            IsActive = true,
        };
        ctx.Tenants.Add(tenant);
        ctx.SaveChanges();
        return (conn, tenant.Id);
    }

    private static AppUser NewUser(PlanscapeDbContext ctx, Guid tenantId)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = $"handoff-{Guid.NewGuid():N}@example.com",
            DisplayName = "Handoff Tester",
            PasswordHash = "unusable-by-design",
            Role = UserRole.Owner,
            Iso19650Role = "A",
            IsActive = true,
        };
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user;
    }

    private static Project StarterFor(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Name = "My First Project",
        Code = "PRJ-001",
        Phase = "Design",
        Status = ProjectStatus.Active,
    };

    // ────────────────────────────────────────────────────────────────────
    //  The constraint that makes the race real
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sqlite_enforces_the_unique_index_that_makes_the_race_real()
    {
        var (conn, tenantId) = NewDb();
        using var _ = conn;

        using var a = NewContext(conn);
        a.Projects.Add(StarterFor(tenantId));
        a.SaveChanges();

        using var b = NewContext(conn);
        b.Projects.Add(StarterFor(tenantId));
        Assert.Throws<DbUpdateException>(() => b.SaveChanges());

        // Guard: if this ever stops throwing, every test below is vacuous.
        using var check = NewContext(conn);
        Assert.Single(check.Projects.IgnoreQueryFilters().Where(p => p.TenantId == tenantId));
    }

    // ────────────────────────────────────────────────────────────────────
    //  The real method, losing the race
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Commits the conflicting row from a SEPARATE connection the instant before
    /// the context under test saves — i.e. exactly when a concurrent redemption
    /// would win. Reproduces the double-click on "open in app" deterministically,
    /// instead of hoping two threads interleave.
    /// </summary>
    private sealed class RivalRedemptionInterceptor : SaveChangesInterceptor
    {
        private readonly SqliteConnection _conn;
        private readonly Guid _tenantId;
        private bool _fired;

        public RivalRedemptionInterceptor(SqliteConnection conn, Guid tenantId)
        {
            _conn = conn;
            _tenantId = tenantId;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var insertingProject = eventData.Context?.ChangeTracker.Entries()
                .Any(e => e.State == EntityState.Added && e.Entity is Project) == true;

            if (!_fired && insertingProject)
            {
                _fired = true;
                using var rival = NewContext(_conn);
                rival.Projects.Add(StarterFor(_tenantId));
                rival.SaveChanges();          // the other request gets there first
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task LosingTheRace_DoesNotPoisonTheContext_SoTheSessionSurvives()
    {
        var (conn, tenantId) = NewDb();
        using var _ = conn;

        using var ctx = NewContext(conn, new RivalRedemptionInterceptor(conn, tenantId));
        var user = NewUser(ctx, tenantId);

        // The REAL method. It must swallow the constraint violation…
        await NewController(ctx).EnsureStarterProjectAsync(user);

        // …and leave nothing half-inserted in the tracker. This is the actual
        // fix: without the detach, the dead Project sits here as Added.
        Assert.DoesNotContain(ctx.ChangeTracker.Entries(),
            e => e.State == EntityState.Added && (e.Entity is Project || e.Entity is ProjectMember));

        // The property users feel. This stands in for the unguarded
        // SaveChangesAsync that persists the refresh token immediately after
        // provisioning returns — before the fix it re-threw and the handoff
        // answered 500, denying login because of a *provisioning* failure.
        user.RefreshToken = "hashed-refresh-token";
        user.LastLoginAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        using var verify = NewContext(conn);
        Assert.Equal("hashed-refresh-token",
            verify.Users.IgnoreQueryFilters().Single(u => u.Id == user.Id).RefreshToken);
        Assert.Single(verify.Projects.IgnoreQueryFilters().Where(p => p.TenantId == tenantId));
    }

    [Fact]
    public async Task TwoRedemptions_OneRacingTheOther_YieldOneProjectAndTwoUsableSessions()
    {
        var (conn, tenantId) = NewDb();
        using var _ = conn;

        // First redemption — loses the race to a rival that commits mid-save.
        using (var first = NewContext(conn, new RivalRedemptionInterceptor(conn, tenantId)))
        {
            var u1 = NewUser(first, tenantId);
            await NewController(first).EnsureStarterProjectAsync(u1);

            u1.RefreshToken = "session-1";
            await first.SaveChangesAsync();      // must not throw
        }

        // Second redemption — a clean run. The tenant now HAS a project, so the
        // idempotency gate ("zero projects") should skip creation and only grant
        // membership.
        using (var second = NewContext(conn))
        {
            var u2 = NewUser(second, tenantId);
            await NewController(second).EnsureStarterProjectAsync(u2);

            u2.RefreshToken = "session-2";
            await second.SaveChangesAsync();
        }

        using var verify = NewContext(conn);
        Assert.Single(verify.Projects.IgnoreQueryFilters().Where(p => p.TenantId == tenantId));

        // Both users came away with a usable session…
        var sessions = verify.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.RefreshToken != null)
            .Select(u => u.RefreshToken).ToList();
        Assert.Equal(2, sessions.Count);
        Assert.Contains("session-1", sessions);
        Assert.Contains("session-2", sessions);

        // …and nobody was granted membership twice.
        var project = verify.Projects.IgnoreQueryFilters().Single(p => p.TenantId == tenantId);
        var members = verify.ProjectMembers.IgnoreQueryFilters()
            .Where(m => m.ProjectId == project.Id).ToList();
        Assert.Equal(members.Select(m => m.UserId).Distinct().Count(), members.Count);
    }

    [Fact]
    public async Task AWinningRun_ProvisionsExactlyOneProjectAndOneMembership()
    {
        var (conn, tenantId) = NewDb();
        using var _ = conn;

        using var ctx = NewContext(conn);
        var user = NewUser(ctx, tenantId);

        await NewController(ctx).EnsureStarterProjectAsync(user);
        // Idempotent by its own gate: a second call must be a no-op.
        await NewController(ctx).EnsureStarterProjectAsync(user);

        using var verify = NewContext(conn);
        var project = verify.Projects.IgnoreQueryFilters().Single(p => p.TenantId == tenantId);
        Assert.Equal("PRJ-001", project.Code);
        Assert.Single(verify.ProjectMembers.IgnoreQueryFilters()
            .Where(m => m.ProjectId == project.Id && m.UserId == user.Id));
    }
}
