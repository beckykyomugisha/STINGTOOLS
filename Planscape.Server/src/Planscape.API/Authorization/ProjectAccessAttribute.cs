using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Authorization;

/// <summary>
/// Phase 175 — declarative project-visibility gate. Looks up either
/// <c>{projectId}</c> or <c>{id}</c> in the route data and short-circuits
/// the request with 404 (Not Found) when the calling user can't see the
/// project per <see cref="ProjectVisibility.CanSeeProjectAsync"/>.
///
/// 404 is deliberate: 403 would tell an attacker the project exists but
/// they're locked out, which leaks the project's existence across
/// tenants and across un-invited users in the same tenant.
///
/// Apply at the controller level so every action inherits the gate:
/// <c>[ProjectAccess]</c>. The route param name defaults to "projectId"
/// — pass <c>RouteParam = "id"</c> on controllers like
/// <see cref="Controllers.ProjectsController"/> that use {id}.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class ProjectAccessAttribute : Attribute, IAsyncActionFilter
{
    public string RouteParam { get; set; } = "projectId";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Phase 175 audit P1-17 — only look up the explicitly-named route
        // param. The previous implicit fallback to {id} silently turned
        // an IssueId / DocumentId into a "ProjectId not found" 404,
        // masking real auth bugs as routine resource-not-found responses.
        // Controllers that own {id} (e.g. ProjectsController) declare
        // [ProjectAccess(RouteParam = "id")].
        var routeValue = context.RouteData.Values.TryGetValue(RouteParam, out var v) ? v?.ToString() : null;

        if (!Guid.TryParse(routeValue, out var projectId) || projectId == Guid.Empty)
        {
            // No project in the route — let the action handle it (e.g.
            // an admin-only endpoint). The action filter shouldn't 404
            // a request that wasn't trying to address a project.
            await next();
            return;
        }

        var db = context.HttpContext.RequestServices.GetService(typeof(PlanscapeDbContext)) as PlanscapeDbContext;
        if (db == null)
        {
            // DI broken — fail closed.
            context.Result = new NotFoundResult();
            return;
        }

        var ok = await ProjectVisibility.CanSeeProjectAsync(db, projectId, context.HttpContext.User, context.HttpContext.RequestAborted);
        if (!ok)
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }
}
