using Planscape.Core.Entities;

namespace Planscape.Core.Interfaces;

/// <summary>
/// K2 keystone — append/drain API for the platform event spine. Append
/// assigns the per-project sequence + chains the SHA-256 hash and fans out
/// over SignalR. The plugin drains via GetPending, applies, then Ack/Reject.
/// </summary>
public interface IPlatformEventService
{
    /// <summary>Append a new event (assigns Sequence + hash chain, fans out via SignalR).</summary>
    Task<PlatformEvent> AppendAsync(PlatformEventAppend cmd, CancellationToken ct = default);

    /// <summary>Pending events for a project after a given sequence (drain cursor).</summary>
    Task<IReadOnlyList<PlatformEvent>> GetPendingAsync(
        Guid projectId, long sinceSequence = 0, int max = 200, CancellationToken ct = default);

    /// <summary>Mark applied (idempotent — already-applied returns true without change).</summary>
    Task<bool> AckAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Mark rejected/failed with a reason (stale base, handler error).</summary>
    Task<bool> RejectAsync(Guid eventId, string reason, bool retryable = false, CancellationToken ct = default);
}

/// <summary>Append command — the minimal surface a caller supplies.</summary>
public sealed record PlatformEventAppend(
    Guid ProjectId,
    string Source,
    string Type,
    string PayloadJson,
    string? TargetIfcGlobalId = null,
    string? BaseRevisionId = null,
    Guid? ActorUserId = null);
