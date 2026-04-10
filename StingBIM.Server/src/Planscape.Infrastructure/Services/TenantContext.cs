using Microsoft.AspNetCore.Http;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Scoped service providing current tenant info resolved from the HTTP request.
/// </summary>
public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContext;

    public TenantContext(IHttpContextAccessor httpContext) => _httpContext = httpContext;

    public Guid TenantId
    {
        get
        {
            if (_httpContext.HttpContext?.Items.TryGetValue("TenantId", out var val) == true && val is Guid id)
                return id;
            var claim = _httpContext.HttpContext?.User?.FindFirst("tenant_id")?.Value;
            return claim != null && Guid.TryParse(claim, out var cid) ? cid : Guid.Empty;
        }
    }

    public string TenantSlug => _httpContext.HttpContext?.User?.FindFirst("tenant_slug")?.Value ?? "";
    public LicenseTier Tier => LicenseTier.Starter; // Resolved from DB in production
    public bool MimEnabled => false;
}
