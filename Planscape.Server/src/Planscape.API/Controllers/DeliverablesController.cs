using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Workflow;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 144 — MIDP / TIDP information-exchange deliverables. Each row is
/// one item the appointed party owes at a stage gate, with the standard
/// ISO 19650 information-state machine:
///
///   PENDING → IN_PROGRESS → SUBMITTED → ACCEPTED
///                                   └→ REJECTED → IN_PROGRESS (re-submit)
///                            (or WAIVED at any non-ACCEPTED point)
///
/// Listing supports filtering by stageGateId / status / discipline / overdue
/// so the BIM Manager can drill the same dataset multiple ways.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/[controller]")]
[EnableRateLimiting("mobile")]
[Authorize]
public class DeliverablesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly ILogger<DeliverablesController> _logger;

    public DeliverablesController(
        PlanscapeDbContext db,
        IAuditService audit,
        INotificationService notifications,
        ILogger<DeliverablesController> logger)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult> List(
        Guid projectId,
        [FromQuery] Guid? stageGateId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? discipline = null,
        [FromQuery] bool overdueOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 100;
        if (pageSize > 500) pageSize = 500;

        var tenantId = GetTenantId();
        var projectOk = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!projectOk) return NotFound("Project not found");

        var query = _db.InformationDeliverables.AsNoTracking()
            .Where(d => d.ProjectId == projectId);

        if (stageGateId.HasValue) query = query.Where(d => d.StageGateId == stageGateId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(d => d.Status == status);
        if (!string.IsNullOrWhiteSpace(discipline)) query = query.Where(d => d.Discipline == discipline);
        if (overdueOnly)
        {
            var now = DateTime.UtcNow;
            query = query.Where(d => d.DueDate < now
                && d.Status != "ACCEPTED" && d.Status != "WAIVED");
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderBy(d => d.DueDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new
            {
                d.Id, d.Code, d.Title, d.Type, d.OwnerRole, d.Discipline,
                d.SuitabilityTarget, d.DueDate, d.Status,
                d.SubmittedAt, d.SubmittedBy, d.AcceptedAt, d.AcceptedBy,
                d.StageGateId, d.DocumentId,
                isOverdue = d.DueDate < DateTime.UtcNow
                    && d.Status != "ACCEPTED" && d.Status != "WAIVED",
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, rows });
    }

    [HttpGet("{deliverableId}")]
    public async Task<ActionResult> Get(Guid projectId, Guid deliverableId)
    {
        var tenantId = GetTenantId();
        var d = await _db.InformationDeliverables.AsNoTracking()
            .Include(x => x.StageGate)
            .FirstOrDefaultAsync(x => x.Id == deliverableId && x.ProjectId == projectId
                                 && x.Project!.TenantId == tenantId);
        if (d == null) return NotFound();
        return Ok(d);
    }

    [HttpPost]
    public async Task<ActionResult> Create(Guid projectId, [FromBody] DeliverableRequest req)
    {
        var tenantId = GetTenantId();
        var projectOk = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!projectOk) return NotFound("Project not found");

        if (string.IsNullOrWhiteSpace(req.Code)) return BadRequest("code is required");
        if (await _db.InformationDeliverables.AnyAsync(d => d.ProjectId == projectId && d.Code == req.Code))
            return Conflict(new { error = $"Deliverable '{req.Code}' already exists for this project" });
        // Validate stageGate FK if provided
        if (req.StageGateId.HasValue)
        {
            var gateOk = await _db.StageGates.AnyAsync(g =>
                g.Id == req.StageGateId.Value && g.ProjectId == projectId);
            if (!gateOk) return BadRequest("stageGateId does not belong to this project");
        }

        var d = new InformationDeliverable
        {
            ProjectId = projectId,
            StageGateId = req.StageGateId,
            Code = req.Code,
            Title = req.Title ?? req.Code,
            Description = req.Description,
            Type = string.IsNullOrWhiteSpace(req.Type) ? "DR" : req.Type!,
            OwnerRole = req.OwnerRole ?? "",
            Discipline = req.Discipline,
            SuitabilityTarget = req.SuitabilityTarget,
            DueDate = req.DueDate,
            Status = "PENDING",
        };
        _db.InformationDeliverables.Add(d);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CREATE", "InformationDeliverable", d.Id.ToString());
        return CreatedAtAction(nameof(Get), new { projectId, deliverableId = d.Id }, d);
    }

    [HttpPut("{deliverableId}")]
    public async Task<ActionResult> Update(Guid projectId, Guid deliverableId, [FromBody] DeliverableRequest req)
    {
        var tenantId = GetTenantId();
        var d = await _db.InformationDeliverables
            .FirstOrDefaultAsync(x => x.Id == deliverableId && x.ProjectId == projectId
                                 && x.Project!.TenantId == tenantId);
        if (d == null) return NotFound();

        if (req.StageGateId.HasValue && req.StageGateId != d.StageGateId)
        {
            var gateOk = await _db.StageGates.AnyAsync(g =>
                g.Id == req.StageGateId.Value && g.ProjectId == projectId);
            if (!gateOk) return BadRequest("stageGateId does not belong to this project");
            d.StageGateId = req.StageGateId;
        }

        if (req.Title != null) d.Title = req.Title;
        if (req.Description != null) d.Description = req.Description;
        if (req.Type != null) d.Type = req.Type;
        if (req.OwnerRole != null) d.OwnerRole = req.OwnerRole;
        if (req.Discipline != null) d.Discipline = req.Discipline;
        if (req.SuitabilityTarget != null) d.SuitabilityTarget = req.SuitabilityTarget;
        d.DueDate = req.DueDate;
        d.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("UPDATE", "InformationDeliverable", d.Id.ToString());
        return Ok(d);
    }

    /// <summary>
    /// Apply a state transition. Validates against the ISO 19650
    /// information-state machine — or the project's
    /// <c>CustomDeliverableStateMachineJson</c> override when set.
    /// Rejects out-of-order moves.
    /// </summary>
    [HttpPost("{deliverableId}/transition")]
    public async Task<ActionResult> Transition(
        Guid projectId, Guid deliverableId, [FromBody] DeliverableTransitionRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        var d = await _db.InformationDeliverables
            .FirstOrDefaultAsync(x => x.Id == deliverableId && x.ProjectId == projectId);
        if (d == null) return NotFound();

        var machine = DeliverableStateMachine.LoadOrDefault(project.CustomDeliverableStateMachineJson);
        var target = (req.NewStatus ?? "").ToUpperInvariant();
        if (!machine.IsValidTransition(d.Status, target))
        {
            var allowed = machine.NextStates(d.Status);
            return BadRequest(new
            {
                error = $"Cannot transition from {d.Status} to {target}",
                machine = machine.Name,
                isCustom = machine.IsCustom,
                allowedTargets = allowed,
            });
        }

        d.Status = target;
        d.UpdatedAt = DateTime.UtcNow;
        var actor = User.FindFirst("display_name")?.Value ?? "Unknown";
        Guid? actorId = Guid.TryParse(User.FindFirst("user_id")?.Value, out var uid) ? uid : null;

        // Phase 146 — side-effects keyed by the *semantic role* of the
        // target state, not its name. Default ISO 19650 machine maps the
        // canonical SUBMITTED / ACCEPTED / REJECTED states to
        // submitting / accepting / rejecting; a custom machine can map
        // bespoke names (e.g. UNDER_REVIEW → submitting) and inherit the
        // same metadata writes.
        switch (machine.RoleOf(target))
        {
            case "submitting":
                d.SubmittedAt = DateTime.UtcNow;
                d.SubmittedBy = actor;
                d.SubmittedByUserId = actorId;
                if (req.DocumentId.HasValue) d.DocumentId = req.DocumentId;
                break;
            case "accepting":
                d.AcceptedAt = DateTime.UtcNow;
                d.AcceptedBy = actor;
                d.RejectionReason = null;
                break;
            case "rejecting":
                d.RejectionReason = req.Reason;
                break;
            // initial / working / terminal / none — plain transition
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync($"TRANSITION_{target}", "InformationDeliverable", d.Id.ToString());

        _ = _notifications.NotifyProjectAsync(projectId, "deliverable",
            $"Deliverable {d.Code} → {target}",
            d.Title,
            new { d.Id, d.Code, d.Status, projectId });

        return Ok(d);
    }

    /// <summary>
    /// Phase 145 — exposes the resolved state machine for this project so a
    /// mobile / web client can render contextual transition buttons that
    /// match whatever the project actually allows. Returns the full
    /// machine description plus a flag indicating whether it is the
    /// canonical default or a tenant override.
    /// </summary>
    [HttpGet("state-machine")]
    public async Task<ActionResult> GetStateMachine(Guid projectId)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        var machine = DeliverableStateMachine.LoadOrDefault(project.CustomDeliverableStateMachineJson);
        // If the JSON column is non-empty but the loader fell back to Default,
        // surface that fact so the BIM Manager can fix the malformed config.
        var jsonProvided = !string.IsNullOrWhiteSpace(project.CustomDeliverableStateMachineJson);
        return Ok(new
        {
            machine.Name,
            isCustom = machine.IsCustom,
            jsonProvidedButFellBack = jsonProvided && !machine.IsCustom,
            states = machine.States,
            initial = machine.InitialState,
            terminal = machine.TerminalStates,
            // Phase 146 — semantic roles per state so the mobile client can
            // surface badges like "this is the submission step" without
            // string-matching on names.
            roles = machine.SemanticRoles,
            transitions = machine.Transitions
                .Select(t => new { from = t.From, to = t.To })
                .OrderBy(t => t.from).ThenBy(t => t.to),
        });
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record DeliverableRequest(
    string Code,
    string? Title,
    string? Description,
    string? Type,
    string? OwnerRole,
    string? Discipline,
    string? SuitabilityTarget,
    DateTime DueDate,
    Guid? StageGateId
);

public record DeliverableTransitionRequest(
    string NewStatus,
    Guid? DocumentId,
    string? Reason
);
