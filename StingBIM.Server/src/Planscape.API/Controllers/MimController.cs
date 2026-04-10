using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;
using Planscape.MIM.Entities;

namespace Planscape.API.Controllers;

/// <summary>
/// StingMIM — Model Information Management: asset lifecycle, maintenance scheduling.
/// Only available when tenant has MIM addon enabled.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/mim")]
[Authorize]
public class MimController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public MimController(PlanscapeDbContext db) => _db = db;

    // ── Assets ──

    [HttpGet("assets")]
    public async Task<ActionResult> GetAssets(Guid projectId,
        [FromQuery] string? discipline = null, [FromQuery] string? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        if (!await IsMimEnabledAsync()) return Forbid("StingMIM addon not enabled for this organization");

        if (!await VerifyProjectAccessAsync(projectId)) return NotFound("Project not found");

        var query = _db.Assets.Where(a => a.ProjectId == projectId);
        if (!string.IsNullOrEmpty(discipline)) query = query.Where(a => a.Discipline == discipline);
        if (!string.IsNullOrEmpty(status)) query = query.Where(a => a.LifecycleStatus == status);

        var total = await query.CountAsync();
        var assets = await query
            .OrderBy(a => a.AssetTag)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return Ok(new { assets, total, page, pageSize });
    }

    [HttpPost("assets")]
    public async Task<ActionResult> CreateAsset(Guid projectId, [FromBody] CreateAssetRequest req)
    {
        if (!await IsMimEnabledAsync()) return Forbid("StingMIM addon not enabled");

        var asset = new Asset
        {
            ProjectId = projectId,
            AssetTag = req.AssetTag,
            AssetName = req.AssetName,
            CategoryName = req.CategoryName ?? "",
            FamilyName = req.FamilyName ?? "",
            Discipline = req.Discipline ?? "",
            SystemCode = req.SystemCode ?? "",
            FunctionCode = req.FunctionCode ?? "",
            ProductCode = req.ProductCode ?? "",
            Location = req.Location ?? "",
            Zone = req.Zone ?? "",
            Level = req.Level ?? "",
            Room = req.Room ?? "",
            LifecycleStatus = req.Status ?? "OPERATIONAL",
            Manufacturer = req.Manufacturer,
            ModelNumber = req.ModelNumber,
            SerialNumber = req.SerialNumber,
            WarrantyEnd = req.WarrantyExpiresAt,
            InstallationDate = req.InstallationDate,
            ExpectedLifeYears = req.ExpectedLifeYears,
            ConditionScore = req.ConditionScore.HasValue ? (double?)req.ConditionScore.Value : null,
            CriticalityRating = req.CriticalityRating
        };

        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAssets), new { projectId }, asset);
    }

    [HttpPost("assets/bulk")]
    public async Task<ActionResult> BulkCreateAssets(Guid projectId, [FromBody] List<CreateAssetRequest> assets)
    {
        if (!await IsMimEnabledAsync()) return Forbid("StingMIM addon not enabled");
        if (assets.Count > 10000) return BadRequest("Maximum 10,000 assets per bulk operation");

        int created = 0;
        foreach (var req in assets)
        {
            var existing = await _db.Assets
                .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.AssetTag == req.AssetTag);
            if (existing != null) continue; // Skip duplicates

            _db.Assets.Add(new Asset
            {
                ProjectId = projectId, AssetTag = req.AssetTag, AssetName = req.AssetName,
                CategoryName = req.CategoryName ?? "", FamilyName = req.FamilyName ?? "",
                Discipline = req.Discipline ?? "", SystemCode = req.SystemCode ?? "",
                FunctionCode = req.FunctionCode ?? "", ProductCode = req.ProductCode ?? "",
                Location = req.Location ?? "", Zone = req.Zone ?? "", Level = req.Level ?? "",
                LifecycleStatus = req.Status ?? "OPERATIONAL"
            });
            created++;
        }

        await _db.SaveChangesAsync();
        return Ok(new { created, skipped = assets.Count - created });
    }

    [HttpGet("assets/{assetId}")]
    public async Task<ActionResult> GetAsset(Guid projectId, Guid assetId)
    {
        if (!await IsMimEnabledAsync()) return Forbid("StingMIM addon not enabled");

        var asset = await _db.Assets
            .Include(a => a.MaintenanceTasks)
            .FirstOrDefaultAsync(a => a.Id == assetId && a.ProjectId == projectId);
        if (asset == null) return NotFound();
        return Ok(asset);
    }

    // ── Maintenance Tasks ──

    [HttpGet("maintenance")]
    public async Task<ActionResult> GetMaintenanceTasks(Guid projectId,
        [FromQuery] string? status = null, [FromQuery] bool? overdue = null)
    {
        if (!await IsMimEnabledAsync()) return Forbid("StingMIM addon not enabled");

        var query = _db.MaintenanceTasks
            .Where(m => m.Asset!.ProjectId == projectId);

        if (!string.IsNullOrEmpty(status)) query = query.Where(m => m.Status == status);
        if (overdue == true) query = query.Where(m => m.NextDueDate < DateTime.UtcNow && m.Status != "COMPLETED");

        var tasks = await query
            .OrderBy(m => m.NextDueDate)
            .Select(m => new
            {
                m.Id, m.Description, m.Type, m.Priority, m.Status, DueDate = m.NextDueDate,
                Assignee = m.AssignedTo, m.FrequencyDays, LastCompletedAt = m.CompletedDate,
                AssetTag = m.Asset!.AssetTag, AssetName = m.Asset.AssetName,
                IsOverdue = m.NextDueDate < DateTime.UtcNow && m.Status != "COMPLETED"
            })
            .ToListAsync();

        return Ok(tasks);
    }

    [HttpPost("maintenance")]
    public async Task<ActionResult> CreateMaintenanceTask(Guid projectId, [FromBody] CreateMaintenanceRequest req)
    {
        if (!await IsMimEnabledAsync()) return Forbid("StingMIM addon not enabled");

        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == req.AssetId && a.ProjectId == projectId);
        if (asset == null) return NotFound("Asset not found");

        var task = new MaintenanceTask
        {
            AssetId = req.AssetId,
            Description = req.Description,
            Priority = req.Priority ?? "MEDIUM",
            Status = "SCHEDULED",
            ScheduledDate = req.DueDate,
            NextDueDate = req.DueDate,
            AssignedTo = req.Assignee,
            FrequencyDays = req.FrequencyDays ?? 365,
            StandardReference = req.StandardReference
        };

        _db.MaintenanceTasks.Add(task);
        await _db.SaveChangesAsync();
        return Ok(task);
    }

    // ── Analytics ──

    [HttpGet("dashboard")]
    public async Task<ActionResult> GetMimDashboard(Guid projectId)
    {
        if (!await IsMimEnabledAsync()) return Forbid("StingMIM addon not enabled");

        var assets = await _db.Assets.Where(a => a.ProjectId == projectId).ToListAsync();
        var tasks = await _db.MaintenanceTasks
            .Where(m => m.Asset!.ProjectId == projectId).ToListAsync();

        var overdueCount = tasks.Count(t => t.NextDueDate < DateTime.UtcNow && t.Status != "COMPLETED");
        var avgCondition = assets.Where(a => a.ConditionScore > 0).Select(a => a.ConditionScore ?? 0.0).DefaultIfEmpty(0.0).Average();

        var byDiscipline = assets.GroupBy(a => a.Discipline)
            .ToDictionary(g => g.Key, g => g.Count());
        var byStatus = assets.GroupBy(a => a.LifecycleStatus)
            .ToDictionary(g => g.Key, g => g.Count());
        var warrantyExpiring = assets.Count(a => a.WarrantyEnd.HasValue
            && a.WarrantyEnd.Value < DateTime.UtcNow.AddMonths(3)
            && a.WarrantyEnd.Value > DateTime.UtcNow);

        return Ok(new
        {
            totalAssets = assets.Count,
            avgConditionScore = Math.Round(avgCondition, 1),
            totalTasks = tasks.Count,
            overdueTasks = overdueCount,
            warrantyExpiringSoon = warrantyExpiring,
            byDiscipline, byStatus
        });
    }

    private async Task<bool> IsMimEnabledAsync()
    {
        var tenant = await _db.Tenants.FindAsync(GetTenantId());
        return tenant?.MimEnabled ?? false;
    }

    private async Task<bool> VerifyProjectAccessAsync(Guid projectId)
    {
        return await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == GetTenantId());
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record CreateAssetRequest(string AssetTag, string AssetName, string? CategoryName, string? FamilyName,
    string? Discipline, string? SystemCode, string? FunctionCode, string? ProductCode,
    string? Location, string? Zone, string? Level, string? Room, string? Status,
    string? Manufacturer, string? ModelNumber, string? SerialNumber,
    DateTime? WarrantyExpiresAt, DateTime? InstallationDate,
    int? ExpectedLifeYears, int? ConditionScore, string? CriticalityRating);

public record CreateMaintenanceRequest(Guid AssetId, string Description, string? TaskType, string? Priority,
    DateTime DueDate, string? Assignee, int? FrequencyDays, string? StandardReference);
