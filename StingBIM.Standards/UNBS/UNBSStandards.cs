using System;
using System.Collections.Generic;
using System.Linq;

namespace StingBIM.Standards.UNBS
{
    /// <summary>
    /// Uganda National Bureau of Standards (UNBS) - Comprehensive Implementation
    /// Building codes, material specifications, and regulatory compliance for Uganda
    /// Standards: UNBS DUS 449, US 148, US 21, US 459 + KCCA, NEMA, NWSC, UMEME
    /// Updated: 2024
    /// </summary>
    public static class UNBSStandards
    {
        #region Room Dimensions (UNBS DUS 449)
        
        public static class RoomDimensions
        {
            public const double MinBedroomArea = 7.5; // m²
            public const double MinBedroomWidth = 2.4; // m
            public const double MinLivingRoomArea = 12.0; // m²
            public const double MinKitchenArea = 4.5; // m²
            public const double MinBathroomArea = 1.8; // m²
            public const double MinToiletArea = 1.2; // m²
            public const double MinCorridorWidth = 1.2; // m
            public const double MinStaircaseWidth = 0.9; // m
            public const double MinCeilingHeight = 2.4; // m
            
            public static bool ValidateRoom(string type, double area, double width, double height)
            {
                if (height < MinCeilingHeight) return false;
                
                switch (type.ToLower())
                {
                    case "bedroom": return area >= MinBedroomArea && width >= MinBedroomWidth;
                    case "living": return area >= MinLivingRoomArea;
                    case "kitchen": return area >= MinKitchenArea;
                    case "bathroom": return area >= MinBathroomArea;
                    case "toilet": return area >= MinToiletArea;
                    default: return true;
                }
            }
        }
        
        #endregion
        
        #region KCCA Building Setbacks
        
        public static class BuildingSetbacks
        {
            public static Dictionary<string, double> GetSetbacks(double plotArea, string zone, int floors)
            {
                var setbacks = new Dictionary<string, double>();
                bool isResidential = zone.ToUpper().Contains("RESIDENTIAL");
                
                if (isResidential)
                {
                    setbacks["Front"] = plotArea < 1000 ? 6.0 : 9.0;
                    setbacks["Rear"] = 6.0;
                    setbacks["Side1"] = 3.0;
                    setbacks["Side2"] = 3.0;
                }
                else if (zone.ToUpper().Contains("COMMERCIAL"))
                {
                    setbacks["Front"] = 9.0;
                    setbacks["Rear"] = 6.0;
                    setbacks["Side1"] = 4.5;
                    setbacks["Side2"] = 4.5;
                }
                else // Industrial
                {
                    setbacks["Front"] = 12.0;
                    setbacks["Rear"] = 9.0;
                    setbacks["Side1"] = 6.0;
                    setbacks["Side2"] = 6.0;
                }
                
                // Additional for multi-story
                if (floors > 2)
                {
                    double extra = (floors - 2) * 1.5;
                    setbacks["Front"] += extra;
                    setbacks["Rear"] += extra;
                }
                
                return setbacks;
            }
            
            public const double MaxPlotCoverageResidential = 60.0; // %
            public const double MaxPlotCoverageCommercial = 70.0; // %
        }
        
        #endregion
        
        #region Material Specifications
        
        public static class Cement
        {
            public const string Standard = "UNBS US 148 (EAS 18-1)";
            public const double MinStrength_OPC325 = 32.5; // MPa at 28 days
            public const double MinStrength_OPC425 = 42.5; // MPa
            public const double MinStrength_OPC525 = 52.5; // MPa
            public const double MaxChlorideContent = 0.1; // %
            public const int MinSettingTime = 45; // minutes
            public const int MaxSettingTime = 600; // minutes
            
            public static bool ValidateStrength(string grade, double actualStrength)
            {
                return grade switch
                {
                    "OPC 32.5" => actualStrength >= MinStrength_OPC325,
                    "OPC 42.5" => actualStrength >= MinStrength_OPC425,
                    "OPC 52.5" => actualStrength >= MinStrength_OPC525,
                    _ => false
                };
            }
        }
        
        public static class Bricks
        {
            public const string Standard = "UNBS US 21 (EAS 78)";
            public const double StandardLength = 225.0; // mm
            public const double StandardWidth = 112.5; // mm
            public const double StandardHeight = 75.0; // mm
            public const double MinCompressiveStrength = 3.5; // MPa
            public const double MaxWaterAbsorption = 20.0; // %
            
            public enum BrickClass
            {
                ClassA, // >10 MPa - Engineering
                ClassB, // >7 MPa - Load-bearing
                ClassC, // >3.5 MPa - Moderate load
                ClassD  // <3.5 MPa - Non-load bearing
            }
            
            public static BrickClass GetClass(double strength)
            {
                if (strength >= 10.0) return BrickClass.ClassA;
                if (strength >= 7.0) return BrickClass.ClassB;
                if (strength >= 3.5) return BrickClass.ClassC;
                return BrickClass.ClassD;
            }
        }
        
        public static class ReinforcementSteel
        {
            public const string Standard = "UNBS DUS 1847 (BS 4449)";
            public const double Grade_Y12 = 250.0; // MPa (Mild steel)
            public const double Grade_Y16 = 460.0; // MPa (High tensile)
            public const double Grade_Y20 = 500.0; // MPa (High yield)
            public const double MinElongation = 12.0; // %
            
            public static readonly double[] StandardDiameters = { 6, 8, 10, 12, 16, 20, 25, 32, 40 }; // mm
            
            public static bool IsStandardSize(double diameter)
            {
                return StandardDiameters.Any(d => Math.Abs(d - diameter) < 0.1);
            }
        }
        
        public static class RoofingSheets
        {
            public const string Standard = "UNBS US 459";
            public static readonly Dictionary<int, double> GaugeThickness = new Dictionary<int, double>
            {
                { 28, 0.40 }, { 30, 0.35 }, { 32, 0.30 }
            };
            public const double MinZincCoating = 100.0; // g/m²
            public static readonly double[] StandardLengths = { 6, 8, 10, 12 }; // feet
        }
        
        #endregion
        
        #region Electrical - UMEME Standards
        
        public static class Electrical
        {
            public const double NominalVoltage = 240.0; // V single-phase
            public const double ThreePhaseVoltage = 415.0; // V
            public const double Frequency = 50.0; // Hz
            public const double MaxEarthResistance = 10.0; // Ohms
            public const double MeterHeightMin = 1.5; // m
            public const double MeterHeightMax = 2.0; // m
            public const double ServiceClearance = 3.5; // m from ground
            
            public static double GetServiceCableSize(double loadKVA, int phases)
            {
                double current = phases == 3 
                    ? (loadKVA * 1000) / (Math.Sqrt(3) * ThreePhaseVoltage)
                    : (loadKVA * 1000) / NominalVoltage;
                
                current *= 1.25; // 25% safety factor
                
                if (current <= 20) return 2.5;
                if (current <= 25) return 4.0;
                if (current <= 32) return 6.0;
                if (current <= 40) return 10.0;
                if (current <= 50) return 16.0;
                if (current <= 63) return 25.0;
                if (current <= 80) return 35.0;
                if (current <= 100) return 50.0;
                if (current <= 125) return 70.0;
                if (current <= 160) return 95.0;
                return 120.0; // mm²
            }
            
            public static class Earthing
            {
                public const double MinElectrodeDepth = 2.4; // m
                public const double ElectrodeRodDiameter = 16.0; // mm
                public const double MinEarthingConductor = 6.0; // mm²
                
                public static int CalculateEarthRods(double targetResistance, double soilResistivity)
                {
                    double singleRodResistance = soilResistivity / (2 * Math.PI * MinElectrodeDepth);
                    return Math.Max(1, (int)Math.Ceiling(singleRodResistance / targetResistance));
                }
            }
        }
        
        #endregion
        
        #region Plumbing - NWSC Standards
        
        public static class Plumbing
        {
            public const double MinWaterPressure = 150.0; // kPa
            public const double MaxWaterPressure = 500.0; // kPa
            public const double PressurePerFloor = 30.0; // kPa
            
            public static double CalculatePumpHead(int floors, double elevation)
            {
                double staticHead = floors * 3.0;
                double pressureHead = MinWaterPressure / 9.81;
                double losses = staticHead * 0.15;
                return staticHead + pressureHead + losses + elevation;
            }
            
            public static class SepticTank
            {
                public const int MinRetentionDays = 2;
                public const double WastewaterPerPerson = 120.0; // liters/day
                public const double SludgePerPersonYear = 40.0; // liters/year
                
                public static double CalculateVolume(int occupants, int desludgingYears = 3)
                {
                    double liquid = occupants * WastewaterPerPerson * MinRetentionDays;
                    double sludge = occupants * SludgePerPersonYear * desludgingYears;
                    return (liquid + sludge) * 1.2; // 20% freeboard
                }
                
                public static (double L, double W, double D) GetDimensions(double volumeLiters)
                {
                    double volumeM3 = volumeLiters / 1000.0;
                    double depth = 1.5;
                    double area = volumeM3 / depth;
                    double width = Math.Sqrt(area / 2.0);
                    double length = width * 2.0;
                    return (Math.Ceiling(length * 2) / 2, Math.Ceiling(width * 2) / 2, depth);
                }
            }
            
            public static class Soakaway
            {
                public static double CalculateVolume(double dailyWastewater, string soilType)
                {
                    double factor = soilType.ToUpper() switch
                    {
                        "SAND" => 1.2,
                        "SANDY_LOAM" => 1.5,
                        "LOAM" => 2.0,
                        "CLAY_LOAM" => 3.0,
                        "CLAY" => 5.0,
                        _ => 2.0
                    };
                    return (dailyWastewater / 1000.0) * factor; // m³
                }
            }
            
            public static class RainwaterHarvesting
            {
                public const double AverageRainfallKampala = 1200.0; // mm/year
                
                public static double CalculateTankSize(double roofArea, double dailyDemand, int dryDays = 60)
                {
                    double annualHarvest = roofArea * (AverageRainfallKampala / 1000.0) * 0.8;
                    double drySeasonStorage = dailyDemand * dryDays;
                    return Math.Min(annualHarvest * 0.5, drySeasonStorage); // liters
                }
            }
        }
        
        #endregion
        
        #region Fire Safety
        
        public static class FireSafety
        {
            public const double MaxTravelDistance = 45.0; // m to exit
            public const double MinCorridorWidth = 1.2; // m
            public const double MinExitDoorWidth = 0.9; // m
            
            public static int GetRequiredExits(int occupancy)
            {
                if (occupancy <= 50) return 1;
                if (occupancy <= 500) return 2;
                if (occupancy <= 1000) return 3;
                return 4;
            }
            
            public static double GetFireResistance(int floors, string occupancyType)
            {
                if (floors <= 2) return 1.0;
                if (floors <= 4) return 2.0;
                return occupancyType.ToUpper().Contains("RESIDENTIAL") ? 2.0 : 4.0; // hours
            }
            
            public const double MaxDetectorSpacing = 7.5; // m
            public const double MaxCoveragePerDetector = 80.0; // m²
            public const int FireDetectionAboveFloors = 3;
        }
        
        #endregion
        
        #region NEMA Environmental Requirements
        
        public static class NEMA
        {
            public static bool RequiresEIA(string projectType, double builtArea)
            {
                string type = projectType.ToUpper();
                
                if (type.Contains("INDUSTRIAL") || type.Contains("FUEL_STATION") || 
                    type.Contains("HOSPITAL")) return true;
                if (type.Contains("HOTEL") && builtArea > 5000) return true;
                if (type.Contains("COMMERCIAL") && builtArea > 10000) return true;
                if (type.Contains("RESIDENTIAL") && builtArea > 20000) return true;
                
                return false;
            }
            
            public static double GetMinGreenSpace(string zone)
            {
                return zone.ToUpper() switch
                {
                    "RESIDENTIAL" => 0.30,
                    "COMMERCIAL" => 0.20,
                    "INDUSTRIAL" => 0.15,
                    "INSTITUTIONAL" => 0.40,
                    _ => 0.20
                };
            }
            
            public static class Parking
            {
                public static int GetRequiredSpaces(string buildingType, double area)
                {
                    return buildingType.ToUpper() switch
                    {
                        "RESIDENTIAL_APARTMENT" => (int)(area / 100.0),
                        "OFFICE" => (int)(area / 40.0),
                        "RETAIL" => (int)(area / 50.0),
                        "RESTAURANT" => (int)(area / 10.0),
                        "HOTEL" => (int)(area / 80.0),
                        _ => (int)(area / 100.0)
                    };
                }
                
                public const double ParkingLength = 5.0; // m
                public const double ParkingWidth = 2.5; // m
                public const double AisleWidth = 6.0; // m
            }
            
            public static class Waste
            {
                public const double MinWasteAreaPercent = 0.02; // 2% of built area
                public const double WastePerPersonDay = 0.5; // kg
                public const double RecyclingRequirement = 0.20; // 20%
            }
        }
        
        #endregion
        
        #region Accessibility
        
        public static class Accessibility
        {
            public const double MaxRampSlope = 1.0 / 12.0; // 8.33%
            public const double MinRampWidth = 1.2; // m
            public const double HandrailHeight = 0.9; // m
            public const double MinDoorClearOpening = 0.85; // m
            public const double GrabBarHeight = 0.75; // m
            public const double MinAccessibleToiletArea = 2.2; // m²
            public const double MinElevatorDoorWidth = 0.9; // m
            
            public static double CalculateRampLength(double height)
            {
                return height / MaxRampSlope;
            }
            
            public static bool RequiresAccessibility(string buildingType, int floors)
            {
                string type = buildingType.ToUpper();
                if (type.Contains("PUBLIC") || type.Contains("COMMERCIAL") || 
                    type.Contains("OFFICE") || type.Contains("INSTITUTIONAL"))
                    return true;
                
                if (type.Contains("RESIDENTIAL") && floors > 2) return true;
                
                return false;
            }
        }
        
        #endregion
        
        #region Structural
        
        public static class Structural
        {
            public static readonly Dictionary<string, double> SoilBearingCapacity = new Dictionary<string, double>
            {
                { "SOFT_CLAY", 75 }, { "FIRM_CLAY", 150 }, { "STIFF_CLAY", 300 },
                { "LOOSE_SAND", 100 }, { "MEDIUM_SAND", 200 }, { "DENSE_SAND", 400 },
                { "MURRAM", 250 }, { "LATERITE", 300 }, { "ROCK", 600 }
            }; // kPa
            
            public static double GetWindSpeed(string region)
            {
                return region.ToUpper() switch
                {
                    "KAMPALA" => 22.0, "ENTEBBE" => 25.0, "MBARARA" => 20.0,
                    "GULU" => 23.0, "MBALE" => 24.0, _ => 22.0
                }; // m/s
            }
            
            public const string SeismicZone = "Zone 0"; // Low seismic activity
            
            public static readonly Dictionary<string, double> FloorLiveLoads = new Dictionary<string, double>
            {
                { "RESIDENTIAL", 2.0 }, { "OFFICE", 3.0 }, { "CLASSROOM", 3.0 },
                { "ASSEMBLY", 4.0 }, { "RETAIL", 4.0 }, { "STORAGE", 5.0 },
                { "LIGHT_INDUSTRIAL", 5.0 }, { "HEAVY_INDUSTRIAL", 7.5 }
            }; // kPa
        }
        
        #endregion
        
        #region Climate
        
        public static class Climate
        {
            public const double AverageMinTemp = 17.0; // °C
            public const double AverageMaxTemp = 28.0; // °C
            public const double AverageHumidity = 75.0; // %
            public const double DesignRainfallIntensity = 50.0; // mm/hr
            public const double MinRoofOverhang = 0.6; // m
            public const double DPCHeight = 0.15; // m above ground
            
            public static double CalculateGutterSize(double roofArea)
            {
                double requiredArea = roofArea; // cm²
                double diameter = Math.Sqrt(4 * requiredArea / Math.PI);
                double[] sizes = { 100, 125, 150, 200 };
                return sizes.First(s => s >= diameter); // mm
            }
            
            public const double MinVentilationPercent = 0.05; // 5% of floor area
            public const double MinWindowOpeningPercent = 0.10; // 10% of floor area
            public const bool CrossVentilationRequired = true;
        }
        
        #endregion
        
        #region Quality Assurance
        
        public static class QualityAssurance
        {
            public static class ConcreteTesting
            {
                public const int MinCubesPerBatch = 3;
                public const double TestingFrequency = 1.0 / 20.0; // per 20m³
                public const int CuringPeriod = 28; // days
                public const double AcceptableVariation = 0.15; // ±15%
            }
            
            public static class SoilTesting
            {
                public const double MinTestPitDepth = 3.0; // m
                
                public static int GetRequiredTestPits(double plotArea)
                {
                    if (plotArea < 1000) return 2;
                    if (plotArea < 5000) return 3;
                    return 4;
                }
                
                public static readonly string[] RequiredTests = 
                {
                    "PARTICLE_SIZE_DISTRIBUTION", "ATTERBERG_LIMITS",
                    "MOISTURE_CONTENT", "COMPACTION_TEST", "CALIFORNIA_BEARING_RATIO"
                };
            }
        }
        
        #endregion
        
        #region Compliance Validation
        
        public class ComplianceResult
        {
            public bool IsCompliant { get; set; }
            public List<string> Violations { get; set; } = new List<string>();
            public List<string> Warnings { get; set; } = new List<string>();
            public List<string> Recommendations { get; set; } = new List<string>();
        }
        
        public static ComplianceResult ValidateCompliance(
            double plotArea, double builtArea, int floors, 
            string buildingType, string zone)
        {
            var result = new ComplianceResult { IsCompliant = true };
            
            // Plot coverage
            double coverage = (builtArea / plotArea) * 100;
            double maxCoverage = zone.ToUpper().Contains("RESIDENTIAL") 
                ? BuildingSetbacks.MaxPlotCoverageResidential 
                : BuildingSetbacks.MaxPlotCoverageCommercial;
            
            if (coverage > maxCoverage)
            {
                result.IsCompliant = false;
                result.Violations.Add($"Coverage {coverage:F1}% exceeds {maxCoverage}%");
            }
            
            // Green space
            double minGreen = NEMA.GetMinGreenSpace(zone);
            double actualGreen = (plotArea - builtArea) / plotArea;
            
            if (actualGreen < minGreen)
            {
                result.IsCompliant = false;
                result.Violations.Add($"Green space {actualGreen * 100:F1}% below {minGreen * 100:F0}%");
            }
            
            // EIA requirement
            if (NEMA.RequiresEIA(buildingType, builtArea))
                result.Warnings.Add("Project requires NEMA Environmental Impact Assessment");
            
            // Accessibility
            if (Accessibility.RequiresAccessibility(buildingType, floors))
                result.Recommendations.Add("Ensure accessibility features required");
            
            // Fire safety
            if (floors > FireSafety.FireDetectionAboveFloors)
                result.Recommendations.Add("Fire detection and alarm system required");
            
            return result;
        }
        
        #endregion
    }
}
