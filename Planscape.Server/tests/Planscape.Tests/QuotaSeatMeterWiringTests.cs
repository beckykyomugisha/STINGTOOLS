using System.Net.Http.Json;
using System.Text.Json;

namespace Planscape.Tests;

/// <summary>
/// The seat meter must count the seats the product actually sells.
///
/// THE DEFECT THESE PIN
/// --------------------
/// `POST /api/tenant/invite` gates on the coordinator seat axis: it calls
/// `IQuotaGuardService.CheckCanAddUserAsync(role)` and returns
/// **402 quota_exceeded** when that axis is full. So the endpoint's own
/// contract says an invite consumes a seat.
///
/// It then creates an `AppUser` row and nothing else — no `ProjectMember`.
/// But every reader of the seat axes counts `ProjectMembers`:
///
///   QuotaGuardService.CountAsync      ProjectMembers, split on ProjectRole
///   TenantAdminController.Dashboard   ProjectMembers, same split inlined again
///
/// So the endpoint gates on a number it can never move. Invite a thousand
/// coordinators and `usage.coordinators.current` stays where it was; the 402
/// fires on a count driven entirely by a different code path
/// (`ProjectMembersController`), which never consults the guard at all.
///
/// This is upstream of the author/coordinator split being inverted. Correcting
/// which roles land on which axis would still leave the meter reading a table
/// that the metered operation does not write — so these tests come first.
///
/// EXPECTED STATE
/// --------------
/// RED on current main: the count does not move. They are written against the
/// INTENDED behaviour, not the current behaviour, so they go GREEN when the
/// meter is wired to the writes.
///
/// The test tenant is `BillingPlan.Enterprise` (unlimited seats), so the quota
/// check always allows and these tests isolate the *counting*, never the gate.
/// </summary>
public class QuotaSeatMeterWiringTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public QuotaSeatMeterWiringTests(PlanscapeWebApplicationFactory factory)
        => _factory = factory;

    private sealed record Seats(int Authors, int Coordinators, int MemberSeats, int UserCount);

    /// <summary>
    /// Reads the seat counters from the surface the customer is billed against.
    /// Asserts the payload actually resolved a tenant: `ITenantContext.TenantId`
    /// falls back to `Guid.Empty`, which matches no rows, and every count would
    /// then read 0 — an increment assertion against an unresolved tenant would
    /// be measuring nothing.
    /// </summary>
    private static async Task<Seats> ReadSeatsAsync(HttpClient client)
    {
        var res = await client.GetAsync("/api/tenant/dashboard");
        Assert.True(res.IsSuccessStatusCode,
            $"tenant dashboard failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var usage = root.GetProperty("usage");

        var users = root.GetProperty("users").GetArrayLength();
        Assert.True(users > 0,
            "The dashboard reported zero users, so the tenant did not resolve and " +
            "these counts prove nothing. Check ITenantContext in the test host.");

        return new Seats(
            usage.GetProperty("authors").GetProperty("current").GetInt32(),
            usage.GetProperty("coordinators").GetProperty("current").GetInt32(),
            usage.GetProperty("memberSeats").GetInt32(),
            users);
    }

    private static async Task<Guid> InviteAsync(HttpClient client, string role)
    {
        var email = $"zz-fixture-seat-{Guid.NewGuid():N}@example.com";
        var res = await client.PostAsJsonAsync("/api/tenant/invite",
            new { email, displayName = "ZZ-FIXTURE Seat Probe", role });

        Assert.True(res.IsSuccessStatusCode,
            $"invite({role}) failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task CleanUpAsync(HttpClient client, Guid userId)
        => await client.DeleteAsync($"/api/tenant/users/{userId}");

    [Fact]
    public async Task Inviting_a_coordinator_consumes_a_coordinator_seat()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(); // Owner

        var before = await ReadSeatsAsync(client);
        var userId = await InviteAsync(client, "Coordinator");
        try
        {
            var after = await ReadSeatsAsync(client);

            // The invite succeeded, so a coordinator seat was sold. The surface
            // the customer is billed against has to show it.
            Assert.Equal(before.Coordinators + 1, after.Coordinators);
        }
        finally
        {
            await CleanUpAsync(client, userId);
        }
    }

    [Fact]
    public async Task Inviting_an_author_consumes_an_author_seat()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var before = await ReadSeatsAsync(client);
        var userId = await InviteAsync(client, "Author");
        try
        {
            var after = await ReadSeatsAsync(client);

            // `/api/tenant/invite` routes "Author" to the Authors axis and gates
            // on MaxAuthors, so an accepted author invite must consume an author
            // seat. Today the author axis is pinned at 0 for a second, separate
            // reason — nothing ever writes ProjectRole = "Author" — but this
            // assertion is about the seat being counted at all.
            Assert.Equal(before.Authors + 1, after.Authors);
        }
        finally
        {
            await CleanUpAsync(client, userId);
        }
    }

    [Fact]
    public async Task An_invited_user_is_visible_to_the_tenant_even_though_no_seat_moves_today()
    {
        // The control. It proves the invite genuinely persisted a user, so the
        // two failures above are the METER failing to count a real seat — not
        // the invite silently doing nothing. Without this, a broken invite and
        // a broken meter are indistinguishable.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var before = await ReadSeatsAsync(client);
        var userId = await InviteAsync(client, "Coordinator");
        try
        {
            var after = await ReadSeatsAsync(client);
            Assert.Equal(before.UserCount + 1, after.UserCount);
        }
        finally
        {
            await CleanUpAsync(client, userId);
        }
    }
}
