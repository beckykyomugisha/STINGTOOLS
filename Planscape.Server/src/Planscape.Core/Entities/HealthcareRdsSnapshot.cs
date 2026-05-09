namespace Planscape.Core.Entities;

/// <summary>
/// Healthcare Pack H-22 — Room Data Sheet snapshot pushed from the
/// desktop plugin RDS engine. The mobile app reads this to render the
/// room in rds-viewer.tsx.
/// </summary>
public class HealthcareRdsSnapshot : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string RoomBimId { get; set; } = "";
    public string RoomNumber { get; set; } = "";
    public string RoomName { get; set; } = "";
    public string RoomClass { get; set; } = "";
    public string HbnRef { get; set; } = "";
    public string AdbCode { get; set; } = "";
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    /// <summary>JSON-encoded RDS context produced by RdsContextBuilder.
    /// Stored verbatim so the mobile viewer can render any token.</summary>
    public string ContextJson { get; set; } = "{}";
    public string DocxRelPath { get; set; } = ""; // relative to project _BIM_COORD/healthcare/rds/
}
