using System;
using System.Collections.Generic;

namespace StingBIM.Standards
{
    #region ALL RESULT CLASSES FOR AEC/FM CALCULATIONS
    
    // ELECTRICAL RESULTS
    
    public class PanelScheduleResult
    {
        public string Standard { get; set; }
        public double VoltageV { get; set; }
        public string PanelType { get; set; }
        public double ConnectedLoadKW { get; set; }
        public double ConnectedLoadA { get; set; }
        public double DemandFactor { get; set; }
        public double DemandLoadKW { get; set; }
        public double DemandLoadA { get; set; }
        public double DesignLoadA { get; set; }
        public double MainBreakerRatingA { get; set; }
        public double BusRatingA { get; set; }
        public int RequiredCircuits { get; set; }
        public int RequiredPoles { get; set; }
        public int AvailablePoles { get; set; }
        public double LoadUtilizationPercent { get; set; }
        public bool IsCompliant { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class FaultCurrentResult
    {
        public string Standard { get; set; }
        public double SourceVoltageV { get; set; }
        public double TransformerKVA { get; set; }
        public double BoltedFaultCurrentKA { get; set; }
        public double TransformerImpedanceOhm { get; set; }
        public double CableImpedanceOhm { get; set; }
        public double TotalImpedanceOhm { get; set; }
        public double ArcFlashIncidentEnergyCalPerCm2 { get; set; }
        public double ArcFlashBoundaryM { get; set; }
        public string RequiredPPECategory { get; set; }
    }
    
    // HVAC/MECHANICAL RESULTS
    
    public class HVACSizingResult
    {
        public string Standard { get; set; }
        public double FloorAreaM2 { get; set; }
        public string BuildingType { get; set; }
        public double EnvelopeLoadW { get; set; }
        public double VentilationLoadW { get; set; }
        public double OccupancyLoadW { get; set; }
        public double EquipmentLoadW { get; set; }
        public double LightingLoadW { get; set; }
        public double SensibleLoadW { get; set; }
        public double LatentLoadW { get; set; }
        public double TotalCoolingLoadKW { get; set; }
        public double TotalCoolingLoadTons { get; set; }
        public double RequiredAirflowCFM { get; set; }
        public double RequiredAirflowCMH { get; set; }
        public double LoadPerAreaWM2 { get; set; }
        public double RecommendedEquipmentSize { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class DuctSizingResult
    {
        public string Standard { get; set; }
        public double AirflowCFM { get; set; }
        public double AirflowCMH { get; set; }
        public double DuctLengthFt { get; set; }
        public double DuctLengthM { get; set; }
        public string DuctSize { get; set; }
        public double EquivalentDiameterIn { get; set; }
        public double ActualVelocityFPM { get; set; }
        public double VelocityPressureInWG { get; set; }
        public double FrictionLossInWG { get; set; }
        public double TotalPressureDropInWG { get; set; }
        public double TotalPressureDropPa { get; set; }
        public double EstimatedNoiseLevelDB { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class PsychrometricResult
    {
        public double DryBulbTempC { get; set; }
        public double WetBulbTempC { get; set; }
        public double DewPointTempC { get; set; }
        public double RelativeHumidityPercent { get; set; }
        public double HumidityRatio { get; set; }
        public double EnthalpyKJPerKg { get; set; }
        public double SpecificVolumeM3PerKg { get; set; }
        public double VaporPressureKPa { get; set; }
        public double AtmosphericPressureKPa { get; set; }
    }
    
    // PLUMBING RESULTS
    
    public class PlumbingResult
    {
        public string Standard { get; set; }
        public double StaticPressurePSI { get; set; }
        public double TotalWSFU { get; set; }
        public double EstimatedFlowGPM { get; set; }
        public string RecommendedPipeSize { get; set; }
        public double FrictionLossPSI { get; set; }
        public double ElevationLossPSI { get; set; }
        public double TotalPressureLossPSI { get; set; }
        public double AvailablePressurePSI { get; set; }
        public double VelocityFPS { get; set; }
        public bool IsCompliant { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class DrainageResult
    {
        public string Standard { get; set; }
        public bool IsStack { get; set; }
        public double TotalDFU { get; set; }
        public string RecommendedPipeSize { get; set; }
        public double PipeCapacityDFU { get; set; }
        public double UtilizationPercent { get; set; }
        public double SlopePer100Ft { get; set; }
        public double VelocityFPS { get; set; }
        public bool IsCompliant { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class WaterHeaterResult
    {
        public int OccupantCount { get; set; }
        public string BuildingType { get; set; }
        public double PeakHourlyDemandGallons { get; set; }
        public double RequiredFirstHourRating { get; set; }
        public double RecommendedTankSizeGallons { get; set; }
        public double RecoveryRateGPH { get; set; }
        public double InputRatingBTUH { get; set; }
        public double InputRatingKW { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    // STRUCTURAL RESULTS
    
    public class SteelBeamResult
    {
        public string Standard { get; set; }
        public double SpanM { get; set; }
        public double TotalLoadKN { get; set; }
        public string LoadType { get; set; }
        public string SteelGrade { get; set; }
        public double MaxMomentKNm { get; set; }
        public double RequiredSectionModulusCm3 { get; set; }
        public string SelectedSection { get; set; }
        public double SectionWeightKgPerM { get; set; }
        public double DeflectionMM { get; set; }
        public double AllowableDeflectionMM { get; set; }
        public bool DeflectionOK { get; set; }
        public double MaxShearKN { get; set; }
        public double ShearCapacityKN { get; set; }
        public bool ShearOK { get; set; }
        public bool IsCompliant { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class ConcreteBeamResult
    {
        public string Standard { get; set; }
        public double SpanM { get; set; }
        public double LoadKNPerM { get; set; }
        public double BeamWidthMM { get; set; }
        public double BeamDepthMM { get; set; }
        public double EffectiveDepthMM { get; set; }
        public double FactoredMomentKNm { get; set; }
        public double RequiredSteelAreaMM2 { get; set; }
        public string RebarSize { get; set; }
        public double MinimumSteelAreaMM2 { get; set; }
        public bool IsCompliant { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class FoundationResult
    {
        public string FoundationType { get; set; }
        public double ColumnLoadKN { get; set; }
        public double SoilBearingCapacityKPa { get; set; }
        public double FootingSizeM { get; set; }
        public double FootingThicknessMM { get; set; }
        public double FootingAreaM2 { get; set; }
        public double ActualBearingPressureKPa { get; set; }
        public double PunchingShearStressMPa { get; set; }
        public double AllowablePunchingStressMPa { get; set; }
        public bool IsCompliant { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class WindLoadResult
    {
        public string Standard { get; set; }
        public double BuildingHeightM { get; set; }
        public double BuildingWidthM { get; set; }
        public double BasicWindSpeedMPS { get; set; }
        public double VelocityPressurePa { get; set; }
        public double ExposureCoefficient { get; set; }
        public double DesignWindPressurePa { get; set; }
        public double DesignWindPressureKPa { get; set; }
        public double TotalWindForceKN { get; set; }
        public double OverturningMomentKNm { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class SeismicLoadResult
    {
        public string Standard { get; set; }
        public double BuildingWeightKN { get; set; }
        public double BuildingHeightM { get; set; }
        public string SoilType { get; set; }
        public double SiteCoeffFa { get; set; }
        public double SiteCoeffFv { get; set; }
        public double DesignSpectralAccelSDS { get; set; }
        public double DesignSpectralAccelSD1 { get; set; }
        public double ResponseModFactorR { get; set; }
        public double FundamentalPeriodSec { get; set; }
        public double SeismicRespCoeff { get; set; }
        public double BaseShearKN { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    // FIRE PROTECTION RESULTS
    
    public class SprinklerResult
    {
        public string Standard { get; set; }
        public double FloorAreaM2 { get; set; }
        public string OccupancyType { get; set; }
        public string HazardClassification { get; set; }
        public double DesignDensityLPMPerM2 { get; set; }
        public double DesignAreaM2 { get; set; }
        public double FlowRateLPM { get; set; }
        public double FlowRateGPM { get; set; }
        public double DesignPressureKPa { get; set; }
        public double DesignPressurePSI { get; set; }
        public int TotalSprinklers { get; set; }
        public int DesignSprinklers { get; set; }
        public int DurationMinutes { get; set; }
        public double TotalWaterDemandLiters { get; set; }
        public double TotalWaterDemandGallons { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class HydrantResult
    {
        public double PlotAreaM2 { get; set; }
        public string BuildingType { get; set; }
        public double BuildingHeightM { get; set; }
        public double RequiredFireFlowLPM { get; set; }
        public double FlowPerHydrantLPM { get; set; }
        public double HydrantSpacingM { get; set; }
        public int RequiredHydrants { get; set; }
        public double ResidualPressureKPa { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    // FM RESULTS
    
    public class SpaceUtilizationResult
    {
        public double GrossFloorAreaM2 { get; set; }
        public double NetUsableAreaM2 { get; set; }
        public double CirculationAreaM2 { get; set; }
        public int TotalSpaces { get; set; }
        public int TotalOccupants { get; set; }
        public double AreaPerPersonM2 { get; set; }
        public double NetToGrossRatio { get; set; }
        public double CirculationRatio { get; set; }
        public string SpaceTypeBreakdown { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class EquipmentLifecycleResult
    {
        public string EquipmentType { get; set; }
        public DateTime InstallDate { get; set; }
        public double PurchaseCost { get; set; }
        public int ExpectedLifeYears { get; set; }
        public int CurrentAgeYears { get; set; }
        public int RemainingLifeYears { get; set; }
        public DateTime ProjectedReplacementDate { get; set; }
        public double TotalMaintenanceCost { get; set; }
        public double TotalLifecycleCost { get; set; }
        public double AnnualDepreciation { get; set; }
        public double BookValue { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class EnergyAnalysisResult
    {
        public double FloorAreaM2 { get; set; }
        public string BuildingType { get; set; }
        public double AnnualElectricityKWH { get; set; }
        public double AnnualGasMJ { get; set; }
        public double TotalEnergyKWH { get; set; }
        public double EnergyUseIntensityKWHPerM2 { get; set; }
        public double BenchmarkEUI { get; set; }
        public double PerformanceRatio { get; set; }
        public double AnnualElectricityCost { get; set; }
        public double AnnualGasCost { get; set; }
        public double TotalEnergyCost { get; set; }
        public double EnergyCostPerM2 { get; set; }
        public double CarbonEmissionsTonnesCO2 { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    // CODE COMPLIANCE RESULTS
    
    public class EgressResult
    {
        public double FloorAreaM2 { get; set; }
        public int OccupantLoad { get; set; }
        public string OccupancyGroup { get; set; }
        public bool Sprinklered { get; set; }
        public double RequiredWidthMM { get; set; }
        public int RequiredExits { get; set; }
        public double MinSeparationDistanceM { get; set; }
        public double MaxTravelDistanceM { get; set; }
        public double MaxDeadEndCorridorM { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class AccessibilityResult
    {
        public double ElevationChangeM { get; set; }
        public int TotalParkingSpaces { get; set; }
        public double RequiredRampLengthM { get; set; }
        public double MaxRampSlope { get; set; }
        public int RequiredLandings { get; set; }
        public int RequiredAccessibleParking { get; set; }
        public int RequiredVanAccessibleSpaces { get; set; }
        public double MinAccessibleRouteWidthMM { get; set; }
        public int RequiredAccessibleToilets { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class ParkingResult
    {
        public double FloorAreaM2 { get; set; }
        public string BuildingUse { get; set; }
        public string LocationContext { get; set; }
        public double ParkingRatio { get; set; }
        public int RequiredSpaces { get; set; }
        public int AccessibleSpaces { get; set; }
        public double TotalParkingAreaM2 { get; set; }
        public int EVChargingSpaces { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    public class LightingResult
    {
        public string Standard { get; set; }
        public double FloorAreaM2 { get; set; }
        public string SpaceType { get; set; }
        public double RequiredLuxLevel { get; set; }
        public double TotalLumensRequired { get; set; }
        public int NumberOfFixtures { get; set; }
        public double LumensPerFixture { get; set; }
        public double TotalPowerW { get; set; }
        public double PowerDensityWPerM2 { get; set; }
        public bool MeetsStandard { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }
    
    #endregion
    
    #region DATA STRUCTURES FOR OFFLINE CALCULATIONS
    
    public class CableInfo
    {
        public string SizeMMOrAWG { get; set; }
        public double AmpacityA { get; set; }
        public double CSA_MM2 { get; set; }
        public string Description { get; set; }
        public double ShearCapacityKN { get; set; } // For structural members
    }
    
    public class CircuitLoad
    {
        public string CircuitName { get; set; }
        public double LoadKW { get; set; }
        public int Poles { get; set; }
        public string LoadType { get; set; }
    }
    
    public class PlumbingFixture
    {
        public string FixtureName { get; set; }
        public double WSFU { get; set; } // Water Supply Fixture Units
        public double DFU { get; set; } // Drainage Fixture Units
        public double MinimumPressurePSI { get; set; }
    }
    
    public class SpaceRecord
    {
        public string SpaceName { get; set; }
        public string SpaceType { get; set; }
        public double AreaM2 { get; set; }
        public int OccupantCount { get; set; }
    }
    
    public class StandardInfo
    {
        public string Code { get; set; }
        public string FullName { get; set; }
        public string Discipline { get; set; }
        public string IssuingBody { get; set; }
        public string Region { get; set; }
        public int YearPublished { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
    }
    
    public class SprinklerDesignCriteria
    {
        public double DensityLPMPerM2 { get; set; }
        public double DesignAreaM2 { get; set; }
        public double KFactor { get; set; }
    }
    
    public class SteelSection
    {
        public string Designation { get; set; }
        public double WeightKgPerM { get; set; }
        public double DepthMM { get; set; }
        public double WidthMM { get; set; }
        public double WebThicknessMM { get; set; }
        public double FlangeThicknessMM { get; set; }
        public double AreaCM2 { get; set; }
        public double MomentOfInertia_cm4 { get; set; }
        public double SectionModulusCm3 { get; set; }
        public double PlasticModulusCm3 { get; set; }
        public double ShearCapacityKN { get; set; }
    }
    
    #endregion
}
