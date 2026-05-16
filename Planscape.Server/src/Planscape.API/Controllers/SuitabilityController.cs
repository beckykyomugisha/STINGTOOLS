using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// ISO 19650 document suitability state machine. Manages transitions between
/// S0-S7 (design), B1-B7 (inter-discipline), A1-A7 (authorised), CR, AB codes.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/suitability")]
[Authorize]
public class SuitabilityController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<SuitabilityController> _logger;

    public SuitabilityController(PlanscapeDbContext db, ILogger<SuitabilityController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirst("tenantId")?.Value
            ?? throw new InvalidOperationException("tenantId claim missing"));

    // ── Transition Rules ──────────────────────────────────────────────────

    [HttpGet("rules")]
    public async Task<ActionResult> GetRules(Guid projectId)
    {
        var tenantId = GetTenantId();
        var rules = await _db.SuitabilityTransitionRules
            .Where(r => r.TenantId == tenantId && (r.ProjectId == null || r.ProjectId == projectId))
            .OrderBy(r => r.FromCode).ThenBy(r => r.ToCode)
            .ToListAsync();
        return Ok(rules);
    }

    [HttpPost("rules")]
    public async Task<ActionResult> CreateRule(Guid projectId, [FromBody] CreateSuitabilityRuleRequest req)
    {
        var tenantId = GetTenantId();
        var rule = new SuitabilityTransitionRule
        {
            TenantId                = tenantId,
            ProjectId               = req.TenantWide ? null : projectId,
            FromCode                = (SuitabilityCode)req.FromCode,
            ToCode                  = (SuitabilityCode)req.ToCode,
            AllowedRoles            = req.AllowedRoles,
            RequiredApprovalChainId = req.RequiredApprovalChainId.HasValue ? req.RequiredApprovalChainId : null,
            PreconditionMask        = req.PreconditionMask,
            AutoTriggerAfterHours   = req.AutoTriggerAfterHours,
            Enabled                 = true,
        };
        _db.SuitabilityTransitionRules.Add(rule);
        await _db.SaveChangesAsync();
        return Ok(rule);
    }

    // ── Document Transitions ──────────────────────────────────────────────

    [HttpPost("documents/{documentId}/transition")]
    public async Task<ActionResult> Transition(Guid projectId, Guid documentId,
        [FromBody] SuitabilityTransitionRequest req)
    {
        var tenantId = GetTenantId();
        // Explicit Include avoids a lazy-load when accessing d.Project!.TenantId.
        var document = await _db.Documents
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.ProjectId == projectId
                                   && d.Project!.TenantId == tenantId);
        if (document is null) return NotFound("Document not found.");

        // Find applicable rule (project-specific first, then tenant-wide; break ties by Id).
        var rule = await _db.SuitabilityTransitionRules
            .Where(r => r.TenantId == tenantId && r.Enabled
                     && r.FromCode == (SuitabilityCode)req.FromCode
                     && r.ToCode   == (SuitabilityCode)req.ToCode
                     && (r.ProjectId == projectId || r.ProjectId == null))
            .OrderBy(r => r.ProjectId == null ? 1 : 0) // project-specific wins
            .ThenBy(r => r.Id)
            .FirstOrDefaultAsync();

        if (rule is null)
            return BadRequest($"No transition rule found from code {req.FromCode} to {req.ToCode}.");

        // Role check — trim each entry to handle "Admin, User" (leading space) formatting.
        if (!string.IsNullOrEmpty(rule.AllowedRoles))
        {
            var userRole = User.FindFirst("role")?.Value ?? "";
            var allowed  = rule.AllowedRoles
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim());
            if (!allowed.Contains(userRole, StringComparer.OrdinalIgnoreCase))
                return Forbid();
        }

        // Record the transition
        var transition = new SuitabilityTransition
        {
            TenantId         = tenantId,
            ProjectId        = projectId,
            DocumentRecordId = documentId,
            RuleId           = rule.Id,
            FromCode         = (SuitabilityCode)req.FromCode,
            ToCode           = (SuitabilityCode)req.ToCode,
            TriggeredBy      = User.Identity?.Name ?? "",
            TriggerSource    = "User",
            Notes            = req.Notes,
        };
        _db.SuitabilityTransitions.Add(transition);

        // Update the document's suitability field if it has one
        // (DocumentRecord has a SuitabilityCode string field per the entity model)
        document.SuitabilityCode = transition.ToCode.ToString();
        document.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Suitability transition: doc={DocId} {From}→{To} by {User}",
            documentId, req.FromCode, req.ToCode, transition.TriggeredBy);

        return Ok(transition);
    }

    [HttpGet("documents/{documentId}/history")]
    public async Task<ActionResult> GetHistory(Guid projectId, Guid documentId)
    {
        var tenantId = GetTenantId();
        var history = await _db.SuitabilityTransitions
            .Where(t => t.DocumentRecordId == documentId && t.TenantId == tenantId)
            .OrderByDescending(t => t.TriggeredAt)
            .Select(t => new
            {
                t.Id, t.FromCode, t.ToCode, t.TriggeredBy,
                t.TriggerSource, t.Notes, t.TriggeredAt, t.RuleId
            })
            .ToListAsync();
        return Ok(history);
    }

    // ── Available next codes for a document ──────────────────────────────

    [HttpGet("documents/{documentId}/next-codes")]
    public async Task<ActionResult> GetNextCodes(Guid projectId, Guid documentId)
    {
        var tenantId = GetTenantId();
        var document = await _db.Documents
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.ProjectId == projectId
                                   && d.Project!.TenantId == tenantId);
        if (document is null) return NotFound();

        if (!Enum.TryParse<SuitabilityCode>(document.SuitabilityCode ?? "S0", out var current))
        {
            _logger.LogWarning("Unknown suitability code '{Code}' on document {Id}, defaulting to S0",
                document.SuitabilityCode, documentId);
            current = SuitabilityCode.S0;
        }

        var nextRules = await _db.SuitabilityTransitionRules
            .Where(r => r.TenantId == tenantId && r.Enabled
                     && r.FromCode == current
                     && (r.ProjectId == projectId || r.ProjectId == null))
            .Select(r => new { r.ToCode, r.AllowedRoles, r.RequiredApprovalChainId })
            .ToListAsync();

        return Ok(new
        {
            currentCode    = current.ToString(),
            currentCodeInt = (int)current,
            availableTransitions = nextRules
        });
    }
}

public record CreateSuitabilityRuleRequest(
    int FromCode,
    int ToCode,
    string? AllowedRoles,
    Guid? RequiredApprovalChainId,
    int PreconditionMask,
    int? AutoTriggerAfterHours,
    bool TenantWide);

public record SuitabilityTransitionRequest(
    int FromCode,
    int ToCode,
    string? Notes);
