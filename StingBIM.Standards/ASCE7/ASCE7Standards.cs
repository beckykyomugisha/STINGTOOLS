// FILE: ASCE7Standards.cs - Wind and Seismic Load Calculations
// Critical for lateral load design
// LINES: ~350 (optimized)

using System;

namespace StingBIM.Standards.ASCE7
{
    public enum ExposureCategory { B_Urban, C_Open, D_Coastal }
    public enum RiskCategory { I_Low, II_Normal, III_Substantial, IV_Essential }
    public enum SeismicDesignCategory { A, B, C, D, E, F }
    
    /// <summary>
    /// ASCE 7-22 Minimum Design Loads and Associated Criteria for Buildings
    /// Wind, seismic, snow, rain loads
    /// </summary>
    public static class ASCE7Standards
    {
        public const string Version = "ASCE 7-22";
        
        // WIND LOADS - CHAPTERS 26-31
        
        // Basic wind speed (mph) - would use wind speed maps
        public static double GetBasicWindSpeed(string location, RiskCategory risk)
        {
            // Simplified - actual values from wind speed maps
            var baseWindSpeeds = new System.Collections.Generic.Dictionary<string, double>
            {
                { "East_Coast_USA", 115 },
                { "Gulf_Coast_USA", 150 },
                { "Interior_USA", 90 },
                { "Mountain_West_USA", 105 },
                { "Caribbean", 170 }
            };
            
            double baseSpeed = baseWindSpeeds.TryGetValue(location, out double speed) ? speed : 90;
            
            // Adjust for risk category
            double factor = risk switch
            {
                RiskCategory.I_Low => 0.87,
                RiskCategory.II_Normal => 1.00,
                RiskCategory.III_Substantial => 1.15,
                RiskCategory.IV_Essential => 1.15,
                _ => 1.00
            };
            
            return baseSpeed * factor;
        }
        
        // Velocity pressure - ASCE 7 Eq 26.10-1
        public static double GetVelocityPressure(double windSpeed, double height, ExposureCategory exposure)
        {
            // qz = 0.00256 × Kz × Kzt × Kd × V²
            double Kz = GetVelocityPressureCoefficient(height, exposure);
            double Kzt = 1.0; // Topographic factor (flat terrain assumed)
            double Kd = 0.85; // Wind directionality factor (buildings)
            
            return 0.00256 * Kz * Kzt * Kd * Math.Pow(windSpeed, 2); // psf
        }
        
        // Velocity pressure coefficient - ASCE 7 Table 26.10-1
        private static double GetVelocityPressureCoefficient(double height, ExposureCategory exposure)
        {
            double alpha, zg;
            
            if (exposure == ExposureCategory.B_Urban)
            {
                alpha = 7.0; zg = 1200;
                return 2.01 * Math.Pow(height / zg, 2.0 / alpha);
            }
            if (exposure == ExposureCategory.C_Open)
            {
                alpha = 9.5; zg = 900;
                return 2.01 * Math.Pow(height / zg, 2.0 / alpha);
            }
            if (exposure == ExposureCategory.D_Coastal)
            {
                alpha = 11.5; zg = 700;
                return 2.01 * Math.Pow(height / zg, 2.0 / alpha);
            }
            
            return 0.85; // Default
        }
        
        // Design wind pressure - ASCE 7 Eq 27.3-1
        public static double GetDesignWindPressure(double qz, double Cp, double qi = 0)
        {
            double G = 0.85; // Gust effect factor (rigid buildings)
            double GCpi = 0.18; // Internal pressure coefficient (partially enclosed)
            
            return qz * G * Cp - qi * GCpi; // psf
        }
        
        // External pressure coefficients - ASCE 7 Fig 27.3-1
        public static double GetExternalPressureCoefficient(string surface)
        {
            return surface switch
            {
                "Windward_Wall" => 0.8,
                "Leeward_Wall" => -0.5,
                "Side_Wall" => -0.7,
                "Roof_0to10deg_Windward" => -0.9,
                "Roof_0to10deg_Leeward" => -0.5,
                "Roof_10to30deg_Windward" => -0.7,
                _ => 0.8
            };
        }
        
        // SEISMIC LOADS - CHAPTERS 11-23
        
        // Seismic Design Category - ASCE 7 Tables 11.6-1 and 11.6-2
        public static SeismicDesignCategory GetSeismicDesignCategory(double SDS, double SD1, RiskCategory risk)
        {
            // SDS = Design spectral response acceleration (short period)
            // SD1 = Design spectral response acceleration (1 second period)
            
            SeismicDesignCategory catFromSDS;
            SeismicDesignCategory catFromSD1;
            
            // Based on SDS
            if (SDS < 0.167) catFromSDS = SeismicDesignCategory.A;
            else if (SDS < 0.33) catFromSDS = SeismicDesignCategory.B;
            else if (SDS < 0.50) catFromSDS = SeismicDesignCategory.C;
            else if (SDS < 0.75) catFromSDS = SeismicDesignCategory.D;
            else if (SDS < 1.25) catFromSDS = SeismicDesignCategory.E;
            else catFromSDS = SeismicDesignCategory.F;
            
            // Based on SD1
            if (SD1 < 0.067) catFromSD1 = SeismicDesignCategory.A;
            else if (SD1 < 0.133) catFromSD1 = SeismicDesignCategory.B;
            else if (SD1 < 0.20) catFromSD1 = SeismicDesignCategory.C;
            else if (SD1 < 0.30) catFromSD1 = SeismicDesignCategory.D;
            else if (SD1 < 0.50) catFromSD1 = SeismicDesignCategory.E;
            else catFromSD1 = SeismicDesignCategory.F;
            
            // Return more severe
            return (SeismicDesignCategory)Math.Max((int)catFromSDS, (int)catFromSD1);
        }
        
        // Response modification factor (R) - ASCE 7 Table 12.2-1
        public static double GetResponseModificationFactor(string structuralSystem)
        {
            return structuralSystem switch
            {
                "Steel_Moment_Frame_Special" => 8.0,
                "Steel_Moment_Frame_Intermediate" => 4.5,
                "Steel_Moment_Frame_Ordinary" => 3.5,
                "Steel_Braced_Frame_Special" => 8.0,
                "Steel_Braced_Frame_Ordinary" => 3.25,
                "Concrete_Moment_Frame_Special" => 8.0,
                "Concrete_Moment_Frame_Intermediate" => 5.0,
                "Concrete_Shear_Wall_Special" => 5.0,
                "Concrete_Shear_Wall_Ordinary" => 4.0,
                "Masonry_Shear_Wall_Special" => 5.0,
                "Masonry_Shear_Wall_Ordinary" => 3.5,
                "Wood_Light_Frame" => 6.5,
                "Steel_Plate_Shear_Wall" => 7.0,
                _ => 3.0
            };
        }
        
        // Seismic base shear - ASCE 7 Eq 12.8-1
        public static double GetSeismicBaseShear(double effectiveWeight, double Cs)
        {
            return Cs * effectiveWeight;
        }
        
        // Seismic response coefficient - ASCE 7 Section 12.8.1
        public static double GetSeismicResponseCoefficient(double SDS, double SD1, double T, double R, double Ie = 1.0)
        {
            // Cs = SDS / (R/Ie)
            double Cs = (SDS * Ie) / R;
            
            // Need not exceed (SD1 × Ie) / (T × R/Ie)
            double CsMax = (SD1 * Ie) / (T * R);
            Cs = Math.Min(Cs, CsMax);
            
            // Minimum Cs
            double CsMin = Math.Max(0.044 * SDS * Ie, 0.01);
            
            // Additional minimum for long-period structures
            if (SD1 >= 0.6)
            {
                double CsMin2 = 0.5 * SD1 / (R / Ie);
                CsMin = Math.Max(CsMin, CsMin2);
            }
            
            return Math.Max(Cs, CsMin);
        }
        
        // Approximate fundamental period - ASCE 7 Eq 12.8-7
        public static double GetApproximatePeriod(double height, string structureType)
        {
            // Ta = Ct × h^x
            double Ct, x;
            
            if (structureType.Contains("Moment_Frame_Steel"))
            {
                Ct = 0.028; x = 0.8;
            }
            else if (structureType.Contains("Moment_Frame_Concrete"))
            {
                Ct = 0.016; x = 0.9;
            }
            else if (structureType.Contains("Braced"))
            {
                Ct = 0.020; x = 0.75;
            }
            else if (structureType.Contains("Shear_Wall"))
            {
                Ct = 0.020; x = 0.75;
            }
            else // Other
            {
                Ct = 0.020; x = 0.75;
            }
            
            return Ct * Math.Pow(height, x); // seconds
        }
        
        // Vertical distribution factor - ASCE 7 Eq 12.8-12
        public static double GetVerticalDistributionFactor(double wx, double hx, double k, double sumWiHik)
        {
            // Cvx = (wx × hx^k) / Σ(wi × hi^k)
            return (wx * Math.Pow(hx, k)) / sumWiHik;
        }
        
        // Exponent k - ASCE 7 Section 12.8.3
        public static double GetVerticalDistributionExponent(double period)
        {
            if (period <= 0.5) return 1.0;
            if (period >= 2.5) return 2.0;
            return 1.0 + (period - 0.5) / 2.0; // Linear interpolation
        }
        
        // Story drift limits - ASCE 7 Table 12.12-1
        public static double GetAllowableStoryDrift(double storyHeight, RiskCategory risk)
        {
            double driftRatio = risk switch
            {
                RiskCategory.I_Low => 0.025,        // 2.5%
                RiskCategory.II_Normal => 0.020,     // 2.0%
                RiskCategory.III_Substantial => 0.015, // 1.5%
                RiskCategory.IV_Essential => 0.010,  // 1.0%
                _ => 0.020
            };
            
            return driftRatio * storyHeight;
        }
        
        // SNOW LOADS - CHAPTER 7
        
        // Flat roof snow load - ASCE 7 Eq 7.3-1
        public static double GetFlatRoofSnowLoad(double pg, double Ce, double Ct, double Is)
        {
            // pf = 0.7 × Ce × Ct × Is × pg
            return 0.7 * Ce * Ct * Is * pg; // psf
        }
        
        // Sloped roof snow load - ASCE 7 Section 7.4
        public static double GetSlopedRoofSnowLoad(double pf, double roofSlope)
        {
            // ps = Cs × pf
            double Cs;
            if (roofSlope <= 30) Cs = 1.0;
            else if (roofSlope >= 70) Cs = 0.0;
            else Cs = (70 - roofSlope) / 40.0;
            
            return Cs * pf;
        }
    }
}
