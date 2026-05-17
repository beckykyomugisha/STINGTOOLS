using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Middleware;

/// <summary>
/// Resolves the current tenant from the JWT token's tenant_id claim,
/// the X-Tenant-Id header, or the subdomain ({slug}.planscape.io).
///
/// SEC-EA-04: when an authenticated request resolves a tenant from
/// header / subdomain that differs from the JWT's tenant_id, refuse
/// the request — the alternative is that a token issued for tenant A
/// could read tenant B's data simply by routing through tenant B's
/// subdomain.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, PlanscapeDbContext db)
    {
        // Skip for non-authenticated endpoints
        if (context.Request.Path.StartsWithSegments("/api/auth"))
        {
            await _next(context);
            return;
        }

        // Pull the JWT's tenant_id first so we can cross-check anything
        // resolved from header / subdomain.
        Guid? jwtTenantId = null;
        var jwtClaim = context.User?.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrEmpty(jwtClaim) && Guid.TryParse(jwtClaim, out var fromJwt))
            jwtTenantId = fromJwt;

        Guid? resolvedTenantId = jwtTenantId;

        // 2. From header (for API key auth)
        Guid? headerTenantId = null;
        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var headerVal)
            && Guid.TryParse(headerVal, out var fromHeader))
        {
            headerTenantId = fromHeader;
            resolvedTenantId ??= fromHeader;
        }

        // 3. From subdomain
        Guid? subdomainTenantId = null;
        var host = context.Request.Host.Host;
        var parts = host.Split('.');
        if (parts.Length >= 3 && parts[1] == "planscape")
        {
            var slug = parts[0];
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug && t.IsActive);
            if (tenant != null)
            {
                subdomainTenantId = tenant.Id;
                resolvedTenantId ??= tenant.Id;
            }
        }

        // SEC-EA-04 — JWT vs resolved-tenant mismatch.
        // Only trip when the user is authenticated AND another channel
        // resolved a different tenant. Public endpoints fall through
        // (the /api/auth skip above already handles those).
        var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
        if (isAuthenticated && jwtTenantId.HasValue)
        {
            Guid? alt = headerTenantId ?? subdomainTenantId;
            if (alt.HasValue && alt.Value != jwtTenantId.Value)
            {
                var userId = context.User?.FindFirst("user_id")?.Value
                          ?? context.User?.FindFirst("sub")?.Value
                          ?? "unknown";
                _logger.LogWarning(
                    "Tenant mismatch detected: JWT tenant {jwtTenant} vs resolved tenant {resolvedTenant} for user {userId}",
                    jwtTenantId, alt, userId);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"Tenant mismatch\"}");
                return;
            }
        }

        if (resolvedTenantId.HasValue)
            context.Items["TenantId"] = resolvedTenantId.Value;

        await _next(context);
    }
}
