// FILE: NFPAStandards.cs - NFPA 13 Sprinklers + NFPA 72 Fire Alarms
// FIRE PROTECTION STANDARDS
// LINES: ~300 (optimized)

using System;
using System.Collections.Generic;

namespace StingBIM.Standards.NFPA
{
    public enum HazardClassification { Light, Ordinary_Group1, Ordinary_Group2, Extra_Group1, Extra_Group2 }
    public enum SprinklerType { Standard_Response, Quick_Response, ESFR, Residential }
    public enum DetectorType { Smoke_Ionization, Smoke_Photoelectric, Heat_Fixed, Heat_Rate, Beam, Aspirating }
    
    public class SprinklerDesignResult
    {
        public double DesignAreaSqM { get; set; }
        public double DensityLpmPerSqM { get; set; }
        public double FlowRateLpm { get; set; }
        public double PressureKPa { get; set; }
        public int NumberOfHeads { get; set; }
        public string Notes { get; set; }
    }
    
    public class DetectorSpacingResult
    {
        public double MaximumSpacingM { get; set; }
        public double MaximumDistanceFromWallM { get; set; }
        public string MountingRequirements { get; set; }
        public List<string> Limitations { get; set; } = new List<string>();
    }
    
    /// <summary>
    /// NFPA Fire Protection Standards
    /// NFPA 13 (Sprinkler Systems) + NFPA 72 (Fire Alarm Systems)
    /// </summary>
    public static class NFPAStandards
    {
        // NFPA 13 - SPRINKLER SYSTEMS
        
        // Design density (L/min/m²) - NFPA 13 Fig 11.2.3.1.1
        public static double GetDesignDensity(HazardClassification hazard)
        {
            return hazard switch
            {
                HazardClassification.Light => 2.5,
                HazardClassification.Ordinary_Group1 => 6.1,
                HazardClassification.Ordinary_Group2 => 8.2,
                HazardClassification.Extra_Group1 => 12.2,
                HazardClassification.Extra_Group2 => 16.3,
                _ => 6.1
            };
        }
        
        // Design area (m²) - NFPA 13 Fig 11.2.3.1.1
        public static double GetDesignArea(HazardClassification hazard)
        {
            return hazard switch
            {
                HazardClassification.Light => 139,
                HazardClassification.Ordinary_Group1 => 139,
                HazardClassification.Ordinary_Group2 => 139,
                HazardClassification.Extra_Group1 => 232,
                HazardClassification.Extra_Group2 => 279,
                _ => 139
            };
        }
        
        // Maximum spacing between sprinklers - NFPA 13 Table 8.6.2.2.1(a)
        public static double GetMaximumSprinklerSpacing(HazardClassification hazard, string construction)
        {
            if (construction == "Combustible")
            {
                if (hazard == HazardClassification.Light) return 4.6; // meters
                if (hazard == HazardClassification.Ordinary_Group1) return 4.6;
                if (hazard == HazardClassification.Ordinary_Group2) return 4.6;
                return 3.7; // Extra hazard
            }
            
            // Non-combustible construction
            return 4.6; // Standard maximum
        }
        
        // Maximum coverage area per sprinkler - NFPA 13 Table 8.6.2.2.1(b)
        public static double GetMaximumCoverageArea(HazardClassification hazard, SprinklerType type)
        {
            if (type == SprinklerType.Standard_Response)
            {
                return hazard switch
                {
                    HazardClassification.Light => 20.9, // m²
                    HazardClassification.Ordinary_Group1 => 12.1,
                    HazardClassification.Ordinary_Group2 => 9.3,
                    HazardClassification.Extra_Group1 => 9.3,
                    HazardClassification.Extra_Group2 => 7.4,
                    _ => 12.1
                };
            }
            
            if (type == SprinklerType.Quick_Response)
            {
                if (hazard == HazardClassification.Light) return 20.9;
                return 12.1; // Ordinary/Extra
            }
            
            return 12.1; // Default
        }
        
        // K-factor for sprinkler head flow calculations - NFPA 13
        public static double GetKFactor(string headSize)
        {
            return headSize switch
            {
                "K2.8" => 40.0,  // (2.8 gpm/psi^0.5) = 40 L/min/bar^0.5
                "K4.2" => 60.7,  // (4.2 gpm/psi^0.5) = 60.7 L/min/bar^0.5
                "K5.6" => 80.6,  // Standard (5.6 gpm/psi^0.5) = 80.6 L/min/bar^0.5
                "K8.0" => 115.2, // (8.0 gpm/psi^0.5) = 115.2 L/min/bar^0.5
                "K11.2" => 161.2, // Large orifice (11.2 gpm/psi^0.5)
                _ => 80.6 // Standard K5.6
            };
        }
        
        // Calculate sprinkler flow rate - Q = K × √P
        public static double CalculateSprinklerFlow(double kFactor, double pressureBar)
        {
            return kFactor * Math.Sqrt(pressureBar); // L/min
        }
        
        // Minimum operating pressure - NFPA 13
        public static double GetMinimumOperatingPressure(SprinklerType type)
        {
            if (type == SprinklerType.ESFR) return 3.5; // bar (50 psi)
            if (type == SprinklerType.Residential) return 0.48; // bar (7 psi)
            return 0.48; // Standard minimum (7 psi)
        }
        
        // Hydraulic calculation result
        public static SprinklerDesignResult CalculateHydraulicRequirements(HazardClassification hazard, 
            double designAreaSqM, SprinklerType type)
        {
            double density = GetDesignDensity(hazard);
            double coveragePerHead = GetMaximumCoverageArea(hazard, type);
            int numHeads = (int)Math.Ceiling(designAreaSqM / coveragePerHead);
            double flowRate = density * designAreaSqM;
            double minPressure = GetMinimumOperatingPressure(type);
            
            return new SprinklerDesignResult
            {
                DesignAreaSqM = designAreaSqM,
                DensityLpmPerSqM = density,
                FlowRateLpm = flowRate,
                PressureKPa = minPressure * 100, // Convert bar to kPa
                NumberOfHeads = numHeads,
                Notes = $"{hazard} hazard classification"
            };
        }
        
        // NFPA 72 - FIRE ALARM SYSTEMS
        
        // Smoke detector spacing - NFPA 72 Table 17.6.3.5.1
        public static DetectorSpacingResult GetSmokeDetectorSpacing(double ceilingHeightM, string ceilingType)
        {
            var result = new DetectorSpacingResult();
            
            // Standard spacing for smooth ceilings
            if (ceilingHeightM <= 3.05) // Up to 10 feet
            {
                result.MaximumSpacingM = 9.1; // 30 feet
                result.MaximumDistanceFromWallM = 4.6; // Half spacing
            }
            else if (ceilingHeightM <= 10.7) // 10-35 feet
            {
                result.MaximumSpacingM = 9.1;
                result.MaximumDistanceFromWallM = 4.6;
                result.Limitations.Add("Stratification possible - consider high ceiling provisions");
            }
            else
            {
                result.MaximumSpacingM = 6.1; // Reduced for high ceilings
                result.MaximumDistanceFromWallM = 3.0;
                result.Limitations.Add("High ceiling - requires engineering analysis");
            }
            
            // Ceiling type adjustments
            if (ceilingType == "Beam_And_Joist")
            {
                result.Limitations.Add("Reduce spacing to 2/3 if beam depth > 10% of ceiling height");
            }
            
            result.MountingRequirements = "Mount on ceiling or within 300mm of ceiling";
            
            return result;
        }
        
        // Heat detector spacing - NFPA 72 Table 17.6.3.5.2
        public static DetectorSpacingResult GetHeatDetectorSpacing(double ceilingHeightM, string detectorRating)
        {
            var result = new DetectorSpacingResult();
            
            // Standard spacing based on detector sensitivity
            if (detectorRating == "Ordinary_57C") // 135°F
            {
                result.MaximumSpacingM = 9.1; // 30 feet
                result.MaximumDistanceFromWallM = 4.6;
            }
            else if (detectorRating == "Intermediate_68C") // 155°F
            {
                result.MaximumSpacingM = 7.6; // 25 feet
                result.MaximumDistanceFromWallM = 3.8;
            }
            else // High temperature
            {
                result.MaximumSpacingM = 6.1; // 20 feet
                result.MaximumDistanceFromWallM = 3.0;
            }
            
            result.MountingRequirements = "Mount on ceiling with minimum clearance from obstructions";
            
            if (ceilingHeightM > 9.1)
            {
                result.Limitations.Add("High ceiling - heat may not reach detector effectively");
            }
            
            return result;
        }
        
        // Notification appliance spacing - NFPA 72 Section 18.4
        public static double GetNotificationApplianceSpacing(string applianceType, bool visualRequired)
        {
            if (applianceType == "Audible_Horn")
            {
                return 30.0; // meters (approximate coverage radius)
            }
            
            if (applianceType == "Visual_Strobe")
            {
                if (visualRequired)
                    return 15.0; // meters (public mode spacing)
                return 30.0; // Private mode
            }
            
            if (applianceType == "Horn_Strobe_Combo")
            {
                return 15.0; // Limited by visual requirements
            }
            
            return 15.0; // Default
        }
        
        // Minimum sound level - NFPA 72 Section 18.4.4
        public static int GetMinimumSoundLevel(string occupancyType)
        {
            if (occupancyType == "Sleeping_Areas")
                return 75; // dBA (at pillow level)
            
            if (occupancyType == "Public_Mode")
                return 15; // dBA above ambient (or 65 dBA minimum)
            
            return 75; // dBA general
        }
        
        // Beam smoke detector spacing - NFPA 72 Section 17.7.3
        public static double GetBeamDetectorSpacing(double ceilingHeightM)
        {
            if (ceilingHeightM <= 9.1)
                return 18.3; // meters (60 feet)
            
            if (ceilingHeightM <= 10.7)
                return 15.2; // meters (50 feet) - reduced for high ceilings
            
            return 12.2; // meters (40 feet) - very high ceilings
        }
        
        // Circuit capacity - NFPA 72 Section 12.3
        public static int GetMaxDevicesPerCircuit(string circuitType)
        {
            if (circuitType == "Initiating_Device_Circuit")
                return 20; // Maximum devices per IDC (typical)
            
            if (circuitType == "Notification_Appliance_Circuit")
                return 10; // Maximum appliances per NAC (typical)
            
            if (circuitType == "Signaling_Line_Circuit")
                return 250; // Addressable devices per SLC
            
            return 20; // Default
        }
        
        // Battery backup duration - NFPA 72 Section 10.6.7
        public static double GetBatteryBackupDuration(bool emergencyVoiceAlarm)
        {
            if (emergencyVoiceAlarm)
                return 24.0; // hours standby + 0.25 hours alarm
            
            return 24.0; // hours standby + 5 minutes alarm (0.083 hours)
        }
    }
}
