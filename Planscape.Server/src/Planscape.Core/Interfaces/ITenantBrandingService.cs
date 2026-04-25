using Planscape.Core.Entities;

namespace Planscape.Core.Interfaces;

/// <summary>
/// FLEX-03 — Resolves tenant branding with fallback chain:
///   1. TenantBranding row (database)
///   2. Tenant:DefaultBranding (appsettings)
///   3. Hardcoded safe defaults (never fails)
/// </summary>
public interface ITenantBrandingService
{
    /// <summary>
    /// Returns the fully-resolved branding for a tenant (never null — falls back to defaults).
    /// Result is cached for 5 minutes per tenant.
    /// </summary>
    Task<ResolvedBranding> GetAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Persists or updates the tenant's branding. Invalidates the cache entry.
    /// </summary>
    Task SetAsync(Guid tenantId, TenantBranding branding, CancellationToken ct = default);

    /// <summary>Clears the resolved-branding cache — used after a bulk import.</summary>
    void InvalidateCache(Guid? tenantId = null);
}

/// <summary>
/// Immutable, always-populated branding snapshot. Use this from template renderers and
/// mobile /branding endpoints — no null handling required at the call site.
/// </summary>
public sealed record ResolvedBranding(
    string ProductName,
    string AccentColor,
    string HeaderColor,
    string? LogoUrl,
    string SupportEmail,
    string EmailFromName,
    string EmailFromAddress,
    string? EmailSignature,
    string DefaultLanguage);
