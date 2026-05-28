using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Pillar B/D — work-order lifecycle. Completion is Pillar D Seam 2: when an
/// as-maintained parameter is supplied, completion emits a K2 "param.stamp"
/// event so the STING drainer writes the value back onto the Revit element —
/// the FM loop closes back into the model the operator already trusts.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/work-orders")]
[Authorize]
public class WorkOrdersController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IPlatformEventService _events;

    public WorkOrdersController(PlanscapeDbContext db, IPlatformEventService events)
    {
        _db = db;
        _events = events;
    }

    [HttpGet]
    public async Task<ActionResult<object>> List(
        Guid projectId, [FromQuery] string? status = null, CancellationToken ct = default)
    {
        var q = _db.WorkOrders.Where(w => w.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(w => w.Status == status);
        return Ok(await q.OrderByDescending(w => w.CreatedAt).ToListAsync(ct));
    }

    [HttpGet("{workOrderId:guid}")]
    public async Task<ActionResult<object>> Get(Guid projectId, Guid workOrderId, CancellationToken ct)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == workOrderId && w.ProjectId == projectId, ct);
        return wo is null ? NotFound() : Ok(wo);
    }

    /// <summary>POST .../complete — close the WO and (optionally) stamp the model.</summary>
    [HttpPost("{workOrderId:guid}/complete")]
    public async Task<ActionResult<object>> Complete(
        Guid projectId, Guid workOrderId, [FromBody] CompleteRequest req, CancellationToken ct)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == workOrderId && w.ProjectId == projectId, ct);
        if (wo is null) return NotFound();

        wo.Status = "COMPLETED";
        wo.CompletedAt = DateTime.UtcNow;
        wo.CompletionNotes = req?.Notes;
        await _db.SaveChangesAsync(ct);

        Guid? eventId = null;
        // Seam 2 — as-maintained write-back via the existing param.stamp handler.
        if (!string.IsNullOrWhiteSpace(req?.AsMaintainedParam)
            && !string.IsNullOrWhiteSpace(wo.IfcGlobalId))
        {
            var payload = JsonConvert.SerializeObject(new
            {
                paramName = req!.AsMaintainedParam,
                value = req.AsMaintainedValue ?? wo.CompletedAt?.ToString("yyyy-MM-dd"),
                workOrderId = wo.Id,
                workOrderCode = wo.Code,
            });
            var ev = await _events.AppendAsync(new PlatformEventAppend(
                ProjectId: projectId,
                Source: "server",
                Type: "param.stamp",
                PayloadJson: payload,
                TargetIfcGlobalId: wo.IfcGlobalId,
                BaseRevisionId: null,
                ActorUserId: GetUserId()), ct);
            eventId = ev.Id;
        }

        return Ok(new { wo.Id, wo.Code, wo.Status, wo.CompletedAt, stampEventId = eventId });
    }

    private Guid? GetUserId()
    {
        var c = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return c != null && Guid.TryParse(c, out var id) ? id : null;
    }

    public class CompleteRequest
    {
        public string? Notes { get; set; }
        public string? AsMaintainedParam { get; set; }
        public string? AsMaintainedValue { get; set; }
    }
}
