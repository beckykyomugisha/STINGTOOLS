// StingTools — HVAC load calc input data models.
//
// Inputs gathered from Revit Spaces (preferred) or Rooms (fallback) +
// envelope geometry + STING parameters. Used by BlockLoadEngine to
// compute hour-by-hour sensible/latent loads and peak-pick at the
// system level.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Hvac.Loads
{
    /// <summary>Thermal zone — typically one Revit Space, sometimes a
    /// merged set when zones share a thermostat.</summary>
    public class LoadZone
    {
        public string Id          { get; set; } = "";
        public string Name        { get; set; } = "";
        public string SystemId    { get; set; } = "";   // VAV box / FCU / system grouping
        public string SpaceTypeId { get; set; } = "Office";

        // Geometry
        public double FloorAreaM2 { get; set; }
        public double HeightM     { get; set; }
        public double VolumeM3    => FloorAreaM2 * HeightM;

        // Envelope segments (one per exterior wall/window/roof).
        public List<EnvelopeSegment> Envelope { get; } = new();

        // Internal gains
        public int    OccupantCount     { get; set; }
        /// <summary>Sensible heat per person at activity level, W. Default 75 W
        /// (seated, light activity, ASHRAE Handbook Fundamentals Ch. 18).</summary>
        public double OccupantSensibleW { get; set; } = 75;
        /// <summary>Latent heat per person, W. Default 55 W.</summary>
        public double OccupantLatentW   { get; set; } = 55;
        /// <summary>Lighting power density, W/m². ASHRAE 90.1 office default 7.6.</summary>
        public double LightingWPerM2    { get; set; } = 7.6;
        /// <summary>Equipment (plug) load density, W/m². Office default 8.0.</summary>
        public double EquipmentWPerM2   { get; set; } = 8.0;

        // Schedules — fraction (0..1) per hour-of-day (0..23). Default
        // is a 06:00–18:00 office occupancy ramp.
        public double[] OccupancySchedule { get; set; } = DefaultOfficeOccupancy();
        public double[] LightingSchedule  { get; set; } = DefaultOfficeLighting();
        public double[] EquipmentSchedule { get; set; } = DefaultOfficeEquipment();

        // Setpoints
        public double CoolingSetpointC { get; set; } = 24.0;
        public double HeatingSetpointC { get; set; } = 21.0;

        // Ventilation per ASHRAE 62.1 / CIBSE Guide B2: per-person +
        // per-area minimum outdoor air.
        public double OaLpsPerPerson { get; set; } = 10.0; // CIBSE office default
        public double OaLpsPerM2     { get; set; } = 0.3;
        public double OaLs => OccupantCount * OaLpsPerPerson + FloorAreaM2 * OaLpsPerM2;

        // Infiltration — air changes per hour at design conditions. May be
        // overridden hour-by-hour by the CIBSE Guide A §4.6 stack + wind
        // model below when Q4Pa and the climate site supply wind data.
        public double InfiltrationAch { get; set; } = 0.3;

        /// <summary>
        /// Q4Pa air permeability — leakage volume rate at 4 Pa reference
        /// pressure difference, m³/(h·m² of envelope area). UK Building
        /// Regs Part L 2021 sets typical caps:
        ///   Dwellings:           ≤ 5 m³/(h·m²) at 50 Pa → ~0.5 at 4 Pa
        ///   Commercial new-build:≤ 10 at 50 Pa  → ~1.0 at 4 Pa
        ///   Passivhaus:          ≤ 0.6 at 50 Pa → ~0.06 at 4 Pa
        /// When 0 (default), <see cref="InfiltrationAch"/> is used directly;
        /// when &gt; 0, BlockLoadEngine layers stack effect + wind pressure
        /// onto the reference Q4Pa via the CIBSE §4.6 model.
        /// </summary>
        public double Q4PaM3PerHperM2 { get; set; } = 0;

        /// <summary>
        /// Envelope area for the infiltration calc, m². When 0, the engine
        /// derives it from the Envelope segments (Σ AreaM2 for non-Window
        /// segments) — best-effort.
        /// </summary>
        public double InfiltrationEnvelopeAreaM2 { get; set; } = 0;

        public static double[] DefaultOfficeOccupancy() => new[]
        {
            0.0,0.0,0.0,0.0,0.0,0.05,0.1,0.2,0.8,0.95,0.95,0.95,0.5,0.95,0.95,0.95,0.7,0.4,0.1,0.05,0.05,0.0,0.0,0.0
        };
        public static double[] DefaultOfficeLighting() => new[]
        {
            0.05,0.05,0.05,0.05,0.05,0.1,0.2,0.5,0.9,0.95,0.95,0.95,0.8,0.95,0.95,0.95,0.8,0.5,0.2,0.1,0.05,0.05,0.05,0.05
        };
        public static double[] DefaultOfficeEquipment() => new[]
        {
            0.2,0.2,0.2,0.2,0.2,0.2,0.3,0.5,0.85,0.9,0.9,0.9,0.7,0.9,0.9,0.9,0.7,0.4,0.3,0.2,0.2,0.2,0.2,0.2
        };
    }

    public enum SegmentKind { ExteriorWall, Window, Roof, Floor, InteriorPartition }

    /// <summary>One exterior surface contributing conduction + (for
    /// glazing) solar heat gain.</summary>
    public class EnvelopeSegment
    {
        public SegmentKind Kind   { get; set; }
        public double AreaM2      { get; set; }
        /// <summary>Overall heat-transfer coefficient W/(m²·K). Wall 0.30,
        /// roof 0.20, window 1.4 are good Part L 2021 defaults.</summary>
        public double UvalueWm2K  { get; set; }
        /// <summary>Solar Heat Gain Coefficient (windows only). 0.4 typical.</summary>
        public double SHGC        { get; set; } = 0.4;
        /// <summary>Cardinal orientation in degrees from North (0=N, 90=E).</summary>
        public double OrientationDeg { get; set; }
        /// <summary>Optional shading factor (0..1). 1 = no shading, 0.5 = blinds.</summary>
        public double ShadingFactor { get; set; } = 1.0;

        /// <summary>
        /// Specific heat capacity per unit area, kJ/(m²·K) — Σ over layers of
        /// (ρ·c·thickness). Used by the Tier-2 per-zone RTS interpolator
        /// (Phase 187g) to derive Radiant Time Factors from actual layer
        /// detail rather than using a project-wide Light/Medium/Heavy class.
        ///
        /// Typical values:
        ///   Lightweight stud + gypsum wall:   ~40-80 kJ/m²K
        ///   Cavity brick / block wall:       ~150-250 kJ/m²K
        ///   Solid concrete wall (200 mm):    ~400-500 kJ/m²K
        ///   Concrete + masonry composite:    600+ kJ/m²K
        /// </summary>
        public double ThermalMassKJperM2K { get; set; } = 0;

        /// <summary>
        /// Tier-3 RTS construction-type id — when set + present in
        /// STING_CTF_COEFFICIENTS.json, the per-zone RTF is derived from
        /// the published Conduction Transfer Function Y-series rather than
        /// interpolated from thermal mass alone. Highest-fidelity RTS path
        /// shipped.
        /// </summary>
        public string ConstructionTypeId { get; set; }
    }

    /// <summary>Per-zone hourly load profile + peaks.</summary>
    public class ZoneLoadResult
    {
        public string ZoneId           { get; set; }
        public string ZoneName         { get; set; }
        public string SystemId         { get; set; }
        public double[] SensibleW      { get; set; }   // 24 hourly values
        public double[] LatentW        { get; set; }
        public double  PeakSensibleW   { get; set; }
        public double  PeakLatentW     { get; set; }
        public int     PeakHour        { get; set; }
        public double  AreaM2          { get; set; }
        public double  OaLs            { get; set; }

        /// <summary>
        /// Hourly OA L/s per ASHRAE 62.1 DCV — modulates per-person component
        /// against the occupancy schedule (per-area stays constant). The
        /// design-day max equals <see cref="OaLs"/>; the 24-hour AVERAGE is
        /// what DCV-equipped systems actually deliver. Null when DCV inputs
        /// aren't available (no occupants / no per-person OA / no schedule).
        /// </summary>
        public double[] HourlyOaLs     { get; set; }
        /// <summary>Average hourly OA over the design day, L/s. 0 when
        /// <see cref="HourlyOaLs"/> is null.</summary>
        public double  AverageOaLs     { get; set; }
        /// <summary>DCV savings vs. design-day max OA, % (0 = no savings,
        /// 50 % = OA averaged half the design-day max).</summary>
        public double  DcvSavingsPct   { get; set; }
    }

    /// <summary>Aggregated system / building load — block load picks
    /// the worst hour for the *system*, not the sum of per-zone peaks.</summary>
    public class BlockLoadResult
    {
        public string  SystemId            { get; set; }
        public double[] SystemSensibleW    { get; set; }
        public double[] SystemLatentW      { get; set; }
        public double  BlockSensibleW      { get; set; }
        public double  BlockLatentW        { get; set; }
        public int     BlockHour           { get; set; }
        public double  SumOfPeaksSensibleW { get; set; }
        /// <summary>Diversity = block / sum-of-peaks. &lt;1 means peaks
        /// don't coincide; sized plant should follow block, not sum.</summary>
        public double  DiversityFactor     => SumOfPeaksSensibleW > 0
            ? BlockSensibleW / SumOfPeaksSensibleW
            : 1.0;
        public List<ZoneLoadResult> Zones  { get; } = new();
    }
}
