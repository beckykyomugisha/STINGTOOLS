using Microsoft.AspNetCore.Authorization;

namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// Phase 158 — handler for <see cref="SecurityOfficerOrAdminRequirement"/>.
/// Pure claims-only check (no DB hit). Grants when ANY of:
///   • <c>Admin</c> role claim
///   • <c>Owner</c> role claim
///   • <c>SecurityOfficer</c> role claim
/// </summary>
public sealed class SecurityOfficerOrAdminHandler : AuthorizationHandler<SecurityOfficerOrAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SecurityOfficerOrAdminRequirement requirement)
    {
        if (context.User.IsInRole("Admin")
            || context.User.IsInRole("Owner")
            || context.User.IsInRole("SecurityOfficer"))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
