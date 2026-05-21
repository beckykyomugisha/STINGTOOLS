using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// IFC-data ingest endpoint. Receives STING tag + host-element-id payloads
/// from any host plugin (Revit, BlenderBIM, ArchiCAD, Tekla); upserts both
/// the cross-host <see cref="ExternalElementMapping"/> table and the
/// <see cref="TaggedElement"/> projection used by mobile / web dashboards.
///
/// Distinct from <c>/api/tagsync/sync</c> in that it carries
/// <c>IfcGlobalId</c> as the canonical key (cross-host stable) rather
/// than Revit-specific <c>RevitElementId</c>, and it carries explicit
/// host attribution so issues raised on one host surface on every host
/// looking at the same IFC element.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/ifc")]
[Authorize]
[EnableRateLimiting("mobile")]
public class IfcController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private const int IngestBatchSize = 500;
    private static readonly HashSet<string> ValidHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "revit", "blender", "archicad", "tekla", "headless",
    };

    public IfcController(PlanscapeDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// POST /api/projects/{projectId}/ifc/data
    /// Ingest IFC-element data + host-element-id mapping from a host plugin.
    /// Returns counts of new vs updated mappings + elements.
    /// </summary>
    [HttpPost("data")]
    public async Task<ActionResult<IfcIngestResponse>> IngestData(
        Guid projectId,
        [FromBody] IfcIngestRequest request)
    {
        if (request is null) return BadRequest("missing body");
        if (request.Elements is null || request.Elements.Count == 0)
            return BadRequest("Elements is empty");

        var host = (request.Host ?? "").Trim().ToLowerInvariant();
        if (!ValidHosts.Contains(host))
            return BadRequest($"unknown host '{request.Host}'; expected one of {string.Join(", ", ValidHosts)}");

        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project is null) return NotFound("project not found in this tenant");

        var resp = new IfcIngestResponse
        {
            Warnings = new List<string>(),
        };
        int newMappings = 0, updMappings = 0;
        int newElements = 0, updElements = 0, skipped = 0;
        var nowUtc = DateTime.UtcNow;

        // Process in batches of 500 to keep transactions small
        foreach (var batch in Chunk(request.Elements, IngestBatchSize))
        {
            // -------------------------------------------------------
            // 1. Upsert ExternalElementMapping rows
            // -------------------------------------------------------
            var batchGuids = batch.Select(e => e.IfcGlobalId).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
            var existingMappings = await _db.ExternalElementMappings
                .Where(m => m.ProjectId == projectId
                            && m.Host == host
                            && batchGuids.Contains(m.IfcGlobalId)
                            && m.HostDocumentGuid == request.HostDocumentGuid)
                .ToDictionaryAsync(m => m.IfcGlobalId);

            foreach (var el in batch)
            {
                if (string.IsNullOrWhiteSpace(el.IfcGlobalId))
                {
                    skipped++;
                    resp.Warnings.Add($"skipped element with empty IfcGlobalId (host_element_id={el.HostElementId})");
                    continue;
                }

                if (existingMappings.TryGetValue(el.IfcGlobalId, out var mapping))
                {
                    mapping.HostElementId = el.HostElementId;
                    mapping.HostDisplayLabel = el.HostDisplayLabel;
                    mapping.LastSeenUtc = nowUtc;
                    mapping.IngestionCount += 1;
                    updMappings++;
                }
                else
                {
                    _db.ExternalElementMappings.Add(new ExternalElementMapping
                    {
                        TenantId = tenantId,
                        ProjectId = projectId,
                        IfcGlobalId = el.IfcGlobalId,
                        Host = host,
                        HostElementId = el.HostElementId,
                        HostDocumentGuid = request.HostDocumentGuid,
                        HostDisplayLabel = el.HostDisplayLabel,
                        FirstSeenUtc = nowUtc,
                        LastSeenUtc = nowUtc,
                        IngestionCount = 1,
                    });
                    newMappings++;
                }
            }

            // -------------------------------------------------------
            // 2. Upsert TaggedElement projection
            //    Match on (ProjectId, UniqueId == IfcGlobalId) — we reuse
            //    TaggedElement.UniqueId to carry the IfcGlobalId for
            //    non-Revit hosts. RevitElementId stays 0 for non-Revit.
            // -------------------------------------------------------
            var existingTagged = await _db.TaggedElements
                .Where(t => t.ProjectId == projectId && batchGuids.Contains(t.UniqueId))
                .ToDictionaryAsync(t => t.UniqueId);

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

            await _db.SaveChangesAsync();
        }

        return Ok(new IfcIngestResponse
        {
            NewMappings = newMappings,
            UpdatedMappings = updMappings,
            NewElements = newElements,
            UpdatedElements = updElements,
            Skipped = skipped,
            Warnings = resp.Warnings,
        });
    }

    /// <summary>
    /// GET /api/projects/{projectId}/ifc/mappings?ifc_guid=...
    /// Look up the host-element-id for a given IFC GlobalId across all
    /// hosts in this project. Used by cross-host issue resolution
    /// (issue raised in Blender → find Revit ElementId).
    /// </summary>
    [HttpGet("mappings")]
    public async Task<ActionResult<MappingsPage>> GetMappings(
        Guid projectId,
        [FromQuery] string? ifcGuid = null,
        [FromQuery] string? host = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 200)
    {
        var tenantId = GetTenantId();
        var exists = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!exists) return NotFound();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 1000);

        var q = _db.ExternalElementMappings.Where(m => m.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(ifcGuid)) q = q.Where(m => m.IfcGlobalId == ifcGuid);
        if (!string.IsNullOrWhiteSpace(host))    q = q.Where(m => m.Host == host.ToLower());

        var total = await q.CountAsync();
        var rows = await q
            .OrderBy(m => m.IfcGlobalId)
            .ThenBy(m => m.Host)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new MappingsPage
        {
            Items = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            HasNextPage = (page * pageSize) < total,
        });
    }

    /// <summary>Paginated response wrapper for the mappings GET endpoint.</summary>
    public class MappingsPage
    {
        public List<ExternalElementMapping> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public bool HasNextPage { get; set; }
    }

    // ------------------------------------------------------------------

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return claim != null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
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
