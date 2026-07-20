using Hangfire;
using Microsoft.Extensions.Configuration;
using Hangfire.InMemory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// In-memory stand-in for the Redis replay guard. Tests drive single-use
    /// behaviour through this — including simulating the store being down.
    /// </summary>
    public TestReplayGuard ReplayGuard { get; } = new();

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

        builder.ConfigureAppConfiguration(cfg =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "false"
            }));

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
                         || d.ImplementationType?.FullName?.Contains("Hangfire") == true)
                .ToList();
            foreach (var d in hangfireDescriptors) services.Remove(d);

            services.AddHangfire(cfg => cfg.UseInMemoryStorage());
            // Static RecurringJob.* APIs read JobStorage.Current, which the DI
            // registration alone does not set.
            //
            Hangfire.JobStorage.Current = new Hangfire.InMemory.InMemoryStorage();

            // Add InMemory database.
            //
            // The InMemory provider has no transactions and raises
            // TransactionIgnoredWarning as an ERROR by default, so any handler
            // calling BeginTransactionAsync (TagSyncController does, at
            // RepeatableRead) threw and returned 500. Downgrading it to a log
            // keeps those paths testable. The isolation semantics genuinely are
            // not exercised here — that belongs to the real-Postgres suite
            // (see PostgresSequenceCounterTests).
            services.AddDbContext<PlanscapeDbContext>(options =>
                options.UseInMemoryDatabase(_dbName)
                       .ConfigureWarnings(w =>
                           w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

            // Replay guard: the production implementation is a Redis SET NX, and
            // no Redis is reachable here, so every call threw and the caller's
            // fail-open branch swallowed it — the *blocking* half of the guard
            // was unreachable from a test. Substituting an in-memory claim store
            // makes both halves drivable (see ReplayGuard).
            var rgDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(Planscape.Core.Interfaces.IReplayGuard));
            if (rgDescriptor != null) services.Remove(rgDescriptor);
            services.AddSingleton<Planscape.Core.Interfaces.IReplayGuard>(ReplayGuard);

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
        // Create test tenant
        var tenant = new Tenant
        {
            Id = TestData.TenantId,
            Name = "Test Organisation",
            Slug = "test-org",
            ContactEmail = "admin@test.org",
            Tier = LicenseTier.Premium,
            MaxUsers = 100,
            MaxProjects = 50,
            // The Quota filter caps by BillingPlan, NOT the legacy MaxProjects
            // field. Left unset this defaults to Plan=Trial, which now allows
            // ONE project — and one is already seeded below, so every "create
            // project" 402'd before reaching the controller. SeedData.cs:60-63
            // hit and documented the same trap for the demo tenant.
            Plan = BillingPlan.Enterprise,
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

        // Create test project.
        //
        // CreatedById and the ProjectMember row below are load-bearing. Projects
        // became visible only to tenant admins, the author, and active members;
        // this project had none of the three, so every [ProjectAccess]-guarded
        // endpoint 404'd for every caller and ~28 tests failed downstream of it.
        var project = new Project
        {
            Id = TestData.ProjectId,
            TenantId = tenant.Id,
            Name = "Test BIM Project",
            Code = "TST-001",
            Phase = "Stage 4",
            Status = ProjectStatus.Active,
            CreatedById = adminUser.Id,
            TotalElements = 1000,
            TaggedElements = 800,
            CompliancePercent = 80.0
        };
        db.Projects.Add(project);

        // A low-privilege user who IS on the project.
        //
        // Tests asserting "a non-manager cannot do X" need this: the access
        // filter deliberately 404s a project you are not a member of (a 403
        // would confirm the project exists), so it answers before the role
        // check the test is actually aiming at. memberUser deliberately stays
        // OFF the project so Members_AddAndList still has someone to add.
        var viewerUser = new AppUser
        {
            Id = TestData.ViewerUserId,
            TenantId = tenant.Id,
            Email = "viewer@test.org",
            DisplayName = "Test Viewer",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 4),
            Role = UserRole.Contributor,
            Iso19650Role = "E",
            IsActive = true
        };
        db.Users.Add(viewerUser);

        db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProjectId = project.Id,
            UserId = viewerUser.Id,
            ProjectRole = "Viewer",
            Iso19650Role = "E",
            IsActive = true
        });

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

    /// <summary>Low-privilege user who IS an active member of <see cref="ProjectId"/>.</summary>
    public static readonly Guid ViewerUserId = Guid.Parse("77777777-7777-7777-7777-777777777777");
}
