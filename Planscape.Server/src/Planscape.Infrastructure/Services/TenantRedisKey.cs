using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// SEC-EA-05 — helper for tenant-namespaced Redis keys.
///
/// Per the East Africa security review, every tenant-scoped Redis
/// read/write should be prefixed with the active tenant id so that a
/// malicious or buggy tenant cannot poison or read another tenant's
/// cache entries. New cache code in tenant-aware services should
/// route through <see cref="For"/> instead of writing raw string
/// concatenations.
///
/// Keys that are deliberately global (JWT revocation list, SLA burn
/// metrics, the well-known "platform" tenant marker) keep their
/// existing flat keys.
///
/// Existing key sites surveyed during the SEC-EA-05 sweep:
///   • <c>tenant:{id}</c>           — TenantContext, already self-keyed by id
///   • <c>tbmr:{TenantId}:{hash}</c> — DbTenantBimManagerRoleResolver
///   • keyword resolver entries     — DbTenantKeywordResolver
///   • <c>jwtrev:{userId}</c>       — RedisPermissionRevocationStore (per-user)
///   • <c>revoked:{jti}</c>         — JWT logout blacklist (intentional global)
///   • <c>sla:1h:*</c> / <c>sla:6h:*</c> — Sla metrics (intentional global)
///   • <c>login_attempts:{email}</c> — Account lockout (pre-auth, no tenant yet)
/// </summary>
public static class TenantRedisKey
{
    /// <summary>
    /// Build a tenant-namespaced Redis key. The output format is
    /// <c>"t:{tenantId}:{key}"</c> so it nests cleanly under the
    /// global <c>"Planscape:"</c> instance prefix configured in
    /// <c>AddStackExchangeRedisCache</c>.
    /// </summary>
    public static string For(ITenantContext tenant, string key)
    {
        if (tenant == null) throw new ArgumentNullException(nameof(tenant));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("key cannot be empty", nameof(key));
        var id = tenant.TenantId;
        if (id == Guid.Empty)
        {
            // Falling back to a well-known "anon" partition is safer than
            // letting the call write to an untenanted slot (which would
            // be readable by every tenant).
            return $"t:anon:{key}";
        }
        return $"t:{id:N}:{key}";
    }

    /// <summary>
    /// Variant for sites that have a Guid in hand but no scoped
    /// <see cref="ITenantContext"/> (e.g. background jobs).
    /// </summary>
    public static string For(Guid tenantId, string key)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("key cannot be empty", nameof(key));
        if (tenantId == Guid.Empty) return $"t:anon:{key}";
        return $"t:{tenantId:N}:{key}";
    }
}
