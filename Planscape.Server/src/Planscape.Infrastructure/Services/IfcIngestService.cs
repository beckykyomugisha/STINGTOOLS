using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Constants;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Implementation of <see cref="IIfcIngestService"/>. Lifted verbatim from the
/// formerly-inline <c>IfcController.IngestData</c> body, with two changes that
/// let it run outside an HTTP request scope:
///   1. tenant is passed in explicitly rather than read from the request, and
///   2. all reads use <c>IgnoreQueryFilters()</c> + an explicit
///      <c>TenantId == tenantId</c> predicate, so a background / fire-and-forget
///      scope (where the HTTP-derived ITenantContext yields Guid.Empty and the
///      global tenant filter would hide everything) still upserts correctly.
/// New rows stamp <c>TenantId = tenantId</c> explicitly — the SaveChanges
/// auto-stamp is a no-op when CurrentTenantId is empty.
/// </summary>
public sealed class IfcIngestService : IIfcIngestService
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<IfcIngestService>? _logger;
    private const int IngestBatchSize = 500;

    public IfcIngestService(PlanscapeDbContext db, ILogger<IfcIngestService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> UpsertMappingsAsync(
        Guid tenantId, Guid projectId, string host, string? hostDocumentGuid,
        IEnumerable<ElementMappingDto> mappings, CancellationToken ct = default)
    {
        var normalisedHost = MappingHosts.Normalize(host);
        var nowUtc = DateTime.UtcNow;
        int touched = 0;

        var valid = mappings.Where(m => !string.IsNullOrWhiteSpace(m.IfcGlobalId));
        foreach (var batch in Chunk(valid, IngestBatchSize))
        {
            var (created, updated) = await UpsertMappingBatchAsync(
                tenantId, projectId, normalisedHost, hostDocumentGuid, batch, nowUtc, ct);
            touched += created + updated;
            await _db.SaveChangesAsync(ct);
        }
        return touched;
    }

    public async Task<IfcIngestResponse> IngestAsync(
        Guid tenantId, Guid projectId, IfcIngestRequest request, CancellationToken ct = default)
    {
        var host = MappingHosts.Normalize(request.Host);
        var warnings = new List<string>();
        int newMappings = 0, updMappings = 0;
        int newElements = 0, updElements = 0, skipped = 0;
        int nonCanonicalVe = 0;   // elements whose ValidationErrors blob isn't the canonical ValidationErrorDto[] shape
        var nowUtc = DateTime.UtcNow;

        foreach (var batch in Chunk(request.Elements, IngestBatchSize))
        {
            // -------------------------------------------------------
            // 1. Upsert ExternalElementMapping rows
            // -------------------------------------------------------
            var mappingDtos = new List<ElementMappingDto>(batch.Count);
            foreach (var el in batch)
            {
                if (string.IsNullOrWhiteSpace(el.IfcGlobalId))
                {
                    skipped++;
                    warnings.Add($"skipped element with empty IfcGlobalId (host_element_id={el.HostElementId})");
                    continue;
                }
                mappingDtos.Add(new ElementMappingDto(el.IfcGlobalId, el.HostElementId, el.HostDisplayLabel));
            }

            var (createdM, updatedM) = await UpsertMappingBatchAsync(
                tenantId, projectId, host, request.HostDocumentGuid, mappingDtos, nowUtc, ct);
            newMappings += createdM;
            updMappings += updatedM;

            // -------------------------------------------------------
            // 2. Upsert TaggedElement projection
            //    Match on (ProjectId, UniqueId == IfcGlobalId) — we reuse
            //    TaggedElement.UniqueId to carry the IfcGlobalId for
            //    non-Revit hosts. RevitElementId stays 0 for non-Revit.
            // -------------------------------------------------------
            var batchGuids = batch
                .Select(e => e.IfcGlobalId)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .ToList();

            var existingTagged = await _db.TaggedElements
                .IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId
                            && t.ProjectId == projectId
                            && batchGuids.Contains(t.UniqueId))
                .ToDictionaryAsync(t => t.UniqueId, ct);

            foreach (var el in batch)
            {
                if (string.IsNullOrWhiteSpace(el.IfcGlobalId)) continue;

                // Validate the inbound ValidationErrors blob against the canonical
                // contract shape (ValidationErrorDto[]) without rewriting it — the
                // raw string is still stored verbatim below (full retype of the
                // wire field is the deferred breaking cutover). This is the
                // read/write site where the contract type is exercised.
                if (!ValidationErrorDto.TryParse(el.ValidationErrors, out _)) nonCanonicalVe++;

                if (existingTagged.TryGetValue(el.IfcGlobalId, out var t))
                {
                    // Stale-write protection
                    if (el.LastModifiedUtc.HasValue
                        && t.LastModifiedUtc.HasValue
                        && el.LastModifiedUtc.Value < t.LastModifiedUtc.Value)
                    {
                        skipped++;
                        continue;
                    }

                    t.Disc = el.Discipline; t.Loc = el.Location; t.Zone = el.Zone; t.Lvl = el.Level;
                    t.Sys = el.System; t.Func = el.Function; t.Prod = el.Product; t.Seq = el.Sequence;
                    t.Tag1 = el.FullTag;
                    t.CategoryName = el.CategoryName; t.FamilyName = el.FamilyName; t.TypeName = el.TypeName;
                    t.Status = el.Status; t.Rev = el.Rev; t.RoomName = el.RoomName; t.Level = el.LevelName;
                    t.IsComplete = el.IsComplete; t.IsFullyResolved = el.IsFullyResolved; t.IsStale = el.IsStale;
                    t.ValidationErrors = el.ValidationErrors;
                    t.LastModifiedUtc = el.LastModifiedUtc ?? nowUtc;
                    updElements++;
                }
                else
                {
                    _db.TaggedElements.Add(new TaggedElement
                    {
                        TenantId = tenantId,
                        ProjectId = projectId,
                        UniqueId = el.IfcGlobalId,
                        RevitElementId = 0,  // host-agnostic — IFC GlobalId is the key
                        Disc = el.Discipline, Loc = el.Location, Zone = el.Zone, Lvl = el.Level,
                        Sys = el.System, Func = el.Function, Prod = el.Product, Seq = el.Sequence,
                        Tag1 = el.FullTag,
                        CategoryName = el.CategoryName, FamilyName = el.FamilyName, TypeName = el.TypeName,
                        Status = el.Status, Rev = el.Rev, RoomName = el.RoomName, Level = el.LevelName,
                        IsComplete = el.IsComplete, IsFullyResolved = el.IsFullyResolved, IsStale = el.IsStale,
                        ValidationErrors = el.ValidationErrors,
                        LastModifiedUtc = el.LastModifiedUtc ?? nowUtc,
                    });
                    newElements++;
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        // -------------------------------------------------------
        // 3. Upsert the canonical cross-tool ElementGlobalIdRegistry.
        //    ExternalElementMapping is the per-(host,doc) ingest ledger;
        //    the registry is the one-row-per-GlobalId coordinator table
        //    that clash/issue/BCF linking dereference. Populating it here
        //    means EVERY /ifc/data host (bonsai, blender, revit, tekla…)
        //    lands a registry row, not just the ArchiCAD /ifc/ingest path.
        //    Host-aware: the tool's own id is parked in the matching column
        //    (RevitUniqueId / ArchiCadGuid / TeklaGuid); bonsai/blender just
        //    register the canonical identity + metadata.
        // -------------------------------------------------------
        try
        {
            await UpsertGlobalIdRegistryAsync(tenantId, projectId, host, request.Elements, nowUtc, ct);
        }
        catch (Exception ex)
        {
            // Never let the secondary registry write fail the primary ingest.
            _logger?.LogWarning(ex, "[ifc-ingest] GlobalIdRegistry upsert failed (mappings + elements still committed)");
            warnings.Add("GlobalIdRegistry upsert failed — cross-host mapping + tagged elements were still saved.");
        }

        if (nonCanonicalVe > 0)
            warnings.Add($"{nonCanonicalVe} element(s) carried a ValidationErrors blob that does not " +
                         "parse as the canonical ValidationErrorDto[] shape ([{code,message,severity}]); " +
                         "stored verbatim — producers should converge on that shape.");

        // Pre-merge Gate 3 — cross-host ingest instrumentation. Purely
        // observational (no behaviour change). Emits the resolved cross-host
        // KEY (IFC GlobalId) + HOST for each ingest so the cross-host
        // validation checklist (docs/CROSS_HOST_VALIDATION_CHECKLIST.md) is
        // grep-checkable: after a Revit push you can confirm
        //   host=revit key=<22-char GlobalId>
        // and after an ArchiCAD push that the SAME key lands with host=archicad.
        // Keys are sampled (first 10) to keep large federated pushes from
        // flooding the log; the count carries the full cardinality.
        var resolvedKeys = request.Elements
            .Select(e => e.IfcGlobalId)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct()
            .ToList();
        var sampleKeys = string.Join(", ", resolvedKeys.Take(10));
        _logger?.LogInformation(
            "[ifc-ingest] cross-host upsert host={Host} project={ProjectId} hostDoc={HostDocumentGuid} " +
            "keys={KeyCount} newMappings={NewMappings} updMappings={UpdMappings} " +
            "newElements={NewElements} updElements={UpdElements} skipped={Skipped} sampleKeys=[{SampleKeys}]",
            host, projectId, request.HostDocumentGuid ?? "(none)",
            resolvedKeys.Count, newMappings, updMappings,
            newElements, updElements, skipped, sampleKeys);

        return new IfcIngestResponse
        {
            NewMappings = newMappings,
            UpdatedMappings = updMappings,
            NewElements = newElements,
            UpdatedElements = updElements,
            Skipped = skipped,
            Warnings = warnings,
        };
    }

    // ------------------------------------------------------------------
    // Shared mapping-upsert for one batch. Does NOT SaveChanges — the
    // caller controls the transaction boundary. Returns (created, updated).
    // host must already be normalised.
    // ------------------------------------------------------------------
    private async Task<(int created, int updated)> UpsertMappingBatchAsync(
        Guid tenantId, Guid projectId, string host, string? hostDocumentGuid,
        IReadOnlyList<ElementMappingDto> batch, DateTime nowUtc, CancellationToken ct)
    {
        if (batch.Count == 0) return (0, 0);

        var batchGuids = batch
            .Select(m => m.IfcGlobalId)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .ToList();

        var existing = await _db.ExternalElementMappings
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId
                        && m.ProjectId == projectId
                        && m.Host == host
                        && batchGuids.Contains(m.IfcGlobalId)
                        && m.HostDocumentGuid == hostDocumentGuid)
            .ToDictionaryAsync(m => m.IfcGlobalId, ct);

        int created = 0, updated = 0;
        // Track guids added in THIS batch so duplicate ids within one payload
        // don't insert two rows (they'd collide on the unique index).
        var addedThisBatch = new Dictionary<string, ExternalElementMapping>();

        foreach (var m in batch)
        {
            if (string.IsNullOrWhiteSpace(m.IfcGlobalId)) continue;

            if (existing.TryGetValue(m.IfcGlobalId, out var row)
                || addedThisBatch.TryGetValue(m.IfcGlobalId, out row))
            {
                row.HostElementId = m.HostElementId;
                row.HostDisplayLabel = m.HostDisplayLabel;
                row.LastSeenUtc = nowUtc;
                row.IngestionCount += 1;
                // Only count a first-time touch in this batch as "updated".
                if (existing.ContainsKey(m.IfcGlobalId)) updated++;
            }
            else
            {
                var added = new ExternalElementMapping
                {
                    TenantId = tenantId,
                    ProjectId = projectId,
                    IfcGlobalId = m.IfcGlobalId,
                    Host = host,
                    HostElementId = m.HostElementId,
                    HostDocumentGuid = hostDocumentGuid,
                    HostDisplayLabel = m.HostDisplayLabel,
                    FirstSeenUtc = nowUtc,
                    LastSeenUtc = nowUtc,
                    IngestionCount = 1,
                };
                _db.ExternalElementMappings.Add(added);
                addedThisBatch[m.IfcGlobalId] = added;
                created++;
            }
        }

        return (created, updated);
    }

    // ------------------------------------------------------------------
    // Canonical cross-tool registry upsert. One row per (project, GlobalId).
    // Host-aware: parks the host element id in the tool-specific column when
    // there is one; bonsai/blender/headless register the canonical identity
    // + metadata only. Idempotent — re-pushing the same GlobalId from a
    // second host fills in that host's column on the existing row.
    // ------------------------------------------------------------------
    private async Task UpsertGlobalIdRegistryAsync(
        Guid tenantId, Guid projectId, string host,
        IReadOnlyList<IfcElementDto> elements, DateTime nowUtc, CancellationToken ct)
    {
        var guids = elements
            .Select(e => e.IfcGlobalId)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct()
            .ToList();
        if (guids.Count == 0) return;

        var existing = await _db.GlobalIdRegistry
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.ProjectId == projectId
                        && r.IfcGlobalId != null && guids.Contains(r.IfcGlobalId))
            .ToDictionaryAsync(r => r.IfcGlobalId ?? "", ct);

        var addedThisCall = new Dictionary<string, ElementGlobalIdRegistry>();
        int written = 0;

        foreach (var el in elements)
        {
            if (string.IsNullOrWhiteSpace(el.IfcGlobalId)) continue;

            if (!existing.TryGetValue(el.IfcGlobalId, out var reg)
                && !addedThisCall.TryGetValue(el.IfcGlobalId, out reg))
            {
                reg = new ElementGlobalIdRegistry
                {
                    TenantId = tenantId,
                    ProjectId = projectId,
                    IfcGlobalId = el.IfcGlobalId,
                    MappingStatus = "AutoMatched",
                };
                _db.GlobalIdRegistry.Add(reg);
                addedThisCall[el.IfcGlobalId] = reg;
                written++;
            }
            else
            {
                reg.UpdatedAt = nowUtc;
            }

            // Canonical metadata — fill when present, never blank an existing value.
            if (!string.IsNullOrWhiteSpace(el.IfcClass)) reg.IfcType = el.IfcClass;
            if (!string.IsNullOrWhiteSpace(el.HostDisplayLabel)) reg.ElementName = el.HostDisplayLabel;
            if (!string.IsNullOrWhiteSpace(el.Discipline)) reg.Discipline = el.Discipline;
            if (!string.IsNullOrWhiteSpace(el.LevelName)) reg.NormalizedLevelName = el.LevelName;

            // Park the host element id in the matching tool column.
            switch (host)
            {
                case MappingHosts.Revit:
                    if (!string.IsNullOrWhiteSpace(el.HostElementId)) reg.RevitUniqueId = el.HostElementId;
                    break;
                case MappingHosts.ArchiCad:
                    if (!string.IsNullOrWhiteSpace(el.HostElementId)) reg.ArchiCadGuid = el.HostElementId;
                    break;
                case MappingHosts.Tekla:
                    if (!string.IsNullOrWhiteSpace(el.HostElementId)) reg.TeklaGuid = el.HostElementId;
                    break;
                // bonsai / blender / headless / iot — canonical identity only;
                // no dedicated authoring-tool column on the registry.
            }
        }

        if (written > 0 || addedThisCall.Count == 0)
            await _db.SaveChangesAsync(ct);

        _logger?.LogInformation(
            "[ifc-ingest] GlobalIdRegistry upsert host={Host} project={ProjectId} guids={GuidCount} new={New}",
            host, projectId, guids.Count, written);
    }

    private static IEnumerable<List<T>> Chunk<T>(IEnumerable<T> source, int size)
    {
        var batch = new List<T>(size);
        foreach (var item in source)
        {
            batch.Add(item);
            if (batch.Count >= size)
            {
                yield return batch;
                batch = new List<T>(size);
            }
        }
        if (batch.Count > 0) yield return batch;
    }
}
