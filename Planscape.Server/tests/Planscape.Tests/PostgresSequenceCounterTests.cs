using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// <see cref="SequenceCounterService"/> against a REAL PostgreSQL.
///
/// WHY THIS FILE HAS TO EXIST
/// --------------------------
/// The bug fixed in PR #441 was `SqlQueryRaw(...).FirstAsync()` composing a
/// `SELECT … LIMIT 1` over non-composable `INSERT … ON CONFLICT … RETURNING`.
/// Npgsql rejects that at execution time, so `/seq/reserve` returned **HTTP 500**
/// in production — and the entire 423-test suite was green, because every other
/// DbContext in it is EF InMemory or SQLite and neither ever generates the SQL
/// that fails. The whole bug class is invisible without a real server.
///
/// So these tests are not "more coverage of AllocateAsync". They are the only
/// place in the suite where raw SQL is actually handed to PostgreSQL.
///
/// SKIPPING, NOT PASSING
/// ---------------------
/// Without `PLANSCAPE_TEST_PG` these tests report **Skipped**, never Passed. A
/// test that silently no-ops when its dependency is missing is worse than absent:
/// it reports safety it did not check. That is precisely the failure this round
/// exists to correct.
///
///     # locally, against the docker compose stack:
///     export PLANSCAPE_TEST_PG="Host=localhost;Port=5432;Database=planscape;Username=planscape;Password=Planscape2026!"
///     dotnet test --filter FullyQualifiedName~Postgres
///
/// The tests write only to `SeqCounters`, keyed on GUIDs minted per test, and
/// delete their own rows — safe to point at a shared dev database.
/// </summary>
public class PostgresSequenceCounterTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("PLANSCAPE_TEST_PG");

    /// <summary>Reason string when the suite runs without a database, or null when it can run.</summary>
    private static string? SkipReason =>
        string.IsNullOrWhiteSpace(ConnectionString)
            ? "PLANSCAPE_TEST_PG is not set — no PostgreSQL to test against."
            : null;

    private static PlanscapeDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    private static SequenceCounterService NewService(PlanscapeDbContext db) => new(db);

    /// <summary>
    /// Materialise the schema once per test run if the target database is empty.
    ///
    /// Locally this is a no-op: the docker compose database already has the full
    /// schema, and EnsureCreated short-circuits when any table exists. In CI the
    /// service container starts empty, so this is what makes the run possible at
    /// all — without it every test fails with `relation "SeqCounters" does not
    /// exist`, which would look like a code regression rather than a missing
    /// fixture.
    ///
    /// EnsureCreated (rather than Migrate) is deliberate: it is this project's
    /// documented schema mechanism — see docs/adr/0001-schema-management.md and
    /// PLANSCAPE_USE_ENSURE_CREATED in render.yaml.
    /// </summary>
    private static readonly Lazy<bool> SchemaReady = new(() =>
    {
        using var db = NewContext();
        db.Database.EnsureCreated();
        return true;
    });

    /// <summary>A counter key no other test or run will collide with.</summary>
    private static string FreshKey() => $"test_{Guid.NewGuid():N}";

    /// <summary>
    /// Create a real tenant + project to hang counters off, and return a disposer
    /// that removes them.
    ///
    /// Needed because `SeqCounters.ProjectId` carries a FOREIGN KEY to `Projects`
    /// — allocating against a random GUID fails with
    /// `23503: violates foreign key constraint "FK_SeqCounters_Projects_ProjectId"`.
    /// EF InMemory does not enforce foreign keys, so a test written against it
    /// would have passed with fabricated ids and told us nothing. Found by running
    /// this against the real server, which is the point of the file.
    /// </summary>
    private static async Task<(Guid tenantId, Guid projectId)> NewProjectAsync()
    {
        await using var db = NewContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "PG Test Org",
            Slug = $"pgtest-{Guid.NewGuid():N}"[..20],
            ContactEmail = "pg@example.test",
            Tier = LicenseTier.Starter,
            MaxUsers = 5,
            MaxProjects = 50,
            IsActive = true,
        };
        var project = new Project
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "PG Test Project",
            Code = $"PGT-{Guid.NewGuid():N}"[..12],
            Phase = "Design",
            Status = ProjectStatus.Active,
        };
        db.Tenants.Add(tenant);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return (tenant.Id, project.Id);
    }

    private static async Task DropProjectAsync(Guid tenantId, Guid projectId)
    {
        try
        {
            await using var db = NewContext();
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"SeqCounters\" WHERE \"ProjectId\" = {0}", projectId);
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"Projects\" WHERE \"Id\" = {0}", projectId);
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"Tenants\" WHERE \"Id\" = {0}", tenantId);
        }
        catch { /* best effort — rows are GUID-keyed and inert */ }
    }

    private static async Task CleanupAsync(string key)
    {
        try
        {
            await using var db = NewContext();
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"SeqCounters\" WHERE \"CounterKey\" = {0}", key);
        }
        catch { /* best effort — a leftover row with a GUID key harms nothing */ }
    }

    // ── the regression that shipped ────────────────────────────────────────

    [SkippableFact]
    public async Task AllocateAsync_OnANewKey_ReturnsOne_AndDoesNotThrowOnPostgres()
    {
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        var key = FreshKey();
        var (tenant, project) = await NewProjectAsync();
        try
        {
            await using var db = NewContext();

            // Before PR #441 this line threw:
            //   InvalidOperationException: 'FromSql' or 'SqlQuery' was called with
            //   non-composable SQL and with a query composing over it.
            // …surfacing as a 500 from /seq/reserve. Nothing else in the suite
            // executes this statement against a server that parses it.
            var value = await NewService(db).AllocateAsync(
                tenant, project, key, updatedBy: "pg-test");

            Assert.Equal(1, value);
        }
        finally { await DropProjectAsync(tenant, project); }
    }

    [SkippableFact]
    public async Task AllocateAsync_OnAnExistingKey_AdvancesByTheRequestedCount()
    {
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        var key = FreshKey();
        var (tenant, project) = await NewProjectAsync();
        try
        {
            await using var db = NewContext();
            var svc = NewService(db);

            Assert.Equal(1, await svc.AllocateAsync(tenant, project, key, updatedBy: "pg-test"));
            Assert.Equal(4, await svc.AllocateAsync(tenant, project, key, count: 3, updatedBy: "pg-test"));
            Assert.Equal(5, await svc.AllocateAsync(tenant, project, key, updatedBy: "pg-test"));

            // The contract callers rely on: the return value is the HIGHEST
            // number allocated, and the block is [value - count + 1, value].
            // /seq/reserve computes `start` that way, so an off-by-one here
            // hands two callers overlapping blocks.
        }
        finally { await DropProjectAsync(tenant, project); }
    }

    [SkippableFact]
    public async Task AllocateAsync_RespectsSeedFloor_OnFirstInsertOnly()
    {
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        var key = FreshKey();
        var (tenant, project) = await NewProjectAsync();
        try
        {
            await using var db = NewContext();
            var svc = NewService(db);

            // Migrating a project whose legacy codes already reach 42.
            Assert.Equal(43, await svc.AllocateAsync(tenant, project, key, seedFloor: 42, updatedBy: "pg-test"));
            // GREATEST(current, seedFloor) — a LOWER floor afterwards must not rewind.
            Assert.Equal(44, await svc.AllocateAsync(tenant, project, key, seedFloor: 10, updatedBy: "pg-test"));
        }
        finally { await DropProjectAsync(tenant, project); }
    }

    // ── the property the endpoint actually depends on ──────────────────────

    [SkippableFact]
    public async Task ConcurrentAllocations_OnTheSameKey_AreDisjoint()
    {
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        var key = FreshKey();
        var (tenant, project) = await NewProjectAsync();
        const int Workers = 8;
        const int PerWorker = 5;

        try
        {
            // Separate DbContexts, genuinely in parallel — one shared context
            // would serialise on EF's internal lock and prove nothing about the
            // database. The row lock inside the UPSERT is what must serialise.
            var tasks = Enumerable.Range(0, Workers).Select(async _ =>
            {
                await using var db = NewContext();
                return await NewService(db).AllocateAsync(
                    tenant, project, key, count: PerWorker, updatedBy: "pg-test");
            });

            var highs = await Task.WhenAll(tasks);

            // Expand each returned high-water mark into the block its caller owns.
            var claimed = highs
                .SelectMany(h => Enumerable.Range(h - PerWorker + 1, PerWorker))
                .ToList();

            Assert.Equal(Workers * PerWorker, claimed.Count);
            Assert.Equal(Workers * PerWorker, claimed.Distinct().Count());   // no overlap
            Assert.Equal(
                Enumerable.Range(1, Workers * PerWorker),
                claimed.OrderBy(n => n));                                    // no gaps either
        }
        finally { await DropProjectAsync(tenant, project); }
    }

    [SkippableFact]
    public async Task TwoKeys_AdvanceIndependently()
    {
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        var a = FreshKey();
        var b = FreshKey();
        var (tenant, project) = await NewProjectAsync();
        try
        {
            await using var db = NewContext();
            var svc = NewService(db);

            await svc.AllocateAsync(tenant, project, a, count: 7, updatedBy: "pg-test");
            // A second key must start at 1 — the UPSERT conflict target is
            // (ProjectId, CounterKey), so a same-project different-key allocation
            // must not inherit the first key's value.
            Assert.Equal(1, await svc.AllocateAsync(tenant, project, b, updatedBy: "pg-test"));
        }
        finally { await DropProjectAsync(tenant, project); }
    }

    [SkippableFact]
    public async Task TheRowIsActuallyPersisted_NotJustReturned()
    {
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        var key = FreshKey();
        var (tenant, project) = await NewProjectAsync();
        try
        {
            await using (var db = NewContext())
                await NewService(db).AllocateAsync(tenant, project, key, count: 9, updatedBy: "pg-test");

            // Fresh context: proves the UPSERT committed rather than the value
            // coming from a tracked in-memory entity.
            await using var verify = NewContext();
            var row = await verify.SeqCounters.IgnoreQueryFilters()
                .SingleAsync(c => c.ProjectId == project && c.CounterKey == key);

            Assert.Equal(9, row.CurrentValue);
            Assert.Equal("pg-test", row.UpdatedBy);
        }
        finally { await DropProjectAsync(tenant, project); }
    }
}
