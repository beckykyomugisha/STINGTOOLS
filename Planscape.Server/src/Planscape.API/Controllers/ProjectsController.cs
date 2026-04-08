using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public ProjectsController(PlanscapeDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult> GetProjects()
    {
        var tenantId = GetTenantId();
        var projects = await _db.Projects
            .Where(p => p.TenantId == tenantId && p.Status == ProjectStatus.Active)
            .Select(p => new
            {
                p.Id, p.Name, p.Code, p.Phase, p.Status,
                p.CompliancePercent, p.RagStatus, p.TotalElements, p.TaggedElements,
                p.LastSyncAt, p.CreatedAt
            })
            .OrderByDescending(p => p.LastSyncAt)
            .ToListAsync();

        return Ok(projects);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetProject(Guid id)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (project == null) return NotFound();

        return Ok(new
        {
            project.Id, project.Name, project.Code, project.Description, project.Phase,
            project.Status, project.TagSeparator, project.SeqNumPad,
            project.TagPrefix, project.TagSuffix, project.ConfigJson,
            project.CompliancePercent, project.ContainerCompliancePercent,
            project.RagStatus, project.TotalElements, project.TaggedElements,
            project.WarningCount, project.LastSyncAt, project.CreatedAt
        });
    }

    [HttpPost]
    public async Task<ActionResult> CreateProject([FromBody] CreateProjectRequest req)
    {
        var tenantId = GetTenantId();
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null) return NotFound("Tenant not found");

        var projectCount = await _db.Projects.CountAsync(p => p.TenantId == tenantId);
        int tierLimit = TierLimits.MaxProjects(tenant.Tier);
        if (!TierLimits.BelowLimit(projectCount, tierLimit, tenant.MaxProjects))
            return BadRequest(new
            {
                message = $"Project limit ({TierLimits.LimitLabel(tierLimit, tenant.MaxProjects)}) reached for {tenant.Tier} tier. Upgrade to Professional or higher for unlimited projects.",
                currentCount = projectCount,
                tier = tenant.Tier.ToString()
            });

        var project = new Project
        {
            TenantId = tenantId,
            Name = req.Name,
            Code = req.Code,
            Description = req.Description,
            Phase = req.Phase ?? "Design"
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, project);
    }

    /// <summary>
    /// Update project settings — name, phase, tag format, config JSON.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateProject(Guid id, [FromBody] UpdateProjectRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (project == null) return NotFound();

        if (req.Name != null) project.Name = req.Name;
        if (req.Description != null) project.Description = req.Description;
        if (req.Phase != null) project.Phase = req.Phase;
        if (req.Status.HasValue) project.Status = req.Status.Value;
        if (req.TagSeparator != null) project.TagSeparator = req.TagSeparator;
        if (req.SeqNumPad.HasValue) project.SeqNumPad = req.SeqNumPad.Value;
        if (req.TagPrefix != null) project.TagPrefix = req.TagPrefix;
        if (req.TagSuffix != null) project.TagSuffix = req.TagSuffix;
        if (req.ConfigJson != null) project.ConfigJson = req.ConfigJson;

        await _db.SaveChangesAsync();
        return Ok(project);
    }

    [HttpPut("{id}/settings")]
    public async Task<ActionResult> UpdateSettings(Guid id, [FromBody] ProjectSettingsRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (project == null) return NotFound();

        if (req.Name        != null) project.Name        = req.Name;
        if (req.Description != null) project.Description = req.Description;
        if (req.Phase       != null) project.Phase       = req.Phase;
        if (req.ConfigJson  != null) project.ConfigJson  = req.ConfigJson;
        if (req.RagGreenThreshold.HasValue || req.RagAmberThreshold.HasValue)
        {
            // Store in ConfigJson as JSON overrides
            var dict = new Dictionary<string, object>();
            if (req.RagGreenThreshold.HasValue) dict["ragGreenThreshold"] = req.RagGreenThreshold.Value;
            if (req.RagAmberThreshold.HasValue) dict["ragAmberThreshold"] = req.RagAmberThreshold.Value;
            if (req.TagSeparator      != null)  dict["tagSeparator"]      = req.TagSeparator;
            project.ConfigJson = System.Text.Json.JsonSerializer.Serialize(dict);
        }

        await _db.SaveChangesAsync();
        return Ok(new { project.Id, project.Name, project.Phase, project.ConfigJson });
    }

    [HttpGet("{id}/stats")]
    public async Task<ActionResult> GetStats(Guid id)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (project == null) return NotFound();

        var elementCount   = await _db.TaggedElements.CountAsync(e => e.ProjectId == id);
        var issueCount     = await _db.Issues.CountAsync(i => i.ProjectId == id && i.Status != "CLOSED");
        var overdueIssues  = await _db.Issues.CountAsync(i => i.ProjectId == id && i.Status != "CLOSED"
                                  && i.DueDate.HasValue && i.DueDate < DateTime.UtcNow);
        var meetingCount   = await _db.Meetings.CountAsync(m => m.ProjectId == id);
        var openActions    = await _db.MeetingActionItems
                                  .CountAsync(a => a.Meeting!.ProjectId == id && a.Status != "COMPLETE");
        var memberCount    = await _db.ProjectMembers.CountAsync(m => m.ProjectId == id && m.IsActive);
        var docCount       = await _db.Documents.CountAsync(d => d.ProjectId == id);
        var lastSnapshot   = await _db.ComplianceSnapshots
                                  .Where(s => s.ProjectId == id)
                                  .OrderByDescending(s => s.CapturedAt)
                                  .FirstOrDefaultAsync();

        return Ok(new
        {
            project.Id, project.Name, project.Code, project.Phase, project.Status,
            project.CompliancePercent, project.RagStatus, project.LastSyncAt,
            elementCount, issueCount, overdueIssues, meetingCount, openActions, memberCount, docCount,
            lastSnapshot = lastSnapshot?.CapturedAt,
            lastCompliancePct = lastSnapshot?.TagPercent
        });
    }

    [HttpGet("{id}/dashboard")]
    public async Task<ActionResult> GetDashboard(Guid id)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (project == null) return NotFound();

        var issueCount = await _db.Issues.CountAsync(i => i.ProjectId == id && i.Status != "CLOSED");
        var docCount = await _db.Documents.CountAsync(d => d.ProjectId == id);
        var workflowRuns = await _db.WorkflowRuns
            .Where(w => w.ProjectId == id)
            .OrderByDescending(w => w.ExecutedAt)
            .Take(10)
            .ToListAsync();

        return Ok(new
        {
            project.Id, project.Name, project.Code, project.Phase,
            project.CompliancePercent, project.ContainerCompliancePercent,
            project.RagStatus, project.TotalElements, project.TaggedElements,
            project.WarningCount, project.LastSyncAt,
            OpenIssues = issueCount,
            Documents = docCount,
            RecentWorkflows = workflowRuns
        });
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record CreateProjectRequest(string Name, string Code, string? Description, string? Phase);
public record UpdateProjectRequest(
    string? Name, string? Description, string? Phase, ProjectStatus? Status,
    string? TagSeparator, int? SeqNumPad, string? TagPrefix, string? TagSuffix,
    string? ConfigJson);
