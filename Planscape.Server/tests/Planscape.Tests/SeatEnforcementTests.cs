using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// Only AUTHOR accounts consume a seat. Viewers are unlimited and free.
///
/// WHAT CHANGES, PRECISELY
/// -----------------------
/// AdminController and ProjectMembersController were the only two paths that
/// enforced <c>tenant.MaxUsers</c>, and they counted TOTAL active accounts. So a
/// Studio tenant was capped at 6 accounts of any kind: the seventh person was
/// refused even if they were read-only. After this change both count authoring
/// accounts against the plan's author cap, and read-only accounts are not
/// counted at all.
///
/// A tenant AT its author cap:
///   before — could add nobody at all, of any role.
///   after  — can add unlimited viewers; still cannot add another author, and
///            still cannot promote a viewer into one.
///
/// The promotion path is new enforcement and it is not optional: without it,
/// "viewers are free" is a bypass — mint unlimited free viewers, then promote
/// them all.
///
/// Each test class gets its own database (the factory names it
/// PlanscapeTest_{Guid}), so mutating the tenant's plan here cannot leak into
/// another class.
/// </summary>
public class SeatEnforcementTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public SeatEnforcementTests(PlanscapeWebApplicationFactory factory)
        => _factory = factory;

    // ── fixture helpers ─────────────────────────────────────────────────────

    private void SetPlan(BillingPlan plan, bool pinMaxUsersToCurrentCount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;

        var tenant = db.Tenants.First(t => t.Id == TestData.TenantId);
        tenant.Plan = plan;

        if (pinMaxUsersToCurrentCount)
        {
            // Makes the OLD total-account check refuse everything, so a test
            // that then succeeds can only have succeeded via the new
            // author-seat rule. Without this the old rule might simply have had
            // headroom, and the test would prove nothing.
            tenant.MaxUsers = db.Users.Count(u => u.TenantId == TestData.TenantId && u.IsActive);
        }
        db.SaveChanges();
    }

    private int AuthorCount()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        return db.Users
            .Where(u => u.TenantId == TestData.TenantId && !u.IsDeleted)
            .Count(u => u.Role != UserRole.Viewer && u.Role != UserRole.SecurityOfficer);
    }

    private static string ZzEmail() => $"zz-fixture-seat-{Guid.NewGuid():N}@example.com";

    private static async Task<HttpResponseMessage> CreateUserAsync(
        HttpClient client, string role, string? email = null)
        => await client.PostAsJsonAsync("/api/admin/users", new
        {
            email = email ?? ZzEmail(),
            displayName = "ZZ-FIXTURE Seat",
            password = "Zz-Fixture-Passw0rd!",
            role,
            iso19650Role = "M",
        });

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage res)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    // ── AdminController: POST /api/admin/users ──────────────────────────────

    [Fact]
    public async Task At_the_author_cap_a_viewer_can_still_be_created()
    {
        // Trial caps authors at 1; the seeded tenant already has two authoring
        // accounts (Owner + Contributor), so it is over the author cap already.
        SetPlan(BillingPlan.Trial, pinMaxUsersToCurrentCount: true);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var authorsBefore = AuthorCount();
        var res = await CreateUserAsync(client, "Viewer");

        Assert.True(res.IsSuccessStatusCode,
            $"a free viewer was refused: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");

        // And it must not have consumed a seat.
        Assert.Equal(authorsBefore, AuthorCount());
    }

    [Fact]
    public async Task At_the_author_cap_an_authoring_account_is_refused()
    {
        // The control. If this ever passes, "viewers are free" has become
        // "everything is free".
        SetPlan(BillingPlan.Trial, pinMaxUsersToCurrentCount: false);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var res = await CreateUserAsync(client, "Contributor");

        Assert.False(res.IsSuccessStatusCode,
            "an authoring account was created beyond the plan's author cap");
        Assert.NotEqual(HttpStatusCode.InternalServerError, res.StatusCode);
    }

    [Fact]
    public async Task A_viewer_cannot_be_promoted_past_the_author_cap()
    {
        // Without this, free viewers are a bypass: mint them, then promote.
        SetPlan(BillingPlan.Trial, pinMaxUsersToCurrentCount: false);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await CreateUserAsync(client, "Viewer");
        Assert.True(created.IsSuccessStatusCode,
            $"viewer create failed: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");
        var userId = await CreatedIdAsync(created);

        var authorsBefore = AuthorCount();

        var promote = await client.PutAsJsonAsync($"/api/admin/users/{userId}",
            new { role = "Contributor" });

        Assert.False(promote.IsSuccessStatusCode,
            "a viewer was promoted into an authoring seat beyond the plan's author cap");
        Assert.Equal(authorsBefore, AuthorCount());
    }

    [Fact]
    public async Task Freeing_a_seat_makes_it_immediately_reusable()
    {
        // Seat reassignment with no lock-in: demote an author, and the seat is
        // available on the very next request. Small practices rotate staff.
        SetPlan(BillingPlan.Studio, pinMaxUsersToCurrentCount: false);   // 6 author seats
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Fill to the cap. Seeded tenant starts with 2 authoring accounts.
        var filled = new List<Guid>();
        while (AuthorCount() < 6)
        {
            var r = await CreateUserAsync(client, "Contributor");
            Assert.True(r.IsSuccessStatusCode,
                $"filling to cap failed at {AuthorCount()} authors: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
            filled.Add(await CreatedIdAsync(r));
        }
        Assert.Equal(6, AuthorCount());

        // At the cap, the next author is refused.
        var refused = await CreateUserAsync(client, "Contributor");
        Assert.False(refused.IsSuccessStatusCode, "created a 7th author on a 6-seat plan");

        // Demote one — the reassignment.
        var demote = await client.PutAsJsonAsync($"/api/admin/users/{filled[0]}",
            new { role = "Viewer" });
        Assert.True(demote.IsSuccessStatusCode,
            $"demotion failed: {(int)demote.StatusCode} {await demote.Content.ReadAsStringAsync()}");
        Assert.Equal(5, AuthorCount());

        // Immediately reusable — no cooling-off, no billing period boundary.
        var reused = await CreateUserAsync(client, "Contributor");
        Assert.True(reused.IsSuccessStatusCode,
            $"a freed seat was not immediately reusable: {(int)reused.StatusCode} {await reused.Content.ReadAsStringAsync()}");
        Assert.Equal(6, AuthorCount());
    }

    // ── ProjectMembersController: POST /api/projects/{id}/members/invite ─────

    [Fact]
    public async Task Project_invite_is_capped_by_author_seats_not_total_accounts()
    {
        // Studio allows 6 authors. Pinning MaxUsers to the current headcount
        // makes the OLD total-account rule refuse the invite outright, so a
        // success here can only come from the new author-seat rule.
        SetPlan(BillingPlan.Studio, pinMaxUsersToCurrentCount: true);
        var client = await _factory.CreateAuthenticatedClientAsync();

        Assert.True(AuthorCount() < 6, "fixture must be below the author cap for this to mean anything");

        var res = await client.PostAsJsonAsync(
            $"/api/projects/{TestData.ProjectId}/members/invite",
            new { email = ZzEmail(), displayName = "ZZ-FIXTURE Invitee", iso19650Role = "M" });

        Assert.True(res.IsSuccessStatusCode,
            $"invite refused while author seats remain: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }
}
