namespace Planscape.Core.Entities;

/// <summary>
/// Per-device sync watermark — records the last LastModifiedUtc the device
/// successfully pulled for a given project. The client passes its previous
/// watermark to the delta-sync GET endpoint and we only return elements
/// whose LastModifiedUtc (fallback: SyncedAt) is greater than it.
///
/// Key is (ProjectId, DeviceId): a device can cache a separate cursor per
/// project. "Device" is whatever the client sends in the X-Device-Id header
/// — a mobile device id, a desktop user id, or the literal "desktop" fallback.
/// </summary>
public class SyncWatermark : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Opaque device identifier from the X-Device-Id header.</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>
    /// The server-side cutoff time this device should pass back on its next
    /// pull to receive only the elements that have changed since this sync.
    /// Updated on every successful delta-sync response.
    /// </summary>
    public DateTime LastSyncUtc { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
}
