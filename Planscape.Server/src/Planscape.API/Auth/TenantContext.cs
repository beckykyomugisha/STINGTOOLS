// C1 prep — NOT wired. Activated in Program.cs when chunk C1 lands.
// Do not call from controllers until then.

namespace Planscape.API.Auth;

/// <summary>
/// C1 prep — NOT wired. Activated in Program.cs when chunk C1 lands.
/// Do not call from controllers until then.
///
/// <para>
/// Per-request, scoped projection of the authenticated principal's JWT claims.
/// <see cref="TenantContextMiddleware"/> populates it from the claims the
/// Cloudflare Workers (B1) stamp; controllers will read it instead of
/// re-parsing claims. C1 registers it with
/// <c>services.AddScoped&lt;TenantContext&gt;()</c>.
/// </para>
/// </summary>
public sealed class TenantContext
{
    // ── JWT claim keys (short names emitted by the Workers auth layer) ──
    /// <summary>Tenant id claim — <c>tid</c>.</summary>
    public const string ClaimTenantId = "tid";
    /// <summary>User id claim — <c>sub</c>.</summary>
    public const string ClaimUserId = "sub";
    /// <summary>Role claim — <c>role</c>.</summary>
    public const string ClaimRole = "role";
    /// <summary>Email-verified claim — <c>ev</c> (truthy: "1"/"true").</summary>
    public const string ClaimEmailVerified = "ev";
    /// <summary>Plan product claim — <c>pp</c>.</summary>
    public const string ClaimPlanProduct = "pp";
    /// <summary>Plan tier claim — <c>pt</c>.</summary>
    public const string ClaimPlanTier = "pt";
    /// <summary>Subscription status claim — <c>ps</c> (e.g. "active", "read_only", "past_due").</summary>
    public const string ClaimSubscriptionStatus = "ps";

    /// <summary>Tenant the request acts within (<c>tid</c>). Null until populated.</summary>
    public string? TenantId { get; set; }

    /// <summary>Authenticated user id (<c>sub</c>). Null until populated.</summary>
    public string? UserId { get; set; }

    /// <summary>Role string (<c>role</c>) — see <see cref="RequireRoleAttribute"/> for the hierarchy.</summary>
    public string? Role { get; set; }

    /// <summary>Whether the user's email is verified (<c>ev</c>).</summary>
    public bool EmailVerified { get; set; }

    /// <summary>Plan product code (<c>pp</c>).</summary>
    public string? PlanProduct { get; set; }

    /// <summary>Plan tier code (<c>pt</c>).</summary>
    public string? PlanTier { get; set; }

    /// <summary>Subscription status (<c>ps</c>). "read_only" makes the API reject writes — see <see cref="SubscriptionStatusFilter"/>.</summary>
    public string? SubscriptionStatus { get; set; }

    /// <summary>True once <see cref="TenantContextMiddleware"/> has populated this from an authenticated principal.</summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);
}
