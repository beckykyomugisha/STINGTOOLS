using Planscape.Core.Entities;

namespace Planscape.Core.Interfaces;

/// <summary>
/// Pillar B (5A) — binds a device to a model element on K1. One call records
/// the iot ExternalElementMapping (so telemetry resolves to the canonical IFC
/// GlobalId) AND upserts the DeviceTwin. This is the only place the twin layer
/// touches identity — it never invents its own.
/// </summary>
public interface ITwinBindingService
{
    Task<DeviceTwin> BindAsync(TwinBindRequest req, CancellationToken ct = default);
}

public sealed record TwinBindRequest(
    Guid ProjectId,
    string DeviceId,
    string IfcGlobalId,
    string Protocol = "mqtt",
    string? AssetTag = null,
    string? Serial = null,
    string? Manufacturer = null,
    string? Model = null,
    string? Label = null,
    string? MetadataJson = null);
