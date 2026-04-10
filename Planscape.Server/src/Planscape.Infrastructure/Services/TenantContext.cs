using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Scoped service providing current tenant info resolved from the HTTP request.
/// Tier and MimEnabled are loaded from Redis cache (5-min TTL) with DB fallback.
/// </summary>
public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContext;
    private readonly PlanscapeDbContext _db;
    private readonly IDistributedCache _cache;
    private Tenant? _cached;
    private bool _loaded;

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
    };

    public TenantContext(IHttpContextAccessor httpContext, PlanscapeDbContext db, IDistributedCache cache)
    {
        _httpContext = httpContext;
        _db = db;
        _cache = cache;
    }

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

    public LicenseTier Tier => LoadTenant()?.Tier ?? LicenseTier.Starter;

    public bool MimEnabled => LoadTenant()?.MimEnabled ?? false;

    private Tenant? LoadTenant()
    {
        if (_loaded) return _cached;
        _loaded = true;
        var id = TenantId;
        if (id == Guid.Empty) return null;

        var cacheKey = $"tenant:{id}";

        // Try Redis first
        try
        {
            var bytes = _cache.Get(cacheKey);
            if (bytes != null)
            {
                _cached = JsonSerializer.Deserialize<Tenant>(bytes);
                return _cached;
            }
        }
        catch { /* Redis unavailable — fall through to DB */ }

        // Fallback to DB
        _cached = _db.Tenants.AsNoTracking().FirstOrDefault(t => t.Id == id);

        // Cache in Redis
        if (_cached != null)
        {
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(_cached);
                _cache.Set(cacheKey, bytes, CacheOptions);
            }
            catch { /* Redis unavailable — continue without caching */ }
        }

        return _cached;
    }
}
