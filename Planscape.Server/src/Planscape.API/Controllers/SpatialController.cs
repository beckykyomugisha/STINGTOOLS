using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// GET /api/projects/{projectId}/spatial
/// Returns the building's level and zone structure extracted from uploaded IFC models.
/// For now: returns a hardcoded but realistic structure seeded from ISO 19650 defaults.
/// Later phase: parse IfcBuildingStorey elements from uploaded IFC via a background job.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/spatial")]
[Authorize]
public class SpatialController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;

    public SpatialController(PlanscapeDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>
    /// Returns building levels and zones for the project.
    /// Tries to extract from model metadata first; falls back to ISO 19650 defaults.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SpatialStructureDto>> GetSpatialStructure(
        Guid projectId, CancellationToken ct)
    {
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == _tenant.TenantId, ct);

        if (project == null) return NotFound();

        // Try to get levels from uploaded model metadata.
        // For now return ISO 19650-aligned defaults — will be enriched by IFC parse job.
        var levels = new List<SpatialLevelDto>
        {
            new() { Code = "RF",  Label = "Roof" },
            new() { Code = "L03", Label = "Level 03" },
            new() { Code = "L02", Label = "Level 02" },
            new() { Code = "L01", Label = "Level 01" },
            new() { Code = "GF",  Label = "Ground Floor" },
            new() { Code = "B1",  Label = "Basement 1" },
        };

        var zones = new List<SpatialZoneDto>
        {
            new() { Code = "Z01", Label = "Zone 01" },
            new() { Code = "Z02", Label = "Zone 02" },
            new() { Code = "Z03", Label = "Zone 03" },
            new() { Code = "Z04", Label = "Zone 04" },
            new() { Code = "EXT", Label = "External" },
            new() { Code = "XX",  Label = "Unknown" },
        };

        return Ok(new SpatialStructureDto { Levels = levels, Zones = zones });
    }
}

public record SpatialLevelDto
{
    public string Code  { get; init; } = "";
    public string Label { get; init; } = "";
}

public record SpatialZoneDto
{
    public string Code  { get; init; } = "";
    public string Label { get; init; } = "";
}

public record SpatialStructureDto
{
    public List<SpatialLevelDto> Levels { get; init; } = new();
    public List<SpatialZoneDto>  Zones  { get; init; } = new();
}
