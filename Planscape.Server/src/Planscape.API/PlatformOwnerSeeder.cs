using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API;

/// <summary>
/// Idempotent startup task that seeds — and keeps — the operator Owner
/// account(s) in the well-known 'planscape' platform tenant (created by
/// <see cref="PlatformTenantSeeder"/>). Lives in the API project because that's
/// where the BCrypt password hasher is referenced.
///
/// Flexibility:
///   • Multi-owner — <c>Platform:OwnerEmails</c> / env <c>PLANSCAPE_OWNER_EMAILS</c>
///     accepts a comma/semicolon/newline-separated allow-list. The single
///     <c>Platform:OwnerEmail</c> / <c>PLANSCAPE_OWNER_EMAIL</c> is still honoured
///     (back-compat) and the default is <c>davis@planscape.build</c>.
///   • Promote — an allow-listed email that already exists in the platform
///     tenant but is NOT yet <see cref="UserRole.Owner"/> is promoted to Owner
///     on every boot. So adding an email to the list + redeploying is enough to
///     grant operator access, and a manual demotion self-heals next deploy.
///   • Loginable — the FIRST (primary) email uses <c>Platform:OwnerPassword</c> /
///     <c>PLANSCAPE_OWNER_PASSWORD</c> and is created active+loginable. Owners
///     without a configured password are created INACTIVE (IsActive=false, like
///     an invited user) with a random unknown hash, so they exist but can't be
///     signed into until they set a password via the reset flow (or a redeploy
///     with the password set). Never a hardcoded default secret.
///
/// Idempotent: an existing Owner row is never touched (so a password the
/// operator has since changed is preserved).
/// </summary>
public static class PlatformOwnerSeeder
{
    /// <summary>
    /// Resolve the configured platform-owner allow-list. First entry is the
    /// "primary" (the one the <c>PLANSCAPE_OWNER_PASSWORD</c> applies to).
    /// Shared with the bootstrap diagnostic so both read the same list.
    /// </summary>
    public static IReadOnlyList<string> ResolveOwnerEmails(IConfiguration config)
    {
        string raw = config["Platform:OwnerEmails"]
            ?? Environment.GetEnvironmentVariable("PLANSCAPE_OWNER_EMAILS")
            ?? config["Platform:OwnerEmail"]
            ?? Environment.GetEnvironmentVariable("PLANSCAPE_OWNER_EMAIL")
            ?? "davis@planscape.build";

        return raw
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task EnsureAsync(
        PlanscapeDbContext db,
        IConfiguration config,
        ILogger logger,
        CancellationToken ct = default)
    {
        // Startup runs without an HTTP context, so the tenant query filter
        // returns nothing unless we bypass it (same pattern as the other seeders).
        db.BypassTenantFilter = true;

        var emails = ResolveOwnerEmails(config);
        if (emails.Count == 0) return;

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == PlatformTenantSeeder.PlatformSlug, ct);
        if (tenant == null)
        {
            logger.LogWarning("PlatformOwnerSeeder: platform tenant '{Slug}' missing — skipping {Count} owner(s).",
                PlatformTenantSeeder.PlatformSlug, emails.Count);
            return;
        }

        string? primaryPassword = config["Platform:OwnerPassword"]
            ?? Environment.GetEnvironmentVariable("PLANSCAPE_OWNER_PASSWORD");
        string primaryEmail = emails[0];

        int created = 0, promoted = 0;
        foreach (var email in emails)
        {
            var existing = await db.Users
                .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == email, ct);

            if (existing != null)
            {
                // Promote-but-don't-clobber: only the role changes; password +
                // IsActive of an established account are left exactly as-is.
                if (existing.Role != UserRole.Owner)
                {
                    existing.Role = UserRole.Owner;
                    promoted++;
                    logger.LogInformation("PlatformOwnerSeeder: promoted {Email} to Owner.", email);
                }
                continue;
            }

            bool isPrimary = string.Equals(email, primaryEmail, StringComparison.OrdinalIgnoreCase);
            bool loginable = isPrimary && !string.IsNullOrWhiteSpace(primaryPassword);

            // No configured password => random unknown hash + inactive, so the
            // account exists (and can be password-reset) but is not loginable.
            string secret = loginable
                ? primaryPassword!
                : Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

            db.Users.Add(new AppUser
            {
                TenantId     = tenant.Id,
                Email        = email,
                DisplayName  = isPrimary
                    ? (config["Platform:OwnerName"]
                       ?? Environment.GetEnvironmentVariable("PLANSCAPE_OWNER_NAME")
                       ?? NameFromEmail(email))
                    : NameFromEmail(email),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(secret, workFactor: 12),
                Role         = UserRole.Owner,
                Iso19650Role = "A", // Appointing Party
                IsActive     = loginable,
            });
            created++;

            if (loginable)
                logger.LogInformation("PlatformOwnerSeeder: seeded loginable Owner {Email}.", email);
            else
                logger.LogWarning(
                    "PlatformOwnerSeeder: seeded Owner {Email} INACTIVE (no password) — set its password via " +
                    "the reset flow, or (primary only) Platform:OwnerPassword / PLANSCAPE_OWNER_PASSWORD + redeploy.",
                    email);
        }

        if (created > 0 || promoted > 0)
            await db.SaveChangesAsync(ct);
    }

    private static string NameFromEmail(string email)
    {
        var local = email.Split('@')[0].Replace('.', ' ').Replace('_', ' ').Trim();
        if (local.Length == 0) return "Owner";
        return char.ToUpperInvariant(local[0]) + (local.Length > 1 ? local.Substring(1) : "");
    }
}
