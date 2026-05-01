namespace Planscape.API.Middleware;

/// <summary>
/// S3.8 — accept <c>/api/v1/...</c> in addition to the historic
/// <c>/api/...</c> paths so the deprecation can happen at the edge
/// without touching every controller. The middleware rewrites
/// <c>/api/v1/foo</c> to <c>/api/foo</c> before routing so the
/// existing controllers serve both URLs.
///
/// Request stamping: every request gets the resolved API version on
/// <c>HttpContext.Items["ApiVersion"]</c> so handlers (telemetry,
/// audit log, CORS) can branch on it.
///
/// Versioning policy:
///   - <c>/api/...</c>   = v1 (legacy default; will be deprecated)
///   - <c>/api/v1/...</c> = v1 (preferred new client path)
///   - <c>/api/v2/...</c> = reserved for the next breaking version
/// </summary>
public class ApiVersionRewriter
{
    private readonly RequestDelegate _next;

    public ApiVersionRewriter(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Items["ApiVersion"] = "v1";
            ctx.Request.Path = "/api" + path.Substring("/api/v1".Length);
            ctx.Response.Headers["X-Planscape-Api-Version"] = "v1";
        }
        else if (path.StartsWith("/api/v2/", StringComparison.OrdinalIgnoreCase))
        {
            // Reserved — return 410 Gone with a clear pointer until v2 ships.
            ctx.Response.StatusCode = StatusCodes.Status410Gone;
            await ctx.Response.WriteAsync("/api/v2 is reserved for the next breaking version. Use /api/v1 today.");
            return;
        }
        else if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Items["ApiVersion"] = "v1-legacy";
            ctx.Response.Headers["X-Planscape-Api-Version"] = "v1-legacy";
            ctx.Response.Headers["Deprecation"]              = "true";
            ctx.Response.Headers["Sunset"]                   = "Wed, 31 Dec 2026 00:00:00 GMT";
            ctx.Response.Headers["Link"]                      = "</api/v1>; rel=\"successor-version\"";
        }
        await _next(ctx);
    }
}

public static class ApiVersionRewriterExtensions
{
    public static IApplicationBuilder UseApiVersionRewriter(this IApplicationBuilder app)
        => app.UseMiddleware<ApiVersionRewriter>();
}
