using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// S6.1 — voice-note endpoints. The mobile app records via the device's
/// native speech-to-text (no server-side transcription cost), uploads
/// the audio + the transcript text. Server stores both; transcript is
/// indexable via the existing search controller for issue-detail full-
/// text matches.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/issues/{issueId:guid}/audio")]
[Authorize]
[ProjectAccess]
public class IssueAudioNotesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IFileStorageService _storage;

    public IssueAudioNotesController(PlanscapeDbContext db, ITenantContext tenant, IFileStorageService storage)
    {
        _db = db; _tenant = tenant; _storage = storage;
    }

    [HttpGet]
    public async Task<ActionResult> List(Guid issueId, CancellationToken ct)
    {
        var rows = await _db.IssueAudioNotes.AsNoTracking()
            .Where(n => n.IssueId == issueId)
            .OrderBy(n => n.CreatedAt)
            .Select(n => new {
                n.Id, n.IssueId, n.UserId,
                durationSec = n.DurationSeconds,
                n.TranscriptText, n.Language,
                url = $"/api/v1/projects/{n.IssueId}/issues/{issueId}/audio/{n.Id}/file",
                n.CreatedAt,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult> Upload(
        Guid projectId, Guid issueId,
        [FromForm] AudioNoteUploadRequest req,
        CancellationToken ct)
    {
        var file = req.File;
        if (file is null || file.Length == 0) return BadRequest(new { error = "empty file" });
        if (file.Length > 10 * 1024 * 1024) return BadRequest(new { error = "max 10 MB per voice note" });

        await using var stream = file.OpenReadStream();
        var path = await _storage.SaveScopedAsync(
            tenantId: _tenant.TenantId,
            projectId: projectId,
            fileName: $"audio/{issueId:N}/{Guid.NewGuid():N}.m4a",
            content: stream, ct: ct);

        var note = new IssueAudioNote
        {
            TenantId        = _tenant.TenantId,
            IssueId         = issueId,
            StoragePath     = path,
            TranscriptText  = req.TranscriptText,
            Language        = req.Language ?? "en",
            DurationSeconds = req.DurationSeconds,
            FileSizeBytes   = file.Length,
            MimeType        = file.ContentType ?? "audio/mp4",
        };
        _db.IssueAudioNotes.Add(note);
        await _db.SaveChangesAsync(ct);
        return Ok(new { note.Id, note.StoragePath });
    }

    [HttpGet("{noteId:guid}/file")]
    public async Task<ActionResult> File(Guid issueId, Guid noteId, CancellationToken ct)
    {
        var note = await _db.IssueAudioNotes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == noteId && n.IssueId == issueId, ct);
        if (note == null) return NotFound();
        var stream = await _storage.GetAsync(note.StoragePath, ct);
        if (stream == null) return NotFound();
        return File(stream, note.MimeType);
    }
}

public class AudioNoteUploadRequest
{
    public IFormFile File { get; set; } = default!;
    public string? TranscriptText { get; set; }
    public string? Language { get; set; }
    public int DurationSeconds { get; set; }
}
