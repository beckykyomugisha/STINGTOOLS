using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>Pillar D Seam 1 + T1 implementation — model/CX → live twin registry.</summary>
public sealed class TwinProvisioningService : ITwinProvisioningService
{
    private readonly PlanscapeDbContext _db;
    private readonly ITwinBindingService _binding;

    // Serviceable-equipment PROD codes worth a twin (HVAC / electrical / DHW).
    private static readonly HashSet<string> ServiceableProd = new(StringComparer.OrdinalIgnoreCase)
    {
        "AHU", "FCU", "DOAS", "CHL", "BLR", "CT", "PMP", "FAN", "VAV",
        "MDB", "DB", "SDB", "UPS", "GEN", "ATS",
        "CAL", "WH", "TMV", "BST",   // DHW / water
        "MANIFOLD", "VIE", "AVSU",   // medical gas
    };

    public TwinProvisioningService(PlanscapeDbContext db, ITwinBindingService binding)
    {
        _db = db;
        _binding = binding;
    }

    public async Task<int> SeedFromModelAsync(Guid projectId, CancellationToken ct = default)
    {
        // Candidate equipment from the model projection. UniqueId carries the
        // IFC GlobalId (IfcController convention), so seeded twins are K1-bound.
        var candidates = await _db.TaggedElements
            .Where(e => e.ProjectId == projectId
                        && e.UniqueId != ""
                        && ServiceableProd.Contains(e.Prod))
            .Select(e => new { e.UniqueId, e.Prod, e.Tag1, e.FamilyName, e.TypeName })
            .ToListAsync(ct);
        if (candidates.Count == 0) return 0;

        var existing = await _db.DeviceTwins
            .Where(t => t.ProjectId == projectId)
            .Select(t => t.IfcGlobalId)
            .ToListAsync(ct);
        var have = new HashSet<string>(existing.Where(g => g != null)!, StringComparer.Ordinal);

        int added = 0;
        foreach (var c in candidates)
        {
            if (have.Contains(c.UniqueId)) continue;
            // Device id = the tag (stable, human-meaningful) falling back to the guid.
            var deviceId = string.IsNullOrWhiteSpace(c.Tag1) ? c.UniqueId : c.Tag1;
            await _binding.BindAsync(new TwinBindRequest(
                projectId, deviceId, c.UniqueId,
                Protocol: "mqtt",
                Label: c.TypeName,
                MetadataJson: JsonConvert.SerializeObject(new { source = "model-seed", prod = c.Prod })), ct);
            have.Add(c.UniqueId);
            added++;
        }
        return added;
    }

    public async Task<DeviceTwin> ProvisionFromCxAsync(TwinProvisionRequest req, CancellationToken ct = default)
    {
        var meta = JsonConvert.SerializeObject(new
        {
            source = "cx-signoff",
            commissioningRef = req.CommissioningRef,
            pressureRegime = req.PressureRegime,
            designDeltaPa = req.DesignDeltaPa,
        });

        return await _binding.BindAsync(new TwinBindRequest(
            req.ProjectId, req.DeviceId, req.IfcGlobalId, req.Protocol,
            req.AssetTag, req.Serial, req.Manufacturer, req.Model,
            Label: req.CommissioningRef, MetadataJson: meta), ct);
    }
}
