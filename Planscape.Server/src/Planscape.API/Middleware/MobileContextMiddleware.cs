namespace Planscape.API.Middleware;

/// <summary>
/// Reads mobile device context headers and stores them in HttpContext.Items
/// for downstream audit log enrichment.
/// </summary>
public class MobileContextMiddleware
{
    private readonly RequestDelegate _next;

    public MobileContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Device-Id", out var deviceId))
            context.Items["DeviceId"] = deviceId.ToString();

        if (context.Request.Headers.TryGetValue("X-Latitude", out var lat)
            && double.TryParse(lat, out var latVal))
            context.Items["Latitude"] = latVal;

        if (context.Request.Headers.TryGetValue("X-Longitude", out var lng)
            && double.TryParse(lng, out var lngVal))
            context.Items["Longitude"] = lngVal;

        // M12 / SRV-11 — derive an audit Source classifier from request headers.
        // Explicit X-Client-Type wins; otherwise fall back to User-Agent sniffing.
        // Recognised values: "mobile" | "plugin" | "web" | "server" | "desktop"
        string? source = null;
        if (context.Request.Headers.TryGetValue("X-Client-Type", out var clientType))
        {
            var v = clientType.ToString().Trim().ToLowerInvariant();
            if (v == "mobile" || v == "plugin" || v == "web" || v == "server" || v == "desktop")
                source = v;
        }
        if (source == null)
        {
            var ua = context.Request.Headers.UserAgent.ToString();
            if (!string.IsNullOrEmpty(ua))
            {
                if (ua.Contains("Expo", StringComparison.OrdinalIgnoreCase)
                    || ua.Contains("Planscape-Mobile", StringComparison.OrdinalIgnoreCase)
                    || ua.Contains("okhttp", StringComparison.OrdinalIgnoreCase))
                    source = "mobile";
                else if (ua.Contains("StingTools", StringComparison.OrdinalIgnoreCase)
                    || ua.Contains("Revit", StringComparison.OrdinalIgnoreCase))
                    source = "plugin";
                else if (ua.Contains("Mozilla", StringComparison.OrdinalIgnoreCase)
                    || ua.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
                    || ua.Contains("Safari", StringComparison.OrdinalIgnoreCase)
                    || ua.Contains("Edge", StringComparison.OrdinalIgnoreCase)
                    || ua.Contains("Firefox", StringComparison.OrdinalIgnoreCase))
                    source = "web";
            }
        }
        if (source != null) context.Items["Source"] = source;

        await _next(context);
    }
}
