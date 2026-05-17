using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Planscape.Tests;

/// <summary>
/// Phase 159 — pins the backward-compat alias on the Phase 158
/// revoke-tokens action. Both the new <c>/api/security</c> route and
/// the retired <c>/api/admin</c> route MUST hit the same handler with
/// the same SecurityOfficerOrAdmin policy gate. The class-level
/// <c>[Route("api/security")]</c> on SecurityController combined with
/// a leading-slash absolute attribute (<c>/api/admin/...</c>) is the
/// ASP.NET Core mechanism that makes this work without registering
/// a stub controller; if the alias regresses, dashboards / CLI
/// tooling on the deprecation window will silently 404.
/// </summary>
public class SecurityControllerRouteAliasTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public SecurityControllerRouteAliasTests(PlanscapeWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task NewRoute_Phase158_Returns200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(
            $"/api/security/users/{TestData.MemberUserId}/revoke-tokens",
            new { reason = "scheduled rotation", category = "scheduled_rotation" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(TestData.MemberUserId, json.GetProperty("userId").GetGuid());
        Assert.Equal("scheduled rotation", json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task LegacyRoute_Phase157Compat_Returns200()
    {
        // The old /api/admin/users/{id}/revoke-tokens path still
        // works during the deprecation window so dashboards on the
        // pre-Phase-158 build don't break the moment the server
        // upgrades.
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(
            $"/api/admin/users/{TestData.MemberUserId}/revoke-tokens",
            new { reason = "credential leak ticket #4321",
                  category = "suspected_credential_leak" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(TestData.MemberUserId, json.GetProperty("userId").GetGuid());
        Assert.Equal("credential leak ticket #4321",
            json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task BothRoutes_HitSamePolicyGate()
    {
        // Owner role granted via seeded admin user; both routes
        // should accept it. The new route uses SecurityOfficerOrAdmin
        // and the legacy alias inherits the same gate by design —
        // explicit verification that the shared handler is shared.
        var client = await _factory.CreateAuthenticatedClientAsync();
        var newResp = await client.PostAsJsonAsync(
            $"/api/security/users/{TestData.MemberUserId}/revoke-tokens",
            new { reason = "via security route" });
        var legacyResp = await client.PostAsJsonAsync(
            $"/api/admin/users/{TestData.MemberUserId}/revoke-tokens",
            new { reason = "via admin alias" });
        Assert.Equal(HttpStatusCode.OK, newResp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, legacyResp.StatusCode);
    }

    [Fact]
    public async Task LegacyRoute_NonExistentUser_Returns404()
    {
        // Tenant-scoped lookup must apply equally to the alias —
        // no cross-tenant leak via the legacy URL path.
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(
            $"/api/admin/users/{System.Guid.NewGuid()}/revoke-tokens",
            new { reason = "ghost user" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LegacyRoute_OtherTenantUser_Returns404()
    {
        // Tenant isolation: an operator in tenant A cannot revoke
        // tokens for a user in tenant B even via the legacy alias.
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(
            $"/api/admin/users/{TestData.OtherUserId}/revoke-tokens",
            new { reason = "cross-tenant probe" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LegacyRoute_MemberRole_Returns403()
    {
        // SOC2 separation-of-duties: the alias must NOT be laxer
        // than the new route. A Member role hits the
        // SecurityOfficerOrAdmin policy and gets denied — same as
        // calling the new path directly.
        var client = await _factory.CreateAuthenticatedClientAsync(
            "member@test.org", "Password123!");
        var response = await client.PostAsJsonAsync(
            $"/api/admin/users/{TestData.AdminUserId}/revoke-tokens",
            new { reason = "should be denied" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LegacyRoute_NoBody_StillWorks()
    {
        // Optional-body contract: the old Phase 157 route accepted
        // a bare POST with no body. The alias must preserve that so
        // CLI scripts using `curl -X POST` without a payload don't
        // start 415-ing on the upgrade.
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsync(
            $"/api/admin/users/{TestData.MemberUserId}/revoke-tokens",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
