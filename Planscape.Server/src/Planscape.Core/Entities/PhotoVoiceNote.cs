namespace Planscape.Core.Entities;

/// <summary>
/// Phase 179 — Voice note attached to a <see cref="SitePhoto"/>.
/// Mirrors <see cref="IssueAudioNote"/> shape so the existing audio
/// playback / transcript / scan plumbing applies uniformly.
///
/// The audio bytes live as a <see cref="DocumentRecord"/> (CDE / scan
/// pipeline / signed-URL download all reuse the document plumbing). The
/// transcript is uploaded by the client (device speech-to-text) or
/// produced server-side later — <see cref="TranscribedAt"/> distinguishes.
/// </summary>
public class PhotoVoiceNote : ITenantScoped
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid PhotoId   { get; set; }
    public Guid? UserId   { get; set; }

    /// <summary>The DocumentRecord that owns the audio bytes.</summary>
    public Guid DocumentId { get; set; }

    public string?  TranscriptText  { get; set; }
    public string?  Language        { get; set; } // 'en' | 'sw'
    public int      DurationSeconds { get; set; }
    public long     FileSizeBytes   { get; set; }
    public string   MimeType        { get; set; } = "audio/mp4";
    public DateTime? TranscribedAt  { get; set; }
    public string?  CreatedBy       { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public SitePhoto?      Photo    { get; set; }
    public DocumentRecord? Document { get; set; }
}
