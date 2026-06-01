using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// FLEX-13 — Issue custom-field schema CRUD.
///
/// Admin/Owner-only. End users see the resolved schema via GET (authenticated)
/// and submit values on the standard Issues POST under the <c>customFields</c>
/// JSON object key.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/custom-fields")]
[Authorize]
[ProjectAccess]
public class IssueCustomFieldsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{0,78}[a-z0-9]$", RegexOptions.Compiled);

    public IssueCustomFieldsController(PlanscapeDbContext db) => _db = db;

    /// <summary>Return the active schema for rendering forms (end-user readable).</summary>
    [HttpGet]
    public async Task<ActionResult> Get(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var rows = await _db.IssueCustomFieldSchemas.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.IsActive && s.DeletedAt == null)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Label)
            .Select(s => new SchemaDto(
                s.Id, s.Key, s.Label, s.FieldType.ToString(), s.HelpText,
                s.DefaultValueJson, s.OptionsJson, s.Required, s.SortOrder))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<ActionResult<SchemaDto>> Create(Guid projectId, [FromBody] UpsertSchemaRequest req, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var error = Validate(req);
        if (error != null) return BadRequest(new { error });

        if (await _db.IssueCustomFieldSchemas
                .AnyAsync(s => s.ProjectId == projectId && s.Key == req.Key && s.DeletedAt == null, ct))
            return Conflict(new { error = "duplicate_key" });

        if (!Enum.TryParse<CustomFieldType>(req.FieldType, true, out var fieldType))
            return BadRequest(new { error = "invalid_field_type", allowed = Enum.GetNames<CustomFieldType>() });

        var row = new IssueCustomFieldSchema
        {
            ProjectId = projectId,
            Key = req.Key!,
            Label = req.Label!,
            FieldType = fieldType,
            HelpText = req.HelpText,
            DefaultValueJson = Normalise(req.DefaultValueJson),
            OptionsJson = Normalise(req.OptionsJson),
            Required = req.Required ?? false,
            SortOrder = req.SortOrder ?? NextSortOrder(projectId),
            UpdatedByUserId = CurrentUserId(),
        };
        _db.IssueCustomFieldSchemas.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(row));
    }

    [HttpPut("{fieldId:guid}")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<ActionResult<SchemaDto>> Update(Guid projectId, Guid fieldId, [FromBody] UpsertSchemaRequest req, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var error = Validate(req);
        if (error != null) return BadRequest(new { error });

        var row = await _db.IssueCustomFieldSchemas
            .FirstOrDefaultAsync(s => s.Id == fieldId && s.ProjectId == projectId, ct);
        if (row == null) return NotFound();

        // Decision 4.6 row 2 — in-place rename preserves data. Key can change
        // if it still matches the regex and is unique.
        if (!string.Equals(row.Key, req.Key, StringComparison.Ordinal) &&
            await _db.IssueCustomFieldSchemas
                .AnyAsync(s => s.ProjectId == projectId && s.Key == req.Key && s.Id != fieldId && s.DeletedAt == null, ct))
            return Conflict(new { error = "duplicate_key" });

        if (!Enum.TryParse<CustomFieldType>(req.FieldType, true, out var fieldType))
            return BadRequest(new { error = "invalid_field_type" });

        row.Key = req.Key!;
        row.Label = req.Label!;
        row.FieldType = fieldType;
        row.HelpText = req.HelpText;
        row.DefaultValueJson = Normalise(req.DefaultValueJson);
        row.OptionsJson = Normalise(req.OptionsJson);
        row.Required = req.Required ?? row.Required;
        row.SortOrder = req.SortOrder ?? row.SortOrder;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(row));
    }

    /// <summary>Soft-delete — archives values for 30 days before purge (decision 4.6 row 1).</summary>
    [HttpDelete("{fieldId:guid}")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> Delete(Guid projectId, Guid fieldId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var row = await _db.IssueCustomFieldSchemas
            .FirstOrDefaultAsync(s => s.Id == fieldId && s.ProjectId == projectId, ct);
        if (row == null) return NotFound();
        row.IsActive = false;
        row.DeletedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = CurrentUserId();
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Reorder multiple fields in one call.</summary>
    [HttpPost("reorder")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> Reorder(Guid projectId, [FromBody] ReorderCustomFieldsRequest req, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var rows = await _db.IssueCustomFieldSchemas
            .Where(s => s.ProjectId == projectId).ToListAsync(ct);
        var lookup = rows.ToDictionary(r => r.Id);
        foreach (var item in req.Items)
        {
            if (lookup.TryGetValue(item.Id, out var r))
            {
                r.SortOrder = item.SortOrder;
                r.UpdatedAt = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static string? Validate(UpsertSchemaRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Key)) return "key_required";
        if (!KeyPattern.IsMatch(req.Key)) return "invalid_key_format (snake_case, a-z0-9_, 2-80 chars)";
        if (string.IsNullOrWhiteSpace(req.Label)) return "label_required";
        if (string.IsNullOrWhiteSpace(req.FieldType)) return "field_type_required";
        return null;
    }

    private static string? Normalise(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { using var _ = JsonDocument.Parse(json); return json; }
        catch { return null; }
    }

    private int NextSortOrder(Guid projectId)
    {
        // 10-spaced so future inserts land without renumbering.
        var max = _db.IssueCustomFieldSchemas
            .Where(s => s.ProjectId == projectId)
            .Max(s => (int?)s.SortOrder) ?? 0;
        return max + 10;
    }

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        if (tenantId == Guid.Empty) return false;
        return await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;

    private static SchemaDto ToDto(IssueCustomFieldSchema s) =>
        new(s.Id, s.Key, s.Label, s.FieldType.ToString(), s.HelpText,
            s.DefaultValueJson, s.OptionsJson, s.Required, s.SortOrder);
}

public record UpsertSchemaRequest(
    string? Key,
    string? Label,
    string? FieldType,
    string? HelpText,
    string? DefaultValueJson,
    string? OptionsJson,
    bool? Required,
    int? SortOrder);

public record SchemaDto(
    Guid Id,
    string Key,
    string Label,
    string FieldType,
    string? HelpText,
    string? DefaultValueJson,
    string? OptionsJson,
    bool Required,
    int SortOrder);

public record ReorderCustomFieldsRequest(ReorderItem[] Items);
public record ReorderItem(Guid Id, int SortOrder);
