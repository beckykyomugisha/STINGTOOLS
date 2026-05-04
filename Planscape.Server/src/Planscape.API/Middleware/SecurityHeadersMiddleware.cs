namespace Planscape.API.Middleware;

/// <summary>
/// SEC-EA-07 — injects the standard hardening headers on every API
/// response. Skipped on health probes so Kubernetes / mobile pings
/// stay tiny.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var isHealth = path.StartsWithSegments("/health")
                    || path.StartsWithSegments("/healthz");

        if (!isHealth)
        {
            // OnStarting fires once before the response body streams, so
            // these headers land even on early-exits from later middleware.
            context.Response.OnStarting(() =>
            {
                var h = context.Response.Headers;
                // HSTS — forces HTTPS for a year. UseHsts already injects
                // this in non-Dev; we keep it idempotent so Dev gets it
                // too (Cloudflare Tunnel + ngrok dev URLs are HTTPS).
                if (!h.ContainsKey("Strict-Transport-Security"))
                    h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                if (!h.ContainsKey("X-Content-Type-Options"))
                    h["X-Content-Type-Options"] = "nosniff";
                if (!h.ContainsKey("X-Frame-Options"))
                    h["X-Frame-Options"] = "DENY";
                if (!h.ContainsKey("X-XSS-Protection"))
                    h["X-XSS-Protection"] = "1; mode=block";
                if (!h.ContainsKey("Referrer-Policy"))
                    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                if (!h.ContainsKey("Content-Security-Policy"))
                {
                    // 'unsafe-inline' on style-src is required by the
                    // Swagger UI we serve from /swagger; the office
                    // dashboard wwwroot uses external stylesheets only.
                    h["Content-Security-Policy"] =
                        "default-src 'self'; " +
                        "script-src 'self'; " +
                        "style-src 'self' 'unsafe-inline'; " +
                        "img-src 'self' data: https:; " +
                        "connect-src 'self' wss: https:; " +
                        "frame-ancestors 'none'";
                }
                if (!h.ContainsKey("Permissions-Policy"))
                    h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
                return Task.CompletedTask;
            });
        }

        return _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
