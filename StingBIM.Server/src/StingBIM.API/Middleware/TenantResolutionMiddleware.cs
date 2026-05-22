using Microsoft.EntityFrameworkCore;
using StingBIM.Infrastructure.Data;

namespace StingBIM.API.Middleware;

/// <summary>
/// Resolves the current tenant from the JWT token's tenant_id claim,
/// the X-Tenant-Id header, or the subdomain ({slug}.stingbim.io).
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, StingBimDbContext db)
    {
        // Skip for non-authenticated endpoints
        if (context.Request.Path.StartsWithSegments("/api/auth"))
        {
            await _next(context);
            return;
        }

        Guid? tenantId = null;

        // 1. From JWT claim
        var claim = context.User?.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out var fromClaim))
            tenantId = fromClaim;

        // 2. From header (for API key auth)
        if (tenantId == null && context.Request.Headers.TryGetValue("X-Tenant-Id", out var headerVal))
            if (Guid.TryParse(headerVal, out var fromHeader))
                tenantId = fromHeader;

        // 3. From subdomain
        if (tenantId == null)
        {
            var host = context.Request.Host.Host;
            var parts = host.Split('.');
            if (parts.Length >= 3 && parts[1] == "stingbim")
            {
                var slug = parts[0];
                var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug && t.IsActive);
                tenantId = tenant?.Id;
            }
        }

        if (tenantId.HasValue)
            context.Items["TenantId"] = tenantId.Value;

        await _next(context);
    }
}
