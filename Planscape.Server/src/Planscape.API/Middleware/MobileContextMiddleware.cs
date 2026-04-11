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

        await _next(context);
    }
}
