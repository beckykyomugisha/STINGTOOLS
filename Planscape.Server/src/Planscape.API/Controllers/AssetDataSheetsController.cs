using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// T4-28 — Generic Asset Data Sheet engine.
///
///   GET    /api/projects/{pid}/asset-sheets               — list (filterable)
///   GET    /api/projects/{pid}/asset-sheets/{id}          — single
///   POST   /api/projects/{pid}/asset-sheets               — create from template
///   PUT    /api/projects/{pid}/asset-sheets/{id}          — update values
///   POST   /api/projects/{pid}/asset-sheets/{id}/submit   — Draft → Submitted
///   POST   /api/projects/{pid}/asset-sheets/{id}/approve  — Submitted → Approved
///   POST   /api/projects/{pid}/asset-sheets/{id}/reject   — Submitted → Rejected
///
/// Templates live under /asset-sheet-templates (separate controller).
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/asset-sheets")]
[Authorize]
[ProjectAccess]
public class AssetDataSheetsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IAuditService _audit;

    public AssetDataSheetsController(PlanscapeDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult> List(
        Guid projectId,
        [FromQuery] string? anchorKind = null,
        [FromQuery] string? anchorKey = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? templateId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var q = _db.AssetDataSheets.AsNoTracking().Where(s => s.ProjectId == projectId);
        if (anchorKind != null) q = q.Where(s => s.AnchorKind == anchorKind);
        if (anchorKey  != null) q = q.Where(s => s.AnchorKey  == anchorKey);
        if (status     != null) q = q.Where(s => s.Status     == status);
        if (templateId.HasValue) q = q.Where(s => s.TemplateId == templateId.Value);
        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(s => s.UpdatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new {
                s.Id, s.TemplateId, s.TemplateVersion,
                s.AnchorKind, s.AnchorKey,
                s.Status, s.CompletenessPct,
                s.AuthorName, s.CreatedAt, s.UpdatedAt
            })
            .ToListAsync(ct);
        return Ok(new { items = rows, total, page, pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> Get(Guid projectId, Guid id, CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var sheet = await _db.AssetDataSheets.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.ProjectId == projectId, ct);
        if (sheet == null) return NotFound();
        return Ok(sheet);
    }

    [HttpPost]
    public async Task<ActionResult> Create(
        Guid projectId,
        [FromBody] CreateAssetSheetRequest req,
        CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var template = await _db.AssetDataSheetTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == req.TemplateId && t.IsActive, ct);
        if (template == null) return BadRequest(new { error = "template_not_found_or_inactive" });

        var validation = ValidateValues(template.SchemaJson, req.ValuesJson ?? "{}");
        if (!validation.IsValid)
            return BadRequest(new { error = "validation_failed", errors = validation.Errors });

        var sheet = new AssetDataSheet
        {
            ProjectId       = projectId,
            TemplateId      = template.Id,
            TemplateVersion = template.Version,
            AnchorKind      = template.AnchorKind,
            AnchorKey       = req.AnchorKey,
            ValuesJson      = req.ValuesJson ?? "{}",
            CompletenessPct = validation.CompletenessPct,
            AuthorUserId    = CurrentUserIdOrNull(),
            AuthorName      = User.FindFirst("display_name")?.Value ?? "Unknown",
        };
        _db.AssetDataSheets.Add(sheet);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CREATE", "AssetDataSheet", sheet.Id.ToString(),
            JsonSerializer.Serialize(new { template.Code, sheet.AnchorKind, sheet.AnchorKey, sheet.CompletenessPct }));
        return CreatedAtAction(nameof(Get), new { projectId, id = sheet.Id }, sheet);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(
        Guid projectId, Guid id,
        [FromBody] UpdateAssetSheetRequest req,
        CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var sheet = await _db.AssetDataSheets.FirstOrDefaultAsync(
            s => s.Id == id && s.ProjectId == projectId, ct);
        if (sheet == null) return NotFound();
        if (sheet.Status is "Approved")
            return Conflict(new { error = "approved_immutable" });

        var template = await _db.AssetDataSheetTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == sheet.TemplateId, ct);
        if (template == null) return Conflict(new { error = "template_missing" });

        var validation = ValidateValues(template.SchemaJson, req.ValuesJson ?? "{}");
        if (!validation.IsValid)
            return BadRequest(new { error = "validation_failed", errors = validation.Errors });

        sheet.ValuesJson      = req.ValuesJson ?? "{}";
        sheet.CompletenessPct = validation.CompletenessPct;
        sheet.UpdatedAt       = DateTime.UtcNow;
        if (req.AnchorKey != null) sheet.AnchorKey = req.AnchorKey;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UPDATE", "AssetDataSheet", sheet.Id.ToString());
        return Ok(sheet);
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<ActionResult> Submit(Guid projectId, Guid id, CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var sheet = await _db.AssetDataSheets.FirstOrDefaultAsync(
            s => s.Id == id && s.ProjectId == projectId, ct);
        if (sheet == null) return NotFound();
        if (sheet.Status != "Draft") return Conflict(new { error = $"cannot_submit_{sheet.Status}" });
        if (sheet.CompletenessPct < 100)
            return BadRequest(new { error = "incomplete", completenessPct = sheet.CompletenessPct });
        sheet.Status      = "Submitted";
        sheet.SubmittedAt = DateTime.UtcNow;
        sheet.UpdatedAt   = sheet.SubmittedAt.Value;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("SUBMIT", "AssetDataSheet", sheet.Id.ToString());
        return Ok(sheet);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult> Approve(Guid projectId, Guid id, CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var sheet = await _db.AssetDataSheets.FirstOrDefaultAsync(
            s => s.Id == id && s.ProjectId == projectId, ct);
        if (sheet == null) return NotFound();
        if (sheet.Status != "Submitted") return Conflict(new { error = $"cannot_approve_{sheet.Status}" });
        sheet.Status            = "Approved";
        sheet.ApprovedAt        = DateTime.UtcNow;
        sheet.ApprovedByUserId  = CurrentUserIdOrNull();
        sheet.UpdatedAt         = sheet.ApprovedAt.Value;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("APPROVE", "AssetDataSheet", sheet.Id.ToString());
        return Ok(sheet);
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult> Reject(
        Guid projectId, Guid id,
        [FromBody] RejectAssetSheetRequest req,
        CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var sheet = await _db.AssetDataSheets.FirstOrDefaultAsync(
            s => s.Id == id && s.ProjectId == projectId, ct);
        if (sheet == null) return NotFound();
        if (sheet.Status != "Submitted") return Conflict(new { error = $"cannot_reject_{sheet.Status}" });
        sheet.Status         = "Rejected";
        sheet.RejectedReason = req.Reason;
        sheet.UpdatedAt      = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("REJECT", "AssetDataSheet", sheet.Id.ToString(),
            JsonSerializer.Serialize(new { req.Reason }));
        return Ok(sheet);
    }

    /// <summary>
    /// Validate a values JSON object against the template's schema.
    /// Checks: required-field presence, type coercion (number / bool /
    /// string / enum), enum membership. Returns per-field errors and a
    /// completeness percentage based on the count of required-fields-
    /// filled / total-required-fields.
    /// </summary>
    private static ValidationResult ValidateValues(string schemaJson, string valuesJson)
    {
        var errors = new List<object>();
        int requiredTotal = 0, requiredFilled = 0;
        try
        {
            using var schema = JsonDocument.Parse(schemaJson);
            using var values = JsonDocument.Parse(valuesJson);
            if (!schema.RootElement.TryGetProperty("fields", out var fields)
                || fields.ValueKind != JsonValueKind.Array)
            {
                return new ValidationResult(true, errors, 100);
            }
            foreach (var field in fields.EnumerateArray())
            {
                var key = field.GetProperty("key").GetString() ?? "";
                var type = field.TryGetProperty("type", out var t) ? t.GetString() : "string";
                var required = field.TryGetProperty("required", out var r) && r.GetBoolean();
                if (required) requiredTotal++;
                if (!values.RootElement.TryGetProperty(key, out var v))
                {
                    if (required) errors.Add(new { key, error = "required_missing" });
                    continue;
                }
                // Reject explicit null / empty string for required fields.
                if (required && (v.ValueKind == JsonValueKind.Null
                    || (v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString()))))
                {
                    errors.Add(new { key, error = "required_empty" });
                    continue;
                }
                // Type coerce — reject obvious mismatches.
                bool typed = type switch
                {
                    "number" => v.ValueKind == JsonValueKind.Number,
                    "boolean" => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    "enum" => v.ValueKind == JsonValueKind.String
                              && field.TryGetProperty("values", out var vs)
                              && vs.ValueKind == JsonValueKind.Array
                              && vs.EnumerateArray().Any(x => x.GetString() == v.GetString()),
                    _ => true, // string / unknown — accept
                };
                if (!typed) { errors.Add(new { key, error = $"type_mismatch_{type}" }); continue; }
                if (required) requiredFilled++;
            }
        }
        catch (Exception ex)
        {
            errors.Add(new { error = "schema_or_values_invalid", detail = ex.Message });
            return new ValidationResult(false, errors, 0);
        }
        var pct = requiredTotal == 0 ? 100 : (int)Math.Round(100.0 * requiredFilled / requiredTotal);
        return new ValidationResult(errors.Count == 0, errors, pct);
    }

    private Guid? CurrentUserIdOrNull()
    {
        var s = User.FindFirst("user_id")?.Value
             ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
             ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(s, out var id) ? id : null;
    }

    private sealed record ValidationResult(bool IsValid, List<object> Errors, int CompletenessPct);
}

public record CreateAssetSheetRequest(Guid TemplateId, string? AnchorKey, string? ValuesJson);
public record UpdateAssetSheetRequest(string? ValuesJson, string? AnchorKey);
public record RejectAssetSheetRequest(string Reason);
