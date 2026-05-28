namespace Planscape.Core.Interfaces;

/// <summary>
/// K1 keystone — unified element identity. Every host (Revit, Blender,
/// ArchiCAD, Tekla) and every IoT device shares ONE canonical key: the IFC
/// GlobalId. This service is the single resolver that maps between a
/// host-side id (Revit ElementId, Blender object name, IoT device id) and
/// that canonical GlobalId, and fans a GlobalId out to every host that knows
/// it.
///
/// Why it matters: meeting element-highlight write-back, model-diff, and the
/// IoT/digital-twin layer all need to answer "which Revit element is this?"
/// from a non-Revit context. Without one resolver each feature reinvents the
/// lookup against <see cref="Planscape.Core.Entities.ExternalElementMapping"/>.
///
/// All reads are tenant-scoped automatically by the DbContext global query
/// filter; callers pass projectId only.
/// </summary>
public interface IIdentityResolverService
{
    /// <summary>
    /// host-side id → canonical IFC GlobalId. Returns null when unmapped.
    /// </summary>
    Task<string?> ResolveCanonicalGuidAsync(
        Guid projectId, string host, string hostElementId,
        CancellationToken ct = default);

    /// <summary>
    /// canonical IFC GlobalId → host-side element refs. Optionally filter to
    /// a single host (e.g. "revit" for write-back); omit to get all hosts.
    /// </summary>
    Task<IReadOnlyList<HostElementRef>> ResolveHostElementsAsync(
        Guid projectId, string ifcGlobalId, string? host = null,
        CancellationToken ct = default);

    /// <summary>
    /// Every host mapping for a GlobalId — the cross-host fan-out used when an
    /// element highlighted in one host must surface in all the others.
    /// </summary>
    Task<IReadOnlyList<HostElementRef>> GetCrossHostFanoutAsync(
        Guid projectId, string ifcGlobalId,
        CancellationToken ct = default);

    /// <summary>
    /// Bind (or re-bind) an IoT device to a model element. Upserts an
    /// ExternalElementMapping row with Host="iot". Idempotent on
    /// (projectId, ifcGlobalId, "iot", hostDocumentGuid).
    /// </summary>
    Task<IdentityBindResult> BindIotDeviceAsync(
        Guid projectId, string ifcGlobalId, string deviceId,
        string? label = null, string? hostDocumentGuid = null,
        CancellationToken ct = default);
}

/// <summary>A host-side element reference resolved from a canonical GlobalId.</summary>
public sealed record HostElementRef(
    string Host,
    string HostElementId,
    string? HostDocumentGuid,
    string? HostDisplayLabel);

/// <summary>Result of an IoT device binding upsert.</summary>
public sealed record IdentityBindResult(bool Created, Guid MappingId);
