using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Pillar B (6A) — twin rule CRUD + a corporate-defaults seeder. Project rows
/// ARE the overlay over the corporate baseline (TwinRuleDefaults).
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/twins/rules")]
[Authorize]
public class TwinRulesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public TwinRulesController(PlanscapeDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<object>> List(Guid projectId, CancellationToken ct)
        => Ok(await _db.TwinRules.Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.Metric).ToListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<object>> Create(
        Guid projectId, [FromBody] RuleRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Metric)) return BadRequest("metric is required");
        if (!await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == GetTenantId(), ct))
            return NotFound();

        var rule = new TwinRule
        {
            TenantId = _db.CurrentTenantId,
            ProjectId = projectId,
            Name = req.Name ?? req.Metric,
            DeviceId = req.DeviceId,
            Metric = req.Metric,
            Operator = string.IsNullOrWhiteSpace(req.Operator) ? "gt" : req.Operator,
            Threshold = req.Threshold,
            AnomalySigma = req.AnomalySigma ?? 3.0,
            Severity = string.IsNullOrWhiteSpace(req.Severity) ? "WARNING" : req.Severity,
            Enabled = req.Enabled ?? true,
            RaiseWorkOrder = req.RaiseWorkOrder ?? false,
            ConsecutiveBreaches = req.ConsecutiveBreaches ?? 1,
        };
        _db.TwinRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return Ok(rule);
    }

    [HttpDelete("{ruleId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid ruleId, CancellationToken ct)
    {
        var rule = await _db.TwinRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.ProjectId == projectId, ct);
        if (rule is null) return NotFound();
        _db.TwinRules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>POST seed-defaults — insert the corporate baseline rules not already present.</summary>
    [HttpPost("seed-defaults")]
    public async Task<ActionResult<object>> SeedDefaults(Guid projectId, CancellationToken ct)
    {
        if (!await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == GetTenantId(), ct))
            return NotFound();

        var existing = await _db.TwinRules
            .Where(r => r.ProjectId == projectId)
            .Select(r => r.Name).ToListAsync(ct);

        int added = 0;
        foreach (var d in TwinRuleDefaults.All)
        {
            if (existing.Contains(d.Name)) continue;
            _db.TwinRules.Add(new TwinRule
            {
                TenantId = _db.CurrentTenantId, ProjectId = projectId,
                Name = d.Name, Metric = d.Metric, Operator = d.Operator,
                Threshold = d.Threshold, Severity = d.Severity,
                Enabled = true, RaiseWorkOrder = d.RaiseWorkOrder,
            });
            added++;
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { added });
    }

    private Guid GetTenantId()
    {
        var c = User.FindFirst("tenant_id")?.Value;
        return c != null && Guid.TryParse(c, out var id) ? id : Guid.Empty;
    }

    public class RuleRequest
    {
        public string? Name { get; set; }
        public string? DeviceId { get; set; }
        public string Metric { get; set; } = "";
        public string? Operator { get; set; }
        public double? Threshold { get; set; }
        public double? AnomalySigma { get; set; }
        public string? Severity { get; set; }
        public bool? Enabled { get; set; }
        public bool? RaiseWorkOrder { get; set; }
        public int? ConsecutiveBreaches { get; set; }
    }
}

/// <summary>Corporate-baseline twin rules (the overlay base for SeedDefaults).</summary>
public static class TwinRuleDefaults
{
    public sealed record Def(string Name, string Metric, string Operator, double? Threshold, string Severity, bool RaiseWorkOrder);

    public static readonly IReadOnlyList<Def> All = new List<Def>
    {
        new("AHU supply air high",   "supply_air_temp_c", "gt",  30, "WARNING", false),
        new("AHU supply air critical","supply_air_temp_c", "gt",  35, "ALARM",   true),
        new("Space CO2 high",        "co2_ppm",           "gt",  1000, "WARNING", false),
        new("DHW below legionella",  "dhw_temp_c",        "lt",  50, "ALARM",   true),   // HTM 04-01
        new("Pressure cascade lost", "room_pressure_pa",  "lt",  0,  "ALARM",   true),   // HTM 03-01
        new("Filter pressure drop",  "filter_dp_pa",      "gt",  250, "WARNING", false),
        new("Vibration anomaly",     "vibration_mm_s",    "anomaly", null, "WARNING", false),
        new("Energy anomaly",        "power_kw",          "anomaly", null, "WARNING", false),
    };
}
