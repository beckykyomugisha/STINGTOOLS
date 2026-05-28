using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>Pillar B/C (6A) — alert → WorkOrder → K2 spine.</summary>
public sealed class WorkOrderAutomator : IWorkOrderAutomator
{
    private readonly PlanscapeDbContext _db;
    private readonly IPlatformEventService _events;

    public WorkOrderAutomator(PlanscapeDbContext db, IPlatformEventService events)
    {
        _db = db;
        _events = events;
    }

    public async Task<WorkOrder> RaiseFromAlertAsync(Guid projectId, TwinAlert alert, CancellationToken ct = default)
    {
        var twin = await _db.DeviceTwins
            .FirstOrDefaultAsync(t => t.ProjectId == projectId && t.DeviceId == alert.DeviceId, ct);

        var n = await _db.WorkOrders.CountAsync(w => w.ProjectId == projectId, ct);
        var wo = new WorkOrder
        {
            TenantId = _db.CurrentTenantId,
            ProjectId = projectId,
            Code = $"WO-{n + 1:D4}",
            DeviceTwinId = twin?.Id,
            IfcGlobalId = alert.IfcGlobalId ?? twin?.IfcGlobalId,
            AlertId = alert.Id,
            Title = $"{alert.Severity}: {alert.Metric} on {alert.DeviceId}",
            Description = alert.Message,
            Priority = alert.Severity == "ALARM" ? "HIGH" : "MEDIUM",
            Status = "OPEN",
            Source = "alert",
        };
        _db.WorkOrders.Add(wo);
        await _db.SaveChangesAsync(ct);

        var payload = JsonConvert.SerializeObject(new
        {
            workOrderId = wo.Id,
            code = wo.Code,
            deviceId = alert.DeviceId,
            metric = alert.Metric,
            value = alert.Value,
            severity = alert.Severity,
            alertId = alert.Id,
        });
        await _events.AppendAsync(new PlatformEventAppend(
            ProjectId: projectId,
            Source: "twin",
            Type: "workorder.created",
            PayloadJson: payload,
            TargetIfcGlobalId: wo.IfcGlobalId,
            BaseRevisionId: null,            // operations-time event, revision-agnostic
            ActorUserId: null), ct);

        return wo;
    }
}
