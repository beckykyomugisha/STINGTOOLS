// FILE: BSStructuralStandards.cs - BS 5950 Steel + BS 8110 Concrete Design
// UK STRUCTURAL STANDARDS USED IN UGANDA
// LINES: ~400 (optimized)

using System;
using System.Collections.Generic;

namespace StingBIM.Standards.BSStructural
{
    public enum SectionClass { Class1_Plastic, Class2_Compact, Class3_SemiCompact, Class4_Slender }
    public enum ConcreteGrade { C20, C25, C30, C35, C40, C45, C50 }
    public enum SteelGrade { S275, S355, S460 }
    
    public class BeamDesignResult
    {
        public double MomentCapacityKNm { get; set; }
        public double ShearCapacityKN { get; set; }
        public double DeflectionMm { get; set; }
        public double UtilizationRatio { get; set; }
        public bool IsAdequate { get; set; }
        public string Notes { get; set; }
    }
    
    /// <summary>
    /// BS 5950 Structural Steel + BS 8110 Reinforced Concrete Design
    /// UK standards used extensively in Uganda
    /// </summary>
    public static class BSStructuralStandards
    {
        // BS 5950 - STRUCTURAL STEEL
        
        // Design strength (N/mm²) - BS 5950 Table 9
        public static double GetSteelDesignStrength(SteelGrade grade, double thicknessMm)
        {
            if (grade == SteelGrade.S275)
            {
                if (thicknessMm <= 16) return 275;
                if (thicknessMm <= 40) return 265;
                if (thicknessMm <= 63) return 255;
                return 245;
            }
            if (grade == SteelGrade.S355)
            {
                if (thicknessMm <= 16) return 355;
                if (thicknessMm <= 40) return 345;
                if (thicknessMm <= 63) return 335;
                return 325;
            }
            return 275;
        }
        
        // Section classification - BS 5950 Table 11
        public static SectionClass ClassifySection(double b_over_t, double d_over_tw, SteelGrade grade)
        {
            double epsilon = Math.Sqrt(275.0 / GetSteelDesignStrength(grade, 10));
            if (b_over_t <= 8.5 * epsilon && d_over_tw <= 79 * epsilon) return SectionClass.Class1_Plastic;
            if (b_over_t <= 9.5 * epsilon && d_over_tw <= 98 * epsilon) return SectionClass.Class2_Compact;
            if (b_over_t <= 13 * epsilon && d_over_tw <= 119 * epsilon) return SectionClass.Class3_SemiCompact;
            return SectionClass.Class4_Slender;
        }
        
        // Moment capacity - BS 5950 Clause 4.2
        public static double GetMomentCapacity(double plasticModulusZcm3, SteelGrade grade, SectionClass sectionClass)
        {
            double py = GetSteelDesignStrength(grade, 10);
            double Z = plasticModulusZcm3 * 1000; // Convert cm³ to mm³
            
            if (sectionClass == SectionClass.Class1_Plastic || sectionClass == SectionClass.Class2_Compact)
                return (py * Z) / 1000000; // kNm
            
            // Use elastic modulus for Class 3/4 (conservative approximation)
            return (py * Z * 0.9) / 1000000;
        }
        
        // Shear capacity - BS 5950 Clause 4.2.3
        public static double GetShearCapacity(double webDepthMm, double webThicknessMm, SteelGrade grade)
        {
            double py = GetSteelDesignStrength(grade, webThicknessMm);
            double Av = webDepthMm * webThicknessMm; // Shear area
            return (0.6 * py * Av) / 1000; // kN
        }
        
        // Deflection limit - BS 5950 Table 8
        public static double GetDeflectionLimit(double spanMm, string loadType)
        {
            if (loadType == "Dead_Plus_Imposed") return spanMm / 200.0;
            if (loadType == "Imposed_Only") return spanMm / 360.0;
            return spanMm / 250.0; // General
        }
        
        // Column axial capacity - BS 5950 Clause 4.7
        public static double GetColumnAxialCapacity(double areaSquareMm, double effectiveLengthMm, 
            double radiusOfGyrationMm, SteelGrade grade)
        {
            double py = GetSteelDesignStrength(grade, 10);
            double lambda = effectiveLengthMm / radiusOfGyrationMm;
            double lambdaLimitSteelRatio = Math.PI * Math.Sqrt(205000.0 / py);
            
            double pc; // Compressive strength
            if (lambda <= lambdaLimitSteelRatio)
                pc = py * (1 - Math.Pow(lambda / lambdaLimitSteelRatio, 2)) * 0.5; // Perry-Robertson
            else
                pc = py / (1 + Math.Pow(lambda / lambdaLimitSteelRatio, 2));
            
            return (pc * areaSquareMm) / 1000; // kN
        }
        
        // BS 8110 - REINFORCED CONCRETE
        
        // Concrete characteristic strength - BS 8110 Table 3.1
        public static double GetConcreteStrength(ConcreteGrade grade)
        {
            return grade switch
            {
                ConcreteGrade.C20 => 20, ConcreteGrade.C25 => 25, ConcreteGrade.C30 => 30,
                ConcreteGrade.C35 => 35, ConcreteGrade.C40 => 40, ConcreteGrade.C45 => 45,
                ConcreteGrade.C50 => 50, _ => 25
            };
        }
        
        // Steel reinforcement design strength - BS 8110
        public static double GetReinforcementStrength(string barType)
        {
            if (barType == "High_Yield") return 460; // High yield steel
            return 250; // Mild steel
        }
        
        // Concrete cover requirements - BS 8110 Table 3.4
        public static double GetMinimumCover(string exposure, string elementType)
        {
            if (exposure == "Severe") return elementType.Contains("Slab") ? 50 : 60;
            if (exposure == "Moderate") return 40;
            return elementType.Contains("Slab") ? 25 : 30; // Mild
        }
        
        // Singly reinforced beam moment capacity - BS 8110 Clause 3.4.4
        public static double GetBeamMomentCapacity(double widthMm, double effectiveDepthMm, 
            double steelAreaSqMm, ConcreteGrade concreteGrade)
        {
            double fcu = GetConcreteStrength(concreteGrade);
            double fy = 460; // High yield steel
            double d = effectiveDepthMm;
            double b = widthMm;
            double As = steelAreaSqMm;
            
            // Lever arm
            double K = (As * fy) / (fcu * b * d * d);
            if (K > 0.156) return 0; // Over-reinforced (not allowed)
            
            double z = d * (0.5 + Math.Sqrt(0.25 - K / 0.9));
            if (z > 0.95 * d) z = 0.95 * d;
            
            double M = (0.87 * fy * As * z) / 1000000; // kNm
            return M;
        }
        
        // Shear capacity - BS 8110 Clause 3.4.5
        public static double GetConcreteShearCapacity(double widthMm, double effectiveDepthMm, 
            double steelAreaSqMm, ConcreteGrade concreteGrade)
        {
            double fcu = GetConcreteStrength(concreteGrade);
            double d = effectiveDepthMm;
            double b = widthMm;
            double As = steelAreaSqMm;
            
            double rho = (100 * As) / (b * d); // Reinforcement ratio
            if (rho > 3.0) rho = 3.0;
            
            // Shear stress - BS 8110 Table 3.8
            double vc = 0.79 * Math.Pow(rho / 100.0, 1.0 / 3.0) * Math.Pow(fcu / 25.0, 1.0 / 3.0) / 1.25;
            
            return (vc * b * d) / 1000; // kN
        }
        
        // Column axial capacity - BS 8110 Clause 3.8
        public static double GetConcreteColumnCapacity(double widthMm, double depthMm, 
            double steelAreaSqMm, ConcreteGrade concreteGrade)
        {
            double fcu = GetConcreteStrength(concreteGrade);
            double fy = 460;
            double Ac = (widthMm * depthMm) - steelAreaSqMm; // Concrete area
            double Asc = steelAreaSqMm; // Steel area
            
            // Short column capacity
            double N = (0.45 * fcu * Ac + 0.87 * fy * Asc) / 1000; // kN
            return N;
        }
        
        // Minimum reinforcement - BS 8110 Clause 3.12
        public static double GetMinimumReinforcement(double concreteAreaSqMm, string elementType)
        {
            if (elementType.Contains("Beam")) return concreteAreaSqMm * 0.0013; // 0.13%
            if (elementType.Contains("Column")) return concreteAreaSqMm * 0.004; // 0.4%
            if (elementType.Contains("Slab")) return concreteAreaSqMm * 0.0013; // 0.13%
            return concreteAreaSqMm * 0.0015;
        }
        
        // Maximum reinforcement - BS 8110
        public static double GetMaximumReinforcement(double concreteAreaSqMm)
        {
            return concreteAreaSqMm * 0.04; // 4% maximum
        }
        
        // Crack width check - BS 8110 Clause 3.12.11
        public static bool IsCrackWidthAcceptable(double spacingMm, double coverMm, double barDiaMm)
        {
            double maxSpacing = 3 * barDiaMm;
            if (maxSpacing > 300) maxSpacing = 300; // 300mm max
            return spacingMm <= maxSpacing;
        }
        
        // Deflection limit - BS 8110 Table 3.10
        public static double GetConcreteDeflectionLimit(double spanMm, string supportCondition)
        {
            if (supportCondition == "Simply_Supported") return spanMm / 20.0;
            if (supportCondition == "Continuous") return spanMm / 26.0;
            if (supportCondition == "Cantilever") return spanMm / 7.0;
            return spanMm / 20.0;
        }
    }
}
