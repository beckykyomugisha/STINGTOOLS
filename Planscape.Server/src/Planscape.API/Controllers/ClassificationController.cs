using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Classification system (NRM2, Uniclass 2015, MasterFormat) browser
/// and takeoff rule authoring.
/// </summary>
[ApiController]
[Route("api/classification")]
[Authorize]
public class ClassificationController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public ClassificationController(PlanscapeDbContext db) => _db = db;

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirst("tenantId")?.Value
            ?? throw new InvalidOperationException("tenantId claim missing"));

    // ── Classification Systems ────────────────────────────────────────────

    [HttpGet("systems")]
    public async Task<ActionResult> GetSystems()
    {
        var tenantId = GetTenantId();
        var systems = await _db.ClassificationSystems
            .Where(s => s.TenantId == null || s.TenantId == tenantId)
            .Where(s => s.IsActive)
            .OrderBy(s => s.Code)
            .Select(s => new { s.Id, s.Code, s.Name, s.Authority, s.Edition, s.MeasurementProtocol, s.TenantId })
            .ToListAsync();
        return Ok(systems);
    }

    [HttpPost("systems")]
    public async Task<ActionResult> CreateSystem([FromBody] CreateClassificationSystemRequest req)
    {
        var tenantId = GetTenantId();
        var exists = await _db.ClassificationSystems
            .AnyAsync(s => s.TenantId == tenantId && s.Code == req.Code);
        if (exists) return Conflict($"System code '{req.Code}' already exists.");

        var system = new ClassificationSystem
        {
            TenantId            = tenantId,
            Code                = req.Code,
            Name                = req.Name,
            Authority           = req.Authority ?? "",
            Edition             = req.Edition ?? "",
            MeasurementProtocol = req.MeasurementProtocol ?? "",
        };
        _db.ClassificationSystems.Add(system);
        await _db.SaveChangesAsync();
        return Ok(system);
    }

    // ── Classification Codes ──────────────────────────────────────────────

    [HttpGet("systems/{systemId}/codes")]
    public async Task<ActionResult> GetCodes(Guid systemId,
        [FromQuery] string? parentCode = null,
        [FromQuery] string? search = null,
        [FromQuery] bool leafOnly = false)
    {
        var query = _db.ClassificationCodes.Where(c => c.SystemId == systemId);

        if (!string.IsNullOrEmpty(parentCode))
        {
            var parent = await _db.ClassificationCodes
                .FirstOrDefaultAsync(c => c.SystemId == systemId && c.Code == parentCode);
            if (parent is null) return NotFound();
            query = query.Where(c => c.Path.StartsWith(parent.Path + ".") || c.ParentCodeId == parent.Id);
        }

        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => c.Code.Contains(search) || c.Title.Contains(search));

        if (leafOnly)
            query = query.Where(c => c.IsLeaf);

        var codes = await query
            .OrderBy(c => c.Path)
            .Take(500)
            .Select(c => new
            {
                c.Id, c.Code, c.Title, c.Description, c.Path, c.Level,
                c.DefaultUnit, c.IsLeaf, c.ParentCodeId, c.SortOrder
            })
            .ToListAsync();
        return Ok(codes);
    }

    [HttpPost("systems/{systemId}/codes")]
    public async Task<ActionResult> CreateCode(Guid systemId, [FromBody] CreateClassificationCodeRequest req)
    {
        var exists = await _db.ClassificationCodes
            .AnyAsync(c => c.SystemId == systemId && c.Code == req.Code);
        if (exists) return Conflict($"Code '{req.Code}' already exists in this system.");

        string path = req.Code;
        int level = 0;
        if (req.ParentCodeId.HasValue)
        {
            var parent = await _db.ClassificationCodes
                .FirstOrDefaultAsync(c => c.Id == req.ParentCodeId && c.SystemId == systemId);
            if (parent is null) return BadRequest("Parent code not found.");
            path = parent.Path + "." + req.Code;
            level = parent.Level + 1;
            parent.IsLeaf = false;
        }

        var code = new ClassificationCode
        {
            SystemId     = systemId,
            ParentCodeId = req.ParentCodeId,
            Code         = req.Code,
            Title        = req.Title,
            Description  = req.Description,
            Path         = path,
            Level        = level,
            DefaultUnit  = req.DefaultUnit ?? "m2",
            IsLeaf       = true,
            SortOrder    = req.SortOrder,
        };
        _db.ClassificationCodes.Add(code);
        await _db.SaveChangesAsync();
        return Ok(code);
    }

    // ── Takeoff Rules ─────────────────────────────────────────────────────

    [HttpGet("takeoff-rules")]
    public async Task<ActionResult> GetTakeoffRules([FromQuery] Guid? projectId = null)
    {
        var tenantId = GetTenantId();
        var query = _db.TakeoffRules
            .Where(r => r.TenantId == tenantId)
            .Where(r => projectId == null || r.ProjectId == null || r.ProjectId == projectId);

        var rules = await query
            .OrderBy(r => r.Priority)
            .Select(r => new
            {
                r.Id, r.ClassificationCodeId, r.ProjectId,
                r.IfcType, r.Discipline, r.CategoryPattern, r.MaterialPattern,
                r.PropertyFiltersJson, r.Unit, r.QuantityFormula,
                r.DescriptionTemplate, r.SpecificationGrade,
                r.DeemedIncludedJson, r.WastePercent, r.Priority, r.Enabled,
                r.CreatedAt, r.CreatedBy
            })
            .ToListAsync();
        return Ok(rules);
    }

    [HttpPost("takeoff-rules")]
    public async Task<ActionResult> CreateTakeoffRule([FromBody] CreateTakeoffRuleRequest req)
    {
        var tenantId = GetTenantId();
        var rule = new TakeoffRule
        {
            TenantId            = tenantId,
            ClassificationCodeId = req.ClassificationCodeId,
            ProjectId           = req.ProjectId,
            IfcType             = req.IfcType ?? "*",
            Discipline          = req.Discipline ?? "*",
            CategoryPattern     = req.CategoryPattern ?? "*",
            MaterialPattern     = req.MaterialPattern ?? "*",
            PropertyFiltersJson = req.PropertyFiltersJson ?? "{}",
            Unit                = req.Unit ?? "m2",
            QuantityFormula     = req.QuantityFormula ?? "",
            DescriptionTemplate = req.DescriptionTemplate ?? "",
            SpecificationGrade  = req.SpecificationGrade ?? "",
            DeemedIncludedJson  = req.DeemedIncludedJson ?? "[]",
            WastePercent        = req.WastePercent,
            Priority            = req.Priority,
            Enabled             = true,
            CreatedBy           = User.Identity?.Name,
        };
        _db.TakeoffRules.Add(rule);
        await _db.SaveChangesAsync();
        return Ok(rule);
    }

    [HttpPut("takeoff-rules/{id}")]
    public async Task<ActionResult> UpdateTakeoffRule(Guid id, [FromBody] CreateTakeoffRuleRequest req)
    {
        var tenantId = GetTenantId();
        var rule = await _db.TakeoffRules.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId);
        if (rule is null) return NotFound();

        rule.IfcType             = req.IfcType ?? rule.IfcType;
        rule.Discipline          = req.Discipline ?? rule.Discipline;
        rule.CategoryPattern     = req.CategoryPattern ?? rule.CategoryPattern;
        rule.MaterialPattern     = req.MaterialPattern ?? rule.MaterialPattern;
        rule.PropertyFiltersJson = req.PropertyFiltersJson ?? rule.PropertyFiltersJson;
        rule.Unit                = req.Unit ?? rule.Unit;
        rule.QuantityFormula     = req.QuantityFormula ?? rule.QuantityFormula;
        rule.DescriptionTemplate = req.DescriptionTemplate ?? rule.DescriptionTemplate;
        rule.SpecificationGrade  = req.SpecificationGrade ?? rule.SpecificationGrade;
        rule.WastePercent        = req.WastePercent;
        rule.Priority            = req.Priority;
        rule.UpdatedAt           = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(rule);
    }

    [HttpDelete("takeoff-rules/{id}")]
    public async Task<ActionResult> DeleteTakeoffRule(Guid id)
    {
        var tenantId = GetTenantId();
        var rule = await _db.TakeoffRules.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId);
        if (rule is null) return NotFound();
        _db.TakeoffRules.Remove(rule);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateClassificationSystemRequest(
    string Code,
    string Name,
    string? Authority,
    string? Edition,
    string? MeasurementProtocol);

public record CreateClassificationCodeRequest(
    string Code,
    string Title,
    string? Description,
    Guid? ParentCodeId,
    string? DefaultUnit,
    int SortOrder);

public record CreateTakeoffRuleRequest(
    Guid ClassificationCodeId,
    Guid? ProjectId,
    string? IfcType,
    string? Discipline,
    string? CategoryPattern,
    string? MaterialPattern,
    string? PropertyFiltersJson,
    string? Unit,
    string? QuantityFormula,
    string? DescriptionTemplate,
    string? SpecificationGrade,
    string? DeemedIncludedJson,
    decimal WastePercent,
    int Priority);
