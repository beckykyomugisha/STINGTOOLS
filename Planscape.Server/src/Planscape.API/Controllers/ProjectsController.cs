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
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(PlanscapeDbContext db, ILogger<ProjectsController> logger)
    {
        _db = db;
        _logger = logger;
    }

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
                p.CoverImageUrl, p.IsPinned, p.DocumentSyncAutoEnabled,
                MemberCount = _db.ProjectMembers
                    .Count(m => m.ProjectId == p.Id && m.IsActive),
                // The projects grid shows an open-issue count per row. Without
                // it here the web app would have to call {id}/dashboard once per
                // project — an N+1 for a single integer. Same predicate the
                // dashboard's OpenIssues uses, so the two agree.
                OpenIssueCount = _db.Issues
                    .Count(i => i.ProjectId == p.Id && i.Status != "CLOSED")
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
            project.Status, project.DocumentSyncAutoEnabled,
            project.TagSeparator, project.SeqNumPad,
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

        // The project cap is enforced ONCE, by [Quota(QuotaAxis.Projects)] above, which
        // resolves plan entitlement against the tenant's tightening override in
        // ProjectCeilingPolicy. An inline `projectCount >= tenant.MaxProjects` used to
        // sit here as a second gate reading only the column; the two disagreed for
        // every self-signup (plan said 1, column said int.MaxValue) and the stricter
        // one won purely because a filter runs before the action body. Do not re-add it.

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
        if (req.DocumentSyncAutoEnabled.HasValue)
            project.DocumentSyncAutoEnabled = req.DocumentSyncAutoEnabled.Value;

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

    /// <summary>
    /// Number of days between scheduling a hard delete and the purge job
    /// actually destroying anything. Matches CustomFieldsPurgeJob's grace.
    /// </summary>
    public const int PurgeGraceDays = 30;

    /// <summary>
    /// Schedule a project for PERMANENT deletion (hard delete).
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ArchiveProject"/> this destroys data and cannot be
    /// undone once the purge runs. Triple-gated, and deliberately stricter
    /// than archive on every axis:
    ///   1. Tenant OWNER only — not Admin, not the project author. Archive
    ///      remains open to author/admin so routine cleanup is unaffected;
    ///      only irreversible destruction is restricted this hard.
    ///   2. Must already be Archived. Forces archive → confirm nothing broke
    ///      → purge, so no single action takes a live project to destroyed.
    ///   3. Must pass ?confirmCode=&lt;Project.Code&gt;, same proof-of-intent
    ///      as archive.
    ///
    /// Effect is a SCHEDULE, not a destruction: PurgeAfter is set to now +
    /// <see cref="PurgeGraceDays"/> days and the project vanishes from every
    /// read path immediately, but the rows survive until ProjectPurgeJob runs
    /// past that date. Until then <see cref="CancelPurge"/> fully restores it.
    /// </remarks>
    [HttpPost("{id}/purge")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SchedulePurge(Guid id, [FromQuery] string? confirmCode = null)
    {
        // Deliberately NOT WhereVisibleTo/CanSeeProjectAsync: those now filter
        // out anything already pending purge, which would turn a double-call
        // into a confusing 404 instead of the "already scheduled" answer below.
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == GetTenantId());
        if (project == null) return NotFound();

        if (!ProjectVisibility.IsTenantOwner(User))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Only the tenant owner can permanently delete a project. Archiving is available to project authors and admins."
            });

        if (project.Status != ProjectStatus.Archived)
            return BadRequest(new
            {
                message = "Archive this project first. Permanent deletion is only available for an archived project, so there is always a reversible step in between.",
                currentStatus = project.Status.ToString()
            });

        if (string.IsNullOrWhiteSpace(confirmCode)
            || !string.Equals(confirmCode.Trim(), project.Code, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Confirmation required — retype the project code to permanently delete.",
                expectedField = "confirmCode",
                expectedValue = project.Code
            });
        }

        if (project.PurgeAfter != null)
            return Ok(new { message = "Already scheduled for deletion.", purgeAfter = project.PurgeAfter, alreadyScheduled = true });

        project.PurgeAfter = DateTime.UtcNow.AddDays(PurgeGraceDays);
        project.PurgeRequestedById = ProjectVisibility.GetUserId(User);
        await _db.SaveChangesAsync();

        _logger.LogWarning(
            "[purge] project {ProjectId} ({Code}) scheduled for PERMANENT deletion after {PurgeAfter:u} by user {UserId}",
            project.Id, project.Code, project.PurgeAfter, project.PurgeRequestedById);

        return Ok(new
        {
            message = $"Scheduled for permanent deletion. Recoverable until {project.PurgeAfter:u}.",
            purgeAfter = project.PurgeAfter,
            graceDays = PurgeGraceDays,
            cancelUrl = $"/api/projects/{project.Id}/purge"
        });
    }

    /// <summary>
    /// Cancel a scheduled hard delete, restoring the project. Valid any time
    /// before ProjectPurgeJob actually runs; after that there is nothing left
    /// to restore, which is the whole point of the grace window.
    /// </summary>
    [HttpDelete("{id}/purge")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> CancelPurge(Guid id)
    {
        // Same direct query as SchedulePurge — a pending-purge project is by
        // definition invisible to the normal visibility path.
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == GetTenantId());
        if (project == null) return NotFound();

        if (!ProjectVisibility.IsTenantOwner(User))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Only the tenant owner can cancel a scheduled deletion." });

        if (project.PurgeAfter == null)
            return Ok(new { message = "This project is not scheduled for deletion.", wasScheduled = false });

        project.PurgeAfter = null;
        project.PurgeRequestedById = null;
        await _db.SaveChangesAsync();

        _logger.LogWarning("[purge] scheduled deletion CANCELLED for project {ProjectId} ({Code})", project.Id, project.Code);
        return Ok(new { message = "Deletion cancelled — the project is restored (still archived).", wasScheduled = true });
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
    string? ConfigJson,
    // Document sync — "Auto-sync this project". Nullable so an existing caller
    // that omits it leaves the flag alone, same as every other field here.
    bool? DocumentSyncAutoEnabled = null);
