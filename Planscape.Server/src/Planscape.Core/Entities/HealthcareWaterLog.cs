namespace Planscape.Core.Entities;

/// <summary>
/// Healthcare Pack H-21 — HTM 04-01 sentinel-flush log entry.
/// Captured by the commissioning / water-safety team via the mobile app
/// (water-flush.tsx). Mirrors <see cref="HealthcarePressureLog"/> so the two
/// slices share the same tenant/project scoping, cross-host identity keys and
/// index shape.
/// </summary>
public class HealthcareWaterLog : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Optional room the outlet serves (BIM element id).</summary>
    public string RoomBimId { get; set; } = "";

    /// <summary>
    /// Optional IFC GlobalId of the room/space — the cross-host identity key
    /// shared with <see cref="ExternalElementMapping"/> and the pressure log.
    /// Nullable: clients that only know the outlet id omit it.
    /// </summary>
    public string? RoomIfcGlobalId { get; set; }

    public string RoomName { get; set; } = "";

    /// <summary>Outlet / TMV identifier being flushed (e.g. PLM-DCW-WD-0042).</summary>
    public string OutletId { get; set; } = "";

    /// <summary>Flush type: SENTINEL / ROUTINE / POST-WORKS [HTM 04-01].</summary>
    public string FlushType { get; set; } = "SENTINEL";

    /// <summary>Measured outlet temperature (°C) at the flush.</summary>
    public double TemperatureC { get; set; }

    /// <summary>Flush duration (seconds).</summary>
    public double DurationSec { get; set; }

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public string CapturedBy { get; set; } = "";
    public string Source { get; set; } = "MANUAL";   // MANUAL / BMS
}
