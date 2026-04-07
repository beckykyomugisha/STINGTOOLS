using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StingBIM.Infrastructure.Data;

namespace StingBIM.API.Middleware;

/// <summary>
/// Resolves the current tenant from the JWT token's tenant_id claim,
/// the X-Tenant-Id header, or the subdomain ({slug}.stingbim.io).
/// Uses in-memory cache for subdomain slug lookups to avoid per-request DB hits.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly MemoryCache _slugCache = new(new MemoryCacheOptions { SizeLimit = 500 });
    private static readonly TimeSpan _slugCacheTtl = TimeSpan.FromMinutes(5);

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, StingBimDbContext db)
    {
        // Skip for non-authenticated endpoints
        if (context.Request.Path.StartsWithSegments("/api/auth") ||
            context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        Guid? tenantId = null;

        // 1. From JWT claim (fast path — no DB hit)
        var claim = context.User?.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out var fromClaim))
            tenantId = fromClaim;

        // 2. From header (for API key auth)
        if (tenantId == null && context.Request.Headers.TryGetValue("X-Tenant-Id", out var headerVal))
            if (Guid.TryParse(headerVal, out var fromHeader))
                tenantId = fromHeader;

        // 3. From subdomain — cached to avoid per-request DB query
        if (tenantId == null)
        {
            var host = context.Request.Host.Host;
            var parts = host.Split('.');
            if (parts.Length >= 3 && parts[1] == "stingbim")
            {
                var slug = parts[0];
                tenantId = await ResolveSlugCachedAsync(slug, db);
            }
        }

        if (tenantId.HasValue)
            context.Items["TenantId"] = tenantId.Value;

        await _next(context);
    }

    private static async Task<Guid?> ResolveSlugCachedAsync(string slug, StingBimDbContext db)
    {
        var cacheKey = $"tenant_slug:{slug}";
        if (_slugCache.TryGetValue(cacheKey, out Guid cachedId))
            return cachedId;

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug && t.IsActive);
        if (tenant == null)
            return null;

        _slugCache.Set(cacheKey, tenant.Id, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _slugCacheTtl,
            Size = 1
        });
        return tenant.Id;
    }

    /// <summary>Evict a slug from cache (call when tenant is deactivated or slug changes).</summary>
    public static void InvalidateSlug(string slug) => _slugCache.Remove($"tenant_slug:{slug}");
}
