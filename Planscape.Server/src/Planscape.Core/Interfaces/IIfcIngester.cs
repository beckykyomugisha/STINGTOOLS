namespace Planscape.Core.Interfaces;

/// <summary>
/// T4-27 — IFC property ingester. First-pass implementation reads
/// IfcElement instances, IfcPropertySet definitions, and the
/// IfcRelDefinesByProperties relationships that bind them, producing
/// an indexed "elementGuid → property bag" record set the rest of the
/// platform can consume for clash + compliance + tag matching.
///
/// Geometry ingest (face index, bounding boxes, clash detection) is
/// DEFERRED to T4-27b — that work needs the Xbim.Geometry native
/// package + a worker container with ~500 MB of build dependencies.
/// The contract below stays geometry-agnostic so the next pass can
/// drop a richer impl in without churning callers.
/// </summary>
public interface IIfcIngester
{
    /// <summary>
    /// Parse the IFC file at <paramref name="ifcPath"/> and return one
    /// <see cref="IfcElementProperties"/> per IfcElement instance with
    /// every property set flattened into the <c>Properties</c> dictionary.
    /// Streams the file rather than buffering — IFC dumps from large
    /// federated models can exceed 1 GB.
    /// </summary>
    Task<IfcIngestResult> IngestAsync(string ifcPath, CancellationToken ct);
}

/// <summary>
/// One row per IfcElement found in the file. Properties are flattened
/// from every linked PropertySet so consumers don't need a second pass.
/// </summary>
public sealed record IfcElementProperties(
    string GlobalId,                     // ifcGuid (stable across exports)
    string IfcType,                      // e.g. "IfcWall", "IfcDoor"
    string? Name,
    string? PredefinedType,              // e.g. "INTERNAL", "EXTERNAL"
    Dictionary<string, string> Properties);

/// <summary>
/// Ingest summary: counts, per-type distribution, and the parsed
/// element list. The DB write is left to the caller — controllers
/// decide whether to persist as TaggedElement / SyncElement / a
/// dedicated IfcElement table per project's strategy.
/// </summary>
public sealed record IfcIngestResult(
    string SchemaVersion,                // e.g. "IFC4", "IFC2X3"
    int    ElementCount,
    Dictionary<string, int> CountsByType,
    IReadOnlyList<IfcElementProperties> Elements,
    TimeSpan Duration,
    string? Warnings);
