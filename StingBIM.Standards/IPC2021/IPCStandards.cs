// FILE: IPCStandards.cs
// LOCATION: StingBIM.Standards/IPC2021/
// LINES: ~2000
// PURPOSE: IPC 2021 plumbing standards compliance and calculations

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingBIM.Standards.IPC2021
{
    #region Supporting Classes and Enums

    /// <summary>
    /// Fixture types for drainage calculations
    /// </summary>
    public enum FixtureType
    {
        WaterCloset,
        Lavatory,
        Shower,
        Bathtub,
        KitchenSink,
        Dishwasher,
        WashingMachine,
        FloorDrain,
        Urinal,
        ServiceSink,
        Drinking_Fountain
    }

    /// <summary>
    /// Pipe materials
    /// </summary>
    public enum PipeMaterial
    {
        PVC,
        CPVC,
        Copper,
        PEX,
        GalvanizedSteel,
        CastIron,
        ABS
    }

    /// <summary>
    /// Fixture unit assignment
    /// </summary>
    public class FixtureUnit
    {
        public FixtureType Type { get; set; }
        public double DrainageUnits { get; set; }
        public double WaterSupplyUnits { get; set; }
        public string TrapSize { get; set; }
        public string DrainSize { get; set; }
    }

    /// <summary>
    /// Pipe sizing result
    /// </summary>
    public class PipeSizeResult
    {
        public string Size { get; set; }
        public double Velocity { get; set; }  // fps
        public double PressureDrop { get; set; }  // psi/100ft
        public bool IsCompliant { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Drainage calculation result
    /// </summary>
    public class DrainageResult
    {
        public double TotalFixtureUnits { get; set; }
        public string MinimumPipeSize { get; set; }
        public double Slope { get; set; }  // inches per foot
        public bool IsCompliant { get; set; }
        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>
    /// Vent sizing result
    /// </summary>
    public class VentSizeResult
    {
        public string VentSize { get; set; }
        public string VentType { get; set; }
        public double MaximumLength { get; set; }  // feet
        public bool IsCompliant { get; set; }
        public List<string> Requirements { get; set; } = new List<string>();
    }

    /// <summary>
    /// Water supply calculation result
    /// </summary>
    public class WaterSupplyResult
    {
        public double TotalFixtureUnits { get; set; }
        public double FlowRate { get; set; }  // GPM
        public string MinimumPipeSize { get; set; }
        public double Pressure { get; set; }  // psi
        public bool IsCompliant { get; set; }
    }

    #endregion

    /// <summary>
    /// International Plumbing Code (IPC) 2021 standards implementation
    /// Includes fixture units, pipe sizing, drainage, venting, and water supply calculations
    /// </summary>
    public static class IPCStandards
    {
        public const string Version = "2021";

        #region Table 709.1 - Drainage Fixture Units

        /// <summary>
        /// Drainage fixture unit values per IPC 2021 Table 709.1
        /// </summary>
        private static readonly Dictionary<FixtureType, FixtureUnit> _fixtureUnits = new Dictionary<FixtureType, FixtureUnit>
        {
            { 
                FixtureType.WaterCloset, 
                new FixtureUnit { Type = FixtureType.WaterCloset, DrainageUnits = 4.0, WaterSupplyUnits = 6.0, TrapSize = "3\"", DrainSize = "3\"" }
            },
            { 
                FixtureType.Lavatory, 
                new FixtureUnit { Type = FixtureType.Lavatory, DrainageUnits = 1.0, WaterSupplyUnits = 1.0, TrapSize = "1-1/4\"", DrainSize = "1-1/2\"" }
            },
            { 
                FixtureType.Shower, 
                new FixtureUnit { Type = FixtureType.Shower, DrainageUnits = 2.0, WaterSupplyUnits = 2.0, TrapSize = "2\"", DrainSize = "2\"" }
            },
            { 
                FixtureType.Bathtub, 
                new FixtureUnit { Type = FixtureType.Bathtub, DrainageUnits = 2.0, WaterSupplyUnits = 4.0, TrapSize = "1-1/2\"", DrainSize = "1-1/2\"" }
            },
            { 
                FixtureType.KitchenSink, 
                new FixtureUnit { Type = FixtureType.KitchenSink, DrainageUnits = 2.0, WaterSupplyUnits = 2.0, TrapSize = "1-1/2\"", DrainSize = "1-1/2\"" }
            },
            { 
                FixtureType.Dishwasher, 
                new FixtureUnit { Type = FixtureType.Dishwasher, DrainageUnits = 2.0, WaterSupplyUnits = 2.0, TrapSize = "1-1/2\"", DrainSize = "1-1/2\"" }
            },
            { 
                FixtureType.WashingMachine, 
                new FixtureUnit { Type = FixtureType.WashingMachine, DrainageUnits = 3.0, WaterSupplyUnits = 4.0, TrapSize = "2\"", DrainSize = "2\"" }
            },
            { 
                FixtureType.FloorDrain, 
                new FixtureUnit { Type = FixtureType.FloorDrain, DrainageUnits = 2.0, WaterSupplyUnits = 0.0, TrapSize = "2\"", DrainSize = "2\"" }
            },
            { 
                FixtureType.Urinal, 
                new FixtureUnit { Type = FixtureType.Urinal, DrainageUnits = 4.0, WaterSupplyUnits = 3.0, TrapSize = "2\"", DrainSize = "2\"" }
            },
            { 
                FixtureType.ServiceSink, 
                new FixtureUnit { Type = FixtureType.ServiceSink, DrainageUnits = 3.0, WaterSupplyUnits = 3.0, TrapSize = "2\"", DrainSize = "2\"" }
            },
            { 
                FixtureType.Drinking_Fountain, 
                new FixtureUnit { Type = FixtureType.Drinking_Fountain, DrainageUnits = 0.5, WaterSupplyUnits = 0.25, TrapSize = "1-1/4\"", DrainSize = "1-1/4\"" }
            }
        };

        /// <summary>
        /// Get fixture unit values for a fixture type
        /// Reference: IPC 2021 Table 709.1
        /// </summary>
        /// <param name="fixtureType">Type of fixture</param>
        /// <returns>Fixture unit information</returns>
        public static FixtureUnit GetFixtureUnits(FixtureType fixtureType)
        {
            return _fixtureUnits.TryGetValue(fixtureType, out FixtureUnit value) 
                ? value 
                : new FixtureUnit { Type = fixtureType, DrainageUnits = 1.0, WaterSupplyUnits = 1.0 };
        }

        /// <summary>
        /// Calculate total drainage fixture units
        /// </summary>
        /// <param name="fixtures">Dictionary of fixture types and quantities</param>
        /// <returns>Total drainage fixture units</returns>
        public static double CalculateTotalDrainageUnits(Dictionary<FixtureType, int> fixtures)
        {
            double total = 0;
            foreach (var fixture in fixtures)
            {
                var units = GetFixtureUnits(fixture.Key);
                total += units.DrainageUnits * fixture.Value;
            }
            return total;
        }

        /// <summary>
        /// Calculate total water supply fixture units
        /// </summary>
        /// <param name="fixtures">Dictionary of fixture types and quantities</param>
        /// <returns>Total water supply fixture units</returns>
        public static double CalculateTotalWaterSupplyUnits(Dictionary<FixtureType, int> fixtures)
        {
            double total = 0;
            foreach (var fixture in fixtures)
            {
                var units = GetFixtureUnits(fixture.Key);
                total += units.WaterSupplyUnits * fixture.Value;
            }
            return total;
        }

        #endregion

        #region Table 710.1 - Drainage Pipe Sizing

        /// <summary>
        /// Maximum fixture units for drainage pipes
        /// Reference: IPC 2021 Table 710.1(2) - Horizontal Fixture Branches and Stacks
        /// </summary>
        private static readonly Dictionary<string, (double horizontal1_4, double horizontal1_2, double stack, double maxLength)> _drainagePipeCapacity = 
            new Dictionary<string, (double, double, double, double)>
        {
            // Pipe Size, (1/4" slope, 1/2" slope, Stack capacity, Max length ft)
            { "1-1/4\"", (1, 1, 2, 0) },
            { "1-1/2\"", (3, 3, 4, 0) },
            { "2\"", (6, 6, 10, 0) },
            { "2-1/2\"", (12, 12, 20, 0) },
            { "3\"", (20, 27, 48, 0) },
            { "4\"", (160, 216, 256, 0) },
            { "5\"", (360, 480, 600, 0) },
            { "6\"", (620, 840, 1380, 0) },
            { "8\"", (1400, 1920, 3600, 0) },
            { "10\"", (2500, 3500, 5600, 0) },
            { "12\"", (3900, 5600, 8400, 0) }
        };

        /// <summary>
        /// Get minimum drainage pipe size for fixture units
        /// Reference: IPC 2021 Table 710.1
        /// </summary>
        /// <param name="fixtureUnits">Total drainage fixture units</param>
        /// <param name="slope">Pipe slope (inches per foot)</param>
        /// <param name="isStack">True if sizing a stack</param>
        /// <returns>Minimum pipe size</returns>
        public static string GetMinimumDrainPipeSize(double fixtureUnits, double slope, bool isStack = false)
        {
            // Determine which capacity column to use based on slope
            int slopeIndex = 0; // Default to 1/4" slope
            if (slope >= 0.5)
                slopeIndex = 1; // 1/2" slope

            foreach (var pipe in _drainagePipeCapacity.OrderBy(p => p.Key))
            {
                double capacity = isStack ? pipe.Value.stack : (slopeIndex == 0 ? pipe.Value.horizontal1_4 : pipe.Value.horizontal1_2);
                
                if (capacity >= fixtureUnits)
                    return pipe.Key;
            }

            return "12\""; // Maximum standard size
        }

        /// <summary>
        /// Calculate drainage system
        /// </summary>
        /// <param name="fixtures">Dictionary of fixtures and quantities</param>
        /// <param name="slope">Pipe slope (inches per foot)</param>
        /// <param name="isStack">True if sizing a stack</param>
        /// <returns>Drainage calculation result</returns>
        public static DrainageResult CalculateDrainageSystem(Dictionary<FixtureType, int> fixtures, double slope, bool isStack = false)
        {
            var result = new DrainageResult
            {
                TotalFixtureUnits = CalculateTotalDrainageUnits(fixtures),
                Slope = slope
            };

            result.MinimumPipeSize = GetMinimumDrainPipeSize(result.TotalFixtureUnits, slope, isStack);

            // Validate slope
            if (slope < 0.125 && !isStack)
            {
                result.IsCompliant = false;
                result.Notes.Add("Minimum slope is 1/8\" per foot for drainage pipes");
            }
            else if (slope > 0.25 && !isStack)
            {
                result.Notes.Add("Slope exceeds 1/4\" per foot - verify with local code");
            }
            else
            {
                result.IsCompliant = true;
            }

            return result;
        }

        #endregion

        #region Table 903.1 - Vent Sizing

        /// <summary>
        /// Vent pipe sizing table
        /// Reference: IPC 2021 Table 916.1 - Size and Length of Vents
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, double>> _ventSizing = new Dictionary<string, Dictionary<string, double>>
        {
            // Drain size -> Vent size -> Max length (feet)
            { "1-1/4\"", new Dictionary<string, double> { { "1-1/4\"", 45 }, { "1-1/2\"", 60 } } },
            { "1-1/2\"", new Dictionary<string, double> { { "1-1/4\"", 30 }, { "1-1/2\"", 50 }, { "2\"", 200 } } },
            { "2\"", new Dictionary<string, double> { { "1-1/2\"", 30 }, { "2\"", 75 }, { "2-1/2\"", 200 } } },
            { "3\"", new Dictionary<string, double> { { "2\"", 42 }, { "2-1/2\"", 150 }, { "3\"", 212 } } },
            { "4\"", new Dictionary<string, double> { { "2-1/2\"", 50 }, { "3\"", 180 }, { "4\"", 1000 } } }
        };

        /// <summary>
        /// Get vent size for drainage pipe
        /// Reference: IPC 2021 Section 916
        /// </summary>
        /// <param name="drainSize">Drainage pipe size</param>
        /// <param name="ventLength">Vent pipe length (feet)</param>
        /// <returns>Minimum vent size</returns>
        public static string GetVentSize(string drainSize, double ventLength)
        {
            if (!_ventSizing.ContainsKey(drainSize))
                return drainSize; // Default to drain size if not in table

            var ventOptions = _ventSizing[drainSize];
            
            foreach (var vent in ventOptions.OrderBy(v => v.Key))
            {
                if (ventLength <= vent.Value)
                    return vent.Key;
            }

            return drainSize; // Return drain size if length exceeds all options
        }

        /// <summary>
        /// Calculate vent requirements
        /// </summary>
        /// <param name="fixtureType">Type of fixture</param>
        /// <param name="drainSize">Drain pipe size</param>
        /// <param name="ventLength">Vent pipe length (feet)</param>
        /// <returns>Vent sizing result</returns>
        public static VentSizeResult CalculateVentRequirements(FixtureType fixtureType, string drainSize, double ventLength)
        {
            var result = new VentSizeResult
            {
                VentSize = GetVentSize(drainSize, ventLength)
            };

            // Determine vent type based on fixture
            if (fixtureType == FixtureType.WaterCloset)
            {
                result.VentType = "Wet Vent or Individual Vent";
                result.Requirements.Add("Water closet requires individual or wet vent");
            }
            else
            {
                result.VentType = "Individual or Common Vent";
            }

            // Maximum vent length validation
            if (_ventSizing.ContainsKey(drainSize) && _ventSizing[drainSize].ContainsKey(result.VentSize))
            {
                result.MaximumLength = _ventSizing[drainSize][result.VentSize];
                result.IsCompliant = ventLength <= result.MaximumLength;

                if (!result.IsCompliant)
                {
                    result.Requirements.Add($"Vent length {ventLength} ft exceeds maximum {result.MaximumLength} ft for {result.VentSize} vent");
                }
            }
            else
            {
                result.IsCompliant = true;
                result.Requirements.Add("Consult IPC Table 916.1 for specific vent sizing");
            }

            // Additional requirements
            result.Requirements.Add("Vent must extend through roof or connect to vent stack");
            result.Requirements.Add("Minimum vent size is 1-1/4\" diameter");

            return result;
        }

        #endregion

        #region Table 604.3 - Water Supply Sizing

        /// <summary>
        /// Water supply pipe sizing based on fixture units
        /// Reference: IPC 2021 Table 610.4 - Water Distribution System Design Criteria
        /// </summary>
        private static readonly Dictionary<string, double> _waterSupplyCapacity = new Dictionary<string, double>
        {
            // Pipe size -> Maximum fixture units
            { "1/2\"", 4 },
            { "3/4\"", 10 },
            { "1\"", 30 },
            { "1-1/4\"", 50 },
            { "1-1/2\"", 75 },
            { "2\"", 150 },
            { "2-1/2\"", 250 },
            { "3\"", 500 }
        };

        /// <summary>
        /// Convert fixture units to flow rate
        /// Reference: Hunter's Curve approximation
        /// </summary>
        /// <param name="fixtureUnits">Total fixture units</param>
        /// <returns>Flow rate in GPM</returns>
        public static double ConvertFixtureUnitsToGPM(double fixtureUnits)
        {
            // Hunter's Curve approximation: GPM = sqrt(WSFU - 1) + 1
            if (fixtureUnits <= 0)
                return 0;

            if (fixtureUnits <= 1)
                return 1.0;

            return Math.Sqrt(fixtureUnits - 1) + 1;
        }

        /// <summary>
        /// Get minimum water supply pipe size
        /// </summary>
        /// <param name="fixtureUnits">Total water supply fixture units</param>
        /// <returns>Minimum pipe size</returns>
        public static string GetMinimumWaterSupplySize(double fixtureUnits)
        {
            foreach (var pipe in _waterSupplyCapacity.OrderBy(p => p.Key))
            {
                if (pipe.Value >= fixtureUnits)
                    return pipe.Key;
            }

            return "3\""; // Maximum standard size
        }

        /// <summary>
        /// Calculate water supply requirements
        /// </summary>
        /// <param name="fixtures">Dictionary of fixtures and quantities</param>
        /// <param name="staticPressure">Available static pressure (psi)</param>
        /// <returns>Water supply calculation result</returns>
        public static WaterSupplyResult CalculateWaterSupply(Dictionary<FixtureType, int> fixtures, double staticPressure = 50)
        {
            var result = new WaterSupplyResult
            {
                TotalFixtureUnits = CalculateTotalWaterSupplyUnits(fixtures),
                Pressure = staticPressure
            };

            result.FlowRate = ConvertFixtureUnitsToGPM(result.TotalFixtureUnits);
            result.MinimumPipeSize = GetMinimumWaterSupplySize(result.TotalFixtureUnits);

            // Validate pressure
            const double MinimumPressure = 15; // psi minimum at fixtures
            const double MaximumPressure = 80; // psi maximum

            if (staticPressure < MinimumPressure)
            {
                result.IsCompliant = false;
            }
            else if (staticPressure > MaximumPressure)
            {
                result.IsCompliant = false; // Pressure reducing valve required
            }
            else
            {
                result.IsCompliant = true;
            }

            return result;
        }

        /// <summary>
        /// Calculate water velocity in pipe
        /// </summary>
        /// <param name="flowRate">Flow rate (GPM)</param>
        /// <param name="pipeSize">Pipe size (inches)</param>
        /// <returns>Velocity in feet per second</returns>
        public static double CalculateWaterVelocity(double flowRate, double pipeSize)
        {
            // V = (0.4085 × GPM) / D²
            // where V is velocity in fps, GPM is flow rate, D is diameter in inches
            return (0.4085 * flowRate) / Math.Pow(pipeSize, 2);
        }

        /// <summary>
        /// Calculate pressure drop in pipe
        /// Reference: Hazen-Williams equation
        /// </summary>
        /// <param name="flowRate">Flow rate (GPM)</param>
        /// <param name="pipeSize">Pipe size (inches)</param>
        /// <param name="length">Pipe length (feet)</param>
        /// <param name="cFactor">Hazen-Williams C factor (120-150 typical)</param>
        /// <returns>Pressure drop in psi</returns>
        public static double CalculatePressureDrop(double flowRate, double pipeSize, double length, double cFactor = 130)
        {
            // Hazen-Williams: P = (0.2083 × (100/C)^1.852 × Q^1.852 × L) / D^4.8655
            // where P is pressure drop in psi, Q is GPM, L is length in feet, D is diameter in inches
            
            double numerator = 0.2083 * Math.Pow(100.0 / cFactor, 1.852) * Math.Pow(flowRate, 1.852) * length;
            double denominator = Math.Pow(pipeSize, 4.8655);
            
            return numerator / denominator;
        }

        #endregion

        #region Pipe Sizing Validation

        /// <summary>
        /// Size water supply pipe for flow and velocity
        /// </summary>
        /// <param name="flowRate">Required flow rate (GPM)</param>
        /// <param name="maxVelocity">Maximum allowable velocity (fps) - typically 8-10</param>
        /// <returns>Pipe size result</returns>
        public static PipeSizeResult SizeWaterPipe(double flowRate, double maxVelocity = 8.0)
        {
            var result = new PipeSizeResult();

            var pipeSizes = new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 2.5, 3.0, 4.0 };

            foreach (var size in pipeSizes)
            {
                double velocity = CalculateWaterVelocity(flowRate, size);
                
                if (velocity <= maxVelocity)
                {
                    result.Size = size < 1 ? $"{size * 2}/4\"" : $"{size}\"";
                    result.Velocity = velocity;
                    result.PressureDrop = CalculatePressureDrop(flowRate, size, 100); // Per 100 ft
                    result.IsCompliant = true;
                    
                    if (velocity < 2.0)
                    {
                        result.Warnings.Add($"Velocity {velocity:F2} fps is very low - risk of sediment buildup");
                    }
                    
                    return result;
                }
            }

            // If we get here, no pipe size works
            result.Size = "4\"";
            result.Velocity = CalculateWaterVelocity(flowRate, 4.0);
            result.PressureDrop = CalculatePressureDrop(flowRate, 4.0, 100);
            result.IsCompliant = false;
            result.Warnings.Add($"Velocity {result.Velocity:F2} fps exceeds maximum {maxVelocity} fps");

            return result;
        }

        /// <summary>
        /// Validate pipe size for material and application
        /// </summary>
        /// <param name="pipeSize">Pipe size</param>
        /// <param name="material">Pipe material</param>
        /// <param name="isDrainage">True if drainage pipe, false if water supply</param>
        /// <returns>True if valid combination</returns>
        public static bool ValidatePipeMaterial(string pipeSize, PipeMaterial material, bool isDrainage)
        {
            // Simplified validation - full implementation would check code requirements
            if (isDrainage)
            {
                // Drainage pipes
                return material == PipeMaterial.PVC || 
                       material == PipeMaterial.ABS || 
                       material == PipeMaterial.CastIron;
            }
            else
            {
                // Water supply pipes
                return material == PipeMaterial.Copper || 
                       material == PipeMaterial.PEX || 
                       material == PipeMaterial.CPVC;
            }
        }

        #endregion

        #region Hot Water System

        /// <summary>
        /// Calculate hot water heater size
        /// Reference: IPC 2021 Section 607
        /// </summary>
        /// <param name="fixtures">Dictionary of fixtures and quantities</param>
        /// <param name="recoveryRate">Recovery rate (GPH at 100°F rise)</param>
        /// <returns>Minimum tank size in gallons</returns>
        public static double CalculateHotWaterHeaterSize(Dictionary<FixtureType, int> fixtures, double recoveryRate = 40)
        {
            // Simplified calculation based on fixture count
            int totalFixtures = fixtures.Sum(f => f.Value);
            
            // Rule of thumb: 10-15 gallons per fixture
            double minimumCapacity = totalFixtures * 12;

            // Adjust for recovery rate
            double peakDemand = ConvertFixtureUnitsToGPM(CalculateTotalWaterSupplyUnits(fixtures));
            double storageNeeded = (peakDemand * 60) - recoveryRate;

            return Math.Max(minimumCapacity, storageNeeded);
        }

        /// <summary>
        /// Calculate hot water circulation pump size
        /// </summary>
        /// <param name="pipeLength">Total pipe length (feet)</param>
        /// <param name="pipeSize">Pipe size (inches)</param>
        /// <returns>Minimum flow rate in GPM</returns>
        public static double CalculateCirculationPumpSize(double pipeLength, double pipeSize)
        {
            // Heat loss approximation: 1-2 GPM per 100 feet of pipe
            return (pipeLength / 100.0) * 1.5;
        }

        #endregion

        #region Trap Requirements

        /// <summary>
        /// Get trap seal depth requirement
        /// Reference: IPC 2021 Section 1002
        /// </summary>
        /// <param name="fixtureType">Type of fixture</param>
        /// <returns>Minimum and maximum trap seal depth (inches)</returns>
        public static (double minimum, double maximum) GetTrapSealDepth(FixtureType fixtureType)
        {
            // Standard trap seal: 2" minimum, 4" maximum
            return (2.0, 4.0);
        }

        /// <summary>
        /// Validate trap distance from fixture
        /// </summary>
        /// <param name="trapSize">Trap size</param>
        /// <param name="distance">Distance from fixture outlet to trap weir (inches)</param>
        /// <returns>True if compliant</returns>
        public static bool ValidateTrapDistance(string trapSize, double distance)
        {
            // Maximum trap distance per IPC Table 1002.2
            var maxDistances = new Dictionary<string, double>
            {
                { "1-1/4\"", 30 },  // 30 inches
                { "1-1/2\"", 42 },  // 42 inches
                { "2\"", 60 },      // 60 inches
                { "3\"", 72 },      // 72 inches
                { "4\"", 120 }      // 120 inches
            };

            if (maxDistances.TryGetValue(trapSize, out double maxDistance))
            {
                return distance <= maxDistance;
            }

            return false; // Unknown size - require verification
        }

        #endregion

        #region Gas Pipe Sizing

        /// <summary>
        /// Size natural gas pipe (simplified)
        /// Reference: International Fuel Gas Code (IFGC)
        /// </summary>
        /// <param name="btuhDemand">Total BTU/hr demand</param>
        /// <param name="pipeLength">Pipe length (feet)</param>
        /// <param name="pressureDrop">Allowable pressure drop (inches w.c.)</param>
        /// <returns>Minimum pipe size</returns>
        public static string SizeGasPipe(double btuhDemand, double pipeLength, double pressureDrop = 0.5)
        {
            // Simplified gas pipe sizing - full calculation requires capacity tables
            // This is a rough approximation
            
            double cfh = btuhDemand / 1000.0; // Convert BTU/hr to approximate CFH

            if (pipeLength <= 100 && cfh <= 275)
                return "1\"";
            else if (pipeLength <= 100 && cfh <= 730)
                return "1-1/4\"";
            else if (pipeLength <= 100 && cfh <= 1400)
                return "1-1/2\"";
            else if (pipeLength <= 100 && cfh <= 2100)
                return "2\"";

            return "Consult IFGC capacity tables";
        }

        #endregion
    }
}
