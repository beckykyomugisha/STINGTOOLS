using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API;

/// <summary>
/// Idempotent startup task that seeds the operator Owner account into the
/// well-known 'planscape' platform tenant (created by
/// <see cref="PlatformTenantSeeder"/>, which logs "Operator should add their
/// Owner account now"). Lives in the API project because that's where the
/// BCrypt password hasher is referenced (the Infrastructure project doesn't
/// pull BCrypt).
///
/// Configuration (all optional):
///   Platform:OwnerEmail     (env PLANSCAPE_OWNER_EMAIL)    default davis@planscape.build
///   Platform:OwnerName      (env PLANSCAPE_OWNER_NAME)     default "Davis"
///   Platform:OwnerPassword  (env PLANSCAPE_OWNER_PASSWORD) — REQUIRED to make the
///       account loginable. When absent the account is still created (so it can be
///       password-reset) but with a random, unknown hash, never a guessable default.
///
/// Idempotent: if a user with the resolved email already exists, it no-ops —
/// so it never clobbers a password the operator has since changed.
/// </summary>
public static class PlatformOwnerSeeder
{
    public static async Task EnsureAsync(
        PlanscapeDbContext db,
        IConfiguration config,
        ILogger logger,
        CancellationToken ct = default)
    {
        // Startup runs without an HTTP context, so the tenant query filter
        // returns nothing unless we bypass it (same pattern as the other seeders).
        db.BypassTenantFilter = true;

        string email = (config["Platform:OwnerEmail"]
            ?? Environment.GetEnvironmentVariable("PLANSCAPE_OWNER_EMAIL")
            ?? "davis@planscape.build").Trim();
        if (string.IsNullOrWhiteSpace(email)) return;

        // Idempotent — never overwrite an account whose password may have changed.
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return;

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == PlatformTenantSeeder.PlatformSlug, ct);
        if (tenant == null)
        {
            logger.LogWarning("PlatformOwnerSeeder: platform tenant '{Slug}' missing — skipping owner {Email}.",
                PlatformTenantSeeder.PlatformSlug, email);
            return;
        }

        string? password = config["Platform:OwnerPassword"]
            ?? Environment.GetEnvironmentVariable("PLANSCAPE_OWNER_PASSWORD");

        bool loginable = !string.IsNullOrWhiteSpace(password);
        // When no password is configured, seed a random unknown hash so the
        // account exists (and can be password-reset) but can't be signed into
        // with any guessable secret — never a hardcoded default.
        string secret = loginable ? password! : Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        string hash = BCrypt.Net.BCrypt.HashPassword(secret, workFactor: 12);

        db.Users.Add(new AppUser
        {
            TenantId     = tenant.Id,
            Email        = email,
            DisplayName  = config["Platform:OwnerName"]
                           ?? Environment.GetEnvironmentVariable("PLANSCAPE_OWNER_NAME")
                           ?? "Davis",
            PasswordHash = hash,
            Role         = UserRole.Owner,
            Iso19650Role = "A", // Appointing Party
            IsActive     = true,
        });
        await db.SaveChangesAsync(ct);

        if (loginable)
            logger.LogInformation("Seeded platform Owner account {Email}.", email);
        else
            logger.LogWarning(
                "Seeded platform Owner {Email} WITHOUT a known password — set Platform:OwnerPassword " +
                "(or env PLANSCAPE_OWNER_PASSWORD) and redeploy, or use the password-reset flow.", email);
    }
}
