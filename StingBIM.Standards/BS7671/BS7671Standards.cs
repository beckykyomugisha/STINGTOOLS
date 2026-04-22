// FILE: BS7671Standards.cs - BS 7671:2018+A2:2022 UK Wiring Regulations
// PRIMARY ELECTRICAL STANDARD FOR UGANDA
// LINES: ~2000

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingBIM.Standards.BS7671
{
    public enum EarthingSystem { TN_S, TN_C_S, TT, IT }
    public enum MCBType { Type_B, Type_C, Type_D }
    public enum InstallationMethod { Method_C, Method_A1, Method_B1, Method_D1, Method_E }
    
    public class BathroomZone
    {
        public string Zone { get; set; }
        public string IPRating { get; set; }
        public bool RCDRequired { get; set; }
        public List<string> PermittedEquipment { get; set; } = new List<string>();
    }
    
    public class VoltageDropResult
    {
        public double ActualDropVolts { get; set; }
        public double ActualDropPercent { get; set; }
        public double MaxAllowedVolts { get; set; }
        public double MaxAllowedPercent { get; set; }
        public bool IsCompliant { get; set; }
        public string Notes { get; set; }
    }
    
    /// <summary>
    /// BS 7671:2018+A2:2022 UK Electrical Wiring Regulations (18th Edition)
    /// PRIMARY ELECTRICAL STANDARD FOR UGANDA (Commonwealth)
    /// </summary>
    public static class BS7671Standards
    {
        public const string Version = "18th Edition + AMD 2:2022";
        public const string Standard = "BS 7671";
        
        // Maximum disconnection times (seconds) - Table 41.1
        public static double GetMaximumDisconnectionTime(double nominalVoltageToEarth, EarthingSystem system)
        {
            if (system == EarthingSystem.TN_S || system == EarthingSystem.TN_C_S)
            {
                if (nominalVoltageToEarth <= 50) return 5.0;
                if (nominalVoltageToEarth <= 120) return 0.8;
                if (nominalVoltageToEarth <= 230) return 0.4;
                if (nominalVoltageToEarth <= 400) return 0.2;
                return 0.1;
            }
            if (system == EarthingSystem.TT)
            {
                if (nominalVoltageToEarth <= 50) return 5.0;
                if (nominalVoltageToEarth <= 120) return 0.3;
                if (nominalVoltageToEarth <= 230) return 0.2;
                if (nominalVoltageToEarth <= 400) return 0.07;
                return 0.04;
            }
            return 0.4;
        }
        
        // Maximum Zs (Earth Fault Loop Impedance) - Appendix 3
        public static double GetMaximumZs(double deviceRating, MCBType mcbType, double nominalVoltage = 230)
        {
            if (mcbType == MCBType.Type_B)
            {
                var zsLimits = new Dictionary<double, double>
                {
                    { 6, 7.67 }, { 10, 4.60 }, { 16, 2.87 }, { 20, 2.30 }, { 25, 1.84 },
                    { 32, 1.44 }, { 40, 1.15 }, { 50, 0.92 }, { 63, 0.73 }, { 80, 0.57 }, { 100, 0.46 }
                };
                if (zsLimits.TryGetValue(deviceRating, out double zs)) return zs;
            }
            if (mcbType == MCBType.Type_C)
            {
                var zsLimits = new Dictionary<double, double>
                {
                    { 6, 3.83 }, { 10, 2.30 }, { 16, 1.44 }, { 20, 1.15 }, { 25, 0.92 },
                    { 32, 0.72 }, { 40, 0.57 }, { 50, 0.46 }, { 63, 0.36 }, { 80, 0.29 }, { 100, 0.23 }
                };
                if (zsLimits.TryGetValue(deviceRating, out double zs)) return zs;
            }
            if (mcbType == MCBType.Type_D) return GetMaximumZs(deviceRating, MCBType.Type_C) * 0.5;
            return nominalVoltage / (5 * deviceRating);
        }
        
        // Minimum conductor sizes - Regulation 525.1
        public static double GetMinimumConductorSize(string circuitType)
        {
            var sizes = new Dictionary<string, double>
            {
                { "Lighting_Fixed", 1.0 }, { "Lighting_Pendant", 1.5 }, { "Power_Socket_Ring", 2.5 },
                { "Power_Socket_Radial_20A", 2.5 }, { "Power_Socket_Radial_32A", 4.0 },
                { "Power_Fixed_Equipment", 1.5 }, { "Cooker_30A", 6.0 }, { "Shower_Electric_40A", 10.0 },
                { "Immersion_Heater", 2.5 }, { "Storage_Heater", 2.5 }, { "Air_Conditioning", 2.5 }
            };
            return sizes.TryGetValue(circuitType, out double size) ? size : 1.5;
        }
        
        // Current carrying capacity - Appendix 4 Table 4D5A (Method C)
        public static double GetCurrentCarryingCapacity(double csa, int numberOfCores, InstallationMethod method = InstallationMethod.Method_C)
        {
            if (method == InstallationMethod.Method_C)
            {
                if (numberOfCores == 2)
                {
                    var capacity = new Dictionary<double, double>
                    {
                        { 1.0, 13.5 }, { 1.5, 17.5 }, { 2.5, 24.0 }, { 4.0, 32.0 }, { 6.0, 41.0 },
                        { 10.0, 57.0 }, { 16.0, 76.0 }, { 25.0, 101.0 }, { 35.0, 125.0 }, { 50.0, 151.0 },
                        { 70.0, 192.0 }, { 95.0, 232.0 }, { 120.0, 269.0 }, { 150.0, 309.0 },
                        { 185.0, 354.0 }, { 240.0, 415.0 }
                    };
                    return capacity.TryGetValue(csa, out double amps) ? amps : 0;
                }
                if (numberOfCores >= 3)
                {
                    var capacity = new Dictionary<double, double>
                    {
                        { 1.5, 15.5 }, { 2.5, 21.0 }, { 4.0, 28.0 }, { 6.0, 36.0 }, { 10.0, 50.0 },
                        { 16.0, 68.0 }, { 25.0, 89.0 }, { 35.0, 110.0 }, { 50.0, 134.0 }, { 70.0, 171.0 },
                        { 95.0, 207.0 }, { 120.0, 239.0 }, { 150.0, 275.0 }, { 185.0, 315.0 }, { 240.0, 370.0 }
                    };
                    return capacity.TryGetValue(csa, out double amps) ? amps : 0;
                }
            }
            return 0;
        }
        
        // Voltage drop (mV/A/m) - Appendix 4 Table 4D1B
        public static double GetVoltageDrop(double csa, int phases = 1)
        {
            if (phases == 1)
            {
                var vdrop = new Dictionary<double, double>
                {
                    { 1.0, 44.0 }, { 1.5, 29.0 }, { 2.5, 18.0 }, { 4.0, 11.0 }, { 6.0, 7.3 },
                    { 10.0, 4.4 }, { 16.0, 2.8 }, { 25.0, 1.75 }, { 35.0, 1.25 }, { 50.0, 0.93 },
                    { 70.0, 0.65 }, { 95.0, 0.49 }, { 120.0, 0.39 }, { 150.0, 0.31 }
                };
                return vdrop.TryGetValue(csa, out double drop) ? drop : 1.0;
            }
            if (phases == 3) return GetVoltageDrop(csa, 1) * 0.866;
            return 1.0;
        }
        
        // Calculate voltage drop with compliance check
        public static VoltageDropResult CalculateVoltageDrop(double csa, double current, double lengthMeters, 
            string circuitType, int phases = 1, double supplyVoltage = 230)
        {
            double vdPerMeter = GetVoltageDrop(csa, phases);
            double actualDropVolts = (vdPerMeter * current * lengthMeters) / 1000.0;
            bool isLighting = circuitType.Contains("Lighting");
            double maxDropPercent = isLighting ? 3.0 : 5.0;
            double maxDropVolts = (supplyVoltage * maxDropPercent) / 100.0;
            
            return new VoltageDropResult
            {
                ActualDropVolts = actualDropVolts,
                ActualDropPercent = (actualDropVolts / supplyVoltage) * 100.0,
                MaxAllowedVolts = maxDropVolts,
                MaxAllowedPercent = maxDropPercent,
                IsCompliant = actualDropVolts <= maxDropVolts,
                Notes = isLighting ? "Lighting circuit (3% max)" : "Power circuit (5% max)"
            };
        }
        
        // Derating factors - Appendix 4 Table 4C1
        public static double GetGroupingFactor(int numberOfCircuits, InstallationMethod method)
        {
            if (method == InstallationMethod.Method_C)
            {
                if (numberOfCircuits == 1) return 1.00;
                if (numberOfCircuits == 2) return 0.80;
                if (numberOfCircuits == 3) return 0.70;
                if (numberOfCircuits == 4) return 0.65;
                if (numberOfCircuits <= 6) return 0.60;
                if (numberOfCircuits <= 9) return 0.55;
                return 0.50;
            }
            return 1.0;
        }
        
        // Temperature correction - Appendix 4 Table 4B1
        public static double GetTemperatureCorrectionFactor(double ambientTempC, int conductorRating = 70)
        {
            if (conductorRating == 70)
            {
                if (ambientTempC <= 25) return 1.03;
                if (ambientTempC <= 30) return 1.00;
                if (ambientTempC <= 35) return 0.94;
                if (ambientTempC <= 40) return 0.87;
                if (ambientTempC <= 45) return 0.79;
                if (ambientTempC <= 50) return 0.71;
                if (ambientTempC <= 55) return 0.61;
                if (ambientTempC <= 60) return 0.50;
            }
            return 1.0;
        }
        
        // Ring final circuit validation - Appendix 15
        public static bool ValidateRingFinalCircuit(double floorAreaSqM, double cableSize)
        {
            return cableSize == 2.5 && floorAreaSqM <= 100.0;
        }
        
        public static (double maxR1, double maxRn, double maxR2) GetRingResistanceLimits(double cableSize)
        {
            if (cableSize == 2.5) return (1.67, 1.67, 1.67);
            return (0, 0, 0);
        }
        
        // Bathroom zones - Section 701
        public static BathroomZone GetBathroomZone(double heightAboveBath, double horizontalFromBath)
        {
            var zone = new BathroomZone();
            if (heightAboveBath <= 0)
            {
                zone.Zone = "Zone_0";
                zone.IPRating = "IPX7";
                zone.RCDRequired = true;
                zone.PermittedEquipment.Add("SELV ≤12V only");
                return zone;
            }
            if (heightAboveBath <= 2.25 && horizontalFromBath <= 1.2)
            {
                zone.Zone = "Zone_1";
                zone.IPRating = "IPX4";
                zone.RCDRequired = true;
                zone.PermittedEquipment.AddRange(new[] { "Water heaters", "Whirlpool units", "SELV ≤25V" });
                return zone;
            }
            if (heightAboveBath <= 2.25 && horizontalFromBath <= 1.8)
            {
                zone.Zone = "Zone_2";
                zone.IPRating = "IPX4";
                zone.RCDRequired = true;
                zone.PermittedEquipment.AddRange(new[] { "Luminaires", "Fans", "Heating appliances", "Shaver units" });
                return zone;
            }
            zone.Zone = "Outside_Zones";
            zone.IPRating = "No specific requirement";
            zone.RCDRequired = false;
            zone.PermittedEquipment.AddRange(new[] { "Standard equipment", "Socket outlets (with RCD)" });
            return zone;
        }
        
        // RCD requirements - Regulation 411.3.3
        public static bool IsRCDRequired(string circuitType, string location, double rating)
        {
            if (circuitType.Contains("Socket") && rating <= 32) return true;
            if (location.Contains("Outdoor") && rating <= 32) return true;
            if (location.Contains("Underground_Shallow")) return true;
            if (location.Contains("Bathroom")) return true;
            return false;
        }
        
        // Insulation resistance - Table 61
        public static double GetMinimumInsulationResistance(double testVoltage, double nominalCircuitVoltage)
        {
            if (nominalCircuitVoltage <= 50 && testVoltage == 250) return 0.5;
            if (nominalCircuitVoltage <= 500 && testVoltage == 500) return 1.0;
            if (nominalCircuitVoltage <= 1000 && testVoltage == 1000) return 1.0;
            return 1.0;
        }
        
        // Earth electrode resistance - Regulation 411.5.3
        public static double GetMaximumEarthElectrodeResistance(double rcdRating = 30)
        {
            return 50000.0 / rcdRating; // Ra × IΔn ≤ 50V
        }
        
        // Prospective fault current validation
        public static bool ValidateProspectiveFaultCurrent(double measuredPFC, double deviceRating)
        {
            double deviceCapacity = deviceRating >= 63 ? 10000 : 6000;
            return measuredPFC <= deviceCapacity;
        }
        
        // Main bonding conductor - Table 54.7
        public static double GetMainBondingConductorSize(double supplyConductorSize)
        {
            double calculatedSize = supplyConductorSize / 2.0;
            if (calculatedSize < 6.0) return 6.0;
            if (calculatedSize > 25.0) return 25.0;
            double[] standardSizes = { 6, 10, 16, 25 };
            var result = standardSizes.FirstOrDefault(s => s >= calculatedSize);
            return result > 0 ? result : 25;
        }
        
        // Supplementary bonding - Regulation 544.2
        public static double GetSupplementaryBondingSize(double cpcSize)
        {
            double minSize = cpcSize / 2.0;
            return minSize < 2.5 ? 2.5 : minSize;
        }
        
        // Circuit protective conductor (CPC) - Table 54.7
        public static double GetCPCSize(double phaseConductorSize)
        {
            if (phaseConductorSize <= 16.0) return phaseConductorSize;
            if (phaseConductorSize <= 35.0) return 16.0;
            return phaseConductorSize / 2.0;
        }
        
        // Zs calculation
        public static double CalculateZs(double Ze, double R1, double R2)
        {
            return Ze + R1 + R2;
        }
    }
}
