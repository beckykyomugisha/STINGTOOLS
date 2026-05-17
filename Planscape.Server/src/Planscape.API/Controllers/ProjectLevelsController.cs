using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Gap 7 — Level harmonisation.
///
/// The coordinator defines the normalised storey dictionary for the project
/// and maps each authoring tool's own level naming convention to it. Used by:
///   • Clash detection — groups results by canonical floor name.
///   • 3D viewer — storey-filter controls use NormalizedName.
///   • GlobalId registry — NormalizedLevelName FK links elements to storeys.
///   • STING plugin — LVL token is written using these canonical codes.
///
/// Endpoints:
///   GET    /api/projects/{id}/levels              — list all levels (sorted)
///   POST   /api/projects/{id}/levels              — create a level
///   PUT    /api/projects/{id}/levels/{levelId}    — full update
///   DELETE /api/projects/{id}/levels/{levelId}    — remove
///   POST   /api/projects/{id}/levels/auto-detect  — scan SceneNodes for level codes
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/levels")]
[Authorize]
public class ProjectLevelsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;

    public ProjectLevelsController(PlanscapeDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    // ── List ───────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/projects/{id}/levels
    /// Returns all levels for the project sorted by SortIndex (ascending).
    /// ToolMappings is deserialised from JSON for the response.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, CancellationToken ct)
    {
        var rows = await _db.ProjectLevels.AsNoTracking()
            .Where(l => l.ProjectId == projectId && l.TenantId == _tenant.TenantId)
            .OrderBy(l => l.SortIndex)
            .ToListAsync(ct);

        return Ok(rows.Select(l => new
        {
            l.Id,
            l.NormalizedName,
            l.DisplayName,
            l.ElevationM,
            l.SortIndex,
            toolMappings = DeserializeToolMappings(l.ToolMappingsJson),
        }).ToList());
    }

    // ── Create ─────────────────────────────────────────────────────────────

    public sealed record ToolLevelMappingDto(string Tool, string ToolLevelName);

    public sealed record LevelCreateDto(
        string NormalizedName,
        string? DisplayName,
        double? ElevationM,
        int SortIndex,
        List<ToolLevelMappingDto>? ToolMappings);

    /// <summary>POST /api/projects/{id}/levels</summary>
    [HttpPost]
    public async Task<ActionResult<ProjectLevel>> Create(
        Guid projectId,
        [FromBody] LevelCreateDto dto,
        CancellationToken ct)
    {
        var row = new ProjectLevel
        {
            TenantId         = _tenant.TenantId,
            ProjectId        = projectId,
            NormalizedName   = dto.NormalizedName,
            DisplayName      = dto.DisplayName,
            ElevationM       = dto.ElevationM,
            SortIndex        = dto.SortIndex,
            ToolMappingsJson = JsonSerializer.Serialize(dto.ToolMappings ?? []),
        };

        _db.ProjectLevels.Add(row);
        await _db.SaveChangesAsync(ct);
        return StatusCode(StatusCodes.Status201Created, row);
    }

    // ── Update ─────────────────────────────────────────────────────────────

    public sealed record LevelUpdateDto(
        string? NormalizedName,
        string? DisplayName,
        double? ElevationM,
        int? SortIndex,
        List<ToolLevelMappingDto>? ToolMappings);

    /// <summary>PUT /api/projects/{id}/levels/{levelId}</summary>
    [HttpPut("{levelId:guid}")]
    public async Task<ActionResult<ProjectLevel>> Update(
        Guid projectId,
        Guid levelId,
        [FromBody] LevelUpdateDto dto,
        CancellationToken ct)
    {
        var row = await _db.ProjectLevels
            .FirstOrDefaultAsync(l => l.Id == levelId
                && l.ProjectId == projectId
                && l.TenantId  == _tenant.TenantId, ct);

        if (row == null) return NotFound();

        if (dto.NormalizedName != null) row.NormalizedName = dto.NormalizedName;
        if (dto.DisplayName    != null) row.DisplayName    = dto.DisplayName;
        if (dto.ElevationM     != null) row.ElevationM     = dto.ElevationM;
        if (dto.SortIndex      != null) row.SortIndex      = dto.SortIndex.Value;
        if (dto.ToolMappings   != null) row.ToolMappingsJson = JsonSerializer.Serialize(dto.ToolMappings);
        row.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    // ── Delete ─────────────────────────────────────────────────────────────

    /// <summary>DELETE /api/projects/{id}/levels/{levelId}</summary>
    [HttpDelete("{levelId:guid}")]
    public async Task<ActionResult> Delete(
        Guid projectId,
        Guid levelId,
        CancellationToken ct)
    {
        var row = await _db.ProjectLevels
            .FirstOrDefaultAsync(l => l.Id == levelId
                && l.ProjectId == projectId
                && l.TenantId  == _tenant.TenantId, ct);

        if (row == null) return NotFound();
        _db.ProjectLevels.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Auto-detect ────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/projects/{id}/levels/auto-detect
    ///
    /// Scans distinct LevelCode values from SceneNodes in the project and
    /// creates a ProjectLevel row for each code that does not yet have one.
    /// Existing levels are left untouched (idempotent).
    ///
    /// Returns: { created: N, total: M } where total is the number of
    /// distinct level codes found in SceneNodes (registered or not).
    /// </summary>
    [HttpPost("auto-detect")]
    public async Task<ActionResult> AutoDetect(Guid projectId, CancellationToken ct)
    {
        var levelCodes = await _db.SceneNodes.AsNoTracking()
            .Where(n => n.ProjectId == projectId
                     && n.DeletedAt == null
                     && n.LevelCode != null)
            .Select(n => n.LevelCode!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

        int created = 0;
        int idx     = 0;

        foreach (var code in levelCodes)
        {
            var alreadyExists = await _db.ProjectLevels
                .AnyAsync(l => l.ProjectId == projectId && l.NormalizedName == code, ct);

            if (alreadyExists)
            {
                idx++;
                continue;
            }

            _db.ProjectLevels.Add(new ProjectLevel
            {
                TenantId       = _tenant.TenantId,
                ProjectId      = projectId,
                NormalizedName = code,
                DisplayName    = code,
                SortIndex      = idx,
            });
            created++;
            idx++;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { created, total = levelCodes.Count });
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static object DeserializeToolMappings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<object>();
        try { return JsonSerializer.Deserialize<object>(json) ?? Array.Empty<object>(); }
        catch { return Array.Empty<object>(); }
    }
}
