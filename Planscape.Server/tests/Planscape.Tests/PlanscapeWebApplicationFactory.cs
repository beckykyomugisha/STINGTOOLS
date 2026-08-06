using Hangfire;
using Microsoft.Extensions.Configuration;
using Hangfire.InMemory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Distributed;
using Planscape.Infrastructure.Data;
using Planscape.Core.Entities;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Planscape.Tests;

/// <summary>
/// Custom WebApplicationFactory that replaces PostgreSQL with EF InMemory,
/// removes Hangfire, and seeds test data.
/// </summary>
public class PlanscapeWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"PlanscapeTest_{Guid.NewGuid():N}";


    // ── Real-PostgreSQL mode ────────────────────────────────────────────────
    //
    // Set PLANSCAPE_TEST_PG and the whole factory runs against PostgreSQL
    // instead of the EF InMemory provider:
    //
    //   export PLANSCAPE_TEST_PG="Host=localhost;Port=5432;Database=planscape;Username=planscape;Password=Planscape2026!"
    //
    // This is what makes provider-specific behaviour testable at all. On
    // InMemory the following are simply unreachable, and were previously
    // skipped for that reason:
    //   • real transactions — InMemory raises TransactionIgnoredWarning, which
    //     EF escalates to an exception, so nothing that opens one could be
    //     exercised and rollback semantics went unverified;
    //   • EF.Functions.ILike (SearchController) — Npgsql-only translation;
    //   • INSERT … ON CONFLICT … RETURNING with gen_random_uuid()
    //     (SequenceCounterService, and transmittal numbering through it).
    //
    // Isolation: each factory instance gets its OWN database, created here and
    // dropped on dispose. A shared database is not an option — SeedTestData
    // inserts the fixed TestData GUIDs, and xunit runs test classes in
    // parallel, so nine factories would collide on primary keys.
    private static string? PgConnectionString =>
        Environment.GetEnvironmentVariable("PLANSCAPE_TEST_PG");

    internal static bool UsingPostgres => !string.IsNullOrWhiteSpace(PgConnectionString);

    /// <summary>Per-factory database name, lowercased — Postgres folds unquoted identifiers.</summary>
    private readonly string _pgDatabase = $"planscape_test_{Guid.NewGuid():N}";

    private string PgTestConnectionString =>
        new Npgsql.NpgsqlConnectionStringBuilder(PgConnectionString!)
        {
            Database = _pgDatabase,
            // Keep each factory's footprint small: nine of them run in parallel
            // against one server, whose default max_connections is 100.
            MaxPoolSize = 8,
        }.ConnectionString;

    private void CreatePgDatabase()
    {
        // Connect to the maintenance database to issue CREATE DATABASE — it
        // cannot run inside a transaction or against the target itself.
        var admin = new Npgsql.NpgsqlConnectionStringBuilder(PgConnectionString!)
        {
            Database = "postgres",
        }.ConnectionString;

        using var conn = new Npgsql.NpgsqlConnection(admin);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{_pgDatabase}\"";
        cmd.ExecuteNonQuery();
    }

    private void DropPgDatabase()
    {
        try
        {
            // Npgsql pools connections per connection string; without clearing
            // them DROP DATABASE fails with "is being accessed by other users".
            //
            // ClearPool, NOT ClearAllPools. Tests stand up more than one factory
            // (AuditCategoriesConfiguredTests builds a second one mid-test), and
            // ClearAllPools would yank the pooled connections out from under
            // every other live factory in the process — a cross-test side effect
            // introduced by cleanup code, which is the worst kind.
            using (var target = new Npgsql.NpgsqlConnection(PgTestConnectionString))
                Npgsql.NpgsqlConnection.ClearPool(target);

            var admin = new Npgsql.NpgsqlConnectionStringBuilder(PgConnectionString!)
            {
                Database = "postgres",
            }.ConnectionString;

            using var conn = new Npgsql.NpgsqlConnection(admin);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_pgDatabase}\" WITH (FORCE)";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // Never fail a test run on cleanup. A leaked test database is
            // noise; a spurious failure here would hide real results.
            Console.Error.WriteLine(
                $"[test-cleanup] could not drop {_pgDatabase}: {ex.Message}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && UsingPostgres) DropPgDatabase();
    }

    /// <summary>
    /// True for a service descriptor registered through an implementation FACTORY
    /// that lives in a Hangfire assembly — the case a name check on
    /// <c>ServiceType</c> / <c>ImplementationType</c> cannot see, because a
    /// factory registration leaves <c>ImplementationType</c> null.
    ///
    /// The one that matters is <c>AddHangfireServer</c>'s
    /// <c>Hangfire.BackgroundJobServerHostedService</c> (assembly
    /// <c>Hangfire.NetCore</c>), registered as <c>IHostedService</c>. See #494.
    ///
    /// Matches on the assembly rather than the type name so a rename inside
    /// Hangfire does not silently re-open the hole this closes.
    /// </summary>
    private static bool IsHangfireFactoryRegistration(ServiceDescriptor d)
    {
        var declaringAssembly = d.ImplementationFactory?.Method.DeclaringType?.Assembly;
        var name = declaringAssembly?.GetName().Name;
        return name != null && name.StartsWith("Hangfire", StringComparison.Ordinal);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // xunit runs test classes in parallel and every request originates from the
        // same loopback IP, so the production "auth" policy (5 attempts / 5 min per
        // IP) is exhausted almost immediately and unrelated tests fail with 429
        // instead of their real assertion. Rate limiting stays ON everywhere else.
        // Program.cs fail-fasts when Jwt:Key is absent (Program.cs:104-115), so
        // EVERY host-building test died unless the developer happened to have
        // Jwt__Key exported in their shell — which is exactly why an earlier
        // "the suite is fixed" claim did not reproduce on a clean machine
        // (265 passed/155 failed clean, vs 347/73 with the var set).
        //
        // This MUST go through UseSetting, not ConfigureAppConfiguration.
        // Program.cs reads builder.Configuration["Jwt:Key"] while the host is
        // still being *built*; ConfigureAppConfiguration callbacks are applied
        // after that read, so injecting there leaves the fail-fast untouched
        // (verified — the run was byte-identical at 265/155).  UseSetting feeds
        // DeferredHostBuilder's settings, which land as an in-memory source
        // before any user code reads configuration.
        //
        // TEST-ONLY VALUE. Never leaves the in-process test host: it signs
        // tokens for an in-memory database discarded when the factory is
        // disposed. It must still clear Program.cs's guards — 32+ chars, not in
        // the banned list, 4+ distinct characters — hence the random-looking
        // literal rather than something readable like "test-key-padding-...".
        builder.UseSetting("Jwt:Key", "qZ7v3Kx9TmR2wLp8Nc5FhJd6Bs4YgVt1Ae0UnXiOrEz");

        // UseSetting, for the same reason Jwt:Key above uses it — and it is the
        // same bug, found twice. Program.cs evaluates
        //   rateLimitingEnabled = Configuration.GetValue("RateLimiting:Enabled", true)
        //                         || Environment.IsProduction()
        // while the host is being built; ConfigureAppConfiguration callbacks are
        // applied after that read, so the "false" never landed and the limiter
        // was mounted in every test host. The Redis-backed "auth" policy (5
        // attempts / 5 min per IP) then counted every test's login against one
        // loopback address, and Login_NonexistentUser_Returns401 intermittently
        // got 429 instead of 401 depending on how many logins ran before it.
        builder.UseSetting("RateLimiting:Enabled", "false");

        builder.ConfigureServices(services =>
        {
            // Remove the real DbContext registration
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<PlanscapeDbContext>));
            if (dbDescriptor != null) services.Remove(dbDescriptor);

            // Replace Hangfire's PostgreSQL storage with in-memory storage.
            //
            // This used to REMOVE every Hangfire descriptor, which broke the whole
            // WebApplicationFactory suite at startup in three separate places:
            // UseHangfireDashboard threw "Unable to find the required services",
            // and the ~40 static RecurringJob.AddOrUpdate registrations threw
            // "Current JobStorage instance has not been initialized yet". Both are
            // unconditional in Program.cs, so no test could construct a host.
            //
            // Substituting storage rather than deleting the feature keeps the
            // production startup path under test instead of routing around it.
            // No Hangfire *server* is started, so jobs are registered but never
            // executed — exactly what a controller test wants.
            var hangfireDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("Hangfire") == true
                         || d.ImplementationType?.FullName?.Contains("Hangfire") == true
                         // DEP-7 residue (#494). AddHangfireServer registers its
                         // background server as IHostedService through an
                         // implementation FACTORY, so ServiceType is
                         // Microsoft.Extensions.Hosting.IHostedService and
                         // ImplementationType is NULL — neither of the two clauses
                         // above can see it. The server therefore survived this
                         // removal and really did start, despite the comment below
                         // saying none does.
                         //
                         // Consequence: every host ran a BackgroundServerProcess,
                         // and each one raced the others' in-memory storage on
                         // teardown, logging 13 warnings per suite run:
                         //
                         //   [WRN] Server ... there was an exception, server may not be removed
                         //   System.ObjectDisposedException: ... Hangfire.InMemory.State.Dispatcher`1
                         //     at InMemoryConnection`1.RemoveServer(String serverId)
                         //     at BackgroundServerProcess.ServerDelete(...)
                         //
                         // Benign today — Hangfire catches it and no test fails —
                         // but it is the same shared-state-at-teardown class of bug
                         // DEP-7 was, still live, and still able to grow teeth.
                         || IsHangfireFactoryRegistration(d))
                .ToList();
            foreach (var d in hangfireDescriptors) services.Remove(d);

            services.AddHangfire(cfg => cfg.UseInMemoryStorage());
            // Note: AddHangfire alone registers no server, so nothing re-adds the
            // hosted service removed above. Jobs are registered but never executed
            // — exactly what a controller test wants.
            //
            // Deliberately does NOT assign Hangfire.JobStorage.Current.
            //
            // Program.cs now registers its recurring jobs through the
            // DI-resolved IRecurringJobManager, so nothing reads that
            // process-global static during host build. Assigning it here gave
            // every factory a handle on one shared object that the first
            // container to shut down disposed, so the next host to build threw
            // ObjectDisposedException (DEP-7). Each host keeps its own storage
            // and nothing crosses between them.

            if (UsingPostgres)
            {
                // Real provider: transactions, ILike and ON CONFLICT all work,
                // so nothing here needs a warning suppressed or a test skipped.
                CreatePgDatabase();
                services.AddDbContext<PlanscapeDbContext>(options =>
                    options.UseNpgsql(PgTestConnectionString));
            }
            else
            {
                services.AddDbContext<PlanscapeDbContext>(options =>
                    options.UseInMemoryDatabase(_dbName)
                        // Endpoints that wrap multi-step writes in an explicit
                        // transaction (tag sync, transmittals, search indexing)
                        // hit TransactionIgnoredWarning, which EF escalates to
                        // an exception — so the request 500'd on a limitation of
                        // the test provider rather than anything under test.
                        //
                        // The trade-off is explicit and is why the Postgres mode
                        // above exists: on InMemory these tests do not verify
                        // rollback semantics. Set PLANSCAPE_TEST_PG to get real
                        // atomicity coverage.
                        .ConfigureWarnings(w =>
                            w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId
                                .TransactionIgnoredWarning)));
            }

            // Redis-backed IDistributedCache → in-process memory. With real Redis
            // in CI, the fixed test GUIDs plus the shared "Planscape:" InstanceName
            // mean a project-visibility verdict cached by one test class
            // ("pv:{tenant}:{user}:{project}") is a live cross-test hit for a
            // parallel class that has different DB state — a flaky, order-dependent
            // false pass/fail. Give every factory instance its own
            // MemoryDistributedCache so there is nothing to bleed between hosts.
            // (The cache-outage regression test builds its own host without this
            // factory, so its coverage is unaffected.) AddDistributedMemoryCache
            // uses TryAdd, so the Redis registration must be removed first.
            var cacheDescriptors = services
                .Where(d => d.ServiceType == typeof(IDistributedCache))
                .ToList();
            foreach (var d in cacheDescriptors) services.Remove(d);
            services.AddDistributedMemoryCache();

            // Build the service provider and seed test data
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
            db.Database.EnsureCreated();
            SeedTestData(db);
        });
    }

    private static void SeedTestData(PlanscapeDbContext db)
    {
        // This runs from the ConfigureWebHost services callback, which is NOT
        // guaranteed to fire once per host: HostApplicationBuilder replays the
        // accumulated ConfigureServices delegates through
        // HostBuilderAdapter.ApplyChanges(). A second pass rebuilds the service
        // provider but keeps this factory instance's _dbName, so it re-seeds the
        // SAME in-memory store and EF InMemory throws
        // "An item with the same key has already been added. Key: 11111111-..."
        // out of host construction — which surfaces as every test in the class
        // failing, not as a seeding error. Observed only in CI (12-16 tests
        // across HandoffProvisioningTests / AuditCategoriesConfiguredTests /
        // ProjectsControllerTests); it does not reproduce locally.
        //
        // The seed is fixed-GUID and deterministic, so a presence check is a
        // complete guard: the first pass leaves exactly the state a second pass
        // would have produced. IgnoreQueryFilters because the tenant query
        // filter falls back to Guid.Empty when no tenant context is resolvable
        // here, which would match no rows and defeat the check.
        if (db.Tenants.IgnoreQueryFilters().Any(t => t.Id == TestData.TenantId))
            return;

        // Create test tenant
        var tenant = new Tenant
        {
            Id = TestData.TenantId,
            Name = "Test Organisation",
            Slug = "test-org",
            ContactEmail = "admin@test.org",
            Tier = LicenseTier.Premium,
            // Plan, not just Tier. QuotaAttribute gates writes on
            // BillingPlanLimits.For(tenant.Plan), and an unset Plan is
            // BillingPlan.Trial — which caps projects at 1. The seed below
            // already creates one, so the cap was reached before any test ran
            // and every "create project" short-circuited with 402
            // PaymentRequired. MaxProjects = 50 above is the legacy field and
            // does not feed the quota guard.
            //
            // Enterprise = unlimited on every axis, so quotas stay out of the
            // way of tests that are about something else. SeedData.cs does the
            // same for the demo sandbox, for the same reason. Quota behaviour
            // itself is covered by SecurityCriticalPathTests.
            Plan = BillingPlan.Enterprise,
            MaxUsers = 100,
            MaxProjects = 50,
            MimEnabled = true,
            IsActive = true
        };
        db.Tenants.Add(tenant);

        // Create test admin user
        var adminUser = new AppUser
        {
            Id = TestData.AdminUserId,
            TenantId = tenant.Id,
            Email = "admin@test.org",
            DisplayName = "Test Admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 4),
            Role = UserRole.Owner,
            Iso19650Role = "A",
            IsActive = true
        };
        db.Users.Add(adminUser);

        // Create a second user for multi-user tests
        var memberUser = new AppUser
        {
            Id = TestData.MemberUserId,
            TenantId = tenant.Id,
            Email = "member@test.org",
            DisplayName = "Test Member",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 4),
            Role = UserRole.Contributor, // pre-existing typo: enum has no `Member`
            Iso19650Role = "E",
            IsActive = true
        };
        db.Users.Add(memberUser);

        // Create a different tenant for isolation tests
        var otherTenant = new Tenant
        {
            Id = TestData.OtherTenantId,
            Name = "Other Organisation",
            Slug = "other-org",
            ContactEmail = "admin@other.org",
            Tier = LicenseTier.Starter,
            MaxUsers = 5,
            MaxProjects = 1,
            IsActive = true
        };
        db.Tenants.Add(otherTenant);

        var otherUser = new AppUser
        {
            Id = TestData.OtherUserId,
            TenantId = otherTenant.Id,
            Email = "admin@other.org",
            DisplayName = "Other Admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 4),
            Role = UserRole.Owner,
            Iso19650Role = "A",
            IsActive = true
        };
        db.Users.Add(otherUser);

        // Create test project
        var project = new Project
        {
            Id = TestData.ProjectId,
            TenantId = tenant.Id,
            Name = "Test BIM Project",
            Code = "TST-001",
            Phase = "Stage 4",
            Status = ProjectStatus.Active,
            TotalElements = 1000,
            TaggedElements = 800,
            CompliancePercent = 80.0
        };
        db.Projects.Add(project);

        // Create test license key
        var license = new LicenseKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Key = "STING-TEST-KEY-1234567890",
            Tier = LicenseTier.Premium,
            MimEnabled = true,
            IsActive = true,
            MaxActivations = 10,
            CurrentActivations = 0,
            ExpiresAt = DateTime.UtcNow.AddDays(365)
        };
        db.LicenseKeys.Add(license);

        db.SaveChanges();
    }

    /// <summary>
    /// Creates an HttpClient with a valid JWT token for the admin user.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string email = "admin@test.org", string password = "Password123!")
    {
        var client = CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password });
        loginResponse.EnsureSuccessStatusCode();

        var json = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = json.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>Well-known test data IDs.</summary>
public static class TestData
{
    public static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdminUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid MemberUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid OtherTenantId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid OtherUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid ProjectId = Guid.Parse("66666666-6666-6666-6666-666666666666");
}
