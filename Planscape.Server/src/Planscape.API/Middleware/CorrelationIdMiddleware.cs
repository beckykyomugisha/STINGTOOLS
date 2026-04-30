using Serilog.Context;

namespace Planscape.API.Middleware;

/// <summary>
/// S9 — request-scoped correlation ID enrichment for Serilog. Every
/// inbound request gets a stable correlation ID (honours an inbound
/// <c>X-Correlation-Id</c> header from upstream gateways, mints a fresh
/// GUID otherwise). The id is pushed into the Serilog
/// <see cref="LogContext"/> together with the tenant + user claims so
/// every log line written during the request can be traced back to a
/// single tenant action across the API + Hangfire + SignalR.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var corrId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(corrId) || corrId.Length > 64)
        {
            corrId = Guid.NewGuid().ToString("N");
        }
        context.Response.Headers[HeaderName] = corrId;
        context.Items["CorrelationId"] = corrId;

        var tenantId = context.User?.FindFirst("tenant_id")?.Value;
        var userId = context.User?.FindFirst("user_id")?.Value
                  ?? context.User?.FindFirst("sub")?.Value;

        using (LogContext.PushProperty("CorrelationId", corrId))
        using (LogContext.PushProperty("TenantId", tenantId ?? ""))
        using (LogContext.PushProperty("UserId", userId ?? ""))
        {
            await _next(context);
        }
    }
}
