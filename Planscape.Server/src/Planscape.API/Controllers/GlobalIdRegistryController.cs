using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Gap 6 — Cross-tool GlobalId registry.
///
/// Lets the BIM coordinator view and correct element identity mappings
/// across ArchiCAD, Revit, and Tekla. Each row maps one physical
/// building element's identity in every authoring tool to a single
/// canonical IfcGlobalId so clash detection, issue linking, and BCF
/// topic references all refer to the same element regardless of tool.
///
/// Endpoints:
///   GET    /api/projects/{id}/global-id-registry          — list / filter
///   GET    /api/projects/{id}/global-id-registry/{id}     — single row
///   POST   /api/projects/{id}/global-id-registry          — create mapping
///   PATCH  /api/projects/{id}/global-id-registry/{id}     — update mapping
///   DELETE /api/projects/{id}/global-id-registry/{id}     — remove mapping
///   POST   /api/projects/{id}/global-id-registry/auto-match — scan FederatedElements
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/global-id-registry")]
[Authorize]
public class GlobalIdRegistryController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;

    public GlobalIdRegistryController(PlanscapeDbContext db, ITenantContext tenant)
    {
        _db    = db;
        _tenant = tenant;
    }

    // ── List ───────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/projects/{id}/global-id-registry
    /// Optional query filters: mappingStatus, discipline, search (name / IfcGuid / RevitUniqueId).
    /// Paged: page (1-based), pageSize (max 200).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> List(
        Guid projectId,
        [FromQuery] string? mappingStatus,
        [FromQuery] string? discipline,
        [FromQuery] string? search,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page     = Math.Max(page, 1);

        var q = _db.GlobalIdRegistry.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.TenantId == _tenant.TenantId);

        if (!string.IsNullOrWhiteSpace(mappingStatus))
            q = q.Where(r => r.MappingStatus == mappingStatus);

        if (!string.IsNullOrWhiteSpace(discipline))
            q = q.Where(r => r.Discipline == discipline);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(r => (r.ElementName    != null && r.ElementName.Contains(search))
                          || (r.IfcGlobalId    != null && r.IfcGlobalId.Contains(search))
                          || (r.RevitUniqueId  != null && r.RevitUniqueId.Contains(search))
                          || (r.ArchiCadGuid   != null && r.ArchiCadGuid.Contains(search)));

        var total = await q.CountAsync(ct);
        var rows  = await q
            .OrderBy(r => r.MappingStatus)
            .ThenBy(r => r.ElementName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, items = rows });
    }

    // ── Single ─────────────────────────────────────────────────────────────

    /// <summary>GET /api/projects/{id}/global-id-registry/{registryId}</summary>
    [HttpGet("{registryId:guid}")]
    public async Task<ActionResult<ElementGlobalIdRegistry>> Get(
        Guid projectId,
        Guid registryId,
        CancellationToken ct)
    {
        var row = await _db.GlobalIdRegistry.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == registryId
                && r.ProjectId == projectId
                && r.TenantId  == _tenant.TenantId, ct);

        return row == null ? NotFound() : Ok(row);
    }

    // ── Create ─────────────────────────────────────────────────────────────

    public sealed record RegistryCreateDto(
        string? IfcGlobalId,
        string? ArchiCadGuid,
        string? RevitUniqueId,
        string? TeklaGuid,
        string? Discipline,
        string? IfcType,
        string? ElementName,
        string? NormalizedLevelName,
        string? Notes);

    /// <summary>POST /api/projects/{id}/global-id-registry</summary>
    [HttpPost]
    public async Task<ActionResult<ElementGlobalIdRegistry>> Create(
        Guid projectId,
        [FromBody] RegistryCreateDto dto,
        CancellationToken ct)
    {
        var row = new ElementGlobalIdRegistry
        {
            TenantId            = _tenant.TenantId,
            ProjectId           = projectId,
            IfcGlobalId         = dto.IfcGlobalId,
            ArchiCadGuid        = dto.ArchiCadGuid,
            RevitUniqueId       = dto.RevitUniqueId,
            TeklaGuid           = dto.TeklaGuid,
            Discipline          = dto.Discipline,
            IfcType             = dto.IfcType,
            ElementName         = dto.ElementName,
            NormalizedLevelName = dto.NormalizedLevelName,
            MappingStatus       = "ManuallyMapped",
            MappedBy            = User.FindFirst("display_name")?.Value
                               ?? User.FindFirst("email")?.Value,
            Notes               = dto.Notes,
        };

        _db.GlobalIdRegistry.Add(row);
        await _db.SaveChangesAsync(ct);
        return StatusCode(StatusCodes.Status201Created, row);
    }

    // ── Update ─────────────────────────────────────────────────────────────

    public sealed record RegistryUpdateDto(
        string? ArchiCadGuid,
        string? RevitUniqueId,
        string? TeklaGuid,
        string? MappingStatus,
        string? NormalizedLevelName,
        string? Notes);

    /// <summary>PATCH /api/projects/{id}/global-id-registry/{registryId}</summary>
    [HttpPatch("{registryId:guid}")]
    public async Task<ActionResult<ElementGlobalIdRegistry>> Update(
        Guid projectId,
        Guid registryId,
        [FromBody] RegistryUpdateDto dto,
        CancellationToken ct)
    {
        var row = await _db.GlobalIdRegistry
            .FirstOrDefaultAsync(r => r.Id == registryId
                && r.ProjectId == projectId
                && r.TenantId  == _tenant.TenantId, ct);

        if (row == null) return NotFound();

        if (dto.ArchiCadGuid        != null) row.ArchiCadGuid        = dto.ArchiCadGuid;
        if (dto.RevitUniqueId       != null) row.RevitUniqueId       = dto.RevitUniqueId;
        if (dto.TeklaGuid           != null) row.TeklaGuid           = dto.TeklaGuid;
        if (dto.MappingStatus       != null) row.MappingStatus       = dto.MappingStatus;
        if (dto.NormalizedLevelName != null) row.NormalizedLevelName = dto.NormalizedLevelName;
        if (dto.Notes               != null) row.Notes               = dto.Notes;

        row.MappedBy  = User.FindFirst("display_name")?.Value ?? User.FindFirst("email")?.Value;
        row.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    // ── Delete ─────────────────────────────────────────────────────────────

    /// <summary>DELETE /api/projects/{id}/global-id-registry/{registryId}</summary>
    [HttpDelete("{registryId:guid}")]
    public async Task<ActionResult> Delete(
        Guid projectId,
        Guid registryId,
        CancellationToken ct)
    {
        var row = await _db.GlobalIdRegistry
            .FirstOrDefaultAsync(r => r.Id == registryId
                && r.ProjectId == projectId
                && r.TenantId  == _tenant.TenantId, ct);

        if (row == null) return NotFound();
        _db.GlobalIdRegistry.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Auto-match ─────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/projects/{id}/global-id-registry/auto-match
    ///
    /// Scans FederatedElements for elements that share the same IfcGuid across
    /// two or more source models (i.e., the same physical element uploaded by
    /// different tools). Creates a registry entry for each unregistered group.
    ///
    /// Returns: { matched: N, total: M } where total is the number of
    /// multi-tool groups found (whether or not they were already registered).
    /// </summary>
    [HttpPost("auto-match")]
    public async Task<ActionResult> AutoMatch(Guid projectId, CancellationToken ct)
    {
        var elements = await _db.FederatedElements.AsNoTracking()
            .Where(e => e.ProjectId == projectId && !e.IsDeleted && e.IfcGuid != null)
            .Select(e => new { e.IfcGuid, e.Category })
            .ToListAsync(ct);

        var multiToolGroups = elements
            .GroupBy(e => e.IfcGuid!)
            .Where(g => g.Count() > 1)
            .ToList();

        int matched = 0;
        foreach (var group in multiToolGroups)
        {
            var alreadyExists = await _db.GlobalIdRegistry
                .AnyAsync(r => r.ProjectId == projectId && r.IfcGlobalId == group.Key, ct);
            if (alreadyExists) continue;

            _db.GlobalIdRegistry.Add(new ElementGlobalIdRegistry
            {
                TenantId      = _tenant.TenantId,
                ProjectId     = projectId,
                IfcGlobalId   = group.Key,
                MappingStatus = "AutoMatched",
                Discipline    = group.First().Category != null
                    ? MapCategoryToDiscipline(group.First().Category!) : null,
                ElementName   = group.First().Category,
                UpdatedAt     = DateTime.UtcNow,
            });
            matched++;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { matched, total = multiToolGroups.Count });
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static string? MapCategoryToDiscipline(string category) => category switch
    {
        var c when c.Contains("Wall") || c.Contains("Floor")
               || c.Contains("Door") || c.Contains("Window")
               || c.Contains("Ceiling") || c.Contains("Room")  => "A",
        var c when c.Contains("Beam") || c.Contains("Column")
               || c.Contains("Foundation") || c.Contains("Slab")
               || c.Contains("Framing")                         => "S",
        var c when c.Contains("Duct") || c.Contains("Air")
               || c.Contains("Diffuser") || c.Contains("Coil")  => "M",
        var c when c.Contains("Pipe") || c.Contains("Plumb")
               || c.Contains("Valve") || c.Contains("Pump")     => "P",
        var c when c.Contains("Conduit") || c.Contains("Cable")
               || c.Contains("Panel") || c.Contains("Light")
               || c.Contains("Electrical")                      => "E",
        _                                                        => null
    };
}
