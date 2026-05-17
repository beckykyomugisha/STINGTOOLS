namespace Planscape.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

/// <summary>
/// CRUD over <see cref="ClashAutomationRule"/> rows for a single project.
/// Read-only listing on mobile (with enable/disable); full create/edit/
/// delete via web admin. Project membership + tenant isolation are
/// enforced by the global query filter + the per-route <c>_tenant.TenantId</c>
/// guard so cross-tenant writes return 404.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/clash-rules")]
[Authorize]
public class ClashAutomationRulesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;

    public ClashAutomationRulesController(PlanscapeDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, CancellationToken ct)
    {
        var rules = await _db.ClashAutomationRules.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.TenantId == _tenant.TenantId)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);
        return Ok(rules);
    }

    [HttpGet("{ruleId:guid}")]
    public async Task<ActionResult<ClashAutomationRule>> Get(Guid projectId, Guid ruleId, CancellationToken ct)
    {
        var rule = await _db.ClashAutomationRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.ProjectId == projectId && r.TenantId == _tenant.TenantId, ct);
        return rule == null ? NotFound() : Ok(rule);
    }

    [HttpPost]
    public async Task<ActionResult<ClashAutomationRule>> Create(Guid projectId, [FromBody] ClashAutomationRule rule, CancellationToken ct)
    {
        rule.Id = Guid.NewGuid();
        rule.TenantId = _tenant.TenantId;
        rule.ProjectId = projectId;
        rule.CreatedAt = DateTime.UtcNow;
        rule.UpdatedAt = DateTime.UtcNow;
        rule.CreatedBy ??= User?.Identity?.Name;
        _db.ClashAutomationRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/projects/{projectId}/clash-rules/{rule.Id}", rule);
    }

    [HttpPut("{ruleId:guid}")]
    public async Task<ActionResult<ClashAutomationRule>> Update(Guid projectId, Guid ruleId, [FromBody] ClashAutomationRule update, CancellationToken ct)
    {
        var rule = await _db.ClashAutomationRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.ProjectId == projectId && r.TenantId == _tenant.TenantId, ct);
        if (rule == null) return NotFound();

        rule.Name = update.Name;
        rule.Enabled = update.Enabled;
        rule.Priority = update.Priority;
        rule.MinSeverity = update.MinSeverity;
        rule.DisciplineA = update.DisciplineA;
        rule.DisciplineB = update.DisciplineB;
        rule.Kind = update.Kind;
        rule.MinOverlapVolumeMm3 = update.MinOverlapVolumeMm3;
        rule.LevelCode = update.LevelCode;
        rule.AutoCreateIssue = update.AutoCreateIssue;
        rule.AutoAssignTo = update.AutoAssignTo;
        rule.IssuePriority = update.IssuePriority;
        rule.NotifyPush = update.NotifyPush;
        rule.NotifyUsers = update.NotifyUsers;
        rule.FireWebhook = update.FireWebhook;
        rule.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(rule);
    }

    [HttpDelete("{ruleId:guid}")]
    public async Task<ActionResult> Delete(Guid projectId, Guid ruleId, CancellationToken ct)
    {
        var rule = await _db.ClashAutomationRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.ProjectId == projectId && r.TenantId == _tenant.TenantId, ct);
        if (rule == null) return NotFound();
        _db.ClashAutomationRules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
