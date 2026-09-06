using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Infrastructure.Services;

namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// S1.4 — declarative quota guard for MVC actions. Decorate a controller
/// action with <c>[Quota(QuotaAxis.Projects)]</c> and the filter will
/// short-circuit with <c>402 Payment Required</c> + an upsell payload when
/// the current tenant has hit the cap. Storage uploads are checked via
/// <see cref="IQuotaGuardService.CheckCanUploadBytesAsync"/> in the
/// controller body, since the byte count isn't known until the request is
/// being read.
///
/// 402 (rather than 403) is chosen deliberately — it lets the client
/// distinguish "you need to upgrade" from "you don't have permission to
/// do this", and the mobile app can render an upsell sheet on 402 only.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class QuotaAttribute : Attribute, IAsyncActionFilter
{
    private readonly QuotaAxis _axis;

    public QuotaAttribute(QuotaAxis axis)
    {
        _axis = axis;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var guard = context.HttpContext.RequestServices.GetRequiredService<IQuotaGuardService>();
        QuotaResult result = _axis switch
        {
            QuotaAxis.Projects => await guard.CheckCanAddProjectAsync(),

            // Storage is the ONLY axis that reaches here, and allowing it is
            // correct rather than a fallback: the byte count is not known until
            // the request body is read, so upload controllers call
            // CheckCanUploadBytesAsync in the action body instead (see the
            // class summary). The per-user axes that used to sit here are gone
            // — QuotaAxis no longer declares them, so naming one is a compile
            // error rather than something this arm would wave through.
            _ => QuotaResult.Allow(_axis, 0, int.MaxValue),
        };

        if (!result.Allowed)
        {
            context.Result = new ObjectResult(new
            {
                error = "quota_exceeded",
                axis = result.Axis.ToString(),
                current = result.Current,
                max = result.Max,
                reason = result.Reason,
                upgrade_url = "/billing/upgrade",
            })
            { StatusCode = StatusCodes.Status402PaymentRequired };
            return;
        }

        await next();
    }
}
