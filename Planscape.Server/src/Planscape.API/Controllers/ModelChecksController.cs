using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// Solibri-grade model checker: rule-set/rule CRUD, run trigger,
/// result query and per-finding status management.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/model-checks")]
[Authorize]
public class ModelChecksController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<ModelChecksController> _logger;
    private readonly IBackgroundJobClient _jobClient;

    public ModelChecksController(PlanscapeDbContext db, ILogger<ModelChecksController> logger,
        IBackgroundJobClient jobClient)
    {
        _db = db;
        _logger = logger;
        _jobClient = jobClient;
    }

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirst("tenantId")?.Value
            ?? throw new InvalidOperationException("tenantId claim missing"));

    // ── Rule Sets ─────────────────────────────────────────────────────────

    [HttpGet("rule-sets")]
    public async Task<ActionResult> GetRuleSets(Guid projectId)
    {
        var tenantId = GetTenantId();
        var sets = await _db.ModelCheckRuleSets
            .Where(s => s.TenantId == tenantId && (s.ProjectId == null || s.ProjectId == projectId))
            .OrderBy(s => s.Code)
            .Select(s => new
            {
                s.Id, s.Code, s.Name, s.Description, s.Version,
                s.Schedule, s.Enabled, s.Checksum, s.ProjectId,
                s.CreatedAt, s.UpdatedAt, s.CreatedBy
            })
            .ToListAsync();
        return Ok(sets);
    }

    [HttpPost("rule-sets")]
    public async Task<ActionResult> CreateRuleSet(Guid projectId, [FromBody] CreateRuleSetRequest req)
    {
        var tenantId = GetTenantId();
        var ruleSet = new ModelCheckRuleSet
        {
            TenantId    = tenantId,
            ProjectId   = req.ProjectScoped ? projectId : null,
            Code        = req.Code,
            Name        = req.Name,
            Description = req.Description,
            Version     = req.Version ?? "1.0",
            Schedule    = req.Schedule,
            Enabled     = true,
            CreatedBy   = User.Identity?.Name,
        };
        _db.ModelCheckRuleSets.Add(ruleSet);
        await _db.SaveChangesAsync();
        return Ok(ruleSet);
    }

    [HttpPut("rule-sets/{id}")]
    public async Task<ActionResult> UpdateRuleSet(Guid projectId, Guid id, [FromBody] CreateRuleSetRequest req)
    {
        var tenantId = GetTenantId();
        // Scope to project: a user can only update rule sets belonging to their project or tenant-wide ones.
        var ruleSet = await _db.ModelCheckRuleSets.FirstOrDefaultAsync(
            s => s.Id == id && s.TenantId == tenantId
              && (s.ProjectId == null || s.ProjectId == projectId));
        if (ruleSet is null) return NotFound();

        ruleSet.Name      = req.Name;
        ruleSet.Description = req.Description ?? ruleSet.Description;
        ruleSet.Version   = req.Version ?? ruleSet.Version;
        ruleSet.Schedule  = req.Schedule ?? ruleSet.Schedule;
        ruleSet.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ruleSet);
    }

    // ── Rules ─────────────────────────────────────────────────────────────

    [HttpGet("rule-sets/{ruleSetId}/rules")]
    public async Task<ActionResult> GetRules(Guid projectId, Guid ruleSetId)
    {
        var tenantId = GetTenantId();
        var rules = await _db.ModelCheckRules
            .Where(r => r.RuleSetId == ruleSetId && r.TenantId == tenantId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Code)
            .ToListAsync();
        return Ok(rules);
    }

    [HttpPost("rule-sets/{ruleSetId}/rules")]
    public async Task<ActionResult> CreateRule(Guid projectId, Guid ruleSetId, [FromBody] CreateModelCheckRuleRequest req)
    {
        var tenantId = GetTenantId();
        var ruleSet = await _db.ModelCheckRuleSets
            .FirstOrDefaultAsync(s => s.Id == ruleSetId && s.TenantId == tenantId
                                   && (s.ProjectId == null || s.ProjectId == projectId));
        if (ruleSet is null) return NotFound("RuleSet not found.");

        var rule = new ModelCheckRule
        {
            TenantId            = tenantId,
            RuleSetId           = ruleSetId,
            Code                = req.Code,
            Name                = req.Name,
            Description         = req.Description,
            Kind                = req.Kind ?? "PropertyRequired",
            Severity            = req.Severity ?? "Major",
            AppliesToIfcTypes   = req.AppliesToIfcTypes,
            AppliesToDiscipline = req.AppliesToDiscipline,
            ParamsJson          = req.ParamsJson ?? "{}",
            AutoAction          = req.AutoAction ?? "None",
            Enabled             = true,
            SortOrder           = req.SortOrder,
            CreatedBy           = User.Identity?.Name,
        };
        _db.ModelCheckRules.Add(rule);
        await _db.SaveChangesAsync();
        return Ok(rule);
    }

    [HttpPut("rule-sets/{ruleSetId}/rules/{ruleId}")]
    public async Task<ActionResult> UpdateRule(Guid projectId, Guid ruleSetId, Guid ruleId,
        [FromBody] CreateModelCheckRuleRequest req)
    {
        var tenantId = GetTenantId();
        var rule = await _db.ModelCheckRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.RuleSetId == ruleSetId && r.TenantId == tenantId);
        if (rule is null) return NotFound();

        rule.Name               = req.Name;
        rule.Description        = req.Description ?? rule.Description;
        rule.Severity           = req.Severity ?? rule.Severity;
        rule.AppliesToIfcTypes  = req.AppliesToIfcTypes ?? rule.AppliesToIfcTypes;
        rule.AppliesToDiscipline = req.AppliesToDiscipline ?? rule.AppliesToDiscipline;
        rule.ParamsJson         = req.ParamsJson ?? rule.ParamsJson;
        rule.AutoAction         = req.AutoAction ?? rule.AutoAction;
        rule.SortOrder          = req.SortOrder;
        rule.UpdatedAt          = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(rule);
    }

    [HttpDelete("rule-sets/{ruleSetId}/rules/{ruleId}")]
    public async Task<ActionResult> DeleteRule(Guid projectId, Guid ruleSetId, Guid ruleId)
    {
        var tenantId = GetTenantId();
        var rule = await _db.ModelCheckRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.RuleSetId == ruleSetId && r.TenantId == tenantId);
        if (rule is null) return NotFound();
        _db.ModelCheckRules.Remove(rule);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Runs ──────────────────────────────────────────────────────────────

    [HttpGet("runs")]
    public async Task<ActionResult> GetRuns(Guid projectId,
        [FromQuery] Guid? ruleSetId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 100);

        var tenantId = GetTenantId();
        var query = _db.ModelCheckRuns
            .Where(r => r.ProjectId == projectId && r.TenantId == tenantId);

        if (ruleSetId.HasValue)
            query = query.Where(r => r.RuleSetId == ruleSetId);

        var total = await query.CountAsync();
        var runs = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                r.Id, r.RuleSetId, r.ProjectModelId, r.StartedAt, r.CompletedAt,
                r.Status, r.TotalRulesEvaluated, r.TotalElementsChecked,
                r.FindingsCount, r.CriticalCount, r.MajorCount, r.MinorCount,
                r.InfoCount, r.ErrorMessage, r.TriggeredBy
            })
            .ToListAsync();
        return Ok(new { total, page, pageSize, items = runs });
    }

    [HttpPost("runs")]
    public async Task<ActionResult> TriggerRun(Guid projectId, [FromBody] TriggerRunRequest req)
    {
        var tenantId = GetTenantId();
        var ruleSet = await _db.ModelCheckRuleSets
            .FirstOrDefaultAsync(s => s.Id == req.RuleSetId && s.TenantId == tenantId);
        if (ruleSet is null) return NotFound("RuleSet not found.");

        var run = new ModelCheckRun
        {
            TenantId       = tenantId,
            ProjectId      = projectId,
            RuleSetId      = req.RuleSetId,
            ProjectModelId = req.ProjectModelId,
            Status         = "Queued",
            TriggeredBy    = User.Identity?.Name ?? "api",
        };
        _db.ModelCheckRuns.Add(run);
        await _db.SaveChangesAsync();

        var jobId = _jobClient.Enqueue<IModelCheckerService>(
            s => s.ExecuteRunAsync(run.Id, CancellationToken.None));

        _logger.LogInformation(
            "Model check run {RunId} enqueued as Hangfire job {JobId} for rule set {RuleSetId}",
            run.Id, jobId, req.RuleSetId);
        return AcceptedAtAction(nameof(GetRun), new { projectId, runId = run.Id }, run);
    }

    [HttpGet("runs/{runId}")]
    public async Task<ActionResult> GetRun(Guid projectId, Guid runId)
    {
        var tenantId = GetTenantId();
        var run = await _db.ModelCheckRuns
            .FirstOrDefaultAsync(r => r.Id == runId && r.ProjectId == projectId && r.TenantId == tenantId);
        return run is null ? NotFound() : Ok(run);
    }

    // ── Results ───────────────────────────────────────────────────────────

    [HttpGet("runs/{runId}/results")]
    public async Task<ActionResult> GetResults(Guid projectId, Guid runId,
        [FromQuery] string? severity = null, [FromQuery] string? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 500);

        var tenantId = GetTenantId();
        var query = _db.ModelCheckResults
            .Where(r => r.RunId == runId && r.ProjectId == projectId && r.TenantId == tenantId);

        if (!string.IsNullOrEmpty(severity))
            query = query.Where(r => r.Severity == severity);
        if (!string.IsNullOrEmpty(status))
            query = query.Where(r => r.Status == status);

        var total = await query.CountAsync();
        var results = await query
            .OrderBy(r => r.Severity).ThenBy(r => r.DetectedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                r.Id, r.RuleId, r.ProjectModelId, r.IfcGlobalId, r.IfcType,
                r.ElementName, r.Level, r.Severity, r.Message, r.Suggestion,
                r.Status, r.BimIssueId, r.DetectedAt, r.ResolvedAt, r.ResolvedBy
            })
            .ToListAsync();
        return Ok(new { total, page, pageSize, items = results });
    }

    [HttpPut("results/{resultId}/status")]
    public async Task<ActionResult> UpdateResultStatus(Guid projectId, Guid resultId,
        [FromBody] UpdateModelCheckResultRequest req)
    {
        var tenantId = GetTenantId();
        var result = await _db.ModelCheckResults
            .FirstOrDefaultAsync(r => r.Id == resultId && r.ProjectId == projectId && r.TenantId == tenantId);
        if (result is null) return NotFound();

        var allowed = new[] { "Open", "Acknowledged", "Resolved", "Ignored", "FalsePositive" };
        if (!allowed.Contains(req.Status)) return BadRequest("Invalid status.");

        result.Status = req.Status;
        if (req.Status == "Resolved")
        {
            result.ResolvedAt = DateTime.UtcNow;
            result.ResolvedBy = User.Identity?.Name;
        }
        if (req.BimIssueId.HasValue)
            result.BimIssueId = req.BimIssueId;

        await _db.SaveChangesAsync();
        return Ok(result);
    }

    // ── Project-level summary ─────────────────────────────────────────────

    [HttpGet("summary")]
    public async Task<ActionResult> GetSummary(Guid projectId)
    {
        var tenantId = GetTenantId();
        var latestRun = await _db.ModelCheckRuns
            .Where(r => r.ProjectId == projectId && r.TenantId == tenantId && r.Status == "Completed")
            .OrderByDescending(r => r.CompletedAt)
            .Select(r => new
            {
                r.Id, r.RuleSetId, r.CompletedAt,
                r.FindingsCount, r.CriticalCount, r.MajorCount, r.MinorCount, r.InfoCount
            })
            .FirstOrDefaultAsync();

        var openByRuleSet = await _db.ModelCheckResults
            .Where(r => r.ProjectId == projectId && r.TenantId == tenantId && r.Status == "Open")
            .GroupBy(r => r.RuleId)
            .Select(g => new { RuleId = g.Key, Count = g.Count() })
            .ToListAsync();

        return Ok(new { latestRun, openFindingsByRule = openByRuleSet });
    }
}

public record CreateRuleSetRequest(
    string Code,
    string Name,
    string? Description,
    string? Version,
    string? Schedule,
    bool ProjectScoped);

public record CreateModelCheckRuleRequest(
    string Code,
    string Name,
    string? Description,
    string? Kind,
    string? Severity,
    string? AppliesToIfcTypes,
    string? AppliesToDiscipline,
    string? ParamsJson,
    string? AutoAction,
    int SortOrder);

public record TriggerRunRequest(Guid RuleSetId, Guid? ProjectModelId);

public record UpdateModelCheckResultRequest(string Status, Guid? BimIssueId);
