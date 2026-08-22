using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Constants;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;
using Planscape.API.Services;

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
///
/// SIBLING ENDPOINT: this and <c>/api/tagsync/sync</c>
/// (<see cref="TagSyncController"/>) are the two ingest paths that write the
/// same <see cref="TaggedElement"/> table. This (verbose
/// <see cref="Planscape.Core.DTOs.IfcElementDto"/>: <c>Discipline</c>/
/// <c>Location</c>/<c>FullTag</c>…, <c>RevitElementId = 0</c>) is the non-Revit
/// path; tagsync (abbreviated <c>TagElementDto</c>: <c>Disc</c>/<c>Loc</c>/
/// <c>Tag1</c>…) is the Revit/BCC path. The DTOs deliberately diverge by field
/// name — they are NOT wire-compatible — but both map onto the same
/// TaggedElement columns. See <c>Planscape.Server/docs/element-ingest-paths.md</c>
/// for the full field diff, the shared-core invariant, and the filtered-unique
/// -index key-space contract (Revit keyed on <c>RevitElementId &gt; 0</c>,
/// non-Revit on <c>UniqueId</c>) that lets both hosts coexist in one table.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/ifc")]
[Authorize]
[ProjectAccess]   // cross-project read gate (404 for a project the caller cannot see)
[EnableRateLimiting("mobile")]
public class IfcController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IIdentityResolverService _identity;
    private readonly IIfcIngestService _ingest;
    private readonly ILogger<IfcController> _logger;

    public IfcController(
        PlanscapeDbContext db,
        IIdentityResolverService identity,
        IIfcIngestService ingest,
        ILogger<IfcController> logger)
    {
        _db = db;
        _identity = identity;
        _ingest = ingest;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/projects/{projectId}/ifc/data
    /// Ingest IFC-element data + host-element-id mapping from a host plugin.
    /// Returns counts of new vs updated mappings + elements.
    /// </summary>
    [HttpPost("data")]
    [ProducesResponseType(typeof(IfcIngestResponse), 200)]
    public async Task<ActionResult<IfcIngestResponse>> IngestData(
        Guid projectId,
        [FromBody] IfcIngestRequest request)
    {
        // Cross-project write gate: only a member of THIS project (or a tenant
        // Admin/Owner) may ingest into it — mirrors IfcIngestController/tagsync.
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;
        if (request is null) return BadRequest("missing body");

        // C3 — a push may legitimately carry ONLY removals: a save whose sole
        // change was deleting elements has nothing to upsert. Rejecting that as
        // "Elements is empty" would drop exactly the deletions this exists to
        // deliver.
        bool hasElements = request.Elements is { Count: > 0 };
        bool hasRemovals = request.RemovedGlobalIds is { Count: > 0 };
        if (!hasElements && !hasRemovals)
            return BadRequest("Elements is empty");

        var host = MappingHosts.Normalize(request.Host);
        if (!MappingHosts.IsValid(host))
            return BadRequest($"unknown host '{request.Host}'; expected one of {string.Join(", ", MappingHosts.All)}");

        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project is null) return NotFound("project not found in this tenant");

        // Ingest (ExternalElementMapping upsert + TaggedElement projection) is
        // owned by IIfcIngestService so TagSync + ArchiCAD feed the same path.
        var response = hasElements
            ? await _ingest.IngestAsync(tenantId, projectId, request)
            : new IfcIngestResponse();

        int removed = 0;
        if (hasRemovals)
            removed = await ApplyRemovalsAsync(tenantId, projectId, host, request);

        return Ok(response with { Removed = removed });
    }

    /// <summary>
    /// C3 — soft-delete elements this host document no longer contains.
    ///
    /// <para><b>Soft, not hard.</b> TaggedElement is ISoftDeletable and carries
    /// tag history, issues and clash references; hard-deleting would break those
    /// and make an accidental deletion in the authoring tool unrecoverable. The
    /// global query filter hides <c>DeletedAtUtc != null</c> rows from every read
    /// path, so a tombstoned element stops rendering and stops answering queries
    /// — which is the observable behaviour that was missing.</para>
    ///
    /// <para><b>Scoped to the reporting host document.</b> Only elements this
    /// document contributed can be removed, resolved through
    /// ExternalElementMapping. Without that scope a full-export diff from one
    /// host would tombstone another host's contribution to the same project —
    /// turning a missing-delete bug into a data-loss one.</para>
    /// </summary>
    private async Task<int> ApplyRemovalsAsync(
        Guid tenantId, Guid projectId, string host, IfcIngestRequest request)
    {
        var ids = request.RemovedGlobalIds
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct()
            .ToList();
        if (ids.Count == 0) return 0;

        // Which of these did THIS host document actually contribute?
        var ownedQuery = _db.ExternalElementMappings.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ProjectId == projectId
                     && m.Host == host && ids.Contains(m.IfcGlobalId));

        if (!string.IsNullOrWhiteSpace(request.HostDocumentGuid))
            ownedQuery = ownedQuery.Where(m => m.HostDocumentGuid == request.HostDocumentGuid);

        var owned = await ownedQuery.Select(m => m.IfcGlobalId).Distinct().ToListAsync();
        if (owned.Count == 0)
        {
            _logger.LogInformation(
                "IFC removals for project {ProjectId} host {Host}: none of the {Count} id(s) are attributed to this document — nothing removed.",
                projectId, host, ids.Count);
            return 0;
        }

        var now = DateTime.UtcNow;
        var targets = await _db.TaggedElements
            .Where(e => e.ProjectId == projectId && e.IfcGlobalId != null
                     && owned.Contains(e.IfcGlobalId) && e.DeletedAtUtc == null)
            .ToListAsync();

        foreach (var el in targets) el.DeletedAtUtc = now;
        if (targets.Count > 0) await _db.SaveChangesAsync();

        _logger.LogInformation(
            "IFC removals for project {ProjectId} host {Host} doc {Doc}: tombstoned {Removed} element(s) of {Requested} requested.",
            projectId, host, request.HostDocumentGuid ?? "(any)", targets.Count, ids.Count);

        return targets.Count;
    }

    /// <summary>
    /// GET /api/projects/{projectId}/ifc/mappings?ifcGuid=...
    /// Look up the host-element-id for a given IFC GlobalId across all
    /// hosts in this project. Used by cross-host issue resolution
    /// (issue raised in Blender → find Revit ElementId).
    /// </summary>
    [HttpGet("mappings")]
    [ProducesResponseType(typeof(MappingsPage), 200)]
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

    /// <summary>
    /// GET /api/projects/{projectId}/ifc/resolve?guid=...&amp;host=...
    /// K1 — resolve a canonical IFC GlobalId to its host-side element refs
    /// across every host (Revit, Blender, IoT, …), or filter to one host.
    /// This is the lookup meeting element-highlight write-back and the
    /// IoT/twin layer call to answer "which Revit element is this?".
    /// </summary>
    [HttpGet("resolve")]
    public async Task<ActionResult<IReadOnlyList<HostElementRef>>> Resolve(
        Guid projectId,
        [FromQuery] string guid,
        [FromQuery] string? host = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(guid)) return BadRequest("guid is required");
        var tenantId = GetTenantId();
        var exists = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (!exists) return NotFound();

        var refs = await _identity.ResolveHostElementsAsync(projectId, guid, host, ct);
        return Ok(refs);
    }

    /// <summary>
    /// POST /api/projects/{projectId}/ifc/iot-binding
    /// K1 — bind an IoT device to a model element via the same cross-host
    /// table (Host="iot"). The digital-twin layer calls this on device
    /// onboarding / commissioning sign-off so telemetry resolves to the
    /// element's canonical GlobalId. Idempotent.
    /// </summary>
    [HttpPost("iot-binding")]
    public async Task<ActionResult<IdentityBindResult>> BindIotDevice(
        Guid projectId,
        [FromBody] IotBindingRequest request,
        CancellationToken ct = default)
    {
        // Cross-project write gate: binding an IoT device mutates this project's
        // cross-host identity table — members (or Admin/Owner) only.
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        if (request is null) return BadRequest("missing body");
        if (string.IsNullOrWhiteSpace(request.IfcGlobalId)) return BadRequest("ifcGlobalId is required");
        if (string.IsNullOrWhiteSpace(request.DeviceId)) return BadRequest("deviceId is required");

        var tenantId = GetTenantId();
        var exists = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (!exists) return NotFound();

        var result = await _identity.BindIotDeviceAsync(
            projectId, request.IfcGlobalId, request.DeviceId,
            request.Label, request.HostDocumentGuid, ct);
        return Ok(result);
    }

    /// <summary>Body for POST iot-binding.</summary>
    public class IotBindingRequest
    {
        public string IfcGlobalId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string? Label { get; set; }
        public string? HostDocumentGuid { get; set; }
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
}
