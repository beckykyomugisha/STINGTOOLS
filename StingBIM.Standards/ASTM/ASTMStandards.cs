using System;
using System.Collections.Generic;

namespace StingBIM.Standards.ASTM
{
    /// <summary>
    /// ASTM International (American Society for Testing and Materials)
    /// Global leader in development and delivery of voluntary consensus standards
    /// 
    /// Used worldwide for materials testing, quality control, and specifications
    /// Critical for Uganda, East Africa, and Liberia construction projects
    /// 
    /// Coverage: Cement, Steel, Concrete, Aggregates, Soil Testing
    /// </summary>
    public static class ASTMStandards
    {
        #region ASTM C150 - Portland Cement

        /// <summary>
        /// ASTM C150 Portland cement types
        /// </summary>
        public enum PortlandCementType
        {
            /// <summary>Type I - General purpose</summary>
            TypeI,
            /// <summary>Type II - Moderate sulfate resistance</summary>
            TypeII,
            /// <summary>Type III - High early strength</summary>
            TypeIII,
            /// <summary>Type IV - Low heat of hydration</summary>
            TypeIV,
            /// <summary>Type V - High sulfate resistance</summary>
            TypeV
        }

        /// <summary>
        /// Gets minimum compressive strength for cement type (psi)
        /// Returns (1-day, 3-day, 7-day, 28-day)
        /// </summary>
        public static (int Day1, int Day3, int Day7, int Day28) GetCementStrength(
            PortlandCementType type)
        {
            return type switch
            {
                PortlandCementType.TypeI => (0, 1450, 2470, 0), // Type I
                PortlandCementType.TypeII => (0, 1160, 2180, 0), // Type II
                PortlandCementType.TypeIII => (1740, 3480, 0, 0), // Type III - high early
                PortlandCementType.TypeIV => (0, 0, 1160, 2470), // Type IV - low heat
                PortlandCementType.TypeV => (0, 1160, 2180, 0), // Type V
                _ => (0, 0, 0, 0)
            };
        }

        /// <summary>
        /// Chemical requirements for cement (% by mass)
        /// </summary>
        public static (double MaxC3A, double MaxSO3) GetChemicalLimits(PortlandCementType type)
        {
            return type switch
            {
                PortlandCementType.TypeII => (8.0, 3.0),  // Moderate sulfate resistance
                PortlandCementType.TypeV => (5.0, 2.3),   // High sulfate resistance
                _ => (15.0, 3.0) // General limits
            };
        }

        #endregion

        #region ASTM A615 - Deformed Steel Reinforcement

        /// <summary>
        /// ASTM A615 reinforcement grades
        /// </summary>
        public enum RebarGrade
        {
            /// <summary>Grade 40 - 40,000 psi yield</summary>
            Grade40,
            /// <summary>Grade 60 - 60,000 psi yield (most common)</summary>
            Grade60,
            /// <summary>Grade 75 - 75,000 psi yield</summary>
            Grade75,
            /// <summary>Grade 80 - 80,000 psi yield</summary>
            Grade80
        }

        /// <summary>
        /// Gets yield and tensile strength for rebar grade (psi)
        /// </summary>
        public static (int YieldStrength, int TensileStrength) GetRebarStrength(RebarGrade grade)
        {
            return grade switch
            {
                RebarGrade.Grade40 => (40000, 60000),
                RebarGrade.Grade60 => (60000, 90000),
                RebarGrade.Grade75 => (75000, 100000),
                RebarGrade.Grade80 => (80000, 105000),
                _ => (0, 0)
            };
        }

        /// <summary>
        /// Standard rebar sizes (US designation)
        /// </summary>
        public static readonly string[] StandardRebarSizes = new[]
        {
            "#3",  // 3/8" diameter
            "#4",  // 1/2"
            "#5",  // 5/8"
            "#6",  // 3/4"
            "#7",  // 7/8"
            "#8",  // 1"
            "#9",  // 1-1/8"
            "#10", // 1-1/4"
            "#11", // 1-3/8"
            "#14", // 1-3/4"
            "#18"  // 2-1/4"
        };

        /// <summary>
        /// Gets nominal diameter for rebar size (inches)
        /// </summary>
        public static double GetRebarDiameter(string barSize)
        {
            return barSize switch
            {
                "#3" => 0.375,
                "#4" => 0.500,
                "#5" => 0.625,
                "#6" => 0.750,
                "#7" => 0.875,
                "#8" => 1.000,
                "#9" => 1.128,
                "#10" => 1.270,
                "#11" => 1.410,
                "#14" => 1.693,
                "#18" => 2.257,
                _ => 0
            };
        }

        /// <summary>
        /// Gets cross-sectional area for rebar size (sq in)
        /// </summary>
        public static double GetRebarArea(string barSize)
        {
            return barSize switch
            {
                "#3" => 0.11,
                "#4" => 0.20,
                "#5" => 0.31,
                "#6" => 0.44,
                "#7" => 0.60,
                "#8" => 0.79,
                "#9" => 1.00,
                "#10" => 1.27,
                "#11" => 1.56,
                "#14" => 2.25,
                "#18" => 4.00,
                _ => 0
            };
        }

        #endregion

        #region ASTM C33 - Concrete Aggregates

        /// <summary>
        /// Sieve analysis limits for fine aggregate (% passing)
        /// </summary>
        public static Dictionary<string, (double Min, double Max)> GetFineAggregateLimits()
        {
            return new Dictionary<string, (double Min, double Max)>
            {
                { "3/8 in", (100, 100) },
                { "No. 4", (95, 100) },
                { "No. 8", (80, 100) },
                { "No. 16", (50, 85) },
                { "No. 30", (25, 60) },
                { "No. 50", (5, 30) },
                { "No. 100", (0, 10) }
            };
        }

        /// <summary>
        /// Fineness modulus limits for fine aggregate
        /// </summary>
        public static (double Min, double Max) GetFinenessModulusLimits()
        {
            return (2.3, 3.1); // FM between 2.3 and 3.1
        }

        /// <summary>
        /// Maximum allowable deleterious substances in aggregates (% by mass)
        /// </summary>
        public static double GetMaxDeleteriousSubstances(string substance)
        {
            return substance.ToLower() switch
            {
                "clay lumps" => 3.0,
                "material finer than no. 200" => 3.0,
                "coal and lignite" => 0.5,
                "total deleterious" => 5.0,
                _ => 3.0
            };
        }

        #endregion

        #region ASTM C39 - Compressive Strength of Concrete

        /// <summary>
        /// Standard concrete cylinder specimen size
        /// </summary>
        public static (double Diameter, double Height) GetStandardCylinderSize()
        {
            return (6.0, 12.0); // 6" x 12" standard
        }

        /// <summary>
        /// Gets correction factor for length-to-diameter ratio
        /// </summary>
        public static double GetLengthCorrectionFactor(double lengthToDiameterRatio)
        {
            if (lengthToDiameterRatio >= 2.0) return 1.00;
            if (lengthToDiameterRatio >= 1.75) return 0.98;
            if (lengthToDiameterRatio >= 1.50) return 0.96;
            if (lengthToDiameterRatio >= 1.25) return 0.93;
            if (lengthToDiameterRatio >= 1.00) return 0.87;
            return 0.87;
        }

        /// <summary>
        /// Standard test ages for concrete (days)
        /// </summary>
        public static readonly int[] StandardTestAges = new[] { 7, 28, 56, 90 };

        /// <summary>
        /// Minimum number of cylinders per test
        /// </summary>
        public static int GetMinimumCylinders(string purpose)
        {
            return purpose.ToLower() switch
            {
                "quality control" => 2,
                "acceptance" => 3,
                "research" => 3,
                _ => 2
            };
        }

        #endregion

        #region ASTM D1586 - Standard Penetration Test (SPT)

        /// <summary>
        /// Interprets SPT N-value for soil classification
        /// </summary>
        public static string InterpretSPTValue(int nValue, string soilType)
        {
            if (soilType.ToLower().Contains("sand") || soilType.ToLower().Contains("gravel"))
            {
                if (nValue < 4) return "Very Loose";
                if (nValue < 10) return "Loose";
                if (nValue < 30) return "Medium Dense";
                if (nValue < 50) return "Dense";
                return "Very Dense";
            }
            else // Clay
            {
                if (nValue < 2) return "Very Soft";
                if (nValue < 4) return "Soft";
                if (nValue < 8) return "Medium Stiff";
                if (nValue < 15) return "Stiff";
                if (nValue < 30) return "Very Stiff";
                return "Hard";
            }
        }

        /// <summary>
        /// Gets approximate bearing capacity from SPT N-value (ksf)
        /// </summary>
        public static double GetApproximateBearingCapacity(int nValue, string soilType)
        {
            if (soilType.ToLower().Contains("sand"))
            {
                // Approximate: qa = N/5 (ksf) for 1-inch settlement
                return nValue * 0.2;
            }
            else if (soilType.ToLower().Contains("clay"))
            {
                // Conservative estimate for clay
                return nValue * 0.15;
            }
            return nValue * 0.15; // Conservative default
        }

        /// <summary>
        /// Standard SPT hammer drop height (inches)
        /// </summary>
        public static double GetStandardHammerDrop()
        {
            return 30.0; // 30 inches
        }

        /// <summary>
        /// Standard SPT hammer weight (lb)
        /// </summary>
        public static double GetStandardHammerWeight()
        {
            return 140.0; // 140 lb
        }

        #endregion

        #region ASTM C94 - Ready-Mixed Concrete

        /// <summary>
        /// Maximum time from batching to discharge (minutes)
        /// </summary>
        public static int GetMaximumTransportTime(double temperature)
        {
            if (temperature >= 85) // °F
                return 75; // 75 minutes in hot weather
            else
                return 90; // 90 minutes normal
        }

        /// <summary>
        /// Maximum number of revolutions during transport
        /// </summary>
        public static int GetMaximumRevolutions()
        {
            return 300; // 300 revolutions maximum
        }

        /// <summary>
        /// Water addition limits on site
        /// </summary>
        public static (bool Permitted, string Conditions) IsWaterAdditionPermitted()
        {
            return (true, "Water may be added on site provided: " +
                         "1) Slump does not exceed specified maximum, " +
                         "2) Maximum w/c ratio not exceeded, " +
                         "3) Mixing continues for 30+ revolutions");
        }

        #endregion

        #region ASTM E119 - Fire Resistance Testing

        /// <summary>
        /// Standard time-temperature curve points (minutes, °F)
        /// </summary>
        public static Dictionary<int, int> GetStandardFireCurve()
        {
            return new Dictionary<int, int>
            {
                { 0, 70 },    // Start at room temp
                { 5, 1000 },
                { 10, 1300 },
                { 30, 1550 },
                { 60, 1700 },
                { 120, 1850 },
                { 240, 2000 },
                { 480, 2300 }
            };
        }

        /// <summary>
        /// Pass/fail criteria for fire resistance test
        /// </summary>
        public static string[] GetPassCriteria()
        {
            return new[]
            {
                "Temperature rise on unexposed surface ≤325°F average, ≤625°F single point",
                "Structural integrity maintained (no collapse)",
                "No passage of flame or hot gases through assembly",
                "Hose stream test passed (if required)"
            };
        }

        #endregion

        #region ASTM A36 - Structural Steel

        /// <summary>
        /// ASTM A36 steel properties
        /// </summary>
        public static (int YieldStrength, int TensileStrength) GetA36Properties()
        {
            return (36000, 58000); // 36 ksi yield, 58-80 ksi tensile
        }

        /// <summary>
        /// Gets thickness limits for A36 steel (inches)
        /// </summary>
        public static double GetMaximumThickness()
        {
            return 8.0; // A36 applicable up to 8" thick
        }

        /// <summary>
        /// Chemical composition limits (% by mass)
        /// </summary>
        public static Dictionary<string, double> GetChemicalComposition()
        {
            return new Dictionary<string, double>
            {
                { "Carbon (max)", 0.26 },
                { "Manganese (max)", 0.80 },
                { "Phosphorus (max)", 0.04 },
                { "Sulfur (max)", 0.05 },
                { "Silicon (max)", 0.40 },
                { "Copper (min, when specified)", 0.20 }
            };
        }

        #endregion

        #region ASTM C90 - Loadbearing Concrete Masonry Units

        /// <summary>
        /// Minimum compressive strength for concrete masonry units (psi)
        /// </summary>
        public static int GetMinimumCMUStrength(string weightClassification)
        {
            return weightClassification.ToLower() switch
            {
                "lightweight" => 1900,
                "mediumweight" => 1900,
                "normalweight" => 1900,
                _ => 1900
            };
        }

        /// <summary>
        /// Maximum water absorption limits (lb/ft³)
        /// </summary>
        public static double GetMaximumWaterAbsorption(string weightClass)
        {
            return weightClass.ToLower() switch
            {
                "lightweight" => 18.0,
                "mediumweight" => 15.0,
                "normalweight" => 13.0,
                _ => 15.0
            };
        }

        /// <summary>
        /// Standard CMU sizes (inches) - Nominal dimensions
        /// </summary>
        public static readonly (int Width, int Height, int Length)[] StandardCMUSizes = new[]
        {
            (4, 8, 16),
            (6, 8, 16),
            (8, 8, 16),
            (10, 8, 16),
            (12, 8, 16)
        };

        #endregion

        #region Quality Control and Testing Frequencies

        /// <summary>
        /// Gets recommended testing frequency for material type
        /// </summary>
        public static string GetTestingFrequency(string materialType)
        {
            return materialType.ToLower() switch
            {
                "concrete" => "One set of cylinders per 100 cubic yards or per day's pour",
                "rebar" => "Mill certificate required; field testing if source unknown",
                "masonry units" => "One test per 5,000 units or per delivery",
                "soil" => "SPT every 50 linear feet and change in strata",
                "aggregates" => "One test per 500 tons or monthly",
                _ => "Per project specifications"
            };
        }

        /// <summary>
        /// Sample size requirements
        /// </summary>
        public static string GetSampleSize(string testType)
        {
            return testType.ToLower() switch
            {
                "concrete cylinders" => "2-3 cylinders per test age",
                "aggregate sieve analysis" => "Minimum 300g for fine, 5kg for coarse",
                "soil moisture-density" => "Minimum 3kg sample",
                "rebar tensile test" => "Two specimens per lot",
                _ => "Per applicable ASTM standard"
            };
        }

        #endregion

        #region ASTM D1785 - PVC Pipe for Water Supply (Critical for Liberia)

        /// <summary>
        /// ASTM D1785 PVC pipe schedules
        /// </summary>
        public enum PVCPipeSchedule
        {
            /// <summary>Schedule 40 - Standard wall</summary>
            Schedule40,
            /// <summary>Schedule 80 - Extra heavy wall</summary>
            Schedule80,
            /// <summary>Schedule 120 - Double extra heavy</summary>
            Schedule120
        }

        /// <summary>
        /// Gets working pressure for PVC pipe (psi at 73°F)
        /// </summary>
        public static int GetPVCWorkingPressure(int nominalSize, PVCPipeSchedule schedule)
        {
            if (schedule == PVCPipeSchedule.Schedule40)
            {
                return nominalSize switch
                {
                    1 => 450, // 1/2"
                    2 => 400, // 3/4"
                    3 => 370, // 1"
                    4 => 330, // 1-1/4"
                    5 => 320, // 1-1/2"
                    6 => 280, // 2"
                    8 => 260, // 2-1/2"
                    10 => 220, // 3"
                    12 => 220, // 3-1/2"
                    16 => 200, // 4"
                    20 => 180, // 5"
                    24 => 165, // 6"
                    30 => 140, // 8"
                    _ => 100
                };
            }
            else if (schedule == PVCPipeSchedule.Schedule80)
            {
                return nominalSize switch
                {
                    1 => 850, // 1/2"
                    2 => 690, // 3/4"
                    3 => 630, // 1"
                    4 => 520, // 1-1/4"
                    5 => 470, // 1-1/2"
                    6 => 400, // 2"
                    8 => 370, // 2-1/2"
                    10 => 320, // 3"
                    12 => 300, // 3-1/2"
                    16 => 280, // 4"
                    20 => 250, // 5"
                    24 => 220, // 6"
                    30 => 190, // 8"
                    _ => 150
                };
            }
            else // Schedule 120
            {
                return nominalSize switch
                {
                    1 => 1230, // 1/2"
                    2 => 1130, // 3/4"
                    3 => 1040, // 1"
                    4 => 850, // 1-1/4"
                    5 => 780, // 1-1/2"
                    6 => 630, // 2"
                    _ => 400
                };
            }
        }

        /// <summary>
        /// Temperature derating factors for PVC pipe pressure
        /// </summary>
        public static double GetTemperatureDerating(double temperatureF)
        {
            if (temperatureF <= 73) return 1.0;
            else if (temperatureF <= 80) return 0.88;
            else if (temperatureF <= 90) return 0.75;
            else if (temperatureF <= 100) return 0.62;
            else if (temperatureF <= 110) return 0.50;
            else if (temperatureF <= 120) return 0.40;
            else if (temperatureF <= 130) return 0.30;
            else if (temperatureF <= 140) return 0.22;
            else return 0.0; // Above 140°F not recommended
        }

        #endregion

        #region ASTM A307 - Carbon Steel Bolts and Studs (Critical for Liberia)

        /// <summary>
        /// ASTM A307 bolt grades
        /// </summary>
        public enum A307BoltGrade
        {
            /// <summary>Grade A - General purpose</summary>
            GradeA,
            /// <summary>Grade B - Heavy hex structural bolts</summary>
            GradeB,
            /// <summary>Grade C - Non-structural applications</summary>
            GradeC
        }

        /// <summary>
        /// Gets minimum tensile strength for A307 bolts (psi)
        /// </summary>
        public static int GetBoltTensileStrength(A307BoltGrade grade)
        {
            return grade switch
            {
                A307BoltGrade.GradeA => 60000, // 60 ksi
                A307BoltGrade.GradeB => 60000, // 60 ksi
                A307BoltGrade.GradeC => 60000, // 60 ksi
                _ => 60000
            };
        }

        /// <summary>
        /// Standard bolt sizes (inches) per ASTM A307
        /// </summary>
        public static readonly double[] StandardBoltSizes = new[]
        {
            0.25, 0.3125, 0.375, 0.4375, 0.5, 0.5625, 0.625, 0.75, 0.875, 1.0,
            1.125, 1.25, 1.375, 1.5, 1.75, 2.0, 2.25, 2.5, 2.75, 3.0, 3.25, 3.5, 3.75, 4.0
        };

        /// <summary>
        /// Gets proof load for bolt size (lbs)
        /// </summary>
        public static double GetBoltProofLoad(double diameter, A307BoltGrade grade)
        {
            double stressArea = GetBoltStressArea(diameter);
            double proofStress = 33000; // psi for Grade A
            return stressArea * proofStress;
        }

        /// <summary>
        /// Calculates bolt stress area (in²)
        /// </summary>
        private static double GetBoltStressArea(double diameter)
        {
            // Simplified formula: As = 0.7854 * (D - 0.9743/n)²
            // For UNC threads (most common)
            double threadsPerInch = diameter switch
            {
                <= 0.25 => 20,
                <= 0.375 => 16,
                <= 0.5 => 13,
                <= 0.75 => 10,
                <= 1.0 => 8,
                <= 1.5 => 6,
                _ => 4.5
            };

            double effectiveDiameter = diameter - (0.9743 / threadsPerInch);
            return 0.7854 * effectiveDiameter * effectiveDiameter;
        }

        /// <summary>
        /// Recommended torque values for A307 bolts (ft-lbs)
        /// Non-lubricated bolts
        /// </summary>
        public static double GetRecommendedTorque(double diameter)
        {
            return diameter switch
            {
                0.25 => 5,    // 1/4"
                0.3125 => 9,  // 5/16"
                0.375 => 15,  // 3/8"
                0.4375 => 24, // 7/16"
                0.5 => 37,    // 1/2"
                0.5625 => 53, // 9/16"
                0.625 => 74,  // 5/8"
                0.75 => 120,  // 3/4"
                0.875 => 200, // 7/8"
                1.0 => 280,   // 1"
                1.125 => 400, // 1-1/8"
                1.25 => 540,  // 1-1/4"
                _ => diameter * diameter * 280 // Approximate for larger
            };
        }

        #endregion

        #region ASTM B117 - Salt Spray Testing (Critical for Coastal Liberia)

        /// <summary>
        /// ASTM B117 salt spray test exposure periods
        /// Critical for corrosion protection in coastal environments
        /// </summary>
        public enum SaltSprayDuration
        {
            /// <summary>24 hours - Minimum test</summary>
            Hours_24,
            /// <summary>48 hours - Standard light duty</summary>
            Hours_48,
            /// <summary>96 hours - Standard moderate duty</summary>
            Hours_96,
            /// <summary>168 hours (1 week) - Standard heavy duty</summary>
            Hours_168,
            /// <summary>240 hours - Extended test</summary>
            Hours_240,
            /// <summary>480 hours - Severe exposure</summary>
            Hours_480,
            /// <summary>720 hours (30 days) - Marine environment</summary>
            Hours_720,
            /// <summary>1000 hours - Ultra-severe marine</summary>
            Hours_1000
        }

        /// <summary>
        /// Gets recommended salt spray test duration for coating type and environment
        /// </summary>
        public static SaltSprayDuration GetRecommendedTestDuration(
            string coatingType,
            string environment)
        {
            bool isCoastal = environment.ToLower().Contains("coastal") ||
                           environment.ToLower().Contains("marine") ||
                           environment.ToLower().Contains("liberia"); // Liberia is coastal

            if (coatingType.ToLower().Contains("zinc") || 
                coatingType.ToLower().Contains("galvanized"))
            {
                return isCoastal ? SaltSprayDuration.Hours_720 : SaltSprayDuration.Hours_168;
            }
            else if (coatingType.ToLower().Contains("paint") ||
                     coatingType.ToLower().Contains("powder"))
            {
                return isCoastal ? SaltSprayDuration.Hours_480 : SaltSprayDuration.Hours_96;
            }
            else if (coatingType.ToLower().Contains("epoxy"))
            {
                return isCoastal ? SaltSprayDuration.Hours_1000 : SaltSprayDuration.Hours_240;
            }
            else if (coatingType.ToLower().Contains("stainless"))
            {
                return isCoastal ? SaltSprayDuration.Hours_240 : SaltSprayDuration.Hours_48;
            }
            else
            {
                return isCoastal ? SaltSprayDuration.Hours_168 : SaltSprayDuration.Hours_48;
            }
        }

        /// <summary>
        /// Evaluates salt spray test results
        /// </summary>
        public static (bool Passed, string Rating, string Recommendation) EvaluateSaltSprayResults(
            double redRustPercent,
            double whiteRustPercent,
            int hoursExposed,
            string coatingType)
        {
            bool passed = false;
            string rating = "";
            string recommendation = "";

            // Red rust is more serious than white rust
            if (redRustPercent < 1 && whiteRustPercent < 5)
            {
                passed = true;
                rating = "Excellent - No significant corrosion";
                recommendation = "Suitable for severe marine environments";
            }
            else if (redRustPercent < 5 && whiteRustPercent < 15)
            {
                passed = true;
                rating = "Good - Minor surface corrosion";
                recommendation = "Suitable for coastal environments with maintenance";
            }
            else if (redRustPercent < 10 && whiteRustPercent < 25)
            {
                passed = false;
                rating = "Fair - Moderate corrosion";
                recommendation = "Additional corrosion protection required for coastal use";
            }
            else
            {
                passed = false;
                rating = "Poor - Significant corrosion";
                recommendation = "NOT suitable for coastal environments. Redesign required.";
            }

            return (passed, rating, recommendation);
        }

        /// <summary>
        /// Liberia-specific corrosion protection requirements
        /// Liberia has high humidity and coastal exposure
        /// </summary>
        public static string[] GetLiberiaCorrosionRequirements(string materialType)
        {
            if (materialType.ToLower().Contains("steel") ||
                materialType.ToLower().Contains("metal"))
            {
                return new[]
                {
                    "Hot-dip galvanizing per ASTM A123 (minimum Z275 coating)",
                    "Epoxy coating for additional protection",
                    "Regular inspection and maintenance program",
                    "Salt spray test: Minimum 720 hours per ASTM B117",
                    "Use stainless steel fasteners (Grade 316 preferred)",
                    "Cathodic protection for buried/submerged metals"
                };
            }
            else if (materialType.ToLower().Contains("concrete"))
            {
                return new[]
                {
                    "Minimum concrete cover: 75mm (3 inches)",
                    "Water-cement ratio: Maximum 0.40",
                    "Use corrosion-inhibiting admixtures",
                    "Epoxy-coated reinforcement required",
                    "Dense, low-permeability concrete mix",
                    "Surface sealers for exposed concrete"
                };
            }
            else if (materialType.ToLower().Contains("wood") ||
                     materialType.ToLower().Contains("timber"))
            {
                return new[]
                {
                    "Pressure-treated lumber mandatory",
                    "CCA or ACQ treatment for ground contact",
                    "Termite protection critical (Liberia has high termite activity)",
                    "Moisture barrier under all wood elements",
                    "Regular inspection and re-treatment"
                };
            }
            else
            {
                return new[]
                {
                    "Material assessment for coastal suitability",
                    "Corrosion testing per ASTM B117",
                    "UV resistance testing for plastics",
                    "Maintenance schedule required"
                };
            }
        }

        #endregion

        #region Liberia Building Climate and Environment

        /// <summary>
        /// Liberia climate characteristics affecting construction
        /// </summary>
        public static class LiberiaClimate
        {
            /// <summary>Annual rainfall in Monrovia (mm)</summary>
            public const double AnnualRainfallMM = 5100; // One of highest in world

            /// <summary>Average temperature range (°C)</summary>
            public static readonly (double Min, double Max) TemperatureRange = (21, 31);

            /// <summary>Relative humidity range (%)</summary>
            public static readonly (double Min, double Max) RelativeHumidity = (70, 95);

            /// <summary>Rainy season months</summary>
            public static readonly string[] RainySeason = new[]
            {
                "April", "May", "June", "July", "August", "September", "October"
            };

            /// <summary>
            /// Gets construction recommendations for Liberia climate
            /// </summary>
            public static string[] GetConstructionRecommendations()
            {
                return new[]
                {
                    "Design for extreme rainfall (5100mm/year)",
                    "Elevated buildings to prevent flood damage",
                    "Extensive roof overhangs (minimum 600mm)",
                    "High-quality waterproofing mandatory",
                    "Mold-resistant materials required",
                    "Corrosion protection critical due to high humidity",
                    "Termite protection essential",
                    "Natural ventilation design for hot-humid climate",
                    "Construction scheduling: Plan major work for dry season (Nov-March)",
                    "Adequate drainage systems mandatory"
                };
            }

            /// <summary>
            /// Gets minimum roof slope for Liberia (degrees)
            /// </summary>
            public static double GetMinimumRoofSlope()
            {
                // Minimum 15 degrees (27% slope) due to extreme rainfall
                return 15.0;
            }

            /// <summary>
            /// Gets required gutter size for roof area (inches)
            /// </summary>
            public static double GetRequiredGutterSize(double roofAreaSqFt)
            {
                // Liberia requires larger gutters due to extreme rainfall
                if (roofAreaSqFt < 500) return 6;  // 6" gutter
                else if (roofAreaSqFt < 1000) return 7; // 7" gutter
                else if (roofAreaSqFt < 1500) return 8; // 8" gutter
                else return 10; // 10" gutter for large roofs
            }
        }

        #endregion
    }
}
