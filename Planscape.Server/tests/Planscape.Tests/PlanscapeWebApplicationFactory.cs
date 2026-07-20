using Hangfire;
using Microsoft.Extensions.Configuration;
using Hangfire.InMemory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            Hangfire.JobStorage.Current = new Hangfire.InMemory.InMemoryStorage();

            // Add InMemory database
            services.AddDbContext<PlanscapeDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

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
