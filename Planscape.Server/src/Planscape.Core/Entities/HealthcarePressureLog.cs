namespace Planscape.Core.Entities;

/// <summary>
/// Healthcare Pack H-22 — point-in-time pressure-cascade log entry.
/// Pushed by the desktop plugin BACnet/OPC-UA bridge or by the
/// commissioning team via the mobile app.
/// </summary>
public class HealthcarePressureLog : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string RoomBimId { get; set; } = "";

    /// <summary>
    /// IFC GlobalId of the room/space this reading belongs to — the
    /// cross-host identity key shared with <see cref="ExternalElementMapping"/>.
    /// Nullable: older clients (and BACnet bridges that only know the BIM
    /// id) omit it. When present, the
    /// <c>GET .../healthcare/by-ifc/{ifcGlobalId}</c> cross-reference joins
    /// this room's healthcare data to every host viewing the element.
    /// </summary>
    public string? RoomIfcGlobalId { get; set; }

    public string RoomName { get; set; } = "";
    public string RoomClass { get; set; } = "";
    public string DesignRegime { get; set; } = "";   // NEG / POS / NEUTRAL
    public double DesignDeltaPa { get; set; }
    public double LiveDeltaPa { get; set; }
    public bool InBand { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public string CapturedBy { get; set; } = "";
    public string Source { get; set; } = "BACNET";    // BACNET / OPC-UA / MANUAL
}
