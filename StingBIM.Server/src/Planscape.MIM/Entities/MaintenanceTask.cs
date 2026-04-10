namespace Planscape.MIM.Entities;

/// <summary>
/// StingMIM Maintenance Task — PPM/reactive maintenance scheduling per BS 8210 / SFG20.
/// </summary>
public class MaintenanceTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public string TaskCode { get; set; } = ""; // e.g., PPM-HVAC-001
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public TaskType Type { get; set; } = TaskType.Preventive;
    public string Priority { get; set; } = "MEDIUM";
    public string Status { get; set; } = "SCHEDULED"; // SCHEDULED, IN_PROGRESS, COMPLETED, OVERDUE
    public string? AssignedTo { get; set; }

    // Scheduling
    public int FrequencyDays { get; set; } = 365; // PPM interval
    public DateTime? ScheduledDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public DateTime? NextDueDate { get; set; }

    // Compliance
    public string? StandardReference { get; set; } // e.g., "SFG20-AHU-001", "BS 8210 Table 3"
    public bool IsStatutory { get; set; } // Legal requirement
    public string? RegulatoryBody { get; set; }

    // Cost
    public decimal? EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }
    public double? EstimatedHours { get; set; }
    public double? ActualHours { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Asset? Asset { get; set; }
}

public enum TaskType
{
    Preventive,   // PPM — Planned Preventive Maintenance
    Corrective,   // Reactive repair
    Condition,    // Condition-based monitoring
    Statutory,    // Legal/regulatory requirement
    Emergency     // Urgent breakdown
}
