// FILE: ACI318Standards.cs - American Concrete Institute Building Code
// Alternative concrete standard (widely used globally)
// LINES: ~400 (optimized)

using System;

namespace StingBIM.Standards.ACI318
{
    public enum ReinforcementType { DeformedBars, WeldedWire, Prestressing }
    public enum LoadCombination { Service, Strength, Extreme }
    
    /// <summary>
    /// ACI 318-19 Building Code Requirements for Structural Concrete
    /// American standard widely adopted internationally
    /// </summary>
    public static class ACI318Standards
    {
        public const string Version = "ACI 318-19";
        
        // MATERIAL PROPERTIES
        
        // Concrete modulus of elasticity - ACI 318-19 Eq 19.2.2.1.a
        public static double GetConcreteModulus(double fcPrimePsi)
        {
            return 57000 * Math.Sqrt(fcPrimePsi); // psi
        }
        
        // Steel modulus of elasticity
        public static double GetSteelModulus()
        {
            return 29000000; // psi (29,000 ksi)
        }
        
        // STRENGTH REDUCTION FACTORS (φ) - ACI 318-19 Table 21.2.1
        
        public static double GetStrengthReductionFactor(string memberType, double netTensileStrain = 0.005)
        {
            if (memberType == "Tension_Controlled_Flexure") return 0.90;
            if (memberType == "Compression_Tied") return 0.65;
            if (memberType == "Compression_Spiral") return 0.75;
            if (memberType == "Shear_Torsion") return 0.75;
            if (memberType == "Bearing") return 0.65;
            if (memberType == "Post_Tensioning_Anchorage") return 0.85;
            
            // Transition zone (between 0.002 and 0.005 strain)
            if (netTensileStrain >= 0.005) return 0.90;
            if (netTensileStrain <= 0.002) return 0.65;
            return 0.65 + (netTensileStrain - 0.002) * (0.90 - 0.65) / 0.003;
        }
        
        // LOAD FACTORS AND COMBINATIONS - ACI 318-19 Table 5.3.1
        
        public static double GetLoadFactor(string loadType, string combination)
        {
            if (combination == "1.4D")
                return loadType == "Dead" ? 1.4 : 0;
            
            if (combination == "1.2D+1.6L+0.5(Lr_or_S)")
            {
                if (loadType == "Dead") return 1.2;
                if (loadType == "Live") return 1.6;
                if (loadType == "Roof_Live" || loadType == "Snow") return 0.5;
            }
            
            if (combination == "1.2D+1.6(Lr_or_S)+1.0L")
            {
                if (loadType == "Dead") return 1.2;
                if (loadType == "Live") return 1.0;
                if (loadType == "Roof_Live" || loadType == "Snow") return 1.6;
            }
            
            if (combination == "1.2D+1.0W+1.0L")
            {
                if (loadType == "Dead") return 1.2;
                if (loadType == "Live") return 1.0;
                if (loadType == "Wind") return 1.0;
            }
            
            return 1.0;
        }
        
        // FLEXURAL DESIGN - BEAMS
        
        // Beta1 factor - ACI 318-19 Table 22.2.2.4.3
        public static double GetBeta1(double fcPrimePsi)
        {
            if (fcPrimePsi <= 4000) return 0.85;
            if (fcPrimePsi >= 8000) return 0.65;
            return 0.85 - 0.05 * (fcPrimePsi - 4000) / 1000;
        }
        
        // Balanced reinforcement ratio
        public static double GetBalancedReinforcementRatio(double fcPrime, double fy)
        {
            double beta1 = GetBeta1(fcPrime);
            double rhoBalanced = 0.85 * beta1 * (fcPrime / fy) * (87000.0 / (87000 + fy));
            return rhoBalanced;
        }
        
        // Minimum flexural reinforcement - ACI 318-19 Eq 9.6.1.2
        public static double GetMinimumFlexuralReinforcement(double width, double depth, double fcPrime, double fy)
        {
            double asMin1 = (3 * Math.Sqrt(fcPrime) / fy) * width * depth;
            double asMin2 = (200.0 / fy) * width * depth;
            return Math.Max(asMin1, asMin2); // in²
        }
        
        // Maximum flexural reinforcement
        public static double GetMaximumFlexuralReinforcement(double width, double depth)
        {
            return 0.04 * width * depth; // 4% gross area
        }
        
        // Nominal moment capacity - simplified (rectangular section)
        public static double GetNominalMomentCapacity(double As, double fy, double width, double d, double fcPrime)
        {
            double a = (As * fy) / (0.85 * fcPrime * width);
            double Mn = As * fy * (d - a / 2.0);
            return Mn / 12000; // Convert lb-in to kip-ft
        }
        
        // SHEAR DESIGN
        
        // Concrete shear strength - ACI 318-19 Eq 22.5.5.1
        public static double GetConcreteShearStrength(double width, double d, double fcPrime)
        {
            return 2 * Math.Sqrt(fcPrime) * width * d / 1000; // kips
        }
        
        // Maximum shear reinforcement spacing - ACI 318-19 Section 9.7.6.2.2
        public static double GetMaximumShearReinforcementSpacing(double depth)
        {
            return Math.Min(depth / 2.0, 24.0); // inches
        }
        
        // DEVELOPMENT LENGTH - ACI 318-19 Section 25.4
        
        // Tension development length - simplified
        public static double GetTensionDevelopmentLength(double barDiameter, double fy, double fcPrime, double coverDepth = 0, double spacing = 0)
        {
            // Simplified basic development length
            double psi_t = 1.0; // Top bar factor (conservative)
            double psi_e = 1.0; // Epoxy coating factor
            double psi_s = 1.0; // Size factor
            double lambda = 1.0; // Lightweight concrete factor
            
            double ldh = (3.0 / 40.0) * psi_t * psi_e * psi_s * (fy / (lambda * Math.Sqrt(fcPrime))) * barDiameter;
            return Math.Max(ldh, 12.0); // Minimum 12 inches
        }
        
        // Compression development length - ACI 318-19 Section 25.4.9
        public static double GetCompressionDevelopmentLength(double barDiameter, double fy, double fcPrime)
        {
            double ldc = (0.02 * fy / Math.Sqrt(fcPrime)) * barDiameter;
            return Math.Max(ldc, 8.0); // Minimum 8 inches
        }
        
        // COLUMN DESIGN
        
        // Column capacity - simplified tied column
        public static double GetTiedColumnCapacity(double Ag, double Ast, double fcPrime, double fy)
        {
            double phi = 0.65; // Tied column
            double Po = 0.85 * fcPrime * (Ag - Ast) + fy * Ast;
            double Pn = 0.80 * Po; // ACI 318-19 Eq 22.4.2.1
            return phi * Pn / 1000; // kips
        }
        
        // Spiral column capacity
        public static double GetSpiralColumnCapacity(double Ag, double Ast, double fcPrime, double fy)
        {
            double phi = 0.75; // Spiral column
            double Po = 0.85 * fcPrime * (Ag - Ast) + fy * Ast;
            double Pn = 0.85 * Po; // ACI 318-19 Eq 22.4.2.2
            return phi * Pn / 1000; // kips
        }
        
        // Minimum column reinforcement - ACI 318-19 Section 10.6.1.1
        public static double GetMinimumColumnSteel(double grossArea)
        {
            return 0.01 * grossArea; // 1% minimum
        }
        
        // Maximum column reinforcement
        public static double GetMaximumColumnSteel(double grossArea)
        {
            return 0.08 * grossArea; // 8% maximum
        }
        
        // SLAB DESIGN
        
        // Minimum slab thickness - ACI 318-19 Table 7.3.1.1
        public static double GetMinimumSlabThickness(double span, string supportCondition)
        {
            return supportCondition switch
            {
                "Simply_Supported" => span / 20.0,
                "One_End_Continuous" => span / 24.0,
                "Both_Ends_Continuous" => span / 28.0,
                "Cantilever" => span / 10.0,
                _ => span / 20.0
            }; // inches
        }
        
        // Two-way slab punching shear - ACI 318-19 Section 8.4.4
        public static double GetPunchingShearStrength(double bo, double d, double fcPrime)
        {
            // bo = perimeter of critical section
            double vc = 4 * Math.Sqrt(fcPrime); // psi
            return vc * bo * d / 1000; // kips
        }
        
        // DEFLECTION CONTROL - ACI 318-19 Table 24.2.2
        
        public static double GetMaximumAllowableDeflection(double span, string loadCondition)
        {
            if (loadCondition == "Live_Load_Only") 
                return span / 360.0;
            if (loadCondition == "Total_Load") 
                return span / 240.0;
            if (loadCondition == "Supporting_Nonstructural") 
                return span / 480.0;
            
            return span / 180.0; // inches
        }
        
        // CRACK CONTROL - ACI 318-19 Section 24.3
        
        // Maximum bar spacing for crack control
        public static double GetMaximumBarSpacing(double coverDepth)
        {
            double s = 15 * (40000.0 / 40000) - 2.5 * coverDepth;
            return Math.Min(s, 12.0); // inches, maximum 12"
        }
        
        // CONCRETE COVER REQUIREMENTS - ACI 318-19 Table 20.6.1.3.1
        
        public static double GetMinimumCover(string exposure, int barSize)
        {
            if (exposure == "Cast_Against_Earth") return 3.0;
            if (exposure == "Weather_Exposed")
            {
                if (barSize <= 5) return 1.5;
                return 2.0;
            }
            if (exposure == "Not_Exposed") return 0.75;
            
            return 1.5; // Default
        }
    }
}
