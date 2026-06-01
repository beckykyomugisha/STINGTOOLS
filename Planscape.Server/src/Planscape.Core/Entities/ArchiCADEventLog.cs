namespace Planscape.Core.Entities;

/// <summary>
/// Durable record of an ArchiCAD change event pushed via
/// <c>POST /api/archicad/{projectId}/push</c>. The live fan-out path keeps a
/// 200-event in-memory ring buffer per project (<c>ArchiCADEventBuffer</c>) for
/// late-join SignalR clients; that buffer is lost on every server restart.
/// This entity is the persistent backing store so
/// <c>GET /api/archicad/{projectId}/events/recent</c> can still answer after a
/// cold start (the controller falls back to the most recent DB rows when the
/// ring buffer is empty).
///
/// One row per pushed <c>ArchiCADEvent</c>. <see cref="PropertiesJson"/> holds
/// the event's property dict verbatim so the recent-events endpoint can
/// reconstruct the original event shape. BoundingBox is intentionally not
/// persisted (optional, geometry-heavy) — keep this log lean.
///
/// MIGRATION REQUIRED: dotnet ef migrations add ArchiCADEventLogPersistence
/// </summary>
public class ArchiCADEventLog : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>"Added" | "Changed" | "Deleted".</summary>
    public string Kind { get; set; } = "Changed";

    /// <summary>ArchiCAD element uuid (host-side id).</summary>
    public string ElementId { get; set; } = "";

    /// <summary>"Wall" | "Slab" | "Column" | … .</summary>
    public string ElementType { get; set; } = "";

    /// <summary>IFC GlobalId when StingBridge supplied one (cross-host key). Nullable.</summary>
    public string? IfcGlobalId { get; set; }

    /// <summary>The event's property dict, serialised verbatim. Nullable.</summary>
    public string? PropertiesJson { get; set; }

    /// <summary>Client-supplied event timestamp (ArchiCADEvent.TimestampUtc).</summary>
    public DateTime EventTimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Server receive time — the ordering key for "recent events".</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
