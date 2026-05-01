namespace Planscape.Core.Entities;

/// <summary>
/// S6.1 — voice note attached to an issue / pin. The mobile app records
/// via the device's native speech-to-text and uploads the audio + a
/// transcript. Transcript surfaces in the issue detail page as searchable
/// text; audio plays back in the issue gallery.
/// </summary>
public class IssueAudioNote : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid IssueId { get; set; }
    public Guid? UserId { get; set; }

    public string StoragePath { get; set; } = "";
    public string? TranscriptText { get; set; }
    public string? Language { get; set; } // 'en' | 'sw' (Swahili) — for now
    public int DurationSeconds { get; set; }
    public long FileSizeBytes { get; set; }
    public string MimeType { get; set; } = "audio/mp4";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
