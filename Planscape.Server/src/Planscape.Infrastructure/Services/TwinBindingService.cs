using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Pillar B (5A) — binds a device to a model element. Records the iot mapping
/// on K1 (so telemetry resolves to the canonical IFC GlobalId) and upserts the
/// DeviceTwin in one call.
/// </summary>
public sealed class TwinBindingService : ITwinBindingService
{
    private readonly PlanscapeDbContext _db;
    private readonly IIdentityResolverService _identity;

    public TwinBindingService(PlanscapeDbContext db, IIdentityResolverService identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task<DeviceTwin> BindAsync(TwinBindRequest req, CancellationToken ct = default)
    {
        // K1 — the single identity record for the device↔element link.
        await _identity.BindIotDeviceAsync(
            req.ProjectId, req.IfcGlobalId, req.DeviceId, req.Label, hostDocumentGuid: null, ct);

        var twin = await _db.DeviceTwins
            .FirstOrDefaultAsync(t => t.ProjectId == req.ProjectId && t.DeviceId == req.DeviceId, ct);
        if (twin is null)
        {
            twin = new DeviceTwin
            {
                TenantId = _db.CurrentTenantId,
                ProjectId = req.ProjectId,
                DeviceId = req.DeviceId,
            };
            _db.DeviceTwins.Add(twin);
        }
        twin.IfcGlobalId  = req.IfcGlobalId;
        twin.Protocol     = req.Protocol;
        twin.AssetTag     = req.AssetTag ?? twin.AssetTag;
        twin.Serial       = req.Serial ?? twin.Serial;
        twin.Manufacturer = req.Manufacturer ?? twin.Manufacturer;
        twin.Model        = req.Model ?? twin.Model;
        twin.MetadataJson = req.MetadataJson ?? twin.MetadataJson;

        await _db.SaveChangesAsync(ct);
        return twin;
    }
}
