using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Planscape.Tests;

/// <summary>
/// Single-use enforcement on the planscape.build → server handoff ticket.
///
/// A ticket travels in a URL, so a prefetch, a back button, or a shared link
/// can replay it. The guard is a Redis SET NX that fails OPEN when Redis is
/// unreachable — a deliberate availability-over-integrity trade, since the
/// worst case is a duplicate session for the same legitimate user inside a
/// 120-second window.
///
/// Both halves went untested for the same reason: no Redis runs in this suite,
/// so every exchange took the fail-open branch and the rejection path was
/// unreachable. <see cref="TestReplayGuard"/> closes that gap — it claims
/// in-memory, and can be told to simulate the store being down.
/// </summary>
public class HandoffReplayGuardTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    // AuthController reads this via Environment.GetEnvironmentVariable, not
    // IConfiguration, so it cannot come through the factory's config layer.
    private const string Secret = "test-handoff-secret-not-a-real-one-0123456789";

    public HandoffReplayGuardTests(PlanscapeWebApplicationFactory factory)
    {
        _factory = factory;
        Environment.SetEnvironmentVariable("PLANSCAPE_HANDOFF_SECRET", Secret);
    }

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Mint a ticket exactly the way the Cloudflare Pages Function does.</summary>
    private static string MintTicket(string email, string tenantSlug)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(new
        {
            jti        = Guid.NewGuid().ToString(),
            email,
            tenantSlug,
            tenantName = "Replay Test Org",
            firstName  = "Replay",
            lastName   = "Tester",
            role       = "owner",
            tier       = "studio",
            iat        = now,
            exp        = now + 120
        });

        var bytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return $"{B64Url(bytes)}.{B64Url(hmac.ComputeHash(bytes))}";
    }

    private static (string email, string slug) FreshIdentity() =>
        ($"replay-{Guid.NewGuid():N}@example.com", $"replay-{Guid.NewGuid():N}"[..20]);

    /// <summary>
    /// The guard's whole point: the same ticket must not mint two sessions.
    /// </summary>
    [Fact]
    public async Task Handoff_SameTicketTwice_SecondExchangeIsRejected()
    {
        _factory.ReplayGuard.Reset();
        var (email, slug) = FreshIdentity();
        var ticket = MintTicket(email, slug);
        var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/auth/handoff/exchange", new { ticket });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/auth/handoff/exchange", new { ticket });

        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("already used", body.GetProperty("message").GetString()!,
                        StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Distinct tickets are independent — the guard keys on jti, not on the
    /// user, so a second legitimate handoff for the same person still works.
    /// </summary>
    [Fact]
    public async Task Handoff_DistinctTickets_BothExchangeSuccessfully()
    {
        _factory.ReplayGuard.Reset();
        var (email, slug) = FreshIdentity();
        var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync(
            "/api/auth/handoff/exchange", new { ticket = MintTicket(email, slug) });
        var second = await client.PostAsJsonAsync(
            "/api/auth/handoff/exchange", new { ticket = MintTicket(email, slug) });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>
    /// Store unreachable ⇒ fail OPEN.
    ///
    /// This pins a deliberate choice, not an accident: a Redis outage must not
    /// lock every subscriber out of the product. If someone later decides
    /// integrity should win here, this test should fail and be changed on
    /// purpose.
    ///
    /// The controller also logs a warning on this path. That is NOT asserted
    /// here, and the omission is deliberate rather than an oversight: Serilog's
    /// Log.Logger is process-global, so when the full suite runs many
    /// WebApplicationFactory hosts concurrently they race over it and a
    /// controller's ILogger can resolve against a different host's pipeline. An
    /// ILoggerProvider attached to *this* factory then captures nothing, and the
    /// assertion passes alone but fails in the suite. Same family of defect as
    /// ROADMAP DEP-7 (Hangfire's process-global JobStorage.Current); see DEP-13.
    /// </summary>
    [Fact]
    public async Task Handoff_ReplayGuardUnavailable_FailsOpen()
    {
        _factory.ReplayGuard.Reset();
        _factory.ReplayGuard.SimulateOutage = true;

        try
        {
            var (email, slug) = FreshIdentity();
            var ticket = MintTicket(email, slug);
            var client = _factory.CreateClient();

            var first = await client.PostAsJsonAsync("/api/auth/handoff/exchange", new { ticket });
            var second = await client.PostAsJsonAsync("/api/auth/handoff/exchange", new { ticket });

            // Both succeed — the replay is NOT blocked when the guard is down.
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            // And the guard really was consulted both times, so "fails open"
            // means the exception path ran — not that the call was skipped.
            Assert.Equal(2, _factory.ReplayGuard.Attempts);
        }
        finally
        {
            _factory.ReplayGuard.SimulateOutage = false;
        }
    }

    /// <summary>
    /// The guard is actually consulted — guards against a future refactor that
    /// drops the call and leaves the other tests passing vacuously.
    /// </summary>
    [Fact]
    public async Task Handoff_Exchange_ConsultsTheReplayGuard()
    {
        _factory.ReplayGuard.Reset();
        var (email, slug) = FreshIdentity();
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/auth/handoff/exchange", new { ticket = MintTicket(email, slug) });

        Assert.True(_factory.ReplayGuard.Attempts > 0,
                    "handoff/exchange did not consult IReplayGuard at all");
    }
}
