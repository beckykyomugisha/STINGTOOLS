namespace Planscape.Core.Entities;

/// <summary>
/// N2 — a server-side recording of a live <see cref="MeetingSession"/> produced by
/// LiveKit Egress (room-composite A/V, or audio-only track egress) and stored in the
/// object store (MinIO / S3). One row per start→stop. Linked to the session and (when
/// the session backs a formal <see cref="Meeting"/>) the meeting, so the recording
/// shows up in the meeting record + minutes alongside snapshots + actions (N5).
/// </summary>
public class MeetingRecording : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>The live session being recorded (room = session id).</summary>
    public Guid SessionId { get; set; }

    /// <summary>The formal Meeting this session backs, if any (for flow-back to the record).</summary>
    public Guid? MeetingId { get; set; }

    /// <summary>LiveKit egress id (returned by StartEgress); used to StopEgress + match webhooks.</summary>
    public string EgressId { get; set; } = "";

    /// <summary>room-composite | audio-only.</summary>
    public string Kind { get; set; } = "room-composite";

    /// <summary>STARTING | ACTIVE | STOPPING | COMPLETE | FAILED.</summary>
    public string Status { get; set; } = "STARTING";

    /// <summary>Object-store key/path of the produced file (set on egress_ended).</summary>
    public string? StorageKey { get; set; }
    public string? FileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public double? DurationSeconds { get; set; }

    /// <summary>Failure detail when Status == FAILED.</summary>
    public string? Error { get; set; }

    public string StartedBy { get; set; } = "";
    public Guid? StartedByUserId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
}
