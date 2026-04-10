using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Planscape.API.Swagger;

/// <summary>
/// Adds a Bearer token security requirement to Swagger operations
/// that have [Authorize] but not [AllowAnonymous].
/// </summary>
public class SecurityRequirementFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasAuthorize = context.MethodInfo.DeclaringType?
            .GetCustomAttributes(true)
            .OfType<AuthorizeAttribute>().Any() == true
            || context.MethodInfo
            .GetCustomAttributes(true)
            .OfType<AuthorizeAttribute>().Any();

        var hasAllowAnonymous = context.MethodInfo
            .GetCustomAttributes(true)
            .OfType<AllowAnonymousAttribute>().Any();

        if (!hasAuthorize || hasAllowAnonymous) return;

        var scheme = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        };

        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new() { [scheme] = Array.Empty<string>() }
        };
    }
}
