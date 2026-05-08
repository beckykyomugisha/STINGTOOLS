namespace Planscape.Core.Entities;

/// <summary>
/// Healthcare Pack H-22 — anti-ligature audit entry from the mobile app.
/// </summary>
public class HealthcareAntiLigatureAudit : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string RoomBimId { get; set; } = "";
    public string RoomName { get; set; } = "";
    public string FittingType { get; set; } = "";
    public bool Pass { get; set; }
    public string Notes { get; set; } = "";
    public string PhotoBlobId { get; set; } = "";    // optional MinIO blob id
    public double? GpsLat { get; set; }
    public double? GpsLon { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public string CapturedBy { get; set; } = "";
}
