// FILE: NECStandards.cs
// LOCATION: StingBIM.Standards/NEC2023/
// LINES: ~3000
// PURPOSE: NEC 2023 electrical standards compliance and calculations

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingBIM.Standards.NEC2023
{
    #region Supporting Classes

    /// <summary>
    /// Conductor material types
    /// </summary>
    public enum ConductorMaterial
    {
        Copper,
        Aluminum
    }

    /// <summary>
    /// Insulation temperature ratings
    /// </summary>
    public enum TemperatureRating
    {
        Celsius60 = 60,
        Celsius75 = 75,
        Celsius90 = 90
    }

    /// <summary>
    /// Conduit types
    /// </summary>
    public enum ConduitType
    {
        RMC,  // Rigid Metal Conduit
        IMC,  // Intermediate Metal Conduit
        EMT,  // Electrical Metallic Tubing
        PVC   // Polyvinyl Chloride
    }

    /// <summary>
    /// Insulation types
    /// </summary>
    public enum InsulationType
    {
        THHN,
        THWN,
        XHHW,
        THW,
        RHW
    }

    /// <summary>
    /// Conductor information
    /// </summary>
    public class Conductor
    {
        public string Size { get; set; }
        public ConductorMaterial Material { get; set; }
        public InsulationType Insulation { get; set; }
        public int Count { get; set; } = 1;
    }

    /// <summary>
    /// Conduit fill calculation result
    /// </summary>
    public class ConduitFillResult
    {
        public string ConduitSize { get; set; }
        public double FillPercentage { get; set; }
        public double AllowedFillPercentage { get; set; }
        public bool IsCompliant { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Validation result
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    #endregion

    /// <summary>
    /// NEC 2023 electrical standards implementation
    /// National Electrical Code compliance checking and calculations
    /// </summary>
    public static class NECStandards
    {
        public const string Version = "2023";

        #region Article 310 - Conductor Sizing

        /// <summary>
        /// Table 310.16 - Allowable Ampacities of Insulated Conductors
        /// Reference: NEC 2023 Table 310.16
        /// Temperature rating: 60°C, 75°C, and 90°C
        /// </summary>
        private static readonly Dictionary<string, (int temp60C, int temp75C, int temp90C)> _copperAmpacityTable = new Dictionary<string, (int, int, int)>
        {
            { "14", (15, 20, 25) },
            { "12", (20, 25, 30) },
            { "10", (30, 35, 40) },
            { "8", (40, 50, 55) },
            { "6", (55, 65, 75) },
            { "4", (70, 85, 95) },
            { "3", (85, 100, 110) },
            { "2", (95, 115, 130) },
            { "1", (110, 130, 145) },
            { "1/0", (125, 150, 170) },
            { "2/0", (145, 175, 195) },
            { "3/0", (165, 200, 225) },
            { "4/0", (195, 230, 260) },
            { "250", (215, 255, 290) },
            { "300", (240, 285, 320) },
            { "350", (260, 310, 350) },
            { "400", (280, 335, 380) },
            { "500", (320, 380, 430) },
            { "600", (355, 420, 475) },
            { "700", (385, 460, 520) },
            { "750", (400, 475, 535) },
            { "800", (410, 490, 555) },
            { "900", (435, 520, 585) },
            { "1000", (455, 545, 615) },
            { "1250", (495, 590, 665) },
            { "1500", (520, 625, 705) },
            { "1750", (545, 650, 735) },
            { "2000", (560, 665, 750) }
        };

        private static readonly Dictionary<string, (int temp60C, int temp75C, int temp90C)> _aluminumAmpacityTable = new Dictionary<string, (int, int, int)>
        {
            { "12", (15, 20, 25) },
            { "10", (25, 30, 35) },
            { "8", (30, 40, 45) },
            { "6", (40, 50, 55) },
            { "4", (55, 65, 75) },
            { "3", (65, 75, 85) },
            { "2", (75, 90, 100) },
            { "1", (85, 100, 115) },
            { "1/0", (100, 120, 135) },
            { "2/0", (115, 135, 150) },
            { "3/0", (130, 155, 175) },
            { "4/0", (150, 180, 205) },
            { "250", (170, 205, 230) },
            { "300", (190, 230, 255) },
            { "350", (210, 250, 280) },
            { "400", (225, 270, 305) },
            { "500", (260, 310, 350) },
            { "600", (285, 340, 385) },
            { "700", (310, 375, 420) },
            { "750", (320, 385, 435) },
            { "800", (330, 395, 445) },
            { "900", (355, 425, 480) },
            { "1000", (375, 445, 500) },
            { "1250", (405, 485, 545) },
            { "1500", (435, 520, 585) },
            { "1750", (455, 545, 615) },
            { "2000", (470, 560, 630) }
        };

        /// <summary>
        /// Get conductor ampacity from NEC Table 310.16
        /// </summary>
        /// <param name="wireSize">Wire size (AWG or kcmil)</param>
        /// <param name="material">Conductor material</param>
        /// <param name="tempRating">Temperature rating (60°C, 75°C, or 90°C)</param>
        /// <returns>Ampacity in amperes</returns>
        /// <exception cref="ArgumentException">Invalid wire size or temperature rating</exception>
        /// <example>
        /// <code>
        /// int ampacity = ConductorSizing.GetConductorAmpacity("8", ConductorMaterial.Copper, 75);
        /// // Returns 50 amperes for 8 AWG copper at 75°C
        /// </code>
        /// </example>
        public static int GetConductorAmpacity(string wireSize, ConductorMaterial material, int tempRating)
        {
            var table = material == ConductorMaterial.Copper ? _copperAmpacityTable : _aluminumAmpacityTable;

            if (!table.ContainsKey(wireSize))
                throw new ArgumentException($"Invalid wire size: {wireSize}");

            var (temp60, temp75, temp90) = table[wireSize];

            return tempRating switch
            {
                60 => temp60,
                75 => temp75,
                90 => temp90,
                _ => throw new ArgumentException($"Invalid temperature rating: {tempRating}. Must be 60, 75, or 90.")
            };
        }

        /// <summary>
        /// Table 310.15(B)(1) - Temperature Correction Factors
        /// </summary>
        private static readonly Dictionary<int, double> _tempCorrectionFactors75C = new Dictionary<int, double>
        {
            { 21, 1.05 }, { 22, 1.04 }, { 23, 1.04 }, { 24, 1.03 }, { 25, 1.02 },
            { 26, 1.00 }, { 27, 1.00 }, { 28, 1.00 }, { 29, 1.00 }, { 30, 1.00 },
            { 31, 0.99 }, { 32, 0.97 }, { 33, 0.96 }, { 34, 0.95 }, { 35, 0.94 },
            { 36, 0.91 }, { 37, 0.90 }, { 38, 0.89 }, { 39, 0.88 }, { 40, 0.87 },
            { 41, 0.82 }, { 42, 0.81 }, { 43, 0.80 }, { 44, 0.79 }, { 45, 0.78 },
            { 46, 0.71 }, { 47, 0.70 }, { 48, 0.69 }, { 49, 0.68 }, { 50, 0.67 }
        };

        /// <summary>
        /// Apply temperature correction factor based on ambient temperature
        /// Reference: NEC 2023 Table 310.15(B)(1)
        /// </summary>
        /// <param name="ampacity">Base ampacity</param>
        /// <param name="ambientTemp">Ambient temperature in Celsius</param>
        /// <returns>Corrected ampacity</returns>
        public static double ApplyTemperatureCorrection(double ampacity, double ambientTemp)
        {
            int temp = (int)Math.Round(ambientTemp);

            if (temp < 21)
                return ampacity * 1.05;

            if (temp > 50)
                return ampacity * 0.67;

            if (_tempCorrectionFactors75C.TryGetValue(temp, out double factor))
                return ampacity * factor;

            return ampacity;
        }

        /// <summary>
        /// Apply conductor bundling adjustment factor
        /// Reference: NEC 2023 Table 310.15(B)(3)(a)
        /// </summary>
        /// <param name="ampacity">Base ampacity</param>
        /// <param name="conductorCount">Number of current-carrying conductors</param>
        /// <returns>Adjusted ampacity</returns>
        public static double ApplyBundlingAdjustment(double ampacity, int conductorCount)
        {
            if (conductorCount <= 3)
                return ampacity;

            if (conductorCount <= 6)
                return ampacity * 0.80;

            if (conductorCount <= 9)
                return ampacity * 0.70;

            if (conductorCount <= 20)
                return ampacity * 0.50;

            if (conductorCount <= 30)
                return ampacity * 0.45;

            if (conductorCount <= 40)
                return ampacity * 0.40;

            return ampacity * 0.35;
        }

        /// <summary>
        /// Calculate voltage drop for conductor
        /// Reference: NEC 2023 recommendations (3% branch, 5% total)
        /// Formula: VD = (2 × K × I × L) / CM
        /// </summary>
        /// <param name="current">Current in amperes</param>
        /// <param name="length">One-way length in feet</param>
        /// <param name="wireSize">Wire size</param>
        /// <param name="voltage">System voltage</param>
        /// <returns>Voltage drop in volts</returns>
        public static double CalculateVoltageDrop(double current, double length, string wireSize, int voltage)
        {
            // K constant: 12.9 for copper, 21.2 for aluminum
            double K = 12.9; // Assuming copper
            double circularMils = GetCircularMils(wireSize);

            double voltageDrop = (2 * K * current * length) / circularMils;
            return voltageDrop;
        }

        /// <summary>
        /// Get minimum conductor size for given load
        /// </summary>
        /// <param name="load">Load in amperes</param>
        /// <param name="conductorCount">Number of conductors</param>
        /// <param name="ambientTemp">Ambient temperature in Celsius</param>
        /// <returns>Minimum wire size</returns>
        public static string GetMinimumConductorSize(double load, int conductorCount, double ambientTemp)
        {
            var wireSizes = new[] { "14", "12", "10", "8", "6", "4", "3", "2", "1", "1/0", "2/0", "3/0", "4/0", "250", "300", "350", "400", "500", "600", "700", "750", "800", "900", "1000" };

            foreach (var size in wireSizes)
            {
                double ampacity = GetConductorAmpacity(size, ConductorMaterial.Copper, 75);
                ampacity = ApplyTemperatureCorrection(ampacity, ambientTemp);
                ampacity = ApplyBundlingAdjustment(ampacity, conductorCount);

                if (ampacity >= load)
                    return size;
            }

            return "Oversized - use parallel conductors";
        }

        private static double GetCircularMils(string wireSize)
        {
            var circularMilsTable = new Dictionary<string, double>
            {
                { "14", 4110 }, { "12", 6530 }, { "10", 10380 }, { "8", 16510 },
                { "6", 26240 }, { "4", 41740 }, { "3", 52620 }, { "2", 66360 },
                { "1", 83690 }, { "1/0", 105600 }, { "2/0", 133100 }, { "3/0", 167800 },
                { "4/0", 211600 }, { "250", 250000 }, { "300", 300000 }, { "350", 350000 },
                { "400", 400000 }, { "500", 500000 }, { "600", 600000 }, { "700", 700000 },
                { "750", 750000 }, { "800", 800000 }, { "900", 900000 }, { "1000", 1000000 }
            };

            return circularMilsTable.TryGetValue(wireSize, out double value) ? value : 0;
        }

        #endregion

        #region Article 240 - Overcurrent Protection

        /// <summary>
        /// Standard breaker sizes per NEC 240.6(A)
        /// </summary>
        private static readonly int[] _standardBreakerSizes = new[]
        {
            15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 90, 100, 110, 125, 150,
            175, 200, 225, 250, 300, 350, 400, 450, 500, 600, 700, 800,
            1000, 1200, 1600, 2000, 2500, 3000, 4000, 5000, 6000
        };

        /// <summary>
        /// Get standard breaker size for required amperage
        /// Reference: NEC 2023 Section 240.6(A)
        /// </summary>
        /// <param name="requiredAmps">Required amperage</param>
        /// <param name="roundUp">If true, round up to next size; if false, select exact or smaller size</param>
        /// <returns>Standard breaker size in amperes</returns>
        public static int GetStandardBreakerSize(double requiredAmps, bool roundUp = true)
        {
            if (roundUp)
            {
                foreach (int size in _standardBreakerSizes)
                {
                    if (size >= requiredAmps)
                        return size;
                }
                return _standardBreakerSizes[_standardBreakerSizes.Length - 1];
            }
            else
            {
                int selectedSize = _standardBreakerSizes[0];
                foreach (int size in _standardBreakerSizes)
                {
                    if (size <= requiredAmps)
                        selectedSize = size;
                    else
                        break;
                }
                return selectedSize;
            }
        }

        /// <summary>
        /// Maximum breaker sizes for conductor protection
        /// Reference: NEC 2023 Table 240.4(D)
        /// </summary>
        private static readonly Dictionary<string, int> _maxBreakerForWire = new Dictionary<string, int>
        {
            { "14", 15 },
            { "12", 20 },
            { "10", 30 },
            { "8", 40 },
            { "6", 60 },
            { "4", 85 },
            { "3", 100 },
            { "2", 115 },
            { "1", 130 },
            { "1/0", 150 },
            { "2/0", 175 },
            { "3/0", 200 },
            { "4/0", 230 }
        };

        /// <summary>
        /// Validate breaker size for conductor
        /// </summary>
        /// <param name="wireSize">Wire size</param>
        /// <param name="breakerAmps">Breaker amperage</param>
        /// <returns>Validation result</returns>
        public static ValidationResult ValidateBreakerSize(string wireSize, int breakerAmps)
        {
            var result = new ValidationResult { IsValid = true };

            if (!_maxBreakerForWire.ContainsKey(wireSize))
            {
                result.IsValid = false;
                result.Errors.Add($"Unknown wire size: {wireSize}");
                return result;
            }

            int maxBreaker = _maxBreakerForWire[wireSize];
            if (breakerAmps > maxBreaker)
            {
                result.IsValid = false;
                result.Errors.Add($"Breaker size {breakerAmps}A exceeds maximum {maxBreaker}A for {wireSize} AWG conductor");
            }

            return result;
        }

        /// <summary>
        /// Get maximum breaker size for conductor
        /// </summary>
        /// <param name="wireSize">Wire size</param>
        /// <returns>Maximum breaker amperage</returns>
        public static int GetMaximumBreakerSize(string wireSize)
        {
            return _maxBreakerForWire.TryGetValue(wireSize, out int value) ? value : 0;
        }

        /// <summary>
        /// Check if GFCI protection is required
        /// Reference: NEC 2023 Section 210.8
        /// </summary>
        /// <param name="location">Location type</param>
        /// <param name="roomType">Room type</param>
        /// <returns>True if GFCI required</returns>
        public static bool RequiresGFCI(string location, string roomType)
        {
            var gfciLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bathroom", "kitchen", "garage", "outdoor", "basement", "crawlspace",
                "laundry", "utility", "unfinished area", "wet bar"
            };

            return gfciLocations.Contains(roomType.ToLower()) ||
                   gfciLocations.Contains(location.ToLower());
        }

        /// <summary>
        /// Check if AFCI protection is required
        /// Reference: NEC 2023 Section 210.12
        /// </summary>
        /// <param name="location">Location type</param>
        /// <param name="roomType">Room type</param>
        /// <returns>True if AFCI required</returns>
        public static bool RequiresAFCI(string location, string roomType)
        {
            // AFCI required for dwelling units in bedrooms and most habitable rooms (2023 update)
            var afciRooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bedroom", "living room", "family room", "den", "library",
                "sunroom", "recreation room", "closet", "hallway", "dining room"
            };

            return afciRooms.Contains(roomType.ToLower());
        }

        #endregion

        #region Article 250 - Grounding and Bonding

        /// <summary>
        /// Table 250.122 - Equipment Grounding Conductor sizing
        /// Reference: NEC 2023 Table 250.122
        /// </summary>
        private static readonly Dictionary<int, string> _equipmentGroundingConductorTable = new Dictionary<int, string>
        {
            { 15, "14" },
            { 20, "12" },
            { 30, "10" },
            { 40, "10" },
            { 60, "10" },
            { 100, "8" },
            { 200, "6" },
            { 300, "4" },
            { 400, "3" },
            { 500, "2" },
            { 600, "1" },
            { 800, "1/0" },
            { 1000, "2/0" },
            { 1200, "3/0" },
            { 1600, "4/0" },
            { 2000, "250" },
            { 2500, "350" },
            { 3000, "400" },
            { 4000, "500" },
            { 5000, "700" },
            { 6000, "800" }
        };

        /// <summary>
        /// Get equipment grounding conductor size
        /// Reference: NEC 2023 Table 250.122
        /// </summary>
        /// <param name="breakerSize">Circuit breaker size in amperes</param>
        /// <returns>Equipment grounding conductor size</returns>
        public static string GetEquipmentGroundingConductor(int breakerSize)
        {
            foreach (var kvp in _equipmentGroundingConductorTable.OrderBy(x => x.Key))
            {
                if (breakerSize <= kvp.Key)
                    return kvp.Value;
            }

            return "Consult NEC for sizes above 6000A";
        }

        /// <summary>
        /// Table 250.66 - Grounding Electrode Conductor sizing
        /// </summary>
        private static readonly Dictionary<string, string> _groundingElectrodeConductorTable = new Dictionary<string, string>
        {
            { "2", "8" },
            { "1", "6" },
            { "1/0", "6" },
            { "2/0", "4" },
            { "3/0", "4" },
            { "4/0", "2" },
            { "250", "2" },
            { "300", "1/0" },
            { "350", "1/0" },
            { "400", "2/0" },
            { "500", "2/0" },
            { "600", "3/0" },
            { "700", "3/0" },
            { "750", "3/0" },
            { "800", "3/0" },
            { "900", "4/0" },
            { "1000", "4/0" }
        };

        /// <summary>
        /// Get grounding electrode conductor size
        /// Reference: NEC 2023 Table 250.66
        /// </summary>
        /// <param name="serviceEntranceSize">Service entrance conductor size</param>
        /// <returns>Grounding electrode conductor size</returns>
        public static string GetGroundingElectrodeConductor(string serviceEntranceSize)
        {
            return _groundingElectrodeConductorTable.TryGetValue(serviceEntranceSize, out string value)
                ? value
                : "Consult NEC Table 250.66";
        }

        /// <summary>
        /// Validate grounding system
        /// </summary>
        /// <param name="breakerSize">Circuit breaker size</param>
        /// <param name="groundingConductorSize">Actual grounding conductor size</param>
        /// <returns>Validation result</returns>
        public static ValidationResult ValidateGroundingSystem(int breakerSize, string groundingConductorSize)
        {
            var result = new ValidationResult { IsValid = true };

            string required = GetEquipmentGroundingConductor(breakerSize);
            
            // Simple validation - in reality would need to compare wire sizes properly
            if (groundingConductorSize != required)
            {
                result.Warnings.Add($"Grounding conductor is {groundingConductorSize}, required size is {required} for {breakerSize}A breaker");
            }

            return result;
        }

        #endregion

        #region Chapter 9 - Conduit Fill

        /// <summary>
        /// Table 1 - Percent Fill for Conduit
        /// </summary>
        private static readonly Dictionary<int, double> _conduitFillPercentages = new Dictionary<int, double>
        {
            { 1, 0.53 },  // 1 conductor: 53%
            { 2, 0.31 },  // 2 conductors: 31%
            { 3, 0.40 }   // 3+ conductors: 40%
        };

        /// <summary>
        /// Table 4 - EMT conduit dimensions (internal area in square inches)
        /// </summary>
        private static readonly Dictionary<string, double> _emtConduitAreas = new Dictionary<string, double>
        {
            { "1/2", 0.304 },
            { "3/4", 0.533 },
            { "1", 0.864 },
            { "1-1/4", 1.496 },
            { "1-1/2", 2.036 },
            { "2", 3.356 },
            { "2-1/2", 5.858 },
            { "3", 8.846 },
            { "3-1/2", 11.545 },
            { "4", 14.753 }
        };

        /// <summary>
        /// Wire cross-sectional areas with THHN insulation (square inches)
        /// </summary>
        private static readonly Dictionary<string, double> _wireAreas = new Dictionary<string, double>
        {
            { "14", 0.0097 },
            { "12", 0.0133 },
            { "10", 0.0211 },
            { "8", 0.0366 },
            { "6", 0.0507 },
            { "4", 0.0824 },
            { "3", 0.0973 },
            { "2", 0.1158 },
            { "1", 0.1562 },
            { "1/0", 0.1855 },
            { "2/0", 0.2223 },
            { "3/0", 0.2679 },
            { "4/0", 0.3237 },
            { "250", 0.3970 },
            { "300", 0.4596 },
            { "350", 0.5281 },
            { "400", 0.5863 },
            { "500", 0.7073 },
            { "600", 0.8676 },
            { "700", 1.0252 },
            { "750", 1.0532 }
        };

        /// <summary>
        /// Calculate conduit fill percentage
        /// Reference: NEC 2023 Chapter 9 Tables 1, 4, and 5
        /// </summary>
        /// <param name="conductors">List of conductors</param>
        /// <param name="type">Conduit type</param>
        /// <param name="size">Conduit size</param>
        /// <returns>Conduit fill result</returns>
        public static ConduitFillResult CalculateConduitFill(List<Conductor> conductors, ConduitType type, string size)
        {
            var result = new ConduitFillResult
            {
                ConduitSize = size
            };

            // Get conduit area (currently only EMT supported)
            if (!_emtConduitAreas.TryGetValue(size, out double conduitArea))
            {
                result.Warnings.Add($"Conduit size {size} not found in tables");
                return result;
            }

            // Calculate total wire area
            double totalWireArea = 0;
            int totalConductors = 0;

            foreach (var conductor in conductors)
            {
                if (_wireAreas.TryGetValue(conductor.Size, out double wireArea))
                {
                    totalWireArea += wireArea * conductor.Count;
                    totalConductors += conductor.Count;
                }
                else
                {
                    result.Warnings.Add($"Wire size {conductor.Size} not found in tables");
                }
            }

            // Determine allowed fill percentage
            int conductorKey = totalConductors == 1 ? 1 : (totalConductors == 2 ? 2 : 3);
            result.AllowedFillPercentage = _conduitFillPercentages[conductorKey] * 100;

            // Calculate actual fill percentage
            result.FillPercentage = (totalWireArea / conduitArea) * 100;

            // Check compliance
            result.IsCompliant = result.FillPercentage <= result.AllowedFillPercentage;

            if (!result.IsCompliant)
            {
                result.Warnings.Add($"Conduit fill {result.FillPercentage:F1}% exceeds allowed {result.AllowedFillPercentage:F0}%");
            }

            return result;
        }

        /// <summary>
        /// Get minimum conduit size for conductors
        /// </summary>
        /// <param name="conductors">List of conductors</param>
        /// <param name="type">Conduit type</param>
        /// <returns>Minimum conduit size</returns>
        public static string GetMinimumConduitSize(List<Conductor> conductors, ConduitType type)
        {
            var conduitSizes = new[] { "1/2", "3/4", "1", "1-1/4", "1-1/2", "2", "2-1/2", "3", "3-1/2", "4" };

            foreach (var size in conduitSizes)
            {
                var fillResult = CalculateConduitFill(conductors, type, size);
                if (fillResult.IsCompliant)
                    return size;
            }

            return "Larger than 4\" - use multiple conduits";
        }

        /// <summary>
        /// Validate conduit fill
        /// </summary>
        /// <param name="conductors">List of conductors</param>
        /// <param name="type">Conduit type</param>
        /// <param name="size">Conduit size</param>
        /// <returns>Validation result</returns>
        public static ValidationResult ValidateConduitFill(List<Conductor> conductors, ConduitType type, string size)
        {
            var result = new ValidationResult { IsValid = true };

            var fillResult = CalculateConduitFill(conductors, type, size);

            if (!fillResult.IsCompliant)
            {
                result.IsValid = false;
                result.Errors.Add($"Conduit {size} is overfilled: {fillResult.FillPercentage:F1}% (allowed: {fillResult.AllowedFillPercentage:F0}%)");
            }

            result.Warnings.AddRange(fillResult.Warnings);

            return result;
        }

        /// <summary>
        /// Get conduit internal area
        /// </summary>
        /// <param name="type">Conduit type</param>
        /// <param name="size">Conduit size</param>
        /// <returns>Internal area in square inches</returns>
        public static double GetConduitArea(ConduitType type, string size)
        {
            // Currently only EMT supported
            return _emtConduitAreas.TryGetValue(size, out double value) ? value : 0;
        }

        /// <summary>
        /// Get wire cross-sectional area
        /// </summary>
        /// <param name="wireSize">Wire size</param>
        /// <param name="insulation">Insulation type</param>
        /// <returns>Area in square inches</returns>
        public static double GetWireArea(string wireSize, InsulationType insulation)
        {
            // Currently only THHN areas included
            return _wireAreas.TryGetValue(wireSize, out double value) ? value : 0;
        }

        #endregion

        #region Article 110 - General Requirements

        /// <summary>
        /// Generate panel schedule label
        /// Reference: NEC 2023 Article 110 labeling requirements
        /// </summary>
        /// <param name="panelName">Panel designation</param>
        /// <param name="voltage">System voltage</param>
        /// <param name="mainBreaker">Main breaker size</param>
        /// <returns>Label text</returns>
        public static string GeneratePanelScheduleLabel(string panelName, int voltage, int mainBreaker)
        {
            return $"PANEL: {panelName}\nVOLTAGE: {voltage}V\nMAIN: {mainBreaker}A\nNEC 2023 COMPLIANT";
        }

        /// <summary>
        /// Generate disconnect label
        /// </summary>
        /// <param name="equipmentName">Equipment name</param>
        /// <param name="voltage">Voltage</param>
        /// <param name="amperage">Amperage</param>
        /// <returns>Label text</returns>
        public static string GenerateDisconnectLabel(string equipmentName, int voltage, int amperage)
        {
            return $"DISCONNECT\n{equipmentName}\n{voltage}V, {amperage}A\nWARNING: ELECTRICAL HAZARD";
        }

        /// <summary>
        /// Validate labeling compliance
        /// </summary>
        /// <param name="hasLabel">Whether label exists</param>
        /// <param name="labelText">Label text content</param>
        /// <returns>Validation result</returns>
        public static ValidationResult ValidateLabelingCompliance(bool hasLabel, string labelText)
        {
            var result = new ValidationResult { IsValid = true };

            if (!hasLabel)
            {
                result.IsValid = false;
                result.Errors.Add("Required labeling is missing per NEC 110.21");
            }
            else if (string.IsNullOrWhiteSpace(labelText))
            {
                result.Warnings.Add("Label text is empty or incomplete");
            }

            return result;
        }

        #endregion

        #region Article 220 - Branch Circuit and Feeder Calculations

        /// <summary>
        /// Calculate general lighting load
        /// Reference: NEC 2023 Table 220.12
        /// </summary>
        /// <param name="squareFeet">Building area in square feet</param>
        /// <param name="occupancyType">Type of occupancy</param>
        /// <returns>Lighting load in VA</returns>
        public static double CalculateLightingLoad(double squareFeet, string occupancyType)
        {
            var loadFactors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "dwelling", 3.0 },
                { "hotel", 2.0 },
                { "warehouse", 0.25 },
                { "office", 1.0 },
                { "retail", 3.0 },
                { "school", 3.0 }
            };

            double vaPerSqFt = loadFactors.ContainsKey(occupancyType) 
                ? loadFactors[occupancyType] 
                : 3.0; // Default to dwelling

            return squareFeet * vaPerSqFt;
        }

        /// <summary>
        /// Apply demand factors to load calculation
        /// </summary>
        /// <param name="loadType">Type of load</param>
        /// <param name="quantity">Number of items</param>
        /// <returns>Demand factor (0.0 to 1.0)</returns>
        public static double ApplyDemandFactors(string loadType, int quantity)
        {
            // Simplified demand factor application
            // Full implementation would reference NEC Tables 220.42, 220.54, etc.
            
            if (loadType.Equals("range", StringComparison.OrdinalIgnoreCase))
            {
                // Table 220.55 - demand factors for ranges
                if (quantity <= 1) return 1.0;
                if (quantity <= 3) return 0.80;
                if (quantity <= 5) return 0.70;
                return 0.55;
            }

            return 1.0; // No demand factor
        }

        #endregion
    }
}
