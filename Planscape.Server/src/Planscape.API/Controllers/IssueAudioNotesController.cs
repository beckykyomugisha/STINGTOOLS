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
///
/// Phase 178c (T3-19) extends with:
///   - DELETE /api/projects/{pid}/issues/{iid}/audio-notes/{aid}
///   - POST   /api/projects/{pid}/issues/{iid}/audio-notes/{aid}/transcribe
///     (stub — calls a TODO transcription service)
///   - audio is now persisted as a DocumentRecord (with FK on the row)
///   - watcher fan-out push on upload mirroring IssueAttachment behaviour
///   - .wav / .m4a / .webm / .mp3 / .ogg file types supported
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/issues/{issueId:guid}/audio-notes")]
[Authorize]
[ProjectAccess]
public class IssueAudioNotesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IFileStorageService _storage;
    private readonly IPushNotificationService _push;
    private readonly ILogger<IssueAudioNotesController> _logger;

    private const long MaxAudioBytes = 25 * 1024 * 1024; // 25 MB cap per note

    private static readonly HashSet<string> AllowedAudioMime = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/wav", "audio/x-wav", "audio/wave",
        "audio/mp4", "audio/m4a", "audio/x-m4a",
        "audio/webm",
        "audio/mpeg", "audio/mp3",
        "audio/ogg"
    };

    private static readonly HashSet<string> AllowedAudioExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".m4a", ".webm", ".mp3", ".ogg", ".oga"
    };

    public IssueAudioNotesController(
        PlanscapeDbContext db,
        ITenantContext tenant,
        IFileStorageService storage,
        IPushNotificationService push,
        ILogger<IssueAudioNotesController> logger)
    {
        _db = db; _tenant = tenant; _storage = storage; _push = push; _logger = logger;
    }

    /// <summary>List audio notes attached to an issue.</summary>
    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, Guid issueId, CancellationToken ct)
    {
        var rows = await _db.IssueAudioNotes.AsNoTracking()
            .Where(n => n.IssueId == issueId)
            .OrderBy(n => n.CreatedAt)
            .Select(n => new
            {
                n.Id,
                n.IssueId,
                n.UserId,
                n.DocumentId,
                durationSec = n.DurationSeconds,
                n.TranscriptText,
                n.Language,
                n.TranscribedAt,
                n.CreatedBy,
                url = $"/api/projects/{projectId}/issues/{issueId}/audio-notes/{n.Id}/file",
                n.MimeType,
                n.FileSizeBytes,
                n.CreatedAt,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>
    /// Upload a new audio note (multipart/form-data). Accepts .wav / .m4a /
    /// .webm / .mp3 / .ogg. Persists as both a row in IssueAudioNotes and a
    /// linked DocumentRecord so the existing CDE / scan plumbing applies.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxAudioBytes)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult> Upload(
        Guid projectId, Guid issueId,
        [FromForm] AudioNoteUploadRequest req,
        CancellationToken ct)
    {
        var file = req.File;
        var durationSeconds = req.DurationSeconds;
        if (file == null || file.Length == 0) return BadRequest(new { error = "empty_file" });
        if (file.Length > MaxAudioBytes) return BadRequest(new { error = "max_25mb_per_voice_note" });

        var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
        var mime = (file.ContentType ?? "audio/mp4").ToLowerInvariant();
        if (!AllowedAudioExt.Contains(ext) && !AllowedAudioMime.Contains(mime))
        {
            return BadRequest(new
            {
                error = "audio_format_not_supported",
                allowedExtensions = AllowedAudioExt,
                allowedMimeTypes = AllowedAudioMime
            });
        }

        var issue = await _db.Issues.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == issueId && i.ProjectId == projectId, ct);
        if (issue == null) return NotFound(new { error = "issue_not_found" });

        await using var stream = file.OpenReadStream();
        var fileName = $"audio/{issueId:N}/{Guid.NewGuid():N}{(string.IsNullOrEmpty(ext) ? ".m4a" : ext)}";
        var path = await _storage.SaveScopedAsync(
            tenantId: _tenant.TenantId,
            projectId: projectId,
            fileName: fileName,
            content: stream, ct: ct);

        var displayName = User.FindFirst("display_name")?.Value ?? "Unknown";

        // Persist as DocumentRecord so the file participates in the
        // CDE / scan / signed-URL pipeline like every other upload.
        var doc = new DocumentRecord
        {
            ProjectId      = projectId,
            FileName       = Path.GetFileName(path),
            FilePath       = path,
            DocumentType   = "AUDIO_NOTE",
            CdeStatus      = "WIP",
            SuitabilityCode = "S0",
            Discipline     = issue.Discipline,
            FileSizeBytes  = file.Length,
            UploadedBy     = displayName
        };
        _db.Documents.Add(doc);

        var note = new IssueAudioNote
        {
            TenantId        = _tenant.TenantId,
            IssueId         = issueId,
            DocumentId      = doc.Id,
            StoragePath     = path,
            TranscriptText  = req.TranscriptText,
            Language        = req.Language ?? "en",
            DurationSeconds = req.DurationSeconds,
            FileSizeBytes   = file.Length,
            MimeType        = string.IsNullOrEmpty(mime) ? "audio/mp4" : mime,
            CreatedBy       = displayName,
        };
        _db.IssueAudioNotes.Add(note);
        await _db.SaveChangesAsync(ct);

        // Watcher fan-out push (mirror IssueAttachment behaviour). Fire and
        // forget — never let push failure break the upload.
        try
        {
            var actorClaim = User.FindFirst("user_id")?.Value;
            Guid? actorId = Guid.TryParse(actorClaim, out var aid) ? aid : (Guid?)null;
            var audience = new HashSet<Guid>();
            if (issue.AssigneeUserId.HasValue) audience.Add(issue.AssigneeUserId.Value);
            foreach (var w in BimIssue.ParseWatcherIds(issue.WatcherUserIds)) audience.Add(w);
            if (actorId.HasValue) audience.Remove(actorId.Value);
            foreach (var uid in audience)
            {
                _ = _push.SendToUserAsync(uid, new PushPayload
                {
                    Title = $"🎙 {issue.IssueCode}",
                    Body  = $"New voice note ({durationSeconds}s)",
                    Channel = "issues",
                    Data = new Dictionary<string, string>
                    {
                        ["type"] = "issue_audio_note",
                        ["issueId"] = issueId.ToString(),
                        ["issueCode"] = issue.IssueCode,
                        ["audioNoteId"] = note.Id.ToString(),
                        ["projectId"] = projectId.ToString()
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watcher push fan-out failed for audio note {NoteId}", note.Id);
        }

        return Ok(new
        {
            note.Id, note.StoragePath, note.DocumentId,
            url = $"/api/projects/{projectId}/issues/{issueId}/audio-notes/{note.Id}/file",
            note.MimeType, note.FileSizeBytes, note.DurationSeconds, note.CreatedAt
        });
    }

    /// <summary>Stream the audio file back to the client.</summary>
    [HttpGet("{noteId:guid}/file")]
    public async Task<ActionResult> Download(Guid issueId, Guid noteId, CancellationToken ct)
    {
        var note = await _db.IssueAudioNotes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == noteId && n.IssueId == issueId, ct);
        if (note == null) return NotFound();
        var stream = await _storage.GetAsync(note.StoragePath, ct);
        if (stream == null) return NotFound();
        return File(stream, note.MimeType);
    }

    /// <summary>
    /// DELETE the audio note. Drops the row + the linked DocumentRecord +
    /// the file in storage.
    /// </summary>
    [HttpDelete("{noteId:guid}")]
    public async Task<ActionResult> Delete(Guid projectId, Guid issueId, Guid noteId, CancellationToken ct)
    {
        var note = await _db.IssueAudioNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.IssueId == issueId, ct);
        if (note == null) return NotFound();

        _db.IssueAudioNotes.Remove(note);

        // Remove the linked DocumentRecord too so we don't accumulate
        // orphan rows. Storage delete is best-effort.
        if (note.DocumentId.HasValue)
        {
            var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == note.DocumentId, ct);
            if (doc != null) _db.Documents.Remove(doc);
        }

        try { await _storage.DeleteAsync(note.StoragePath, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Storage delete failed for {Path}", note.StoragePath); }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Server-side transcription stub (T3-19). Calls a future transcription
    /// service (Whisper / Google Speech-to-Text / Azure Speech). For now,
    /// records the request and returns 202 Accepted with a TODO marker so
    /// the mobile UX can already show "Transcribing…" affordances. When
    /// the real service is wired up, this endpoint will queue a Hangfire
    /// job and update <see cref="IssueAudioNote.TranscriptText"/> +
    /// <see cref="IssueAudioNote.TranscribedAt"/> on completion.
    /// </summary>
    [HttpPost("{noteId:guid}/transcribe")]
    public async Task<ActionResult> Transcribe(Guid issueId, Guid noteId, CancellationToken ct)
    {
        var note = await _db.IssueAudioNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.IssueId == issueId, ct);
        if (note == null) return NotFound();

        // ───────────────────────────────────────────────────────────────
        // TODO(Phase 178c-followup): wire to a real transcription service.
        // For now we mark the row as "queued" by stamping TranscribedAt
        // only when a transcript already exists (client-supplied) — the
        // mobile UX can poll this field. A 202 Accepted signals queued.
        // ───────────────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(note.TranscriptText))
        {
            note.TranscribedAt ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(new
            {
                note.Id, note.TranscriptText, note.Language, note.TranscribedAt,
                source = "client_speech_to_text"
            });
        }

        _logger.LogInformation(
            "Transcription requested for audio note {NoteId} (server-side stub — no transcription service wired yet)",
            noteId);

        return Accepted(new
        {
            note.Id,
            status = "queued",
            todo = "server_side_transcription_service_pending",
            message = "Audio note queued for transcription. Result will be written to TranscriptText + TranscribedAt when the transcription service is wired up."
        });
    }
}

public class AudioNoteUploadRequest
{
    public IFormFile File { get; set; } = default!;
    public string? TranscriptText { get; set; }
    public string? Language { get; set; }
    public int DurationSeconds { get; set; }
}
