using Microsoft.Extensions.DependencyInjection;
using Planscape.Infrastructure.Data;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Planscape.Tests;

/// <summary>
/// The planscape.build → server identity handoff, and the account provisioning
/// it performs.
///
/// The property under test: a subscriber who has only ever existed in
/// Cloudflare D1 arrives with a signed ticket and ends up with a usable
/// account — tenant, user, and a project to put work in — without an
/// administrator touching anything.
/// </summary>
public class HandoffProvisioningTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    // Must be set before the host boots: AuthController reads this with
    // Environment.GetEnvironmentVariable, not IConfiguration, so it cannot be
    // supplied through the factory's config layer.
    private const string Secret = "test-handoff-secret-not-a-real-one-0123456789";

    public HandoffProvisioningTests(PlanscapeWebApplicationFactory factory)
    {
        _factory = factory;
        Environment.SetEnvironmentVariable("PLANSCAPE_HANDOFF_SECRET", Secret);
    }

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Mint a ticket exactly the way the Cloudflare Pages Function does.</summary>
    private static string MintTicket(
        string email, string tenantSlug, string role = "owner", int ttlSeconds = 120)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(new
        {
            jti        = Guid.NewGuid().ToString(),
            email,
            tenantSlug,
            tenantName = "Handoff Test Org",
            firstName  = "Handoff",
            lastName   = "Tester",
            role,
            tier       = "studio",
            iat        = now,
            exp        = now + ttlSeconds
        });

        var bytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return $"{B64Url(bytes)}.{B64Url(hmac.ComputeHash(bytes))}";
    }

    private async Task<JsonElement> ExchangeAsync(string ticket, HttpStatusCode expect = HttpStatusCode.OK)
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/handoff/exchange", new { ticket });
        Assert.Equal(expect, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ── Provisioning ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handoff_NewSubscriber_ProvisionsTenantUserAndStarterProject()
    {
        var email = $"fresh-{Guid.NewGuid():N}@example.com";
        var slug  = $"fresh-{Guid.NewGuid():N}"[..20];

        var session = await ExchangeAsync(MintTicket(email, slug));
        Assert.False(string.IsNullOrEmpty(session.GetProperty("accessToken").GetString()));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;

        var tenant = db.Tenants.Single(t => t.Slug == slug);
        var user   = db.Users.Single(u => u.Email == email);
        Assert.Equal(tenant.Id, user.TenantId);

        // The gap this phase closes: a handed-off subscriber used to land in an
        // account with no project at all.
        var project = db.Projects.Single(p => p.TenantId == tenant.Id);
        Assert.Equal(user.Id, project.CreatedById);

        // …and must actually be a member of it, since projects are private to
        // author + invited members + tenant admins.
        Assert.True(db.ProjectMembers.Any(m => m.ProjectId == project.Id && m.UserId == user.Id));
    }

    [Fact]
    public async Task Handoff_SecondTimeForSameTenant_DoesNotCreateASecondProject()
    {
        var email = $"repeat-{Guid.NewGuid():N}@example.com";
        var slug  = $"repeat-{Guid.NewGuid():N}"[..20];

        await ExchangeAsync(MintTicket(email, slug));
        await ExchangeAsync(MintTicket(email, slug));   // fresh jti, same identity

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;

        var tenant = db.Tenants.Single(t => t.Slug == slug);
        Assert.Equal(1, db.Projects.Count(p => p.TenantId == tenant.Id));
        Assert.Equal(1, db.Users.Count(u => u.Email == email));
    }

    [Fact]
    public async Task Handoff_ProvisionedUser_CannotLogInWithAPassword()
    {
        // The mirror account is created with an unusable random hash on purpose.
        // This is the exact reason personal access tokens exist — if this test
        // ever starts failing, the security model changed.
        var email = $"nopw-{Guid.NewGuid():N}@example.com";
        var slug  = $"nopw-{Guid.NewGuid():N}"[..20];
        await ExchangeAsync(MintTicket(email, slug));

        var client = _factory.CreateClient();
        foreach (var attempt in new[] { "", "password", "Password123!" })
        {
            var res = await client.PostAsJsonAsync("/api/auth/login",
                new { email, password = attempt });
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
    }

    [Fact]
    public async Task Handoff_ProvisionedUser_CanReachTheApiViaAPersonalAccessToken()
    {
        // The full journey this phase is for: D1-only subscriber → handoff →
        // provisioned account → PAT → headless client authenticated.
        var email = $"bridge-{Guid.NewGuid():N}@example.com";
        var slug  = $"bridge-{Guid.NewGuid():N}"[..20];

        var session = await ExchangeAsync(MintTicket(email, slug));
        var jwt = session.GetProperty("accessToken").GetString();

        var authed = _factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);

        var mint = await authed.PostAsJsonAsync("/api/auth/tokens", new { name = "stingbridge" });
        Assert.Equal(HttpStatusCode.OK, mint.StatusCode);
        var pat = (await mint.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token").GetString();

        // A brand-new client with nothing but the PAT gets a working session.
        var headless = _factory.CreateClient();
        var exch = await headless.PostAsJsonAsync("/api/auth/token/exchange", new { token = pat });
        Assert.Equal(HttpStatusCode.OK, exch.StatusCode);

        var bridgeJwt = (await exch.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();
        headless.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bridgeJwt);

        var me = await headless.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(email,
            (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString());

        // And the starter project is visible to it — the bridge needs a project id.
        var projects = await headless.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.OK, projects.StatusCode);
        var list = await projects.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(list.GetArrayLength() >= 1, "starter project should be visible to the PAT session");
    }

    // ── Ticket validation ──────────────────────────────────────────────────

    [Fact]
    public async Task Handoff_BadSignature_Returns401()
    {
        var ticket = MintTicket($"x-{Guid.NewGuid():N}@example.com", "sig-test");
        var parts = ticket.Split('.');
        // Keep the payload, corrupt the signature.
        var tampered = $"{parts[0]}.{B64Url(Encoding.UTF8.GetBytes("not-the-real-signature"))}";

        await ExchangeAsync(tampered, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Handoff_ExpiredTicket_Returns401()
    {
        var ticket = MintTicket($"exp-{Guid.NewGuid():N}@example.com", "exp-test", ttlSeconds: -10);
        await ExchangeAsync(ticket, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Handoff_MalformedTicket_Returns401()
    {
        await ExchangeAsync("not-a-ticket-at-all", HttpStatusCode.Unauthorized);
    }

    // ── Single-use / replay (DEP-6a) ─────────────────────────────────────────

    // The jti single-use guard is a Redis `SET handoff:jti:{jti} … When.NotExists`.
    // Proving it requires a REAL Redis: without one the guard FAILS OPEN (the
    // controller logs a warning and proceeds — an availability-over-integrity
    // choice, acceptable given the 120 s ticket TTL), so a replay would succeed
    // and this assertion would be meaningless. It is therefore gated on
    // PLANSCAPE_TEST_REDIS exactly like PostgresSequenceCounterTests gates on
    // PLANSCAPE_TEST_PG: **Skipped**, never falsely Passed, when absent. The
    // factory leaves the app's IConnectionMultiplexer at its default
    // `localhost:6379`, so with `PLANSCAPE_TEST_REDIS=1` and the docker Redis up
    // the guard is live end-to-end through the HTTP endpoint.
    private static string? RedisSkipReason =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLANSCAPE_TEST_REDIS"))
            ? "PLANSCAPE_TEST_REDIS is not set — no Redis to prove single-use against "
              + "(the guard fails OPEN without it, by design; see the comment above)."
            : null;

    [SkippableFact]
    public async Task Handoff_SameTicketReplayed_SecondRedemptionIsRejected()
    {
        Skip.If(RedisSkipReason is not null, RedisSkipReason!);

        var email = $"replay-{Guid.NewGuid():N}@example.com";
        var slug  = $"replay-{Guid.NewGuid():N}"[..20];

        // ONE ticket — same jti both times. A replayed URL (prefetch, back
        // button, shared link) must not mint a second session.
        var ticket = MintTicket(email, slug);

        await ExchangeAsync(ticket, HttpStatusCode.OK);            // first redemption: minted
        await ExchangeAsync(ticket, HttpStatusCode.Unauthorized);  // replay: refused by the jti guard

        // Exactly one account was provisioned — the replay created nothing.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        Assert.Equal(1, db.Users.Count(u => u.Email == email));
    }

    [Fact]
    public async Task Handoff_UnknownRole_DefaultsToViewerNotUpward()
    {
        // Role mapping must default DOWN — a rename on the D1 side must never
        // escalate privileges here.
        var email = $"role-{Guid.NewGuid():N}@example.com";
        var slug  = $"role-{Guid.NewGuid():N}"[..20];

        var session = await ExchangeAsync(MintTicket(email, slug, role: "supreme_overlord"));
        Assert.Equal("Viewer", session.GetProperty("role").GetString());
    }
}
