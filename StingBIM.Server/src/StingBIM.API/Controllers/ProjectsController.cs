using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StingBIM.Core.Entities;
using StingBIM.Infrastructure.Data;

namespace StingBIM.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly StingBimDbContext _db;

    public ProjectsController(StingBimDbContext db) => _db = db;

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

    [HttpPost]
    public async Task<ActionResult> CreateProject([FromBody] CreateProjectRequest req)
    {
        var tenantId = GetTenantId();
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null) return NotFound("Tenant not found");

        var projectCount = await _db.Projects.CountAsync(p => p.TenantId == tenantId);
        if (projectCount >= tenant.MaxProjects)
            return BadRequest($"Project limit ({tenant.MaxProjects}) reached for {tenant.Tier} tier");

        var project = new Project
        {
            TenantId = tenantId,
            Name = req.Name,
            Code = req.Code,
            Description = req.Description,
            Phase = req.Phase ?? "Design"
        };
        _db.Projects.Add(project);

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = project.Id,
            UserId = userId,
            Action = "project_created",
            EntityType = "Project",
            EntityId = project.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { project.Name, project.Code, project.Phase }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetProjects), new { id = project.Id }, project);
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
