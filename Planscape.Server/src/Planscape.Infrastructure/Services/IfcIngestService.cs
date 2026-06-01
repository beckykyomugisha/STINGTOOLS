using Microsoft.EntityFrameworkCore;
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
    private const int IngestBatchSize = 500;

    public IfcIngestService(PlanscapeDbContext db) => _db = db;

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
