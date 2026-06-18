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
/// Authorization filter enforcing a MINIMUM role from the platform hierarchy
/// (higher rank satisfies a lower-rank requirement):
/// </para>
/// <code>owner &gt; admin &gt; bim_manager &gt; project_lead &gt; coordinator &gt; viewer &gt; client</code>
/// <para>
/// Reads the role from <see cref="TenantContext"/> when available, falling back
/// to the principal's <c>role</c> claim so it works whether or not the scoped
/// context is registered. Usage once C1 lands:
/// <c>[RequireRole("bim_manager")]</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireRoleAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>
    /// Role hierarchy ranks — higher number outranks lower. A request passes
    /// when its role rank is &gt;= the required role's rank.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> Hierarchy =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["owner"] = 6,
            ["admin"] = 5,
            ["bim_manager"] = 4,
            ["project_lead"] = 3,
            ["coordinator"] = 2,
            ["viewer"] = 1,
            ["client"] = 0,
        };

    private readonly string _minimumRole;

    public RequireRoleAttribute(string minimumRole) => _minimumRole = minimumRole;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Prefer the populated TenantContext; fall back to the raw claim so the
        // attribute is usable even before TenantContext is registered in DI.
        var tenant = context.HttpContext.RequestServices.GetService(typeof(TenantContext)) as TenantContext;
        var role = tenant?.Role
                   ?? user.FindFirst(TenantContext.ClaimRole)?.Value
                   ?? user.FindFirst(ClaimTypes.Role)?.Value;

        if (!Satisfies(role, _minimumRole))
            context.Result = new ForbidResult();
    }

    /// <summary>True when <paramref name="actual"/> outranks or equals <paramref name="required"/>.</summary>
    public static bool Satisfies(string? actual, string required)
    {
        if (string.IsNullOrWhiteSpace(actual)) return false;
        if (!Hierarchy.TryGetValue(actual, out var have)) return false;
        if (!Hierarchy.TryGetValue(required, out var need)) return false;
        return have >= need;
    }
}
