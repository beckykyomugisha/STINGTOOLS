using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// Register / remove the APS webhooks for a project's ACC connection.
///
///   POST   /api/projects/{projectId}/acc/webhooks/subscribe   body: { "folderUrns": ["urn:adsk.wipprod:fs.folder:co.…"] }
///   DELETE /api/projects/{projectId}/acc/webhooks
///   GET    /api/projects/{projectId}/acc/folders[?parentUrn=…]  — folder browser for folderUrns
///
/// Same gate as connecting ACC (AccOAuthController.Start): [ProjectAccess] +
/// CanAdministerProject. Issue hooks are always created (project scope). Data
/// Management hooks are folder-scoped in APS: for the folderUrns given, or —
/// when the body has no folderUrns — the project's top folders (Project Files),
/// reported back as folderSource/folders. "folderUrns": [] means no DM hooks.
/// The hooks' callback URL carries ?connectionId so AutodeskWebhooksController
/// does not have to trust payload.projectId.
///
/// There is no ACC settings screen in the Planscape web or mobile app yet, so
/// these endpoints are the interface (documented in docs/DEPLOY_RUNBOOK.md).
///
/// 200 OK / PARTIAL (body says which), 409 RECONNECT_REQUIRED, 502 FAILED.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/acc/webhooks")]
[Authorize]
[ProjectAccess]
public class AccWebhookSubscriptionsController : ControllerBase
{
    private readonly AccWebhookService _hooks;
    private readonly PlanscapeDbContext _db;

    public AccWebhookSubscriptionsController(AccWebhookService hooks, PlanscapeDbContext db)
    {
        _hooks = hooks;
        _db = db;
    }

    public sealed record SubscribeRequest(List<string>? FolderUrns);

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(Guid projectId, [FromBody] SubscribeRequest? req, CancellationToken ct)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId, ct)) return Forbidden();
        return ToHttp(await _hooks.SubscribeAsync(projectId, req?.FolderUrns, ct));
    }

    [HttpDelete]
    public async Task<IActionResult> Unsubscribe(Guid projectId, CancellationToken ct)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId, ct)) return Forbidden();
        return ToHttp(await _hooks.UnsubscribeAsync(projectId, ct));
    }

    /// <summary>
    /// Browse the ACC folders a Data Management hook can be scoped to (ACC-SRV-6).
    /// No parentUrn → the project's top folders (hidden ones flagged); with one →
    /// its sub-folders, every page. Pick URNs from here for subscribe's folderUrns.
    /// </summary>
    [HttpGet("~/api/projects/{projectId:guid}/acc/folders")]
    public async Task<IActionResult> Folders(Guid projectId, [FromQuery] string? parentUrn, CancellationToken ct)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId, ct)) return Forbidden();
        var r = await _hooks.ListFoldersAsync(projectId, parentUrn, ct);
        return r.Status switch
        {
            AccSyncService.StatusOk => Ok(r),
            AccSyncService.StatusReconnect => Conflict(r),
            _ => StatusCode(502, r),
        };
    }

    private IActionResult ToHttp(AccWebhookService.Result r) => r.Status switch
    {
        AccSyncService.StatusOk or AccSyncService.StatusPartial => Ok(r),
        AccSyncService.StatusReconnect => Conflict(r),
        _ => StatusCode(502, r),
    };

    private ObjectResult Forbidden()
        => StatusCode(403, new { error = "Only a project manager or administrator can manage ACC webhooks." });
}
