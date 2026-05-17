namespace Planscape.Core.Entities;

/// <summary>
/// S6.1 — voice note attached to an issue / pin. The mobile app records
/// via the device's native speech-to-text and uploads the audio + a
/// transcript. Transcript surfaces in the issue detail page as searchable
/// text; audio plays back in the issue gallery.
///
/// Phase 178c (T3-19) — extended with <see cref="DocumentId"/>,
/// <see cref="TranscribedAt"/>, <see cref="CreatedBy"/> so the audio file
/// is persisted as a first-class <see cref="DocumentRecord"/> (CDE / scan
/// pipeline / signed-URL download all reuse the document plumbing) and the
/// server-side transcription stub records when a transcript was minted vs
/// uploaded by the client.
/// </summary>
public class IssueAudioNote : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid IssueId { get; set; }
    public Guid? UserId { get; set; }

    /// <summary>Storage path of the raw audio (legacy — kept for back-compat).</summary>
    public string StoragePath { get; set; } = "";

    /// <summary>
    /// Phase 178c — the audio file is also persisted as a DocumentRecord
    /// so the existing CDE / scan / signed-URL pipeline applies. Nullable
    /// for back-compat with rows minted before the column was added.
    /// </summary>
    public Guid? DocumentId { get; set; }

    public string? TranscriptText { get; set; }
    public string? Language { get; set; } // 'en' | 'sw' (Swahili) — for now
    public int DurationSeconds { get; set; }
    public long FileSizeBytes { get; set; }
    public string MimeType { get; set; } = "audio/mp4";

    /// <summary>
    /// When server-side transcription completed. Null = transcript was
    /// supplied by the client (device speech-to-text) or none yet.
    /// </summary>
    public DateTime? TranscribedAt { get; set; }

    /// <summary>Display name of the uploading user (display_name claim).</summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Client-supplied idempotency key (e.g. UUID v4 from the mobile app).
    /// Used to deduplicate retried uploads from unreliable connections.
    /// Nullable — omitted when the client does not supply one.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public DocumentRecord? Document { get; set; }
}
