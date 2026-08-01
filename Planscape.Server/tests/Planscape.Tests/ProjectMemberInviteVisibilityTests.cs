using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Planscape.Tests;

/// <summary>
/// Reported: "I added another project member but it removed itself and could
/// not reflect in the platform — it couldn't get saved."
///
/// The invite endpoint returns 200 and calls SaveChangesAsync, so the row
/// commits. The question is whether the member is then VISIBLE to the very
/// next list call. Anything that writes the row outside the reader's tenant
/// scope — ProjectMember is ITenantScoped and the read has a global
/// TenantId == CurrentTenantId filter — produces exactly the reported
/// symptom: saved, returns success, never appears again.
///
/// This drives the real round-trip (invite -> list) through the real
/// container so the answer is measured rather than theorised.
/// </summary>
public class ProjectMemberInviteVisibilityTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public ProjectMemberInviteVisibilityTests(PlanscapeWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task InvitedMember_AppearsInTheMemberList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var email = $"invitee-{Guid.NewGuid():N}@example.com";

        var invite = await client.PostAsJsonAsync(
            $"/api/projects/{TestData.ProjectId}/members/invite",
            new { email, projectRole = "Contributor", iso19650Role = "M" });

        var inviteBody = await invite.Content.ReadAsStringAsync();
        Assert.True(invite.IsSuccessStatusCode,
            $"invite failed: {(int)invite.StatusCode} {inviteBody}");

        // The exact call the members page makes immediately after inviting.
        var list = await client.GetAsync($"/api/projects/{TestData.ProjectId}/members");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var members = await list.Content.ReadFromJsonAsync<JsonElement>();
        var emails = members.EnumerateArray()
            .Select(m => m.TryGetProperty("email", out var e) ? e.GetString() : null)
            .Where(e => e != null)
            .ToList();

        Assert.True(emails.Contains(email),
            $"invited '{email}' is missing from the list — saved but invisible. " +
            $"list returned: {string.Join(", ", emails)}");
    }
}
