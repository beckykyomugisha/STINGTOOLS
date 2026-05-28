namespace Planscape.Core.Entities;

/// <summary>
/// Pillar A (3B, G13) — a replayable snapshot of a live meeting-viewer state.
/// Captures the full visual context (camera, K3 overlay profile, section
/// planes, phase filter, highlighted guids) as one JSON blob so a participant
/// who joins late — or anyone reviewing the minutes afterwards — can restore
/// exactly what the room was looking at when a decision was made.
/// </summary>
public class MeetingSnapshot : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid SessionId { get; set; }

    /// <summary>Human label, e.g. "Clash 42 — agreed re-route".</summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Opaque viewer-state JSON the front-end produced (camera/overlay/section/
    /// phase/highlights). The server stores and returns it verbatim — the
    /// viewer owns the shape, mirroring captureViewState/restoreViewState.
    /// </summary>
    public string StateJson { get; set; } = "{}";

    public string CapturedBy { get; set; } = "";
    public Guid? CapturedByUserId { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    public MeetingSession? Session { get; set; }
}
