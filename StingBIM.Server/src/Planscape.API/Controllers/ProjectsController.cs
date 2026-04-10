using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
/// <summary>
/// BIM project management — CRUD, settings, and dashboard.
/// </summary>
public class ProjectsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public ProjectsController(PlanscapeDbContext db) => _db = db;

    /// <summary>List all active projects for the current tenant.</summary>
    /// <response code="200">Array of project summaries ordered by last sync date.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
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

    /// <summary>Get a single project by ID (includes full settings).</summary>
    /// <response code="200">Project detail object.</response>
    /// <response code="404">Project not found or does not belong to tenant.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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

    /// <summary>Create a new BIM project (subject to tenant project limit).</summary>
    /// <response code="201">Project created.</response>
    /// <response code="400">Tenant project limit reached.</response>
    /// <response code="404">Tenant not found.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, project);
    }

    /// <summary>Update project settings — name, phase, tag format, config JSON.</summary>
    /// <response code="200">Updated project object.</response>
    /// <response code="404">Project not found or does not belong to tenant.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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

    /// <summary>Get the project dashboard — compliance, issues, documents, and recent workflows.</summary>
    /// <response code="200">Dashboard data object.</response>
    /// <response code="404">Project not found or does not belong to tenant.</response>
    [HttpGet("{id}/dashboard")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
