using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// BIM project management — CRUD, settings, and dashboard.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public ProjectsController(PlanscapeDbContext db) => _db = db;

    /// <summary>List active and archived projects for the current tenant.</summary>
    /// <remarks>
    /// Phase 169 — extended to include location, cover image, pin flag, and
    /// member count so the dashboard can render ACC-style project cards
    /// and the Mapbox project location map. Archived projects are now
    /// returned alongside active ones (the map renders archived = green).
    /// </remarks>
    /// <response code="200">Array of project summaries ordered by last sync date.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetProjects()
    {
        var projects = await _db.Projects
            .Where(p => p.Status == ProjectStatus.Active || p.Status == ProjectStatus.Archived)
            .WhereVisibleTo(_db, User)
            .Select(p => new
            {
                p.Id, p.Name, p.Code, p.Phase, p.Status,
                p.CompliancePercent, p.RagStatus, p.TotalElements, p.TaggedElements,
                p.LastSyncAt, p.CreatedAt,
                p.Latitude, p.Longitude, p.City, p.Country,
                p.CoverImageUrl, p.IsPinned,
                MemberCount = _db.ProjectMembers
                    .Count(m => m.ProjectId == p.Id && m.IsActive)
            })
            .OrderByDescending(p => p.IsPinned)
            .ThenByDescending(p => p.LastSyncAt)
            .ToListAsync();

        return Ok(projects);
    }

    /// <summary>Toggle the pinned state of a project.</summary>
    /// <remarks>
    /// Phase 169 — used by the dashboard project cards. Pinned projects
    /// surface in a dedicated row at the top of the overview and rank
    /// first in the default sort.
    /// </remarks>
    /// <response code="204">Pin state toggled.</response>
    /// <response code="404">Project not found or does not belong to tenant.</response>
    [HttpPatch("{id}/pin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> TogglePin(Guid id)
    {
        if (!await ProjectVisibility.CanSeeProjectAsync(_db, id, User)) return NotFound();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        project.IsPinned = !project.IsPinned;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Get a single project by ID (includes full settings).</summary>
    /// <response code="200">Project detail object.</response>
    /// <response code="404">Project not found or does not belong to tenant.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetProject(Guid id)
    {
        if (!await ProjectVisibility.CanSeeProjectAsync(_db, id, User)) return NotFound();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id);
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
    [Planscape.Infrastructure.Authorization.Quota(Planscape.Infrastructure.Services.QuotaAxis.Projects)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> CreateProject([FromBody] CreateProjectRequest req)
    {
        var tenantId = GetTenantId();
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null) return NotFound("Tenant not found");
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Project name is required" });

        // D — Code is optional. An explicit code must be unique (409). An omitted code is
        // derived from the name and auto-uniquified, so a name-only create always succeeds.
        var explicitCode = !string.IsNullOrWhiteSpace(req.Code);
        var baseCode = explicitCode ? req.Code!.Trim() : DeriveProjectCode(req.Name);
        var code = baseCode;
        var dup = await _db.Projects.AnyAsync(p => p.TenantId == tenantId && p.Code == code);
        if (dup && explicitCode)
            return Conflict(new { message = $"A project with code '{code}' already exists" });
        for (int i = 2; dup && i < 1000; i++)
        {
            code = $"{baseCode}-{i}";
            dup = await _db.Projects.AnyAsync(p => p.TenantId == tenantId && p.Code == code);
        }

        var projectCount = await _db.Projects.CountAsync(p => p.TenantId == tenantId);
        if (projectCount >= tenant.MaxProjects)
            return BadRequest($"Project limit ({tenant.MaxProjects}) reached for {tenant.Tier} tier");

        var creatorId = ProjectVisibility.GetUserId(User);
        var project = new Project
        {
            TenantId = tenantId,
            Name = req.Name,
            Code = code,
            Description = req.Description,
            Phase = req.Phase ?? "Design",
            CreatedById = creatorId == Guid.Empty ? null : creatorId
        };
        _db.Projects.Add(project);

        // Phase 175 — author auto-becomes a project Manager / BIM
        // Coordinator. Without this, the author can still see the
        // project (CreatedById predicate kicks in), but they wouldn't
        // appear in the team list and downstream write-side guards
        // that only consult ProjectMember would lock them out.
        if (creatorId != Guid.Empty)
        {
            var creatorIso = User.FindFirst("iso_role")?.Value;
            if (string.IsNullOrWhiteSpace(creatorIso)) creatorIso = "BC";
            _db.ProjectMembers.Add(new ProjectMember
            {
                TenantId     = tenantId,
                ProjectId    = project.Id,
                UserId       = creatorId,
                ProjectRole  = "Manager",
                Iso19650Role = creatorIso!,
                IsActive     = true,
                JoinedAt     = DateTime.UtcNow,
                InvitedBy    = User.FindFirst("display_name")?.Value
            });
        }

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, new
        {
            project.Id, project.Name, project.Code, project.Description,
            project.Phase, project.Status, project.CreatedAt
        });
    }

    // D — derive a short uppercase alphanumeric code from a project name (caller uniquifies
    // within the tenant). Falls back to "PRJ" when the name has no usable characters.
    private static string DeriveProjectCode(string? name)
    {
        var s = new string((name ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(s)) s = "PRJ";
        return s.Length > 12 ? s.Substring(0, 12) : s;
    }

    /// <summary>Update project settings — name, phase, tag format, config JSON.</summary>
    /// <response code="200">Updated project object.</response>
    /// <response code="404">Project not found or does not belong to tenant.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateProject(Guid id, [FromBody] UpdateProjectRequest req)
    {
        if (!await ProjectVisibility.CanSeeProjectAsync(_db, id, User)) return NotFound();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id);
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
        if (!await ProjectVisibility.CanSeeProjectAsync(_db, id, User)) return NotFound();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        var issueCount = await _db.Issues.CountAsync(i => i.ProjectId == id && i.Status != "CLOSED");
        var overdueCount = await _db.Issues.CountAsync(i =>
            i.ProjectId == id && i.DueDate != null && i.DueDate < DateTime.UtcNow
            && i.Status != "CLOSED" && i.Status != "RESOLVED");
        var criticalCount = await _db.Issues.CountAsync(i =>
            i.ProjectId == id && i.Priority == "CRITICAL" && i.Status != "CLOSED");
        var docCount = await _db.Documents.CountAsync(d => d.ProjectId == id);
        var workflowRuns = await _db.WorkflowRuns
            .Where(w => w.ProjectId == id)
            .OrderByDescending(w => w.ExecutedAt)
            .Take(10)
            .ToListAsync();

        // NEW-INFO-04 — RecentIssues inline so the home screen isn't N+1.
        var recentIssues = await _db.Issues
            .Where(i => i.ProjectId == id)
            .OrderByDescending(i => i.CreatedAt)
            .Take(5)
            .Select(i => new
            {
                i.Id, i.IssueCode, i.Type, i.Title, i.Priority, i.Status,
                i.Assignee, i.CreatedAt, i.DueDate,
                IsOverdue = i.DueDate.HasValue && i.DueDate < DateTime.UtcNow
                    && i.Status != "CLOSED" && i.Status != "RESOLVED",
                DaysOpen = (int)(DateTime.UtcNow - i.CreatedAt).TotalDays
            })
            .ToListAsync();

        // NEW-INFO-05 — 30-day compliance trend inline (light payload: just
        // timestamp + overall percent, not the full snapshot).
        var trendStart = DateTime.UtcNow.AddDays(-30);
        var complianceTrend = await _db.ComplianceSnapshots
            .Where(s => s.ProjectId == id && s.CapturedAt >= trendStart)
            .OrderBy(s => s.CapturedAt)
            .Select(s => new { s.CapturedAt, s.TagPercent, s.ContainerPercent })
            .ToListAsync();

        return Ok(new
        {
            project.Id, project.Name, project.Code, project.Phase,
            project.CompliancePercent, project.ContainerCompliancePercent,
            project.RagStatus, project.TotalElements, project.TaggedElements,
            project.WarningCount, project.LastSyncAt,
            OpenIssues = issueCount,
            OverdueIssues = overdueCount,
            CriticalIssues = criticalCount,
            Documents = docCount,
            RecentWorkflows = workflowRuns,
            RecentIssues = recentIssues,
            ComplianceTrend = complianceTrend,
        });
    }

    /// <summary>
    /// Archive a project (soft delete). Restricted to tenant Admin /
    /// Owner / SecurityOfficer or the project author.
    /// </summary>
    /// <remarks>
    /// Phase 175 — destructive operation, double-gated:
    ///   1. Caller must be admin OR Project.CreatedById matches.
    ///   2. Caller must pass <c>?confirmCode=&lt;Project.Code&gt;</c> to
    ///      prove they typed the code rather than misclicked. The
    ///      front-end exposes this behind a "⋯ → Archive" menu and a
    ///      modal that requires the user to retype the project code.
    ///
    /// Effect: <c>Status</c> flips to <see cref="ProjectStatus.Archived"/>.
    /// The project keeps its data and remains visible to the same
    /// audience; the dashboard renders it under the "Completed" filter
    /// and stops counting it in active-project totals. There is no hard
    /// delete via this endpoint — true purges go through Admin tooling
    /// and require an additional approval flow.
    /// </remarks>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ArchiveProject(Guid id, [FromQuery] string? confirmCode = null)
    {
        if (!await ProjectVisibility.CanSeeProjectAsync(_db, id, User)) return NotFound();

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        var userId = ProjectVisibility.GetUserId(User);
        var isAdmin = ProjectVisibility.IsTenantAdmin(User);
        var isAuthor = project.CreatedById.HasValue && project.CreatedById.Value == userId;
        if (!isAdmin && !isAuthor)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Only the project author or a tenant admin can archive this project." });

        if (string.IsNullOrWhiteSpace(confirmCode)
            || !string.Equals(confirmCode.Trim(), project.Code, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Confirmation required — retype the project code to archive.",
                expectedField = "confirmCode",
                expectedValue = project.Code
            });
        }

        if (project.Status == ProjectStatus.Archived)
            return NoContent();

        project.Status = ProjectStatus.Archived;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

// D — Code is OPTIONAL: the UI / a user creating a project sends a name only. A required
// Code 400'd every name-only create ("Failed to create a new project"). When omitted it's
// auto-derived from the name (and uniquified within the tenant).
public record CreateProjectRequest(string Name, string? Code, string? Description, string? Phase);
public record UpdateProjectRequest(
    string? Name, string? Description, string? Phase, ProjectStatus? Status,
    string? TagSeparator, int? SeqNumPad, string? TagPrefix, string? TagSuffix,
    string? ConfigJson);
