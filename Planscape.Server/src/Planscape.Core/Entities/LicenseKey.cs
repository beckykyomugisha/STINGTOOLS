namespace Planscape.Core.Entities;

/// <summary>
/// License key for Revit plugin activation. Validated on plugin startup.
/// </summary>
public class LicenseKey : ITenantScoped,  ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Key { get; set; } = ""; // PLANSCAPE-XXXX-XXXX-XXXX-XXXX
    public LicenseTier Tier { get; set; }
    public bool MimEnabled { get; set; }
    public bool IsActive { get; set; } = true;
    public int MaxActivations { get; set; } = 1;
    public int CurrentActivations { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public string? ActivatedMachineIds { get; set; } // JSON array of machine fingerprints
    public string? LastActivatedBy { get; set; }
    public DateTime? LastActivatedAt { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
