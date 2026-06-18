// C1 prep — NOT wired. Activated in Program.cs when chunk C1 lands.
// Do not call from controllers until then.

using System.Security.Claims;

namespace Planscape.API.Auth;

/// <summary>
/// C1 prep — NOT wired. Activated in Program.cs when chunk C1 lands.
/// Do not call from controllers until then.
///
/// <para>
/// Reads the authenticated principal's JWT claims and projects them onto the
/// per-request scoped <see cref="TenantContext"/>. C1 wires it AFTER
/// <c>UseAuthentication()</c> with <c>app.UseMiddleware&lt;TenantContextMiddleware&gt;()</c>
/// and registers <see cref="TenantContext"/> as scoped. No DI registration is
/// done here.
/// </para>
/// </summary>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next) => _next = next;

    /// <summary>
    /// <paramref name="tenant"/> is method-injected per request (scoped). When
    /// the principal is authenticated, copy the short claims the Workers emit
    /// onto it; otherwise leave it blank (anonymous request).
    /// </summary>
    public async Task InvokeAsync(HttpContext context, TenantContext tenant)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            tenant.TenantId = Claim(user, TenantContext.ClaimTenantId);
            tenant.UserId = Claim(user, TenantContext.ClaimUserId)
                            ?? Claim(user, ClaimTypes.NameIdentifier);
            tenant.Role = Claim(user, TenantContext.ClaimRole)
                          ?? Claim(user, ClaimTypes.Role);
            tenant.EmailVerified = IsTruthy(Claim(user, TenantContext.ClaimEmailVerified));
            tenant.PlanProduct = Claim(user, TenantContext.ClaimPlanProduct);
            tenant.PlanTier = Claim(user, TenantContext.ClaimPlanTier);
            tenant.SubscriptionStatus = Claim(user, TenantContext.ClaimSubscriptionStatus);
        }

        await _next(context);
    }

    private static string? Claim(ClaimsPrincipal user, string type)
    {
        var value = user.FindFirst(type)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool IsTruthy(string? value)
        => value is "1" or "true" or "True" or "yes";
}
