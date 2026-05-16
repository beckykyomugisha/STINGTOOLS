using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/projects/{projectId}/boq")]
[Authorize]
public class BoqController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<BoqController> _logger;

    public BoqController(PlanscapeDbContext db, ILogger<BoqController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirst("tenantId")?.Value
            ?? throw new InvalidOperationException("tenantId claim missing"));

    // ── BOQ Documents ─────────────────────────────────────────────────────

    [HttpGet("documents")]
    public async Task<ActionResult> GetDocuments(Guid projectId)
    {
        var tenantId = GetTenantId();
        var docs = await _db.BoqDocuments
            .Where(d => d.ProjectId == projectId && d.TenantId == tenantId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                d.Id, d.Name, d.ClientName, d.Architect, d.ContractForm,
                d.PricingBasis, d.Currency, d.Status, d.Revision,
                d.DayworkLabourPct, d.DayworkMaterialsPct, d.DayworkPlantPct,
                d.LocationFactor, d.PrimaryClassificationSystemId,
                d.SecondaryClassificationSystemId,
                d.CreatedAt, d.UpdatedAt, d.CreatedBy
            })
            .ToListAsync();
        return Ok(docs);
    }

    [HttpGet("documents/{id}")]
    public async Task<ActionResult> GetDocument(Guid projectId, Guid id)
    {
        var tenantId = GetTenantId();
        var doc = await _db.BoqDocuments
            .Where(d => d.Id == id && d.ProjectId == projectId && d.TenantId == tenantId)
            .FirstOrDefaultAsync();
        return doc is null ? NotFound() : Ok(doc);
    }

    [HttpPost("documents")]
    public async Task<ActionResult> CreateDocument(Guid projectId, [FromBody] CreateBoqDocumentRequest req)
    {
        var tenantId = GetTenantId();
        var doc = new BoqDocument
        {
            TenantId                    = tenantId,
            ProjectId                   = projectId,
            Name                        = req.Name,
            ClientName                  = req.ClientName,
            Architect                   = req.Architect ?? "",
            ContractForm                = req.ContractForm ?? "",
            InsuranceParticulars        = req.InsuranceParticulars,
            DayworkLabourPct            = req.DayworkLabourPct ?? 115m,
            DayworkMaterialsPct         = req.DayworkMaterialsPct ?? 110m,
            DayworkPlantPct             = req.DayworkPlantPct ?? 112m,
            LocationFactor              = req.LocationFactor ?? 1.000m,
            PricingBasis                = req.PricingBasis ?? "Remeasure",
            Currency                    = req.Currency ?? "GBP",
            Status                      = "Draft",
            Revision                    = "A",
            PrimaryClassificationSystemId = req.PrimaryClassificationSystemId,
            SecondaryClassificationSystemId = req.SecondaryClassificationSystemId,
            CreatedBy                   = User.Identity?.Name,
        };
        _db.BoqDocuments.Add(doc);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetDocument), new { projectId, id = doc.Id }, doc);
    }

    [HttpPut("documents/{id}")]
    public async Task<ActionResult> UpdateDocument(Guid projectId, Guid id, [FromBody] CreateBoqDocumentRequest req)
    {
        var tenantId = GetTenantId();
        var doc = await _db.BoqDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.ProjectId == projectId && d.TenantId == tenantId);
        if (doc is null) return NotFound();

        doc.Name                        = req.Name;
        doc.ClientName                  = req.ClientName ?? doc.ClientName;
        doc.Architect                   = req.Architect ?? doc.Architect;
        doc.ContractForm                = req.ContractForm ?? doc.ContractForm;
        doc.InsuranceParticulars        = req.InsuranceParticulars ?? doc.InsuranceParticulars;
        doc.DayworkLabourPct            = req.DayworkLabourPct ?? doc.DayworkLabourPct;
        doc.DayworkMaterialsPct         = req.DayworkMaterialsPct ?? doc.DayworkMaterialsPct;
        doc.DayworkPlantPct             = req.DayworkPlantPct ?? doc.DayworkPlantPct;
        doc.LocationFactor              = req.LocationFactor ?? doc.LocationFactor;
        doc.PricingBasis                = req.PricingBasis ?? doc.PricingBasis;
        doc.Currency                    = req.Currency ?? doc.Currency;
        doc.UpdatedAt                   = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(doc);
    }

    // ── Baselines ─────────────────────────────────────────────────────────

    [HttpGet("baselines")]
    public async Task<ActionResult> GetBaselines(Guid projectId)
    {
        var tenantId = GetTenantId();
        var baselines = await _db.BoqBaselines
            .Where(b => b.ProjectId == projectId && b.TenantId == tenantId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new
            {
                b.Id, b.Name, b.Kind, b.TotalValue, b.Currency,
                b.BaselinedAt, b.Checksum, b.DocumentRecordId, b.Description,
                b.CreatedAt, b.CreatedBy
            })
            .ToListAsync();
        return Ok(baselines);
    }

    [HttpPost("baselines")]
    public async Task<ActionResult> CreateBaseline(Guid projectId, [FromBody] CreateBaselineRequest req)
    {
        var tenantId = GetTenantId();

        var baseline = new BoqBaseline
        {
            TenantId      = tenantId,
            ProjectId     = projectId,
            Name          = req.Name,
            Kind          = req.Kind ?? "Tender",
            Currency      = req.Currency ?? "GBP",
            Description   = req.Description,
            CreatedBy     = User.Identity?.Name,
        };
        _db.BoqBaselines.Add(baseline);
        await _db.SaveChangesAsync();
        return Ok(baseline);
    }

    // ── Quantity Lines ────────────────────────────────────────────────────

    [HttpGet("baselines/{baselineId}/lines")]
    public async Task<ActionResult> GetQuantityLines(Guid projectId, Guid baselineId,
        [FromQuery] string? sectionCode = null, [FromQuery] Guid? workPackageId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 200)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 500);

        var tenantId = GetTenantId();
        var query = _db.QuantityLines
            .Where(l => l.BaselineId == baselineId && l.ProjectId == projectId && l.TenantId == tenantId);

        if (!string.IsNullOrEmpty(sectionCode))
            query = query.Where(l => l.SectionCode == sectionCode);
        if (workPackageId.HasValue)
            query = query.Where(l => l.WorkPackageId == workPackageId);

        var total = await query.CountAsync();
        var lines = await query
            .OrderBy(l => l.SectionCode).ThenBy(l => l.ItemDescription)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id, l.SectionCode, l.ItemDescription, l.IfcGlobalId, l.IfcType,
                l.Level, l.Zone, l.Unit, l.NetQuantity, l.WastePercent, l.Quantity,
                l.UnitRate, l.LineTotal, l.Currency, l.LineKind, l.PricingBasis,
                l.EmbodiedCarbonPerUnit, l.EmbodiedCarbonTotal,
                ClassificationCodeId = l.ClassificationCodeId,
                WorkPackageId        = l.WorkPackageId,
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items = lines });
    }

    [HttpPost("baselines/{baselineId}/lines")]
    public async Task<ActionResult> UpsertQuantityLines(Guid projectId, Guid baselineId,
        [FromBody] List<UpsertQuantityLineRequest> req)
    {
        var tenantId = GetTenantId();
        var baseline = await _db.BoqBaselines
            .FirstOrDefaultAsync(b => b.Id == baselineId && b.ProjectId == projectId && b.TenantId == tenantId);
        if (baseline is null) return NotFound();
        if (req.Count == 0) return BadRequest("No lines provided.");

        var lines = req.Select(r => new QuantityLine
        {
            TenantId             = tenantId,
            ProjectId            = projectId,
            BaselineId           = baselineId,
            ClassificationCodeId = r.ClassificationCodeId,
            TakeoffRuleId        = r.TakeoffRuleId,
            WorkPackageId        = r.WorkPackageId,
            ProjectModelId       = r.ProjectModelId,
            IfcGlobalId          = r.IfcGlobalId,
            IfcType              = r.IfcType ?? "",
            RevitElementId       = long.TryParse(r.RevitElementId, out var revitId) ? revitId : (long?)null,
            Level                = r.Level ?? "",
            Zone                 = r.Zone ?? "",
            SectionCode          = r.SectionCode ?? "",
            ItemDescription      = r.ItemDescription ?? "",
            Unit                 = r.Unit ?? "m2",
            NetQuantity          = r.NetQuantity,
            WastePercent         = (double)r.WastePercent,
            Quantity             = r.NetQuantity * (1 + r.WastePercent / 100.0),
            UnitRate             = r.UnitRate,
            LineTotal            = r.UnitRate.HasValue
                                    ? (decimal)(r.NetQuantity * (1 + r.WastePercent / 100.0)) * r.UnitRate.Value
                                    : null,
            Currency             = r.Currency ?? "GBP",
            LineKind             = r.LineKind ?? "Measured",
            PricingBasis         = r.PricingBasis ?? "Remeasure",
            EmbodiedCarbonPerUnit = r.EmbodiedCarbonPerUnit,
            EmbodiedCarbonTotal  = r.EmbodiedCarbonPerUnit.HasValue
                                    ? r.EmbodiedCarbonPerUnit * r.NetQuantity : null,
        }).ToList();

        _db.QuantityLines.AddRange(lines);

        // Update baseline total
        var existingTotal = await _db.QuantityLines
            .Where(l => l.BaselineId == baselineId && l.LineTotal.HasValue)
            .SumAsync(l => l.LineTotal!.Value);
        var newTotal = lines.Where(l => l.LineTotal.HasValue).Sum(l => l.LineTotal!.Value);
        baseline.TotalValue = existingTotal + newTotal;

        await _db.SaveChangesAsync();
        return Ok(new { created = lines.Count, baselineTotal = baseline.TotalValue });
    }

    // ── BOQ Variations ────────────────────────────────────────────────────

    [HttpGet("variations")]
    public async Task<ActionResult> GetVariations(Guid projectId)
    {
        var tenantId = GetTenantId();
        var variations = await _db.BoqVariations
            .Where(v => v.ProjectId == projectId && v.TenantId == tenantId)
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new
            {
                v.Id, v.Reference, v.Title, v.Description, v.Kind,
                v.Status, v.NetValue, v.Currency,
                v.BimIssueId, v.SubmittedAt, v.ApprovedAt, v.ApprovedBy,
                v.CreatedAt, v.CreatedBy
            })
            .ToListAsync();
        return Ok(variations);
    }

    [HttpPost("variations")]
    public async Task<ActionResult> CreateVariation(Guid projectId, [FromBody] CreateVariationRequest req)
    {
        var tenantId = GetTenantId();
        var exists = await _db.BoqVariations
            .AnyAsync(v => v.ProjectId == projectId && v.Reference == req.Reference && v.TenantId == tenantId);
        if (exists) return Conflict($"Variation reference '{req.Reference}' already exists.");

        var variation = new BoqVariation
        {
            TenantId   = tenantId,
            ProjectId  = projectId,
            BaselineId = req.BaselineId,
            Reference  = req.Reference,
            Title      = req.Title,
            Description = req.Description,
            Kind       = req.Kind ?? "VO",
            Status     = "Draft",
            NetValue   = req.NetValue,
            Currency   = req.Currency ?? "GBP",
            BimIssueId = req.BimIssueId,
            CreatedBy  = User.Identity?.Name,
        };
        _db.BoqVariations.Add(variation);
        await _db.SaveChangesAsync();
        return Ok(variation);
    }

    [HttpPut("variations/{id}/status")]
    public async Task<ActionResult> UpdateVariationStatus(Guid projectId, Guid id, [FromBody] UpdateStatusRequest req)
    {
        var tenantId = GetTenantId();
        var variation = await _db.BoqVariations
            .FirstOrDefaultAsync(v => v.Id == id && v.ProjectId == projectId && v.TenantId == tenantId);
        if (variation is null) return NotFound();

        var allowed = new[] { "Draft", "Submitted", "Reviewed", "Approved", "Rejected", "Incorporated" };
        if (!allowed.Contains(req.Status)) return BadRequest("Invalid status.");

        variation.Status    = req.Status;
        variation.UpdatedAt = DateTime.UtcNow;
        if (req.Status is "Approved" or "Rejected")
        {
            variation.ApprovedAt = DateTime.UtcNow;
            variation.ApprovedBy = User.Identity?.Name;
        }
        await _db.SaveChangesAsync();
        return Ok(variation);
    }

    // ── Work Packages ─────────────────────────────────────────────────────

    [HttpGet("work-packages")]
    public async Task<ActionResult> GetWorkPackages(Guid projectId)
    {
        var tenantId = GetTenantId();
        var wps = await _db.WorkPackages
            .Where(w => w.ProjectId == projectId && w.TenantId == tenantId)
            .OrderBy(w => w.Code)
            .ToListAsync();
        return Ok(wps);
    }

    [HttpPost("work-packages")]
    public async Task<ActionResult> CreateWorkPackage(Guid projectId, [FromBody] CreateWorkPackageRequest req)
    {
        var tenantId = GetTenantId();
        var exists = await _db.WorkPackages
            .AnyAsync(w => w.ProjectId == projectId && w.Code == req.Code && w.TenantId == tenantId);
        if (exists) return Conflict($"Work package code '{req.Code}' already exists.");

        var wp = new WorkPackage
        {
            TenantId       = tenantId,
            ProjectId      = projectId,
            Code           = req.Code,
            Name           = req.Name,
            Discipline     = req.Discipline ?? "",
            SectionPrefixesJson = req.SectionPrefixesJson ?? "[]",
            Contractor     = req.Contractor,
            EstimatedValue = req.EstimatedValue,
            Status         = "Pending",
        };
        _db.WorkPackages.Add(wp);
        await _db.SaveChangesAsync();
        return Ok(wp);
    }

    // ── Preliminaries ─────────────────────────────────────────────────────

    [HttpGet("documents/{docId}/preliminaries")]
    public async Task<ActionResult> GetPreliminaries(Guid projectId, Guid docId)
    {
        var tenantId = GetTenantId();
        var items = await _db.Nrm2PreliminariesItems
            .Where(p => p.BoqDocumentId == docId && p.ProjectId == projectId && p.TenantId == tenantId)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost("documents/{docId}/preliminaries")]
    public async Task<ActionResult> AddPreliminaryItem(Guid projectId, Guid docId, [FromBody] CreatePreliminaryRequest req)
    {
        var tenantId = GetTenantId();
        var item = new Nrm2PreliminariesItem
        {
            TenantId      = tenantId,
            ProjectId     = projectId,
            BoqDocumentId = docId,
            Code          = req.Code,
            Description   = req.Description,
            Kind          = req.Kind ?? "Fixed",
            DurationWeeks = req.DurationWeeks,
            PercentageBase = req.PercentageBase,
            SortOrder     = req.SortOrder,
        };
        _db.Nrm2PreliminariesItems.Add(item);
        await _db.SaveChangesAsync();
        return Ok(item);
    }

    // ── Summary ───────────────────────────────────────────────────────────

    [HttpGet("baselines/{baselineId}/summary")]
    public async Task<ActionResult> GetBaselineSummary(Guid projectId, Guid baselineId)
    {
        var tenantId = GetTenantId();
        var baseline = await _db.BoqBaselines
            .FirstOrDefaultAsync(b => b.Id == baselineId && b.ProjectId == projectId && b.TenantId == tenantId);
        if (baseline is null) return NotFound();

        var bySection = await _db.QuantityLines
            .Where(l => l.BaselineId == baselineId && l.TenantId == tenantId)
            .GroupBy(l => l.SectionCode)
            .Select(g => new
            {
                SectionCode = g.Key,
                ItemCount   = g.Count(),
                NetTotal    = g.Sum(l => l.LineTotal),
                EmbodiedCarbon = g.Sum(l => l.EmbodiedCarbonTotal),
            })
            .OrderBy(s => s.SectionCode)
            .ToListAsync();

        return Ok(new
        {
            baseline.Id,
            baseline.Name,
            baseline.Kind,
            baseline.TotalValue,
            baseline.Currency,
            baseline.BaselinedAt,
            sections = bySection
        });
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────

public record CreateBoqDocumentRequest(
    string Name,
    string? ClientName,
    string? Architect,
    string? ContractForm,
    string? InsuranceParticulars,
    decimal? DayworkLabourPct,
    decimal? DayworkMaterialsPct,
    decimal? DayworkPlantPct,
    decimal? LocationFactor,
    string? PricingBasis,
    string? Currency,
    Guid PrimaryClassificationSystemId,
    Guid? SecondaryClassificationSystemId);

public record CreateBaselineRequest(string Name, string? Kind, string? Currency, string? Description);

public record UpsertQuantityLineRequest(
    Guid ClassificationCodeId,
    Guid? TakeoffRuleId,
    Guid? WorkPackageId,
    Guid? ProjectModelId,
    string? IfcGlobalId,
    string? IfcType,
    string? RevitElementId,
    string? Level,
    string? Zone,
    string? SectionCode,
    string? ItemDescription,
    string? Unit,
    double NetQuantity,
    double WastePercent,
    decimal? UnitRate,
    string? Currency,
    string? LineKind,
    string? PricingBasis,
    double? EmbodiedCarbonPerUnit);

public record CreateVariationRequest(
    Guid BaselineId,
    string Reference,
    string Title,
    string? Description,
    string? Kind,
    decimal NetValue,
    string? Currency,
    Guid? BimIssueId);

public record UpdateStatusRequest(string Status);

public record CreateWorkPackageRequest(
    string Code,
    string Name,
    string? Discipline,
    string? SectionPrefixesJson,
    string? Contractor,
    decimal? EstimatedValue);

public record CreatePreliminaryRequest(
    string Code,
    string Description,
    string? Kind,
    int? DurationWeeks,
    decimal? PercentageBase,
    int SortOrder);
