namespace Planscape.Core.Entities;

/// <summary>
/// K2 keystone — the single durable, ordered, tenant-scoped channel for
/// EVERY cross-surface action that must reach the Revit model (mobile work
/// orders, web markups, meeting clash-resolutions, IoT twin alerts).
///
/// Flow: any surface appends a PlatformEvent → SignalR fans it out (live) +
/// the STING plugin polls as a fallback → the plugin's IExternalEventHandler
/// drains pending events, applies each under one Revit transaction, and acks.
///
/// Idempotency: the plugin keys on <see cref="Id"/> so replaying an event is
/// a no-op. Conflict safety: <see cref="BaseRevisionId"/> carries the model
/// revision the originator saw; the plugin rejects an event whose base is
/// stale rather than blindly applying (no last-writer-wins corruption).
///
/// Integrity: every row carries a SHA-256 <see cref="RowHash"/> chained to
/// the previous row's hash via <see cref="PrevHash"/> (per project), mirroring
/// the AuditLog chain, so the event log is tamper-evident.
/// </summary>
public class PlatformEvent : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Monotonic per-project sequence, assigned on append. Drives polling (?sinceSeq=).</summary>
    public long Sequence { get; set; }

    /// <summary>Originating surface: mobile | web | meeting | twin | revit | server.</summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Event type the plugin dispatches on, e.g. clash.resolved,
    /// issue.created, workorder.completed, param.stamp, twin.alert.
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>JSON payload — shape is per Type; the plugin handler deserialises.</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>Optional canonical IFC GlobalId the event targets (resolves via K1).</summary>
    public string? TargetIfcGlobalId { get; set; }

    /// <summary>
    /// The model revision the originator saw. The plugin rejects the event if
    /// the live model has moved past this (stale write) and surfaces a merge
    /// prompt. Null = revision-agnostic event (safe to apply anytime).
    /// </summary>
    public string? BaseRevisionId { get; set; }

    public PlatformEventStatus Status { get; set; } = PlatformEventStatus.Pending;

    /// <summary>Set when a handler rejects/fails — human-readable reason for the originator.</summary>
    public string? StatusDetail { get; set; }

    public Guid? ActorUserId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AppliedUtc { get; set; }

    // ── SHA-256 tamper-evidence chain (per project) ───────────────────────
    public string? PrevHash { get; set; }
    public string? RowHash { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Project? Project { get; set; }
}

public enum PlatformEventStatus
{
    /// <summary>Appended, not yet drained by the plugin.</summary>
    Pending = 0,
    /// <summary>Applied to the Revit model and acked.</summary>
    Applied = 1,
    /// <summary>Rejected (e.g. stale BaseRevisionId) — needs originator attention.</summary>
    Rejected = 2,
    /// <summary>Handler errored applying — retryable.</summary>
    Failed = 3,
}
