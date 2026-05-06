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

        builder.ConfigureServices(services =>
        {
            // Remove the real DbContext registration
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<PlanscapeDbContext>));
            if (dbDescriptor != null) services.Remove(dbDescriptor);

            // Remove Hangfire services (they require PostgreSQL)
            var hangfireDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("Hangfire") == true
                         || d.ImplementationType?.FullName?.Contains("Hangfire") == true)
                .ToList();
            foreach (var d in hangfireDescriptors) services.Remove(d);

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
