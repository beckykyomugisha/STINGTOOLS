using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// A tenant must not be able to lock itself out of administration.
///
/// Every admin surface is [Authorize(Roles = "Admin,Owner")], so if the last
/// account holding one of those roles is demoted or deactivated, NOBODY can
/// administer the tenant again — including undoing the change that caused it.
/// There is no self-service recovery: the fix is a manual database edit by the
/// platform operator.
///
/// PUT /api/admin/users/{id} could do this in a single request, and it became
/// materially more reachable once demotion turned into the seat-management
/// tool ("free a seat by changing an existing author to Viewer"). The obvious
/// way to release a seat was also the way to brick the tenant.
///
/// TenantAdminController.RemoveUser already refuses to delete a UserRole.Owner,
/// so that surface was partly covered; this closes the demote and deactivate
/// routes, which were not.
/// </summary>
public class LastAdministratorGuardTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public LastAdministratorGuardTests(PlanscapeWebApplicationFactory factory)
        => _factory = factory;

    private int AdministratorCount()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        return db.Users.Count(u => u.TenantId == TestData.TenantId
                                && !u.IsDeleted && u.IsActive
                                && (u.Role == UserRole.Owner || u.Role == UserRole.Admin));
    }

    /// <summary>
    /// Puts the seeded administrator back, straight through the DbContext.
    ///
    /// Needed because these tests are written against the INTENDED behaviour:
    /// in the RED run the guard does not exist, so the demote/deactivate calls
    /// SUCCEED and the tenant really does become unadministrable — which then
    /// breaks authentication for every later test in the class. Recovering via
    /// the API is impossible by definition (that is the bug), so recovery has to
    /// bypass it. Once the guard exists these restores are no-ops.
    /// </summary>
    private void RestoreSeededAdministrator()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        var admin = db.Users.First(u => u.Id == TestData.AdminUserId);
        admin.Role = UserRole.Owner;
        admin.IsActive = true;
        db.SaveChanges();
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string role)
    {
        var res = await client.PostAsJsonAsync("/api/admin/users", new
        {
            email = $"zz-fixture-admin-{Guid.NewGuid():N}@example.com",
            displayName = "ZZ-FIXTURE Admin",
            password = "Zz-Fixture-Passw0rd!",
            role,
            iso19650Role = "M",
        });
        Assert.True(res.IsSuccessStatusCode,
            $"create({role}) failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        using var doc = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task The_last_administrator_cannot_be_demoted()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(); // admin@test.org, Owner

        // Non-vacuous: the guard only means something if there really is
        // exactly one administrator to lose.
        Assert.Equal(1, AdministratorCount());

        try
        {
            var res = await client.PutAsJsonAsync($"/api/admin/users/{TestData.AdminUserId}",
                new { role = "Viewer" });

            Assert.False(res.IsSuccessStatusCode,
                "the last administrator was demoted — the tenant is now unadministrable");
            Assert.Equal(1, AdministratorCount());
        }
        finally { RestoreSeededAdministrator(); }
    }

    [Fact]
    public async Task The_last_administrator_cannot_be_deactivated()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        Assert.Equal(1, AdministratorCount());

        try
        {
            var res = await client.PutAsJsonAsync($"/api/admin/users/{TestData.AdminUserId}",
                new { isActive = false });

            Assert.False(res.IsSuccessStatusCode,
                "the last administrator was deactivated — same lockout by another route");
            Assert.Equal(1, AdministratorCount());
        }
        finally { RestoreSeededAdministrator(); }
    }

    [Fact]
    public async Task An_administrator_can_be_demoted_while_another_remains()
    {
        // The control. A guard that refuses every demotion would pass the two
        // tests above while making role management impossible.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var secondAdmin = await CreateAsync(client, "Admin");
        Assert.Equal(2, AdministratorCount());

        var res = await client.PutAsJsonAsync($"/api/admin/users/{secondAdmin}",
            new { role = "Viewer" });

        Assert.True(res.IsSuccessStatusCode,
            $"demotion refused while another administrator remains: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        Assert.Equal(1, AdministratorCount());
    }
}
