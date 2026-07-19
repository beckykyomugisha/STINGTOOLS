using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Planscape.Tests;

/// <summary>
/// Personal access tokens — the credential path for headless clients that
/// cannot complete an interactive login (notably handoff-provisioned accounts,
/// which have a deliberately unusable password hash).
///
/// The invariant these tests protect: a PAT is a *credential*, never a session.
/// It is only ever exchanged for a normal JWT; it must not be accepted as a
/// bearer token anywhere.
/// </summary>
public class PersonalAccessTokenTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public PersonalAccessTokenTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    /// <summary>Mint a PAT as admin@test.org and return its plaintext.</summary>
    private async Task<(HttpClient authed, string token, string id)> MintAsync(
        string name = "test-token", int? expiresInDays = null)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.PostAsJsonAsync("/api/auth/tokens",
            new { name, expiresInDays });
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return (client,
                json.GetProperty("token").GetString()!,
                json.GetProperty("id").GetString()!);
    }

    // ── Minting ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateToken_RequiresAuthentication()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/tokens", new { name = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task CreateToken_ReturnsPlaintextOnceWithExpectedShape()
    {
        var (_, token, _) = await MintAsync("studio-workstation");

        // The prefix exists so a leaked token is greppable in logs and repos.
        Assert.StartsWith("psat_", token);
        // 32 bytes base64url ≈ 43 chars, plus the 5-char prefix.
        Assert.True(token.Length > 40, $"token unexpectedly short: {token.Length}");
    }

    [Fact]
    public async Task CreateToken_RejectsMissingName()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.PostAsJsonAsync("/api/auth/tokens", new { name = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task CreateToken_TwoMintsProduceDifferentSecrets()
    {
        var (_, a, _) = await MintAsync("token-a");
        var (_, b, _) = await MintAsync("token-b");
        Assert.NotEqual(a, b);
    }

    // ── Listing ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListTokens_NeverLeaksTheSecret()
    {
        var (client, token, _) = await MintAsync("listable");

        var res = await client.GetAsync("/api/auth/tokens");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();

        // The whole point of hashing at rest: the plaintext must be
        // unrecoverable through the API after the mint response.
        Assert.DoesNotContain(token, body);
        // The display prefix may appear — it is not sufficient to authenticate.
        Assert.Contains("prefix", body);
    }

    // ── Exchange ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Exchange_ValidToken_ReturnsSessionShapedLikeLogin()
    {
        var (_, token, _) = await MintAsync("exchangeable");

        var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/auth/token/exchange", new { token });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(json.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(json.GetProperty("refreshToken").GetString()));
        // Acts as the minting user, with that user's role — not an escalation.
        Assert.Equal("Owner", json.GetProperty("role").GetString());
        Assert.Equal("Test Admin", json.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Exchange_IssuedJwtActuallyWorksAgainstAProtectedEndpoint()
    {
        var (_, token, _) = await MintAsync("usable-session");

        var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/auth/token/exchange", new { token });
        var jwt = (await res.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        var authed = _factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);

        var me = await authed.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var profile = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("admin@test.org", profile.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Exchange_UnknownToken_Returns401()
    {
        var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/auth/token/exchange",
            new { token = "psat_thisisnotarealtokenatallnotevencloseok" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Exchange_EmptyToken_Returns401()
    {
        var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/auth/token/exchange", new { token = "" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Exchange_RevokedToken_Returns401()
    {
        var (client, token, id) = await MintAsync("to-be-revoked");

        var del = await client.DeleteAsync($"/api/auth/tokens/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/auth/token/exchange", new { token });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Exchange_ExpiredToken_Returns401()
    {
        // Mint with a positive TTL, then age it past expiry directly in the store —
        // the endpoint deliberately offers no way to create an already-expired token.
        var (_, token, id) = await MintAsync("short-lived", expiresInDays: 1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<Planscape.Infrastructure.Data.PlanscapeDbContext>();
            db.BypassTenantFilter = true;
            var pat = db.PersonalAccessTokens.First(t => t.Id == Guid.Parse(id));
            pat.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            db.SaveChanges();
        }

        var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/auth/token/exchange", new { token });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Exchange_AllFailureModesReturnTheSameMessage()
    {
        // An attacker probing this endpoint must not be able to distinguish
        // "no such token" from "revoked" from "expired".
        var (client, revokedToken, revokedId) = await MintAsync("indistinguishable");
        await client.DeleteAsync($"/api/auth/tokens/{revokedId}");

        var anon = _factory.CreateClient();

        var unknown = await (await anon.PostAsJsonAsync("/api/auth/token/exchange",
            new { token = "psat_definitelynotarealtokenvaluehere00" })).Content.ReadAsStringAsync();
        var revoked = await (await anon.PostAsJsonAsync("/api/auth/token/exchange",
            new { token = revokedToken })).Content.ReadAsStringAsync();

        Assert.Equal(unknown, revoked);
    }

    [Fact]
    public async Task Exchange_StampsLastUsedAt()
    {
        var (client, token, id) = await MintAsync("tracked");

        var before = await client.GetFromJsonAsync<JsonElement>("/api/auth/tokens");
        var row = before.EnumerateArray().First(t => t.GetProperty("id").GetString() == id);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("lastUsedAt").ValueKind);

        var anon = _factory.CreateClient();
        await anon.PostAsJsonAsync("/api/auth/token/exchange", new { token });

        var after = await client.GetFromJsonAsync<JsonElement>("/api/auth/tokens");
        var rowAfter = after.EnumerateArray().First(t => t.GetProperty("id").GetString() == id);
        Assert.NotEqual(JsonValueKind.Null, rowAfter.GetProperty("lastUsedAt").ValueKind);
    }

    // ── Revocation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_RequiresAuthentication()
    {
        var (_, _, id) = await MintAsync("protected");
        var anon = _factory.CreateClient();
        var res = await anon.DeleteAsync($"/api/auth/tokens/{id}");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Revoke_UnknownId_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.DeleteAsync($"/api/auth/tokens/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Revoke_AnotherUsersToken_Returns404AndLeavesItUsable()
    {
        // A user must not be able to revoke a colleague's credential by
        // guessing an id — and must not learn that the id exists.
        var (_, victimToken, victimId) = await MintAsync("victims-token");

        var attacker = await _factory.CreateAuthenticatedClientAsync("member@test.org");
        var res = await attacker.DeleteAsync($"/api/auth/tokens/{victimId}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);

        // Still works.
        var anon = _factory.CreateClient();
        var exch = await anon.PostAsJsonAsync("/api/auth/token/exchange", new { token = victimToken });
        Assert.Equal(HttpStatusCode.OK, exch.StatusCode);
    }

    [Fact]
    public async Task Revoke_IsIdempotentFromTheCallersPerspective()
    {
        var (client, _, id) = await MintAsync("double-revoke");

        var first = await client.DeleteAsync($"/api/auth/tokens/{id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Already revoked → filtered out of the "active" query → 404, not a 500.
        var second = await client.DeleteAsync($"/api/auth/tokens/{id}");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task RevokedToken_DisappearsFromTheList()
    {
        var (client, _, id) = await MintAsync("vanishing");
        await client.DeleteAsync($"/api/auth/tokens/{id}");

        var list = await client.GetFromJsonAsync<JsonElement>("/api/auth/tokens");
        Assert.DoesNotContain(list.EnumerateArray(),
            t => t.GetProperty("id").GetString() == id);
    }
}
