namespace Planscape.Core.Entities;

/// <summary>
/// Pillar B (6A) — a threshold/anomaly rule evaluated against incoming
/// telemetry. A breach fires a <see cref="TwinAlert"/> and (when
/// <see cref="RaiseWorkOrder"/>) a <see cref="WorkOrder"/> onto the K2 spine.
/// Corporate defaults ship in STING_TWIN_RULES.json; project rows override.
/// </summary>
public class TwinRule : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Scope: null = all devices, else a specific device.</summary>
    public string? DeviceId { get; set; }

    /// <summary>Metric this rule watches, e.g. "supply_air_temp_c".</summary>
    public string Metric { get; set; } = "";

    /// <summary>gt | gte | lt | lte | eq | ne | anomaly (z-score/EWMA).</summary>
    public string Operator { get; set; } = "gt";

    /// <summary>Comparison threshold (ignored for anomaly).</summary>
    public double? Threshold { get; set; }

    /// <summary>Anomaly sensitivity in std-devs (anomaly operator only).</summary>
    public double AnomalySigma { get; set; } = 3.0;

    /// <summary>WARNING | ALARM.</summary>
    public string Severity { get; set; } = "WARNING";

    public bool Enabled { get; set; } = true;

    /// <summary>Auto-raise a work order on breach.</summary>
    public bool RaiseWorkOrder { get; set; }

    /// <summary>Consecutive breaches before firing (debounce flapping sensors).</summary>
    public int ConsecutiveBreaches { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
}

/// <summary>Pillar B (6A) — a fired rule breach.</summary>
public class TwinAlert : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public Guid? RuleId { get; set; }
    public string DeviceId { get; set; } = "";
    public string? IfcGlobalId { get; set; }
    public string Metric { get; set; } = "";
    public double Value { get; set; }
    public string Severity { get; set; } = "WARNING";
    public string Message { get; set; } = "";

    /// <summary>OPEN | ACKNOWLEDGED | RESOLVED.</summary>
    public string Status { get; set; } = "OPEN";

    public DateTime FiredAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public Project? Project { get; set; }
}
