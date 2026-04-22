using System;
using System.Collections.Generic;

namespace StingBIM.Standards.EurocodesComplete
{
    /// <summary>
    /// Complete Eurocode Suite - EN 1990 through EN 1999
    /// Structural design standards for Europe and East Africa
    /// 
    /// This expands the base Eurocodes (EC2, EC3) with:
    /// - EN 1990: Basis of structural design
    /// - EN 1991: Actions on structures  
    /// - EN 1994: Composite steel/concrete
    /// - EN 1995: Timber structures
    /// - EN 1996: Masonry structures
    /// - EN 1997: Geotechnical design
    /// - EN 1998: Earthquake resistance
    /// - EN 1999: Aluminium structures
    /// </summary>
    public static class EurocodesCompleteStandards
    {
        #region EN 1990 - Basis of Structural Design

        /// <summary>
        /// Reliability classes per EN 1990
        /// </summary>
        public enum ReliabilityClass
        {
            /// <summary>RC1 - Low consequence of failure (agricultural buildings)</summary>
            RC1_Low,
            /// <summary>RC2 - Medium consequence (residential, office)</summary>
            RC2_Medium,
            /// <summary>RC3 - High consequence (public assembly, high-rise)</summary>
            RC3_High
        }

        /// <summary>
        /// Partial safety factors for actions (loads)
        /// </summary>
        public static class PartialFactorsActions
        {
            /// <summary>Permanent actions - unfavorable</summary>
            public const double GammaG_Unfavorable = 1.35;

            /// <summary>Permanent actions - favorable</summary>
            public const double GammaG_Favorable = 1.0;

            /// <summary>Variable actions - unfavorable</summary>
            public const double GammaQ_Unfavorable = 1.5;

            /// <summary>Variable actions - favorable</summary>
            public const double GammaQ_Favorable = 0.0;
        }

        /// <summary>
        /// Gets combination factor psi for variable actions
        /// </summary>
        public static (double Psi0, double Psi1, double Psi2) GetCombinationFactors(
            string actionType)
        {
            return actionType.ToLower() switch
            {
                "imposed loads - category a" => (0.7, 0.5, 0.3),  // Domestic/residential
                "imposed loads - category b" => (0.7, 0.5, 0.3),  // Offices
                "imposed loads - category c" => (0.7, 0.7, 0.6),  // Congregation areas
                "imposed loads - category d" => (0.7, 0.7, 0.6),  // Shopping areas
                "snow loads" => (0.5, 0.2, 0.0),
                "wind loads" => (0.6, 0.2, 0.0),
                "temperature" => (0.6, 0.5, 0.0),
                _ => (0.7, 0.5, 0.3)
            };
        }

        /// <summary>
        /// Design working life categories
        /// </summary>
        public static int GetDesignWorkingLife(string category)
        {
            return category.ToLower() switch
            {
                "category 1" => 10,   // Temporary structures
                "category 2" => 25,   // Replaceable parts
                "category 3" => 25,   // Agricultural buildings
                "category 4" => 50,   // Building structures, bridges
                "category 5" => 100,  // Monumental buildings, major bridges
                _ => 50
            };
        }

        #endregion

        #region EN 1991 - Actions on Structures

        /// <summary>
        /// Imposed floor loads per EN 1991-1-1
        /// </summary>
        public static (double UniformLoad, double PointLoad) GetImposedFloorLoads(
            string occupancyCategory)
        {
            return occupancyCategory.ToUpper() switch
            {
                "A" => (1.5, 2.0),      // Category A - Domestic/residential (kN/m², kN)
                "B" => (2.0, 2.0),      // Category B - Offices
                "C1" => (2.5, 3.0),     // Category C1 - Areas with tables (schools)
                "C2" => (3.0, 4.0),     // Category C2 - Areas with fixed seats
                "C3" => (4.0, 4.0),     // Category C3 - Areas without obstacles
                "C4" => (4.5, 4.0),     // Category C4 - Areas for physical activities
                "C5" => (5.0, 3.5),     // Category C5 - Areas for large crowds
                "D1" => (4.0, 4.0),     // Category D1 - Shopping areas
                "D2" => (4.0, 4.0),     // Category D2 - Department stores
                "E1" => (5.0, 2.0),     // Category E1 - Storage areas (general)
                "E2" => (7.5, 7.0),     // Category E2 - Industrial use
                _ => (2.0, 2.0)
            };
        }

        /// <summary>
        /// Snow loads per EN 1991-1-3
        /// </summary>
        public static double GetGroundSnowLoad(double altitude, string snowZone)
        {
            // sk = characteristic ground snow load
            // Simplified - actual calculation more complex
            double baseLoad = snowZone.ToUpper() switch
            {
                "ZONE 1" => 0.4,  // Low altitude areas
                "ZONE 2" => 0.6,
                "ZONE 3" => 0.8,
                "ZONE 4" => 1.0,
                "ZONE 5" => 1.5,  // High altitude/northern areas
                _ => 0.5
            };

            // Increase with altitude (simplified)
            if (altitude > 500)
                baseLoad += (altitude - 500) / 1000.0;

            return baseLoad;
        }

        /// <summary>
        /// Roof snow shape coefficient
        /// </summary>
        public static double GetRoofSnowShapeCoefficient(double roofPitchDegrees)
        {
            if (roofPitchDegrees <= 30)
                return 0.8;
            else if (roofPitchDegrees >= 60)
                return 0.0; // No snow retention
            else
                return 0.8 * (60 - roofPitchDegrees) / 30.0; // Linear interpolation
        }

        /// <summary>
        /// Basic wind velocity per EN 1991-1-4
        /// </summary>
        public static double GetBasicWindVelocity(string windZone)
        {
            // vb,0 in m/s - varies by country
            // Example values for reference
            return windZone.ToUpper() switch
            {
                "ZONE I" => 22,    // Low wind
                "ZONE II" => 24,
                "ZONE III" => 26,
                "ZONE IV" => 28,   // High wind
                _ => 24
            };
        }

        /// <summary>
        /// Terrain roughness categories
        /// </summary>
        public enum TerrainCategory
        {
            /// <summary>0 - Sea, coastal areas</summary>
            Category0_Sea,
            /// <summary>I - Lakes or flat terrain</summary>
            CategoryI_Flat,
            /// <summary>II - Low vegetation, scattered obstacles</summary>
            CategoryII_LowVeg,
            /// <summary>III - Regular vegetation, villages</summary>
            CategoryIII_RegularCover,
            /// <summary>IV - Dense urban, forests</summary>
            CategoryIV_Dense
        }

        /// <summary>
        /// Gets terrain roughness factor
        /// </summary>
        public static double GetTerrainFactor(TerrainCategory category, double height)
        {
            double z0 = category switch // Roughness length
            {
                TerrainCategory.Category0_Sea => 0.003,
                TerrainCategory.CategoryI_Flat => 0.01,
                TerrainCategory.CategoryII_LowVeg => 0.05,
                TerrainCategory.CategoryIII_RegularCover => 0.3,
                TerrainCategory.CategoryIV_Dense => 1.0,
                _ => 0.05
            };

            double zmin = category switch // Minimum height
            {
                TerrainCategory.Category0_Sea => 1,
                TerrainCategory.CategoryI_Flat => 1,
                TerrainCategory.CategoryII_LowVeg => 2,
                TerrainCategory.CategoryIII_RegularCover => 5,
                TerrainCategory.CategoryIV_Dense => 10,
                _ => 2
            };

            // Simplified terrain factor calculation
            if (height < zmin)
                height = zmin;

            double kr = 0.19 * Math.Pow(z0 / 0.05, 0.07);
            return kr * Math.Log(height / z0);
        }

        #endregion

        #region EN 1994 - Composite Steel and Concrete Structures

        /// <summary>
        /// Shear connector types for composite beams
        /// </summary>
        public enum ShearConnectorType
        {
            /// <summary>Headed studs (most common)</summary>
            HeadedStud,
            /// <summary>High strength friction grip bolts</summary>
            HSFG_Bolt,
            /// <summary>Hoops or angles</summary>
            Hoop,
            /// <summary>Block connectors</summary>
            Block
        }

        /// <summary>
        /// Gets design shear resistance of headed stud (kN)
        /// </summary>
        public static double GetStudShearResistance(
            double studDiameter,       // mm
            double studHeight,         // mm
            double concreteFck,        // MPa
            double steelFu)            // MPa
        {
            // Simplified calculation per EN 1994
            double alpha = studHeight / studDiameter >= 4 ? 1.0 : 0.2 * (studHeight / studDiameter + 1);
            
            double Astudd = Math.PI * studDiameter * studDiameter / 4.0;
            
            double PRd1 = 0.8 * steelFu * Astudd / 1000; // Stud failure
            double PRd2 = 0.29 * alpha * Math.Pow(studDiameter, 2) * Math.Sqrt(concreteFck * 30) / 1000; // Concrete failure
            
            return Math.Min(PRd1, PRd2);
        }

        /// <summary>
        /// Minimum degree of shear connection
        /// </summary>
        public static double GetMinimumShearConnection(double spanLength)
        {
            // η (eta) - degree of shear connection
            if (spanLength <= 25000) // mm
                return 1.0 - (355.0 / 510.0) * (0.75 - 0.03 * spanLength / 1000.0);
            else
                return 0.4;
        }

        /// <summary>
        /// Effective width of concrete flange
        /// </summary>
        public static double GetEffectiveWidth(
            double beamSpacing,
            double spanLength,
            double slabThickness)
        {
            // Simplified - take minimum of:
            double Le = 0.85 * spanLength; // Equivalent span for simply supported
            
            double beff1 = Le / 8.0;
            double beff2 = beamSpacing / 2.0;
            
            return Math.Min(beff1, beff2) * 2 + 200; // +200mm for web
        }

        #endregion

        #region EN 1995 - Design of Timber Structures

        /// <summary>
        /// Service classes for timber structures
        /// </summary>
        public enum TimberServiceClass
        {
            /// <summary>Class 1 - Interior, generally dry (MC ≈ 12%)</summary>
            Class1_Dry,
            /// <summary>Class 2 - Covered, occasional wetting (MC ≈ 20%)</summary>
            Class2_Humid,
            /// <summary>Class 3 - Exposed to weather (MC > 20%)</summary>
            Class3_Exterior
        }

        /// <summary>
        /// Load duration classes
        /// </summary>
        public enum LoadDurationClass
        {
            /// <summary>Permanent > 10 years</summary>
            Permanent,
            /// <summary>Long-term 6 months - 10 years</summary>
            LongTerm,
            /// <summary>Medium-term 1 week - 6 months</summary>
            MediumTerm,
            /// <summary>Short-term < 1 week</summary>
            ShortTerm,
            /// <summary>Instantaneous</summary>
            Instantaneous
        }

        /// <summary>
        /// Gets modification factor kmod
        /// </summary>
        public static double GetModificationFactor(
            TimberServiceClass serviceClass,
            LoadDurationClass loadDuration)
        {
            if (serviceClass == TimberServiceClass.Class1_Dry)
            {
                return loadDuration switch
                {
                    LoadDurationClass.Permanent => 0.60,
                    LoadDurationClass.LongTerm => 0.70,
                    LoadDurationClass.MediumTerm => 0.80,
                    LoadDurationClass.ShortTerm => 0.90,
                    LoadDurationClass.Instantaneous => 1.10,
                    _ => 0.60
                };
            }
            else if (serviceClass == TimberServiceClass.Class2_Humid)
            {
                return loadDuration switch
                {
                    LoadDurationClass.Permanent => 0.60,
                    LoadDurationClass.LongTerm => 0.70,
                    LoadDurationClass.MediumTerm => 0.80,
                    LoadDurationClass.ShortTerm => 0.90,
                    LoadDurationClass.Instantaneous => 1.10,
                    _ => 0.60
                };
            }
            else // Class 3
            {
                return loadDuration switch
                {
                    LoadDurationClass.Permanent => 0.50,
                    LoadDurationClass.LongTerm => 0.55,
                    LoadDurationClass.MediumTerm => 0.65,
                    LoadDurationClass.ShortTerm => 0.70,
                    LoadDurationClass.Instantaneous => 0.90,
                    _ => 0.50
                };
            }
        }

        /// <summary>
        /// Partial factor for material properties
        /// </summary>
        public const double GammaM = 1.3; // For solid timber

        /// <summary>
        /// Deformation factor for service class
        /// </summary>
        public static double GetDeformationFactor(TimberServiceClass serviceClass)
        {
            return serviceClass switch
            {
                TimberServiceClass.Class1_Dry => 0.6,
                TimberServiceClass.Class2_Humid => 0.8,
                TimberServiceClass.Class3_Exterior => 2.0,
                _ => 0.8
            };
        }

        #endregion

        #region EN 1996 - Design of Masonry Structures

        /// <summary>
        /// Masonry unit types
        /// </summary>
        public enum MasonryUnitType
        {
            /// <summary>Clay units</summary>
            Clay,
            /// <summary>Calcium silicate units</summary>
            CalciumSilicate,
            /// <summary>Aggregate concrete units</summary>
            AggregateConcreteAutoclaved,
            /// <summary>Manufactured stone</summary>
            ManufacturedStone
        }

        /// <summary>
        /// Mortar types per EN 1996
        /// </summary>
        public enum MortarType
        {
            /// <summary>General purpose mortar</summary>
            GeneralPurpose_GP,
            /// <summary>Lightweight mortar</summary>
            Lightweight_LW,
            /// <summary>Thin layer mortar</summary>
            ThinLayer_TL
        }

        /// <summary>
        /// Gets characteristic compressive strength of masonry (fk in MPa)
        /// </summary>
        public static double GetMasonryCompressiveStrength(
            double unitStrength,        // fb in MPa
            double mortarStrength,      // fm in MPa
            MasonryUnitType unitType)
        {
            // Simplified formula: fk = K * fb^0.7 * fm^0.3
            double K = unitType switch
            {
                MasonryUnitType.Clay => 0.45,
                MasonryUnitType.CalciumSilicate => 0.45,
                MasonryUnitType.AggregateConcreteAutoclaved => 0.50,
                MasonryUnitType.ManufacturedStone => 0.45,
                _ => 0.45
            };

            return K * Math.Pow(unitStrength, 0.7) * Math.Pow(mortarStrength, 0.3);
        }

        /// <summary>
        /// Partial factor for masonry
        /// </summary>
        public const double GammaM_Masonry = 2.5;

        /// <summary>
        /// Gets slenderness reduction factor
        /// </summary>
        public static double GetSlendernessReductionFactor(
            double effectiveHeight,
            double effectiveThickness)
        {
            double slenderness = effectiveHeight / effectiveThickness;
            
            if (slenderness <= 12)
                return 1.0;
            else if (slenderness >= 27)
                return 0.0; // Too slender
            else
                return 1.0 - Math.Pow((slenderness - 12) / 15.0, 2);
        }

        #endregion

        #region EN 1997 - Geotechnical Design

        /// <summary>
        /// Geotechnical categories
        /// </summary>
        public enum GeotechnicalCategory
        {
            /// <summary>GC1 - Small and simple structures</summary>
            GC1_Simple,
            /// <summary>GC2 - Conventional structures</summary>
            GC2_Conventional,
            /// <summary>GC3 - Very large or unusual structures</summary>
            GC3_Complex
        }

        /// <summary>
        /// Limit state design approaches
        /// </summary>
        public enum DesignApproach
        {
            /// <summary>DA1 - Combination 1 and 2</summary>
            DA1,
            /// <summary>DA2 - Single set of factors</summary>
            DA2,
            /// <summary>DA3 - Structural actions separate</summary>
            DA3
        }

        /// <summary>
        /// Gets partial factors for Design Approach 1
        /// </summary>
        public static (double GammaG, double GammaQ, double GammaR) GetPartialFactors_DA1(
            string combination)
        {
            if (combination == "C1")
                return (1.35, 1.5, 1.0);  // Combination 1
            else // C2
                return (1.0, 1.3, 1.4);   // Combination 2
        }

        /// <summary>
        /// Bearing capacity factors for shallow foundations
        /// </summary>
        public static (double Nc, double Nq, double Ngamma) GetBearingCapacityFactors(
            double phi) // Effective angle of friction (degrees)
        {
            double phiRad = phi * Math.PI / 180.0;
            
            double Nq = Math.Exp(Math.PI * Math.Tan(phiRad)) * 
                       Math.Pow(Math.Tan(Math.PI / 4.0 + phiRad / 2.0), 2);
            
            double Nc = (Nq - 1) / Math.Tan(phiRad);
            
            double Ngamma = 2 * (Nq - 1) * Math.Tan(phiRad);
            
            return (Nc, Nq, Ngamma);
        }

        /// <summary>
        /// Ultimate bearing capacity of shallow foundation (kPa)
        /// </summary>
        public static double CalculateBearingCapacity(
            double cohesion,            // c' in kPa
            double phi,                 // φ' in degrees
            double soilDensity,         // γ in kN/m³
            double foundationDepth,     // Df in m
            double foundationWidth,     // B in m
            double overburdenPressure)  // q in kPa
        {
            var (Nc, Nq, Ngamma) = GetBearingCapacityFactors(phi);
            
            double qult = cohesion * Nc + 
                         overburdenPressure * Nq + 
                         0.5 * soilDensity * foundationWidth * Ngamma;
            
            return qult;
        }

        /// <summary>
        /// Pile capacity calculation (simplified)
        /// </summary>
        public static (double BaseResistance, double ShaftResistance) CalculatePileCapacity(
            double pileDiameter,        // m
            double pileLength,          // m
            double baseBearingCapacity, // kPa
            double averageShaftFriction) // kPa
        {
            double baseArea = Math.PI * pileDiameter * pileDiameter / 4.0;
            double shaftArea = Math.PI * pileDiameter * pileLength;
            
            double qb = baseBearingCapacity * baseArea;
            double qs = averageShaftFriction * shaftArea;
            
            return (qb, qs);
        }

        #endregion

        #region EN 1998 - Design of Structures for Earthquake Resistance

        /// <summary>
        /// Seismic zones (example - actual zones country-specific)
        /// </summary>
        public enum SeismicZone
        {
            /// <summary>Very low seismicity (agR < 0.04g)</summary>
            VeryLow,
            /// <summary>Low seismicity (agR < 0.08g)</summary>
            Low,
            /// <summary>Moderate seismicity (agR < 0.16g)</summary>
            Moderate,
            /// <summary>High seismicity (agR ≥ 0.16g)</summary>
            High
        }

        /// <summary>
        /// Importance classes for buildings
        /// </summary>
        public enum ImportanceClass
        {
            /// <summary>I - Minor importance</summary>
            Class_I,
            /// <summary>II - Normal importance</summary>
            Class_II,
            /// <summary>III - Important</summary>
            Class_III,
            /// <summary>IV - Critical/essential</summary>
            Class_IV
        }

        /// <summary>
        /// Gets importance factor
        /// </summary>
        public static double GetImportanceFactor(ImportanceClass importanceClass)
        {
            return importanceClass switch
            {
                ImportanceClass.Class_I => 0.8,
                ImportanceClass.Class_II => 1.0,
                ImportanceClass.Class_III => 1.2,
                ImportanceClass.Class_IV => 1.4,
                _ => 1.0
            };
        }

        /// <summary>
        /// Ground types for seismic design
        /// </summary>
        public enum GroundType
        {
            /// <summary>A - Rock</summary>
            Type_A_Rock,
            /// <summary>B - Dense sand/gravel</summary>
            Type_B_Dense,
            /// <summary>C - Medium dense sand</summary>
            Type_C_Medium,
            /// <summary>D - Loose sand</summary>
            Type_D_Loose,
            /// <summary>E - Soft clay</summary>
            Type_E_Soft
        }

        /// <summary>
        /// Gets behavior factor q for ductility
        /// </summary>
        public static double GetBehaviorFactor(
            string structuralSystem,
            string ductilityClass)
        {
            bool highDuctility = ductilityClass.ToUpper() == "DCH";
            
            return structuralSystem.ToLower() switch
            {
                "moment resisting frame" => highDuctility ? 5.0 : 3.0,
                "dual system" => highDuctility ? 4.5 : 3.0,
                "shear wall" => highDuctility ? 4.0 : 3.0,
                "frame with infill" => 2.0,
                _ => 1.5
            };
        }

        /// <summary>
        /// Design spectral acceleration
        /// </summary>
        public static double GetDesignSpectralAcceleration(
            double period,              // T in seconds
            double agR,                 // Reference peak ground acceleration (g)
            GroundType groundType,
            ImportanceClass importance)
        {
            double gammaI = GetImportanceFactor(importance);
            double ag = agR * gammaI;
            
            // Simplified spectrum parameters
            double S = groundType switch
            {
                GroundType.Type_A_Rock => 1.0,
                GroundType.Type_B_Dense => 1.2,
                GroundType.Type_C_Medium => 1.15,
                GroundType.Type_D_Loose => 1.35,
                GroundType.Type_E_Soft => 1.4,
                _ => 1.0
            };
            
            // Simplified for illustration
            double TB = 0.15;
            double TC = 0.5;
            double TD = 2.0;
            
            if (period <= TB)
                return ag * S * (1 + period / TB * (2.5 - 1));
            else if (period <= TC)
                return ag * S * 2.5;
            else if (period <= TD)
                return ag * S * 2.5 * (TC / period);
            else
                return ag * S * 2.5 * (TC * TD / (period * period));
        }

        #endregion

        #region EN 1999 - Design of Aluminium Structures

        /// <summary>
        /// Aluminium alloy types
        /// </summary>
        public enum AluminiumAlloy
        {
            /// <summary>3000 series - Non-heat treatable</summary>
            Series3000,
            /// <summary>5000 series - Non-heat treatable</summary>
            Series5000,
            /// <summary>6000 series - Heat treatable</summary>
            Series6000,
            /// <summary>7000 series - Heat treatable</summary>
            Series7000
        }

        /// <summary>
        /// Temper designations
        /// </summary>
        public enum TemperType
        {
            /// <summary>O - Annealed</summary>
            O_Annealed,
            /// <summary>H - Strain hardened</summary>
            H_StrainHardened,
            /// <summary>T4 - Naturally aged</summary>
            T4_NaturallyAged,
            /// <summary>T6 - Artificially aged</summary>
            T6_ArtificiallyAged
        }

        /// <summary>
        /// Gets 0.2% proof strength (N/mm²)
        /// </summary>
        public static double GetProofStrength(AluminiumAlloy alloy, TemperType temper)
        {
            if (alloy == AluminiumAlloy.Series6000)
            {
                return temper switch
                {
                    TemperType.O_Annealed => 55,
                    TemperType.T4_NaturallyAged => 110,
                    TemperType.T6_ArtificiallyAged => 215,
                    _ => 110
                };
            }
            else if (alloy == AluminiumAlloy.Series5000)
            {
                return temper switch
                {
                    TemperType.O_Annealed => 40,
                    TemperType.H_StrainHardened => 125,
                    _ => 70
                };
            }
            else
            {
                return 100; // Generic value
            }
        }

        /// <summary>
        /// Partial factor for aluminium
        /// </summary>
        public const double GammaM_Aluminium = 1.1;

        /// <summary>
        /// Modulus of elasticity for aluminium
        /// </summary>
        public const double E_Aluminium = 70000; // N/mm²

        #endregion
    }
}
