using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 177-D — tenant-scoped CRUD for named ACL presets used when
/// inviting / updating <see cref="ProjectMember"/> rows. Reads are
/// available to any signed-in user (the picker needs to render);
/// writes require Manager+ at the tenant level.
/// </summary>
[ApiController]
[Route("api/access-profiles")]
[Authorize]
[EnableRateLimiting("mobile")]
public class AccessProfilesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<AccessProfilesController> _log;

    public AccessProfilesController(PlanscapeDbContext db, ILogger<AccessProfilesController> log)
    {
        _db = db;
        _log = log;
    }

    [HttpGet]
    public async Task<ActionResult> List()
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var rows = await _db.AccessProfiles
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => Project(p))
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> Get(Guid id)
    {
        var tenantId = GetTenantId();
        var p = await _db.AccessProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        return p == null ? NotFound() : Ok(Project(p));
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] AccessProfileBody req)
    {
        if (!IsManagerOrAbove()) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "name is required" });

        var tenantId = GetTenantId();
        if (await _db.AccessProfiles.AnyAsync(p => p.TenantId == tenantId && p.Name == req.Name && p.IsActive))
            return Conflict(new { message = "An access profile with that name already exists." });

        var entity = new AccessProfile
        {
            TenantId             = tenantId,
            Name                 = req.Name.Trim(),
            Description          = req.Description?.Trim(),
            AllowedCdeStates     = ToCsv(req.AllowedCdeStates),
            AllowedDisciplines   = ToCsv(req.AllowedDisciplines),
            AllowedSuitabilities = ToCsv(req.AllowedSuitabilities),
            DefaultProjectRole   = string.IsNullOrWhiteSpace(req.DefaultProjectRole)  ? "Contributor" : req.DefaultProjectRole.Trim(),
            DefaultIso19650Role  = string.IsNullOrWhiteSpace(req.DefaultIso19650Role) ? "M"           : req.DefaultIso19650Role.Trim(),
            CreatedBy            = User.FindFirst("display_name")?.Value,
        };
        _db.AccessProfiles.Add(entity);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, Project(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, [FromBody] AccessProfileBody req)
    {
        if (!IsManagerOrAbove()) return Forbid();

        var tenantId = GetTenantId();
        var entity = await _db.AccessProfiles
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (entity == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name))         entity.Name        = req.Name.Trim();
        if (req.Description != null)                       entity.Description = req.Description.Trim();
        if (req.AllowedCdeStates     != null) entity.AllowedCdeStates     = ToCsv(req.AllowedCdeStates);
        if (req.AllowedDisciplines   != null) entity.AllowedDisciplines   = ToCsv(req.AllowedDisciplines);
        if (req.AllowedSuitabilities != null) entity.AllowedSuitabilities = ToCsv(req.AllowedSuitabilities);
        if (!string.IsNullOrWhiteSpace(req.DefaultProjectRole))  entity.DefaultProjectRole  = req.DefaultProjectRole.Trim();
        if (!string.IsNullOrWhiteSpace(req.DefaultIso19650Role)) entity.DefaultIso19650Role = req.DefaultIso19650Role.Trim();

        await _db.SaveChangesAsync();
        return Ok(Project(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        if (!IsManagerOrAbove()) return Forbid();
        var tenantId = GetTenantId();
        var entity = await _db.AccessProfiles
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (entity == null) return NotFound();
        // Soft-delete to preserve invite history references.
        entity.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    private bool IsManagerOrAbove()
    {
        var role = User.FindFirst("role")?.Value ?? "";
        return role is "Manager" or "Admin" or "Owner";
    }

    private static string? ToCsv(string[]? arr)
    {
        if (arr == null) return null;
        var cleaned = arr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
        return cleaned.Length == 0 ? null : string.Join(',', cleaned);
    }

    private static object Project(AccessProfile p) => new
    {
        p.Id,
        p.Name,
        p.Description,
        allowedCdeStates     = ProjectMember.ParseAllowList(p.AllowedCdeStates)     ?? Array.Empty<string>(),
        allowedDisciplines   = ProjectMember.ParseAllowList(p.AllowedDisciplines)   ?? Array.Empty<string>(),
        allowedSuitabilities = ProjectMember.ParseAllowList(p.AllowedSuitabilities) ?? Array.Empty<string>(),
        p.DefaultProjectRole,
        p.DefaultIso19650Role,
        p.CreatedAt,
        p.CreatedBy,
    };
}

public record AccessProfileBody(
    string?  Name,
    string?  Description,
    string[]? AllowedCdeStates,
    string[]? AllowedDisciplines,
    string[]? AllowedSuitabilities,
    string?  DefaultProjectRole,
    string?  DefaultIso19650Role);
