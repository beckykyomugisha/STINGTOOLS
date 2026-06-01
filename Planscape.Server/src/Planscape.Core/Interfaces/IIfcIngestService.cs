using Planscape.Core.DTOs;

namespace Planscape.Core.Interfaces;

/// <summary>
/// Cross-host IFC ingest — the single write path into
/// <see cref="Planscape.Core.Entities.ExternalElementMapping"/> (and, for the
/// full ingest, the <see cref="Planscape.Core.Entities.TaggedElement"/>
/// projection).
///
/// Extracted from the formerly-inline <c>IfcController.IngestData</c> so that
/// every host surface that learns a (IFC GlobalId ↔ host-element-id) pairing
/// feeds the same identity table:
///   • IfcController  — full ingest (mappings + tagged projection).
///   • TagSyncController — mapping upsert after a Revit tag sync.
///   • ArchiCADController — mapping upsert after a StingBridge push.
///
/// IMPORTANT: the methods take an explicit <paramref name="tenantId"/> and the
/// implementation ignores the DbContext tenant query filter, so they are
/// correct both inside a request scope AND inside a background / fire-and-forget
/// scope where the HTTP-derived <c>ITenantContext</c> resolves no tenant.
/// Callers are responsible for having validated that the project belongs to the
/// tenant before invoking.
/// </summary>
public interface IIfcIngestService
{
    /// <summary>
    /// Full ingest: upserts <c>ExternalElementMapping</c> rows AND the
    /// <c>TaggedElement</c> projection from a host plugin's IFC payload.
    /// </summary>
    Task<IfcIngestResponse> IngestAsync(
        Guid tenantId, Guid projectId, IfcIngestRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Mapping-only upsert: writes just the cross-host identity rows for a
    /// batch of (IFC GlobalId ↔ host-element-id) pairs. Used by callers that
    /// already own (TagSync) or cannot produce (ArchiCAD) the full tagged
    /// projection. Returns the number of mapping rows created or updated.
    /// Mappings with an empty <see cref="ElementMappingDto.IfcGlobalId"/> are
    /// skipped.
    /// </summary>
    Task<int> UpsertMappingsAsync(
        Guid tenantId, Guid projectId, string host, string? hostDocumentGuid,
        IEnumerable<ElementMappingDto> mappings,
        CancellationToken ct = default);
}

/// <summary>
/// Minimal (IFC GlobalId ↔ host-element-id) pairing used by the mapping-only
/// ingest path. Decouples mapping callers from the much larger
/// <see cref="IfcElementDto"/>.
/// </summary>
public sealed record ElementMappingDto(
    string IfcGlobalId,
    string HostElementId,
    string? HostDisplayLabel = null);
