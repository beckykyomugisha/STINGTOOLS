// C1 prep — NOT wired. Activated in Program.cs when chunk C1 lands.
// Do not call from controllers until then.

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Planscape.API.Auth;

/// <summary>
/// C1 prep — NOT wired. Activated in Program.cs when chunk C1 lands.
/// Do not call from controllers until then.
///
/// <para>
/// Global action filter that enforces the subscription status (<c>ps</c>) claim:
/// when a tenant's subscription is <c>read_only</c> (e.g. lapsed / downgraded /
/// past due), unsafe HTTP methods (POST/PUT/PATCH/DELETE) are rejected while
/// safe reads (GET/HEAD/OPTIONS) are allowed through. C1 registers it via
/// <c>options.Filters.Add&lt;SubscriptionStatusFilter&gt;()</c>.
/// </para>
/// </summary>
public sealed class SubscriptionStatusFilter : IActionFilter
{
    /// <summary>Subscription status that triggers write-blocking.</summary>
    public const string ReadOnlyStatus = "read_only";

    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var http = context.HttpContext;
        if (SafeMethods.Contains(http.Request.Method))
            return; // reads always allowed

        var user = http.User;
        if (user?.Identity?.IsAuthenticated != true)
            return; // auth filters handle anonymous access; not this filter's job

        var tenant = http.RequestServices.GetService(typeof(TenantContext)) as TenantContext;
        var status = tenant?.SubscriptionStatus
                     ?? user.FindFirst(TenantContext.ClaimSubscriptionStatus)?.Value;

        if (string.Equals(status, ReadOnlyStatus, StringComparison.OrdinalIgnoreCase))
        {
            // 403 — authenticated and authorized by role, but the subscription
            // tier forbids mutations. C1 may switch this to 402 Payment Required
            // if the front-end prefers an upgrade prompt over a plain forbid.
            context.Result = new ObjectResult(new
            {
                error = "subscription_read_only",
                message = "Your subscription is read-only. Renew or upgrade to make changes.",
            })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        // No post-action work.
    }
}
