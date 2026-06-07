using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// Locks in the invite / accept-token contract behind the invite deep link.
/// The invite token is stored as <c>RESET:&lt;sha256-hex&gt;</c> and consumed by
/// <c>AuthController.ResetPassword</c> (the same flow the email link uses), which
/// enforces:
///   • expiry — the lookup requires <c>RefreshTokenExpiresAt &gt; UtcNow</c>; an
///     expired token returns 400; invite tokens are minted with a 7-day window
///     (Auth:InviteTokenExpiryDays).
///   • single-use — on success the token is nulled, so a second use no longer
///     matches and returns 400.
/// </summary>
public class InviteTokenTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;
    public InviteTokenTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    // Mirrors AuthController.HashResetToken / the invite mint: hex SHA-256 of the
    // raw token. The DB only ever holds the hash; the raw value is what a real
    // invite email carries.
    private static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private async Task<string> SeedPendingInviteeAsync(string email, DateTime expiresAt)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.Users.Add(new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = TestData.TenantId,
            Email = email,
            DisplayName = email.Split('@')[0],
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), workFactor: 4),
            Role = UserRole.Contributor,
            Iso19650Role = "M",
            IsActive = false,                       // pending — never activated
            RefreshToken = $"RESET:{Hash(raw)}",
            RefreshTokenExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
        return raw;
    }

    [Fact]
    public async Task InviteToken_IsSingleUse()
    {
        var token = await SeedPendingInviteeAsync("pending-singleuse@test.org", DateTime.UtcNow.AddDays(7));
        var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { token, newPassword = "BrandNewPass1!" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Reusing the same token must fail — it was consumed on first use.
        var second = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { token, newPassword = "AnotherPass2!" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task InviteToken_ExpiredIsRejected()
    {
        var token = await SeedPendingInviteeAsync("pending-expired@test.org", DateTime.UtcNow.AddMinutes(-5));
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { token, newPassword = "BrandNewPass1!" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
