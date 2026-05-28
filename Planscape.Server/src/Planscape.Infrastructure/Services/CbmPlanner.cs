using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Pillar B (6B) — runtime-based preventive maintenance. Devices report a
/// monotonic runtime metric ("run_hours"); when (current - lastService) crosses
/// the service interval, a preventive WorkOrder is raised onto the K2 spine and
/// the baseline advances. The first reading just seeds the baseline (so a device
/// commissioned at 5000 h doesn't trigger immediately), and an open CBM order
/// blocks re-raises until it's actioned.
/// </summary>
public sealed class CbmPlanner : ICbmPlanner
{
    private const string RuntimeMetric = "run_hours";
    private const double DefaultIntervalHours = 2000;

    private readonly PlanscapeDbContext _db;
    private readonly IPlatformEventService _events;

    public CbmPlanner(PlanscapeDbContext db, IPlatformEventService events)
    {
        _db = db;
        _events = events;
    }

    public async Task EvaluateAsync(
        Guid projectId, IReadOnlyList<TelemetryReading> readings, CancellationToken ct = default)
    {
        // Latest runtime reading per device this batch.
        var runtime = readings
            .Where(r => r.Metric == RuntimeMetric)
            .GroupBy(r => r.DeviceId)
            .Select(g => g.OrderByDescending(r => r.Ts ?? DateTime.UtcNow).First())
            .ToList();
        if (runtime.Count == 0) return;

        var deviceIds = runtime.Select(r => r.DeviceId).ToList();
        var twins = (await _db.DeviceTwins
                .Where(t => t.ProjectId == projectId && deviceIds.Contains(t.DeviceId))
                .ToListAsync(ct))
            .ToDictionary(t => t.DeviceId);

        // Devices that already have an open CBM work order — don't re-raise.
        var openCbmDevices = await _db.WorkOrders
            .Where(w => w.ProjectId == projectId && w.Source == "cbm"
                        && (w.Status == "OPEN" || w.Status == "IN_PROGRESS"))
            .Select(w => w.DeviceTwinId)
            .ToListAsync(ct);
        var openCbmTwinIds = openCbmDevices.Where(id => id != null).Select(id => id!.Value).ToHashSet();

        var woCount = await _db.WorkOrders.CountAsync(w => w.ProjectId == projectId, ct);
        var raised = new List<WorkOrder>();

        foreach (var r in runtime)
        {
            if (!twins.TryGetValue(r.DeviceId, out var twin)) continue;

            // First observation seeds the baseline — never triggers immediately.
            if (twin.LastServiceRunHours is null)
            {
                twin.LastServiceRunHours = r.Value;
                continue;
            }

            var interval = twin.ServiceIntervalHours is > 0 ? twin.ServiceIntervalHours!.Value : DefaultIntervalHours;
            var sinceService = r.Value - twin.LastServiceRunHours.Value;
            if (sinceService < interval) continue;
            if (openCbmTwinIds.Contains(twin.Id)) continue; // one open CBM order at a time

            var wo = new WorkOrder
            {
                TenantId = _db.CurrentTenantId,
                ProjectId = projectId,
                Code = $"WO-{++woCount:D4}",
                DeviceTwinId = twin.Id,
                IfcGlobalId = twin.IfcGlobalId,
                Title = $"Preventive maintenance due — {twin.AssetTag ?? twin.DeviceId}",
                Description = $"Runtime {r.Value:F0} h ≥ service interval {interval:F0} h "
                              + $"(last service at {twin.LastServiceRunHours.Value:F0} h).",
                Priority = "MEDIUM",
                Status = "OPEN",
                Source = "cbm",
            };
            _db.WorkOrders.Add(wo);
            raised.Add(wo);

            // Advance baseline so the next order is one interval out (and we don't
            // re-raise every tick); the open-order guard covers the interim.
            twin.LastServiceRunHours = r.Value;
            openCbmTwinIds.Add(twin.Id);
        }

        if (raised.Count == 0) return;
        await _db.SaveChangesAsync(ct);

        foreach (var wo in raised)
        {
            var payload = JsonConvert.SerializeObject(new
            {
                workOrderId = wo.Id, code = wo.Code, deviceTwinId = wo.DeviceTwinId,
                source = "cbm", title = wo.Title,
            });
            await _events.AppendAsync(new PlatformEventAppend(
                ProjectId: projectId, Source: "twin", Type: "workorder.created",
                PayloadJson: payload, TargetIfcGlobalId: wo.IfcGlobalId,
                BaseRevisionId: null, ActorUserId: null), ct);
        }
    }
}
