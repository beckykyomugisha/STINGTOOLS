// FILE: SMACNAStandards.cs - HVAC Ductwork Construction Standards
// Sheet Metal and Air Conditioning Contractors' National Association
// LINES: ~300 (optimized)

using System;
using System.Collections.Generic;

namespace StingBIM.Standards.SMACNA
{
    public enum DuctPressureClass { 
        LowPressure_1inWG,      // Up to 1" WG
        MediumPressure_4inWG,   // 1" to 4" WG
        HighPressure_10inWG     // 4" to 10" WG
    }
    
    public enum DuctShape { Rectangular, Round, Oval, Flat_Oval }
    
    /// <summary>
    /// SMACNA HVAC Duct Construction Standards
    /// Industry standard for ductwork design and installation
    /// </summary>
    public static class SMACNAStandards
    {
        // DUCT CONSTRUCTION - PRESSURE CLASSIFICATION
        
        // Maximum static pressure for each class
        public static double GetMaximumPressure(DuctPressureClass pressureClass)
        {
            return pressureClass switch
            {
                DuctPressureClass.LowPressure_1inWG => 1.0,      // inches water gauge
                DuctPressureClass.MediumPressure_4inWG => 4.0,
                DuctPressureClass.HighPressure_10inWG => 10.0,
                _ => 1.0
            };
        }
        
        // Minimum metal thickness (gauge) - SMACNA Table 2-1
        public static int GetMinimumGaugeThickness(double ductSize, DuctPressureClass pressureClass, DuctShape shape = DuctShape.Rectangular)
        {
            // ductSize = longest side for rectangular, diameter for round (inches)
            
            if (pressureClass == DuctPressureClass.LowPressure_1inWG)
            {
                if (ductSize <= 12) return 26;  // 26 gauge
                if (ductSize <= 30) return 24;
                if (ductSize <= 54) return 22;
                if (ductSize <= 84) return 20;
                if (ductSize <= 96) return 18;
                return 16;
            }
            
            if (pressureClass == DuctPressureClass.MediumPressure_4inWG)
            {
                if (ductSize <= 12) return 24;
                if (ductSize <= 30) return 22;
                if (ductSize <= 54) return 20;
                if (ductSize <= 84) return 18;
                return 16;
            }
            
            // High pressure
            if (ductSize <= 30) return 20;
            if (ductSize <= 60) return 18;
            return 16;
        }
        
        // DUCT SUPPORT AND HANGERS - SMACNA Chapter 5
        
        // Maximum support spacing (feet)
        public static double GetMaximumSupportSpacing(double ductSize, DuctShape shape)
        {
            if (shape == DuctShape.Round)
            {
                if (ductSize <= 12) return 8.0;
                if (ductSize <= 24) return 10.0;
                return 12.0;
            }
            
            // Rectangular duct
            if (ductSize <= 24) return 8.0;
            if (ductSize <= 60) return 10.0;
            if (ductSize <= 96) return 12.0;
            return 12.0;
        }
        
        // Hanger rod size (inches diameter)
        public static double GetHangerRodSize(double ductWeight)
        {
            if (ductWeight <= 50) return 0.25;    // 1/4" rod
            if (ductWeight <= 100) return 0.375;  // 3/8" rod
            if (ductWeight <= 200) return 0.5;    // 1/2" rod
            if (ductWeight <= 400) return 0.625;  // 5/8" rod
            return 0.75;                          // 3/4" rod
        }
        
        // DUCT SEALING AND LEAKAGE - SMACNA Chapter 6
        
        // Seal class requirements
        public static string GetSealingClass(DuctPressureClass pressureClass)
        {
            return pressureClass switch
            {
                DuctPressureClass.LowPressure_1inWG => "Seal Class C",
                DuctPressureClass.MediumPressure_4inWG => "Seal Class B",
                DuctPressureClass.HighPressure_10inWG => "Seal Class A",
                _ => "Seal Class C"
            };
        }
        
        // Maximum leakage rate (CFM per 100 sq ft of duct surface)
        public static double GetMaximumLeakageRate(DuctPressureClass pressureClass)
        {
            return pressureClass switch
            {
                DuctPressureClass.LowPressure_1inWG => 48.0,   // CFM/100 sq ft
                DuctPressureClass.MediumPressure_4inWG => 24.0,
                DuctPressureClass.HighPressure_10inWG => 12.0,
                _ => 48.0
            };
        }
        
        // INSULATION REQUIREMENTS - SMACNA Chapter 9
        
        // Minimum insulation thickness (inches)
        public static double GetMinimumInsulationThickness(string application, bool externalInsulation = true)
        {
            if (externalInsulation) // External wrap
            {
                return application switch
                {
                    "Supply_Air_Conditioned" => 1.0,
                    "Supply_Air_Heated" => 1.0,
                    "Return_Air" => 0.5,
                    "Exhaust_Air" => 0,
                    "Outdoor_Air" => 2.0,
                    _ => 1.0
                };
            }
            else // Duct liner
            {
                return application switch
                {
                    "Supply_Main" => 1.0,
                    "Supply_Branch" => 0.5,
                    "Return" => 0.5,
                    _ => 1.0
                };
            }
        }
        
        // Insulation R-value
        public static double GetInsulationRValue(double thickness)
        {
            // Typical fiberglass duct wrap: R-4.2 per inch
            return thickness * 4.2;
        }
        
        // RECTANGULAR TO ROUND EQUIVALENT - SMACNA Table 4-2
        
        // Equivalent round diameter
        public static double GetEquivalentRoundDiameter(double width, double height)
        {
            // De = 1.30 × (W × H)^0.625 / (W + H)^0.25
            return 1.30 * Math.Pow(width * height, 0.625) / Math.Pow(width + height, 0.25);
        }
        
        // FITTINGS AND PRESSURE LOSS
        
        // Elbow pressure loss coefficient
        public static double GetElbowLossCoefficient(double centerlineRadius, double ductWidth)
        {
            double radiusRatio = centerlineRadius / ductWidth;
            
            if (radiusRatio < 0.5) return 1.3;      // Sharp elbow
            if (radiusRatio < 0.75) return 0.9;     // Medium radius
            if (radiusRatio < 1.0) return 0.7;      // Long radius
            if (radiusRatio < 1.5) return 0.5;      // Extra long radius
            return 0.4;                             // Very long radius
        }
        
        // Minimum elbow radius (good practice)
        public static double GetMinimumElbowRadius(double ductWidth)
        {
            return ductWidth * 1.5; // 1.5 × width for good performance
        }
        
        // Transition loss coefficient
        public static double GetTransitionLossCoefficient(double angle)
        {
            // Angle in degrees
            if (angle <= 15) return 0.05;
            if (angle <= 30) return 0.15;
            if (angle <= 45) return 0.25;
            if (angle <= 60) return 0.35;
            return 0.50; // >60 degrees
        }
        
        // FLEX DUCT - SMACNA Standards
        
        // Maximum flex duct length
        public static double GetFlexDuctMaximumLength()
        {
            return 5.0; // feet - recommended maximum
        }
        
        // Flex duct pressure loss multiplier
        public static double GetFlexDuctLossMultiplier(bool fullyExtended)
        {
            return fullyExtended ? 1.0 : 2.5; // 2.5× if not fully extended
        }
        
        // Flex duct velocity limit
        public static double GetFlexDuctMaximumVelocity()
        {
            return 900; // fpm (feet per minute)
        }
        
        // FIRE AND SMOKE DAMPERS
        
        // Fire damper requirement check
        public static bool IsFireDamperRequired(string penetrationType)
        {
            var requiredLocations = new List<string>
            {
                "Fire_Rated_Wall",
                "Fire_Rated_Floor",
                "Fire_Rated_Ceiling",
                "Shaft_Penetration",
                "Corridor_Wall",
                "Exit_Stair",
                "Fire_Barrier"
            };
            
            return requiredLocations.Contains(penetrationType);
        }
        
        // Fire damper rating
        public static string GetFireDamperRating(string barrierRating)
        {
            return barrierRating switch
            {
                "1_Hour" => "1.5 Hour Fire Damper",
                "2_Hour" => "3 Hour Fire Damper",
                "3_Hour" => "3 Hour Fire Damper",
                _ => "1.5 Hour Fire Damper"
            };
        }
        
        // Smoke damper requirement
        public static bool IsSmokeDamperRequired(string systemType)
        {
            var requiredSystems = new List<string>
            {
                "Air_Handling_Unit_>15000CFM",
                "Return_Air_Plenum",
                "Transfer_Air_Corridor",
                "Smoke_Control_System"
            };
            
            return requiredSystems.Contains(systemType);
        }
        
        // ACCESS DOORS AND PANELS
        
        // Access door requirement
        public static bool IsAccessDoorRequired(double ductSize)
        {
            return ductSize >= 30; // Minimum 30" duct requires access door
        }
        
        // Access door size (inches)
        public static (double width, double height) GetAccessDoorSize(double ductSize)
        {
            if (ductSize >= 60) return (24, 24);  // 24" × 24"
            if (ductSize >= 48) return (18, 18);  // 18" × 18"
            return (12, 12);                      // 12" × 12"
        }
        
        // DUCT VELOCITY LIMITS
        
        // Maximum duct velocity (fpm)
        public static double GetMaximumDuctVelocity(string ductType, string application)
        {
            if (ductType == "Supply_Main")
            {
                if (application.Contains("Residential")) return 900;
                if (application.Contains("Office")) return 1800;
                return 2000; // Industrial
            }
            
            if (ductType == "Supply_Branch")
            {
                if (application.Contains("Residential")) return 700;
                return 1200;
            }
            
            if (ductType == "Return")
            {
                if (application.Contains("Residential")) return 700;
                return 1500;
            }
            
            return 2500; // High velocity systems
        }
    }
}
