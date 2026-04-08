namespace Planscape.MIM.Entities;

/// <summary>
/// StingMIM Asset — a managed BIM asset with lifecycle, maintenance, and FM data.
/// Extends tagged elements with CAFM-ready fields for digital twin and O&M handover.
/// </summary>
public class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? TaggedElementId { get; set; } // Link to Planscape tagged element
    public string AssetTag { get; set; } = ""; // ISO 19650 tag (same as TAG1)
    public string AssetName { get; set; } = "";
    public string? Manufacturer { get; set; }
    public string? ModelNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? BarCode { get; set; }

    // Classification
    public string CategoryName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string? UniclassCode { get; set; }
    public string? OmniClassCode { get; set; }
    public string? CobieType { get; set; }
    public string? CobieSpace { get; set; }

    // STING tag tokens (mirrored from tagged element)
    public string Discipline { get; set; } = "";
    public string SystemCode { get; set; } = "";
    public string FunctionCode { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string Location { get; set; } = "";   // LOC token (BLD1, EXT, etc.)
    public string Level { get; set; } = "";       // LVL token (L01, GF, etc.)
    public string LifecycleStatus { get; set; } = "OPERATIONAL"; // OPERATIONAL, DECOMMISSIONED, DISPOSED, UNDER_REPAIR
    public string? CriticalityRating { get; set; } // CRITICAL, HIGH, MEDIUM, LOW

    // Lifecycle (ISO 15686)
    public DateTime? InstallationDate { get; set; }
    public DateTime? CommissioningDate { get; set; }
    public int? ExpectedLifeYears { get; set; }
    public DateTime? ExpectedReplacementDate { get; set; }
    public string ConditionGrade { get; set; } = "A"; // A-E (ISO 15686)
    public double? ConditionScore { get; set; } // 0-100

    // Warranty
    public string? WarrantyProvider { get; set; }
    public DateTime? WarrantyStart { get; set; }
    public DateTime? WarrantyEnd { get; set; }
    public int? WarrantyDurationMonths { get; set; }

    // Cost
    public decimal? CapitalCost { get; set; }
    public decimal? ReplacementCost { get; set; }
    public decimal? AnnualMaintenanceCost { get; set; }
    public string? CostCurrency { get; set; } = "GBP";

    // Spatial
    public string? Building { get; set; }
    public string? Floor { get; set; }
    public string? Room { get; set; }
    public string? Zone { get; set; }

    // IoT / Digital Twin
    public string? SensorId { get; set; }
    public string? DigitalTwinId { get; set; }
    public DateTime? LastSensorReading { get; set; }
    public string? SensorDataJson { get; set; }

    // Energy
    public double? EnergyConsumptionKwh { get; set; }
    public double? EmbodiedCarbonKgCo2 { get; set; }

    // Metadata
    public string? DocumentRefsJson { get; set; } // O&M manuals, certificates
    public string? SparePartsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public ICollection<MaintenanceTask> MaintenanceTasks { get; set; } = new List<MaintenanceTask>();
}
