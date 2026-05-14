namespace Planscape.Core.Entities;

/// <summary>
/// S6.2 — 3D markup polyline scene-anchored to a project model. Drawn by
/// coordinators on the mobile viewer: red-line annotations, dimension
/// callouts, sketches over the federated scene. Persists as JSON
/// polylines so re-rendering stays cheap on every viewer load.
///
/// One row per markup. The polyline JSON shape matches what
/// viewer-extras.js's startArea/finishArea path emits.
/// </summary>
public class ModelMarkup : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? ModelId { get; set; }
    public Guid? IssueId { get; set; }    // optional anchor — markup may belong to an issue
    public Guid? UserId { get; set; }

    public string? Label { get; set; }
    public string Color { get; set; } = "#E8912D";
    public float Thickness { get; set; } = 2f;

    /// <summary>JSON-encoded polylines: [{ points: [[x,y,z],...], color, thickness }, ...]</summary>
    public string PolylinesJson { get; set; } = "[]";

    /// <summary>Client-supplied idempotency key for replay-safe offline uploads.</summary>
    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}
