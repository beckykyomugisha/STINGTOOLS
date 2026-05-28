namespace Planscape.Core.Entities;

/// <summary>
/// Pillar B/C/D — a maintenance work order. Auto-raised from a twin alert
/// (6A) or a condition-based-maintenance schedule (6B), linked to the device
/// + the model element. Lifecycle flows through the K2 spine: creation can
/// pin a 3D marker; completion stamps an as-maintained Revit parameter
/// (Pillar D Seam 2) via an "param.stamp" / "workorder.completed" event.
/// </summary>
public class WorkOrder : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public string Code { get; set; } = ""; // WO-0001
    public Guid? DeviceTwinId { get; set; }
    public string? IfcGlobalId { get; set; }
    public Guid? AlertId { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Priority { get; set; } = "MEDIUM"; // CRITICAL | HIGH | MEDIUM | LOW
    public string Status { get; set; } = "OPEN";     // OPEN | IN_PROGRESS | COMPLETED | CANCELLED

    /// <summary>alert | cbm | manual.</summary>
    public string Source { get; set; } = "manual";

    public Guid? AssigneeUserId { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? CompletionNotes { get; set; }

    public Project? Project { get; set; }
}
