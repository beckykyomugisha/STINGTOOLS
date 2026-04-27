using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 144 — RIBA Plan of Work / ISO 19650 stage-gate dashboard.
///
/// One row per (project, stage_code). Endpoints:
///   GET    /api/projects/{id}/stagegates                — timeline list
///   GET    /api/projects/{id}/stagegates/{gateId}        — detail with deliverable rollup
///   POST   /api/projects/{id}/stagegates                 — create gate
///   PUT    /api/projects/{id}/stagegates/{gateId}        — edit gate
///   POST   /api/projects/{id}/stagegates/{gateId}/decide — record pass/fail
///   POST   /api/projects/{id}/stagegates/seed-riba       — convenience: insert RIBA 0–7 stages with default sortOrder
///
/// Decision (PASSED / FAILED / WAIVED) fires a project-scoped push so the
/// rest of the team is notified.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/[controller]")]
[EnableRateLimiting("mobile")]
[Authorize]
public class StageGatesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly ILogger<StageGatesController> _logger;

    public StageGatesController(
        PlanscapeDbContext db,
        IAuditService audit,
        INotificationService notifications,
        ILogger<StageGatesController> logger)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult> List(Guid projectId)
    {
        var tenantId = GetTenantId();
        var projectOk = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!projectOk) return NotFound("Project not found");

        // Pull deliverable counts in one round-trip via a sub-query so the
        // stage list is render-ready on mobile (no follow-up queries).
        var rows = await _db.StageGates.AsNoTracking()
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.PlannedDate ?? DateTime.MaxValue)
            .Select(g => new
            {
                g.Id, g.StageCode, g.StageName, g.SortOrder,
                g.PlannedDate, g.ActualDate, g.Status,
                g.DecidedBy, g.DecidedAt,
                deliverables = new
                {
                    total = _db.InformationDeliverables.Count(d => d.StageGateId == g.Id),
                    pending = _db.InformationDeliverables.Count(d => d.StageGateId == g.Id && d.Status == "PENDING"),
                    inProgress = _db.InformationDeliverables.Count(d => d.StageGateId == g.Id && d.Status == "IN_PROGRESS"),
                    submitted = _db.InformationDeliverables.Count(d => d.StageGateId == g.Id && d.Status == "SUBMITTED"),
                    accepted = _db.InformationDeliverables.Count(d => d.StageGateId == g.Id && d.Status == "ACCEPTED"),
                    rejected = _db.InformationDeliverables.Count(d => d.StageGateId == g.Id && d.Status == "REJECTED"),
                    overdue = _db.InformationDeliverables.Count(d => d.StageGateId == g.Id
                        && d.Status != "ACCEPTED" && d.Status != "WAIVED"
                        && d.DueDate < DateTime.UtcNow),
                }
            })
            .ToListAsync();

        return Ok(rows);
    }

    [HttpGet("{gateId}")]
    public async Task<ActionResult> Get(Guid projectId, Guid gateId)
    {
        var tenantId = GetTenantId();
        var gate = await _db.StageGates.AsNoTracking()
            .Include(g => g.Deliverables)
            .FirstOrDefaultAsync(g => g.Id == gateId && g.ProjectId == projectId
                                 && g.Project!.TenantId == tenantId);
        if (gate == null) return NotFound();
        return Ok(gate);
    }

    [HttpPost]
    public async Task<ActionResult> Create(Guid projectId, [FromBody] StageGateRequest req)
    {
        var tenantId = GetTenantId();
        var projectOk = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!projectOk) return NotFound("Project not found");

        if (string.IsNullOrWhiteSpace(req.StageCode)) return BadRequest("stageCode is required");
        if (await _db.StageGates.AnyAsync(g => g.ProjectId == projectId && g.StageCode == req.StageCode))
            return Conflict(new { error = $"Stage gate '{req.StageCode}' already exists for this project" });

        var gate = new StageGate
        {
            ProjectId = projectId,
            StageCode = req.StageCode,
            StageName = req.StageName ?? req.StageCode,
            SortOrder = req.SortOrder,
            PlannedDate = req.PlannedDate,
            Description = req.Description,
            CriteriaJson = req.CriteriaJson,
            Status = "NOT_STARTED",
        };
        _db.StageGates.Add(gate);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CREATE", "StageGate", gate.Id.ToString());
        return CreatedAtAction(nameof(Get), new { projectId, gateId = gate.Id }, gate);
    }

    [HttpPut("{gateId}")]
    public async Task<ActionResult> Update(Guid projectId, Guid gateId, [FromBody] StageGateRequest req)
    {
        var tenantId = GetTenantId();
        var gate = await _db.StageGates
            .FirstOrDefaultAsync(g => g.Id == gateId && g.ProjectId == projectId
                                 && g.Project!.TenantId == tenantId);
        if (gate == null) return NotFound();

        if (req.StageName != null) gate.StageName = req.StageName;
        gate.SortOrder = req.SortOrder;
        gate.PlannedDate = req.PlannedDate;
        if (req.Description != null) gate.Description = req.Description;
        if (req.CriteriaJson != null) gate.CriteriaJson = req.CriteriaJson;
        gate.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("UPDATE", "StageGate", gate.Id.ToString());
        return Ok(gate);
    }

    [HttpPost("{gateId}/decide")]
    public async Task<ActionResult> Decide(Guid projectId, Guid gateId, [FromBody] StageGateDecisionRequest req)
    {
        var tenantId = GetTenantId();
        var gate = await _db.StageGates
            .FirstOrDefaultAsync(g => g.Id == gateId && g.ProjectId == projectId
                                 && g.Project!.TenantId == tenantId);
        if (gate == null) return NotFound();

        var decision = (req.Status ?? "").ToUpperInvariant();
        if (decision != "PASSED" && decision != "FAILED" && decision != "WAIVED")
            return BadRequest("status must be PASSED | FAILED | WAIVED");

        gate.Status = decision;
        gate.ActualDate = req.ActualDate ?? DateTime.UtcNow;
        gate.DecidedBy = User.FindFirst("display_name")?.Value ?? "Unknown";
        if (Guid.TryParse(User.FindFirst("user_id")?.Value, out var uid))
            gate.DecidedByUserId = uid;
        gate.DecidedAt = DateTime.UtcNow;
        gate.UpdatedAt = DateTime.UtcNow;
        if (req.CriteriaJson != null) gate.CriteriaJson = req.CriteriaJson;

        await _db.SaveChangesAsync();
        await _audit.LogAsync($"GATE_{decision}", "StageGate", gate.Id.ToString());

        _ = _notifications.NotifyProjectAsync(projectId, "stage_gate",
            $"{gate.StageCode} — {decision}",
            $"{gate.StageName} decided by {gate.DecidedBy}",
            new { gate.Id, gate.StageCode, gate.Status, projectId });

        return Ok(gate);
    }

    /// <summary>
    /// Phase 144 — convenience: seed the project with the eight RIBA Plan of
    /// Work 2020 stages (0–7). Idempotent — existing stage codes are skipped.
    /// Useful when a new project is created so the BIM Manager doesn't have
    /// to type out the timeline by hand.
    /// </summary>
    [HttpPost("seed-riba")]
    public async Task<ActionResult> SeedRiba(Guid projectId)
    {
        var tenantId = GetTenantId();
        var projectOk = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!projectOk) return NotFound("Project not found");

        var existingCodes = await _db.StageGates
            .Where(g => g.ProjectId == projectId)
            .Select(g => g.StageCode)
            .ToListAsync();

        var added = 0;
        foreach (var (code, name, order) in RibaStages)
        {
            if (existingCodes.Contains(code, StringComparer.OrdinalIgnoreCase)) continue;
            _db.StageGates.Add(new StageGate
            {
                ProjectId = projectId,
                StageCode = code,
                StageName = name,
                SortOrder = order,
                Status = "NOT_STARTED",
            });
            added++;
        }
        if (added > 0) await _db.SaveChangesAsync();
        return Ok(new { added, totalNow = existingCodes.Count + added });
    }

    private static readonly (string Code, string Name, int SortOrder)[] RibaStages =
    {
        ("RIBA-0", "Strategic Definition",   0),
        ("RIBA-1", "Preparation and Briefing", 1),
        ("RIBA-2", "Concept Design",          2),
        ("RIBA-3", "Spatial Coordination",    3),
        ("RIBA-4", "Technical Design",        4),
        ("RIBA-5", "Manufacturing and Construction", 5),
        ("RIBA-6", "Handover",                6),
        ("RIBA-7", "Use",                     7),
    };

    // ── Criterion sign-off (Phase 145) ──────────────────────────────────

    /// <summary>
    /// Phase 145 — list the structured criteria for a stage gate. Returns
    /// the parsed criteria array even when CriteriaJson is null/empty, so
    /// the mobile checklist can render an empty-state. Each criterion is
    /// the shape { key, label, description?, met, evidenceDocId?,
    /// signedBy?, signedAt?, comment? }.
    /// </summary>
    [HttpGet("{gateId}/criteria")]
    public async Task<ActionResult> ListCriteria(Guid projectId, Guid gateId)
    {
        var tenantId = GetTenantId();
        var gate = await _db.StageGates.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gateId && g.ProjectId == projectId
                                 && g.Project!.TenantId == tenantId);
        if (gate == null) return NotFound();

        var criteria = ParseCriteriaJson(gate.CriteriaJson);
        return Ok(new
        {
            gateId,
            stageCode = gate.StageCode,
            criteria,
            summary = new
            {
                total = criteria.Count,
                met = criteria.Count(c => c.Met),
                outstanding = criteria.Count(c => !c.Met),
            }
        });
    }

    /// <summary>
    /// Replace the criteria list. Used when the BIM Manager imports a new
    /// criteria template (e.g. RIBA Stage 3 default checks). The body is
    /// the full list; partial updates use the per-criterion sign-off
    /// endpoint below.
    /// </summary>
    [HttpPut("{gateId}/criteria")]
    public async Task<ActionResult> ReplaceCriteria(Guid projectId, Guid gateId, [FromBody] List<CriterionDto> criteria)
    {
        var tenantId = GetTenantId();
        var gate = await _db.StageGates
            .FirstOrDefaultAsync(g => g.Id == gateId && g.ProjectId == projectId
                                 && g.Project!.TenantId == tenantId);
        if (gate == null) return NotFound();

        // Reject duplicate keys — the per-criterion sign-off path keys by
        // `key`, so duplicates would silently shadow each other on PUT.
        var keys = criteria.Select(c => (c.Key ?? "").Trim()).Where(k => k.Length > 0).ToList();
        if (keys.Count != keys.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return BadRequest("criteria contains duplicate keys");
        if (keys.Count != criteria.Count)
            return BadRequest("every criterion must have a non-empty key");

        gate.CriteriaJson = SerializeCriteria(criteria);
        gate.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("REPLACE_CRITERIA", "StageGate", gate.Id.ToString());
        return Ok(new { gateId, criteria });
    }

    /// <summary>
    /// Sign off (or reset) a single criterion by key. The caller's display
    /// name is stamped on the row; <c>met=false</c> clears the signoff.
    /// Returns the updated criteria list so the mobile UI can re-render
    /// without re-fetching.
    /// </summary>
    [HttpPost("{gateId}/criteria/{key}/signoff")]
    public async Task<ActionResult> SignOffCriterion(
        Guid projectId, Guid gateId, string key, [FromBody] CriterionSignoffRequest req)
    {
        var tenantId = GetTenantId();
        var gate = await _db.StageGates
            .FirstOrDefaultAsync(g => g.Id == gateId && g.ProjectId == projectId
                                 && g.Project!.TenantId == tenantId);
        if (gate == null) return NotFound();

        var criteria = ParseCriteriaJson(gate.CriteriaJson);
        var match = criteria.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        if (match == null) return NotFound(new { error = $"Criterion '{key}' not found on this gate" });

        var actor = User.FindFirst("display_name")?.Value ?? "Unknown";
        match.Met = req.Met;
        match.Comment = req.Comment;
        match.EvidenceDocId = req.EvidenceDocId;
        if (req.Met)
        {
            match.SignedBy = actor;
            match.SignedAt = DateTime.UtcNow;
        }
        else
        {
            // Clearing — strip the signoff so a future "met=true" gets a
            // fresh timestamp / signer rather than the old one.
            match.SignedBy = null;
            match.SignedAt = null;
        }

        gate.CriteriaJson = SerializeCriteria(criteria);
        gate.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(req.Met ? "SIGNOFF_CRITERION" : "UNSIGN_CRITERION",
            "StageGate", $"{gate.Id}::{match.Key}",
            System.Text.Json.JsonSerializer.Serialize(new { match.Key, match.Met }));

        return Ok(new
        {
            gateId,
            criteria,
            summary = new
            {
                total = criteria.Count,
                met = criteria.Count(c => c.Met),
                outstanding = criteria.Count(c => !c.Met),
            }
        });
    }

    private static List<CriterionDto> ParseCriteriaJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<CriterionDto>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<CriterionDto>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<CriterionDto>();
        }
        catch
        {
            return new List<CriterionDto>();
        }
    }

    private static string SerializeCriteria(List<CriterionDto> criteria) =>
        System.Text.Json.JsonSerializer.Serialize(criteria, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

/// <summary>
/// Phase 145 — structured stage-gate criterion. Stored as an array under
/// <see cref="StageGate.CriteriaJson"/>. Validated by key uniqueness on
/// <c>PUT /criteria</c>.
/// </summary>
public class CriterionDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public bool Met { get; set; }
    public Guid? EvidenceDocId { get; set; }
    public string? SignedBy { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? Comment { get; set; }
}

public record CriterionSignoffRequest(bool Met, string? Comment, Guid? EvidenceDocId);

public record StageGateRequest(
    string StageCode,
    string? StageName,
    int SortOrder,
    DateTime? PlannedDate,
    string? Description,
    string? CriteriaJson
);

public record StageGateDecisionRequest(
    string Status,
    DateTime? ActualDate,
    string? CriteriaJson
);
