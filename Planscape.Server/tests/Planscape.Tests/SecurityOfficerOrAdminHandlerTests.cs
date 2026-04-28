using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Planscape.Infrastructure.Authorization;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 158 — exercises the SecurityOfficerOrAdmin claims-only
/// handler. Pure compute, no DB / Redis.
/// </summary>
public class SecurityOfficerOrAdminHandlerTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("SecurityOfficer")]
    public async Task QualifyingRole_Grants(string role)
    {
        var (handler, ctx) = Arrange(role);
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Contributor")]
    [InlineData("Coordinator")]
    [InlineData("Manager")]
    [InlineData("BimManager")]    // bespoke / not in our enum
    [InlineData("")]
    public async Task NonQualifyingRole_Denies(string role)
    {
        var (handler, ctx) = Arrange(role);
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task NoRoleClaim_Denies()
    {
        var handler = new SecurityOfficerOrAdminHandler();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("user_id", System.Guid.NewGuid().ToString()),
        }, "test"));
        var ctx = new AuthorizationHandlerContext(
            new[] { new SecurityOfficerOrAdminRequirement() }, user, resource: null);
        await handler.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task MultipleRoles_GrantsIfAnyQualifies()
    {
        var handler = new SecurityOfficerOrAdminHandler();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Viewer"),
            new Claim(ClaimTypes.Role, "SecurityOfficer"),
        }, "test"));
        var ctx = new AuthorizationHandlerContext(
            new[] { new SecurityOfficerOrAdminRequirement() }, user, resource: null);
        await handler.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    private static (SecurityOfficerOrAdminHandler, AuthorizationHandlerContext) Arrange(string role)
    {
        var handler = new SecurityOfficerOrAdminHandler();
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(role)) claims.Add(new Claim(ClaimTypes.Role, role));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var ctx = new AuthorizationHandlerContext(
            new[] { new SecurityOfficerOrAdminRequirement() }, user, resource: null);
        return (handler, ctx);
    }
}
