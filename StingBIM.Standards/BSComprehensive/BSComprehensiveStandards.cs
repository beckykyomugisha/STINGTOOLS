using System;
using System.Collections.Generic;

namespace StingBIM.Standards.BSComprehensive
{
    /// <summary>
    /// Comprehensive British Standards for Construction
    /// BS 8000 (Workmanship), BS 5268 (Timber), BS 8004 (Foundations), BS 8102 (Waterproofing)
    /// 
    /// These standards are widely used across Commonwealth Africa including:
    /// Uganda, Kenya, Tanzania, Rwanda, Burundi, South Sudan, and many other former British colonies
    /// 
    /// Provides detailed guidance on quality workmanship, materials, and construction practices
    /// </summary>
    public static class BSComprehensiveStandards
    {
        #region BS 8000 - Workmanship on Building Sites

        /// <summary>
        /// BS 8000 workmanship standards - Part categories
        /// </summary>
        public enum BS8000Part
        {
            /// <summary>Part 0 - Introduction and general principles</summary>
            Part0_Introduction,
            /// <summary>Part 1 - Excavation and filling</summary>
            Part1_Excavation,
            /// <summary>Part 2 - Concrete work</summary>
            Part2_Concrete,
            /// <summary>Part 3 - Masonry</summary>
            Part3_Masonry,
            /// <summary>Part 4 - Waterproofing</summary>
            Part4_Waterproofing,
            /// <summary>Part 15 - Structural steelwork</summary>
            Part15_Steel
        }

        /// <summary>
        /// Excavation tolerances per BS 8000-1
        /// </summary>
        public static class ExcavationStandards
        {
            /// <summary>
            /// Gets permitted deviation for excavation level (mm)
            /// </summary>
            public static double GetLevelTolerance(string excavationType)
            {
                return excavationType.ToLower() switch
                {
                    "strip foundation" => 25,      // ±25mm
                    "pad foundation" => 25,        // ±25mm
                    "basement" => 50,              // ±50mm
                    "drainage trenches" => 25,     // ±25mm
                    "bulk excavation" => 75,       // ±75mm
                    _ => 50
                };
            }

            /// <summary>
            /// Gets required compaction for different fill materials
            /// </summary>
            public static (int NumberOfPasses, int LayerThickness) GetCompactionRequirements(
                string fillMaterial)
            {
                return fillMaterial.ToLower() switch
                {
                    "granular fill" => (4, 150),       // 4 passes, 150mm layers
                    "selected fill" => (4, 200),        // 4 passes, 200mm layers
                    "hardcore" => (3, 150),            // 3 passes, 150mm layers
                    "clayey soil" => (4, 100),         // 4 passes, 100mm layers
                    _ => (4, 150)
                };
            }

            /// <summary>
            /// Required field density ratio for compacted fill (%)
            /// </summary>
            public static double GetRequiredFieldDensityRatio(string location)
            {
                return location.ToLower() switch
                {
                    "under slabs" => 95,            // 95% maximum dry density
                    "under foundations" => 95,       // 95% maximum dry density
                    "under roads" => 98,            // 98% maximum dry density
                    "general backfill" => 90,       // 90% maximum dry density
                    _ => 95
                };
            }
        }

        /// <summary>
        /// Concrete workmanship standards per BS 8000-2
        /// </summary>
        public static class ConcreteWorkmanship
        {
            /// <summary>
            /// Maximum permitted deviation from specified level (mm)
            /// </summary>
            public static double GetLevelTolerance(string concreteElement)
            {
                return concreteElement.ToLower() switch
                {
                    "foundation" => 15,             // ±15mm
                    "floor slab" => 10,             // ±10mm
                    "columns" => 10,                // ±10mm
                    "beams" => 10,                  // ±10mm
                    "walls" => 10,                  // ±10mm
                    _ => 15
                };
            }

            /// <summary>
            /// Maximum slump variation from specified (mm)
            /// </summary>
            public static int GetPermittedSlumpVariation()
            {
                return 25; // ±25mm from specified slump
            }

            /// <summary>
            /// Minimum curing period (days) at different temperatures
            /// </summary>
            public static int GetMinimumCuringPeriod(double temperatureC, string cementType)
            {
                if (temperatureC < 5)
                {
                    return 10; // Extended curing at low temperatures
                }
                else if (temperatureC >= 20)
                {
                    return cementType.ToLower().Contains("rapid") ? 3 : 7;
                }
                else
                {
                    return 7; // Standard curing
                }
            }

            /// <summary>
            /// Maximum size of honeycombing defect requiring repair (mm)
            /// </summary>
            public static int GetMaximumHoneycombSize()
            {
                return 20; // Honeycombing > 20mm requires repair
            }

            /// <summary>
            /// Cover tolerance for reinforcement (mm)
            /// </summary>
            public static (double MinusTolerance, double PlusTolerance) GetCoverTolerance()
            {
                return (-5, 10); // -5mm / +10mm tolerance on specified cover
            }
        }

        /// <summary>
        /// Masonry workmanship standards per BS 8000-3
        /// </summary>
        public static class MasonryWorkmanship
        {
            /// <summary>
            /// Maximum permitted deviation from vertical (mm per m height)
            /// </summary>
            public static double GetVerticalityTolerance(string wallType)
            {
                return wallType.ToLower() switch
                {
                    "fair face" => 5,    // 5mm per m for fair-face work
                    "loadbearing" => 10,  // 10mm per m for load-bearing
                    "partition" => 10,    // 10mm per m for partitions
                    _ => 10
                };
            }

            /// <summary>
            /// Mortar joint thickness tolerances (mm)
            /// </summary>
            public static (int Minimum, int Maximum) GetJointThickness(string jointType)
            {
                return jointType.ToLower() switch
                {
                    "bed joints" => (8, 15),        // 8-15mm for bed joints
                    "perpend joints" => (8, 15),    // 8-15mm for perpends
                    "pointing" => (8, 12),          // 8-12mm for pointing
                    _ => (10, 15)
                };
            }

            /// <summary>
            /// Maximum bow or bulge in wall face (mm)
            /// </summary>
            public static double GetMaximumBow(double wallLength)
            {
                // Maximum bow = length/300 but not exceeding 20mm
                double calculatedBow = wallLength / 300;
                return Math.Min(calculatedBow, 20);
            }

            /// <summary>
            /// Required bond strength for mortar (N/mm²)
            /// </summary>
            public static double GetRequiredBondStrength(string exposureCondition)
            {
                return exposureCondition.ToLower() switch
                {
                    "internal" => 0.15,
                    "external" => 0.25,
                    "severe" => 0.35,
                    _ => 0.20
                };
            }
        }

        /// <summary>
        /// Waterproofing workmanship standards per BS 8000-4
        /// </summary>
        public static class WaterproofingWorkmanship
        {
            /// <summary>
            /// Minimum laps for waterproof membranes (mm)
            /// </summary>
            public static int GetMinimumLap(string membraneType)
            {
                return membraneType.ToLower() switch
                {
                    "bituminous felt" => 100,    // 100mm minimum lap
                    "plastic sheet" => 150,       // 150mm minimum lap
                    "liquid applied" => 0,        // No lap (continuous)
                    _ => 100
                };
            }

            /// <summary>
            /// Required number of membrane layers for waterproofing grade
            /// </summary>
            public static int GetRequiredLayers(int waterproofingGrade)
            {
                return waterproofingGrade switch
                {
                    1 => 1,  // Grade 1 - Low risk
                    2 => 2,  // Grade 2 - Medium risk
                    3 => 3,  // Grade 3 - High risk
                    4 => 3,  // Grade 4 - Specialist
                    _ => 2
                };
            }

            /// <summary>
            /// Substrate moisture content limits before membrane application (%)
            /// </summary>
            public static double GetMaximumSubstrateMoisture(string substrateType)
            {
                return substrateType.ToLower() switch
                {
                    "concrete" => 75,      // 75% RH maximum
                    "screed" => 75,        // 75% RH maximum
                    "masonry" => 5,        // 5% moisture content by weight
                    "timber" => 18,        // 18% moisture content
                    _ => 75
                };
            }
        }

        /// <summary>
        /// Structural steelwork standards per BS 8000-15
        /// </summary>
        public static class SteelworkWorkmanship
        {
            /// <summary>
            /// Fabrication tolerances for steel members (mm)
            /// </summary>
            public static double GetFabricationTolerance(string dimension, double value)
            {
                return dimension.ToLower() switch
                {
                    "length" when value <= 5000 => 3,       // ±3mm up to 5m
                    "length" when value > 5000 => 5,        // ±5mm over 5m
                    "straightness" => value / 1000,         // L/1000
                    "squareness" => 1.5,                    // ±1.5mm
                    _ => 3
                };
            }

            /// <summary>
            /// Permitted deviation in plumbness for columns (mm)
            /// </summary>
            public static double GetPlumbnessTolerance(double columnHeight)
            {
                // h/750 with maximum of 25mm
                double calculated = columnHeight / 750;
                return Math.Min(calculated, 25);
            }

            /// <summary>
            /// Bolt tightening methods and inspection requirements
            /// </summary>
            public static string[] GetBoltTighteningRequirements(string boltGrade)
            {
                if (boltGrade.ToLower().Contains("8.8") || boltGrade.ToLower().Contains("10.9"))
                {
                    return new[]
                    {
                        "Controlled tightening required",
                        "Torque wrench method or turn-of-nut method",
                        "100% inspection of critical connections",
                        "Record torque values or rotation",
                        "Use calibrated equipment"
                    };
                }
                else
                {
                    return new[]
                    {
                        "Snug tight acceptable for non-critical connections",
                        "Impact wrench permitted",
                        "Visual inspection of 10% sample"
                    };
                }
            }
        }

        #endregion

        #region BS 5268 - Structural Use of Timber

        /// <summary>
        /// Timber strength classes per BS 5268
        /// </summary>
        public enum TimberStrengthClass
        {
            /// <summary>C14 - Softwood, basic grade</summary>
            C14,
            /// <summary>C16 - Softwood, general purpose</summary>
            C16,
            /// <summary>C24 - Softwood, structural</summary>
            C24,
            /// <summary>D30 - Hardwood, lower grade</summary>
            D30,
            /// <summary>D40 - Hardwood, medium grade</summary>
            D40,
            /// <summary>D50 - Hardwood, higher grade</summary>
            D50,
            /// <summary>D70 - Hardwood, highest grade</summary>
            D70
        }

        /// <summary>
        /// Gets bending stress for timber strength class (N/mm²)
        /// </summary>
        public static double GetBendingStress(TimberStrengthClass strengthClass)
        {
            return strengthClass switch
            {
                TimberStrengthClass.C14 => 4.1,
                TimberStrengthClass.C16 => 5.3,
                TimberStrengthClass.C24 => 7.5,
                TimberStrengthClass.D30 => 9.0,
                TimberStrengthClass.D40 => 12.0,
                TimberStrengthClass.D50 => 15.0,
                TimberStrengthClass.D70 => 21.0,
                _ => 5.3
            };
        }

        /// <summary>
        /// Gets compression parallel to grain stress (N/mm²)
        /// </summary>
        public static double GetCompressionStress(TimberStrengthClass strengthClass)
        {
            return strengthClass switch
            {
                TimberStrengthClass.C14 => 6.8,
                TimberStrengthClass.C16 => 8.0,
                TimberStrengthClass.C24 => 9.7,
                TimberStrengthClass.D30 => 13.0,
                TimberStrengthClass.D40 => 16.0,
                TimberStrengthClass.D50 => 19.0,
                TimberStrengthClass.D70 => 26.0,
                _ => 8.0
            };
        }

        /// <summary>
        /// Modulus of elasticity (N/mm²)
        /// </summary>
        public static double GetModulusOfElasticity(TimberStrengthClass strengthClass)
        {
            return strengthClass switch
            {
                TimberStrengthClass.C14 => 6000,
                TimberStrengthClass.C16 => 8000,
                TimberStrengthClass.C24 => 10800,
                TimberStrengthClass.D30 => 9500,
                TimberStrengthClass.D40 => 11000,
                TimberStrengthClass.D50 => 14000,
                TimberStrengthClass.D70 => 20000,
                _ => 8000
            };
        }

        /// <summary>
        /// Load duration factor K3
        /// </summary>
        public static double GetLoadDurationFactor(string loadDuration)
        {
            return loadDuration.ToLower() switch
            {
                "long-term" => 1.0,         // Permanent load
                "medium-term" => 1.25,       // Snow load
                "short-term" => 1.5,        // Wind load
                "very short-term" => 1.75,  // Impact load
                _ => 1.0
            };
        }

        /// <summary>
        /// Service class moisture factors K2
        /// </summary>
        public static double GetServiceClassFactor(int serviceClass)
        {
            return serviceClass switch
            {
                1 => 1.0,    // Internal, dry
                2 => 0.9,    // External, protected
                3 => 0.75,   // External, exposed
                _ => 0.9
            };
        }

        /// <summary>
        /// Minimum bearing length for timber beams (mm)
        /// </summary>
        public static double GetMinimumBearingLength(double memberDepth, string supportType)
        {
            if (supportType.ToLower().Contains("steel"))
                return Math.Max(75, memberDepth / 3);
            else if (supportType.ToLower().Contains("concrete"))
                return Math.Max(90, memberDepth / 3);
            else // Timber on timber
                return Math.Max(100, memberDepth / 2);
        }

        #endregion

        #region BS 8004 - Foundations

        /// <summary>
        /// Foundation types per BS 8004
        /// </summary>
        public enum FoundationType
        {
            /// <summary>Strip foundation for walls</summary>
            Strip,
            /// <summary>Pad foundation for columns</summary>
            Pad,
            /// <summary>Raft foundation</summary>
            Raft,
            /// <summary>Piled foundation</summary>
            Piled,
            /// <summary>Combined foundation</summary>
            Combined
        }

        /// <summary>
        /// Presumed bearing capacity for soil types (kN/m²)
        /// </summary>
        public static double GetPresumedBearingCapacity(string soilType)
        {
            return soilType.ToLower() switch
            {
                "rock" or "solid bedrock" => 10000,
                "gravel" or "dense gravel" => 600,
                "sand" or "dense sand" => 300,
                "medium dense sand" => 200,
                "loose sand" => 100,
                "firm clay" => 150,
                "stiff clay" => 300,
                "very stiff clay" => 600,
                "soft clay" => 75,
                _ => 100 // Conservative default
            };
        }

        /// <summary>
        /// Minimum foundation depth below ground level (mm)
        /// </summary>
        public static double GetMinimumFoundationDepth(string soilType, string climateZone)
        {
            double baseDepth = soilType.ToLower() switch
            {
                "rock" => 450,
                "gravel" or "sand" => 600,
                "clay" => 900,
                _ => 750
            };

            // Increase for frost or shrinkage
            if (climateZone.ToLower().Contains("frost"))
                baseDepth = Math.Max(baseDepth, 900);

            return baseDepth;
        }

        /// <summary>
        /// Strip foundation width calculation (mm)
        /// </summary>
        public static double CalculateStripFoundationWidth(
            double wallLoad,           // kN/m
            double bearingCapacity,     // kN/m²
            double safetyFactor = 3.0)
        {
            double requiredWidth = (wallLoad * safetyFactor) / bearingCapacity * 1000;
            
            // Round up to nearest 50mm, minimum 450mm
            return Math.Max(450, Math.Ceiling(requiredWidth / 50) * 50);
        }

        /// <summary>
        /// Pad foundation size calculation (mm)
        /// </summary>
        public static (double Length, double Width) CalculatePadFoundation(
            double columnLoad,          // kN
            double bearingCapacity,     // kN/m²
            double safetyFactor = 3.0)
        {
            double requiredArea = (columnLoad * safetyFactor) / bearingCapacity;
            double side = Math.Sqrt(requiredArea) * 1000; // Convert to mm
            
            // Round up to nearest 100mm, minimum 900mm
            side = Math.Max(900, Math.Ceiling(side / 100) * 100);
            
            return (side, side);
        }

        /// <summary>
        /// Settlement limits for different structures (mm)
        /// </summary>
        public static (double Maximum, double Differential) GetPermittedSettlement(
            string structureType)
        {
            return structureType.ToLower() switch
            {
                "isolated foundation" => (50, 25),
                "raft" => (75, 50),
                "frame structure" => (50, 25),
                "loadbearing wall" => (75, 40),
                "portal frame" => (40, 20),
                _ => (50, 25)
            };
        }

        #endregion

        #region BS 8102 - Protection of Below-Ground Structures from Water

        /// <summary>
        /// Waterproofing grades per BS 8102
        /// </summary>
        public enum WaterproofingGrade
        {
            /// <summary>Grade 1 - Basic utility (some seepage tolerable)</summary>
            Grade1_Basic,
            /// <summary>Grade 2 - Better utility (seepage controlled)</summary>
            Grade2_Better,
            /// <summary>Grade 3 - Habitable (dry environment)</summary>
            Grade3_Habitable,
            /// <summary>Grade 4 - Special (archives, data centers)</summary>
            Grade4_Special
        }

        /// <summary>
        /// Gets waterproofing type required for grade
        /// </summary>
        public static string GetWaterproofingType(WaterproofingGrade grade)
        {
            return grade switch
            {
                WaterproofingGrade.Grade1_Basic => 
                    "Type A (Barrier) - Waterproof concrete only",
                WaterproofingGrade.Grade2_Better => 
                    "Type B (Structurally integral) - Concrete + internal drainage",
                WaterproofingGrade.Grade3_Habitable => 
                    "Type C (Drained cavity) - Cavity drainage membrane system",
                WaterproofingGrade.Grade4_Special => 
                    "Combination: Type A + Type B + Type C (triple protection)",
                _ => "Type B (Structurally integral)"
            };
        }

        /// <summary>
        /// Concrete specification for water-resisting construction
        /// </summary>
        public static class WaterResistingConcrete
        {
            /// <summary>Maximum water-cement ratio</summary>
            public const double MaxWaterCementRatio = 0.55;

            /// <summary>Minimum cement content (kg/m³)</summary>
            public const double MinCementContent = 325;

            /// <summary>Maximum aggregate size (mm)</summary>
            public const double MaxAggregateSize = 20;

            /// <summary>
            /// Minimum concrete cover for reinforcement (mm)
            /// </summary>
            public static double GetMinimumCover(string exposure)
            {
                return exposure.ToLower() switch
                {
                    "internal dry" => 40,
                    "external sheltered" => 50,
                    "external severe" => 60,
                    "aggressive soil" => 75,
                    _ => 50
                };
            }

            /// <summary>
            /// Required minimum thickness for water-resisting concrete (mm)
            /// </summary>
            public static double GetMinimumThickness(WaterproofingGrade grade)
            {
                return grade switch
                {
                    WaterproofingGrade.Grade1_Basic => 200,
                    WaterproofingGrade.Grade2_Better => 250,
                    WaterproofingGrade.Grade3_Habitable => 300,
                    WaterproofingGrade.Grade4_Special => 350,
                    _ => 250
                };
            }
        }

        /// <summary>
        /// Drainage requirements for below-ground structures
        /// </summary>
        public static class DrainageRequirements
        {
            /// <summary>
            /// Gets required sump capacity (liters)
            /// </summary>
            public static double GetSumpCapacity(double basementArea)
            {
                // Minimum 0.5L per m² of floor area, minimum 500L
                return Math.Max(500, basementArea * 0.5);
            }

            /// <summary>
            /// Required pump capacity (L/min)
            /// </summary>
            public static double GetPumpCapacity(double basementArea, double designRainfall)
            {
                // Based on area and design rainfall intensity
                // Conservative: 20L/min per 100m² floor area
                return Math.Max(100, (basementArea / 100) * 20);
            }

            /// <summary>
            /// Minimum diameter for drainage pipes (mm)
            /// </summary>
            public static int GetMinimumDrainPipeDiameter()
            {
                return 100; // 100mm minimum
            }
        }

        #endregion
    }
}
