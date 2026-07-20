using Microsoft.EntityFrameworkCore;
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
    // ── R7 hardening ───────────────────────────────────────────────────────

    [Fact]
    public async Task TokenPrefix_ContainsNoBytesOfTheSecret()
    {
        var (_, token, _) = await MintAsync("prefix-leak-check");

        var client = await _factory.CreateAuthenticatedClientAsync();
        var rows = await (await client.GetAsync("/api/auth/tokens"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var prefix = rows.EnumerateArray()
            .Select(r => r.GetProperty("prefix").GetString()!)
            .First(p => p.StartsWith("psat_"));

        // The prefix is stored in the clear and handed back by GET /tokens. It
        // used to be raw[..12] — "psat_" plus the first SEVEN characters of the
        // secret — which leaks part of the credential to exactly the audience
        // that hashing at rest is meant to defend against.
        var secretBody = token["psat_".Length..];
        var prefixBody = prefix["psat_".Length..];

        Assert.False(string.IsNullOrEmpty(prefixBody));
        Assert.False(secretBody.StartsWith(prefixBody, StringComparison.Ordinal),
            "the displayed prefix is a slice of the secret");
        Assert.DoesNotContain(prefixBody, secretBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expiry_DefaultsTo90Days_WhenNotRequested()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.PostAsJsonAsync("/api/auth/tokens", new { name = "default-expiry" });
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var expiresAt = json.GetProperty("expiresAt").GetDateTime();

        // A credential that lives on disk should age out on its own.
        var days = (expiresAt - DateTime.UtcNow).TotalDays;
        Assert.InRange(days, 89, 91);
    }

    [Theory]
    [InlineData(366)]
    [InlineData(3650)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Expiry_OutsideOneYear_IsRejected(int days)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.PostAsJsonAsync("/api/auth/tokens",
            new { name = "bad-expiry", expiresInDays = days });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Expiry_AtTheOneYearCeiling_IsAccepted()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.PostAsJsonAsync("/api/auth/tokens",
            new { name = "max-expiry", expiresInDays = 365 });

        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ActiveTokenCap_IsEnforcedAtTwenty()
    {
        // A fresh user, so the cap is measured against a known starting point
        // rather than whatever other tests in this class have already minted.
        var client = await _factory.CreateAuthenticatedClientAsync("member@test.org");

        for (int i = 0; i < 20; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/auth/tokens", new { name = $"cap-{i}" });
            Assert.True(ok.IsSuccessStatusCode, $"token {i} should have been allowed");
        }

        var twentyFirst = await client.PostAsJsonAsync("/api/auth/tokens", new { name = "cap-21" });
        Assert.Equal(HttpStatusCode.BadRequest, twentyFirst.StatusCode);

        // …and revoking one frees exactly one slot, so the cap is a live count
        // and not a monotonic counter.
        var rows = await (await client.GetAsync("/api/auth/tokens"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var victim = rows.EnumerateArray().First().GetProperty("id").GetString();
        (await client.DeleteAsync($"/api/auth/tokens/{victim}")).EnsureSuccessStatusCode();

        var afterRevoke = await client.PostAsJsonAsync("/api/auth/tokens", new { name = "cap-after" });
        Assert.True(afterRevoke.IsSuccessStatusCode, "revoking should free a slot");
    }

    [Fact]
    public async Task MintAndRevoke_AreWrittenToTheDurableAuditLog()
    {
        var (client, _, id) = await MintAsync("audited");
        (await client.DeleteAsync($"/api/auth/tokens/{id}")).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<Planscape.Infrastructure.Data.PlanscapeDbContext>();

        var actions = db.AuditLogs.AsQueryable().IgnoreQueryFilters()
            .Where(a => a.EntityId == id)
            .Select(a => a.Action)
            .ToList();

        // ILogger output is for operators tailing a console; "who minted a
        // long-lived credential" has to outlive log retention.
        Assert.Contains("pat_minted", actions);
        Assert.Contains("pat_revoked", actions);
    }

    [Fact]
    public async Task RepeatedFailedExchanges_LogAtMostOneAuditRowPerTokenPerDay()
    {
        var (client, token, id) = await MintAsync("burst");
        (await client.DeleteAsync($"/api/auth/tokens/{id}")).EnsureSuccessStatusCode();

        var anon = _factory.CreateClient();
        for (int i = 0; i < 5; i++)
        {
            var res = await anon.PostAsJsonAsync("/api/auth/token/exchange", new { token });
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<Planscape.Infrastructure.Data.PlanscapeDbContext>();

        var denials = db.AuditLogs.AsQueryable().IgnoreQueryFilters()
            .Count(a => a.EntityId == id && a.Action == "pat_exchange_denied");

        // A revoked token baked into a CI job retries on every build. One row
        // per attempt would make the audit log an unbounded write amplifier
        // driven by an unauthenticated caller.
        Assert.Equal(1, denials);
    }

}
