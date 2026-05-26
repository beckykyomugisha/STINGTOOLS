namespace Planscape.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

/// <summary>
/// Gap A — CRUD surface for the project's canonical coordinate system.
/// One row per project; the coordinator declares the CRS, benchmark
/// origin, true-north angle, and length unit here. All model alignment
/// checks (Gap B transforms, IfcAlignmentReports) are validated against
/// this definition.
/// Route: /api/projects/{projectId}/coordinate-system
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/coordinate-system")]
[Authorize]
public class CoordinateSystemController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;

    public CoordinateSystemController(PlanscapeDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    // ── GET /api/projects/{projectId}/coordinate-system ──────────────────────
    /// <summary>Returns the project's coordinate system, or 404 if not yet defined.</summary>
    [HttpGet]
    public async Task<ActionResult<ProjectCoordinateSystem>> Get(Guid projectId, CancellationToken ct)
    {
        var crs = await _db.ProjectCoordinateSystems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.TenantId == _tenant.TenantId, ct);

        return crs == null ? NotFound() : Ok(crs);
    }

    // ── POST /api/projects/{projectId}/coordinate-system ─────────────────────
    /// <summary>
    /// Creates the coordinate system for the project.
    /// Returns 409 Conflict if one already exists — use PUT to update.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ProjectCoordinateSystem>> Create(
        Guid projectId,
        [FromBody] CoordinateSystemDto dto,
        CancellationToken ct)
    {
        var existing = await _db.ProjectCoordinateSystems
            .AnyAsync(x => x.ProjectId == projectId && x.TenantId == _tenant.TenantId, ct);

        if (existing)
            return Conflict(new { message = "A coordinate system already exists for this project. Use PUT to update it." });

        var entity = new ProjectCoordinateSystem
        {
            TenantId        = _tenant.TenantId,
            ProjectId       = projectId,
            CrsEpsgCode     = dto.CrsEpsgCode,
            CrsName         = dto.CrsName,
            OriginEasting   = dto.OriginEasting,
            OriginNorthing  = dto.OriginNorthing,
            OriginElevation = dto.OriginElevation,
            TrueNorthDeg    = dto.TrueNorthDeg,
            LengthUnit      = dto.LengthUnit ?? "mm",
            ReferenceModelId = dto.ReferenceModelId,
            Notes           = dto.Notes,
            DefinedBy       = User.Identity?.Name,
            DefinedAt       = DateTime.UtcNow,
            CreatedAt       = DateTime.UtcNow,
        };

        _db.ProjectCoordinateSystems.Add(entity);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { projectId }, entity);
    }

    // ── PUT /api/projects/{projectId}/coordinate-system ──────────────────────
    /// <summary>
    /// Updates the project's coordinate system.
    /// Returns 404 if none exists — use POST to create.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<ProjectCoordinateSystem>> Update(
        Guid projectId,
        [FromBody] CoordinateSystemDto dto,
        CancellationToken ct)
    {
        var entity = await _db.ProjectCoordinateSystems
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.TenantId == _tenant.TenantId, ct);

        if (entity == null)
            return NotFound();

        entity.CrsEpsgCode      = dto.CrsEpsgCode;
        entity.CrsName          = dto.CrsName;
        entity.OriginEasting    = dto.OriginEasting;
        entity.OriginNorthing   = dto.OriginNorthing;
        entity.OriginElevation  = dto.OriginElevation;
        entity.TrueNorthDeg     = dto.TrueNorthDeg;
        entity.LengthUnit       = dto.LengthUnit ?? entity.LengthUnit;
        entity.ReferenceModelId = dto.ReferenceModelId;
        entity.Notes            = dto.Notes;
        entity.UpdatedAt        = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(entity);
    }

    // ── DELETE /api/projects/{projectId}/coordinate-system ───────────────────
    /// <summary>
    /// Hard-deletes the project's coordinate system so the coordinator can
    /// start fresh. Existing IfcAlignmentReports and ProjectModelTransforms
    /// are NOT cascade-deleted — they remain for audit purposes.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(Guid projectId, CancellationToken ct)
    {
        var entity = await _db.ProjectCoordinateSystems
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.TenantId == _tenant.TenantId, ct);

        if (entity == null)
            return NotFound();

        _db.ProjectCoordinateSystems.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>
/// DTO for creating or updating a <see cref="ProjectCoordinateSystem"/>.
/// Used by both POST and PUT bodies.
/// </summary>
public sealed class CoordinateSystemDto
{
    /// <summary>EPSG code, e.g. "EPSG:27700".</summary>
    public string? CrsEpsgCode { get; set; }

    /// <summary>Human-readable CRS name, e.g. "British National Grid".</summary>
    public string? CrsName { get; set; }

    /// <summary>Benchmark easting in the CRS (metres).</summary>
    public double? OriginEasting { get; set; }

    /// <summary>Benchmark northing in the CRS (metres).</summary>
    public double? OriginNorthing { get; set; }

    /// <summary>Benchmark elevation above datum (metres).</summary>
    public double? OriginElevation { get; set; }

    /// <summary>True north offset from CRS Y-axis, clockwise positive (degrees). Defaults to 0.</summary>
    public double TrueNorthDeg { get; set; } = 0.0;

    /// <summary>Canonical project length unit: "mm" | "m". Defaults to "mm".</summary>
    public string? LengthUnit { get; set; }

    /// <summary>
    /// Optional reference model ID. When supplied the auto-align service uses
    /// this model's IfcMapConversion as the base frame for new uploads.
    /// </summary>
    public Guid? ReferenceModelId { get; set; }

    /// <summary>Free-text coordinator notes (survey reference, monument ID, etc.).</summary>
    public string? Notes { get; set; }
}
