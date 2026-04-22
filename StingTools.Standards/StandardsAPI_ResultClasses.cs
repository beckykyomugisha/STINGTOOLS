using System;
using System.Collections.Generic;

namespace StingTools.Standards
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
    
    // FIRE PROTECTION RESULTS
    
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
