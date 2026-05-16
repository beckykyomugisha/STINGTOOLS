using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Executive dashboard: daily KPI snapshots, coordinator workload
/// heatmap, and per-user pinned widget configuration.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public DashboardController(PlanscapeDbContext db) => _db = db;

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirst("tenantId")?.Value
            ?? throw new InvalidOperationException("tenantId claim missing"));

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("userId claim missing"));

    // ── KPI Snapshots ─────────────────────────────────────────────────────

    [HttpGet("kpi")]
    public async Task<ActionResult> GetKpiHistory(Guid projectId,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] int days = 30)
    {
        var tenantId = GetTenantId();
        var cutoff   = from ?? DateTime.UtcNow.AddDays(-days);
        var end      = to ?? DateTime.UtcNow;

        var snapshots = await _db.KpiSnapshots
            .Where(s => s.ProjectId == projectId && s.TenantId == tenantId
                     && s.SnapshotDate >= cutoff && s.SnapshotDate <= end)
            .OrderBy(s => s.SnapshotDate)
            .ToListAsync();
        return Ok(snapshots);
    }

    [HttpGet("kpi/latest")]
    public async Task<ActionResult> GetLatestKpi(Guid projectId)
    {
        var tenantId = GetTenantId();
        var snapshot = await _db.KpiSnapshots
            .Where(s => s.ProjectId == projectId && s.TenantId == tenantId)
            .OrderByDescending(s => s.SnapshotDate)
            .FirstOrDefaultAsync();
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    // ── Live KPI (computed on demand) ─────────────────────────────────────

    [HttpGet("kpi/live")]
    public async Task<ActionResult> GetLiveKpi(Guid projectId)
    {
        var tenantId = GetTenantId();

        var issuesOpen     = await _db.Issues.CountAsync(i => i.ProjectId == projectId
                                && i.Project!.TenantId == tenantId && i.Status != "Closed");
        var issuesOverdue  = await _db.Issues.CountAsync(i => i.ProjectId == projectId
                                && i.Project!.TenantId == tenantId && i.Status != "Closed"
                                && i.DueDate.HasValue && i.DueDate < DateTime.UtcNow);
        var clashesOpen    = await _db.ClashRecords.CountAsync(c => c.ProjectId == projectId
                                && c.TenantId == tenantId && c.Status == "Open");
        var clashCritical  = await _db.ClashRecords.CountAsync(c => c.ProjectId == projectId
                                && c.TenantId == tenantId && c.Status == "Open"
                                && (int)c.Severity >= 2);
        var docsTotal      = await _db.Documents.CountAsync(d => d.ProjectId == projectId
                                && d.Project!.TenantId == tenantId);
        var docsPublished  = await _db.Documents.CountAsync(d => d.ProjectId == projectId
                                && d.Project!.TenantId == tenantId && d.CdeStatus == "PUBLISHED");
        var modelFindings  = await _db.ModelCheckResults.CountAsync(r => r.ProjectId == projectId
                                && r.TenantId == tenantId && r.Status == "Open");

        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var issuesThisWeek = await _db.Issues.CountAsync(i => i.ProjectId == projectId
                                && i.Project!.TenantId == tenantId && i.CreatedAt >= weekAgo);

        return Ok(new
        {
            IssuesOpen        = issuesOpen,
            IssuesOverdue     = issuesOverdue,
            IssuesCreatedThisWeek = issuesThisWeek,
            ClashesOpen       = clashesOpen,
            ClashesCritical   = clashCritical,
            DocumentsTotal    = docsTotal,
            DocumentsPublished = docsPublished,
            ModelCheckFindingsOpen = modelFindings,
            ComputedAt        = DateTime.UtcNow,
        });
    }

    // ── Coordinator Workload ──────────────────────────────────────────────

    [HttpGet("workload")]
    public async Task<ActionResult> GetWorkloads(Guid projectId,
        [FromQuery] DateTime? weekOf = null)
    {
        var tenantId = GetTenantId();
        // If weekOf not specified, return the latest available week
        IQueryable<CoordinatorWorkload> query;
        if (weekOf.HasValue)
        {
            // Round to Monday
            var monday = weekOf.Value.AddDays(-(int)weekOf.Value.DayOfWeek + 1);
            query = _db.CoordinatorWorkloads
                .Where(w => w.TenantId == tenantId && w.WeekStarting == monday);
        }
        else
        {
            var latestWeek = await _db.CoordinatorWorkloads
                .Where(w => w.TenantId == tenantId)
                .MaxAsync(w => (DateTime?)w.WeekStarting);
            if (!latestWeek.HasValue) return Ok(Array.Empty<object>());
            query = _db.CoordinatorWorkloads
                .Where(w => w.TenantId == tenantId && w.WeekStarting == latestWeek);
        }

        var workloads = await query
            .OrderByDescending(w => w.WorkloadIndex)
            .Select(w => new
            {
                w.Id, w.UserId, w.WeekStarting,
                w.OpenIssuesAssigned, w.OpenIssuesCritical, w.OpenIssuesMajor,
                w.OpenIssuesOverdue, w.IssuesResolvedThisWeek, w.IssuesCreatedThisWeek,
                w.OpenClashesAssigned, w.OpenModelCheckFindings,
                w.PendingApprovalsCount, w.WorkloadIndex, w.LoadBand
            })
            .ToListAsync();
        return Ok(workloads);
    }

    // ── Widget Configuration ──────────────────────────────────────────────

    [HttpGet("widgets")]
    public async Task<ActionResult> GetWidgets(Guid projectId)
    {
        var tenantId = GetTenantId();
        var userId   = GetUserId();

        var widgets = await _db.DashboardWidgets
            .Where(w => w.TenantId == tenantId
                     && (w.ProjectId == null || w.ProjectId == projectId)
                     && (w.UserId == null || w.UserId == userId)
                     && w.Pinned)
            .OrderBy(w => w.SortOrder)
            .ToListAsync();
        return Ok(widgets);
    }

    [HttpPost("widgets")]
    public async Task<ActionResult> CreateWidget(Guid projectId, [FromBody] CreateWidgetRequest req)
    {
        var tenantId = GetTenantId();
        var userId   = GetUserId();

        var widget = new DashboardWidget
        {
            TenantId   = tenantId,
            UserId     = req.UserScoped ? userId : null,
            ProjectId  = req.ProjectId ?? projectId,
            Kind       = req.Kind,
            Title      = req.Title ?? req.Kind,
            ConfigJson = req.ConfigJson ?? "{}",
            SortOrder  = req.SortOrder,
            GridCol    = req.GridCol,
            GridRow    = req.GridRow,
            GridWidth  = req.GridWidth,
            GridHeight = req.GridHeight,
            Pinned     = true,
        };
        _db.DashboardWidgets.Add(widget);
        await _db.SaveChangesAsync();
        return Ok(widget);
    }

    [HttpPut("widgets/{id}")]
    public async Task<ActionResult> UpdateWidget(Guid projectId, Guid id, [FromBody] UpdateWidgetRequest req)
    {
        var tenantId = GetTenantId();
        var userId   = GetUserId();

        var widget = await _db.DashboardWidgets
            .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId
                                   && (w.UserId == null || w.UserId == userId));
        if (widget is null) return NotFound();

        widget.Title      = req.Title      ?? widget.Title;
        widget.ConfigJson = req.ConfigJson ?? widget.ConfigJson;
        widget.SortOrder  = req.SortOrder  ?? widget.SortOrder;
        widget.GridCol    = req.GridCol    ?? widget.GridCol;
        widget.GridRow    = req.GridRow    ?? widget.GridRow;
        widget.GridWidth  = req.GridWidth  ?? widget.GridWidth;
        widget.GridHeight = req.GridHeight ?? widget.GridHeight;
        widget.Pinned     = req.Pinned     ?? widget.Pinned;
        widget.UpdatedAt  = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(widget);
    }

    [HttpDelete("widgets/{id}")]
    public async Task<ActionResult> DeleteWidget(Guid projectId, Guid id)
    {
        var tenantId = GetTenantId();
        var userId   = GetUserId();
        var widget   = await _db.DashboardWidgets
            .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId
                                   && (w.UserId == null || w.UserId == userId));
        if (widget is null) return NotFound();
        _db.DashboardWidgets.Remove(widget);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Cross-project board view ──────────────────────────────────────────

    [HttpGet("/api/dashboard/board")]
    [Authorize(Roles = "TenantAdmin,ProjectManager,BimManager")]
    public async Task<ActionResult> GetBoardView()
    {
        var tenantId = GetTenantId();
        var latest = await _db.KpiSnapshots
            .Where(s => s.TenantId == tenantId)
            .GroupBy(s => s.ProjectId)
            .Select(g => g.OrderByDescending(s => s.SnapshotDate).First())
            .ToListAsync();
        return Ok(latest);
    }
}

public record CreateWidgetRequest(
    string Kind,
    string? Title,
    string? ConfigJson,
    Guid? ProjectId,
    int SortOrder,
    int? GridCol,
    int? GridRow,
    int? GridWidth,
    int? GridHeight,
    bool UserScoped);

public record UpdateWidgetRequest(
    string? Title,
    string? ConfigJson,
    int? SortOrder,
    int? GridCol,
    int? GridRow,
    int? GridWidth,
    int? GridHeight,
    bool? Pinned);
