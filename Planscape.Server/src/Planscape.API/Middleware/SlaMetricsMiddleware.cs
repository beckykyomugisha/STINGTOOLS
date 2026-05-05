using Microsoft.Extensions.Caching.Distributed;

namespace Planscape.API.Middleware;

/// <summary>
/// S7.2 — counts every API response into rolling 1-h and 6-h Redis
/// buckets so SlaBurnRateJob has data to evaluate against. Cheap:
/// two INCR calls per request with no waits on the hot path.
///
/// Bucket keys:
///   sla:1h:total · sla:1h:5xx · sla:6h:total · sla:6h:5xx
///
/// TODO-SEC: SEC-EA-05 — these Redis keys are intentionally NOT
///   tenant-scoped. They aggregate cluster-wide error rates so
///   SlaBurnRateJob can alert the founder tenant on platform-level
///   SLO breaches. Tenant-scoping these keys would defeat the
///   purpose. Verified intentional 2026-05.
/// </summary>
public class SlaMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDistributedCache _cache;

    public SlaMetricsMiddleware(RequestDelegate next, IDistributedCache cache)
    {
        _next = next;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        await _next(ctx);
        if (!(ctx.Request.Path.StartsWithSegments("/api"))) return;
        var status = ctx.Response.StatusCode;
        try
        {
            await Inc("sla:1h:total", TimeSpan.FromHours(1));
            await Inc("sla:6h:total", TimeSpan.FromHours(6));
            if (status >= 500)
            {
                await Inc("sla:1h:5xx", TimeSpan.FromHours(1));
                await Inc("sla:6h:5xx", TimeSpan.FromHours(6));
            }
        }
        catch { /* metrics best-effort, don't break the request */ }
    }

    private async Task Inc(string key, TimeSpan ttl)
    {
        var current = long.TryParse(await _cache.GetStringAsync(key), out var n) ? n : 0;
        await _cache.SetStringAsync(key, (current + 1).ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
    }
}

public static class SlaMetricsMiddlewareExtensions
{
    public static IApplicationBuilder UseSlaMetrics(this IApplicationBuilder app)
        => app.UseMiddleware<SlaMetricsMiddleware>();
}
