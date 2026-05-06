using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 142 — Daily Site Diary CRUD + lifecycle (DRAFT → SUBMITTED →
/// ACKNOWLEDGED → ARCHIVED). Mobile clients post the day's narrative,
/// weather, manpower count, equipment-on-site list, and free-text notes;
/// photos hang off the diary via <see cref="SiteDiaryAttachment"/> rows
/// that link to <see cref="DocumentRecord"/> uploads on the same project.
///
/// Notifications: submitting a diary fires a project-scoped push so
/// supervisors and clients see it without polling. Acknowledging triggers
/// no notification (the back-office surface is enough).
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/[controller]")]
[EnableRateLimiting("mobile")]
[Authorize]
[ProjectAccess]
public class SiteDiariesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;
    private readonly ILogger<SiteDiariesController> _logger;

    public SiteDiariesController(
        PlanscapeDbContext db,
        INotificationService notifications,
        IAuditService audit,
        ILogger<SiteDiariesController> logger)
    {
        _db = db;
        _notifications = notifications;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult> List(
        Guid projectId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        if (pageSize < 1) pageSize = 30;
        if (pageSize > 200) pageSize = 200;
        if (page < 1) page = 1;

        var query = _db.SiteDiaries.AsNoTracking().Where(d => d.ProjectId == projectId);
        if (from.HasValue) query = query.Where(d => d.DiaryDate >= from.Value);
        if (to.HasValue) query = query.Where(d => d.DiaryDate <= to.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(d => d.Status == status);

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(d => d.DiaryDate)
            .ThenByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                d.Id, d.DiaryDate, d.AuthorName, d.AuthorRole, d.Status,
                d.Weather, d.TemperatureCelsius, d.ManpowerCount,
                d.SubmittedAt, d.AcknowledgedAt, d.CreatedAt,
                attachmentCount = d.Attachments.Count
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, rows });
    }

    [HttpGet("{diaryId}")]
    public async Task<ActionResult> Get(Guid projectId, Guid diaryId)
    {
        var diary = await _db.SiteDiaries
            .AsNoTracking()
            .Include(d => d.Attachments)
            .ThenInclude(a => a.Document)
            .FirstOrDefaultAsync(d => d.Id == diaryId && d.ProjectId == projectId);
        if (diary == null) return NotFound();

        return Ok(new
        {
            diary.Id, diary.ProjectId, diary.DiaryDate,
            diary.AuthorUserId, diary.AuthorName, diary.AuthorRole,
            diary.Weather, diary.TemperatureCelsius, diary.WindSpeedKph, diary.RainfallMm,
            diary.ManpowerCount, diary.ManpowerByTradeJson,
            diary.EquipmentJson, diary.DeliveriesJson, diary.ChecklistJson,
            diary.Narrative, diary.VisitorsLog, diary.SafetyIncidents, diary.DelaysAndDisruption,
            diary.Status, diary.CreatedAt, diary.SubmittedAt,
            diary.AcknowledgedAt, diary.AcknowledgedBy,
            diary.Latitude, diary.Longitude,
            attachments = diary.Attachments.Select(a => new
            {
                a.Id, a.DocumentId, a.AttachedBy, a.AttachedAt, a.Caption,
                fileName = a.Document?.FileName,
                filePath = a.Document?.FilePath,
                contentHash = a.Document?.ContentHash
            })
        });
    }

    [HttpPost]
    public async Task<ActionResult> Create(Guid projectId, [FromBody] CreateSiteDiaryRequest req)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null) return NotFound("Project not found");

        var userId = GetUserId();
        var displayName = User.FindFirst("display_name")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            ?? "Unknown";

        // One diary per (project, date, author) — re-POSTing the same day's
        // entry by the same author updates rather than duplicates. Anything
        // more lenient produces noisy lists for the manager.
        var existing = await _db.SiteDiaries.FirstOrDefaultAsync(d =>
            d.ProjectId == projectId
            && d.DiaryDate.Date == req.DiaryDate.Date
            && (userId != Guid.Empty
                ? d.AuthorUserId == userId
                : d.AuthorName == displayName));

        if (existing != null && existing.Status == "DRAFT")
        {
            ApplyEditableFields(existing, req);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("UPDATE", "SiteDiary", existing.Id.ToString());
            return Ok(new { existing.Id, existing.Status, updated = true });
        }
        if (existing != null)
        {
            return Conflict(new
            {
                error = "Diary for this date already submitted; create a follow-up entry or unsubmit first.",
                diaryId = existing.Id, status = existing.Status
            });
        }

        var diary = new SiteDiary
        {
            ProjectId = projectId,
            DiaryDate = req.DiaryDate.Date,
            AuthorUserId = userId == Guid.Empty ? null : userId,
            AuthorName = displayName,
            AuthorRole = req.AuthorRole ?? "",
            Status = "DRAFT"
        };
        ApplyEditableFields(diary, req);
        _db.SiteDiaries.Add(diary);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CREATE", "SiteDiary", diary.Id.ToString());
        return CreatedAtAction(nameof(Get), new { projectId, diaryId = diary.Id }, new { diary.Id, diary.Status });
    }

    [HttpPut("{diaryId}")]
    public async Task<ActionResult> Update(Guid projectId, Guid diaryId, [FromBody] CreateSiteDiaryRequest req)
    {
        var diary = await _db.SiteDiaries.FirstOrDefaultAsync(d =>
            d.Id == diaryId && d.ProjectId == projectId);
        if (diary == null) return NotFound();
        if (diary.Status != "DRAFT")
            return Conflict(new { error = $"Diary is {diary.Status}; only DRAFT entries can be edited." });

        ApplyEditableFields(diary, req);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("UPDATE", "SiteDiary", diary.Id.ToString());
        return Ok(new { diary.Id, diary.Status });
    }

    [HttpPost("{diaryId}/submit")]
    public async Task<ActionResult> Submit(Guid projectId, Guid diaryId)
    {
        var diary = await _db.SiteDiaries.FirstOrDefaultAsync(d =>
            d.Id == diaryId && d.ProjectId == projectId);
        if (diary == null) return NotFound();
        if (diary.Status != "DRAFT")
            return Conflict(new { error = $"Cannot submit a {diary.Status} diary." });

        diary.Status = "SUBMITTED";
        diary.SubmittedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SUBMIT", "SiteDiary", diary.Id.ToString());

        // Project-scoped push so the team sees the day's report without polling.
        _ = _notifications.NotifyProjectAsync(projectId, "site_diary",
            $"Site diary submitted — {diary.DiaryDate:yyyy-MM-dd}",
            $"{diary.AuthorName} posted today's diary ({diary.ManpowerCount} on site).",
            new { diary.Id, diary.DiaryDate, diary.AuthorName, projectId });

        return Ok(new { diary.Id, diary.Status, diary.SubmittedAt });
    }

    [HttpPost("{diaryId}/acknowledge")]
    public async Task<ActionResult> Acknowledge(Guid projectId, Guid diaryId)
    {
        var diary = await _db.SiteDiaries.FirstOrDefaultAsync(d =>
            d.Id == diaryId && d.ProjectId == projectId);
        if (diary == null) return NotFound();
        if (diary.Status != "SUBMITTED")
            return Conflict(new { error = $"Cannot acknowledge a {diary.Status} diary." });

        diary.Status = "ACKNOWLEDGED";
        diary.AcknowledgedAt = DateTime.UtcNow;
        diary.AcknowledgedBy = User.FindFirst("display_name")?.Value ?? "Unknown";
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ACK", "SiteDiary", diary.Id.ToString());
        return Ok(new { diary.Id, diary.Status, diary.AcknowledgedAt, diary.AcknowledgedBy });
    }

    [HttpPost("{diaryId}/attachments/link")]
    public async Task<ActionResult> LinkAttachment(Guid projectId, Guid diaryId, [FromBody] LinkDiaryAttachmentRequest req)
    {
        var diary = await _db.SiteDiaries.FirstOrDefaultAsync(d =>
            d.Id == diaryId && d.ProjectId == projectId);
        if (diary == null) return NotFound();

        var doc = await _db.Documents.FirstOrDefaultAsync(d =>
            d.Id == req.DocumentId && d.ProjectId == projectId);
        if (doc == null) return BadRequest("Document does not belong to this project.");

        // Idempotent — re-linking the same document is a no-op rather than a 409.
        var already = await _db.SiteDiaryAttachments.AnyAsync(a =>
            a.SiteDiaryId == diaryId && a.DocumentId == req.DocumentId);
        if (already) return Ok(new { linked = false, message = "Already linked" });

        var att = new SiteDiaryAttachment
        {
            SiteDiaryId = diaryId,
            DocumentId = req.DocumentId,
            AttachedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            Caption = req.Caption
        };
        _db.SiteDiaryAttachments.Add(att);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ATTACH", "SiteDiary", diary.Id.ToString());
        return Ok(new { att.Id, att.SiteDiaryId, att.DocumentId, att.AttachedAt });
    }

    [HttpDelete("{diaryId}")]
    public async Task<ActionResult> Delete(Guid projectId, Guid diaryId)
    {
        var diary = await _db.SiteDiaries.FirstOrDefaultAsync(d =>
            d.Id == diaryId && d.ProjectId == projectId);
        if (diary == null) return NotFound();
        if (diary.Status != "DRAFT")
            return Conflict(new { error = $"Cannot delete a {diary.Status} diary; archive it instead." });

        _db.SiteDiaries.Remove(diary);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("DELETE", "SiteDiary", diaryId.ToString());
        return NoContent();
    }

    private static void ApplyEditableFields(SiteDiary diary, CreateSiteDiaryRequest req)
    {
        diary.DiaryDate = req.DiaryDate.Date;
        if (req.AuthorRole != null) diary.AuthorRole = req.AuthorRole;
        diary.Weather = req.Weather;
        diary.TemperatureCelsius = req.TemperatureCelsius;
        diary.WindSpeedKph = req.WindSpeedKph;
        diary.RainfallMm = req.RainfallMm;
        diary.ManpowerCount = req.ManpowerCount;
        diary.ManpowerByTradeJson = req.ManpowerByTradeJson;
        diary.EquipmentJson = req.EquipmentJson;
        diary.DeliveriesJson = req.DeliveriesJson;
        diary.Narrative = req.Narrative;
        diary.ChecklistJson = req.ChecklistJson;
        diary.VisitorsLog = req.VisitorsLog;
        diary.SafetyIncidents = req.SafetyIncidents;
        diary.DelaysAndDisruption = req.DelaysAndDisruption;
        diary.Latitude = req.Latitude;
        diary.Longitude = req.Longitude;
    }

    private Guid GetUserId() =>
        Guid.TryParse(
            User.FindFirst("user_id")?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id) ? id : Guid.Empty;
}

public record CreateSiteDiaryRequest(
    DateTime DiaryDate,
    string? AuthorRole,
    string? Weather,
    double? TemperatureCelsius,
    double? WindSpeedKph,
    double? RainfallMm,
    int ManpowerCount,
    string? ManpowerByTradeJson,
    string? EquipmentJson,
    string? DeliveriesJson,
    string? Narrative,
    string? ChecklistJson,
    string? VisitorsLog,
    string? SafetyIncidents,
    string? DelaysAndDisruption,
    double? Latitude,
    double? Longitude
);

public record LinkDiaryAttachmentRequest(Guid DocumentId, string? Caption);
