namespace Planscape.Core.Entities;

/// <summary>
/// Pillar A (3A) — a LIVE 3D meeting-viewer session: the shared-camera,
/// shared-highlight room participants join from STING/BCC, web, or mobile.
/// Distinct from <see cref="Meeting"/> (the agenda/minutes record): a Meeting
/// MAY have a live session, but quick coordination sessions can exist without
/// a formal Meeting. Transient sync traffic (camera, highlight) flows over
/// <c>MeetingHub</c> and is never persisted; this row is the durable anchor
/// (who is host, which model, what base revision) the hub + controller need.
/// </summary>
public class MeetingSession : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Optional link to the formal Meeting this session backs.</summary>
    public Guid? MeetingId { get; set; }

    /// <summary>The participant whose camera followers track (follow-host).</summary>
    public Guid? HostUserId { get; set; }

    /// <summary>Model the room is viewing (FederatedModel / DocumentRecord id).</summary>
    public Guid? ModelId { get; set; }

    /// <summary>
    /// Model revision the session opened against — carried onto any PlatformEvent
    /// the meeting emits (e.g. deferred-clash → issue) so the K2 conflict guard
    /// can reject stale write-backs.
    /// </summary>
    public string? BaseRevisionId { get; set; }

    /// <summary>ACTIVE | ENDED.</summary>
    public string Status { get; set; } = "ACTIVE";

    /// <summary>
    /// WS3 (MeetingMedia) — what every client is currently looking at:
    /// <c>model</c> (the shared 3D viewer), <c>document</c> (a shared doc), or
    /// <c>screen</c> (a presenter's LiveKit screen-share). The SignalR hub owns
    /// this "active surface" state; LiveKit owns the A/V media plane.
    /// </summary>
    public string ActiveSurface { get; set; } = "model";

    /// <summary>When <see cref="ActiveSurface"/> is <c>document</c>, the shared DocumentRecord id.</summary>
    public Guid? ActiveDocumentId { get; set; }

    public string CreatedBy { get; set; } = "";
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    public Project? Project { get; set; }
    public ICollection<MeetingViewerParticipant> Participants { get; set; } = new List<MeetingViewerParticipant>();
}

/// <summary>A participant currently in (or recently in) a live meeting session.</summary>
public class MeetingViewerParticipant : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = "";

    /// <summary>This participant is the host (their camera drives followers).</summary>
    public bool IsHost { get; set; }

    /// <summary>This participant is following the host's camera.</summary>
    public bool IsFollowingHost { get; set; }

    /// <summary>Surface: revit | web | mobile.</summary>
    public string Surface { get; set; } = "";

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }

    public MeetingSession? Session { get; set; }
}
