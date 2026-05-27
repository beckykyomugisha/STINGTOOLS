using Planscape.Core.Entities;

namespace Planscape.Core.Interfaces;

/// <summary>
/// Pillar D Seam 1 (T3) + T1 — turns the model/handover register into a LIVE
/// twin registry. SeedFromModel makes the asset register the operational
/// source-of-truth (not a dead file); ProvisionFromCx provisions a twin at
/// commissioning sign-off. Both bind through K1, so seeded twins resolve to
/// the same IFC GlobalId the viewer + Revit use and are immediately ready to
/// receive telemetry.
/// </summary>
public interface ITwinProvisioningService
{
    /// <summary>Create twins for serviceable equipment in the model projection.</summary>
    Task<int> SeedFromModelAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Provision (bind + commission metadata) one device at CX sign-off.</summary>
    Task<DeviceTwin> ProvisionFromCxAsync(TwinProvisionRequest req, CancellationToken ct = default);
}

public sealed record TwinProvisionRequest(
    Guid ProjectId,
    string DeviceId,
    string IfcGlobalId,
    string Protocol = "mqtt",
    string? AssetTag = null,
    string? Serial = null,
    string? Manufacturer = null,
    string? Model = null,
    string? CommissioningRef = null,
    string? PressureRegime = null,     // NEG | POS | NEUTRAL (healthcare 6C)
    double? DesignDeltaPa = null);
