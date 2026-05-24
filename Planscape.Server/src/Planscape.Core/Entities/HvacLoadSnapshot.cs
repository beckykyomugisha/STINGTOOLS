namespace Planscape.Core.Entities;

/// <summary>
/// HVAC block-load snapshot pushed from the desktop plugin's
/// BlockLoadEngine. One row per system (or "(building)" total) per
/// push. Used for sizing comparison + design-vs-as-built drift trends.
/// </summary>
public class HvacLoadSnapshot : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public string SystemId            { get; set; } = "";
    public string ClimateSiteId       { get; set; } = "";
    public string ClimateSiteLabel    { get; set; } = "";
    public string ConstructionProfile { get; set; } = "";
    public string RtsClass            { get; set; } = "Reactive";
    public bool   Cooling             { get; set; } = true;

    public double BlockSensibleW      { get; set; }
    public double BlockLatentW        { get; set; }
    public int    BlockHour           { get; set; }
    public double SumOfPeaksSensibleW { get; set; }
    public double DiversityFactor     { get; set; }
    public int    ZoneCount           { get; set; }

    /// <summary>JSON array of {zoneName, peakW, peakHour, areaM2, oaLs}.</summary>
    public string ZonesJson           { get; set; } = "";

    public DateTime CapturedAt        { get; set; } = DateTime.UtcNow;
    public string CapturedBy          { get; set; } = "";
    public string Source              { get; set; } = "PLUGIN";   // PLUGIN / MOBILE / API
}

/// <summary>
/// HVAC NC prediction record pushed from HvacNcPredictionCommand.
/// One row per duct-path prediction.
/// </summary>
public class HvacNcSnapshot : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public string PathLabel            { get; set; } = "";
    public string ReceiverRoom         { get; set; } = "";
    public int    PredictedNc          { get; set; }
    public int    TargetNc             { get; set; }
    public double PathFlowLs           { get; set; }
    public double PathPressureDropPa   { get; set; }

    /// <summary>JSON octave-band Lp at the receiver (8 values).</summary>
    public string OctaveLpJson         { get; set; } = "";
    /// <summary>JSON array of per-element atten + regen breakdown.</summary>
    public string ElementBreakdownJson { get; set; } = "";

    public DateTime CapturedAt         { get; set; } = DateTime.UtcNow;
    public string CapturedBy           { get; set; } = "";
}

/// <summary>
/// HVAC refrigerant pipe sizing record. Captures inputs + outputs so a
/// future audit can compare design intent vs as-installed.
/// </summary>
public class HvacRefrigerantSizing : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public string RefrigerantId        { get; set; } = "";   // R410A / R32 / R134a / CO2
    public string Leg                  { get; set; } = "";   // Suction / Discharge / Liquid
    public double CapacityKw           { get; set; }
    public double EquivLengthM         { get; set; }
    public double LiftM                { get; set; }
    public bool   HasVerticalRiser     { get; set; }
    public double MaxPressureDropKpa   { get; set; }
    public double SubcoolingReserveK   { get; set; }

    public bool   Ok                   { get; set; }
    public double SelectedBoreMm       { get; set; }
    public double VelocityMs           { get; set; }
    public double PressureDropKpa      { get; set; }
    public double LiftPenaltyKpa       { get; set; }
    public double SatTempDropK         { get; set; }
    public string WarningsJson         { get; set; } = "";

    public DateTime CapturedAt         { get; set; } = DateTime.UtcNow;
    public string CapturedBy           { get; set; } = "";
}
