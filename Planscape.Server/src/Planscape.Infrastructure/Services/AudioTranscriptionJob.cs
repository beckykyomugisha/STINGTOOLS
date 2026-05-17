using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 178 (Gap 1) — Hangfire job that runs server-side speech-to-text
/// transcription for a single <see cref="Planscape.Core.Entities.IssueAudioNote"/>
/// after the mobile client uploads the audio file.
///
/// The STT provider is intentionally stubbed: wire Azure Cognitive Services
/// Speech SDK or the OpenAI Whisper API behind <c>ITranscriptionProvider</c>
/// when a contract is in place. Until then the job marks the note as Done
/// with a placeholder transcript so the endpoint round-trips cleanly and
/// the mobile audio-player renders immediately.
///
/// Caller: <see cref="Planscape.API.Controllers.IssuesController.CaptureAudioNote"/>
/// enqueues via <c>IBackgroundJobClient.Enqueue&lt;AudioTranscriptionJob&gt;</c>.
/// </summary>
public class AudioTranscriptionJob
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<AudioTranscriptionJob> _log;

    public AudioTranscriptionJob(PlanscapeDbContext db, ILogger<AudioTranscriptionJob> log)
    {
        _db = db;
        _log = log;
    }

    [Hangfire.AutomaticRetry(Attempts = 3, OnAttemptsExceeded = Hangfire.AttemptsExceededAction.Delete)]
    public async Task TranscribeAsync(Guid noteId, CancellationToken ct)
    {
        // Hangfire jobs run without an HttpContext — bypass the global tenant
        // filter so the row is visible regardless of resolved TenantId.
        _db.BypassTenantFilter = true;

        var note = await _db.IssueAudioNotes.FindAsync(new object[] { noteId }, ct);
        if (note == null)
        {
            _log.LogWarning("AudioTranscriptionJob: note {NoteId} not found — skipping", noteId);
            return;
        }

        try
        {
            // TODO: wire real STT provider (Azure Cognitive / Whisper API) here.
            // For now, mark Done with a placeholder so the endpoint round-trips
            // cleanly and the mobile audio player renders without blocking.
            note.TranscribedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("AudioTranscriptionJob: note {NoteId} transcription stub complete", noteId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AudioTranscriptionJob: transcription failed for note {NoteId}", noteId);
            throw; // let Hangfire retry-policy kick in
        }
    }
}
