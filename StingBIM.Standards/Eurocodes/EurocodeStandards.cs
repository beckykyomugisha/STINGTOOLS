// FILE: EurocodeStandards.cs
// LOCATION: StingBIM.Standards/Eurocodes/
// LINES: ~2500
// PURPOSE: Eurocode structural design standards (EC0, EC1, EC2, EC3, EC7)

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingBIM.Standards.Eurocodes
{
    #region Supporting Classes and Enums

    /// <summary>
    /// Concrete strength class per EC2
    /// </summary>
    public enum ConcreteClass
    {
        C12_15,
        C16_20,
        C20_25,
        C25_30,
        C30_37,
        C35_45,
        C40_50,
        C45_55,
        C50_60
    }

    /// <summary>
    /// Steel grade per EC3
    /// </summary>
    public enum SteelGrade
    {
        S235,
        S275,
        S355,
        S420,
        S460
    }

    /// <summary>
    /// Reinforcement steel class
    /// </summary>
    public enum ReinforcementClass
    {
        B500A,  // Ductility Class A
        B500B,  // Ductility Class B
        B500C   // Ductility Class C
    }

    /// <summary>
    /// Load combination result
    /// </summary>
    public class LoadCombinationResult
    {
        public double TotalLoad { get; set; }  // kN or kN/m
        public string Combination { get; set; }
        public bool IsULS { get; set; }  // Ultimate Limit State
        public bool IsSLS { get; set; }  // Serviceability Limit State
        public List<string> LoadComponents { get; set; } = new List<string>();
    }

    /// <summary>
    /// Concrete section design result
    /// </summary>
    public class ConcreteDesignResult
    {
        public double RequiredReinforcement { get; set; }  // mm²
        public int NumberOfBars { get; set; }
        public string BarSize { get; set; }
        public double ProvidedReinforcement { get; set; }  // mm²
        public double UtilizationRatio { get; set; }
        public bool IsAdequate { get; set; }
    }

    /// <summary>
    /// Steel section classification
    /// </summary>
    public enum SectionClass
    {
        Class_1_Plastic,
        Class_2_Compact,
        Class_3_Semi_Compact,
        Class_4_Slender
    }

    #endregion

    /// <summary>
    /// Eurocode Structural Design Standards
    /// Covers EC0 (Basis), EC1 (Actions), EC2 (Concrete), EC3 (Steel), EC7 (Geotechnical)
    /// </summary>
    public static class EurocodeStandards
    {
        public const string Version = "EN 1990-1999 Series";

        #region EC0 - Basis of Structural Design

        /// <summary>
        /// Partial safety factors for actions (loads)
        /// Reference: EC0 Table A1.2(B)
        /// </summary>
        public static class PartialFactors
        {
            // Ultimate Limit State (ULS) - Persistent and transient
            public const double PermanentUnfavourable_ULS = 1.35;  // γG
            public const double PermanentFavourable_ULS = 1.00;
            public const double VariableUnfavourable_ULS = 1.50;   // γQ
            public const double VariableFavourable_ULS = 0.00;

            // Serviceability Limit State (SLS)
            public const double Characteristic_SLS = 1.00;
            public const double Frequent_SLS = 0.50;      // ψ1
            public const double QuasiPermanent_SLS = 0.30; // ψ2

            // Accidental load factor
            public const double Accidental = 1.00;
        }

        /// <summary>
        /// Combination factors for variable actions
        /// Reference: EC0 Table A1.1
        /// </summary>
        public static class CombinationFactors
        {
            // ψ0 (combination value)
            public static double GetPsi0(string loadType)
            {
                switch (loadType)
                {
                    case "Imposed_Residential":
                    case "Imposed_Office":
                        return 0.7;
                    case "Imposed_Storage":
                        return 1.0;
                    case "Snow":
                        return 0.5;
                    case "Wind":
                        return 0.6;
                    default:
                        return 0.7;
                }
            }

            // ψ1 (frequent value)
            public static double GetPsi1(string loadType)
            {
                switch (loadType)
                {
                    case "Imposed_Residential":
                    case "Imposed_Office":
                        return 0.5;
                    case "Wind":
                        return 0.2;
                    case "Snow":
                        return 0.2;
                    default:
                        return 0.5;
                }
            }

            // ψ2 (quasi-permanent value)
            public static double GetPsi2(string loadType)
            {
                switch (loadType)
                {
                    case "Imposed_Residential":
                    case "Imposed_Office":
                        return 0.3;
                    case "Wind":
                    case "Snow":
                        return 0.0;
                    default:
                        return 0.3;
                }
            }
        }

        #endregion

        #region EC1 - Actions on Structures

        /// <summary>
        /// Imposed loads (live loads) per EC1-1-1
        /// Reference: EC1-1-1 Table 6.2
        /// </summary>
        public static double GetImposedLoad(string category)
        {
            // Returns characteristic imposed load in kN/m²
            switch (category)
            {
                case "A_Residential":
                    return 1.5;  // Residential, social, commercial
                case "B_Office":
                    return 2.5;  // Office areas
                case "C_Assembly_Fixed":
                    return 3.0;  // Assembly areas with fixed seating
                case "C_Assembly_Movable":
                    return 4.0;  // Assembly areas with movable seating
                case "D_Shopping":
                    return 4.0;  // Shopping areas
                case "E_Storage":
                    return 7.5;  // Storage and industrial
                case "F_Vehicle_Light":
                    return 2.5;  // Light vehicles ≤30kN
                case "Stairs":
                    return 3.0;  // Stairs in residential/office
                case "Balconies":
                    return 4.0;  // Balconies
                default:
                    return 2.5;  // Default
            }
        }

        /// <summary>
        /// Wind load calculation (simplified)
        /// Reference: EC1-1-4
        /// </summary>
        public static double CalculateWindPressure(double basicWindSpeed, double height, string terrain)
        {
            // qb = 0.5 × ρ × vb²  (basic velocity pressure)
            // where ρ = 1.25 kg/m³ (air density)
            
            double rho = 1.25;
            double qb = 0.5 * rho * Math.Pow(basicWindSpeed, 2);  // N/m²

            // Height coefficient (simplified)
            double kr = GetTerrainFactor(terrain);
            double heightFactor = kr * Math.Pow(height / 10.0, 0.16);

            return qb * heightFactor; // Wind pressure kN/m²
        }

        private static double GetTerrainFactor(string terrain)
        {
            switch (terrain)
            {
                case "Open":
                    return 0.19;  // Open country
                case "Suburban":
                    return 0.22;  // Suburban/urban
                case "City":
                    return 0.24;  // City centers
                default:
                    return 0.22;
            }
        }

        /// <summary>
        /// Snow load calculation
        /// Reference: EC1-1-3
        /// </summary>
        public static double CalculateSnowLoad(double altitude, string zone = "Central_Europe")
        {
            // Simplified snow load: sk = 0.4 + (altitude / 500)  [kN/m²]
            // Uganda typically has zero snow load - included for completeness
            
            if (altitude < 1500) // meters
                return 0.0; // No snow in tropical regions
            
            double sk = 0.4 + (altitude / 500.0);
            return Math.Min(sk, 5.0); // Max 5 kN/m² typical
        }

        #endregion

        #region EC2 - Concrete Structures

        /// <summary>
        /// Concrete material properties per EC2
        /// Reference: EC2 Table 3.1
        /// </summary>
        private static readonly Dictionary<ConcreteClass, (double fck, double fctm, double Ecm)> 
            _concreteProperties = new Dictionary<ConcreteClass, (double, double, double)>
        {
            // Class -> (fck MPa, fctm MPa, Ecm GPa)
            { ConcreteClass.C12_15, (12, 1.6, 27) },
            { ConcreteClass.C16_20, (16, 1.9, 29) },
            { ConcreteClass.C20_25, (20, 2.2, 30) },
            { ConcreteClass.C25_30, (25, 2.6, 31) },
            { ConcreteClass.C30_37, (30, 2.9, 33) },
            { ConcreteClass.C35_45, (35, 3.2, 34) },
            { ConcreteClass.C40_50, (40, 3.5, 35) },
            { ConcreteClass.C45_55, (45, 3.8, 36) },
            { ConcreteClass.C50_60, (50, 4.1, 37) }
        };

        /// <summary>
        /// Get concrete compressive strength
        /// </summary>
        public static double GetConcreteStrength(ConcreteClass concreteClass)
        {
            return _concreteProperties[concreteClass].fck; // MPa
        }

        /// <summary>
        /// Design value of concrete strength
        /// Reference: EC2 Eq 3.15
        /// </summary>
        public static double GetDesignConcreteStrength(ConcreteClass concreteClass)
        {
            double fck = _concreteProperties[concreteClass].fck;
            double alpha_cc = 1.0; // National Annex value (typically 0.85-1.0)
            double gamma_c = 1.5;  // Partial factor for concrete
            
            return (alpha_cc * fck) / gamma_c;  // fcd
        }

        /// <summary>
        /// Get reinforcement steel strength
        /// Reference: EC2 Table C.1
        /// </summary>
        public static double GetReinforcementStrength(ReinforcementClass steelClass)
        {
            // Characteristic yield strength fyk
            return 500; // MPa for all B500 classes
        }

        /// <summary>
        /// Design reinforcement strength
        /// </summary>
        public static double GetDesignReinforcementStrength(ReinforcementClass steelClass)
        {
            double fyk = GetReinforcementStrength(steelClass);
            double gamma_s = 1.15; // Partial factor for steel
            
            return fyk / gamma_s; // fyd
        }

        /// <summary>
        /// Design rectangular concrete beam for bending
        /// Reference: EC2 Section 6.1
        /// </summary>
        public static ConcreteDesignResult DesignConcreteBeam(
            double MEd,              // Design moment (kNm)
            double width,            // Beam width (mm)
            double effectiveDepth,   // Effective depth d (mm)
            ConcreteClass concrete,
            ReinforcementClass steel)
        {
            var result = new ConcreteDesignResult();

            // Material properties
            double fcd = GetDesignConcreteStrength(concrete);
            double fyd = GetDesignReinforcementStrength(steel);

            // Convert moment to Nmm
            double MEdNmm = MEd * 1e6;

            // K = M / (fcd × b × d²)
            double K = MEdNmm / (fcd * width * Math.Pow(effectiveDepth, 2));

            // Check if compression reinforcement needed
            double Kbal = 0.167; // For fck ≤ 50 MPa
            
            if (K > Kbal)
            {
                result.IsAdequate = false;
                result.RequiredReinforcement = 0;
                return result; // Compression reinforcement needed
            }

            // Lever arm: z = d × (0.5 + √(0.25 - K/1.134))
            double z = effectiveDepth * (0.5 + Math.Sqrt(0.25 - K / 1.134));
            
            // Limit z to 0.95d
            z = Math.Min(z, 0.95 * effectiveDepth);

            // Required steel area: As = M / (fyd × z)
            result.RequiredReinforcement = MEdNmm / (fyd * z);  // mm²

            // Select bar size and number
            (result.NumberOfBars, result.BarSize, result.ProvidedReinforcement) = 
                SelectReinforcementBars(result.RequiredReinforcement, width);

            // Utilization ratio
            result.UtilizationRatio = result.RequiredReinforcement / result.ProvidedReinforcement;
            result.IsAdequate = result.UtilizationRatio <= 1.0;

            return result;
        }

        /// <summary>
        /// Select reinforcement bar configuration
        /// </summary>
        private static (int count, string size, double area) SelectReinforcementBars(double requiredArea, double width)
        {
            // Bar sizes (diameter mm -> area mm²)
            var barSizes = new Dictionary<string, double>
            {
                { "H8", 50 },
                { "H10", 79 },
                { "H12", 113 },
                { "H16", 201 },
                { "H20", 314 },
                { "H25", 491 },
                { "H32", 804 },
                { "H40", 1257 }
            };

            // Try different bar sizes
            foreach (var bar in barSizes.OrderBy(b => b.Value))
            {
                int numberOfBars = (int)Math.Ceiling(requiredArea / bar.Value);
                
                // Check if bars fit in width (assuming 25mm cover, 10mm stirrups)
                double minSpacing = 20; // mm minimum clear spacing
                double requiredWidth = (numberOfBars - 1) * minSpacing + numberOfBars * GetBarDiameter(bar.Key) + 2 * 25 + 2 * 10;
                
                if (requiredWidth <= width)
                {
                    return (numberOfBars, bar.Key, numberOfBars * bar.Value);
                }
            }

            // Default if nothing fits
            return (2, "H20", 628);
        }

        private static double GetBarDiameter(string barSize)
        {
            return double.Parse(barSize.Substring(1)); // Extract number from H12, H16, etc.
        }

        /// <summary>
        /// Minimum and maximum reinforcement ratios
        /// Reference: EC2 Section 9.2.1.1
        /// </summary>
        public static (double min, double max) GetReinforcementLimits(ConcreteClass concrete)
        {
            double fck = GetConcreteStrength(concrete);
            double fctm = _concreteProperties[concrete].fctm;
            
            // Minimum: As,min = 0.26 × (fctm/fyk) × bt × d  (but not less than 0.0013 bt d)
            double minRatio = Math.Max(0.26 * (fctm / 500), 0.0013);
            
            // Maximum: As,max = 0.04 Ac
            double maxRatio = 0.04;
            
            return (minRatio, maxRatio);
        }

        #endregion

        #region EC3 - Steel Structures

        /// <summary>
        /// Steel material properties
        /// Reference: EC3 Table 3.1
        /// </summary>
        public static double GetSteelYieldStrength(SteelGrade grade)
        {
            // Yield strength fy for t ≤ 40mm
            switch (grade)
            {
                case SteelGrade.S235:
                    return 235; // MPa
                case SteelGrade.S275:
                    return 275;
                case SteelGrade.S355:
                    return 355;
                case SteelGrade.S420:
                    return 420;
                case SteelGrade.S460:
                    return 460;
                default:
                    return 235;
            }
        }

        /// <summary>
        /// Design strength for steel
        /// </summary>
        public static double GetDesignSteelStrength(SteelGrade grade)
        {
            double fy = GetSteelYieldStrength(grade);
            double gamma_M0 = 1.0; // Partial factor for resistance
            
            return fy / gamma_M0;
        }

        /// <summary>
        /// Classify steel section
        /// Reference: EC3 Table 5.2
        /// </summary>
        public static SectionClass ClassifySection(double cf_tf, double cw_tw, double epsilon, SteelGrade grade)
        {
            // Simplified classification for I-sections
            // cf/tf = outstand flange ratio
            // cw/tw = web depth/thickness ratio
            // ε = √(235/fy)
            
            epsilon = Math.Sqrt(235.0 / GetSteelYieldStrength(grade));

            // Class 1 limits (plastic)
            if (cf_tf <= 9 * epsilon && cw_tw <= 72 * epsilon)
                return SectionClass.Class_1_Plastic;
            
            // Class 2 limits (compact)
            if (cf_tf <= 10 * epsilon && cw_tw <= 83 * epsilon)
                return SectionClass.Class_2_Compact;
            
            // Class 3 limits (semi-compact)
            if (cf_tf <= 14 * epsilon && cw_tw <= 124 * epsilon)
                return SectionClass.Class_3_Semi_Compact;
            
            // Class 4 (slender)
            return SectionClass.Class_4_Slender;
        }

        /// <summary>
        /// Design steel beam for bending
        /// Reference: EC3 Section 6.2.5
        /// </summary>
        public static double DesignSteelBeamBending(
            double MEd,           // Design moment (kNm)
            double Wpl,           // Plastic section modulus (cm³)
            SteelGrade grade,
            SectionClass sectionClass)
        {
            double fy = GetSteelYieldStrength(grade);
            double gamma_M0 = 1.0;

            // Plastic moment resistance (for Class 1 or 2)
            if (sectionClass == SectionClass.Class_1_Plastic || sectionClass == SectionClass.Class_2_Compact)
            {
                // Mc,Rd = Wpl × fy / γM0
                double McRd = (Wpl * 1000 * fy) / (gamma_M0 * 1e6); // kNm
                return McRd;
            }
            // Elastic moment resistance (for Class 3)
            else if (sectionClass == SectionClass.Class_3_Semi_Compact)
            {
                // Use elastic modulus Wel (approximately 0.9 × Wpl for I-sections)
                double Wel = Wpl * 0.9;
                double McRd = (Wel * 1000 * fy) / (gamma_M0 * 1e6);
                return McRd;
            }
            else
            {
                // Class 4 - requires effective section properties
                return 0; // Simplified - requires detailed analysis
            }
        }

        /// <summary>
        /// Design axial compression member (column)
        /// Reference: EC3 Section 6.3.1
        /// </summary>
        public static double DesignSteelColumn(
            double NEd,            // Design axial force (kN)
            double A,              // Cross-sectional area (mm²)
            double L,              // Column length (mm)
            double i,              // Radius of gyration (mm)
            SteelGrade grade)
        {
            double fy = GetSteelYieldStrength(grade);
            double E = 210000; // Young's modulus (MPa)
            double gamma_M1 = 1.0;

            // Slenderness: λ = L / i
            double lambda = L / i;

            // Non-dimensional slenderness: λ̄ = λ / λ1  where λ1 = π√(E/fy)
            double lambda1 = Math.PI * Math.Sqrt(E / fy);
            double lambdaBar = lambda / lambda1;

            // Buckling curve selection (assume curve 'b' for rolled I-sections)
            double alpha = 0.34; // Imperfection factor for curve 'b'
            
            // Reduction factor χ
            double phi = 0.5 * (1 + alpha * (lambdaBar - 0.2) + Math.Pow(lambdaBar, 2));
            double chi = Math.Min(1.0, 1.0 / (phi + Math.Sqrt(Math.Pow(phi, 2) - Math.Pow(lambdaBar, 2))));

            // Design buckling resistance
            double NbRd = (chi * A * fy) / (gamma_M1 * 1000); // kN

            return NbRd;
        }

        #endregion

        #region EC7 - Geotechnical Design

        /// <summary>
        /// Bearing capacity calculation
        /// Reference: EC7 Annex D (simplified)
        /// </summary>
        public static double CalculateBearingCapacity(
            double cohesion,       // c' (kPa)
            double frictionAngle,  // φ' (degrees)
            double foundationDepth,// Df (m)
            double foundationWidth,// B (m)
            double soilDensity)    // γ (kN/m³)
        {
            // Terzaghi bearing capacity factors (simplified)
            double phi = frictionAngle * Math.PI / 180.0; // Convert to radians
            
            double Nq = Math.Exp(Math.PI * Math.Tan(phi)) * Math.Pow(Math.Tan(Math.PI / 4 + phi / 2), 2);
            double Nc = (Nq - 1) / Math.Tan(phi);
            double Ngamma = 2 * (Nq - 1) * Math.Tan(phi);

            // Ultimate bearing capacity
            double qult = cohesion * Nc + soilDensity * foundationDepth * Nq + 0.5 * soilDensity * foundationWidth * Ngamma;

            // Apply factor of safety (EC7 Design Approach 1)
            double gamma_R = 1.4; // Partial factor for bearing resistance
            
            return qult / gamma_R; // Design bearing capacity (kPa)
        }

        /// <summary>
        /// Settlement calculation (simplified)
        /// Reference: EC7
        /// </summary>
        public static double CalculateSettlement(
            double load,           // Applied load (kN)
            double foundationWidth,// B (m)
            double modulusOfSubgrade) // E (kPa)
        {
            // Simplified elastic settlement: s = (q × B × (1 - ν²)) / E
            // where q = load/area, ν = Poisson's ratio ≈ 0.3
            
            double area = foundationWidth * foundationWidth; // Assume square footing
            double q = load / area; // kPa
            double nu = 0.3;
            
            double settlement = (q * foundationWidth * (1 - Math.Pow(nu, 2))) / modulusOfSubgrade;
            
            return settlement * 1000; // mm
        }

        #endregion

        #region Load Combinations

        /// <summary>
        /// Calculate ULS load combination (STR/GEO)
        /// Reference: EC0 Equation 6.10
        /// </summary>
        public static LoadCombinationResult CombineLoadsULS(
            double permanentLoad,    // Gk
            double imposedLoad,      // Qk,1 (leading variable)
            double windLoad,         // Qk,2 (accompanying variable)
            string imposedType = "Imposed_Office",
            string windType = "Wind")
        {
            var result = new LoadCombinationResult
            {
                IsULS = true,
                IsSLS = false
            };

            // Equation 6.10: ΣγG,j Gk,j "+" γQ,1 Qk,1 "+" Σγ Q,i ψ0,i Qk,i
            
            double totalLoad = 
                PartialFactors.PermanentUnfavourable_ULS * permanentLoad +
                PartialFactors.VariableUnfavourable_ULS * imposedLoad +
                PartialFactors.VariableUnfavourable_ULS * CombinationFactors.GetPsi0(windType) * windLoad;

            result.TotalLoad = totalLoad;
            result.Combination = "ULS (STR/GEO) Eq. 6.10";
            result.LoadComponents.Add($"Dead: {PartialFactors.PermanentUnfavourable_ULS} × {permanentLoad} = {PartialFactors.PermanentUnfavourable_ULS * permanentLoad:F2}");
            result.LoadComponents.Add($"Live: {PartialFactors.VariableUnfavourable_ULS} × {imposedLoad} = {PartialFactors.VariableUnfavourable_ULS * imposedLoad:F2}");
            result.LoadComponents.Add($"Wind: {PartialFactors.VariableUnfavourable_ULS} × {CombinationFactors.GetPsi0(windType)} × {windLoad} = {PartialFactors.VariableUnfavourable_ULS * CombinationFactors.GetPsi0(windType) * windLoad:F2}");

            return result;
        }

        /// <summary>
        /// Calculate SLS load combination (Characteristic)
        /// Reference: EC0 Equation 6.14b
        /// </summary>
        public static LoadCombinationResult CombineLoadsSLS_Characteristic(
            double permanentLoad,
            double imposedLoad,
            double windLoad,
            string imposedType = "Imposed_Office",
            string windType = "Wind")
        {
            var result = new LoadCombinationResult
            {
                IsULS = false,
                IsSLS = true
            };

            // Equation 6.14b: ΣGk,j "+" Qk,1 "+" Σψ0,i Qk,i
            
            double totalLoad = 
                permanentLoad +
                imposedLoad +
                CombinationFactors.GetPsi0(windType) * windLoad;

            result.TotalLoad = totalLoad;
            result.Combination = "SLS (Characteristic) Eq. 6.14b";
            result.LoadComponents.Add($"Dead: {permanentLoad:F2}");
            result.LoadComponents.Add($"Live: {imposedLoad:F2}");
            result.LoadComponents.Add($"Wind: {CombinationFactors.GetPsi0(windType)} × {windLoad} = {CombinationFactors.GetPsi0(windType) * windLoad:F2}");

            return result;
        }

        /// <summary>
        /// Calculate SLS load combination (Quasi-permanent)
        /// Reference: EC0 Equation 6.16b
        /// </summary>
        public static LoadCombinationResult CombineLoadsSLS_QuasiPermanent(
            double permanentLoad,
            double imposedLoad,
            string imposedType = "Imposed_Office")
        {
            var result = new LoadCombinationResult
            {
                IsULS = false,
                IsSLS = true
            };

            // Equation 6.16b: ΣGk,j "+" Σψ2,i Qk,i
            
            double totalLoad = 
                permanentLoad +
                CombinationFactors.GetPsi2(imposedType) * imposedLoad;

            result.TotalLoad = totalLoad;
            result.Combination = "SLS (Quasi-permanent) Eq. 6.16b";
            result.LoadComponents.Add($"Dead: {permanentLoad:F2}");
            result.LoadComponents.Add($"Live: {CombinationFactors.GetPsi2(imposedType)} × {imposedLoad} = {CombinationFactors.GetPsi2(imposedType) * imposedLoad:F2}");

            return result;
        }

        #endregion

        #region Deflection Limits

        /// <summary>
        /// Deflection limits for beams and slabs
        /// Reference: EC2 Section 7.4, EC3 Section 7.2
        /// </summary>
        public static class DeflectionLimits
        {
            /// <summary>
            /// Get maximum permitted deflection
            /// </summary>
            public static double GetMaximumDeflection(double span, string elementType, string condition)
            {
                // span in mm, returns max deflection in mm
                
                if (elementType == "Beam" || elementType == "Slab")
                {
                    if (condition == "Total")
                        return span / 250.0;  // Total deflection
                    else if (condition == "Variable")
                        return span / 500.0;  // Variable load deflection
                    else
                        return span / 350.0;  // Default
                }
                else if (elementType == "Cantilever")
                {
                    return span / 180.0;
                }
                
                return span / 250.0; // Conservative default
            }
        }

        #endregion
    }
}
