#nullable enable
using Autodesk.Revit.DB;

namespace StingTools.BIMManager.PlatformEvents;

/// <summary>
/// K2 (STING side) — a handler that applies ONE platform event type to the
/// Revit model. Register implementations with <see cref="PlatformEventRegistry"/>.
/// The drainer guarantees the handler runs on the Revit API thread, inside a
/// transaction it opens, and only once per event id (idempotency + conflict
/// guard are enforced by the drainer, not the handler).
/// </summary>
public interface IApplyPlatformEvent
{
    /// <summary>Event <c>Type</c> this handler claims, e.g. "param.stamp".</summary>
    string EventType { get; }

    /// <summary>
    /// True if this event type is sensitive to the model revision — when true
    /// the drainer rejects the event if the live model has moved past the
    /// event's BaseRevisionId. False = safe to apply regardless of revision
    /// (e.g. raising an issue, which doesn't mutate geometry).
    /// </summary>
    bool RevisionSensitive { get; }

    /// <summary>
    /// Apply the event. Runs inside a Revit transaction opened by the drainer.
    /// Return Applied/Rejected/Failed; the drainer acks or rejects on the
    /// server accordingly. Throwing is treated as Failed (retryable).
    /// </summary>
    PlatformEventApplyResult Apply(Document doc, PlatformEventDto ev);
}

/// <summary>The plugin-side view of a server PlatformEvent.</summary>
public sealed class PlatformEventDto
{
    public System.Guid Id { get; set; }
    public long Sequence { get; set; }
    public string Source { get; set; } = "";
    public string Type { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public string? TargetIfcGlobalId { get; set; }
    public string? BaseRevisionId { get; set; }
}

public enum PlatformEventOutcome { Applied, Rejected, Failed }

public sealed class PlatformEventApplyResult
{
    public PlatformEventOutcome Outcome { get; init; }
    public string? Detail { get; init; }

    public static PlatformEventApplyResult Applied(string? detail = null) =>
        new() { Outcome = PlatformEventOutcome.Applied, Detail = detail };
    public static PlatformEventApplyResult Rejected(string reason) =>
        new() { Outcome = PlatformEventOutcome.Rejected, Detail = reason };
    public static PlatformEventApplyResult Failed(string reason) =>
        new() { Outcome = PlatformEventOutcome.Failed, Detail = reason };
}

/// <summary>
/// Supplies the live model revision for the drainer's conflict guard. Default
/// implementation (<see cref="NullModelRevisionProvider"/>) returns null
/// (revision-agnostic), which makes the guard a pass-through. Projects that
/// maintain a monotonic model revision plug a real provider via
/// <see cref="PlatformEventRegistry.RevisionProvider"/>.
/// </summary>
public interface IModelRevisionProvider
{
    string? GetCurrentRevision(Document doc);
}

public sealed class NullModelRevisionProvider : IModelRevisionProvider
{
    public string? GetCurrentRevision(Document doc) => null;
}
